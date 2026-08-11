#!/usr/bin/env python3
"""Drive the travel-planning Agent Skill end to end and prove the three-tier runtime fires.

What this proves
----------------
The skill runtime has three tiers: the catalog (tier 1, a name + description injected on every
run), `load_skill` (tier 2, fetches the SKILL.md body), and `render_skill_surface` (tier 3, draws
one of the skill's bundled A2UI templates). A skill body is never persisted into the conversation
history, so on the turn *after* a surface submission the model holds the catalog and nothing else.
The observed regression was exactly that: on turn 3 the model continued the flow from memory of an
earlier turn instead of reloading the skill, then narrated raw data-model paths at the user rather
than rendering `trip-confirm`. `SkillPrompt.BuildCatalog` now carries the paragraph that has to win
("A loaded skill lasts only for the current reply"), and this script is the regression test for it.

It runs three real turns against a running orchestrator, as the browser would:

  turn 1  a plain user message  ..............  expects a `trip-search`  surface
  turn 2  a `trip_search_submit` submission  ..  expects a `trip-results` surface
  turn 3  a `trip_option_selected` submission .  expects a `trip-confirm` surface

and asserts, per turn: no RUN_ERROR; `load_skill` fired; `render_skill_surface` fired for the
expected surface; the render's tool result is `{"a2ui_operations": [...]}` with exactly the three
operations `createSurface` / `updateComponents` / `updateDataModel`. Turn 1 additionally asserts
the destination the user typed ("Da Nang") reached the surface's data model, which is the Phase 0
data-binding fix proved end to end rather than in a unit test.

Turns 2 and 3 are not typed text. They reproduce byte for byte what `@ag-ui/a2ui-middleware`'s
`processUserAction` posts when the user clicks a button on a rendered surface: an assistant message
carrying a `log_a2ui_event` tool call whose arguments are the A2UI `userAction`, followed by a tool
message holding the formatted result. `AgUiController.ExtractPrompt` turns that pair into the
synthetic acknowledgement prompt the model actually sees. The submitted form state is built from
the *previous* turn's rendered data model, the same way the live surface does it, so a turn only
makes sense if the turn before it really rendered.

On `load_skill` and the AG-UI stream
------------------------------------
`load_skill` is a plain server-side `AIFunction`. `AgentService` forwards a tool call to the stream
only when it is a frontend (browser-executed) tool, or when it was captured for rendering by
`RenderableToolCapture` — `render_skill_surface` is the latter, `load_skill` is neither. So a
`load_skill` call is invisible in the SSE stream by construction. This script looks for it there
first (it is where it would appear if that ever changes), then falls back to the orchestrator's
own stdout, where `SkillTools.CreateLoadSkill` logs "Agent <id> loaded skill '<name>'". Under
Aspire that stdout is a file in /tmp/aspire-dcp*/; only bytes appended during the turn are read, so
the evidence is scoped to that turn without trusting the wall clock.

Usage
-----
  python3 scripts/check-skill-flow.py
  python3 scripts/check-skill-flow.py --base-url https://localhost:7093 --agent <guid>
  python3 scripts/check-skill-flow.py --email me@example.com --password '...'

Exits 0 when every turn passes, 1 otherwise. The app must be running (start it with ./ntg → run);
the initial connection is retried for up to 90 seconds while it finishes starting.
"""

from __future__ import annotations

import argparse
import copy
import glob
import http.cookiejar
import json
import os
import re
import ssl
import sys
import time
import urllib.error
import urllib.request
import uuid

DEFAULT_BASE_URL = "https://localhost:7093"
DEFAULT_AGENT = "31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"  # AgentFactory.DefaultAgentId
DEFAULT_EMAIL = "admin@ntgagent.com"
DEFAULT_LOG_GLOB = "/tmp/aspire-dcp*/*_out"

SKILL = "travel-planning"
LOAD_TOOL = "load_skill"
RENDER_TOOL = "render_skill_surface"

DESTINATION = "Da Nang"
OPENING_PROMPT = f"I want a trip to {DESTINATION}, can you plan it for me"

# The trip the simulated user fills into trip-search, and the option they pick on trip-results.
TRIP = {
    "destination": DESTINATION,
    "departDate": "2026-09-12",
    "returnDate": "2026-09-19",
    "travellers": 2,
    "style": ["balanced"],
}
SURFACE_ID = "trip-wizard"
CHOICE = "o2"

# The A2UI tool @ag-ui/a2ui-middleware injects into every browser run (its RENDER_A2UI_TOOL).
# Declared here so the model sees the same tool set it sees in production — including the
# render_a2ui escape hatch the skill catalog tells it not to use when a skill applies.
RENDER_A2UI_TOOL = {
    "name": "render_a2ui",
    "description": (
        "Render a dynamic A2UI v0.9 surface with structured parameters. "
        "Follow the A2UI render tool usage guide provided in context."
    ),
    "parameters": {
        "type": "object",
        "properties": {
            "surfaceId": {"type": "string", "description": "Unique surface identifier."},
            "components": {
                "type": "array",
                "description": 'A2UI v0.9 component array (flat format). The root component must have id "root".',
                "items": {"type": "object"},
            },
            "data": {"type": "object", "description": "Initial data model for the surface."},
        },
        "required": ["surfaceId", "components"],
    },
}

CONNECT_RETRY_SECONDS = 90
RUN_TIMEOUT_SECONDS = 240


# ---------------------------------------------------------------- transport


class Orchestrator:
    """Authenticated client for the orchestrator's AG-UI endpoint."""

    def __init__(self, base_url: str, agent_id: str):
        self.base_url = base_url.rstrip("/")
        self.agent_id = agent_id
        self.jar = http.cookiejar.CookieJar()
        # Aspire serves https with the ASP.NET Core self-signed dev certificate, which no store
        # trusts. Verification is off for the whole client; this only ever talks to localhost.
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(self.jar),
            urllib.request.HTTPSHandler(context=ssl._create_unverified_context()),
        )

    def login(self, email: str, password: str) -> str:
        """Signs in, retrying while the app starts. Returns the account's user name."""
        body = json.dumps({"email": email, "password": password, "rememberMe": True}).encode()
        deadline = time.monotonic() + CONNECT_RETRY_SECONDS
        attempt = 0

        while True:
            attempt += 1
            request = urllib.request.Request(
                f"{self.base_url}/api/account/login",
                data=body,
                headers={"Content-Type": "application/json"},
                method="POST",
            )
            try:
                with self.opener.open(request, timeout=30) as response:
                    payload = json.loads(response.read().decode("utf-8", "replace") or "{}")
                break
            except urllib.error.HTTPError as exc:
                # A 401 is a wrong credential, not a cold start — retrying cannot fix it.
                detail = exc.read().decode("utf-8", "replace")[:200]
                raise RuntimeError(f"login rejected with HTTP {exc.code}: {detail}") from exc
            except (urllib.error.URLError, TimeoutError, ConnectionError) as exc:
                if time.monotonic() >= deadline:
                    raise RuntimeError(
                        f"could not reach {self.base_url} after {CONNECT_RETRY_SECONDS}s ({exc})"
                    ) from exc
                if attempt == 1:
                    print(f"  ....  waiting for {self.base_url} to accept connections", flush=True)
                time.sleep(3)

        if not any(c.name == ".AspNetCore.Identity.Application" for c in self.jar):
            raise RuntimeError("login succeeded but no identity cookie was set")

        return payload.get("userName") or payload.get("email") or "?"

    def run(self, thread_id: str, messages: list[dict], tools: list[dict] | None) -> list[dict]:
        """POSTs one AG-UI run and returns every parsed SSE event."""
        payload: dict = {
            "threadId": thread_id,
            "runId": str(uuid.uuid4()),
            "messages": messages,
        }
        if tools:
            payload["tools"] = tools

        request = urllib.request.Request(
            f"{self.base_url}/api/agui/{self.agent_id}",
            data=json.dumps(payload).encode(),
            headers={"Content-Type": "application/json", "Accept": "text/event-stream"},
            method="POST",
        )

        events: list[dict] = []
        with self.opener.open(request, timeout=RUN_TIMEOUT_SECONDS) as response:
            for data in iter_sse(response):
                try:
                    events.append(json.loads(data))
                except json.JSONDecodeError:
                    continue  # A frame we cannot read is not a frame we can assert on.
        return events


def iter_sse(response):
    """Yields the `data:` payload of each SSE frame.

    Frames are terminated by a blank line and a frame may carry several `data:` lines, which the
    spec says to join with a newline. readline() reassembles lines split across TCP chunks, so a
    100 KB tool result arriving in fragments still comes out whole.
    """
    fields: list[str] = []
    while True:
        raw = response.readline()
        if not raw:
            break
        line = raw.decode("utf-8", "replace").rstrip("\r\n")
        if line == "":
            if fields:
                yield "\n".join(fields)
                fields = []
        elif line.startswith("data:"):
            fields.append(line[5:].lstrip())
        # Other SSE fields (event:, id:, retry:, comments) are not used by AgUiController.
    if fields:
        yield "\n".join(fields)


# ---------------------------------------------------------------- stream model


class ToolCall:
    def __init__(self, call_id: str, name: str):
        self.call_id = call_id
        self.name = name
        self.args = ""
        self.result: str | None = None

    def parsed_args(self) -> dict:
        try:
            value = json.loads(self.args or "{}")
        except json.JSONDecodeError:
            return {}
        return value if isinstance(value, dict) else {}


class Stream:
    """The parts of one run's event stream this script asserts on."""

    def __init__(self, events: list[dict]):
        self.events = events
        self.calls: list[ToolCall] = []
        self.errors: list[str] = []
        self.text = ""

        by_id: dict[str, ToolCall] = {}
        for event in events:
            kind = event.get("type")
            if kind == "TOOL_CALL_START":
                call = ToolCall(event.get("toolCallId") or "", event.get("toolCallName") or "")
                by_id[call.call_id] = call
                self.calls.append(call)
            elif kind == "TOOL_CALL_ARGS":
                # Providers chunk argument JSON across deltas; reassemble by toolCallId.
                call = by_id.get(event.get("toolCallId") or "")
                if call is not None:
                    call.args += event.get("delta") or ""
            elif kind == "TOOL_CALL_RESULT":
                call = by_id.get(event.get("toolCallId") or "")
                if call is not None:
                    call.result = (call.result or "") + (event.get("content") or "")
            elif kind == "RUN_ERROR":
                self.errors.append(event.get("message") or "RUN_ERROR")
            elif kind == "TEXT_MESSAGE_CONTENT":
                self.text += event.get("delta") or ""

    def named(self, name: str) -> list[ToolCall]:
        return [c for c in self.calls if c.name == name]

    def tool_names(self) -> list[str]:
        return [c.name for c in self.calls]


def rendered_data_model(call: ToolCall) -> dict | None:
    """
    The data model a render_skill_surface result installed, or None.

    The renderer emits one updateDataModel per TOP-LEVEL branch rather than a single write to
    "/", so that a re-render leaves /__inputs — the user's own typed answers — untouched. Reading
    only the first write would therefore return whichever branch happened to come first.
    """
    model: dict = {}
    found = False

    for operation in operations_of(call):
        update = operation.get("updateDataModel")
        if not isinstance(update, dict):
            continue
        path = update.get("path")
        if not isinstance(path, str) or path == "/":
            continue
        model[path.strip("/")] = update.get("value")
        found = True

    return model if found else None


def operations_of(call: ToolCall) -> list[dict]:
    """The a2ui_operations array inside a render tool result, or []."""
    try:
        payload = json.loads(call.result or "")
    except (json.JSONDecodeError, TypeError):
        return []
    if isinstance(payload, str):  # some transports re-encode the result as a JSON string
        try:
            payload = json.loads(payload)
        except json.JSONDecodeError:
            return []
    if not isinstance(payload, dict):
        return []
    operations = payload.get("a2ui_operations")
    if not isinstance(operations, list):
        return []
    return [op for op in operations if isinstance(op, dict)]


# ---------------------------------------------------------------- orchestrator log


class OrchestratorLog:
    """Reads only what the orchestrator printed during a turn.

    Aspire's DCP writes each resource's stdout to /tmp/aspire-dcp*/<id>_out. Sizes are snapshotted
    before a turn and only the bytes appended after are searched, so evidence cannot leak in from
    an earlier run and no timestamp has to be parsed (this machine's WSL clock drifts).
    """

    def __init__(self, pattern: str):
        self.pattern = pattern
        self.offsets: dict[str, int] = {}
        self.available = bool(glob.glob(pattern))

    def mark(self) -> None:
        self.offsets = {}
        for path in glob.glob(self.pattern):
            try:
                self.offsets[path] = os.path.getsize(path)
            except OSError:
                continue

    def appended(self) -> str:
        chunks: list[str] = []
        for path in glob.glob(self.pattern):
            start = self.offsets.get(path, 0)
            try:
                with open(path, "rb") as handle:
                    handle.seek(start)
                    chunks.append(handle.read().decode("utf-8", "replace"))
            except OSError:
                continue
        return "\n".join(chunks)

    def loaded_skill(self, agent_id: str, skill: str, settle: float = 2.0) -> bool:
        """True when the run logged a load_skill for this agent and skill."""
        pattern = re.compile(
            rf"Agent {re.escape(agent_id)} loaded skill '{re.escape(skill)}'", re.IGNORECASE)
        deadline = time.monotonic() + settle
        while True:
            if pattern.search(self.appended()):
                return True
            if time.monotonic() >= deadline:
                return False
            time.sleep(0.5)  # stdout is line-buffered through DCP; give it a moment to land.


# ---------------------------------------------------------------- turn construction


def user_message(content: str) -> dict:
    return {"id": str(uuid.uuid4()), "role": "user", "content": content}


def submission_messages(action: dict) -> list[dict]:
    """The assistant + tool pair @ag-ui/a2ui-middleware appends for a surface submission.

    Mirrors processUserAction/formatUserActionResult in @ag-ui/a2ui-middleware: the assistant
    message carries a `log_a2ui_event` tool call whose arguments are the userAction verbatim, and
    the tool message holds the sentence the model actually reads. AgUiController.ExtractPrompt
    finds the tool name through the matching toolCallId, so the two ids must agree.
    """
    call_id = str(uuid.uuid4())
    name = action.get("name", "unknown_action")
    surface_id = action.get("surfaceId", "unknown_surface")
    source = action.get("sourceComponentId")
    context = json.dumps(action.get("context", {}))

    result = f'User performed action "{name}" on surface "{surface_id}"'
    if source:
        result += f" (component: {source})"
    result += f". Context: {context}"

    return [
        {
            "id": str(uuid.uuid4()),
            "role": "assistant",
            "content": "",
            "toolCalls": [
                {
                    "id": call_id,
                    "type": "function",
                    "function": {"name": "log_a2ui_event", "arguments": json.dumps(action)},
                }
            ],
        },
        {"id": str(uuid.uuid4()), "role": "tool", "toolCallId": call_id, "content": result},
    ]


def search_submission(previous_model: dict | None) -> dict:
    """The userAction for clicking "Find trips" on the wizard's first tab."""
    form_data = copy.deepcopy(previous_model) if isinstance(previous_model, dict) else {}
    trip = dict(form_data.get("trip") or {})
    trip.update(TRIP)
    form_data["trip"] = trip
    # The interactive catalog mirrors every input to /__inputs/<componentId> on each keystroke,
    # regardless of binding, and the skill reads it as a fallback. These are the wizard's ids.
    # "wizard" is the Tabs component, which reports {index, title} rather than a bare value.
    form_data["__inputs"] = {
        "trip-destination": TRIP["destination"],
        "trip-depart": TRIP["departDate"],
        "trip-return": TRIP["returnDate"],
        "trip-travellers": TRIP["travellers"],
        "trip-style": TRIP["style"],
        "wizard": {"index": 0, "title": "1. Trip"},
    }
    return {
        "name": "trip_search_submit",
        "surfaceId": SURFACE_ID,
        "sourceComponentId": "trip-submit",
        "context": {**TRIP, "formData": form_data},
        "timestamp": "2026-09-01T00:00:00.000Z",
    }


def option_submission(previous_model: dict | None) -> dict:
    """The userAction for clicking "Continue to review" on the wizard's second tab."""
    form_data = copy.deepcopy(previous_model) if isinstance(previous_model, dict) else {}
    options = dict(form_data.get("options") or {})
    options["choice"] = [CHOICE]
    form_data["options"] = options
    form_data["__inputs"] = {
        "options-picker": [CHOICE],
        "wizard": {"index": 1, "title": "2. Options"},
    }
    return {
        "name": "trip_option_selected",
        "surfaceId": SURFACE_ID,
        "sourceComponentId": "options-submit",
        "context": {"choice": [CHOICE], "formData": form_data},
        "timestamp": "2026-09-01T00:01:00.000Z",
    }


# ---------------------------------------------------------------- assertions


class Report:
    """Accumulates PASS/FAIL lines in the house style of check-a2ui-guide.py."""

    def __init__(self):
        self.ok = True

    def check(self, passed: bool, message: str) -> bool:
        print(("  PASS  " if passed else "  FAIL  ") + message, flush=True)
        self.ok = self.ok and passed
        return passed

    def note(self, message: str) -> None:
        print(f"          - {message}", flush=True)


def assert_turn(
    report: Report,
    index: int,
    stream: Stream,
    expected_surface: str,
    agent_id: str,
    log: OrchestratorLog,
    check_destination: bool,
    expected_tab: int,
) -> ToolCall | None:
    """Runs every per-turn assertion. Returns the render call, when there was one."""
    print(f"  tools called: {', '.join(stream.tool_names()) or '(none)'}", flush=True)

    report.check(not stream.errors, "no RUN_ERROR on the stream")
    for error in stream.errors:
        report.note(error)

    # Tier 2. Not forwarded to the stream (see the module docstring), so the orchestrator's own
    # log is the evidence; the stream is still checked first in case that ever changes.
    if stream.named(LOAD_TOOL):
        report.check(True, f"{LOAD_TOOL} fired on turn {index} (seen on the stream)")
    elif not log.available:
        report.check(
            False,
            f"{LOAD_TOOL} could not be verified — no orchestrator log matched {log.pattern}")
    elif log.loaded_skill(agent_id, SKILL):
        report.check(True, f"{LOAD_TOOL} fired on turn {index} (orchestrator log)")
    else:
        report.check(False, f"{LOAD_TOOL} did NOT fire on turn {index} — the skill was not reloaded")
        report.note("The model continued the flow from memory of an earlier turn. Two suspects: "
                    "SkillPrompt.BuildCatalog's reload rule (a system message), and the synthetic "
                    "prompt AgUiController.ExtractPrompt puts in the user turn on a tool-result "
                    "turn — the latter is more recent, so it wins any conflict.")

    renders = stream.named(RENDER_TOOL)
    if not report.check(len(renders) == 1, f"{RENDER_TOOL} fired exactly once ({len(renders)} call(s))"):
        if not renders:
            if stream.named("render_a2ui"):
                report.note("the model called render_a2ui instead — it built the UI by hand "
                            "rather than using the skill's template")
            elif stream.text:
                report.note(f"the model answered in text instead: {stream.text.strip()[:160]}")
                report.note("Re-send this turn as a plain user message to tell the two causes "
                            "apart: if it renders then, the fault is ExtractPrompt's synthetic "
                            "acknowledgement, not the skill catalog.")
            return None

    call = renders[0]
    surface = call.parsed_args().get("surface")
    skill = call.parsed_args().get("skill")
    report.check(skill == SKILL, f"rendered from skill '{skill}'")
    report.check(
        isinstance(surface, str) and surface.split("/")[-1].removesuffix(".json") == expected_surface,
        f"rendered the '{expected_surface}' surface (got '{surface}')")

    operations = operations_of(call)
    if not report.check(bool(operations), "tool result parses as JSON with an a2ui_operations array"):
        report.note((call.result or "")[:200] or "(no result on the stream)")
        return call

    writes = [o for o in operations if "updateDataModel" in o]
    expected = 2 + len(writes)
    report.check(
        len(operations) == expected,
        f"a2ui_operations carries createSurface + updateComponents + {len(writes)} data writes "
        f"(got {len(operations)})")
    report.check(
        all(o["updateDataModel"].get("path") not in ("/", "", None) for o in writes),
        "every data write targets a branch, never the root (a root write would discard /__inputs)")
    for kind in ("createSurface", "updateComponents", "updateDataModel"):
        report.check(any(kind in op for op in operations), f"a2ui_operations includes {kind}")

    # The tab index is how a single surface behaves as a wizard: the agent seeds /__tabs/wizard
    # and InteractiveTabs moves the user there. Without this the flow renders one card that never
    # leaves step 1, which would still pass every assertion above.
    model = rendered_data_model(call) or {}
    tab = (model.get("__tabs") or {}).get("wizard") if isinstance(model.get("__tabs"), dict) else None
    if not report.check(
        tab == expected_tab,
        f"the agent moved the wizard to tab {expected_tab} (/__tabs/wizard = {tab!r})",
    ):
        report.note("the surface renders but never advances, so the user stays on step 1 and has "
                    "to find the next tab themselves")

    if check_destination:
        model = rendered_data_model(call)
        seeded = (model or {}).get("trip", {}).get("destination") if isinstance(model, dict) else None
        if not report.check(
            isinstance(seeded, str) and DESTINATION.lower() in seeded.lower(),
            f"the user's destination reached the data model (/trip/destination = {seeded!r})",
        ):
            report.note("the model rendered the surface without passing `values`, so the template's "
                        "empty defaults were shown and the user has to retype what they just said")

    return call


# ---------------------------------------------------------------- driver


def run_flow(args) -> bool:
    report = Report()
    client = Orchestrator(args.base_url, args.agent)

    print(f"Skill flow — live check against {client.base_url}")
    print(f"  agent:  {client.agent_id}")
    print(f"  skill:  {SKILL}")
    print("  (TLS verification disabled — Aspire dev certificate)\n")

    print("Authentication:")
    try:
        who = client.login(args.email, args.password)
    except RuntimeError as exc:
        report.check(False, f"sign in as {args.email}: {exc}")
        print("        Is the AppHost running? Start it with ./ntg → run.")
        return False
    report.check(True, f"signed in as {who} (identity cookie captured)")

    thread_id = str(uuid.uuid4())
    print(f"  thread: {thread_id}")

    log = OrchestratorLog(args.log_glob)
    if not log.available:
        print(f"  note:   no orchestrator stdout matched {args.log_glob}; "
              f"{LOAD_TOOL} cannot be corroborated")

    tools = None if args.no_render_a2ui else [RENDER_A2UI_TOOL]
    messages = [user_message(OPENING_PROMPT)]

    # The same surface every turn. That is the assertion now: the flow re-renders "trip-planner"
    # and the stable messageId makes the browser replace one card rather than stack three.
    turns = [
        ("plain user message", "trip-planner", True, 0, None),
        ("trip_search_submit submission", "trip-planner", False, 1, search_submission),
        ("trip_option_selected submission", "trip-planner", False, 2, option_submission),
    ]

    previous_model: dict | None = None

    for index, (label, expected_surface, check_destination, expected_tab, build_action) in enumerate(turns, start=1):
        if build_action is not None:
            action = build_action(previous_model)
            messages = messages + submission_messages(action)
            detail = f'action "{action["name"]}" on "{action["surfaceId"]}"'
        else:
            detail = f'"{OPENING_PROMPT}"'

        print(f"\nTurn {index} — {label}")
        print(f"  sends:  {detail}", flush=True)

        log.mark()
        started = time.monotonic()
        try:
            events = client.run(thread_id, messages, tools)
        except Exception as exc:  # noqa: BLE001 - any transport failure is a real result
            report.check(False, f"turn {index} run failed: {exc}")
            return False
        print(f"  {len(events)} events in {time.monotonic() - started:.1f}s", flush=True)

        stream = Stream(events)
        call = assert_turn(
            report, index, stream, expected_surface, client.agent_id, log, check_destination, expected_tab)

        if call is None:
            print(f"\n  Turn {index} rendered nothing, so turns after it have no surface to submit.")
            return False

        previous_model = rendered_data_model(call)

    print("\n  All three steps rendered onto one surface. The visual confirmation is in the "
          "browser: one card, the tab strip advancing, earlier tabs still open for review.")
    return report.ok


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base-url", default=DEFAULT_BASE_URL, help="orchestrator base URL")
    parser.add_argument("--agent", default=DEFAULT_AGENT, help="agent id (defaults to the seeded Default Agent)")
    parser.add_argument("--email", default=DEFAULT_EMAIL, help="account to sign in as")
    parser.add_argument("--password", default="Ntg@123", help="password for --email (dev default)")
    parser.add_argument("--log-glob", default=DEFAULT_LOG_GLOB,
                        help="glob for the orchestrator's stdout, used to observe load_skill")
    parser.add_argument("--no-render-a2ui", action="store_true",
                        help="do not declare the render_a2ui frontend tool the browser injects")
    args = parser.parse_args()

    ok = run_flow(args)
    print("\n" + ("RESULT: PASS" if ok else "RESULT: FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Verify the Phase 0 fix to A2uiPrompt.RenderGuide.

The fix corrected seven property names that do not exist in the A2UI v0.9 basic catalog.
It is easy to regress, because the symptom is nearly invisible: interactiveCatalog.tsx keeps
local React state and mirrors every input to /__inputs/<id>, so typing and submitting keep
working even when the data-model binding never binds. The one visible symptom is that seeding
`data` stops pre-filling fields.

Two modes:

  static (default)   Checks the guide text and validates the JSON examples inside it against
                     the real catalog schema. Needs nothing running.

  --live URL         Asks a running agent to build a pre-filled form, captures the render_a2ui
                     tool call off the AG-UI stream, and validates what the model actually
                     emitted. This is the test that proves the fix does its job.

Usage:
  python3 scripts/check-a2ui-guide.py
  python3 scripts/check-a2ui-guide.py --live http://localhost:5142
  python3 scripts/check-a2ui-guide.py --live http://localhost:5142 --agent <guid>
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GUIDE = REPO / "NTG.Agent.Orchestrator" / "Services" / "Agents" / "A2uiPrompt.cs"
SCHEMA = REPO / "my-copilot-app" / "node_modules" / "@a2ui" / "web_core" / "src" / "v0_9" / "schemas" / "basic_catalog.json"

DEFAULT_AGENT = "31cf1546-e9c9-4d95-a8e5-3c7c7570fec5"  # AgentFactory.DefaultAgentId
LAYOUT = {"Column", "Row", "Card", "List"}

# Property names the guide used to name that do not exist in the catalog. Each pattern is
# specific enough not to collide with legitimate text (e.g. Text's real "text" property).
DEAD_PATTERNS = [
    (r'"text"\s*:\s*\{\s*"path"', 'TextField bound via "text" (real prop is "value")'),
    (r'"checked"\s*:\s*\{\s*"path"', 'CheckBox bound via "checked" (real prop is "value")'),
    (r'"selections"\s*:\s*\{\s*"path"', 'ChoicePicker bound via "selections" (real prop is "value")'),
    (r"\btextFieldType\b", 'TextField "textFieldType" (real prop is "variant")'),
    (r"\bminValue\b", 'Slider "minValue" (real prop is "min")'),
    (r"\bmaxValue\b", 'Slider "maxValue" (real prop is "max")'),
    (r"\bmaxAllowedSelections\b", 'ChoicePicker "maxAllowedSelections" (does not exist)'),
    (r"primary\|secondary\|text", "Button variant enum (real values are default|primary|borderless)"),
    # The enum pattern above only catches the pipe-separated spelling. The guide once ALSO said
    # 'give secondary actions variant "secondary" or "text"' in prose, which contradicted its own
    # component table and slipped through for months. Match the variant names where they are
    # named as variants, not the ordinary English words "secondary" and "text".
    (r'variant\s+"?secondary"?', 'Button variant "secondary" named in prose (does not exist)'),
    (r'variant\s+"?text"?(?!\w)', 'Button variant "text" named in prose (does not exist)'),
]

BINDABLE_INPUTS = ["TextField", "CheckBox", "Slider", "DateTimeInput", "ChoicePicker"]


# ---------------------------------------------------------------- catalog


def load_catalog() -> dict:
    """Flatten basic_catalog.json into {component: (props, required, enums)}."""
    if not SCHEMA.is_file():
        return {}

    raw = json.loads(SCHEMA.read_text())
    common = json.loads((SCHEMA.parent / "common_types.json").read_text())["$defs"]
    base = set((common["ComponentCommon"].get("properties") or {}).keys())
    checkable = set((common["Checkable"].get("properties") or {}).keys())
    cat_common = set(((raw.get("$defs", {}).get("CatalogComponentCommon") or {}).get("properties") or {}).keys())

    catalog = {}
    for name, node in raw["components"].items():
        props, required, enums, has_checks = {}, set(), {}, False
        for part in node.get("allOf", []):
            if "Checkable" in part.get("$ref", ""):
                has_checks = True
            for key, value in (part.get("properties") or {}).items():
                if key == "component":
                    continue
                props[key] = value
                if "enum" in value:
                    enums[key] = set(value["enum"])
            required |= {r for r in part.get("required", []) if r != "component"}
        allowed = set(props) | base | cat_common | (checkable if has_checks else set())
        catalog[name] = (allowed, required, enums)
    return catalog


def validate_surface(label: str, surface: dict, catalog: dict) -> list[str]:
    """Structural checks mirroring SurfaceValidator.cs."""
    errors: list[str] = []
    components = surface.get("components")
    if not isinstance(components, list) or not components:
        return [f"{label}: no components array"]

    by_id: dict[str, dict] = {}
    for i, component in enumerate(components):
        cid = component.get("id")
        if not isinstance(cid, str) or not cid:
            errors.append(f"{label}: components[{i}] has no id")
            continue
        if cid in by_id:
            errors.append(f"{label}: duplicate component id '{cid}'")
        by_id[cid] = component

    data = surface.get("data") or {}

    def seeded(path: str) -> bool:
        node = data
        for segment in [s for s in path.split("/") if s]:
            if not isinstance(node, dict) or segment not in node:
                return False
            node = node[segment]
        return True

    def walk_bindings(node, cid: str):
        if isinstance(node, dict):
            if set(node) == {"path"} and isinstance(node["path"], str):
                if not seeded(node["path"]):
                    errors.append(f"{label}: '{cid}' binds '{node['path']}' with no seed in data")
                return
            for value in node.values():
                walk_bindings(value, cid)
        elif isinstance(node, list):
            for value in node:
                walk_bindings(value, cid)

    referenced: set[str] = set()
    for cid, component in by_id.items():
        kind = component.get("component")
        if catalog and kind not in catalog:
            errors.append(f"{label}: '{cid}' uses unknown component '{kind}'")
            continue
        if catalog:
            allowed, required, enums = catalog[kind]
            for prop in required:
                if prop not in component:
                    errors.append(f"{label}: '{cid}' ({kind}) missing required '{prop}'")
            for prop, value in component.items():
                if prop == "component":
                    continue
                if prop not in allowed:
                    errors.append(f"{label}: '{cid}' ({kind}) has unknown property '{prop}'")
                elif prop in enums and isinstance(value, str) and value not in enums[prop]:
                    errors.append(
                        f"{label}: '{cid}' ({kind}) {prop}='{value}' not in {'|'.join(sorted(enums[prop]))}")

        for prop in ("child", "children", "content", "trigger"):
            if prop not in component:
                continue
            value = component[prop]
            targets = [value] if isinstance(value, str) else (value if isinstance(value, list) else [])
            if isinstance(value, dict) and isinstance(value.get("componentId"), str):
                targets = [value["componentId"]]
            for target in targets:
                referenced.add(target)
                if target == cid:
                    errors.append(f"{label}: '{cid}' references itself")
                elif target not in by_id:
                    errors.append(f"{label}: '{cid}' references missing child '{target}'")

        walk_bindings(component, cid)

    root = by_id.get("root")
    if root is None:
        errors.append(f"{label}: no component with id 'root'")
    elif root.get("component") not in LAYOUT:
        errors.append(f"{label}: root is '{root.get('component')}', must be one of {'|'.join(sorted(LAYOUT))}")

    for cid in sorted(set(by_id) - referenced - {"root"}):
        errors.append(f"{label}: '{cid}' is unreachable and will not render")

    return errors


# ---------------------------------------------------------------- static mode


def extract_examples(text: str) -> list[dict]:
    """Pull the JSON argument object out of each render_a2ui({...}) example in the guide."""
    examples = []
    for match in re.finditer(r"render_a2ui\(\s*\{", text):
        start = text.index("{", match.start())
        depth, i = 0, start
        while i < len(text):
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        try:
            examples.append(json.loads(text[start:i + 1]))
        except json.JSONDecodeError as exc:
            examples.append({"__parse_error__": str(exc)})
    return examples


def run_static(catalog: dict) -> bool:
    print("Phase 0 — static checks on A2uiPrompt.RenderGuide\n")
    if not GUIDE.is_file():
        print(f"  FAIL  {GUIDE} not found")
        return False

    text = GUIDE.read_text()
    ok = True

    def check(passed: bool, message: str):
        nonlocal ok
        print(("  PASS  " if passed else "  FAIL  ") + message)
        ok = ok and passed

    print("Dead property names must not reappear:")
    for pattern, description in DEAD_PATTERNS:
        check(re.search(pattern, text) is None, f"no {description}")

    print("\nEvery input binds through \"value\":")
    for component in BINDABLE_INPUTS:
        check(
            re.search(rf'-\s*{component}\s*(?:→|->)\s*"value"', text) is not None,
            f"{component} documented as binding via \"value\"")

    print("\nExamples in the guide:")
    examples = extract_examples(text)
    check(len(examples) >= 2, f"found {len(examples)} render_a2ui example(s)")

    if not catalog:
        print(f"  SKIP  catalog schema not found at {SCHEMA} — run npm install in my-copilot-app")

    for index, example in enumerate(examples, start=1):
        label = f"example {index}"
        if "__parse_error__" in example:
            check(False, f"{label} is not valid JSON: {example['__parse_error__']}")
            continue
        root = next((c for c in example.get("components", []) if c.get("id") == "root"), None)
        check(root is not None and root.get("component") == "Column",
              f"{label} uses a Column root (not Card — the guide forbids double-framing)")
        errors = validate_surface(label, example, catalog)
        check(not errors, f"{label} validates against the catalog")
        for error in errors:
            print(f"          - {error}")

    return ok


# ---------------------------------------------------------------- live mode

RENDER_TOOL = {
    "name": "render_a2ui",
    "description": "Render a dynamic A2UI v0.9 surface with structured parameters. Follow the A2UI render tool usage guide provided in context.",
    "parameters": {
        "type": "object",
        "properties": {
            "surfaceId": {"type": "string", "description": "Unique surface identifier."},
            "components": {"type": "array", "description": "A2UI v0.9 component array (flat format). The root component must have id \"root\".", "items": {"type": "object"}},
            "data": {"type": "object", "description": "Initial data model for the surface."},
        },
        "required": ["surfaceId", "components"],
    },
}

PROMPT = (
    "Build me a signup form with a name field already filled in with Tien, "
    "an 'email me updates' checkbox that starts checked, and a submit button."
)


def run_live(base_url: str, agent_id: str, catalog: dict, insecure: bool = False) -> bool:
    import ssl
    import urllib.request

    print(f"Phase 0 — live check against {base_url}\n")
    print(f"  prompt: {PROMPT}\n")

    # The Aspire run uses the ASP.NET Core self-signed dev cert, so an https base URL
    # fails verification. Only ever relaxed for localhost.
    context = None
    if insecure:
        context = ssl._create_unverified_context()
        print("  (TLS verification disabled — dev certificate)\n")

    payload = {
        "threadId": str(uuid.uuid4()),
        "runId": str(uuid.uuid4()),
        "messages": [{"id": str(uuid.uuid4()), "role": "user", "content": PROMPT}],
        "tools": [RENDER_TOOL],
    }
    request = urllib.request.Request(
        f"{base_url.rstrip('/')}/api/agui/{agent_id}",
        data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json", "Accept": "text/event-stream"},
        method="POST",
    )

    calls: dict[str, dict] = {}
    try:
        with urllib.request.urlopen(request, timeout=180, context=context) as response:
            for raw in response:
                line = raw.decode("utf-8", "replace").strip()
                if not line.startswith("data:"):
                    continue
                try:
                    event = json.loads(line[5:].strip())
                except json.JSONDecodeError:
                    continue
                kind = event.get("type")
                if kind == "TOOL_CALL_START" and event.get("toolCallName") == "render_a2ui":
                    calls[event["toolCallId"]] = {"args": ""}
                elif kind == "TOOL_CALL_ARGS" and event.get("toolCallId") in calls:
                    calls[event["toolCallId"]]["args"] += event.get("delta", "")
    except Exception as exc:  # noqa: BLE001 - surface any transport failure verbatim
        print(f"  FAIL  could not reach the agent: {exc}")
        print("        Is the AppHost running? Check the Aspire dashboard for the orchestrator URL.")
        return False

    if not calls:
        print("  FAIL  the model never called render_a2ui")
        print("        Either the guide was not injected, or the model answered in text.")
        return False

    ok = True
    for call_id, call in calls.items():
        print(f"  render_a2ui call {call_id[:8]} — {len(call['args'])} bytes of arguments")
        try:
            surface = json.loads(call["args"])
        except json.JSONDecodeError as exc:
            print(f"  FAIL  arguments are not valid JSON: {exc}")
            ok = False
            continue

        errors = validate_surface("emitted surface", surface, catalog)
        if errors:
            ok = False
            print("  FAIL  emitted surface does not validate:")
            for error in errors:
                print(f"          - {error}")
        else:
            print("  PASS  emitted surface validates against the catalog")

        # The point of the fix: bindings use real props AND the seed carries the pre-filled value.
        components = surface.get("components", [])
        inputs = [c for c in components if c.get("component") in BINDABLE_INPUTS]
        print(f"  {'PASS' if inputs else 'FAIL'}  surface contains {len(inputs)} input component(s)")
        ok = ok and bool(inputs)

        for component in inputs:
            kind, cid = component.get("component"), component.get("id")
            bound = isinstance(component.get("value"), dict) and "path" in component["value"]
            legacy = [p for p in ("text", "checked", "selections") if isinstance(component.get(p), dict)]
            if legacy:
                print(f"  FAIL  '{cid}' ({kind}) bound via {legacy[0]!r} — the dead prop name is back")
                ok = False
            elif not bound:
                print(f"  FAIL  '{cid}' ({kind}) has no {{path}} binding on 'value' — it will render frozen")
                ok = False
            else:
                print(f"  PASS  '{cid}' ({kind}) binds value -> {component['value']['path']}")

        seeded = json.dumps(surface.get("data") or {})
        prefilled = "Tien" in seeded
        print(f"  {'PASS' if prefilled else 'FAIL'}  data seeds the pre-filled value  data={seeded[:160]}")
        ok = ok and prefilled

    print("\n  The final confirmation is visual: the name field should render containing 'Tien'.")
    print("  Before the fix it rendered empty — that is the only symptom the fallback did not mask.")
    return ok


# ---------------------------------------------------------------- entry point


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--live", metavar="URL", help="base URL of a running orchestrator")
    parser.add_argument("--agent", default=DEFAULT_AGENT, help="agent id (defaults to the seeded Default Agent)")
    parser.add_argument("--guide", metavar="PATH", help="override the A2uiPrompt.cs path (used to self-test this script)")
    parser.add_argument("--insecure", action="store_true", help="skip TLS verification (Aspire's self-signed dev cert)")
    args = parser.parse_args()

    global GUIDE
    if args.guide:
        GUIDE = Path(args.guide)

    catalog = load_catalog()
    ok = run_static(catalog)

    if args.live:
        print()
        ok = run_live(args.live, args.agent, catalog, insecure=args.insecure) and ok

    print("\n" + ("RESULT: PASS" if ok else "RESULT: FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())

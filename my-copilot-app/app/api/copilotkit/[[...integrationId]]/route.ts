// app/api/copilotkit/[[...integrationId]]/route.ts

import { NextRequest, NextResponse } from "next/server";
import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { EventType, HttpAgent, Middleware } from "@ag-ui/client";
import type {
  AbstractAgent,
  ActivitySnapshotEvent,
  BaseEvent,
  RunAgentInput,
  ToolCallStartEvent,
} from "@ag-ui/client";
import { A2UIMiddleware } from "@ag-ui/a2ui-middleware";
// rxjs ships with @ag-ui/client (a middleware's `run` must return one of its Observables), so
// this is the same copy the agent itself pipes events through, not a second stream library.
import { Observable } from "rxjs";

if (process.env.NODE_ENV !== "production") {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
}

const orchestratorUrl =
  process.env.services__ntg_agent_orchestrator__https__0 ??
  process.env.ORCHESTRATOR_URL ??
  "https://localhost:7093";

// Freeform A2UI — the model hand-authoring a surface via `render_a2ui` ("build me a card with…").
// On unless a deployment opts out; the A2UIMiddleware call below explains what it costs.
const freeformA2UIEnabled = !/^(0|false|off)$/i.test(
  process.env.A2UI_FREEFORM_TOOL?.trim() ?? ""
);

// One chat card per skill surface instead of one per render — see StableSurfaceIdMiddleware.
// On unless a deployment opts out, same switch shape as the flag above. The escape hatch exists
// because this changes where a re-rendered surface appears in the transcript (in place, rather
// than appended), which is a judgement call about the conversation, not a bug fix nobody could
// disagree with; off, the stack behaves exactly as it did before.
const stableSurfaceIdsEnabled = !/^(0|false|off)$/i.test(
  process.env.A2UI_STABLE_SURFACE_IDS?.trim() ?? ""
);

// The activity type @ag-ui/a2ui-middleware stamps on the surfaces it emits (its exported
// `A2UIActivityType`), the prefix of the message id it gives them, and the key it wraps the
// operations in. Mirrored as literals rather than imported so this stays three strings.
const A2UI_ACTIVITY_TYPE = "a2ui-surface";
const A2UI_SURFACE_MESSAGE_ID_PREFIX = "a2ui-surface-";
const A2UI_OPERATIONS_KEY = "a2ui_operations";
// What the middleware groups operations under when none of them names a surface.
const A2UI_FALLBACK_SURFACE_ID = "default";
// The orchestrator's server-side surface tool (SkillPrompt.RenderToolName).
const SKILL_SURFACE_TOOL_NAME = "render_skill_surface";

/** The four operation payloads a surfaceId can hide in — mirrors the middleware's `getOperationSurfaceId`. */
type A2UIOperation = {
  createSurface?: { surfaceId?: unknown };
  updateComponents?: { surfaceId?: unknown };
  updateDataModel?: { surfaceId?: unknown };
  deleteSurface?: { surfaceId?: unknown };
};

/**
 * The surface an ACTIVITY_SNAPSHOT is about, read from the operations it carries.
 *
 * Deliberately not "strip the last dash-delimited segment of the message id": surface ids are
 * authored names like `trip-search`, so that would eat half the id. Reading the operations gives
 * the authoritative id, and the caller then rebuilds the prefix from it — an exact prefix match
 * is the only proof that the remainder of the id really is a call id.
 */
function snapshotSurfaceId(content: unknown): string | null {
  if (typeof content !== "object" || content === null || Array.isArray(content)) return null;
  const operations = (content as Record<string, unknown>)[A2UI_OPERATIONS_KEY];
  if (!Array.isArray(operations) || operations.length === 0) return null;

  for (const operation of operations) {
    if (typeof operation !== "object" || operation === null || Array.isArray(operation)) continue;
    const { createSurface, updateComponents, updateDataModel, deleteSurface } =
      operation as A2UIOperation;
    for (const payload of [createSurface, updateComponents, updateDataModel, deleteSurface]) {
      const surfaceId = payload?.surfaceId;
      if (typeof surfaceId === "string" && surfaceId.length > 0) return surfaceId;
    }
  }

  // No operation names a surface, so the middleware grouped them under "default" and keyed the
  // message with that. Not a guess: it groups per surface id, so an event whose group key is a
  // real id carries that id on every one of its operations — reaching here means the key was
  // the fallback.
  return A2UI_FALLBACK_SURFACE_ID;
}

/** Renames one event; anything that is not a call-id-keyed skill surface passes straight through. */
function stabiliseSurfaceMessageId(
  event: BaseEvent,
  toolNamesByCallId: ReadonlyMap<string, string>
): BaseEvent {
  if (event.type !== EventType.ACTIVITY_SNAPSHOT) return event;
  const snapshot = event as ActivitySnapshotEvent;
  if (snapshot.activityType !== A2UI_ACTIVITY_TYPE) return event;
  if (typeof snapshot.messageId !== "string") return event;

  const surfaceId = snapshotSurfaceId(snapshot.content);
  if (surfaceId === null) return event;

  const stableMessageId = `${A2UI_SURFACE_MESSAGE_ID_PREFIX}${surfaceId}`;
  // Already stable, or an id we cannot account for: leave it exactly as it is.
  if (!snapshot.messageId.startsWith(`${stableMessageId}-`)) return event;
  const callId = snapshot.messageId.slice(stableMessageId.length + 1);

  // Scope: skill surfaces only. Collapsing two renders that share a surface id is right when the
  // id was *authored* — a skill's template owns its surface id, and A2UI treats that id as the
  // surface's identity, so re-rendering `trip-search` is by definition the same surface. Ids in
  // freeform `render_a2ui` are invented by the model turn by turn and get reused across unrelated
  // requests, where collapsing would silently rewrite a card further up the transcript. The two
  // are distinguishable here: the id's suffix is a tool call id this stream has already carried a
  // TOOL_CALL_START for, and for a skill surface that call is render_skill_surface itself (the
  // middleware treats any non-A2UI tool call as the "outer" call it keys the surface by).
  if (toolNamesByCallId.get(callId) !== SKILL_SURFACE_TOOL_NAME) return event;

  return { ...snapshot, messageId: stableMessageId };
}

/**
 * Makes a re-rendered skill surface update its chat card instead of opening a new one.
 *
 * @ag-ui/a2ui-middleware republishes every A2UI tool result as an `a2ui-surface`
 * ACTIVITY_SNAPSHOT keyed `a2ui-surface-<surfaceId>-<callId>`. @ag-ui/client resolves such an
 * event by messageId — a hit replaces that message where it sits, a miss appends a new one — and
 * CopilotKit renders each activity message with `key={message.id}`, so an unchanged key keeps the
 * same React element, the same A2UIProvider and the same surface store. The call id in the key is
 * therefore the whole reason a second render of one surface opens a second card with an empty
 * store. The middleware does have a call-id-free form of that id, but cannot reach it on this
 * path: it keys by `outerCallId ?? toolCallId`, and render_skill_surface is neither an A2UI tool
 * name nor log_a2ui_event, so it always becomes the outer call and the id is never bare.
 * Dropping the call id restores what react-core's own A2UI renderer documents as the model —
 * "one stable messageId … rendered in place".
 */
class StableSurfaceIdMiddleware extends Middleware {
  run(input: RunAgentInput, next: AbstractAgent): Observable<BaseEvent> {
    return new Observable<BaseEvent>((subscriber) => {
      // Tool name per call id, so a surface can be traced back to the tool that drew it.
      // Filled from this same stream: TOOL_CALL_START is forwarded before the snapshot derived
      // from that call's result. Scoped to one subscription, i.e. one run.
      const toolNamesByCallId = new Map<string, string>();

      const subscription = this.runNext(input, next).subscribe({
        next: (event) => {
          try {
            if (event.type === EventType.TOOL_CALL_START) {
              const start = event as ToolCallStartEvent;
              toolNamesByCallId.set(start.toolCallId, start.toolCallName);
            }
            subscriber.next(stabiliseSurfaceMessageId(event, toolNamesByCallId));
          } catch (renameError) {
            // This middleware only ever renames a message, but it sits in front of every event of
            // every chat — so a malformed event must cost one un-renamed surface, not the run.
            console.error("[Copilot Handler] A2UI surface id rewrite skipped", renameError);
            subscriber.next(event);
          }
        },
        error: (streamError) => subscriber.error(streamError),
        complete: () => subscriber.complete(),
      });

      return () => subscription.unsubscribe();
    });
  }
}

async function handleCopilotRequest(req: NextRequest, integrationId: string) {
  console.log(`[Copilot Handler] ${req.method} ${req.url}`);

  try {
    const url = new URL(req.url);
    // Segments: ["", "api", "copilotkit", "<uuid>", ...]
    // We want exactly the first 4 segments as the base: /api/copilotkit/<uuid>
    const segments = url.pathname.split("/");
    const endpoint = segments.slice(0, 4).join("/") || "/";
    console.log(`[Copilot Handler] basePath endpoint: ${endpoint}`);

    // Forward the session cookie so the .NET backend can identify the user / anonymous session.
    const cookie = req.headers.get("cookie") ?? "";
    const agentInstance = new HttpAgent({
      agentId: "dotnet_orchestrator_agent",
      url: `${orchestratorUrl}/api/agui/${integrationId}`,
      headers: { ...(cookie ? { Cookie: cookie } : {}) },
    });
    console.log(`[Copilot Handler] HttpAgent → ${orchestratorUrl}/api/agui/${integrationId}`);

    // A2UI: the middleware converts A2UI coming back from the agent into ACTIVITY_SNAPSHOT
    // messages that the A2UI renderer (registered on <CopilotKit renderActivityMessages>)
    // consumes — both the model's streamed render_a2ui args and any tool result shaped
    // {"a2ui_operations": […]}, which is how the orchestrator's server-side render_skill_surface
    // returns a skill's pre-authored template. It also injects log_a2ui_event tool calls for user
    // interactions on a surface. None of that depends on the flag below, so the middleware itself
    // is applied either way.
    //
    // `injectA2UITool` adds one thing this stack acts on: the `render_a2ui` declaration in the
    // run's tool list. Its other two effects are inert here — the usage guidance and the
    // forwardedProps flag ride RunAgentInput.context/forwardedProps, and AgUiRunRequest binds only
    // threadId, runId, messages and tools, so the backend drops both unread. (Nor is the
    // middleware's notorious wrong-prop catalog a factor: that text is the exported A2UI_PROMPT,
    // which nothing inside the package references.) What the model actually reads is the
    // orchestrator's A2uiPrompt.RenderGuide, which AgentService prepends *because* render_a2ui is
    // among the run's frontend tools. So this one boolean also decides whether every turn spends
    // ~130 lines teaching the model to hand-author component trees — dead weight for an agent
    // whose bound skill owns the surface, and a rival to the skill catalog telling it to call
    // render_skill_surface instead.
    //
    // It is a per-deployment switch rather than a per-agent one because this route cannot tell the
    // difference: skill bindings are server state, the only endpoint exposing them is Admin-only
    // (403 for an ordinary user), and asking would add a round trip to every chat turn. The
    // per-agent form of the decision belongs beside the guide in AgentService, which already holds
    // both facts. Default on, so an agent with no skills keeps freeform A2UI.
    //
    // Registration order is outermost-first, and it matters: `use()` pushes onto a list that
    // AbstractAgent folds with reduceRight, so the middleware registered *first* wraps the rest
    // and sees what they emit. Registering the id rewriter before A2UIMiddleware is what lets it
    // see the ACTIVITY_SNAPSHOTs A2UIMiddleware invents; registered after, it would only ever see
    // the raw orchestrator stream, where those events do not exist yet. Verified both ways
    // against the real packages.
    if (stableSurfaceIdsEnabled) agentInstance.use(new StableSurfaceIdMiddleware());
    agentInstance.use(new A2UIMiddleware({ injectA2UITool: freeformA2UIEnabled }));

    const runtime = new CopilotRuntime({
      agents: { dotnet_orchestrator_agent: agentInstance },
    });

    const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
      runtime,
      serviceAdapter: new ExperimentalEmptyAdapter(),
      endpoint,
    });

    const response = await handleRequest(req);
    console.log(`[Copilot Handler] responded with status: ${response.status}`);

    // Set session cookie if not already present
    const incomingCookie = req.headers.get("cookie") ?? "";
    if (!/ntg_session_id=/.test(incomingCookie) && response instanceof Response) {
      const sessionId = crypto.randomUUID();
      // Secure in production so the session id is never sent over plaintext HTTP
      // (dev runs on http://localhost, where Secure would prevent the cookie being set).
      const secureAttribute = process.env.NODE_ENV === "production" ? "; Secure" : "";
      response.headers.append(
        "Set-Cookie",
        `ntg_session_id=${sessionId}; Path=/; HttpOnly; SameSite=Lax${secureAttribute}`
      );
    }

    return response;

  } catch (globalError: unknown) {
    console.error("[Copilot Handler CRITICAL]", globalError);
    return NextResponse.json(
      { error: "Internal Server Error", details: globalError instanceof Error ? globalError.message : String(globalError) },
      { status: 500 }
    );
  }
}

type RouteParams = { params: Promise<{ integrationId?: string[] }> };

export async function GET(req: NextRequest, { params }: RouteParams) {
  const { integrationId } = await params;
  const lastSegment = integrationId?.[integrationId.length - 1];

  // CopilotKit's thread-store pings /threads to load conversation history.
  // ExperimentalEmptyAdapter doesn't implement threads — return an empty list.
  if (lastSegment === "threads") {
    return NextResponse.json({ threads: [] });
  }

  const extractedId = integrationId?.[0] ?? "default_agent";
  return handleCopilotRequest(req, extractedId);
}

export async function POST(req: NextRequest, { params }: RouteParams) {
  const { integrationId } = await params;
  const extractedId = integrationId?.[0] ?? "default_agent";
  return handleCopilotRequest(req, extractedId);
}

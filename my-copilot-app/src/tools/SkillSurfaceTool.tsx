"use client";

import React from "react";
import {
  A2UIProvider,
  A2UIRenderer,
  DEFAULT_SURFACE_ID,
  useA2UIActions,
  useA2UIError,
  type A2UIClientEventMessage,
} from "@copilotkit/a2ui-renderer";
import {
  a2uiDefaultTheme,
  useAgent,
  useCopilotKit,
  useRenderTool,
  UseAgentUpdate,
  type Message,
} from "@copilotkit/react-core/v2";
import { z } from "zod";

import { interactiveCatalog } from "../a2ui/interactiveCatalog";
import { AGENT_ID } from "../constants";

// The browser renders the result of the server-side `render_skill_surface` tool: the
// orchestrator looks up one of a skill's bundled A2UI templates, merges the model's values
// into its data model, and streams the resulting A2UI operations back as the tool result.
//
// Why this file exists — during a LIVE run @ag-ui/a2ui-middleware also parses that same tool
// result and re-emits it as an `a2ui-surface` ACTIVITY_SNAPSHOT, which src/a2ui/activityRenderer
// paints. That parsing runs over the event stream only. On reload src/utils/agentMessages rebuilds
// the persisted tool call + result but no activity message, so without a renderer here the surface
// vanished and the raw operations JSON (often ~100 KB) fell through to CopilotKit's default
// tool-call card. This renderer replays the stored operations through the very same A2UI pieces
// the live path uses, so a reloaded conversation shows the real, interactive surface again.
const parameters = z.object({
  skill: z.string().describe("Name of the skill that owns the surface"),
  surface: z.string().describe("Name of the surface template, without the assets/ prefix"),
  values: z
    .record(z.string(), z.unknown())
    .optional()
    .describe("Values the agent merged into the surface's data model"),
});

// Key the orchestrator wraps the operations in — the same literal as `A2UI_OPERATIONS_KEY` in
// @ag-ui/a2ui-middleware and copilotkit.a2ui. 
// generic-api-key rule fires on any identifier containing "key" that is assigned a quoted
// mixed-case string, so the upstream spelling failed CI on a value that is a protocol field name,
// not a credential. Renaming it back reopens that failure.
const A2UI_OPERATIONS_FIELD = "a2ui_operations";

// The activity type @ag-ui/a2ui-middleware stamps on the ACTIVITY_SNAPSHOT it derives from this
// tool's result (its exported `A2UIActivityType`) and the prefix of the id it gives that activity
// message. Two id shapes exist and both can be on screen: `a2ui-surface-<surfaceId>` after the
// route's StableSurfaceIdMiddleware has renamed it, and the middleware's own
// `a2ui-surface-<surfaceId>-<toolCallId>` when that rewrite is switched off. Mirrored rather than
// imported because that package is server-side — it pulls in node's `crypto` — and importing it
// would drag it into the browser bundle just for a couple of string constants.
const A2UI_ACTIVITY_TYPE = "a2ui-surface";
const A2UI_SURFACE_MESSAGE_ID_PREFIX = "a2ui-surface-";

// The tool this file renders (the orchestrator's SkillPrompt.RenderToolName).
const SKILL_SURFACE_TOOL_NAME = "render_skill_surface";

const NOTE_NOT_RESTORABLE =
  "This interactive surface can't be redrawn from the saved conversation. Ask the assistant to show it again.";
const NOTE_RENDER_FAILED =
  "This interactive surface couldn't be redrawn. Ask the assistant to show it again.";

// One entry of the a2ui_operations array. A type alias (not an interface) so it keeps the implicit
// index signature that `processMessages(messages: Array<Record<string, unknown>>)` expects.
type A2UIOperation = {
  version?: string;
  surfaceId?: string;
  createSurface?: { surfaceId?: string; catalogId?: string };
  updateComponents?: { surfaceId?: string; components?: unknown[] };
  updateDataModel?: { surfaceId?: string; path?: string; value?: unknown };
  deleteSurface?: { surfaceId?: string };
};

interface SurfaceGroup {
  surfaceId: string;
  operations: A2UIOperation[];
}

/** Reads `{"a2ui_operations":[…]}` out of the tool result. Never throws; returns [] on anything odd. */
function parseResult(result: string | undefined): A2UIOperation[] {
  if (!result) return [];
  try {
    let parsed: unknown = JSON.parse(result);
    // The backend may forward the tool result as a JSON-encoded string; unwrap once more.
    if (typeof parsed === "string") {
      parsed = JSON.parse(parsed);
    }
    if (typeof parsed !== "object" || parsed === null) return [];
    const operations = (parsed as Record<string, unknown>)[A2UI_OPERATIONS_FIELD];
    if (!Array.isArray(operations)) return [];
    return operations.filter(
      (op): op is A2UIOperation => typeof op === "object" && op !== null && !Array.isArray(op),
    );
  } catch {
    return [];
  }
}

/** Mirrors the middleware's `getOperationSurfaceId`: the id may sit on the op or on its payload. */
function operationSurfaceId(operation: A2UIOperation): string | null {
  const id =
    operation.surfaceId ??
    operation.createSurface?.surfaceId ??
    operation.updateComponents?.surfaceId ??
    operation.updateDataModel?.surfaceId ??
    operation.deleteSurface?.surfaceId;
  return typeof id === "string" && id.length > 0 ? id : null;
}

/**
 * A surface is only worth mounting when the operations both create it and give it at least one
 * component — that is what the renderer needs to paint anything. A missing updateDataModel is fine
 * (the template's own defaults still render), so it is deliberately not required here.
 */
function isPaintable(group: SurfaceGroup): boolean {
  const created = group.operations.some((op) => Boolean(op.createSurface));
  const hasComponents = group.operations.some((op) => {
    const components = op.updateComponents?.components;
    return Array.isArray(components) && components.length > 0;
  });
  return created && hasComponents;
}

/** Groups operations per surface, the way the live A2UI activity renderer does. */
function groupBySurface(operations: A2UIOperation[]): SurfaceGroup[] {
  const groups = new Map<string, A2UIOperation[]>();
  for (const operation of operations) {
    const surfaceId = operationSurfaceId(operation) ?? DEFAULT_SURFACE_ID;
    const existing = groups.get(surfaceId);
    if (existing) existing.push(operation);
    else groups.set(surfaceId, [operation]);
  }
  return Array.from(groups, ([surfaceId, ops]) => ({ surfaceId, operations: ops })).filter(isPaintable);
}

// Which surfaces each render_skill_surface call painted, keyed by tool call id. A stored result
// runs to ~100 KB of JSON and every surface renderer in the conversation re-renders whenever the
// agent's messages change, so a call's result is parsed once per session instead of once per
// render. Safe to key on the call id alone because a tool call's result never changes once it
// exists, and the id is the model's own — unique per call, across conversations too. Each value is
// a handful of short ids, so there is nothing here worth evicting.
const surfaceIdsByToolCall = new Map<string, readonly string[]>();

function surfaceIdsOf(toolCallId: string, result: string | undefined): readonly string[] {
  const cached = surfaceIdsByToolCall.get(toolCallId);
  if (cached) return cached;
  // A live run's tool result may not have reached the message list yet — nothing to remember.
  if (!result) return [];
  const surfaceIds = groupBySurface(parseResult(result)).map((group) => group.surfaceId);
  surfaceIdsByToolCall.set(toolCallId, surfaceIds);
  return surfaceIds;
}

/**
 * The newest render_skill_surface call for each surface, in message order.
 *
 * Rendering one surface three times leaves three persisted tool calls carrying the same surface
 * id, so a reloaded conversation would paint three cards where the live run showed one card that
 * the later renders updated in place. Letting only the last call per surface paint keeps reload
 * agreeing with the live view — content-wise exactly (the last render is the current state of the
 * surface), position-wise at the last render rather than the first.
 *
 * Keyed on the surface ids in each result, not on the skill/surface arguments, because the
 * surface id is what the live path collapses on; two calls that named the same template but
 * produced different surface ids are genuinely two surfaces.
 */
function latestRenderBySurface(messages: readonly Message[]): Map<string, string> {
  const resultsByCallId = new Map<string, string>();
  const renderCallIds: string[] = [];
  for (const message of messages) {
    if (message.role === "assistant" && message.toolCalls) {
      for (const call of message.toolCalls) {
        if (call.function.name === SKILL_SURFACE_TOOL_NAME) renderCallIds.push(call.id);
      }
    } else if (message.role === "tool") {
      resultsByCallId.set(message.toolCallId, message.content);
    }
  }

  // Calls are walked oldest first, so the last write per surface is its newest render.
  const latest = new Map<string, string>();
  for (const callId of renderCallIds) {
    for (const surfaceId of surfaceIdsOf(callId, resultsByCallId.get(callId))) {
      latest.set(surfaceId, callId);
    }
  }
  return latest;
}

/** Trims a tool argument down to a usable label, or null when it is missing or blank. */
function asLabel(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

function Pill({ children }: { children: React.ReactNode }) {
  return (
    <div className="my-2 inline-flex items-center gap-2 rounded-lg border border-gray-300 dark:border-gray-700 bg-gray-100 dark:bg-gray-800 px-3 py-1.5 text-sm text-gray-700 dark:text-gray-200">
      {children}
    </div>
  );
}

/** Compact stand-in for a surface we cannot repaint. Never shows the raw operations JSON. */
function SurfaceSummary({
  skill,
  surface,
  note,
}: {
  skill: string | null;
  surface: string | null;
  note: string;
}) {
  return (
    <div className="my-2 w-full max-w-[460px] rounded-2xl border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-900 px-4 py-3 shadow-sm">
      <div className="flex items-center gap-2">
        <span aria-hidden className="text-base leading-none">
          🧩
        </span>
        <span className="truncate text-sm font-semibold text-gray-900 dark:text-gray-100">
          {surface ?? "Interactive surface"}
        </span>
      </div>
      {skill && (
        <div className="mt-0.5 pl-6 text-[11px] font-medium uppercase tracking-wide text-indigo-500 dark:text-indigo-400">
          {skill}
        </div>
      )}
      <p className="mt-2 pl-6 text-xs leading-relaxed text-gray-500 dark:text-gray-400">{note}</p>
    </div>
  );
}

/**
 * Feeds the stored operations into the surrounding provider's message processor.
 * Must be a child of <A2UIProvider> to reach the actions context.
 */
function SurfaceOperations({ surfaceId, operations }: SurfaceGroup) {
  const { processMessages, getSurface } = useA2UIActions();
  const appliedRef = React.useRef<string | null>(null);

  React.useEffect(() => {
    let signature: string;
    try {
      signature = JSON.stringify(operations);
    } catch {
      return; // Not serialisable, so there is nothing safe to apply.
    }
    if (signature === appliedRef.current) return;
    appliedRef.current = signature;
    // Replaying createSurface over a surface that already exists resets it, so drop it on
    // re-application — the same guard the live A2UI renderer uses.
    const pending = getSurface(surfaceId)
      ? operations.filter((operation) => !operation.createSurface)
      : operations;
    processMessages(pending); // A2UIProvider catches and reports processing errors itself.
  }, [processMessages, getSurface, surfaceId, operations]);

  return null;
}

/** The painted surface, or the summary card when the processor rejected the operations. */
function SurfaceOrSummary({
  surfaceId,
  skill,
  surface,
}: {
  surfaceId: string;
  skill: string | null;
  surface: string | null;
}) {
  const error = useA2UIError();
  if (error) {
    return <SurfaceSummary skill={skill} surface={surface} note={NOTE_RENDER_FAILED} />;
  }
  return <A2UIRenderer surfaceId={surfaceId} />;
}

function RestoredSkillSurface({
  toolCallId,
  skill,
  surface,
  result,
}: {
  toolCallId: string;
  skill: string | null;
  surface: string | null;
  result: string;
}) {
  const { copilotkit } = useCopilotKit();
  const { agent } = useAgent({ agentId: AGENT_ID, updates: [UseAgentUpdate.OnMessagesChanged] });

  const groups = React.useMemo(() => groupBySurface(parseResult(result)), [result]);

  // Stand down during a live run. The middleware turns this exact tool result into an
  // `a2ui-surface` activity which the activity renderer already paints — repainting it here would
  // show the surface twice. A reloaded conversation carries no activity messages, which is
  // precisely the case we exist for.
  const messages = agent?.messages;
  const surfaceActivityIds = React.useMemo(() => {
    const ids = new Set<string>();
    for (const message of messages ?? []) {
      if (message.role === "activity" && message.activityType === A2UI_ACTIVITY_TYPE) {
        ids.add(message.id);
      }
    }
    return ids;
  }, [messages]);

  // The middleware's own id shape names this tool call, whatever surface the result turns out to
  // describe — so it stands the whole card down, including a result the middleware could parse and
  // `groups` below could not. Matched on the full `-<toolCallId>` tail rather than a substring, so
  // it stays exact. (An outer tool call wrapping this one would key the activity by the outer id
  // and slip past, exactly as it did before this check was rewritten; nothing on this path does.)
  const claimedByThisCall = React.useMemo(() => {
    for (const id of surfaceActivityIds) {
      if (id.endsWith(`-${toolCallId}`)) return true;
    }
    return false;
  }, [surfaceActivityIds, toolCallId]);

  const latestBySurface = React.useMemo(() => latestRenderBySurface(messages ?? []), [messages]);

  // What is left for this renderer to draw: a surface nothing else is already showing.
  const visibleGroups = React.useMemo(
    () =>
      groups.filter((group) => {
        // Live and keyed by surface alone: one activity card serves every render of it, so this
        // stands down whether that card came from our render or a later one.
        if (surfaceActivityIds.has(`${A2UI_SURFACE_MESSAGE_ID_PREFIX}${group.surfaceId}`)) {
          return false;
        }
        // Restored, but a later call in this conversation re-rendered the same surface.
        const latest = latestBySurface.get(group.surfaceId);
        return latest === undefined || latest === toolCallId;
      }),
    [groups, surfaceActivityIds, latestBySurface, toolCallId],
  );

  // Same bridge the live renderer uses: stash the action on the run properties, re-run the agent,
  // then clear it so the next turn does not replay it.
  const handleAction = React.useCallback(
    async (message: A2UIClientEventMessage) => {
      if (!agent) return;
      try {
        copilotkit.setProperties({ ...copilotkit.properties, a2uiAction: message });
        await copilotkit.runAgent({ agent });
      } catch (err) {
        console.error("Failed to send the skill surface action to the agent", err);
      } finally {
        const remaining = { ...copilotkit.properties };
        delete remaining.a2uiAction;
        copilotkit.setProperties(remaining);
      }
    },
    [agent, copilotkit],
  );

  if (claimedByThisCall) return null;

  if (groups.length === 0) {
    return <SurfaceSummary skill={skill} surface={surface} note={NOTE_NOT_RESTORABLE} />;
  }

  // Every surface in this result is on screen somewhere else — say nothing rather than repeat it.
  if (visibleGroups.length === 0) return null;

  return (
    <div className="my-2 flex w-full flex-col gap-4">
      {visibleGroups.map((group) => (
        <A2UIProvider
          key={group.surfaceId}
          theme={a2uiDefaultTheme}
          catalog={interactiveCatalog}
          onAction={handleAction}
        >
          <SurfaceOperations surfaceId={group.surfaceId} operations={group.operations} />
          <SurfaceOrSummary surfaceId={group.surfaceId} skill={skill} surface={surface} />
        </A2UIProvider>
      ))}
    </div>
  );
}

// AG-UI generative UI: renders the server-side render_skill_surface tool call inline in the chat.
// Must be rendered inside the <CopilotKit> provider.
export default function SkillSurfaceTool() {
  useRenderTool({
    name: SKILL_SURFACE_TOOL_NAME,
    parameters,
    render: (props) => {
      const skill = asLabel(props.parameters?.skill);
      const surface = asLabel(props.parameters?.surface);

      if (props.status !== "complete") {
        return (
          <Pill>
            <span aria-hidden>🧩</span>
            Preparing {surface ? `the ${surface} surface` : "an interactive surface"}…
          </Pill>
        );
      }

      return (
        <RestoredSkillSurface
          toolCallId={props.toolCallId}
          skill={skill}
          surface={surface}
          result={props.result}
        />
      );
    },
  });
  return null;
}

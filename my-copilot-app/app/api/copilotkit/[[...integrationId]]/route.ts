// app/api/copilotkit/[[...integrationId]]/route.ts

import { NextRequest, NextResponse } from "next/server";
import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";
import { A2UIMiddleware } from "@ag-ui/a2ui-middleware";

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

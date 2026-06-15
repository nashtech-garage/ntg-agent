// app/api/copilotkit/[[...integrationId]]/route.ts

import { NextRequest, NextResponse } from "next/server";
import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";

if (process.env.NODE_ENV !== "production") {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
}

const orchestratorUrl =
  process.env.services__ntg_agent_orchestrator__https__0 ??
  process.env.ORCHESTRATOR_URL ??
  "https://localhost:7093";

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
      response.headers.append(
        "Set-Cookie",
        `ntg_session_id=${sessionId}; Path=/; HttpOnly; SameSite=Lax`
      );
    }

    return response;

  } catch (globalError: any) {
    console.error("[Copilot Handler CRITICAL]", globalError);
    return NextResponse.json(
      { error: "Internal Server Error", details: globalError?.message },
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

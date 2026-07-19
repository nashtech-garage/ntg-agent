// Proxies the agent's enabled Agent Skills list (GET) to the Orchestrator.
// Backs the "/" skill picker in the chat input.
import { NextRequest, NextResponse } from "next/server";
import { orchestratorUrl, forwardCookieHeader } from "../../../../../src/utils/orchestrator";

type RouteParams = { params: Promise<{ agentId: string }> };

export async function GET(req: NextRequest, { params }: RouteParams) {
  const { agentId } = await params;
  const upstream = await fetch(`${orchestratorUrl}/api/agents/${agentId}/skills`, {
    headers: { ...forwardCookieHeader(req) },
  });

  if (!upstream.ok) {
    return new NextResponse(null, { status: upstream.status });
  }
  return NextResponse.json(await upstream.json());
}

// Proxies the message history of a conversation (GET) to the Orchestrator.
import { NextRequest, NextResponse } from "next/server";
import { orchestratorUrl, forwardCookieHeader } from "../../../../../src/utils/orchestrator";

type RouteParams = { params: Promise<{ id: string }> };

export async function GET(req: NextRequest, { params }: RouteParams) {
  const { id } = await params;
  const search = new URL(req.url).search; // optional ?currentSessionId=...
  const upstream = await fetch(`${orchestratorUrl}/api/conversations/${id}/messages${search}`, {
    headers: { ...forwardCookieHeader(req) },
  });

  if (!upstream.ok) {
    return new NextResponse(null, { status: upstream.status });
  }
  return NextResponse.json(await upstream.json());
}

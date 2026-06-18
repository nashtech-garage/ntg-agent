// Returns the current user (or 401) by forwarding the Identity cookie to the Orchestrator.
import { NextRequest, NextResponse } from "next/server";
import { orchestratorUrl, forwardCookieHeader } from "../../../../src/utils/orchestrator";

export async function GET(req: NextRequest) {
  const upstream = await fetch(`${orchestratorUrl}/api/account/me`, {
    headers: { ...forwardCookieHeader(req) },
  });

  if (!upstream.ok) {
    return new NextResponse(null, { status: upstream.status });
  }

  const data = await upstream.json();
  return NextResponse.json(data);
}

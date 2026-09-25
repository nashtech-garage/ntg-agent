// Proxies the conversation list (GET) and create (POST) to the Orchestrator, forwarding the cookie.
import { NextRequest, NextResponse } from "next/server";
import { orchestratorUrl, forwardCookieHeader } from "../../../src/utils/orchestrator";

export async function GET(req: NextRequest) {
  const search = new URL(req.url).search;
  const upstream = await fetch(`${orchestratorUrl}/api/conversations${search}`, {
    headers: { ...forwardCookieHeader(req) },
  });

  if (!upstream.ok) {
    return new NextResponse(null, { status: upstream.status });
  }
  return NextResponse.json(await upstream.json());
}

export async function POST(req: NextRequest) {
  const search = new URL(req.url).search;
  const upstream = await fetch(`${orchestratorUrl}/api/conversations${search}`, {
    method: "POST",
    headers: { ...forwardCookieHeader(req) },
  });

  if (!upstream.ok) {
    return new NextResponse(null, { status: upstream.status });
  }
  return NextResponse.json(await upstream.json());
}

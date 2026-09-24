// Proxies logout to the Orchestrator and relays the cookie-clearing Set-Cookie to the browser.
import { NextRequest, NextResponse } from "next/server";
import { orchestratorUrl, forwardCookieHeader, relaySetCookies } from "../../../../src/utils/orchestrator";

export async function POST(req: NextRequest) {
  const upstream = await fetch(`${orchestratorUrl}/api/account/logout`, {
    method: "POST",
    headers: { ...forwardCookieHeader(req) },
  });

  const response = new NextResponse(null, { status: upstream.status });
  relaySetCookies(upstream, response);
  return response;
}

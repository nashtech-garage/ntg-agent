// Proxies login to the Orchestrator and relays the Identity cookie back to the browser.
import { NextRequest, NextResponse } from "next/server";
import { orchestratorUrl, relaySetCookies } from "../../../../src/utils/orchestrator";

export async function POST(req: NextRequest) {
  const body = await req.text();

  const upstream = await fetch(`${orchestratorUrl}/api/account/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body,
  });

  const payload = await upstream.text();
  const response = new NextResponse(payload, {
    status: upstream.status,
    headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json" },
  });

  // On success the Orchestrator sets `.AspNetCore.Identity.Application`; forward it to the browser.
  relaySetCookies(upstream, response);
  return response;
}

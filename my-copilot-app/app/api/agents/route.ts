// app/api/agents/route.ts
import { NextRequest } from "next/server";

if (process.env.NODE_ENV !== "production") {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
}

const orchestratorUrl =
  process.env.services__ntg_agent_orchestrator__https__0 ??
  process.env.ORCHESTRATOR_URL ??
  "https://localhost:7093";

export async function GET(req: NextRequest) {
  const cookieHeader = req.headers.get("cookie") ?? "";
  const res = await fetch(`${orchestratorUrl}/api/agents`, {
    headers: { ...(cookieHeader ? { Cookie: cookieHeader } : {}) },
  });
  console.log("[agents] orchestratorUrl:", orchestratorUrl);
  console.log("[agents] cookieHeader:", cookieHeader);
  const data = await res.json();
  return Response.json(data);
}
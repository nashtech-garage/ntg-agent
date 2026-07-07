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
  // Forwarded so the backend can identify the user; never log it — it carries auth cookies.
  const cookieHeader = req.headers.get("cookie") ?? "";
  const res = await fetch(`${orchestratorUrl}/api/agents`, {
    headers: { ...(cookieHeader ? { Cookie: cookieHeader } : {}) },
  });
  const data = await res.json();
  return Response.json(data);
}
import { NextRequest } from "next/server";

if (process.env.NODE_ENV !== "production") {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
}

const orchestratorUrl =
  process.env.services__ntg_agent_orchestrator__https__0 ??
  process.env.ORCHESTRATOR_URL ??
  "https://localhost:7093";

const conversationStore = new Map<string, string>();

// Cache the default agent ID so we don't fetch it on every request
let cachedDefaultAgentId: string | null = null;

async function getDefaultAgentId(cookieHeader: string): Promise<string> {
  if (cachedDefaultAgentId) return cachedDefaultAgentId;

  const res = await fetch(`${orchestratorUrl}/api/agents`, {
    headers: { ...(cookieHeader ? { Cookie: cookieHeader } : {}) },
  });

  if (!res.ok) throw new Error(`Failed to fetch agents: ${res.status}`);

  const agents: { id: string; name: string; isDefault: boolean; mode: string }[] = await res.json();
  if (!agents.length) throw new Error("No published agents available");

  // Match the C# fallback logic: prefer isDefault, then first available
  const defaultAgent = agents.find(a => a.isDefault) ?? agents[0];
  cachedDefaultAgentId = defaultAgent.id;

  console.log("[ntg] Resolved default agent:", defaultAgent.name, defaultAgent.id);
  return cachedDefaultAgentId;
}

async function createConversation(sessionId: string, cookieHeader: string): Promise<string> {
  const res = await fetch(
    `${orchestratorUrl}/api/conversations?currentSessionId=${encodeURIComponent(sessionId)}`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(cookieHeader ? { Cookie: cookieHeader } : {}),
      },
    }
  );
  if (!res.ok) throw new Error(`Create conversation failed: ${res.status}`);
  const conv = await res.json();
  const id = conv.id ?? conv.Id;
  if (!id) throw new Error("No id in conversation response");
  return id;
}

export async function POST(req: NextRequest) {
  // 1. Extract both message and agentId from the incoming JSON payload
  const { message, agentId: requestAgentId } = await req.json();
  const cookieHeader = req.headers.get("cookie") ?? "";

  // Resolve sessionId from cookie
  const sessionCookieMatch = cookieHeader.match(/ntg_session_id=([^;]+)/);
  const sessionId = sessionCookieMatch?.[1] ?? crypto.randomUUID();

  // Resolve conversationId
  let conversationId = conversationStore.get(sessionId);
  if (!conversationId) {
    conversationId = await createConversation(sessionId, cookieHeader);
    conversationStore.set(sessionId, conversationId);
  }

  // 2. Use the client's selected agentId if provided; otherwise, fetch/fallback to default
  let agentId: string;
  if (requestAgentId) {
    agentId = requestAgentId;
  } else {
    try {
      agentId = await getDefaultAgentId(cookieHeader);
    } catch (err) {
      console.error("[ntg] Could not resolve agent ID:", err);
      return new Response(`Could not resolve agent: ${String(err)}`, { status: 502 });
    }
  }

  console.log("[ntg] Using agentId:", agentId);
  console.log("[ntg] ConversationId:", conversationId);
  console.log("[ntg] SessionId:", sessionId);

  // Forward to orchestrator
  const formData = new FormData();
  formData.append("Prompt", message);
  formData.append("ConversationId", conversationId);
  formData.append("SessionId", sessionId);
  formData.append("AgentId", agentId);

  const upstream = await fetch(`${orchestratorUrl}/api/agents/chat`, {
    method: "POST",
    body: formData,
    headers: { ...(cookieHeader ? { Cookie: cookieHeader } : {}) },
  });

  if (!upstream.ok || !upstream.body) {
    return new Response(`Orchestrator error: ${upstream.status}`, { status: 502 });
  }

  // Set session cookie if new
  const headers = new Headers({ "Content-Type": "application/json" });
  if (!sessionCookieMatch) {
    headers.append("Set-Cookie", `ntg_session_id=${sessionId}; Path=/; HttpOnly; SameSite=Lax`);
  }

  return new Response(upstream.body, { status: 200, headers });
}
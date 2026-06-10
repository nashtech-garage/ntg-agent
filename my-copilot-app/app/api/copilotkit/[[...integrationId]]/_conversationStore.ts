// app/api/copilotkit/[integrationId]/_conversationStore.ts

export const orchestratorUrl =
  process.env.services__ntg_agent_orchestrator__https__0 ??
  process.env.ORCHESTRATOR_URL ??
  "https://localhost:7093";

// Thread conversation map caching layer instance
export const conversationStore = new Map<string, string>();

/**
 * Executes a handshake configuration call to create an active tracking conversation session.
 */
export async function createConversation(sessionId: string, cookieHeader: string): Promise<string> {
  console.log(`[Orchestrator Sync] Initializing conversation thread for session: ${sessionId}`);
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

/**
 * Checks an anonymous session profile status to protect against system load bursts.
 */
export async function checkRateLimitStatus(sessionId: string, cookieHeader: string): Promise<boolean> {
  try {
    const rlRes = await fetch(
      `${orchestratorUrl}/api/conversations/anonymous/rate-limit-status?sessionId=${encodeURIComponent(sessionId)}`,
      { headers: { ...(cookieHeader ? { Cookie: cookieHeader } : {}) } }
    );
    if (rlRes.ok) {
      const rl = await rlRes.json();
      return rl.canSendMessage ?? rl.CanSendMessage ?? true;
    }
  } catch (err) {
    console.error("[RateLimit Warning] Could not fetch status validation flags:", err);
  }
  return true;
}
// Shared helpers for proxying requests from the Next.js app to the .NET Orchestrator.
// All browser <-> Orchestrator traffic flows through Next.js API routes so that the
// ASP.NET Identity cookie can be stored on (and replayed from) the Next.js origin.

import { NextRequest } from "next/server";

// In dev the Orchestrator runs on a self-signed https cert; allow it.
if (process.env.NODE_ENV !== "production") {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
}

export const orchestratorUrl =
  process.env.services__ntg_agent_orchestrator__https__0 ??
  process.env.ORCHESTRATOR_URL ??
  "https://localhost:7093";

/** Reads the incoming cookie header so it can be forwarded to the Orchestrator. */
export function forwardCookieHeader(req: NextRequest): Record<string, string> {
  const cookie = req.headers.get("cookie") ?? "";
  return cookie ? { Cookie: cookie } : {};
}

/**
 * Rewrites Set-Cookie attributes so cookies issued by the Orchestrator (https, possibly
 * domain-scoped) are accepted by the browser on the Next.js dev origin (http://localhost:3000).
 * In dev we strip `Secure` (Next dev is http) and any `Domain` attribute. Identity may emit
 * multiple/chunked cookies, so this is applied to each Set-Cookie value.
 */
export function rewriteSetCookieForBrowser(setCookie: string): string {
  const isProd = process.env.NODE_ENV === "production";
  return setCookie
    .split(";")
    .map((part) => part.trim())
    .filter((part) => {
      const lower = part.toLowerCase();
      if (lower.startsWith("domain=")) return false; // host-only on the Next.js origin
      if (!isProd && lower === "secure") return false; // dev is http
      return true;
    })
    .join("; ");
}

/**
 * Copies all Set-Cookie headers from an Orchestrator response onto the outgoing response,
 * rewriting each for the browser. Uses getSetCookie() to capture chunked Identity cookies.
 */
export function relaySetCookies(from: Response, to: Response): void {
  const setCookies = from.headers.getSetCookie?.() ?? [];
  for (const cookie of setCookies) {
    to.headers.append("Set-Cookie", rewriteSetCookieForBrowser(cookie));
  }
}

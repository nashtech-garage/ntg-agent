// Proxies rename (PUT) and delete (DELETE) of a single conversation to the Orchestrator.
import { NextRequest, NextResponse } from "next/server";
import { orchestratorUrl, forwardCookieHeader } from "../../../../src/utils/orchestrator";

type RouteParams = { params: Promise<{ id: string }> };

export async function DELETE(req: NextRequest, { params }: RouteParams) {
  const { id } = await params;
  const upstream = await fetch(`${orchestratorUrl}/api/conversations/${id}`, {
    method: "DELETE",
    headers: { ...forwardCookieHeader(req) },
  });
  return new NextResponse(null, { status: upstream.status });
}

export async function PUT(req: NextRequest, { params }: RouteParams) {
  const { id } = await params;
  const search = new URL(req.url).search; // expects ?newName=...
  const upstream = await fetch(`${orchestratorUrl}/api/conversations/${id}/rename${search}`, {
    method: "PUT",
    headers: { ...forwardCookieHeader(req) },
  });
  return new NextResponse(null, { status: upstream.status });
}

"use client";

import { useCallback, useEffect, useState } from "react";

export interface ConversationItem {
  id: string;
  name: string;
  sessionId: string | null;
}

interface ConversationListResponse {
  items: ConversationItem[];
  pageNumber: number;
  hasMore: boolean;
}

interface Props {
  /** The session id (== CopilotKit threadId) of the active conversation, for highlighting. */
  activeSessionId: string | null;
  /** Bump this to force a reload (e.g. after an assistant run finishes). */
  reloadSignal: number;
  onSelect: (sessionId: string | null, conversationId: string) => void;
  onNewChat: () => void;
}

const PAGE_SIZE = 20;

export default function ConversationSidebar({
  activeSessionId,
  reloadSignal,
  onSelect,
  onNewChat,
}: Props) {
  const [conversations, setConversations] = useState<ConversationItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [menuFor, setMenuFor] = useState<string | null>(null);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");

  const load = useCallback(async (pageNumber: number, append: boolean) => {
    try {
      if (!append) setLoading(true);
      const res = await fetch(
        `/api/conversations?pageNumber=${pageNumber}&pageSize=${PAGE_SIZE}`,
        { cache: "no-store" }
      );
      if (!res.ok) return;
      const data: ConversationListResponse = await res.json();
      setConversations((prev) => (append ? [...prev, ...data.items] : data.items));
      setPage(data.pageNumber);
      setHasMore(data.hasMore);
    } finally {
      setLoading(false);
    }
  }, []);

  // Reload page 1 on mount and whenever the parent signals a change.
  useEffect(() => {
    load(1, false);
  }, [load, reloadSignal]);

  async function handleDelete(id: string) {
    setMenuFor(null);
    if (!window.confirm("Delete this conversation? This cannot be undone.")) return;
    const res = await fetch(`/api/conversations/${id}`, { method: "DELETE" });
    if (res.ok) {
      const deleted = conversations.find((c) => c.id === id);
      setConversations((prev) => prev.filter((c) => c.id !== id));
      if (deleted && deleted.sessionId && deleted.sessionId === activeSessionId) onNewChat();
    }
  }

  function startRename(c: ConversationItem) {
    setMenuFor(null);
    setRenamingId(c.id);
    setRenameValue(c.name);
  }

  async function commitRename(id: string) {
    const name = renameValue.trim();
    setRenamingId(null);
    if (!name) return;
    const res = await fetch(`/api/conversations/${id}?newName=${encodeURIComponent(name)}`, {
      method: "PUT",
    });
    if (res.ok) {
      setConversations((prev) => prev.map((c) => (c.id === id ? { ...c, name } : c)));
    }
  }

  return (
    <aside className="flex h-full w-64 flex-col border-r border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-800">
      <div className="p-3">
        <button
          onClick={onNewChat}
          className="flex w-full items-center justify-center gap-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-900 px-3 py-2 text-sm font-medium text-gray-800 dark:text-gray-100 hover:bg-gray-100 dark:hover:bg-gray-700"
        >
          <span className="text-lg leading-none">+</span> New chat
        </button>
      </div>

      <div className="px-3 pb-1 text-xs font-semibold uppercase tracking-wide text-gray-400">
        Chat history
      </div>

      <div className="flex-1 overflow-y-auto px-2 pb-3">
        {loading ? (
          <div className="px-2 py-3 text-sm text-gray-400">Loading…</div>
        ) : conversations.length === 0 ? (
          <div className="px-2 py-3 text-sm text-gray-400">No conversations yet</div>
        ) : (
          <ul className="space-y-0.5">
            {conversations.map((c) => {
              const active = c.sessionId != null && c.sessionId === activeSessionId;
              return (
                <li key={c.id} className="group relative">
                  {renamingId === c.id ? (
                    <input
                      autoFocus
                      value={renameValue}
                      onChange={(e) => setRenameValue(e.target.value)}
                      onBlur={() => commitRename(c.id)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") commitRename(c.id);
                        if (e.key === "Escape") setRenamingId(null);
                      }}
                      className="w-full rounded-md border border-blue-400 bg-white dark:bg-gray-900 px-2 py-1.5 text-sm text-gray-900 dark:text-white focus:outline-none"
                    />
                  ) : (
                    <button
                      onClick={() => onSelect(c.sessionId, c.id)}
                      className={`flex w-full items-center justify-between rounded-md px-2 py-1.5 text-left text-sm ${
                        active
                          ? "bg-blue-100 dark:bg-blue-900/40 text-blue-900 dark:text-blue-100"
                          : "text-gray-700 dark:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700"
                      }`}
                    >
                      <span className="truncate">{c.name}</span>
                      <span
                        role="button"
                        tabIndex={0}
                        onClick={(e) => {
                          e.stopPropagation();
                          setMenuFor(menuFor === c.id ? null : c.id);
                        }}
                        className="ml-1 hidden shrink-0 px-1 text-gray-400 hover:text-gray-700 dark:hover:text-gray-100 group-hover:inline"
                      >
                        ⋯
                      </span>
                    </button>
                  )}

                  {menuFor === c.id && (
                    <div className="absolute right-2 top-8 z-10 w-32 rounded-md border border-gray-200 dark:border-gray-600 bg-white dark:bg-gray-800 py-1 shadow-lg">
                      <button
                        onClick={() => startRename(c)}
                        className="block w-full px-3 py-1.5 text-left text-sm text-gray-700 dark:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-700"
                      >
                        Rename
                      </button>
                      <button
                        onClick={() => handleDelete(c.id)}
                        className="block w-full px-3 py-1.5 text-left text-sm text-red-600 hover:bg-gray-100 dark:hover:bg-gray-700"
                      >
                        Delete
                      </button>
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        )}

        {hasMore && !loading && (
          <button
            onClick={() => load(page + 1, true)}
            className="mt-2 w-full rounded-md px-2 py-1.5 text-sm text-gray-500 hover:bg-gray-100 dark:hover:bg-gray-700"
          >
            Load more
          </button>
        )}
      </div>
    </aside>
  );
}

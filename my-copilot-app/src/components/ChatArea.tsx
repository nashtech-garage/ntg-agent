"use client";

import { RefObject } from "react";

export interface Message {
  role: "user" | "assistant";
  content: string;
}

interface ChatAreaProps {
  messages: Message[];
  loading: boolean;
  bottomRef: RefObject<HTMLDivElement | null>;
}

export default function ChatArea({ messages, loading, bottomRef }: ChatAreaProps) {
  return (
    <main className="flex-1 overflow-y-auto px-4 py-6 space-y-4 max-w-3xl mx-auto w-full">
      {messages.map((msg, i) => (
        <div key={i} className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}>
          <div className={`max-w-[80%] px-4 py-3 rounded-2xl text-sm whitespace-pre-wrap ${
            msg.role === "user"
              ? "bg-blue-600 text-white rounded-br-sm"
              : "bg-gray-100 dark:bg-gray-800 text-gray-900 dark:text-white rounded-bl-sm"
          }`}>
            {msg.content || (loading && msg.role === "assistant" ? "▌" : "")}
          </div>
        </div>
      ))}
      <div ref={bottomRef} />
    </main>
  );
}
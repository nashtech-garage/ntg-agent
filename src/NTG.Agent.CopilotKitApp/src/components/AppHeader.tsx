"use client";

import AuthControls from "./AuthControls";

/** Top navigation bar: app title on the left, auth controls on the right. */
export default function AppHeader({ title = "NTG Agent" }: { title?: string }) {
  return (
    <header className="w-full bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 shadow-sm z-50">
      <div className="flex items-center justify-between px-4 py-3">
        <h1 className="text-xl font-semibold">{title}</h1>
        <AuthControls />
      </div>
    </header>
  );
}

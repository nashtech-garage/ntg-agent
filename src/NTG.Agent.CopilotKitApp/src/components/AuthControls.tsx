"use client";

import Link from "next/link";
import { useAuth } from "../auth/AuthProvider";

/** Navbar auth area: shows the signed-in user + Logout, or a Sign in link. */
export default function AuthControls() {
  const { user, logout } = useAuth();

  return (
    <div className="flex items-center gap-3 text-sm">
      {user ? (
        <>
          <span className="text-gray-500 dark:text-gray-400">{user.email ?? user.userName}</span>
          <button
            onClick={logout}
            className="rounded-md border border-gray-300 dark:border-gray-600 px-3 py-1.5 font-medium hover:bg-gray-100 dark:hover:bg-gray-700"
          >
            Logout
          </button>
        </>
      ) : (
        <Link
          href="/login"
          className="rounded-md bg-blue-600 px-3 py-1.5 font-medium text-white hover:bg-blue-700"
        >
          Sign in
        </Link>
      )}
    </div>
  );
}

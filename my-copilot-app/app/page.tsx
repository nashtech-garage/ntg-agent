// "use client";

// import { CopilotSidebar } from "@copilotkit/react-core/v2"; 

// export default function Page() {
//   return (
//     <main>
//       <h1>Your App</h1>
//       <CopilotSidebar />
//     </main>
//   );
// }


"use client";

import { CopilotKit } from "@copilotkit/react-core/v2";
import { CopilotChat } from "@copilotkit/react-core/v2";
// ĐỔI TẠI ĐÂY: Thay CopilotSidebar bằng CopilotPopup
import { CopilotPopup } from "@copilotkit/react-core/v2"; 
import { useState } from "react";

export default function Page() {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  const toggleMobileMenu = () => {
    setMobileMenuOpen(!mobileMenuOpen);
  };

  return (
    <CopilotKit runtimeUrl="/api/copilotkit">
      <div className="flex flex-col h-screen bg-white dark:bg-gray-900 text-gray-900 dark:text-white">
        
        {/* Header */}
        <header className="w-full bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 shadow-sm z-50">
          <div className="flex items-center justify-between px-4 py-3">
            <div className="flex items-center gap-3">
              <button
                onClick={toggleMobileMenu}
                className="md:hidden p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-700 transition-colors"
                aria-label="Toggle menu"
              >
                <svg
                  className="w-6 h-6"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M4 6h16M4 12h16M4 18h16"
                  />
                </svg>
              </button>
              <h1 className="text-xl font-semibold">NTG Agent</h1>
            </div>
            <nav className="hidden md:flex items-center gap-6">
              <a
                href="#home"
                className="hover:text-blue-600 dark:hover:text-blue-400 transition-colors"
              >
                Home
              </a>
              <a
                href="#account"
                className="hover:text-blue-600 dark:hover:text-blue-400 transition-colors"
              >
                Account
              </a>
              <button className="px-4 py-2 bg-blue-600 hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600 text-white rounded-lg transition-colors">
                Logout
              </button>
            </nav>
          </div>
        </header>

        {/* Mobile menu overlay */}
        {mobileMenuOpen && (
          <div
            className="fixed inset-0 bg-black/50 md:hidden z-30"
            onClick={() => setMobileMenuOpen(false)}
          />
        )}

        {/* Main content area */}
        <div className="flex flex-1 overflow-hidden relative">
          
          {/* Main content */}
          <main className="flex-1 overflow-auto">
            <article className="container-fluid p-4 md:p-6 max-w-7xl mx-auto">
              <div className="space-y-6">
                <section>
                  <h2 className="text-3xl font-bold mb-4">Welcome to NTG Agent</h2>
                  <p className="text-gray-600 dark:text-gray-400 max-w-2xl">
                    This is your AI agent interface. Click on the chat icon at the bottom right corner to start a conversation with the assistant.
                  </p>
                </section>

                <section className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div className="p-4 bg-blue-50 dark:bg-blue-900/20 rounded-lg border border-blue-200 dark:border-blue-800">
                    <h3 className="font-semibold mb-2 text-blue-900 dark:text-blue-200">
                      💬 Chat
                    </h3>
                    <p className="text-sm text-blue-700 dark:text-blue-300">
                      Start a new conversation with the AI assistant.
                    </p>
                  </div>
                  <div className="p-4 bg-green-50 dark:bg-green-900/20 rounded-lg border border-green-200 dark:border-green-800">
                    <h3 className="font-semibold mb-2 text-green-900 dark:text-green-200">
                      📁 Documents
                    </h3>
                    <p className="text-sm text-green-700 dark:text-green-300">
                      Upload and manage your documents.
                    </p>
                  </div>
                </section>
              </div>
            </article>
          </main>
        </div>

        {/* 

           CHATBOX HERE:
        */}
        <CopilotChat 
          labels={{
            // chatbox attributes
            welcomeMessageText: "Xin chào! Tôi là NTG Assistant. Tôi có thể giúp gì cho bạn hôm nay?",
            chatInputPlaceholder: "Nhập tin nhắn...", 
          }}
        />

      </div>
    </CopilotKit>
  );
}
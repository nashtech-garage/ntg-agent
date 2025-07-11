import React, { useState } from 'react';
import { Copy, Check } from 'lucide-react';

interface CodeBlockProps {
  children: string;
  className?: string;
  language?: string;
}

export const CodeBlock: React.FC<CodeBlockProps> = ({ children, className, language }) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(children);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error('Failed to copy text: ', err);
    }
  };

  const displayLanguage = language || className?.replace('language-', '') || 'text';

  return (
    <div className="relative rounded-lg bg-gray-900 my-4">
      <div className="flex items-center justify-between px-4 py-2 bg-gray-800 rounded-t-lg border-b border-gray-700">
        <span className="text-sm text-gray-300 font-medium capitalize">
          {displayLanguage}
        </span>
        <button
          onClick={handleCopy}
          className="flex items-center gap-1 px-2 py-1 text-sm text-gray-400 hover:text-white transition-colors rounded"
          title="Copy code"
        >
          {copied ? <Check size={16} /> : <Copy size={16} />}
          <span className="text-xs">{copied ? 'Copied!' : 'Copy'}</span>
        </button>
      </div>
      <pre className="overflow-x-auto p-4">
        <code className={`${className} text-sm`}>{children}</code>
      </pre>
    </div>
  );
};

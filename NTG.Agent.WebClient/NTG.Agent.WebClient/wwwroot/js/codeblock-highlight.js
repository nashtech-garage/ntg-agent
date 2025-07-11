window.highlightCodeBlocks = () => {
    document.querySelectorAll('pre code').forEach((el) => {
        hljs.highlightElement(el);
    });
};


window.beautifyCodeBlocks = function () {
    const codeBlocks = document.querySelectorAll('pre code:not(.enhanced)');

    codeBlocks.forEach(codeBlock => {
        const pre = codeBlock.parentElement;
        const language = getLanguageFromClass(codeBlock.className) || codeBlock.getAttribute('data-language') || 'text';

        // Skip if already enhanced
        if (codeBlock.classList.contains('enhanced')) {
            return;
        }

        // Mark as enhanced
        codeBlock.classList.add('enhanced');

        // Create header with language and copy button
        const header = document.createElement('div');
        header.className = 'code-block-header';
        header.innerHTML = `
            <span class="language-label">${language}</span>
            <button class="copy-button" onclick="copyToClipboard(this)" title="Copy code">
                <svg width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                    <path d="M4 1.5H3a2 2 0 0 0-2 2V14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V3.5a2 2 0 0 0-2-2h-1v1h1a1 1 0 0 1 1 1V14a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V3.5a1 1 0 0 1 1-1h1v-1z"/>
                    <path d="M9.5 1a.5.5 0 0 1 .5.5v1a.5.5 0 0 1-.5.5h-3a.5.5 0 0 1-.5-.5v-1a.5.5 0 0 1 .5-.5h3zm-3-1A1.5 1.5 0 0 0 5 1.5v1A1.5 1.5 0 0 0 6.5 4h3A1.5 1.5 0 0 0 11 2.5v-1A1.5 1.5 0 0 0 9.5 0h-3z"/>
                </svg>
                <span class="copy-text">Copy</span>
            </button>
        `;

        // Wrap pre element with container
        const container = document.createElement('div');
        container.className = 'code-block-container';
        pre.parentNode.insertBefore(container, pre);
        container.appendChild(header);
        container.appendChild(pre);

        // Add classes for styling
        pre.classList.add('enhanced-pre');
    });
};

function getLanguageFromClass(className) {
    const match = className.match(/language-(\w+)/);
    return match ? match[1] : null;
}

window.copyToClipboard = async function (button) {
    const container = button.closest('.code-block-container');
    const codeBlock = container.querySelector('code');
    const text = codeBlock.textContent || codeBlock.innerText;

    try {
        await navigator.clipboard.writeText(text);

        // Update button appearance
        const copyText = button.querySelector('.copy-text');
        const originalText = copyText.textContent;

        button.classList.add('copied');
        copyText.textContent = 'Copied!';

        setTimeout(() => {
            button.classList.remove('copied');
            copyText.textContent = originalText;
        }, 2000);
    } catch (err) {
        // Fallback for older browsers
        const textArea = document.createElement('textarea');
        textArea.value = text;
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        document.execCommand('copy');
        document.body.removeChild(textArea);
    }
};


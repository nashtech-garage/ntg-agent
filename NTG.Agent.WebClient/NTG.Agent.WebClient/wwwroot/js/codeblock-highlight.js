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

window.renderMermaidDiagrams = function () {
    if (typeof mermaid === 'undefined') {
        console.warn('Mermaid library not loaded. Please add mermaid.js to render diagrams.');
        return;
    }

    // Find all mermaid code blocks that haven't been processed yet
    // Handle both 'lang-mermaid' and 'language-mermaid' class formats
    const mermaidBlocks = document.querySelectorAll('pre lang-mermaid:not(.enhanced)');
    
    mermaidBlocks.forEach((codeBlock, index) => {
        try {
            const mermaidCode = codeBlock.textContent || codeBlock.innerText;
            const pre = codeBlock.parentElement;
            const codeBlockContainer = pre.parentElement; // Should be .code-block-container
            
            // Mark as processed to avoid re-rendering
            codeBlock.classList.add('enhanced');
            
            // Create container for the diagram (styles now in CSS)
            const diagramContainer = document.createElement('div');
            diagramContainer.className = 'mermaid-diagram-container';
            
            // Create unique ID for this diagram
            const diagramId = `mermaid-diagram-${Date.now()}-${index}`;
            const diagramDiv = document.createElement('div');
            diagramDiv.id = diagramId;
            diagramDiv.className = 'mermaid';
            diagramDiv.textContent = mermaidCode;
            
            // Add header (styles now in CSS)
            const header = document.createElement('div');
            header.className = 'mermaid-header';
            header.innerHTML = `
                <span>📊</span>
                <span>Mermaid Diagram</span>
            `;
            
            diagramContainer.appendChild(header);
            diagramContainer.appendChild(diagramDiv);
            
            // Insert the diagram container after the code block container
            if (codeBlockContainer && codeBlockContainer.classList.contains('code-block-container')) {
                // Insert after the existing code block container
                codeBlockContainer.parentNode.insertBefore(diagramContainer, codeBlockContainer.nextSibling);
                // Hide the code block container using CSS class
                codeBlockContainer.classList.add('mermaid-code-hidden');
            } else {
                // Fallback: insert after the pre element
                pre.parentNode.insertBefore(diagramContainer, pre.nextSibling);
                pre.style.display = 'none';
            }
            
            // Initialize mermaid for this specific diagram
            mermaid.init(undefined, diagramDiv);
            
        } catch (error) {
            console.error('Error rendering Mermaid diagram:', error);
            
            // Show error message (styles now in CSS)
            const errorDiv = document.createElement('div');
            errorDiv.className = 'mermaid-error';
            errorDiv.innerHTML = `
                <strong>⚠️ Mermaid Diagram Error:</strong>
                <span>${error.message || 'Failed to render diagram'}</span>
            `;
            
            const targetElement = codeBlock.parentElement.parentElement || codeBlock.parentElement;
            targetElement.parentNode.insertBefore(errorDiv, targetElement.nextSibling);
        }
    });
};




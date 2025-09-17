window.renderMermaidBlocks = function () {
    const codeBlocks = document.querySelectorAll('pre code:not(.enhanced)');

    codeBlocks.forEach(codeBlock => {
        const pre = codeBlock.parentElement;
        const language = getLanguageFromClass(codeBlock.className) || codeBlock.getAttribute('data-language') || 'text';

        // Skip if already enhanced
        if (codeBlock.classList.contains('enhanced')) {
            return;
        }

        // Only handle mermaid diagrams
        if (language === 'mermaid' && renderMermaidDiagram(codeBlock, pre)) {
            // Mark as enhanced
            codeBlock.classList.add('enhanced');
        }
    });
};

function getLanguageFromClass(className) {
    const match = className.match(/language-(\w+)/);
    return match ? match[1] : null;
}

function renderMermaidDiagram(codeBlock, pre) {
    if (typeof mermaid === 'undefined') {
        return false;
    }
    
    try {
        const mermaidCode = codeBlock.textContent;
        
        // Create header with mermaid label and buttons
        const header = document.createElement('div');
        header.className = 'code-block-header';
        header.innerHTML = `
            <span class="language-label">mermaid</span>
            <div style="display: flex; gap: 8px;">
                <button class="copy-button" onclick="copyMermaidCode(this)" title="Copy mermaid code">
                    <svg width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                        <path d="M4 1.5H3a2 2 0 0 0-2 2V14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V3.5a2 2 0 0 0-2-2h-1v1h1a1 1 0 0 1 1 1V14a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V3.5a1 1 0 0 1 1-1h1v-1z"/>
                        <path d="M9.5 1a.5.5 0 0 1 .5.5v1a.5.5 0 0 1-.5.5h-3a.5.5 0 0 1-.5-.5v-1a.5.5 0 0 1 .5-.5h3zm-3-1A1.5 1.5 0 0 0 5 1.5v1A1.5 1.5 0 0 0 6.5 4h3A1.5 1.5 0 0 0 11 2.5v-1A1.5 1.5 0 0 0 9.5 0h-3z"/>
                    </svg>
                    <span class="copy-text">Copy</span>
                </button>
                <button class="toggle-button" onclick="toggleMermaidView(this)" title="Toggle between code and diagram">
                    <svg width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                        <path d="M8 15A7 7 0 1 1 8 1a7 7 0 0 1 0 14zm0 1A8 8 0 1 0 8 0a8 8 0 0 0 0 16z"/>
                        <path d="m8.93 6.588-2.29.287-.082.38.45.083c.294.07.352.176.288.469l-.738 3.468c-.194.897.105 1.319.808 1.319.545 0 1.178-.252 1.465-.598l.088-.416c-.2.176-.492.246-.686.246-.275 0-.375-.193-.304-.533L8.93 6.588zM9 4.5a1 1 0 1 1-2 0 1 1 0 0 1 2 0z"/>
                    </svg>
                    <span class="toggle-text">Code</span>
                </button>
            </div>
        `;

        // Create mermaid diagram container
        const diagramDiv = document.createElement('div');
        diagramDiv.className = 'mermaid';
        diagramDiv.textContent = mermaidCode;
        
        // Create container for the mermaid block
        const container = document.createElement('div');
        container.className = 'code-block-container';
        pre.parentNode.insertBefore(container, pre);
        container.appendChild(header);
        container.appendChild(diagramDiv);
        
        // Store references for toggling
        diagramDiv.setAttribute('data-mermaid-code', mermaidCode);
        container.appendChild(pre);
        pre.classList.add('mermaid-code-view');
        pre.style.display = 'none';
        
        // Render the diagram
        mermaid.init(undefined, diagramDiv);
        return true;
        
    } catch (error) {
        console.error('Mermaid rendering error:', error);
        return false;
    }
}

window.copyMermaidCode = async function (button) {
    const container = button.closest('.code-block-container');
    const diagramDiv = container.querySelector('.mermaid');
    const mermaidCode = diagramDiv.getAttribute('data-mermaid-code');

    try {
        await navigator.clipboard.writeText(mermaidCode);

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
        textArea.value = mermaidCode;
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        document.execCommand('copy');
        document.body.removeChild(textArea);
    }
};

window.toggleMermaidView = function (button) {
    const container = button.closest('.code-block-container');
    const diagramDiv = container.querySelector('.mermaid');
    const preElement = container.querySelector('.mermaid-code-view');
    const toggleText = button.querySelector('.toggle-text');

    if (diagramDiv.style.display === 'none') {
        // Switch to diagram view
        diagramDiv.style.display = 'block';
        preElement.style.display = 'none';
        toggleText.textContent = 'Code';
    } else {
        // Switch to code view
        diagramDiv.style.display = 'none';
        preElement.style.display = 'block';
        toggleText.textContent = 'Diagram';
    }
};

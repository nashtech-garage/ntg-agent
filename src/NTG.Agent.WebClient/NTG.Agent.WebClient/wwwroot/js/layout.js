function toggleMobileMenu() {
    const sidebarColumn = document.getElementById('sidebarColumn');
    const overlay = document.getElementById('mobileOverlay');
    const sidebar = document.querySelector('.conversation-sidebar');
    const mobileHeader = document.getElementById('mobileHeader');

    if (sidebarColumn && overlay) {
        const isOpening = !sidebarColumn.classList.contains('mobile-open');
        
        if (isOpening) {
            mobileHeader.style.display = 'none';
            sidebarColumn.classList.add('mobile-open');
            overlay.classList.add('show');
            
            if (sidebar) {
                sidebar.style.cssText = `
                    width: 280px !important;
                    transform: none !important;
                    visibility: visible !important;
                    display: flex !important;
                    position: relative !important;
                `;
                
                const hiddenElements = sidebar.querySelectorAll('.app-title, .button-text, .conversation-name, .section-title, .settings-text');
                hiddenElements.forEach(el => {
                    el.style.cssText = `
                        opacity: 1 !important;
                        width: auto !important;
                        overflow: visible !important;
                        display: block !important;
                    `;
                });
                
                const buttonContainer = sidebar.querySelector('.button-container');
                if (buttonContainer) {
                    buttonContainer.style.display = 'flex !important';
                }
            }
        } else {
            sidebarColumn.classList.remove('mobile-open');
            overlay.classList.remove('show');
            mobileHeader.style.display = 'flex';
        }
    }
}

function closeMobileMenu() {
    const sidebarColumn = document.getElementById('sidebarColumn');
    const overlay = document.getElementById('mobileOverlay');

    const mobileHeader = document.getElementById('mobileHeader');
    if (mobileHeader) {
        mobileHeader.style.display = 'flex';
    }

    if (sidebarColumn && overlay) {
        sidebarColumn.classList.remove('mobile-open');
        overlay.classList.remove('show');
    }
}

window.addEventListener('resize', function() {
    const sidebarColumn = document.getElementById('sidebarColumn');
    const overlay = document.getElementById('mobileOverlay');
    const mobileHeader = document.getElementById('mobileHeader');

    if (window.innerWidth > 768) {
        if (sidebarColumn && overlay) {
            sidebarColumn.classList.remove('mobile-open');
            overlay.classList.remove('show');
            if (mobileHeader) {
                mobileHeader.style.display = 'flex';
            }
            const sidebar = sidebarColumn.querySelector('.conversation-sidebar');
            if (sidebar) {
                sidebar.style.cssText = '';
                // Reset forced styles on elements
                const elements = sidebar.querySelectorAll('.app-title, .button-text, .conversation-name, .section-title, .settings-text, .button-container');
                elements.forEach(el => {
                    el.style.cssText = '';
                });
            }
        }
    }
});

// ─── Theme & Accent Color Management ────────────────────────────────────────

/**
 * Applies and persists the given appearance theme.
 * Swaps the Highlight.js stylesheet and re-highlights any existing code blocks.
 * @param {string} theme - "light" or "dark"
 */
window.setTheme = function (theme) {
    localStorage.setItem('ntg-theme', theme);
    document.documentElement.setAttribute('data-bs-theme', theme);

    var hljsLink = document.getElementById('hljs-theme-link');
    if (hljsLink) {
        hljsLink.href = theme === 'dark'
            ? 'https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/styles/a11y-dark.min.css'
            : 'https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/styles/a11y-light.min.css';
    }

    // Re-highlight already-rendered code blocks so colours match the new theme
    if (typeof window.highlightCodeBlocks === 'function') {
        // Small delay to let the new stylesheet load before re-highlighting
        setTimeout(function () { window.highlightCodeBlocks(); }, 50);
    }
};

/**
 * Returns the currently active theme from localStorage.
 * @returns {string} "light" or "dark"
 */
window.getTheme = function () {
    return localStorage.getItem('ntg-theme') || 'light';
};

/**
 * Applies and persists the given accent color.
 * @param {string} accent - "default" | "Violet" | "green" | "yellow" | "orange"
 */
window.setAccentColor = function (accent) {
    localStorage.setItem('ntg-accent', accent);
    if (accent === 'default') {
        document.documentElement.removeAttribute('data-accent');
    } else {
        document.documentElement.setAttribute('data-accent', accent);
    }
};

/**
 * Returns the currently active accent color from localStorage.
 * @returns {string}
 */
window.getAccentColor = function () {
    return localStorage.getItem('ntg-accent') || 'default';
};

/**
 * Clears all app-specific localStorage keys.
 * Called before logout so a subsequent login (or a different user on the same
 * browser) starts with a clean state and picks up their own DB preferences.
 */
window.clearAppStorage = function () {
    localStorage.removeItem('ntg-theme');
    localStorage.removeItem('ntg-accent');
    localStorage.removeItem('sidebar-collapsed');
};

// After Blazor enhanced navigation:
// 1. The head observer in App.razor already restored the hljs-theme-link href.
// 2. Re-strip the `data-highlighted` marker and re-run hljs so code block colours
//    match the restored stylesheet (the previous render may have used a11y-light).
document.addEventListener('blazor:navigated', function () {
    if (typeof hljs === 'undefined' || typeof window.highlightCodeBlocks !== 'function') return;
    // Remove the "already highlighted" marker so hljs will re-process them
    document.querySelectorAll('pre code[data-highlighted]').forEach(function (el) {
        el.removeAttribute('data-highlighted');
    });
    // Small delay to let the restored stylesheet finish loading
    setTimeout(function () { window.highlightCodeBlocks(); }, 80);
});
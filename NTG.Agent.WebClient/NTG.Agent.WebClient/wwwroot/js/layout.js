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
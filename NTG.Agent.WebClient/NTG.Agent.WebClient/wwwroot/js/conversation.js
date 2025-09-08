let dotNetRef = null;
let handler = null;
let originalDisplays = [];

window.registerClickOutsideHandler = function (dotNetHelper) {
    dotNetRef = dotNetHelper;

    handler = function (e) {
        const menus = document.querySelectorAll('.context-menu');
        const toggles = document.querySelectorAll('.menu-toggle');

        let clickedInside = false;
        menus.forEach(menu => {
            if (menu.contains(e.target)) clickedInside = true;
        });
        toggles.forEach(toggle => {
            if (toggle.contains(e.target)) clickedInside = true;
        });

        if (!clickedInside && dotNetRef) {
            dotNetRef.invokeMethodAsync('OnOutsideClick');
        }
    };

    document.addEventListener('click', handler);
}

window.removeClickOutsideHandler = function () {
    document.removeEventListener('click', handler);
    handler = null;
    dotNetRef = null;
}

window.hideInputChatContainer = function () {
    const inputContainer = document.getElementById('inputChatContainer');
    if (inputContainer) {
        inputContainer.style.display = 'none';
    }
    
    const listItems = document.querySelectorAll('.toastui-editor-contents ol li, .toastui-editor-contents ul li');
    originalDisplays = [];
    
    listItems.forEach(item => {
        originalDisplays.push({
            element: item,
            display: item.style.display
        });
        
        item.style.display = 'none';
    });

    const shareConversationButton = document.getElementById('share-conversation-btn');
    if (shareConversationButton) {
        shareConversationButton.style.display = 'none';
    }
}

window.showInputChatContainer = function () {
    const inputContainer = document.getElementById('inputChatContainer');
    if (inputContainer) {
        inputContainer.style.display = '';
    }

    // Restore original display values
    originalDisplays.forEach(item => {
        item.element.style.display = item.display;
    });
    originalDisplays = [];
    
    const shareConversationButton = document.getElementById('share-conversation-btn');
    if (shareConversationButton) {
        shareConversationButton.style.display = '';
    }
}

window.getSidebarState = function() {
    try {
        const state = localStorage.getItem('sidebar-collapsed');
        if (state === null) return false;
        return state === 'true'; 
    } catch (error) {
        console.error('Error getting sidebar state:', error);
        return false;
    }
}

window.isMobileOrTablet = function() {
    return window.innerWidth <= 1024;
}

window.setSidebarState = function(isCollapsed) {
    try {
        localStorage.setItem('sidebar-collapsed', isCollapsed);
        // Only update sidebar state on desktop
        if (!window.isMobileOrTablet()) {
            window.updateSidebarState(isCollapsed);
        }
    } catch (error) {
        console.error('Error setting sidebar state:', error);
    }
}

window.updateSidebarState = function (isCollapsed) {
    if (window.isMobileOrTablet()) {
        console.log('Mobile/tablet detected, skipping desktop sidebar state');
        return;
    }
    
    // Get references to the main elements
    const mainContent = document.getElementById('mainContent') || document.querySelector('main');
    const sidebarCol = document.getElementById('sidebarColumn') || document.querySelector('.sidebar').closest('[class*="col-"]');
    
    if (mainContent && sidebarCol) {
        //console.warn('Updating sidebar state:', isCollapsed ? 'collapsed' : 'expanded');
        
        try {
            // Reset all column classes first to avoid conflicts
            ['col-1', 'col-2', 'col-10', 'col-11'].forEach(cls => {
                if (mainContent.classList.contains(cls)) mainContent.classList.remove(cls);
                if (sidebarCol.classList.contains(cls)) sidebarCol.classList.remove(cls);
            });
            
            // Apply the appropriate classes based on state - use fixed widths for better control
            if (isCollapsed) {
                sidebarCol.classList.add('sidebarcolumn-collapsed');
                mainContent.classList.add('expanded-content');
            } else {
                sidebarCol.classList.add('col-2');
                mainContent.classList.add('col-10');
                sidebarCol.classList.remove('sidebarcolumn-collapsed');
                mainContent.classList.remove('expanded-content');
            }
            
            // Store the current state for potential recovery
            localStorage.setItem('sidebar-collapsed', isCollapsed);
            
            // Trigger window resize event to ensure any responsive components adjust
            window.dispatchEvent(new Event('resize'));
        } catch (error) {
            console.error('Error updating sidebar state:', error);
        }
    } else {
        console.warn('Could not find main content or sidebar elements');
    }
}


window.initSidebar = function() {
    try {
        // Only apply sidebar state on desktop screens
        if (!window.isMobileOrTablet()) {
            if (typeof window.getSidebarState === 'function') {
                const isCollapsed = window.getSidebarState();
                window.updateSidebarState(isCollapsed);
            } else {
                const state = localStorage.getItem('sidebar-collapsed');
                const isCollapsed = state === 'true';
                window.updateSidebarState(isCollapsed);
            }
        }
    } catch (error) {
        console.error('Error initializing sidebar:', error);
    }
}

// Handle window resize to reset sidebar state when switching between desktop/tablet
window.addEventListener('resize', function() {
    setTimeout(function() {
        if (window.isMobileOrTablet()) {
            // Reset any desktop sidebar classes on mobile/tablet
            const mainContent = document.getElementById('mainContent');
            const sidebarCol = document.getElementById('sidebarColumn');
            
            if (mainContent && sidebarCol) {
                ['col-1', 'col-2', 'col-10', 'col-11', 'expanded-content', 'sidebarcolumn-collapsed'].forEach(cls => {
                    mainContent.classList.remove(cls);
                    sidebarCol.classList.remove(cls);
                });
            }
        } else {
            // Re-apply desktop sidebar state when switching back to desktop
            window.initSidebar();
        }
    }, 100);
});

document.addEventListener('DOMContentLoaded', function() {
    setTimeout(window.initSidebar, 100);
});

window.addEventListener('load', function() {
    setTimeout(window.initSidebar, 100);
});

window.hideMobileHeader = function() {
    const mobileHeader = document.getElementById('mobileHeader');
    if (mobileHeader) {
        mobileHeader.style.display = 'none';
    }
    // Update the padding top of the #mainContent
    const mainContent = document.getElementById('mainContent');
    if (mainContent) {
        mainContent.style.setProperty('padding-top', '40px', 'important');
    }
}
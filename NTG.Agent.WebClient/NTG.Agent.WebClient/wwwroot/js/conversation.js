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
    
    // Find and modify list items to prevent them from showing above the modal
    const listItems = document.querySelectorAll('.toastui-editor-contents ol li, .toastui-editor-contents ul li');
    originalDisplays = [];
    
    listItems.forEach(item => {
        // Store original display value
        originalDisplays.push({
            element: item,
            display: item.style.display
        });
        
        // Modify to prevent appearing above modal
        item.style.display = 'none';
    });
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
}

// Define getSidebarState first, before it's used by other functions
window.getSidebarState = function() {
    try {
        const state = localStorage.getItem('sidebar-collapsed');
        if (state === null) return false; // Default to expanded if no state saved
        return state === 'true'; // Convert the string to a boolean
    } catch (error) {
        console.error('Error getting sidebar state:', error);
        return false; // Default to expanded on error
    }
}

window.setSidebarState = function(isCollapsed) {
    try {
        localStorage.setItem('sidebar-collapsed', isCollapsed);
        window.updateSidebarState(isCollapsed);
    } catch (error) {
        console.error('Error setting sidebar state:', error);
    }
}

window.updateSidebarState = function (isCollapsed) {
    // Get references to the main elements
    const mainContent = document.getElementById('mainContent') || document.querySelector('main');
    const sidebarCol = document.getElementById('sidebarColumn') || document.querySelector('.sidebar').closest('[class*="col-"]');
    
    if (mainContent && sidebarCol) {
        console.log('Updating sidebar state:', isCollapsed ? 'collapsed' : 'expanded');
        
        try {
            // Reset all column classes first to avoid conflicts
            ['col-1', 'col-2', 'col-10', 'col-11'].forEach(cls => {
                if (mainContent.classList.contains(cls)) mainContent.classList.remove(cls);
                if (sidebarCol.classList.contains(cls)) sidebarCol.classList.remove(cls);
            });
            
            // Apply the appropriate classes based on state
            if (isCollapsed) {
                console.log('Setting collapsed layout');
                mainContent.classList.add('col-11');
                sidebarCol.classList.add('col-1');
                sidebarCol.classList.add('sidebarcolumn-collapsed');
                mainContent.classList.add('expanded-content');
            } else {
                console.log('Setting expanded layout');
                mainContent.classList.add('col-10');
                sidebarCol.classList.add('col-2');
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
        if (typeof window.getSidebarState === 'function') {
            const isCollapsed = window.getSidebarState();
            window.updateSidebarState(isCollapsed);
        } else {
            const state = localStorage.getItem('sidebar-collapsed');
            const isCollapsed = state === 'true';
            window.updateSidebarState(isCollapsed);
        }
    } catch (error) {
        console.error('Error initializing sidebar:', error);
    }
}

document.addEventListener('DOMContentLoaded', function() {
    setTimeout(window.initSidebar, 100);
});

window.addEventListener('load', function() {
    setTimeout(window.initSidebar, 100);
});
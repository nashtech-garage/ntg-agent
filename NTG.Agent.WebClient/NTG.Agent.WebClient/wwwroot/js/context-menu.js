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
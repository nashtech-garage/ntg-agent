let dotNetRef = null;
let handler = null;

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
}

window.showInputChatContainer = function () {
    const inputContainer = document.getElementById('inputChatContainer');
    if (inputContainer) {
        inputContainer.style.display = '';
    }
}
window.getBoundingClientRectById = function(id) {
    const element = document.getElementById(id);
    if (element) {
        const rect = element.getBoundingClientRect();
        return {
            x: rect.x,
            y: rect.y,
            width: rect.width,
            height: rect.height,
            top: rect.top,
            right: rect.right,
            bottom: rect.bottom,
            left: rect.left
        };
    }
    return null;
};

// Functions to handle document click for context menu
window.addDocumentClickListener = function(dotNetReference) {
    window.documentClickHandler = function() {
        dotNetReference.invokeMethodAsync('HideContextMenuFromJS');
    };
    
    // Add the event listener with a small delay to avoid immediate trigger
    setTimeout(() => {
        document.addEventListener('click', window.documentClickHandler);
    }, 50);
};

window.removeDocumentClickListener = function() {
    if (window.documentClickHandler) {
        document.removeEventListener('click', window.documentClickHandler);
        window.documentClickHandler = null;
    }
};

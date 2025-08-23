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

// Function to download files from JavaScript
window.downloadFile = function(fileName, base64Data, contentType) {
    try {
        // Convert base64 to bytes
        const byteCharacters = atob(base64Data);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        
        // Create blob and download
        const blob = new Blob([byteArray], { type: contentType });
        const url = window.URL.createObjectURL(blob);
        
        // Create temporary link element and trigger download
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        
        // Cleanup
        document.body.removeChild(link);
        window.URL.revokeObjectURL(url);
    } catch (error) {
        console.error('Error downloading file:', error);
    }
};

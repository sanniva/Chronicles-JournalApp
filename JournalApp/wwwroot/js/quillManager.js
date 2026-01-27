// quillManager.js
export function initializeQuill(editorId, dotNetHelper, initialContent) {
    const quill = new Quill('#' + editorId, {
        theme: 'snow',
        modules: {
            toolbar: [
                [{ 'header': [1, 2, 3, false] }],
                ['bold', 'italic', 'underline', 'strike'],
                [{ 'list': 'ordered'}, { 'list': 'bullet' }],
                ['link', 'blockquote', 'code-block'],
                [{ 'color': [] }, { 'background': [] }],
                ['clean']
            ]
        },
        placeholder: 'Write your thoughts here...'
    });

    // Set initial content if editing
    if (initialContent && initialContent.trim() !== '') {
        quill.root.innerHTML = initialContent;
    }

    // Update character count and notify Blazor
    quill.on('text-change', function() {
        const html = quill.root.innerHTML;
        const plainText = quill.getText().trim();
        const charCount = plainText.length;

        // Update character count display
        const charCountElement = document.getElementById('char-count');
        if (charCountElement) {
            charCountElement.textContent = charCount;
        }

        // Notify Blazor component
        if (dotNetHelper) {
            dotNetHelper.invokeMethodAsync('UpdateContent', html, plainText);
        }
    });

    return quill;
}

export function getQuillContent(quill) {
    return quill.root.innerHTML;
}

export function setQuillContent(quill, content) {
    quill.root.innerHTML = content;
}

// Fallback initialization for direct CDN loading
if (typeof window !== 'undefined') {
    window.initializeQuill = initializeQuill;
    window.getQuillContent = getQuillContent;
    window.setQuillContent = setQuillContent;
}
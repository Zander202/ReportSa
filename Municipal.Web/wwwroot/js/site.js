document.querySelectorAll('[data-copy]').forEach(button => {
    button.addEventListener('click', async () => {
        const input = document.getElementById(button.dataset.copy);
        const feedback = document.getElementById('copy-feedback');
        try { await navigator.clipboard.writeText(input.value); feedback.textContent = 'Reference copied.'; }
        catch { input.select(); feedback.textContent = 'Select and copy the reference using Ctrl+C.'; }
    });
});

document.addEventListener('DOMContentLoaded', () => {

    // ---- Mobile menu ----
    const toggle = document.querySelector('.menu-toggle');
    const nav = document.getElementById('main-nav');
    if (toggle && nav) {
        toggle.addEventListener('click', () => {
            const open = nav.classList.toggle('open');
            toggle.setAttribute('aria-expanded', open);
        });
    }

    // ---- Highlight current nav link ----
    document.querySelectorAll('nav a:not(.cta)').forEach(a => {
        if (a.pathname === location.pathname) a.classList.add('active');
    });

    // ---- Copy reference code (Confirmation page) ----
    document.querySelectorAll('[data-copy]').forEach(button => {
        button.addEventListener('click', async () => {
            const input = document.getElementById(button.dataset.copy);
            const feedback = document.getElementById('copy-feedback');
            try {
                await navigator.clipboard.writeText(input.value);
                if (feedback) feedback.textContent = 'Reference copied.';
                button.textContent = 'Copied ✓';
                setTimeout(() => (button.textContent = 'Copy'), 2000);
            } catch {
                input.select();
                if (feedback) feedback.textContent = 'Select and copy the reference using Ctrl+C.';
            }
        });
    });

    // ---- Category tiles keep the dropdown in sync ----
    const select = document.getElementById('Category');
    const tiles = document.querySelectorAll('.tile');
    const syncTiles = () => tiles.forEach(t =>
        t.classList.toggle('selected', select && t.dataset.category === select.value));
    if (select && tiles.length) {
        tiles.forEach(t => t.addEventListener('click', () => {
            select.value = t.dataset.category;
            select.dispatchEvent(new Event('change', { bubbles: true }));
        }));
        select.addEventListener('change', syncTiles);
        syncTiles();
    }

    // ---- Description character counter ----
    const desc = document.getElementById('Description');
    const count = document.getElementById('char-count');
    if (desc && count) {
        const update = () => (count.textContent = desc.value.length);
        desc.addEventListener('input', update);
        update();
    }

    // ---- Reference code: uppercase + trim as you type ----
    const ref = document.getElementById('ReferenceCode');
    if (ref) ref.addEventListener('input', () => (ref.value = ref.value.toUpperCase().trim()));

    // ---- Submit button loading state ----
    document.querySelectorAll('form').forEach(form => {
        form.addEventListener('submit', () => {
            if (form.querySelector('.input-validation-error')) return;
            const btn = form.querySelector('button[type="submit"].button');
            if (btn) { btn.classList.add('loading'); btn.textContent = 'Please wait…'; }
        });
    });

    // ---- Scroll reveal ----
    const items = document.querySelectorAll('.reveal, .panel, .hero');
    if ('IntersectionObserver' in window) {
        const io = new IntersectionObserver(entries => entries.forEach(e => {
            if (e.isIntersecting) { e.target.classList.add('visible'); io.unobserve(e.target); }
        }), { threshold: 0.1 });
        items.forEach(el => { el.classList.add('reveal'); io.observe(el); });
    } else {
        items.forEach(el => el.classList.add('visible'));
    }

    // ---- Auto-hide success notices ----
    document.querySelectorAll('.notice.success').forEach(n =>
        setTimeout(() => { n.style.transition = 'opacity .6s'; n.style.opacity = 0; }, 6000));
});
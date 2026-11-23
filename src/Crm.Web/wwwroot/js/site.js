// ===== App shell behaviour: sidebar, toasts =====
(function () {
    'use strict';

    // Sidebar toggle
    const toggle = document.getElementById('sidebarToggle');
    const sidebar = document.getElementById('sidebar');
    const scrim = document.getElementById('scrim');
    if (toggle && sidebar && scrim) {
        const setOpen = (open) => {
            sidebar.classList.toggle('is-open', open);
            scrim.classList.toggle('is-visible', open);
            toggle.classList.toggle('is-open', open);
            toggle.setAttribute('aria-expanded', String(open));
        };
        toggle.addEventListener('click', () => setOpen(!sidebar.classList.contains('is-open')));
        scrim.addEventListener('click', () => setOpen(false));
        document.addEventListener('keydown', e => { if (e.key === 'Escape') setOpen(false); });
    }


    // Collapsible nav groups: click a .nav-group to hide/show its links
    document.querySelectorAll('.nav-group').forEach(g => {
        g.classList.add('nav-group--toggle');
        g.setAttribute('role', 'button');
        g.setAttribute('tabindex', '0');
        const toggle = () => {
            const collapsed = g.classList.toggle('is-collapsed');
            let el = g.nextElementSibling;
            while (el && !el.classList.contains('nav-group')) {
                el.classList.toggle('is-hidden', collapsed);
                el = el.nextElementSibling;
            }
        };
        g.addEventListener('click', toggle);
        g.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggle(); } });
    });

    // Highlight active nav link
    const path = location.pathname.toLowerCase();
    document.querySelectorAll('.nav-link').forEach(a => {
        const href = a.getAttribute('href') || '';
        if (href && path.startsWith(href.toLowerCase()) && href.length > 1) {
            a.classList.add('is-active');
        }
    });

    // Auto-dismissing toasts
    document.querySelectorAll('.toast[data-auto]').forEach(t => {
        requestAnimationFrame(() => t.classList.add('is-in'));
        setTimeout(() => { t.classList.remove('is-in'); setTimeout(() => t.remove(), 500); }, 4500);
        t.addEventListener('click', () => t.remove());
    });
})();

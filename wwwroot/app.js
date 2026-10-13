// Small page behaviours that must work on both interactive pages and the
// statically rendered account pages, so they live outside Blazor.
(function () {
    const root = document.documentElement;

    function storedTheme() {
        try {
            return localStorage.getItem('theme');
        } catch {
            return null;
        }
    }

    function applyTheme() {
        const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        const theme = storedTheme() || (prefersDark ? 'dark' : 'light');
        if (root.getAttribute('data-bs-theme') !== theme) {
            root.setAttribute('data-bs-theme', theme);
        }
    }

    applyTheme();

    document.addEventListener('click', (e) => {
        if (e.target.closest('[data-theme-toggle]')) {
            const next = root.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
            try {
                localStorage.setItem('theme', next);
            } catch {
                // Private mode: the choice just won't be remembered.
            }
            root.setAttribute('data-bs-theme', next);
            return;
        }

        if (e.target.closest('[data-nav-toggle]')) {
            document.body.classList.toggle('nav-open');
        } else if (e.target.closest('.scrim, .sidebar a')) {
            document.body.classList.remove('nav-open');
        }
    });

    // Firefox only starts a drag once some data has been set on it.
    document.addEventListener('dragstart', (e) => {
        if (e.target.closest && e.target.closest('[draggable="true"]') && e.dataTransfer) {
            e.dataTransfer.setData('text/plain', '');
            e.dataTransfer.effectAllowed = 'move';
        }
    });

    // Enhanced navigation re-syncs <html> attributes from the server response.
    if (window.Blazor && Blazor.addEventListener) {
        Blazor.addEventListener('enhancedload', applyTheme);
    }
})();

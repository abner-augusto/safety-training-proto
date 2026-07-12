    /* ================================================================
     * THEME — dark (live use) ↔ light (figure mode). It swaps tokens
     * only; the 3D viewport re-reads the values via setTheme().
     * ============================================================== */
    let theme = localStorage.getItem('sp_theme') || 'dark';

    export function applyTheme() {
      document.documentElement.dataset.theme = theme;
      document.getElementById('theme-icon-moon').style.display = theme === 'dark' ? 'block' : 'none';
      document.getElementById('theme-icon-sun').style.display  = theme === 'dark' ? 'none' : 'block';
      document.getElementById('btn-theme').setAttribute('aria-pressed', String(theme === 'light'));
      window.__spViewport?.setTheme();
    }

    export function toggleTheme() {
      theme = theme === 'dark' ? 'light' : 'dark';
      localStorage.setItem('sp_theme', theme);
      applyTheme();
    }

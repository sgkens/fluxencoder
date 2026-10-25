/**
 * @fileoverview Client-side progressive enhancement for the FluxEncoder documentation site.
 * Zero external runtime dependencies; provides theme switching, mobile navigation drawer,
 * terminal clipboard copy, TOC scroll-spy, Fluxfile schema filtering, and Pagefind search modal.
 */

const root = document.documentElement;

// 1. Theme Switcher
function initTheme() {
  const toggleBtn = document.querySelector('[data-theme-toggle]');
  const labelEl = document.querySelector('[data-theme-label]');

  function syncThemeUI(theme) {
    root.setAttribute('data-theme', theme);
    if (labelEl) {
      labelEl.textContent = theme.toUpperCase();
    }
  }

  const current = root.getAttribute('data-theme') || 'dark';
  syncThemeUI(current);

  if (toggleBtn) {
    toggleBtn.addEventListener('click', () => {
      const next = root.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
      syncThemeUI(next);
      try {
        localStorage.setItem('flux-docs-theme', next);
      } catch (_) {}
    });
  }
}

// 2. Mobile Navigation Drawer
function initDrawer() {
  const sidebar = document.getElementById('flux-sidebar-drawer');
  const openBtn = document.querySelector('[data-drawer-toggle]');
  const closeBtn = document.querySelector('[data-drawer-close]');
  const backdrop = document.querySelector('[data-drawer-backdrop]');

  if (!sidebar || !openBtn) return;

  function setDrawerOpen(isOpen) {
    sidebar.classList.toggle('is-open', isOpen);
    openBtn.setAttribute('aria-expanded', String(isOpen));
    if (backdrop) {
      backdrop.hidden = !isOpen;
    }
    document.body.classList.toggle('flux-drawer-locked', isOpen);
  }

  openBtn.addEventListener('click', () => {
    const isOpen = sidebar.classList.contains('is-open');
    setDrawerOpen(!isOpen);
  });

  if (closeBtn) {
    closeBtn.addEventListener('click', () => setDrawerOpen(false));
  }

  if (backdrop) {
    backdrop.addEventListener('click', () => setDrawerOpen(false));
  }

  window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && sidebar.classList.contains('is-open')) {
      setDrawerOpen(false);
    }
  });
}

// 3. Terminal & Code Copy Buttons
function initCopyButtons() {
  document.querySelectorAll('[data-copy-code]').forEach((btn) => {
    btn.addEventListener('click', async () => {
      const container = btn.closest('.flux-terminal');
      const codeEl = container?.querySelector('pre code');
      if (!codeEl) return;

      const label = btn.querySelector('[data-copy-label]');
      try {
        await navigator.clipboard.writeText(codeEl.textContent ?? '');
        if (label) label.textContent = 'COPIED';
        btn.classList.add('is-copied');
        setTimeout(() => {
          if (label) label.textContent = 'COPY';
          btn.classList.remove('is-copied');
        }, 1600);
      } catch (_) {
        if (label) label.textContent = 'ERROR';
      }
    });
  });
}

// 4. TOC Scroll-Spy
function initScrollSpy() {
  const tocLinks = Array.from(document.querySelectorAll('[data-toc-link]'));
  if (tocLinks.length === 0 || !('IntersectionObserver' in window)) return;

  const linkMap = new Map();
  const headings = [];

  for (const link of tocLinks) {
    const id = link.getAttribute('data-toc-link');
    const target = id ? document.getElementById(id) : null;
    if (id && target) {
      linkMap.set(id, link);
      headings.push(target);
    }
  }

  if (headings.length === 0) return;

  function setActive(id) {
    for (const [key, link] of linkMap.entries()) {
      link.classList.toggle('is-active', key === id);
    }
  }

  const observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          setActive(entry.target.id);
          break;
        }
      }
    },
    { rootMargin: '-80px 0px -70% 0px', threshold: 0 }
  );

  headings.forEach((h) => observer.observe(h));
  setActive(headings[0].id);
}

// 5. Interactive Fluxfile Schema Filter
function initSchemaFilter() {
  const input = document.querySelector('[data-schema-filter]');
  const rows = Array.from(document.querySelectorAll('[data-schema-row]'));
  if (!input || rows.length === 0) return;

  input.addEventListener('input', () => {
    const query = input.value.trim().toLowerCase();
    for (const row of rows) {
      const text = row.getAttribute('data-schema-row') || '';
      row.hidden = query.length > 0 && !text.includes(query);
    }
  });
}

// 6. Pagefind Search Modal
function initSearchModal() {
  const dialog = document.getElementById('flux-search-dialog');
  const openBtn = document.querySelector('[data-search-open]');
  const closeBtn = document.querySelector('[data-search-close]');
  const input = document.querySelector('[data-search-input]');
  const resultsEl = document.querySelector('[data-search-results]');

  if (!dialog || !input || !resultsEl) return;

  let pagefindModule = null;
  let activeIndex = -1;

  async function ensurePagefind() {
    if (pagefindModule) return pagefindModule;
    try {
      pagefindModule = await import('/pagefind/pagefind.js');
      await pagefindModule.init();
      return pagefindModule;
    } catch (_) {
      return null;
    }
  }

  function openSearch() {
    if (!dialog.open) {
      dialog.showModal();
    }
    input.focus();
    input.select();
    ensurePagefind();
  }

  function closeSearch() {
    if (dialog.open) {
      dialog.close();
    }
  }

  if (openBtn) {
    openBtn.addEventListener('click', openSearch);
  }
  if (closeBtn) {
    closeBtn.addEventListener('click', closeSearch);
  }

  dialog.addEventListener('click', (e) => {
    if (e.target === dialog) {
      closeSearch();
    }
  });

  window.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      openSearch();
    } else if (
      e.key === '/' &&
      !dialog.open &&
      document.activeElement?.tagName !== 'INPUT' &&
      document.activeElement?.tagName !== 'TEXTAREA'
    ) {
      e.preventDefault();
      openSearch();
    }
  });

  function updateHighlight(items) {
    items.forEach((el, idx) => {
      el.classList.toggle('is-selected', idx === activeIndex);
      if (idx === activeIndex) {
        el.scrollIntoView({ block: 'nearest' });
      }
    });
  }

  input.addEventListener('keydown', (e) => {
    const items = Array.from(resultsEl.querySelectorAll('.flux-search-hit'));
    if (items.length === 0) return;

    if (e.key === 'ArrowDown') {
      e.preventDefault();
      activeIndex = (activeIndex + 1) % items.length;
      updateHighlight(items);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      activeIndex = (activeIndex - 1 + items.length) % items.length;
      updateHighlight(items);
    } else if (e.key === 'Enter' && activeIndex >= 0 && items[activeIndex]) {
      e.preventDefault();
      items[activeIndex].click();
    }
  });

  input.addEventListener('input', async () => {
    const query = input.value.trim();
    activeIndex = -1;

    if (!query) {
      resultsEl.innerHTML =
        '<div class="flux-search-dialog__empty">Type a query to search across CLI contracts, Fluxfile schema definitions, security boundaries, and ADRs.</div>';
      return;
    }

    const pf = await ensurePagefind();
    if (!pf) {
      resultsEl.innerHTML =
        '<div class="flux-search-dialog__empty">Search index is available after running <code>npm run docs:search</code>.</div>';
      return;
    }

    const search = await pf.search(query);
    if (!search || search.results.length === 0) {
      resultsEl.innerHTML = `<div class="flux-search-dialog__empty">No matching technical documentation found for <code>${query.replace(/[<>&"]/g, '')}</code>.</div>`;
      return;
    }

    const topResults = await Promise.all(search.results.slice(0, 8).map((r) => r.data()));
    resultsEl.innerHTML = topResults
      .map(
        (item, idx) => `
        <a href="${item.url}" class="flux-search-hit${idx === 0 ? ' is-selected' : ''}">
          <div class="flux-search-hit__top">
            <span class="flux-glyph" aria-hidden="true">▶</span>
            <strong class="flux-search-hit__title">${item.meta?.title || item.url}</strong>
            <code class="flux-search-hit__url">${item.url}</code>
          </div>
          <p class="flux-search-hit__excerpt">${item.excerpt || ''}</p>
        </a>
      `
      )
      .join('');
    activeIndex = 0;
  });
}

initTheme();
initDrawer();
initCopyButtons();
initScrollSpy();
initSchemaFilter();
initSearchModal();

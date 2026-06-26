(function () {
  var root = document.documentElement;
  var btn = document.getElementById('theme-toggle');
  if (!btn) return;
  var lbl = btn.querySelector('.toggle-label');

  function sync() {
    var dark = root.getAttribute('data-theme') === 'dark';
    if (lbl) lbl.textContent = dark ? 'Vault' : 'Stone';
    // aria-pressed reflects whether the light (Stone) theme is active
    btn.setAttribute('aria-pressed', dark ? 'false' : 'true');
    btn.setAttribute('aria-label', dark ? 'Theme: Vault. Activate to switch to Stone.' : 'Theme: Stone. Activate to switch to Vault.');
  }

  sync();

  btn.addEventListener('click', function () {
    var next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    try { localStorage.setItem('hiram-theme', next); } catch (e) { /* storage blocked, theme still applies for the session */ }
    sync();
  });
})();

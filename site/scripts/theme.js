(function () {
  var root = document.documentElement;
  var btn = document.getElementById('theme-toggle');
  if (!btn) return;
  var lbl = btn.querySelector('.toggle-label');

  function sync() {
    var dark = root.getAttribute('data-theme') === 'dark';
    if (lbl) lbl.textContent = dark ? 'Vault' : 'Stone';
    btn.setAttribute('aria-label', dark ? 'Switch to the stone theme' : 'Switch to the vault theme');
  }

  sync();

  btn.addEventListener('click', function () {
    var next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    try { localStorage.setItem('hiram-theme', next); } catch (e) { /* storage blocked, theme still applies for the session */ }
    sync();
  });
})();

(function () {
  var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (reduce) return;

  var hero = document.querySelector('.hero');
  if (hero) hero.classList.add('is-animated');
})();

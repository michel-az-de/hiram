(function () {
  var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (reduce) return;

  var hero = document.querySelector('.hero');
  if (hero) hero.classList.add('is-animated');

  if (!('IntersectionObserver' in window)) return;

  var targets = document.querySelectorAll(
    '.section-head, .split, .steps, .statemachine, .feature-cols, .grid, .timeline, .faq, .cta-final'
  );
  if (!targets.length) return;

  document.documentElement.classList.add('reveal-on');

  var vh = window.innerHeight || document.documentElement.clientHeight;
  var observer = new IntersectionObserver(function (entries) {
    entries.forEach(function (entry) {
      if (!entry.isIntersecting) return;
      entry.target.classList.add('is-visible');
      observer.unobserve(entry.target);
    });
  }, { rootMargin: '0px 0px -10% 0px', threshold: 0.08 });

  targets.forEach(function (el) {
    el.classList.add('reveal');
    // Anything already within the first viewport reveals now, without a flash.
    if (el.getBoundingClientRect().top < vh * 0.92) {
      el.classList.add('is-visible');
    } else {
      observer.observe(el);
    }
  });
})();

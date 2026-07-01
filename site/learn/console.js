// Stage console for the learn hub. Deterministic fixtures, no network: the stage is the default
// mode from ADR-021 and stays wifi proof. The live mode that fetches the local API arrives next.

const form = document.getElementById('console-form');

if (form) {
  const requestEl = document.getElementById('console-request');
  const responseEl = document.getElementById('console-response');
  const flowEl = document.getElementById('console-flow');
  const hintEl = document.getElementById('console-hint');
  const modeButtons = Array.from(document.querySelectorAll('.console-mode'));
  const reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const stages = ['accepted', 'queued', 'sending', 'sent'];
  let sequence = 4207;
  let stepTimer = null;

  function escape(value) {
    return value.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  }

  function notificationId() {
    return 'ntf_01HZX' + sequence.toString(36).toUpperCase();
  }

  function renderRequest(recipient, subject) {
    requestEl.innerHTML =
      '<span class="c3">$</span> curl -X POST <span class="c2">http://localhost:3357/v1/notifications</span> \\\n' +
      '  -H <span class="c1">"X-Api-Key: hk_live_demo…"</span> \\\n' +
      '  -H <span class="c1">"Idempotency-Key: palco-' + sequence + '"</span> \\\n' +
      '  -d <span class="c1">\'{"channel":"email","recipient":"' + escape(recipient) + '",\n' +
      '       "subject":"' + escape(subject) + '"}\'</span>';
  }

  function renderResponse(id) {
    responseEl.innerHTML =
      '<span class="c3">HTTP/1.1</span> <span class="c4">202 Accepted</span>\n' +
      '{ <span class="c1">"id"</span>: "' + id + '", <span class="c1">"status"</span>: "accepted" }';
  }

  function renderFlow(activeIndex) {
    const complete = activeIndex >= stages.length - 1;
    flowEl.innerHTML = stages
      .map((stage, i) => {
        let cls = '';
        if (complete || i < activeIndex) cls = 'done';
        else if (i === activeIndex) cls = 'active';
        return '<span class="flow-step ' + cls + '">' + stage + '</span>';
      })
      .join('<span class="flow-arr">→</span>');
  }

  function run(recipient, subject) {
    if (stepTimer) { clearTimeout(stepTimer); stepTimer = null; }
    sequence += 1;
    renderRequest(recipient, subject);
    renderResponse(notificationId());

    if (reduceMotion) { renderFlow(stages.length - 1); return; }

    let index = 0;
    renderFlow(index);
    const advance = () => {
      index += 1;
      renderFlow(index);
      if (index < stages.length - 1) stepTimer = setTimeout(advance, 520);
    };
    stepTimer = setTimeout(advance, 520);
  }

  function submit() {
    const recipient = form.recipient.value.trim() || 'loja@tenant.dev';
    const subject = form.subject.value.trim() || 'Pedido confirmado';
    run(recipient, subject);
  }

  // The button stays type=button so a click before this module loads never triggers a native
  // form submit and page reload. Enter inside the form still works once the listener is attached.
  const sendButton = document.getElementById('console-send');
  if (sendButton) sendButton.addEventListener('click', submit);
  form.addEventListener('submit', (event) => { event.preventDefault(); submit(); });

  modeButtons.forEach((button) => {
    button.addEventListener('click', () => {
      const live = button.dataset.mode === 'live';
      modeButtons.forEach((other) => {
        const active = other === button;
        other.classList.toggle('is-active', active);
        other.setAttribute('aria-pressed', active ? 'true' : 'false');
      });
      hintEl.textContent = live
        ? 'O modo ao vivo dispara contra a API local; chega no próximo passo. Por ora, o palco responde.'
        : 'Modo palco: resposta determinística, sem rede.';
    });
  });

  renderFlow(-1);
}

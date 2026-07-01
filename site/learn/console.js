// Demo console for the learn hub. Stage mode replays deterministic fixtures and stays wifi proof;
// live mode fetches the local API on the same origin with a demo key the presenter pastes in, so the
// operator X-Admin-Key never reaches the browser. The key lives only in this browser's localStorage.

const form = document.getElementById('console-form');

if (form) {
  const requestEl = document.getElementById('console-request');
  const responseEl = document.getElementById('console-response');
  const flowEl = document.getElementById('console-flow');
  const hintEl = document.getElementById('console-hint');
  const keyInput = document.getElementById('c-key');
  const modeButtons = Array.from(document.querySelectorAll('.console-mode'));
  const reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const stages = ['accepted', 'queued', 'sending', 'sent'];
  const keyStorage = 'hiram-demo-key';
  const stageHint = 'Modo palco: resposta determinística, sem rede.';

  let mode = 'palco';
  let sequence = 4207;
  let stepTimer = null;

  restoreKey();

  function restoreKey() {
    try {
      const saved = localStorage.getItem(keyStorage);
      if (saved) keyInput.value = saved;
    } catch (e) { /* storage blocked, the field still works for this session */ }
  }

  function rememberKey() {
    try { localStorage.setItem(keyStorage, keyInput.value.trim()); }
    catch (e) { /* storage blocked, nothing to persist */ }
  }

  function escape(value) {
    return value.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  }

  function maskedKey() {
    const key = keyInput.value.trim();
    return key.length > 10 ? escape(key.slice(0, 8)) + '…' : 'hk_live_…';
  }

  function fields() {
    return {
      recipient: form.recipient.value.trim() || 'loja@tenant.dev',
      subject: form.subject.value.trim() || 'Pedido confirmado',
      body: form.body.value.trim() || 'Seu pedido foi confirmado.'
    };
  }

  function renderRequest(f, host, keyLabel, idempotency) {
    requestEl.innerHTML =
      '<span class="c3">$</span> curl -X POST <span class="c2">' + escape(host) + '/v1/notifications</span> \\\n' +
      '  -H <span class="c1">"X-Api-Key: ' + keyLabel + '"</span> \\\n' +
      '  -H <span class="c1">"Idempotency-Key: ' + escape(idempotency) + '"</span> \\\n' +
      '  -d <span class="c1">\'{"channel":"email","recipient":"' + escape(f.recipient) + '",\n' +
      '       "subject":"' + escape(f.subject) + '","body":"' + escape(f.body) + '"}\'</span>';
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

  function runStage(f) {
    if (stepTimer) { clearTimeout(stepTimer); stepTimer = null; }
    sequence += 1;
    const id = 'ntf_01HZX' + sequence.toString(36).toUpperCase();
    renderRequest(f, 'http://localhost:3357', 'hk_live_demo…', 'palco-' + sequence);
    responseEl.innerHTML =
      '<span class="c3">HTTP/1.1</span> <span class="c4">202 Accepted</span>\n' +
      '{ <span class="c1">"id"</span>: "' + id + '", <span class="c1">"status"</span>: "accepted" }';

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

  async function runLive(f) {
    const key = keyInput.value.trim();
    if (!key) {
      hintEl.textContent = 'Cole a demo key do bootstrap para o modo ao vivo.';
      keyInput.focus();
      return;
    }

    sequence += 1;
    const idempotency = 'live-' + sequence;
    renderRequest(f, window.location.origin, maskedKey(), idempotency);
    responseEl.textContent = '… enviando';
    flowEl.innerHTML = '';

    let response;
    try {
      response = await fetch('/v1/notifications', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Api-Key': key,
          'Idempotency-Key': idempotency
        },
        body: JSON.stringify({ channel: 'email', recipient: f.recipient, subject: f.subject, body: f.body })
      });
    } catch (error) {
      responseEl.innerHTML = '<span class="c1">// a API local não respondeu. Rode a Hiram.Api ou volte ao palco.</span>';
      hintEl.textContent = 'API local indisponível; o palco continua funcionando.';
      return;
    }

    const raw = await response.text();
    let parsed = null;
    try { parsed = JSON.parse(raw); } catch (e) { /* not json, keep the raw text */ }
    renderLiveResponse(response.status, response.statusText, parsed, raw);

    if (response.status === 202) {
      renderFlow(0);
      hintEl.textContent = 'Aceito de verdade. A entrega segue assíncrona.';
    } else {
      hintEl.textContent = 'A API respondeu ' + response.status + '. Confira a chave e os campos.';
    }
  }

  function renderLiveResponse(status, statusText, parsed, raw) {
    const ok = status >= 200 && status < 300;
    const head = '<span class="c3">HTTP/1.1</span> <span class="' + (ok ? 'c4' : 'c1') + '">'
      + status + ' ' + escape(statusText || '') + '</span>';
    const body = parsed ? JSON.stringify(parsed, null, 2) : (raw || '');
    responseEl.innerHTML = head + '\n' + escape(body);
  }

  function submit() {
    const f = fields();
    if (mode === 'live') runLive(f);
    else runStage(f);
  }

  const sendButton = document.getElementById('console-send');
  if (sendButton) sendButton.addEventListener('click', submit);
  form.addEventListener('submit', (event) => { event.preventDefault(); submit(); });
  keyInput.addEventListener('change', rememberKey);

  modeButtons.forEach((button) => {
    button.addEventListener('click', () => {
      mode = button.dataset.mode === 'live' ? 'live' : 'palco';
      modeButtons.forEach((other) => {
        const active = other === button;
        other.classList.toggle('is-active', active);
        other.setAttribute('aria-pressed', active ? 'true' : 'false');
      });
      hintEl.textContent = mode === 'live'
        ? 'Modo ao vivo: dispara contra a API local com a demo key.'
        : stageHint;
    });
  });

  renderFlow(-1);
}

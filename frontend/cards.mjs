export function formatSize(bytes) {
  if (!Number.isFinite(bytes) || bytes < 0) return null;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes / 1000;
  let unit = 0;
  while (value >= 1000 && unit < units.length - 1) { value /= 1000; unit++; }
  // Promote a value that would round to 1000 instead of displaying 1000 GB.
  if (Number(value.toFixed(1)) >= 1000 && unit < units.length - 1) { value /= 1000; unit++; }
  return `${Number(value.toFixed(1))} ${units[unit]}`;
}

export function cardIdentity(card) {
  const link = card.querySelector(':scope > a[href]');
  if (!link) return null;
  try {
    const url = new URL(link.getAttribute('href'), card.ownerDocument.baseURI);
    if (url.origin !== new URL(card.ownerDocument.baseURI).origin) return null;
    const path = url.hash.startsWith('#/') ? url.hash.slice(1).split('?')[0] : url.pathname;
    const match = /^\/(performer|studio|video)\/(\d+)\/?$/.exec(path);
    const id = match ? Number(match[2]) : 0;
    return Number.isSafeInteger(id) && id > 0 ? { kind: match[1], id } : null;
  } catch { return null; }
}

export async function loadSizes(kind, ids, request, signal) {
  const result = [];
  for (let offset = 0; offset < ids.length; offset += 100) {
    const response = await request(`/api/ext/filesizes/${kind}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, signal,
      body: JSON.stringify({ ids: ids.slice(offset, offset + 100) }),
    });
    if (!response.ok) throw new Error(`Filesizes request failed (${response.status})`);
    result.push(...await response.json());
  }
  return result;
}

const selector = '.entity-card, .video-card';
const svgNS = 'http://www.w3.org/2000/svg';
function makeLabel(document) {
  const span = document.createElement('span');
  span.className = 'cove-filesize';
  const svg = document.createElementNS(svgNS, 'svg');
  for (const [key, value] of Object.entries({ viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', 'stroke-width': '2', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true' })) svg.setAttribute(key, value);
  const ellipse = document.createElementNS(svgNS, 'ellipse');
  for (const [key, value] of Object.entries({ cx: '12', cy: '5', rx: '9', ry: '3' })) ellipse.setAttribute(key, value);
  svg.append(ellipse);
  for (const d of ['M3 5v14a9 3 0 0 0 18 0V5', 'M3 12a9 3 0 0 0 18 0']) {
    const path = document.createElementNS(svgNS, 'path'); path.setAttribute('d', d); svg.append(path);
  }
  span.append(svg, document.createElement('span'));
  return span;
}

export function watchCards(document, onIds) {
  const window = document.defaultView;
  const states = new Map();
  const totals = { performer: new Map(), studio: new Map(), video: new Map() };
  let previousIds = '';
  let frame = null;
  let stopped = false;
  function schedule() { if (!stopped && frame === null) frame = window.requestAnimationFrame(scan); }
  function remove(card, state) {
    state.resize?.disconnect();
    state.label.remove();
    state.footer?.remove();
    state.hr?.remove();
    if (state.media) {
      state.media.classList.remove('cove-filesize-portrait');
      state.media.style.removeProperty('--cove-filesize-portrait-height');
    }
    states.delete(card);
  }
  function scan() {
    frame = null;
    const ids = { performer: new Set(), studio: new Set(), video: new Set() };
    const seen = new Set();
    for (const card of document.querySelectorAll(selector)) {
      const identity = cardIdentity(card);
      if (!identity) continue;
      const { kind, id } = identity;
      ids[kind].add(id);
      seen.add(card);
      let state = states.get(card);
      if (state && (state.key !== `${kind}:${id}` || !card.contains(state.label))) { remove(card, state); state = null; }
      const bytes = totals[kind].get(id);
      const formatted = formatSize(bytes);
      if (!formatted) { if (state) remove(card, state); continue; }
      if (!state) {
        const label = makeLabel(document);
        state = { key: `${kind}:${id}`, label };
        if (kind === 'performer') {
          const body = card.querySelector(':scope > .card-body');
          const media = card.querySelector(':scope > .card-media');
          if (!body || !media) continue;
          label.classList.add('cove-filesize-performer');
          // Age/gender is the body's final native row; adding after it also handles absent ages.
          const aspect = window.getComputedStyle(media).aspectRatio.split('/').map(Number);
          const ratio = aspect.length === 2 && aspect.every(n => n > 0) ? aspect[0] / aspect[1] : 2 / 3;
          const resize = () => {
            const width = media.getBoundingClientRect().width;
            if (width > 0) media.style.setProperty('--cove-filesize-portrait-height', `${Math.max(0, width / ratio - 18)}px`);
          };
          state.media = media;
          resize();
          media.classList.add('cove-filesize-portrait');
          if (window.ResizeObserver) { state.resize = new window.ResizeObserver(resize); state.resize.observe(media); }
          body.append(label);
        } else {
          let footer = card.querySelector(':scope > .card-popovers');
          if (!footer) {
            state.hr = document.createElement('hr'); state.hr.className = 'cove-filesize-divider';
            state.footer = document.createElement('div'); state.footer.className = 'card-popovers cove-filesize-footer';
            footer = state.footer;
            const body = card.querySelector(':scope > .card-body');
            if (body) body.after(state.hr, footer); else card.append(state.hr, footer);
          }
          footer.append(label);
        }
        states.set(card, state);
      }
      const text = state.label.lastElementChild;
      if (text.textContent !== formatted) text.textContent = formatted;
      const title = `${kind === 'video' ? 'Video files' : 'Total attributed files'}: ${formatted} (${bytes.toLocaleString('en-US')} bytes)`;
      if (state.label.title !== title) { state.label.title = title; state.label.setAttribute('aria-label', title); }
    }
    for (const [card, state] of states) if (!seen.has(card)) remove(card, state);
    const normalized = Object.fromEntries(Object.entries(ids).map(([kind, values]) => [kind, [...values].sort((a, b) => a - b)]));
    const key = JSON.stringify(normalized);
    if (key !== previousIds) { previousIds = key; onIds(normalized); }
  }
  const observer = new window.MutationObserver(schedule);
  observer.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['href', 'class'] });
  scan();
  return {
    update(kind, entries) { totals[kind] = new Map(entries.map(entry => [entry.id, entry.bytes])); schedule(); },
    stop() {
      stopped = true; observer.disconnect();
      if (frame !== null) window.cancelAnimationFrame(frame);
      for (const [card, state] of states) remove(card, state);
    },
  };
}

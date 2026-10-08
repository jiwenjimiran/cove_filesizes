// Use the host's normal string criterion state, with a custom DOM editor.
// No React internals: native input events feed Cove's controlled input.
export function decodeRange(value) {
  const bound = text => {
    const match = text.trim().match(/^(\d+(?:\.\d+)?)\s*(B|KB|MB|GB|TB)?$/i);
    if (!match) return { value: '', unit: 'GB' };
    const unit = (match[2] ?? 'B').toUpperCase();
    if (unit === 'MB' || unit === 'GB') return { value: match[1], unit };
    const multiplier = { B: 0.000001, KB: 0.001, TB: 1000000 }[unit];
    return { value: String(Number(match[1]) * multiplier), unit: 'MB' };
  };
  const parts = (value ?? '').split('..');
  return { minimum: bound(parts[0]), maximum: bound(parts.length > 1 ? parts[1] : parts[0]) };
}
export function encodeRange(range) {
  const bound = field => field.value === '' ? '' : `${field.value} ${field.unit}`;
  return `${bound(range.minimum)}..${bound(range.maximum)}`;
}
export function watchFilesizeFilters(document) {
  const window = document.defaultView;
  const editors = new Map();
  function scan() {
    for (const [input, editor] of editors) {
      if (!input.isConnected || !input.closest('[role="tabpanel"][aria-label="Filesize"]')) {
        editor.remove(); editors.delete(input);
      }
    }
    for (const panel of document.querySelectorAll('[role="tabpanel"][aria-label="Filesize"]')) {
      for (const input of panel.querySelectorAll('input[aria-label="Value"]')) {
        if (editors.has(input)) { editors.get(input).sync(); continue; }
        const native = input.parentElement.parentElement;
        if (!native || !panel.contains(native)) continue;
        const editor = document.createElement('div');
        editor.className = 'cove-filesize-range';
        const state = decodeRange(input.value);
        let lastValue = input.value;
        const fields = {};
        function publish() {
          const encoded = encodeRange(state);
          lastValue = encoded;
          const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
          setter.call(input, encoded);
          input.dispatchEvent(new window.Event('input', { bubbles: true }));
        }
        for (const name of ['minimum', 'maximum']) {
          const row = document.createElement('label');
          row.className = 'cove-filesize-range-row';
          const label = document.createElement('span');
          label.textContent = `${name === 'minimum' ? 'Minimum' : 'Maximum'} (inclusive)`;
          const hint = document.createElement('small');
          hint.textContent = name === 'minimum' ? 'Empty means 0 (no lower limit)' : 'Empty means unlimited';
          const controls = document.createElement('span');
          controls.className = 'cove-filesize-range-controls';
          const number = document.createElement('input');
          number.type = 'number'; number.min = '0'; number.step = 'any'; number.inputMode = 'decimal';
          number.setAttribute('aria-label', `${name === 'minimum' ? 'Minimum' : 'Maximum'} filesize (inclusive)`);
          number.placeholder = name === 'minimum' ? '0' : 'Unlimited'; number.value = state[name].value;
          const unit = document.createElement('select');
          unit.setAttribute('aria-label', `${name === 'minimum' ? 'Minimum' : 'Maximum'} filesize unit`);
          for (const value of ['MB', 'GB']) {
            const option = document.createElement('option'); option.value = value; option.textContent = value; unit.append(option);
          }
          unit.value = state[name].unit;
          number.addEventListener('input', () => { state[name].value = number.value; publish(); });
          unit.addEventListener('change', () => { state[name].unit = unit.value; publish(); });
          controls.append(number, unit); row.append(label, hint, controls); editor.append(row);
          fields[name] = { number, unit };
        }
        editor.sync = () => {
          if (input.value === lastValue) return;
          lastValue = input.value;
          Object.assign(state, decodeRange(input.value));
          for (const name of ['minimum', 'maximum']) {
            fields[name].number.value = state[name].value;
            fields[name].unit.value = state[name].unit;
          }
        };
        native.classList.add('cove-filesize-native-editor');
        native.after(editor);
        // Opening a saved filter from 1.1.0 switches its draft to range semantics.
        const equals = native.querySelector('button[data-modifier="EQUALS"]');
        if (equals?.getAttribute('aria-pressed') === 'false') equals.click();
        if (document.activeElement === input) fields.minimum.number.focus();
        editor.remove = () => { native.classList.remove('cove-filesize-native-editor'); window.Element.prototype.remove.call(editor); };
        editors.set(input, editor);
      }
    }
  }
  const observer = new window.MutationObserver(scan);
  observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-label'] });
  // React can reset a controlled input's value without a DOM mutation (Clear all).
  const timer = window.setInterval(scan, 200);
  scan();
  return { stop() { window.clearInterval(timer); observer.disconnect(); for (const editor of editors.values()) editor.remove(); editors.clear(); } };
}

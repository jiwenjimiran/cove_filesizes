import { test } from 'node:test';
import assert from 'node:assert/strict';
import { JSDOM } from 'jsdom';
import { decodeRange, encodeRange, watchFilesizeFilters } from '../frontend/filters.mjs';

const tick = () => new Promise(resolve => setTimeout(resolve, 35));
test('range editor round trips numeric inputs, independent units and empty bounds', () => {
  assert.equal(encodeRange(decodeRange('500 MB..1.2 GB')), '500 MB..1.2 GB');
  assert.equal(encodeRange(decodeRange('..10 GB')), '..10 GB');
  assert.equal(encodeRange(decodeRange('500 MB..')), '500 MB..');
  assert.equal(encodeRange(decodeRange('..')), '..');
});

test('range controls update real React criterion state, survive rerenders, and clean up', async () => {
  const dom = new JSDOM('<div id="root"></div>', { pretendToBeVisual: true });
  globalThis.window = dom.window; globalThis.document = dom.window.document;
  const { createElement: h, useState } = await import('react');
  const { createRoot } = await import('react-dom/client');
  let changePanel, clear, current;
  // Cove 1.5.1 StringEditor / LabeledControl DOM and controlled-input contract.
  function Host() {
    const [value, setValue] = useState('500 MB..10 GB');
    const [label, setLabel] = useState('Filesize');
    changePanel = setLabel; clear = () => setValue(''); current = value;
    return h('div', { role: 'tabpanel', 'aria-label': label },
      h('div', { className: 'space-y-2' },
        h('div', { role: 'group', 'aria-label': 'Match' }, h('button', { 'data-modifier': 'EQUALS' }, '=')),
        h('label', null, h('span', null, 'Value'), h('input', { 'aria-label': 'Value', value, onChange: event => setValue(event.target.value) }))));
  }
  const root = createRoot(document.querySelector('#root')); root.render(h(Host)); await tick();
  const watcher = watchFilesizeFilters(document);
  const minimum = () => document.querySelector('input[aria-label="Minimum filesize (inclusive)"]');
  const maximum = () => document.querySelector('input[aria-label="Maximum filesize (inclusive)"]');
  function fill(input, value) { input.value = value; input.dispatchEvent(new window.Event('input', { bubbles: true })); }
  assert.equal(minimum().type, 'number'); assert.equal(minimum().value, '500');
  assert.equal(document.querySelectorAll('select option').length, 4);
  assert.ok(document.querySelector('[aria-label="Match"]').closest('.cove-filesize-native-editor'));
  fill(minimum(), '700'); await tick(); assert.equal(current, '700 MB..10 GB');
  const unit = document.querySelector('select[aria-label="Maximum filesize unit"]');
  unit.value = 'MB'; unit.dispatchEvent(new window.Event('change', { bubbles: true })); await tick();
  assert.equal(current, '700 MB..10 MB');
  fill(maximum(), ''); await tick(); assert.equal(current, '700 MB..');
  fill(minimum(), ''); await tick(); assert.equal(current, '..');
  assert.equal(document.querySelectorAll('.cove-filesize-range').length, 1);
  fill(minimum(), '2'); await tick(); clear(); await new Promise(resolve => setTimeout(resolve, 250));
  assert.equal(minimum().value, ''); assert.equal(maximum().value, '');
  changePanel('Name'); await tick(); assert.equal(document.querySelectorAll('.cove-filesize-range').length, 0);
  clear(); changePanel('Filesize'); await tick(); assert.equal(minimum().value, ''); assert.equal(maximum().value, '');
  watcher.stop(); assert.equal(document.querySelectorAll('.cove-filesize-range').length, 0);
  assert.equal(document.querySelectorAll('.cove-filesize-native-editor').length, 0);
  root.unmount(); await tick(); dom.window.close(); delete globalThis.window; delete globalThis.document;
});

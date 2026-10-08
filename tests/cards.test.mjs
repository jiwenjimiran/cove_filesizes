import { test } from 'node:test';
import assert from 'node:assert/strict';
import { JSDOM } from 'jsdom';
import { cardIdentity, formatSize, loadSizes, watchCards } from '../frontend/cards.mjs';

const tick = () => new Promise(resolve => setTimeout(resolve, 45));
const card = (kind, id, age = '') => `<div class="${kind === 'video' ? 'video-card' : 'entity-card'}"><a href="/${kind}/${id}"></a><div class="card-media"></div><div class="card-body"><p class="card-title">Name</p>${age}</div><div class="card-popovers"><span>2 scenes</span></div></div>`;
const fixture = html => new JSDOM(html, { url: 'http://localhost/performers', pretendToBeVisual: true });

test('formats decimal units, rounding boundaries, zero and invalid data', () => {
  for (const [bytes, expected] of [[0, '0 KB'], [500, '0.5 KB'], [1000, '1 KB'], [1234567, '1.2 MB'], [1234000000000, '1.2 TB'], [999999999999, '1 TB'], [NaN, null], [-1, null], [undefined, null]]) assert.equal(formatSize(bytes), expected);
});
test('identifies only same-origin direct card links, including hash routes', () => {
  const dom = fixture(card('performer', 7));
  const node = dom.window.document.querySelector('.entity-card');
  assert.deepEqual(cardIdentity(node), { kind: 'performer', id: 7 });
  for (const [href, expected] of [['/#/studio/8', { kind: 'studio', id: 8 }], ['/video/2?tab=files', { kind: 'video', id: 2 }], ['/performer/0', null], ['https://other.test/video/3', null], ['/video/9007199254740993', null]]) {
    node.querySelector('a').href = href; assert.deepEqual(cardIdentity(node), expected);
  }
  dom.window.close();
});
test('inserts below age, uses studio/video footers, handles recycled cards, and cleans up', async () => {
  const dom = fixture(card('performer', 1, '<div class="age">25 years old</div>') + card('studio', 2) + card('video', 3));
  const document = dom.window.document;
  const snapshots = [];
  const watcher = watchCards(document, ids => snapshots.push(ids));
  watcher.update('performer', [{ id: 1, bytes: 1234000000000 }]);
  watcher.update('studio', [{ id: 2, bytes: 0 }]);
  watcher.update('video', [{ id: 3, bytes: 1234567 }]);
  await tick();
  assert.equal(document.querySelector('.age').nextElementSibling.textContent, '1.2 TB');
  assert.equal(document.querySelector('.entity-card:nth-child(2) .card-popovers .cove-filesize').textContent, '0 KB');
  assert.equal(document.querySelector('.video-card .card-popovers .cove-filesize').textContent, '1.2 MB');
  assert.equal(document.querySelectorAll('.cove-filesize svg').length, 3);
  assert.equal(snapshots.length, 1, 'own mutations must not produce request loops');
  document.querySelector('a').href = '/performer/4';
  await tick();
  assert.equal(document.querySelector('.cove-filesize-performer'), null);
  watcher.update('performer', [{ id: 4, bytes: 1000 }]);
  await tick();
  assert.equal(document.querySelectorAll('.cove-filesize-performer').length, 1);
  watcher.stop();
  assert.equal(document.querySelectorAll('.cove-filesize, .cove-filesize-portrait').length, 0);
  assert.equal(document.querySelectorAll('.card-popovers').length, 3);
  dom.window.close();
});
test('supports performers without age and studios without a native footer', async () => {
  const dom = fixture(card('performer', 1) + card('studio', 2).replace('<div class="card-popovers"><span>2 scenes</span></div>', ''));
  const document = dom.window.document;
  const watcher = watchCards(document, () => {});
  watcher.update('performer', [{ id: 1, bytes: 1000 }]); watcher.update('studio', [{ id: 2, bytes: 2000 }]);
  await tick();
  assert.equal(document.querySelector('.card-title').nextElementSibling.textContent, '1 KB');
  assert.equal(document.querySelector('.cove-filesize-footer').textContent, '2 KB');
  watcher.stop();
  assert.equal(document.querySelector('.cove-filesize-footer'), null);
  dom.window.close();
});
test('chunks requests, passes cancellation, and rejects failures', async () => {
  const calls = []; const controller = new AbortController();
  const entries = await loadSizes('performer', Array.from({ length: 205 }, (_, i) => i + 1), async (url, options) => {
    calls.push({ url, options }); return { ok: true, json: async () => JSON.parse(options.body).ids.map(id => ({ id, bytes: id })) };
  }, controller.signal);
  assert.equal(entries.length, 205);
  assert.deepEqual(calls.map(call => JSON.parse(call.options.body).ids.length), [100, 100, 5]);
  assert.equal(calls[0].options.signal, controller.signal);
  await assert.rejects(loadSizes('studio', [1], async () => ({ ok: false, status: 403 })), /403/);
});

import { chromium } from '@playwright/test';
import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import assert from 'node:assert/strict';

const css = await readFile(new URL('../frontend/filesizes.css', import.meta.url), 'utf8');
const module = await readFile(new URL('../frontend/cards.mjs', import.meta.url), 'utf8');
const server = createServer((request, response) => {
  if (request.url === '/cards.mjs') { response.setHeader('Content-Type', 'text/javascript'); response.end(module); return; }
  response.setHeader('Content-Type', 'text/html; charset=utf-8');
  response.end(`<!doctype html><style>
    *{box-sizing:border-box} body{background:#14161d;color:#eee;font:14px/20px Arial;margin:24px;--color-muted:#9398ac;--color-border:#343847}
    .grid{display:flex;gap:24px;align-items:flex-start}.entity-card,.video-card{position:relative;display:flex;flex-direction:column;width:240px;border:1px solid #343847;border-radius:8px;overflow:hidden;background:#1e212b}
    .card-media{aspect-ratio:2/3;flex-shrink:0;background:linear-gradient(130deg,#44556c,#212737);position:relative}.card-body{display:flex;flex-direction:column;gap:4px;padding:10px;border-top:1px solid #343847;flex:1}
    .card-title{margin:0;font-weight:600;line-height:20px}.age{font-size:11px;line-height:16px}
    .card-popovers{display:flex;flex-wrap:wrap;align-items:center;justify-content:center;gap:4px;min-height:28px;padding:6px 8px;font-size:11px;color:#9398ac}hr{border:0;border-top:1px solid #343847;margin:0}
    .studio .card-media,.video-card .card-media{aspect-ratio:16/9}.entity-card>a,.video-card>a{position:absolute;inset:0}
    ${css}
  </style><div class="grid">${['performer', 'performer', 'studio', 'video'].map((kind, i) => `<div class="${kind === 'video' ? 'video-card' : 'entity-card'} ${kind}"><a href="/${kind}/${i + 1}"></a><div class="card-media"></div><div class="card-body"><p class="card-title">${i === 1 ? 'Performer with a longer name<br>over two lines' : kind[0].toUpperCase() + kind.slice(1)}</p>${i === 0 ? '<div class="age">25 years old</div>' : ''}</div><hr><div class="card-popovers"><span>♧ 12</span><span>◇ 5</span><span>□ 3</span></div></div>`).join('')}</div>
    <script type="module">import {watchCards} from '/cards.mjs'; window.watcher=watchCards(document,()=>{});window.ready=true;</script>`);
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 1300, height: 900 } });
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.waitForFunction(() => window.ready);
  const measure = () => page.locator('.performer').evaluateAll(cards => cards.map(card => card.getBoundingClientRect().height));
  for (const width of [150, 240, 320]) {
    await page.locator('.performer').evaluateAll((cards, width) => cards.forEach(card => card.style.width = `${width}px`), width);
    await page.evaluate(() => window.watcher.update('performer', []));
    await page.waitForTimeout(80);
    const before = await measure();
    await page.evaluate(() => window.watcher.update('performer', [{ id: 1, bytes: 1234000000000 }, { id: 2, bytes: 125000000000 }]));
    await page.waitForTimeout(80);
    const after = await measure();
    for (let i = 0; i < before.length; i++) assert.ok(Math.abs(before[i] - after[i]) <= 1, `width ${width}, performer ${i}: ${before[i]} -> ${after[i]}`);
    console.log(`PASS performer heights at ${width}px: ${before.join(', ')} -> ${after.join(', ')}`);
  }
  await page.locator('.performer').evaluateAll(cards => cards.forEach(card => card.style.width = '240px'));
  await page.evaluate(() => { window.watcher.update('studio', [{ id: 3, bytes: 3450000000000 }]); window.watcher.update('video', [{ id: 4, bytes: 6700000000 }]); });
  await page.waitForTimeout(100);
  const unselectedHeights = await measure();
  await page.locator('.entity-card, .video-card').evaluateAll(cards => cards.forEach(card => {
    const link = card.querySelector(':scope > a');
    card.dataset.restoreHref = link.getAttribute('href');
    link.remove(); card.classList.add('ring-2');
  }));
  await page.waitForTimeout(80);
  for (const label of await page.locator('.cove-filesize').all()) assert.ok(await label.isVisible());
  assert.equal(await page.locator('.cove-filesize').count(), 4);
  assert.deepEqual(await measure(), unselectedHeights);
  await page.locator('.entity-card, .video-card').evaluateAll(cards => cards.forEach(card => {
    const link = document.createElement('a'); link.href = card.dataset.restoreHref; card.prepend(link); card.classList.remove('ring-2');
  }));
  await page.waitForTimeout(80);
  assert.equal(await page.locator('.cove-filesize').count(), 4);
  assert.deepEqual(await measure(), unselectedHeights);
  console.log('PASS labels stay visible on selection/deselection for all three kinds');
  await mkdir(new URL('../artifacts/', import.meta.url), { recursive: true });
  await page.screenshot({ path: new URL('../artifacts/cards-preview.png', import.meta.url).pathname.replace(/^\/(\w:)/, '$1') });
  const resized = await measure();
  await page.evaluate(() => window.watcher.stop());
  await page.waitForTimeout(80);
  assert.equal(await page.locator('.cove-filesize').count(), 0);
  assert.deepEqual(await measure(), resized);
  console.log('PASS cleanup and responsive portrait compensation');
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }

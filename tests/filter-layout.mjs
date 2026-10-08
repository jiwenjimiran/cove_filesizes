import { chromium } from '@playwright/test';
import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import assert from 'node:assert/strict';

const css = await readFile(new URL('../frontend/filesizes.css', import.meta.url), 'utf8');
const module = await readFile(new URL('../frontend/filters.mjs', import.meta.url), 'utf8');
const server = createServer((request, response) => {
  if (request.url === '/filters.mjs') { response.setHeader('Content-Type', 'text/javascript'); response.end(module); return; }
  response.setHeader('Content-Type', 'text/html');
  response.end(`<!doctype html><style>*{box-sizing:border-box}body{background:#14161d;color:#eee;font:14px Arial;margin:24px;--color-muted:#9398ac;--color-border:#343847;--color-input:#202330}main{max-width:500px} ${css}</style>
    <main><h2>Filesize</h2><div role="tabpanel" aria-label="Filesize"><div><div role="group" aria-label="Match"><button>=</button></div><label><span>Value</span><input aria-label="Value" value=""></label></div></div></main>
    <script type="module">import {watchFilesizeFilters} from '/filters.mjs'; window.values=[];document.querySelector('input').addEventListener('input',e=>values.push(e.target.value));window.watcher=watchFilesizeFilters(document);window.ready=true;</script>`);
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch();
try {
  const page = await browser.newPage(); await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.waitForFunction(() => window.ready);
  assert.equal(await page.getByRole('group', { name: 'Match' }).isVisible(), false);
  assert.equal(await page.getByLabel('Value', { exact: true }).isVisible(), false);
  const min = page.getByLabel('Minimum filesize (inclusive)', { exact: true });
  const max = page.getByLabel('Maximum filesize (inclusive)', { exact: true });
  for (const width of [320, 768]) {
    await page.setViewportSize({ width, height: 700 });
    assert.ok(await min.isVisible() && await max.isVisible());
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  }
  await min.fill('500'); await page.getByLabel('Minimum filesize unit', { exact: true }).selectOption('MB');
  await max.fill('1.2');
  assert.equal(await page.evaluate(() => values.at(-1)), '500 MB..1.2 GB');
  await max.fill(''); assert.equal(await page.evaluate(() => values.at(-1)), '500 MB..');
  await min.fill(''); assert.equal(await page.evaluate(() => values.at(-1)), '..');
  await mkdir(new URL('../artifacts/', import.meta.url), { recursive: true });
  await page.screenshot({ path: new URL('../artifacts/filter-preview.png', import.meta.url).pathname.replace(/^\/(\w:)/, '$1') });
  await page.evaluate(() => watcher.stop()); assert.equal(await page.getByLabel('Value', { exact: true }).isVisible(), true);
  console.log('PASS range editor visibility, numeric inputs, MB/GB units, empty bounds and responsive layout');
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }

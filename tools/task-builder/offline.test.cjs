const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const http = require('node:http');
const { pathToFileURL } = require('node:url');
const { chromium } = require('playwright');
const XLSX = require('xlsx');
const root = path.resolve(__dirname, '../..');

test('offline resources, failed Sheets, manual entry and Excel/CSV export', async () => {
  const server = http.createServer(async (req, res) => {
    if (req.url.startsWith('/api/')) { res.writeHead(502); res.end('offline'); return; }
    const name = req.url.slice(1);
    if (name !== 'task_builder_tailwind.html' && !/^task-builder-assets\/[a-zA-Z0-9_./-]+$/.test(name)) {
      res.writeHead(404); res.end(); return;
    }
    try {
      const contentType = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript', '.woff2': 'font/woff2' }[path.extname(name)];
      res.setHeader('Content-Type', contentType || 'text/plain');
      res.end(await fs.readFile(path.join(root, name)));
    } catch { res.writeHead(404); res.end(); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  let browser;
  try {
    browser = await chromium.launch({ channel: 'msedge', headless: true });
    for (const url of [
      `http://127.0.0.1:${server.address().port}/task_builder_tailwind.html`,
      pathToFileURL(path.join(root, 'task_builder_tailwind.html')).href,
    ]) {
      const context = await browser.newContext({ acceptDownloads: true });
      const external = [], errors = [];
      await context.route('**/*', route => {
        const requested = new URL(route.request().url());
        if (requested.protocol === 'file:' || requested.hostname === '127.0.0.1') return route.continue();
        // Local antivirus injects this script into browser pages. Block it too;
        // it is not a resource requested by the checked-in application.
        if (requested.hostname === 'me.kis.v2.scr.kaspersky-labs.com') return route.abort();
        external.push(requested.href); return route.abort();
      });
      const page = await context.newPage();
      page.on('pageerror', error => errors.push(error.message));
      await page.goto(url);
      await page.waitForFunction(() => document.querySelector('#cloud-status').textContent.includes('失敗') || location.protocol === 'file:');
      await page.locator('#field-title').fill('離線測試任務');
      await page.locator('#field-date').fill('2099-01-01');
      await page.locator('#field-clock').fill('13:00');
      await page.locator('#field-target').fill('TEST-001');
      await page.locator('#field-exclude').fill('TEST-002');
      await page.locator('#btn-submit').click();
      assert.equal(await page.evaluate(() => tasks.length), 1);
      assert.ok(await page.locator('svg.lucide').count() > 0);
      assert.equal(await page.locator('body').evaluate(el => getComputedStyle(el).backgroundColor), 'rgb(248, 250, 252)');
      assert.equal(await page.evaluate(async () => {
        await document.fonts.load('14px "Noto Sans TC Variable"', '離線任務');
        return document.fonts.check('14px "Noto Sans TC Variable"', '離線任務');
      }), true);
      for (const exporter of ['exportToExcel', 'exportToCsv']) {
        const downloadPromise = page.waitForEvent('download');
        await page.evaluate(name => window[name](), exporter);
        const download = await downloadPromise;
        const contents = await fs.readFile(await download.path());
        if (exporter === 'exportToExcel') {
          const wb = XLSX.read(contents);
          const rows = XLSX.utils.sheet_to_json(wb.Sheets.Tasks, { header: 1 });
          assert.ok(rows.some(row => row.includes('離線測試任務') && row.includes('TEST-001') && row.includes('TEST-002')));
        } else assert.ok(contents.toString('utf8').includes('離線測試任務'));
      }
      assert.deepEqual(external, []);
      assert.deepEqual(errors, []);
      await context.close();
    }
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
});

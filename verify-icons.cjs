const { chromium } = require('C:/Users/dzand/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const assert = require('node:assert/strict');
const baseUrl = process.env.REPORTSA_URL || 'http://localhost:5050';

(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        for (const path of ['/Requests/Report', '/Requests/Track', '/Account/Login', '/Requests/Nearby']) {
            await page.goto(baseUrl + path, { waitUntil: 'domcontentloaded' });
            await page.waitForFunction(() => document.querySelectorAll('svg.lucide').length > 0);
            assert.equal(await page.locator('i[data-lucide]').count(), 0, `Missing icons on ${path}`);
            assert.equal(await page.locator('script[src*="lucide.min.js"]').count(), 0);
            const iconCount = await page.locator('svg.lucide').count();
            await page.evaluate(() => window.lucide.createIcons());
            assert.equal(await page.locator('svg.lucide').count(), iconCount);
            if (path === '/Account/Login') {
                await page.locator('#password-toggle').click();
                assert.equal(await page.locator('#Password').getAttribute('type'), 'text');
                assert.equal(await page.locator('.hide-password-icon').isVisible(), true);
            }
            if (path === '/Requests/Report') {
                await page.locator('[data-photo-input]').setInputFiles({
                    name: 'preview.png', mimeType: 'image/png',
                    buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1sAAAAASUVORK5CYII=', 'base64')
                });
                assert.equal(await page.locator('[data-photo-preview] svg[data-lucide="x"]').count(), 1);
            }
        }
        assert.deepEqual(errors, []);
        console.log('PASS: reduced icon bundle renders all page icons, category tiles, password toggle and dynamic photo controls.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const baseUrl = process.env.REPORTSA_URL || 'http://localhost:5050';

(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        for (const mode of ['coarse', 'unknown', 'accurate', 'improved', 'manual', 'stalled']) {
            const context = await browser.newContext();
            await context.addInitScript(mode => {
                const fix = accuracy => ({ coords: { latitude: -33.9249, longitude: accuracy === 8 ? 18.42415 : 18.4241, accuracy } });
                let pending;
                window.gpsCleared = 0;
                Object.defineProperty(navigator, 'geolocation', { value: {
                    getCurrentPosition(success) { setTimeout(() => success(fix(mode === 'accurate' ? 8 : mode === 'unknown' ? undefined : 3852)), 0); },
                    watchPosition(success, error) {
                        window.lateGps = () => success(fix(8));
                        if (mode === 'stalled') return 1;
                        pending = setTimeout(() => mode === 'improved' || mode === 'manual' ? success(fix(8)) : error({ code: 2 }), mode === 'manual' ? 2000 : 100);
                        return 1;
                    },
                    clearWatch() { clearTimeout(pending); window.gpsCleared++; }
                } });
            }, mode);
            const page = await context.newPage();
            await page.route('**/Requests/LocateMunicipality?*', route => {
                const accuracy = Number(new URL(route.request().url()).searchParams.get('accuracy') ?? 'NaN');
                route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
                    code: 'CPT', label: 'Cape Town Metropolitan Municipality', area: 'Cape Town, Western Cape',
                    address: Number.isFinite(accuracy) && accuracy <= 100 ? 'Darling Street, Cape Town' : null
                }) });
            });
            let lookupRequests = 0;
            page.on('request', request => { if (request.url().includes('/LocateMunicipality?')) lookupRequests++; });
            await page.goto(`${baseUrl}/Requests/Report`);
            const started = Date.now();
            await page.locator('#locate-button').click();
            if (mode === 'stalled') {
                await page.waitForFunction(() => document.querySelector('#Latitude').value !== '', null, { timeout: 1000 });
                assert.equal(lookupRequests, 0, 'Coordinates should appear before GPS refinement finishes');
            }
            if (mode === 'manual') {
                await page.waitForFunction(() => document.querySelector('#location-feedback').textContent.includes('better GPS fix'));
                await page.locator('#use-map-centre').click();
                await page.waitForFunction(() => document.querySelector('#MunicipalityCode').value === 'CPT');
                const selected = await page.locator('#Longitude').inputValue();
                await page.evaluate(() => window.lateGps());
                assert.equal(await page.locator('#Longitude').inputValue(), selected);
                assert(await page.evaluate(() => window.gpsCleared > 0));
                assert.match(await page.locator('#location-feedback').innerText(), /Nearest mapped road/);
            } else if (mode === 'coarse' || mode === 'unknown' || mode === 'stalled') {
                await page.waitForFunction(() => document.querySelector('#MunicipalityCode').value === 'CPT');
                assert.match(await page.locator('#Location').inputValue(), /^Approximate area:.*Cape Town/);
                assert.equal(await page.locator('#Latitude').inputValue(), '-33.924900');
                assert.match(await page.locator('#location-feedback').innerText(), /Approximate location/);
                assert.equal(lookupRequests, 1);
                if (mode === 'stalled') {
                    assert(Date.now() - started < 5000, 'Stalled GPS refinement must finish within five seconds');
                    assert(await page.evaluate(() => window.gpsCleared > 0));
                }
            } else {
                await page.waitForFunction(() => document.querySelector('#MunicipalityCode').value === 'CPT');
                assert.match(await page.locator('#Location').inputValue(), /Darling Street/);
                assert.match(await page.locator('#location-feedback').innerText(), /GPS accuracy: within about 8 m/);
                assert.equal(await page.locator('#Longitude').inputValue(), '18.424150');
                if (mode === 'improved') assert(await page.evaluate(() => window.gpsCleared > 0));
                assert.equal(lookupRequests, 1);
            }
            assert.equal(await page.locator('#locate-button').isEnabled(), true);
            await context.close();
        }
        console.log('PASS: GPS coordinates appear immediately; stalled refinement finishes within five seconds; better fixes replace coarse positions; manual pins resist late callbacks; coarse fallback works.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

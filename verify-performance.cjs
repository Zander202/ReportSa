const assert = require('node:assert/strict');
const baseUrl = process.env.REPORTSA_URL || 'http://localhost:5050';

(async () => {
    const asset = await fetch(`${baseUrl}/js/site.js?v=performance-check`, {
        headers: { 'Accept-Encoding': 'gzip' }
    });
    assert.equal(asset.status, 200);
    assert.equal(asset.headers.get('content-encoding'), 'gzip');
    assert.match(asset.headers.get('cache-control'), /max-age=31536000,immutable/);
    assert.match(await asset.text(), /2500/);
    const page = await fetch(`${baseUrl}/Requests/Report`, {
        headers: { 'Accept-Encoding': 'gzip' }
    });
    assert.equal(page.status, 200);
    assert.equal(page.headers.get('content-encoding'), null, 'Private HTML must not use asset compression');
    assert.doesNotMatch(page.headers.get('cache-control') || '', /public|immutable/);
    console.log('PASS: static JavaScript is compressed and cached; private HTML is excluded.');
})().catch(error => { console.error(error); process.exitCode = 1; });

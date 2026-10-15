// UI checks use isolated storage/service; no production library is changed.
// FUNCLIPPER_SHARE_KEY=<isolated key> node v2-ui.cjs [http://127.0.0.1:5186]
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const path = require('node:path');
const fs = require('node:fs/promises');
const assert = require('node:assert/strict');

const base = process.argv[2] || 'http://127.0.0.1:5186';
const key = process.env.FUNCLIPPER_SHARE_KEY;
if (!key || new URL(base).hostname !== '127.0.0.1') throw new Error('Use an isolated 127.0.0.1 service and FUNCLIPPER_SHARE_KEY.');
const headers = { 'X-FunClipper-Key': key };
const evidence = path.resolve(__dirname, '../../../.impeccable/review');
const shot = (page, name) => page.screenshot({ path: path.join(evidence, `v2-web-${name}.png`), fullPage: true });

(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.PLAYWRIGHT_EXECUTABLE_PATH ? { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH } : {}) });
  try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await fs.mkdir(evidence, { recursive: true });
    const existing = await (await fetch(`${base}/api/clips`, { headers })).json();
    assert.equal(existing.length, 0, 'Use fresh isolated storage.');

    await page.goto(base);
    await page.locator('#connect-panel').waitFor({ state: 'visible' });
    assert.equal(await page.locator('#library-actions').isVisible(), false);
    await shot(page, 'connect-desktop');
    await page.locator('#access-key').fill('wrong-isolated-key');
    await page.locator('#connect').click();
    await page.waitForFunction(() => document.getElementById('message').classList.contains('error'));
    assert.equal(await page.locator('#connect-panel').isVisible(), true);

    await page.goto(`${base}/#key=${encodeURIComponent(key)}`);
    await page.locator('#empty-state').waitFor({ state: 'visible' });
    assert.equal(new URL(page.url()).hash, '');
    assert.equal(await page.locator('#scope-label').innerText(), 'Local preview');
    assert.match(await page.locator('#local-notice').innerText(), /this PC/);
    await shot(page, 'empty-desktop');
    // Fragment navigation intentionally retains the current document's focus.
    await page.reload();
    await page.locator('#empty-state').waitFor({ state: 'visible' });
    await page.keyboard.press('Tab');
    assert.equal(await page.locator('.skip-link').evaluate(link => link === document.activeElement), true);
    assert.equal(await page.locator('.skip-link').evaluate(link => getComputedStyle(link).outlineStyle), 'solid');
    await page.locator('#show-upload').click();
    assert.equal(await page.locator('#show-upload').getAttribute('aria-expanded'), 'true');
    await page.locator('#cancel-upload').click();
    assert.equal(await page.locator('#upload-panel').isVisible(), false);
    assert.equal(await page.locator('#show-upload').evaluate(button => button === document.activeElement), true);

    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator('#show-upload').click();
    await shot(page, 'upload-mobile');
    await page.locator('#clip-file').setInputFiles(path.join(__dirname, 'smoke.mp4'));
    await page.locator('#clip-title').fill('Synthetic test clip (2 seconds)');
    await page.locator('#upload').click();
    await page.locator('.clip-title').waitFor({ state: 'visible' });
    await page.locator('#upload-panel').waitFor({ state: 'hidden' });
    await page.locator('.clip-preview.has-frame').waitFor({ state: 'visible' });
    const href = await page.locator('.clip-title').getAttribute('href');
    assert.equal(await page.locator('.clip-preview video').evaluate(video => video.muted && !video.autoplay && !video.loop && video.preload === 'metadata'), true);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await shot(page, 'mobile');
    await page.setViewportSize({ width: 768, height: 900 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.setViewportSize({ width: 1440, height: 900 });
    await shot(page, 'desktop');

    // Clipboard denial keeps a selectable recovery link, rather than claiming success.
    await page.evaluate(() => Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: () => Promise.reject(new Error('Clipboard unavailable')) } }));
    await page.locator('.clip-row button', { hasText: 'Copy link' }).click();
    await page.locator('#copy-fallback').waitFor({ state: 'visible' });
    assert.equal(await page.locator('#copy-url').inputValue(), `${base}${href}`);
    assert.equal(await page.locator('#copy-url').evaluate(input => input.selectionEnd - input.selectionStart), `${base}${href}`.length);

    // Hold one upload at the browser boundary, then exercise the real XHR abort path.
    let releaseUpload;
    const heldUpload = new Promise(resolve => { releaseUpload = resolve; });
    let sawUpload;
    const uploadStarted = new Promise(resolve => { sawUpload = resolve; });
    await page.route('**/api/clips', async route => {
      if (route.request().method() !== 'POST') return route.continue();
      sawUpload();
      await heldUpload;
      await route.abort().catch(() => {});
    });
    await page.locator('#show-upload').click();
    await page.locator('#clip-file').setInputFiles(path.join(__dirname, 'smoke.mp4'));
    await page.locator('#upload').click();
    await uploadStarted;
    assert.equal(await page.locator('#upload').isDisabled(), true);
    await page.locator('#cancel-upload').click();
    releaseUpload();
    await page.waitForFunction(() => document.getElementById('message').textContent === 'Upload cancelled.');
    assert.equal(await page.locator('#upload').isDisabled(), false);
    await page.unroute('**/api/clips');

    await page.goto(`${base}${href}`);
    await page.locator('#clip').waitFor({ state: 'visible' });
    await page.waitForFunction(() => document.getElementById('video').readyState >= 2);
    assert.equal(await page.locator('#video').evaluate(video => video.duration), 2);
    assert.match(await page.locator('#expiry').innerText(), /expires/);
    assert.equal(await page.locator('#download').getAttribute('href'), `/media/${href.split('/').pop()}`);
    // Decode a real frame before capturing; metadata readiness can retain Chrome's buffering indicator.
    await page.locator('#video').evaluate(video => video.play());
    await page.waitForFunction(() => document.getElementById('video').currentTime > 0.25);
    await page.locator('#video').evaluate(video => video.pause());
    await shot(page, 'viewer-desktop');
    await page.setViewportSize({ width: 390, height: 844 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await shot(page, 'viewer-mobile');
    await page.emulateMedia({ reducedMotion: 'reduce' });
    assert.equal(await page.locator('#copy-link').evaluate(button => getComputedStyle(button).transitionDuration), '0s');

    await page.goto(`${base}/c/00000000000000000000000000000000`);
    await page.waitForFunction(() => document.getElementById('message').classList.contains('error'));
    assert.match(await page.locator('#message').innerText(), /unavailable|expired/);
    await page.goto(base);
    await page.locator('.clip-title').waitFor({ state: 'visible' });
    page.once('dialog', dialog => dialog.accept());
    await page.locator('.delete').click();
    await page.locator('#empty-state').waitFor({ state: 'visible' });
    await page.locator('#disconnect').click();
    assert.equal(await page.locator('#connect-panel').isVisible(), true);
    assert.equal(await page.locator('#clips-panel').isVisible(), false);
    assert.equal(errors.length, 0, errors.join('\n'));
    console.log('PASS: invalid/fragment auth, keyboard focus, real upload/preview/playback, mobile/tablet fit, clipboard recovery, upload abort, expiry/unavailable state, deletion, disconnect, reduced motion; no JS errors.');
    console.log(`Evidence: ${evidence}/v2-web-*.png`);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });

// Captures the 1200 × 630 link-preview images in Halgoworks/wwwroot/og from a running copy of the site.
// Usage: dotnet run --project Halgoworks, then `npm run og-images` (BASE defaults to http://localhost:5273).
import { mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { launch, labs } from './browser.mjs';

const base = (process.env.BASE ?? 'http://localhost:5273').replace(/\/$/, '');
const out = fileURLToPath(new URL('../Halgoworks/wwwroot/og/', import.meta.url));
await mkdir(out, { recursive: true });

const browser = await launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
for (const name of ['home', ...labs]) {
    await page.goto(name === 'home' ? `${base}/` : `${base}/${name}`, { waitUntil: 'networkidle' });
    await page.waitForSelector('h1');
    // Let the 3D scenes draw a few frames, then step a lab partway so the picture has something going on.
    await page.waitForTimeout(1500);
    if (name !== 'home') {
        await page.addStyleTag({ content: '.topbar, .lab-facts, .toolbar, .share-notice { display: none !important } .lab-header { padding-top: 26px !important; padding-bottom: 18px !important }' });
        const steps = await page.locator('.playback input[type=range]').getAttribute('max');
        await page.locator('.playback input[type=range]').evaluate((el, v) => { el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); }, String(Math.round(Number(steps) * 0.6)));
        await page.waitForTimeout(600);
    }
    await page.screenshot({ path: `${out}${name}.png` });
    console.log(`og/${name}.png`);
}
await browser.close();

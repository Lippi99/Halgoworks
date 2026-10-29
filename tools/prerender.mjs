// Pre-renders the published site for search engines and link previews.
//
// Blazor WebAssembly builds its pages in the browser, so without this every URL serves the same nearly empty
// index.html. This script serves the published files, opens each page in a headless browser, and saves the
// rendered page as static HTML: the full text, the title, the description, the canonical link, Open Graph
// tags and structured data are then in the file itself. Blazor still starts on top and takes over.
//
// It also writes sitemap.xml, robots.txt and 404.html (static hosts serve that with a real 404 status).
//
// Usage:
//   dotnet publish Halgoworks -c Release -o publish
//   cd tools && SITE_URL=https://your.domain npm run prerender
//
// Each page is written as <slug>.html, which GitHub Pages, Netlify and Cloudflare Pages serve at /<slug>.

import http from 'node:http';
import { readFile, writeFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { brotliCompressSync, gzipSync, constants } from 'node:zlib';
import { launch, labs } from './browser.mjs';

const site = process.env.SITE_URL?.replace(/\/$/, '');
if (!site || !/^https?:\/\//.test(site)) {
    console.error('Set SITE_URL to the site\'s public address, for example SITE_URL=https://halgoworks.dev');
    process.exit(1);
}

const root = path.resolve(process.env.PUBLISH_DIR ?? fileURLToPath(new URL('../publish/wwwroot', import.meta.url)));
const shell = await readFile(path.join(root, 'index.html'), 'utf8');
if (shell.includes('data-prerendered')) {
    console.error(`${root} is already pre-rendered. Run dotnet publish again first.`);
    process.exit(1);
}

const types = {
    '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
    '.json': 'application/json', '.wasm': 'application/wasm', '.dat': 'application/octet-stream', '.dll': 'application/octet-stream',
    '.png': 'image/png', '.svg': 'image/svg+xml', '.ico': 'image/x-icon', '.woff2': 'font/woff2', '.txt': 'text/plain', '.xml': 'application/xml',
};

// A minimal static server: real files when they exist, the original app shell for every page route.
const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, 'http://localhost');
    const file = path.join(root, decodeURIComponent(url.pathname));
    try {
        if (file.startsWith(root) && (await stat(file)).isFile()) {
            res.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' });
            res.end(await readFile(file));
            return;
        }
    } catch {
        // Not a file: fall through to the app shell.
    }

    res.writeHead(200, { 'content-type': types['.html'] });
    res.end(shell);
});
await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const local = `http://127.0.0.1:${server.address().port}`;

// Writes a file plus the .br and .gz copies that dotnet publish makes for every asset. Hosts that serve those
// would otherwise hand out the stale, empty index.html.
async function write(name, text) {
    const data = Buffer.from(text, 'utf8');
    await writeFile(path.join(root, name), data);
    await writeFile(path.join(root, `${name}.br`), brotliCompressSync(data, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }));
    await writeFile(path.join(root, `${name}.gz`), gzipSync(data, { level: 9 }));
}

const routes = [
    { path: '/', file: 'index.html' },
    ...labs.map((slug) => ({ path: `/${slug}`, file: `${slug}.html` })),
    { path: '/not-found', file: '404.html' },
];

const blazorScript = /<script src="(_framework\/blazor\.webassembly[^"]*\.js)"[^>]*><\/script>/;
if (!blazorScript.test(shell)) throw new Error('Could not find the Blazor script in index.html');
const appStart = shell.indexOf('<div id="app">');
const appEnd = shell.indexOf('<div id="blazor-error-ui"');
if (appStart < 0 || appEnd < 0) throw new Error('Could not find the #app element in index.html');

const browser = await launch();
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const problems = [];
page.on('pageerror', (e) => problems.push(e.message));

for (const route of routes) {
    await page.goto(local + route.path, { waitUntil: 'networkidle' });
    await page.waitForSelector('main h1');
    await page.waitForFunction(() => document.querySelector('head [data-seo]'));
    const rendered = await page.evaluate(() => {
        const app = document.getElementById('app').cloneNode(true);
        // Canvases (3D scenes) and live map tiles mean nothing without the running app.
        app.querySelectorAll('canvas, .realmap').forEach((n) => n.replaceChildren());
        const head = [...document.head.querySelectorAll('[data-seo]')].map((n) => {
            const copy = n.cloneNode(true);
            copy.setAttribute('data-prerendered', '');
            return copy.outerHTML;
        });
        return { title: document.title, head, app: app.innerHTML };
    });

    // Rendered locally, so absolute links point at the local server; give them the real address.
    const toSite = (html) => html.split(`${local}/`).join(`${site}/`).split(local).join(site);
    const escape = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;');
    let html = shell.slice(0, appStart)
        + `<div id="app">${rendered.app}</div>\n\n    `
        + shell.slice(appEnd);
    html = html.replace(/<title>[^<]*<\/title>/, `<title>${escape(rendered.title)}</title>`);
    html = html.replace('</head>', `    ${rendered.head.join('\n    ')}\n</head>`);
    // Start Blazor by hand so the pre-rendered head tags can be dropped once the live ones exist.
    html = html.replace(blazorScript, (_, src) => `<script src="${src}" autostart="false"></script>
    <script>
        Blazor.start().then(() => document.querySelectorAll('head [data-prerendered]').forEach((n) => n.remove()));
    </script>`);
    await write(route.file, toSite(html));
    console.log(`${route.path.padEnd(14)} → ${route.file}  "${rendered.title}"`);
}

await browser.close();
server.close();

const today = new Date().toISOString().slice(0, 10);
const urls = ['/', ...labs.map((slug) => `/${slug}`)];
await write('sitemap.xml', `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${urls.map((u) => `  <url><loc>${site}${u === '/' ? '/' : u}</loc><lastmod>${today}</lastmod></url>`).join('\n')}
</urlset>
`);
await write('robots.txt', `User-agent: *
Allow: /

Sitemap: ${site}/sitemap.xml
`);
console.log('sitemap.xml, robots.txt');

if (problems.length) {
    console.error(`Page errors while rendering:\n${problems.join('\n')}`);
    process.exit(1);
}

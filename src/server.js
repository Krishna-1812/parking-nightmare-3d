// Dev server for local testing. Serves the built game from the repo root.
// Cache-first service worker note: after every rebuild, unregister service
// workers and delete caches in the browser, or you will test the old build.
//
//   node src/server.js            -> http://localhost:8377
//
// POST /shot?name=foo writes a base64 data-URL body to shots/foo.png, which is
// how headless screenshots get out of the browser.
const http = require('http'), fs = require('fs'), path = require('path');

const ROOT = path.resolve(__dirname, '..');
const SHOTS = path.join(ROOT, 'shots');
const PORT = process.env.PORT || 8377;

const MIME = {
  '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json',
  '.webmanifest': 'application/manifest+json', '.png': 'image/png',
  '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.css': 'text/css',
};

http.createServer((req, res) => {
  if (req.method === 'POST' && req.url.startsWith('/shot')) {
    const name = (req.url.split('=')[1] || 'shot').replace(/[^a-z0-9_-]/gi, '');
    let body = '';
    req.on('data', c => body += c);
    req.on('end', () => {
      try {
        fs.mkdirSync(SHOTS, { recursive: true });
        const b64 = body.replace(/^data:image\/\w+;base64,/, '');
        fs.writeFileSync(path.join(SHOTS, name + '.png'), Buffer.from(b64, 'base64'));
        res.writeHead(200, { 'Access-Control-Allow-Origin': '*' });
        res.end('saved');
      } catch (e) { res.writeHead(500); res.end(String(e)); }
    });
    return;
  }
  // resolve inside ROOT only — no climbing out with ../
  const rel = decodeURIComponent(req.url === '/' ? 'index.html' : req.url.split('?')[0]);
  const p = path.join(ROOT, rel);
  if (!p.startsWith(ROOT)) { res.writeHead(403); res.end('nope'); return; }
  fs.readFile(p, (e, d) => {
    if (e) { res.writeHead(404); res.end('nope'); return; }
    res.writeHead(200, { 'Content-Type': MIME[path.extname(p)] || 'application/octet-stream' });
    res.end(d);
  });
}).listen(PORT, () => console.log('serving ' + ROOT + ' on http://localhost:' + PORT));

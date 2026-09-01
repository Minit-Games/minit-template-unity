// Zero-dependency static server for the bundled game.
//   node tools/serve.mjs [dir] [port]
// Exists because the .wasm must be served as application/wasm and because the
// bundle has to be exercised over http(s), not file://.
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { join, extname, normalize } from 'node:path';

const root = process.argv[2] || 'dist/Mole Mayhem';
const port = Number(process.argv[3] || 8137);
const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png',
  '.css': 'text/css; charset=utf-8', '.ogg': 'audio/ogg', '.wav': 'audio/wav',
  '.data': 'application/octet-stream', '.symbols': 'application/octet-stream',
};

createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost');
    let p = decodeURIComponent(url.pathname);
    if (p === '/') p = '/index.html';
    const file = join(root, normalize(p).replace(/^(\.\.[/\\])+/, ''));
    const info = await stat(file);
    if (!info.isFile()) throw new Error('not a file');
    const body = await readFile(file);

    // Unity's Minit build ships Brotli with decompressionFallback OFF, so the
    // browser must decompress natively -- which it only does when the response
    // carries Content-Encoding. Without this the loader receives raw Brotli
    // bytes and the game never starts. The real type comes from the extension
    // UNDER the .br (foo.wasm.br -> application/wasm).
    const headers = { 'Cache-Control': 'no-store', 'Content-Length': body.length };
    let name = file;
    const encoding = { '.br': 'br', '.gz': 'gzip' }[extname(file).toLowerCase()];
    if (encoding) {
      headers['Content-Encoding'] = encoding;
      name = file.slice(0, -3);
    }
    headers['Content-Type'] = TYPES[extname(name).toLowerCase()] || 'application/octet-stream';
    res.writeHead(200, headers);
    res.end(body);
  } catch {
    res.writeHead(404, { 'Content-Type': 'text/plain' });
    res.end('not found');
  }
}).listen(port, () => console.log(`serving ${root} on http://localhost:${port}`));

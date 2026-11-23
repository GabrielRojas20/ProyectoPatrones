import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../frontend/pos');
const port = 5173;
const types = { '.html':'text/html; charset=utf-8', '.js':'text/javascript; charset=utf-8', '.css':'text/css; charset=utf-8', '.json':'application/json; charset=utf-8' };

function lanIp() {
  const addresses = [];
  for (const entries of Object.values(os.networkInterfaces()))
    for (const item of entries ?? [])
      if (item.family === 'IPv4' && !item.internal && !item.address.startsWith('169.254.')) addresses.push(item.address);
  return addresses.find(ip => ip.startsWith('192.168.'))
    ?? addresses.find(ip => ip.startsWith('10.'))
    ?? addresses.find(ip => /^172\.(1[6-9]|2\d|3[01])\./.test(ip))
    ?? addresses[0] ?? '127.0.0.1';
}

const server = http.createServer((req, res) => {
  if (req.url?.startsWith('/api/')) {
    const proxy = http.request({ hostname:'127.0.0.1', port:5080, path:req.url, method:req.method, headers:req.headers }, upstream => {
      res.writeHead(upstream.statusCode ?? 502, upstream.headers); upstream.pipe(res);
    });
    proxy.on('error', () => { res.writeHead(502); res.end('Backend no disponible'); });
    req.pipe(proxy); return;
  }
  const pathname = decodeURIComponent((req.url ?? '/').split('?')[0]);
  const requested = pathname === '/' ? 'index.html' : pathname.replace(/^\/+/, '');
  const file = path.resolve(root, requested);
  if (!file.startsWith(root + path.sep)) { res.writeHead(403); res.end(); return; }
  fs.readFile(file, (error, data) => {
    if (error) { res.writeHead(404); res.end('No encontrado'); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream', 'Cache-Control':'no-cache' }); res.end(data);
  });
});
server.listen(port, '0.0.0.0', () => console.log(`LAN_URL=http://${lanIp()}:${port}`));

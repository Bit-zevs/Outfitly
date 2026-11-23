import { createServer } from 'node:http';
import { readFile, realpath } from 'node:fs/promises';
import { extname, isAbsolute, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = await realpath(fileURLToPath(new URL('../', import.meta.url)));
const contentTypes = { '.css': 'text/css', '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.svg': 'image/svg+xml', '.webp': 'image/webp' };

createServer(async (request, response) => {
  let pathname;
  try {
    pathname = decodeURIComponent((request.url ?? '/').split('?')[0]);
  } catch {
    response.writeHead(400).end('Invalid path');
    return;
  }
  const requestedPath = pathname === '/' ? 'index.html' : pathname.slice(1);
  const filePath = join(root, requestedPath);
  const relativePath = relative(root, filePath);

  if (relativePath === '..' || relativePath.startsWith(`..${sep}`) || isAbsolute(relativePath)) {
    response.writeHead(403).end();
    return;
  }

  const clientPath = relativePath.split(sep).join('/');
  if (clientPath !== 'index.html' &&
      !/^src\/[\w-]+\.(?:css|js)$/.test(clientPath) &&
      !/^src\/assets\/[\w-]+\.(?:png|jpe?g|svg|webp)$/.test(clientPath)) {
    response.writeHead(404).end('Not found');
    return;
  }
  try {
    if (await realpath(filePath) !== filePath) {
      response.writeHead(404).end('Not found');
      return;
    }
    const content = await readFile(filePath);
    response.writeHead(200, { 'Content-Type': contentTypes[extname(filePath)] ?? 'application/octet-stream' });
    response.end(content);
  } catch {
    response.writeHead(404).end('Not found');
  }
}).listen(5173, '127.0.0.1', () => console.log('Frontend: http://127.0.0.1:5173'));

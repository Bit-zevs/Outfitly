import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, isAbsolute, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const contentTypes = { '.css': 'text/css', '.html': 'text/html', '.js': 'text/javascript' };

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

  try {
    const content = await readFile(filePath);
    response.writeHead(200, { 'Content-Type': contentTypes[extname(filePath)] ?? 'application/octet-stream' });
    response.end(content);
  } catch {
    response.writeHead(404).end('Not found');
  }
}).listen(5173, () => console.log('Frontend: http://localhost:5173'));

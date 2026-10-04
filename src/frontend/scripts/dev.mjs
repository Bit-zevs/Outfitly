import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';

const root = new URL('../', import.meta.url).pathname.slice(1);
const contentTypes = { '.css': 'text/css', '.html': 'text/html', '.js': 'text/javascript' };

createServer(async (request, response) => {
  const requestedPath = request.url === '/' ? 'index.html' : request.url.slice(1);
  const filePath = normalize(join(root, requestedPath));

  if (!filePath.startsWith(normalize(root))) {
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

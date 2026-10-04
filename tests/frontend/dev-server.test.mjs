import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, mkdir, copyFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { request } from 'node:http';

// Use the real server in an isolated directory. Raw HTTP paths deliberately
// bypass the URL normalization performed by fetch and browsers.
test('development server HTTP contract', async (t) => {
  const directory = await mkdtemp(join(tmpdir(), 'outfitly-http-'));
  const root = join(directory, 'frontend');
  await mkdir(join(root, 'scripts'), { recursive: true });
  await mkdir(join(directory, 'frontend-private'));
  await writeFile(join(root, 'index.html'), '<h1>Wardrobe</h1>');
  await writeFile(join(directory, 'frontend-private', 'secret.txt'), 'PRIVATE');
  await copyFile(new URL('../../src/frontend/scripts/dev.mjs', import.meta.url), join(root, 'scripts', 'dev.mjs'));
  const server = spawn(process.execPath, [join(root, 'scripts', 'dev.mjs')], { stdio: ['ignore', 'pipe', 'pipe'] });
  t.after(async () => {
    if (server.exitCode === null && server.signalCode === null) {
      const exited = once(server, 'exit');
      server.kill();
      await exited;
    }
    await rm(directory, { recursive: true, force: true });
  });
  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('Server did not start on port 5173')), 5000);
    server.stdout.once('data', () => { clearTimeout(timeout); resolve(); });
    server.once('error', (error) => { clearTimeout(timeout); reject(error); });
    server.once('exit', (code) => { clearTimeout(timeout); reject(new Error(`Server exited: ${code}`)); });
  });

  await t.test('home page remains accessible', async () => {
    const result = await get('/');
    assert.equal(result.status, 200);
    assert.equal(result.body, '<h1>Wardrobe</h1>');
  });
  await t.test('query parameters do not change the requested resource', async () => {
    const result = await get('/index.html?v=1');
    assert.equal(result.status, 200);
    assert.equal(result.body, '<h1>Wardrobe</h1>');
  });
  await t.test('a sibling directory sharing the root prefix is inaccessible', async () => {
    const result = await get('/../frontend-private/secret.txt');
    assert.equal(result.status, 403);
    assert.equal(result.body.includes('PRIVATE'), false);
  });
  await t.test('encoded traversal cannot disclose files outside the root', async () => {
    const result = await get('/%2e%2e/frontend-private/secret.txt');
    assert.ok([403, 404].includes(result.status), `Unexpected status: ${result.status}`);
    assert.equal(result.body.includes('PRIVATE'), false);
  });
});

function get(path) {
  return new Promise((resolve, reject) => {
    const req = request({ hostname: '127.0.0.1', port: 5173, path }, (response) => {
      let body = '';
      response.setEncoding('utf8');
      response.on('data', (chunk) => { body += chunk; });
      response.on('end', () => resolve({ status: response.statusCode, body }));
    });
    req.setTimeout(3000, () => req.destroy(new Error('HTTP request timed out')));
    req.on('error', reject);
    req.end();
  });
}

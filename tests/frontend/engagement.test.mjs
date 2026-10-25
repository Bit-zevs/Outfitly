import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { bindEngagement } from '../../src/frontend/src/engagement.js';
const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
const flush = async () => { for (let i = 0; i < 8; i++) await new Promise(r => setImmediate(r)); };
test('comments are escaped, failed drafts survive and private tokens accompany every request', async t => {
  const dom = new JSDOM('<section id="engagement"></section>');
  const previous = globalThis.document; globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  const calls = [];
  const ctx = { isCurrent: () => true, api: { async request(path, options = {}) {
    calls.push([path, options]);
    if (path.includes('/engagement')) return { views: 1, likes: 0, comments: 1, canInteract: true, isLiked: false };
    if (options.method === 'POST' && path.includes('/comments')) throw new Error('offline');
    if (path.includes('/comments')) return { items: [{ id: 'c', author: { username: 'anna' }, text: '<img src=x onerror=alert(1)>', createdAt: '2026-01-01T00:00:00Z' }], total: 1, pageSize: 20 };
  } } };
  bindEngagement(ctx, 'items', { id: 'item' }, 'private-token'); await flush();
  assert.equal(document.querySelector('.comment-text').textContent, '<img src=x onerror=alert(1)>');
  assert.equal(document.querySelector('.comment-text img'), null);
  assert.equal(calls.filter(([p, o]) => p.includes('/views') && o.method === 'POST').length, 1);
  assert.ok(calls.every(([p]) => p.includes('shareToken=private-token')));
  const form = document.querySelector('.comment-compose form');
  form.querySelector('textarea').value = 'Keep my draft';
  form.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })); await flush();
  assert.equal(form.querySelector('textarea').value, 'Keep my draft');
  assert.equal(form.querySelector('[type=submit]').disabled, false);
  assert.equal(document.querySelector('.error').hidden, false);
});
test('guests send only reads and responses after navigation are ignored', async t => {
  const dom = new JSDOM('<section id="engagement"></section>');
  const previous = globalThis.document; globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  let current = true; const methods = [];
  const ctx = { isCurrent: () => current, api: { async request(path, options = {}) {
    methods.push(options.method || 'GET');
    return path.includes('/engagement') ? { views: 0, likes: 0, comments: 0, canInteract: false } : { items: [], total: 0, pageSize: 20 };
  } } };
  bindEngagement(ctx, 'outfits', { id: 'look' }); await flush();
  assert.deepEqual(methods, ['GET', 'GET']); assert.equal(document.querySelector('textarea'), null);
  let finish; ctx.api.request = () => new Promise(resolve => { finish = resolve; });
  bindEngagement(ctx, 'items', { id: 'late' }); current = false;
  document.querySelector('#engagement').innerHTML = 'New page'; finish({ canInteract: true }); await flush();
  assert.equal(document.querySelector('#engagement').textContent, 'New page');
});

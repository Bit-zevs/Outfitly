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

const deferred = () => {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
};

for (const mode of ['comment', 'reply', 'edit']) {
  test(`${mode} text is locked during a pending save and remains available after failure`, async t => {
    const dom = new JSDOM('<section id="engagement"></section>');
    const previous = globalThis.document; globalThis.document = dom.window.document;
    t.after(() => { globalThis.document = previous; dom.window.close(); });
    const pending = deferred(); const sent = [];
    const ctx = { isCurrent: () => true, api: { async request(path, options = {}) {
      if (options.method && path.includes('/comments')) { sent.push(options.body.text); return pending.promise; }
      if (path.endsWith('/engagement')) return { views: 1, likes: 0, comments: 1, canInteract: true, isLiked: false };
      if (path.includes('/comments')) return { items: [{ id: 'root', author: { username: 'anna' }, text: 'Original', createdAt: '2026-01-01T00:00:00Z', canEdit: true }], total: 1, pageSize: 20 };
    } } };
    bindEngagement(ctx, 'items', { id: 'item' }); await flush();
    if (mode !== 'comment') {
      const label = mode === 'reply' ? 'Ответить' : 'Изменить';
      [...document.querySelectorAll('.comment-actions button')].find(button => button.textContent === label).click();
    }
    const form = document.querySelector(mode === 'comment' ? '.comment-compose form' : '.comment-editor form');
    const input = form.querySelector('textarea'); input.value = 'Submitted text';
    form.dispatchEvent(new dom.window.Event('submit', { cancelable: true })); await flush();
    assert.deepEqual(sent, ['Submitted text']);
    assert.equal(input.disabled, true);
    assert.equal(form.querySelector('[type=submit]').disabled, true);
    form.dispatchEvent(new dom.window.Event('submit', { cancelable: true }));
    assert.equal(sent.length, 1);
    pending.resolve(Promise.reject(new Error('offline'))); await flush();
    assert.equal(input.disabled, false); assert.equal(input.value, 'Submitted text');
    assert.equal(form.querySelector('[type=submit]').disabled, false);
  });
}

test('late view statistics cannot undo a successful like or change the next command to PUT', async t => {
  const dom = new JSDOM('<section id="engagement"></section>');
  const previous = globalThis.document; globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  const lateStats = deferred(); const pendingLike = deferred(); const mutations = [];
  let reads = 0;
  const snapshot = liked => ({ views: 1, likes: liked ? 1 : 0, comments: 0, canInteract: true, isLiked: liked });
  const ctx = { isCurrent: () => true, api: { async request(path, options = {}) {
    if (path.endsWith('/engagement')) { reads++; return reads === 2 ? lateStats.promise : snapshot(reads >= 3); }
    if (path.includes('/comments')) return { items: [], total: 0, pageSize: 20 };
    if (path.endsWith('/like')) { mutations.push(options.method); if (options.method === 'PUT') await pendingLike.promise; }
  } } };
  bindEngagement(ctx, 'items', { id: 'item' }); await flush(); assert.equal(reads, 2);
  document.querySelector('#post-like').click(); await flush();
  pendingLike.resolve(); await flush();
  assert.equal(document.querySelector('#post-like').getAttribute('aria-pressed'), 'true');
  lateStats.resolve(snapshot(false)); await flush();
  assert.equal(document.querySelector('#post-like').getAttribute('aria-pressed'), 'true');
  document.querySelector('#post-like').click(); await flush();
  assert.deepEqual(mutations, ['PUT', 'DELETE']);
});

test('statistics returned during a pending like cannot enable a duplicate like command', async t => {
  const dom = new JSDOM('<section id="engagement"></section>');
  const previous = globalThis.document; globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  const lateStats = deferred(), pendingLike = deferred(); let reads = 0; const mutations = [];
  const snapshot = { views: 1, likes: 0, comments: 0, canInteract: true, isLiked: false };
  const ctx = { isCurrent: () => true, api: { async request(path, options = {}) {
    if (path.endsWith('/engagement')) return ++reads === 2 ? lateStats.promise : snapshot;
    if (path.includes('/comments')) return { items: [], total: 0, pageSize: 20 };
    if (path.endsWith('/like')) { mutations.push(options.method); await pendingLike.promise; }
  } } };
  bindEngagement(ctx, 'items', { id: 'item' }); await flush();
  document.querySelector('#post-like').click(); await flush();
  lateStats.resolve(snapshot); await flush();
  assert.equal(document.querySelector('#post-like').disabled, true);
  document.querySelector('#post-like').click(); await flush(); assert.deepEqual(mutations, ['PUT']);
  pendingLike.resolve(); await flush(); assert.equal(document.querySelector('#post-like').disabled, false);
});

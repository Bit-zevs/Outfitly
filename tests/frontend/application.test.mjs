import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createApi, ApiError } from '../../src/frontend/src/api.js';
import { createAuth } from '../../src/frontend/src/auth.js';
import { catalogQuery, changedFilter, parseRoute, privateRoute, safeReturn, shareAddress } from '../../src/frontend/src/router.js';
import { saveItem, saveOutfit, uploadedPhoto } from '../../src/frontend/src/save.js';
import { detail } from '../../src/frontend/src/pages.js';
import { validatePhoto } from '../../src/frontend/src/editors.js';
import { itemCard, photo } from '../../src/frontend/src/components.js';

const json = (value, status = 200) => new Response(status === 204 ? null : JSON.stringify(value), { status });

test('changing requests use same-origin cookies and a CSRF token, reads do not', async () => {
  const calls = [];
  const api = createApi(async (url, options) => { calls.push({ url, ...options }); return json(url.endsWith('/csrf') ? { token: 'csrf-one' } : { id: 'coat' }); });
  await api.request('/items?category=Outerwear');
  await api.request('/items', { method: 'POST', body: { name: 'Пальто', category: 'Outerwear' } });
  await api.request('/items/coat', { method: 'DELETE' });
  assert.equal(calls.filter(c => c.url.endsWith('/csrf')).length, 1);
  assert.equal(calls[0].headers['X-CSRF-TOKEN'], undefined);
  assert.equal(calls[2].headers['X-CSRF-TOKEN'], 'csrf-one');
  assert.deepEqual(JSON.parse(calls[2].body), { name: 'Пальто', category: 'Outerwear' });
  assert.ok(calls.every(c => c.credentials === 'same-origin' && c.cache === 'no-store'));
  api.resetCsrf();
  await api.request('/items/coat', { method: 'DELETE' });
  assert.equal(calls.filter(c => c.url.endsWith('/csrf')).length, 2);
});

test('multipart upload keeps the browser-generated boundary and sends CSRF', async () => {
  const form = new FormData(); form.append('file', new Blob(['png'], { type: 'image/png' }), 'coat.png');
  let upload;
  const api = createApi(async (url, options) => { if (url.endsWith('/csrf')) return json({ token: 'photo-token' }); upload = options; return json({ id: 'coat' }); });
  await api.request('/items/coat/photo', { method: 'PUT', body: form });
  assert.equal(upload.body, form);
  assert.equal(upload.headers['Content-Type'], undefined);
  assert.equal(upload.headers['X-CSRF-TOKEN'], 'photo-token');
});

test('an expired private session triggers sign-in, failed login remains on the form', async () => {
  let expired = 0;
  const api = createApi(async url => url.endsWith('/csrf') ? json({ token: 'x' }) : json({ title: 'Unauthorized' }, 401), () => expired++);
  await assert.rejects(api.request('/outfits'), e => e instanceof ApiError && e.status === 401);
  await assert.rejects(api.request('/auth/login', { method: 'POST', body: {}, anonymous: true }));
  assert.equal(expired, 1);
});

test('auth restores the server user and rotates CSRF after login and logout', async () => {
  const calls = [];
  const user = { id: 'person', displayName: 'Мария', email: 'm@example.test' };
  const api = createApi(async (url, options) => { calls.push({ url, ...options }); return json(url.endsWith('/csrf') ? { token: `token-${calls.length}` } : user, url.endsWith('/logout') ? 204 : 200); });
  const auth = createAuth(api);
  await auth.restore(); assert.deepEqual(auth.user, user);
  await auth.login('m@example.test', 'long password here');
  await api.request('/items/person', { method: 'DELETE' });
  await auth.logout(); assert.equal(auth.user, null);
  await api.token();
  assert.equal(calls.filter(c => c.url.endsWith('/csrf')).length, 3);
});

test('catalog links preserve filters, reset pagination and use the real API search contract', () => {
  const route = parseRoute('#/discover?tab=items&search=linen&category=Top&page=4&sort=nameDesc');
  const next = parseRoute(`#${changedFilter(route, 'category', 'Dress')}`);
  assert.equal(next.query.get('tab'), 'items');
  assert.equal(next.query.get('search'), 'linen');
  assert.equal(next.query.has('page'), false);
  const api = catalogQuery(next.query);
  assert.equal(api.get('page'), '1'); assert.equal(api.get('pageSize'), '20');
  assert.equal(api.has('tab'), false); assert.equal(api.get('sort'), 'nameDesc');
  assert.equal(catalogQuery(new URLSearchParams('page=-3')).get('page'), '1');
  assert.equal(privateRoute('/items/new'), true); assert.equal(privateRoute('/shared/items/x'), false);
  assert.equal(safeReturn('https://outside.example'), '/dashboard');
  assert.equal(shareAddress('https://localhost:7043', 'items', 'a/b'), 'https://localhost:7043/#/shared/items/a%2Fb');
});

test('photo failure preserves the created item ID and retry never creates a duplicate', async () => {
  const calls = [];
  let failed = false;
  const api = { async request(path, options = {}) {
    calls.push([path, options.method]);
    if (path === '/items') return { id: 'new-item' };
    if (path.endsWith('/photo') && !failed) { failed = true; throw new ApiError(415); }
    return { id: 'new-item', photoUrl: '/api/items/new-item/photo' };
  } };
  const draft = { values: { name: 'Льняная рубашка', category: 'Top' }, photoMode: 'file', file: new Blob(['png'], { type: 'image/png' }) };
  await assert.rejects(saveItem(api, draft));
  assert.equal(draft.id, 'new-item'); assert.equal(draft.metadataSaved, true);
  await saveItem(api, draft);
  assert.equal(calls.filter(([path]) => path === '/items').length, 1);
  assert.equal(draft.file, null);
});

test('removing external and uploaded photos uses the correct API operations', async () => {
  const calls = [];
  const api = { async request(path, options) { calls.push({ path, ...options }); } };
  await saveItem(api, { id: 'external', baseline: { photoUrl: 'https://images.test/coat.png' }, photoMode: 'file', removePhoto: true, values: { name: 'Coat', category: 'Outerwear' } });
  assert.equal(calls[0].body.photoUrl, null);
  assert.equal(calls.length, 1);
  await saveItem(api, { id: 'uploaded', baseline: { photoUrl: '/api/items/uploaded/photo' }, photoMode: 'file', removePhoto: true, values: { name: 'Shoes', category: 'Footwear' } });
  assert.equal(calls[2].path, '/items/uploaded/photo'); assert.equal(calls[2].method, 'DELETE');
  await saveItem(api, { id: 'replacement', baseline: { photoUrl: '/api/items/replacement/photo' }, photoMode: 'url', removePhoto: true, values: { name: 'Dress', category: 'Dress', photoUrl: 'https://images.test/new.png' } });
  assert.equal(calls[3].body.photoUrl, 'https://images.test/new.png');
  assert.equal(calls.length, 4);
});

test('partial outfit save keeps desired membership, rereads on retry and adds before removing', async () => {
  const saved = new Set(['old']);
  const writes = [];
  let fail = true;
  const api = { async request(path, options = {}) {
    if (!options.method) return { items: [...saved].map(id => ({ id })) };
    if (path === '/outfits') return { id: 'draft' };
    writes.push([path, options.method]);
    const id = path.split('/').at(-1);
    if (path.includes('/items/')) {
      if (id === 'second' && fail) { fail = false; throw new ApiError(409); }
      options.method === 'POST' ? saved.add(id) : saved.delete(id);
    }
  } };
  const draft = { name: 'Встреча', selected: [{ id: 'first' }, { id: 'second' }] };
  await assert.rejects(saveOutfit(api, draft), e => e.status === 409);
  assert.deepEqual(draft.selected.map(i => i.id), ['first', 'second']);
  assert.equal(draft.id, 'draft'); assert.deepEqual([...saved], ['old', 'first']);
  await saveOutfit(api, draft);
  assert.deepEqual([...saved], ['first', 'second']);
  assert.equal(writes.filter(([p, m]) => p.endsWith('/first') && m === 'POST').length, 1);
  assert.deepEqual(writes.at(-1), ['/outfits/draft/items/old', 'DELETE']);
});

test('empty outfits save as drafts without adding memberships', async () => {
  const calls = [];
  const api = { async request(path, options = {}) { calls.push([path, options.method]); return path === '/outfits' ? { id: 'empty' } : { items: [] }; } };
  assert.equal(await saveOutfit(api, { name: 'Завтра', selected: [] }), 'empty');
  assert.deepEqual(calls, [['/outfits', 'POST'], ['/outfits/empty', undefined]]);
});

test('image validation and rendering reject unsafe inputs while escaping user content', () => {
  assert.equal(validatePhoto({ type: 'image/png', size: 5 * 1024 * 1024 }), '');
  assert.ok(validatePhoto({ type: 'image/svg+xml', size: 50 }));
  assert.ok(validatePhoto({ type: 'image/jpeg', size: 0 }));
  assert.ok(validatePhoto({ type: 'image/jpeg', size: 5 * 1024 * 1024 + 1 }));
  assert.ok(!photo({ name: 'unsafe', photoUrl: 'javascript:alert(1)' }).includes('<img'));
  const card = itemCard({ id: 'one', name: '<img onerror=alert(1)>', category: 'Top', brand: 'A&B' });
  assert.ok(card.includes('&lt;img onerror=alert(1)&gt;')); assert.ok(card.includes('A&amp;B'));
  assert.equal(uploadedPhoto({ id: 'coat', photoUrl: '/api/items/coat/photo' }), true);
  assert.equal(uploadedPhoto({ id: 'coat', photoUrl: 'https://outside.test/api/items/coat/photo' }), false);
  assert.equal(uploadedPhoto({ id: 'coat', photoUrl: '/api/items/other/photo' }), false);
});

test('opening public details directly searches later catalog pages without a private request', async () => {
  const requests = [];
  const ctx = { auth: { user: null }, async get(path) {
    requests.push(path);
    return path.includes('page=1&') ? { items: [{ id: 'first', name: 'First', category: 'Top' }], total: 2, pageSize: 1 } : { items: [{ id: 'target', name: 'Public coat', category: 'Outerwear' }], total: 2, pageSize: 1 };
  } };
  const page = await detail(ctx, 'items', 'target', 'public');
  assert.equal(page.title, 'Public coat');
  assert.deepEqual(requests, ['/wardrobe?page=1&pageSize=100', '/wardrobe?page=2&pageSize=100']);
  assert.ok(!page.html.includes('id="delete-resource"'));
});

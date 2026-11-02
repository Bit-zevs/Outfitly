import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { supportPage } from '../../src/frontend/src/support.js';
import { parseRoute, privateRoute } from '../../src/frontend/src/router.js';
const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
function mount(t, page) {
  const dom = new JSDOM(page.html); const old = { document: globalThis.document, FormData: globalThis.FormData };
  globalThis.document = dom.window.document; globalThis.FormData = dom.window.FormData;
  t.after(() => { Object.assign(globalThis, old); dom.window.close(); }); page.bind?.(); return dom;
}
const ticket = { id: 'ticket', ownerId: 'owner', subject: '<bug>', category: 'bug', status: 'new', revision: 'version', messages: [{ id: 'm', text: '<script>bad</script>', isAdmin: false, createdAtUtc: '2026-11-02T10:00:00Z', attachments: [{ id: 'a', url: '/api/support/tickets/ticket/attachments/a' }] }] };
test('support routes are private and admin screen does not request data for ordinary users', async () => {
  assert.equal(privateRoute('/support/new'), true); assert.equal(privateRoute('/admin/support'), true);
  const page = await supportPage({ auth: { user: {} }, get: () => { throw new Error('should not fetch'); } }, true);
  assert.match(page.html, /Нет доступа/);
});
test('ticket escapes messages, displays private attachments and closed state', async () => {
  const ctx = { auth: { user: {} }, get: async () => ({ ...ticket, status: 'closed' }) };
  const page = await supportPage(ctx, false, 'ticket');
  assert.match(page.html, /&lt;script&gt;/); assert.doesNotMatch(page.html, /<script>/);
  assert.match(page.html, /Обращение закрыто/); assert.doesNotMatch(page.html, /id="support-form"/);
  assert.match(page.html, /\/api\/support\/tickets\/ticket\/attachments\/a/);
});
test('creation uses multipart, prevents duplicate submits and preserves input on errors', async t => {
  let calls = 0; let finish; let target;
  const ctx = { auth: { user: {} }, isCurrent: () => true, navigate: value => { target = value; },
    api: { request: async (path, options) => { calls++; assert.equal(path, '/support/tickets'); assert.equal(options.body.get('subject'), 'Bug'); await new Promise(resolve => { finish = resolve; }); throw new Error('offline'); } } };
  const page = await supportPage(ctx, false, 'new'); mount(t, page);
  const form = document.querySelector('form'); form.elements.subject.value = 'Bug'; form.elements.text.value = 'Description';
  const promise = form.onsubmit({ preventDefault() {} }); await form.onsubmit({ preventDefault() {} }); assert.equal(calls, 1);
  finish(); await promise; assert.equal(form.elements.text.value, 'Description'); assert.equal(target, undefined); assert.equal(document.querySelector('#support-form-error').hidden, false);
});
test('conflict reloads ticket while retaining typed reply and newer revision', async t => {
  let requests = 0;
  const ctx = { auth: { user: {} }, isCurrent: () => true, get: async () => ({ ...ticket, revision: ++requests === 1 ? 'old' : 'new' }),
    api: { request: async () => { const error = new Error('conflict'); error.status = 409; throw error; } } };
  const page = await supportPage(ctx, false, 'ticket'); mount(t, page); const form = document.querySelector('#support-form'); form.elements.text.value = 'Keep me';
  await form.onsubmit({ preventDefault() {} }); assert.equal(requests, 2); assert.equal(form.elements.text.value, 'Keep me');
});
test('admin list shows filters and pagination and personal admin sees admin link', async () => {
  const ctx = { auth: { user: { isSupportAdmin: true } }, route: parseRoute('#/support?category=bug&page=2'), get: async () => ({ items: [], total: 21, page: 2, pageSize: 20 }) };
  assert.match((await supportPage(ctx)).html, /Обращения пользователей/);
  assert.match((await supportPage(ctx, true)).html, /support-filters/);
});

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { cropGeometry, avatarSettings, bindAvatarSettings } from '../../src/frontend/src/avatar-settings.js';
import { profilePage } from '../../src/frontend/src/profiles.js';
import { ApiError } from '../../src/frontend/src/api.js';
const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
const flush = async () => { for (let i = 0; i < 8; i++) await new Promise(r => setImmediate(r)); };
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; };
function mount(t, html) {
  const dom = new JSDOM(html); const previous = { document: globalThis.document, FormData: globalThis.FormData, Image: globalThis.Image };
  globalThis.document = dom.window.document; globalThis.FormData = dom.window.FormData;
  t.after(() => { Object.assign(globalThis, previous); dom.window.close(); }); return dom;
}
function images(t, dom, decodes = []) {
  const made = [], revoked = [], drawings = [];
  const create = URL.createObjectURL, revoke = URL.revokeObjectURL;
  URL.createObjectURL = file => { const url = 'blob:test-' + made.length; made.push(file); return url; };
  URL.revokeObjectURL = url => revoked.push(url);
  t.after(() => { URL.createObjectURL = create; URL.revokeObjectURL = revoke; });
  globalThis.Image = class { naturalWidth = 400; naturalHeight = 200; decode() { return decodes.shift() || Promise.resolve(); } };
  dom.window.HTMLCanvasElement.prototype.getContext = () => ({ clearRect() {}, drawImage(...args) { drawings.push(args.slice(1)); } });
  return { made, revoked, drawings };
}
function select(dom, name = 'avatar.png') {
  const input = document.querySelector('[name=file]');
  Object.defineProperty(input, 'files', { configurable: true, value: [new dom.window.File(['image'], name, { type: 'image/png' })] });
  input.dispatchEvent(new dom.window.Event('change'));
}

test('crop controls select a square in oriented image coordinates', () => {
  assert.deepEqual(cropGeometry(400, 200), { x: 100, y: 0, size: 200 });
  assert.deepEqual(cropGeometry(400, 200, 2, 100, 0), { x: 300, y: 0, size: 100 });
  assert.deepEqual(cropGeometry(200, 400, 1, 0, 100), { x: 0, y: 200, size: 200 });
});

test('avatar sends the original file and selected crop, blocks duplicates and preserves a failed draft', async t => {
  const dom = mount(t, avatarSettings({ displayName: 'Anna' })); const fixtures = images(t, dom);
  const pending = deferred(); const sent = []; const dirty = [];
  let fail = true;
  const cleanup = bindAvatarSettings({ isCurrent: () => true, auth: { async uploadAvatar(data) {
    sent.push(data); if (fail) return pending.promise; return { displayName: 'Anna', avatarUrl: '/api/users/id/avatar' };
  } } }, { canSave: () => true, setSaving() {}, setDirty: value => dirty.push(value), updateIdentity() {} }); t.after(cleanup);
  select(dom); await flush();
  const form = document.querySelector('#avatar-form');
  assert.equal(document.querySelector('#avatar-crop').hidden, false);
  form.elements.zoom.value = '2'; form.elements.horizontal.value = '100'; form.elements.vertical.value = '0';
  form.elements.zoom.dispatchEvent(new dom.window.Event('input'));
  form.dispatchEvent(new dom.window.Event('submit', { cancelable: true })); await flush();
  assert.equal(sent.length, 1); assert.equal(sent[0].get('file').name, 'avatar.png');
  assert.equal(sent[0].get('cropX'), '300'); assert.equal(sent[0].get('cropY'), '0'); assert.equal(sent[0].get('cropSize'), '100');
  assert.equal(form.elements.zoom.disabled, true);
  form.dispatchEvent(new dom.window.Event('submit', { cancelable: true })); assert.equal(sent.length, 1);
  pending.reject(new ApiError(409)); await flush();
  assert.equal(form.elements.zoom.disabled, false); assert.equal(form.elements.zoom.value, '2'); assert.equal(dirty.at(-1), true);
  fail = false; form.dispatchEvent(new dom.window.Event('submit', { cancelable: true })); await flush();
  assert.equal(sent.length, 2); assert.equal(document.querySelector('#avatar-crop').hidden, true); assert.equal(dirty.at(-1), false);
  assert.equal(fixtures.revoked.length, 1); assert.ok(document.querySelector('#avatar-current img'));
});

test('cancel and cleanup release previews; a late image decode cannot reopen a cancelled crop', async t => {
  const dom = mount(t, avatarSettings({ displayName: 'Anna' })); const late = deferred(); const fixtures = images(t, dom, [late.promise]);
  let uploads = 0;
  const cleanup = bindAvatarSettings({ isCurrent: () => true, auth: { uploadAvatar() { uploads++; } } }, { canSave: () => true, setSaving() {}, setDirty() {}, updateIdentity() {} });
  select(dom); await flush(); document.querySelector('#avatar-cancel').click(); late.resolve(); await flush();
  assert.equal(document.querySelector('#avatar-crop').hidden, true); assert.equal(uploads, 0); assert.equal(fixtures.revoked.length, 1);
  select(dom, 'second.png'); await flush(); cleanup(); assert.equal(fixtures.revoked.length, 2);
});

test('profile renders escaped bio, public connections and current follow state without duplicate commands', async t => {
  let following = false, current = true; const pending = deferred(); const commands = [];
  const ctx = { route: { path: '/@anna', query: new URLSearchParams('tab=followers') }, auth: { user: { id: 'bob' } }, isCurrent: () => current,
    async get(path) {
      if (path.includes('/followers?')) return { items: [{ username: 'bob', displayName: 'Bob', avatarUrl: '/api/users/bob/avatar' }], total: 1, page: 1, pageSize: 20 };
      return { id: 'anna', username: 'anna', displayName: 'Anna', bio: '<img src=x onerror=alert(1)>', canFollow: true, isFollowing: following, followersCount: following ? 2 : 1 };
    }, api: { async request(path, options) { commands.push(options.method); await pending.promise; following = true; } } };
  const page = await profilePage(ctx, 'anna'); const dom = mount(t, page.html); page.bind();
  assert.equal(document.querySelector('.profile-bio img'), null); assert.match(document.querySelector('.profile-bio').textContent, /<img/);
  assert.ok(document.querySelector('a[href="#/@bob"]'));
  document.querySelector('#follow-user').click(); document.querySelector('#follow-user').click(); assert.deepEqual(commands, ['PUT']);
  pending.resolve(); await flush(); assert.equal(document.querySelector('#follow-user').textContent, 'Отписаться');
  const late = deferred(); ctx.api.request = async (_, options) => { commands.push(options.method); await late.promise; };
  document.querySelector('#follow-user').click(); current = false; document.querySelector('#public-profile-header').textContent = 'New page'; late.resolve(); await flush();
  assert.deepEqual(commands, ['PUT', 'DELETE']); assert.equal(document.querySelector('#public-profile-header').textContent, 'New page');
});

test('bio editing keeps a failed draft and the email form while saving normalizes a cleared description', async t => {
  const { accountSettings } = await import('../../src/frontend/src/account-settings.js');
  let user = { id: 'anna', username: 'anna', displayName: 'Anna', email: 'anna@example.com', bio: 'Original' };
  const pending = deferred(); const sent = []; let fail = true;
  const ctx = { isCurrent: () => true, setDirty() {}, auth: { get user() { return user; }, async changeProfile(name, username, bio) {
    sent.push(bio); if (fail) await pending.promise; user = { ...user, bio: null }; return user;
  } } };
  const page = accountSettings(ctx), dom = mount(t, page.html); page.bind(); t.after(() => page.cleanup());
  const form = document.querySelector('#profile-form'); const email = document.querySelector('#email-form').elements.email;
  email.value = 'pending@example.com'; form.elements.bio.value = 'Draft';
  form.elements.bio.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
  assert.equal(document.querySelector('#bio-count').textContent, '5/500');
  const saving = form.onsubmit({ preventDefault() {} }); await flush(); assert.equal(form.elements.bio.disabled, true);
  pending.reject(new ApiError(400)); await saving;
  assert.equal(form.elements.bio.value, 'Draft'); assert.equal(email.value, 'pending@example.com'); assert.deepEqual(sent, ['Draft']);
  fail = false; form.elements.bio.value = '   '; await form.onsubmit({ preventDefault() {} });
  assert.equal(form.elements.bio.value, ''); assert.equal(document.querySelector('#bio-count').textContent, '0/500');
  assert.equal(email.value, 'pending@example.com');
});

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { accountSettings, confirmEmailChangePage } from '../../src/frontend/src/account-settings.js';
import { createAuth } from '../../src/frontend/src/auth.js';
import { ApiError } from '../../src/frontend/src/api.js';
import { parseRoute } from '../../src/frontend/src/router.js';

const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
const preventDefault = () => {};
function mount(t, page) {
  const dom = new JSDOM(`<main>${page.html}</main>`, { url: 'https://outfitly.test' });
  const old = { document: globalThis.document, FormData: globalThis.FormData };
  globalThis.document = dom.window.document; globalThis.FormData = dom.window.FormData;
  t.after(() => { Object.assign(globalThis, old); dom.window.close(); });
  page.bind();
}
const user = () => ({ id: 'owner', displayName: 'Анна', username: 'anna', email: 'old@example.com', pendingEmail: null });

test('profile save updates identity without losing edits in the independent email and password forms', async t => {
  const auth = { user: user(), async changeProfile(displayName, username) { this.user = { ...this.user, displayName: displayName.trim(), username }; return this.user; } };
  let dirty;
  mount(t, accountSettings({ auth, isCurrent: () => true, setDirty: value => { dirty = value; }, toast() {} }));
  const profile = document.querySelector('#profile-form'), email = document.querySelector('#email-form'), password = document.querySelector('#password-form');
  email.elements.email.value = 'unsaved@example.com'; email.oninput();
  password.elements.newPassword.value = 'unsaved password'; password.oninput();
  profile.elements.displayName.value = ' Мария '; profile.elements.username.value = 'maria'; profile.oninput();
  await profile.onsubmit({ preventDefault });
  assert.match(document.querySelector('#account-identity').textContent, /Мария/);
  assert.equal(document.querySelector('#account-identity a').getAttribute('href'), '#/@maria');
  assert.equal(email.elements.email.value, 'unsaved@example.com');
  assert.equal(password.elements.newPassword.value, 'unsaved password');
  assert.equal(dirty, true);
});

test('email form reports SMTP failure, preserves pending email, clears password and supports cancellation', async t => {
  const auth = { user: user(), async requestEmailChange(email, password) {
    assert.equal(password, 'current password'); this.user.pendingEmail = email;
    return { pendingEmail: email, confirmationEmailSent: false };
  }, async cancelEmailChange() { this.user.pendingEmail = null; } };
  mount(t, accountSettings({ auth, isCurrent: () => true, toast() {} }));
  const form = document.querySelector('#email-form');
  form.elements.email.value = 'new@example.com'; form.elements.emailCurrentPassword.value = 'current password';
  await form.onsubmit({ preventDefault });
  assert.match(document.querySelector('#email-status').textContent, /письмо отправить не удалось/);
  assert.match(document.querySelector('#pending-email').textContent, /new@example.com/);
  assert.equal(form.elements.emailCurrentPassword.value, '');
  assert.equal(document.querySelector('#current-email').textContent, 'old@example.com');
  await document.querySelector('#cancel-email-change').onclick();
  assert.equal(document.querySelector('#pending-email').textContent, '');
  assert.equal(document.querySelector('#cancel-email-change').hidden, true);
});

test('password mismatch sends nothing; wrong current password stays in the section; success asks for login', async t => {
  let calls = 0, fail = true;
  const navigations = [];
  mount(t, accountSettings({ auth: { user: user(), async changePassword(current, next) {
    calls++; assert.equal(current, 'current password'); assert.equal(next, 'new password');
    if (fail) throw new ApiError(400, { code: 'invalid_current_password' });
  } }, isCurrent: () => true, navigate: path => navigations.push(path), toast() {} }));
  const form = document.querySelector('#password-form');
  form.elements.newPassword.value = 'new password'; form.elements.passwordConfirmation.value = 'different';
  await form.onsubmit({ preventDefault });
  assert.equal(calls, 0);
  const fill = () => { form.elements.currentPassword.value = 'current password'; form.elements.newPassword.value = 'new password'; form.elements.passwordConfirmation.value = 'new password'; };
  fill(); await form.onsubmit({ preventDefault });
  assert.match(document.querySelector('#password-error').textContent, /Текущий пароль неверен/);
  assert.deepEqual(navigations, []);
  assert.equal(form.elements.currentPassword.value, ''); assert.equal(form.elements.newPassword.value, '');
  fail = false; fill(); await form.onsubmit({ preventDefault });
  assert.deepEqual(navigations, ['/login']);
});

test('saving blocks duplicate submissions and late results do not edit another page', async t => {
  let finish, current = true, calls = 0;
  const auth = { user: user(), changeProfile() { calls++; return new Promise(resolve => { finish = resolve; }); } };
  mount(t, accountSettings({ auth, isCurrent: () => current, toast() {} }));
  const form = document.querySelector('#profile-form');
  const pending = form.onsubmit({ preventDefault });
  await form.onsubmit({ preventDefault });
  assert.equal(calls, 1);
  current = false; document.querySelector('main').innerHTML = '<h1>Another page</h1>';
  finish({ displayName: 'Changed', username: 'changed' }); await pending;
  assert.equal(document.querySelector('main').textContent, 'Another page');
});

test('opening email change link is read-only; confirmation removes token after explicit submit', async t => {
  const calls = [], navigations = [];
  mount(t, confirmEmailChangePage({ route: parseRoute('#/confirm-email-change?token=secret'), isCurrent: () => true,
    auth: { async confirmEmailChange(token) { calls.push(token); } }, navigate: (...args) => navigations.push(args) }));
  assert.deepEqual(calls, []);
  await document.querySelector('form').onsubmit({ preventDefault });
  assert.deepEqual(calls, ['secret']);
  assert.deepEqual(navigations, [['/confirm-email-change?confirmed=1', true]]);
});

test('confirmation errors explain an expired token and an occupied email', async t => {
  let code = 'invalid_email_change_token';
  mount(t, confirmEmailChangePage({ route: parseRoute('#/confirm-email-change?token=secret'), isCurrent: () => true,
    auth: { async confirmEmailChange() { throw new ApiError(400, { code }); } } }));
  const form = document.querySelector('form');
  await form.onsubmit({ preventDefault });
  assert.match(document.querySelector('#email-change-status').textContent, /Ссылка недействительна/);
  code = 'email_taken'; await form.onsubmit({ preventDefault });
  assert.match(document.querySelector('#email-change-status').textContent, /адрес уже занят/);
});

test('auth settings use dedicated methods and clear expired session state even after leaving confirmation', async () => {
  let confirmed = false, resets = 0;
  const calls = [];
  const auth = createAuth({ resetCsrf() { resets++; }, async request(path, options) {
    calls.push({ path, options });
    if (path === '/auth/me') { if (confirmed) throw new ApiError(401); return user(); }
    if (path === '/auth/me/profile') return { ...user(), ...options.body };
    if (path === '/auth/me/email-change' && options.method === 'POST') return { pendingEmail: options.body.email, confirmationEmailSent: true };
    if (path === '/auth/confirm-email-change') confirmed = true;
  } });
  await auth.restore();
  await auth.changeProfile('Мария', 'maria'); assert.equal(auth.user.username, 'maria');
  await auth.requestEmailChange('new@example.com', 'password'); assert.equal(auth.user.pendingEmail, 'new@example.com');
  await auth.cancelEmailChange(); assert.equal(auth.user.pendingEmail, null);
  await auth.confirmEmailChange('token'); assert.equal(auth.user, null); assert.equal(resets, 1);
  const confirm = calls.find(call => call.path === '/auth/confirm-email-change');
  assert.deepEqual(confirm.options, { method: 'POST', body: { token: 'token' }, anonymous: true });
  confirmed = false; await auth.restore();
  await auth.changePassword('old password', 'new password'); assert.equal(auth.user, null); assert.equal(resets, 2);
});

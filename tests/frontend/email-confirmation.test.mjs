import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { checkEmailPage, confirmEmailPage } from '../../src/frontend/src/email-confirmation.js';
import { authPage } from '../../src/frontend/src/pages.js';
import { createAuth } from '../../src/frontend/src/auth.js';
import { parseRoute } from '../../src/frontend/src/router.js';
import { ApiError } from '../../src/frontend/src/api.js';

const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
function mount(t, page) {
  const dom = new JSDOM(`<main>${page.html}</main>`);
  const oldDocument = globalThis.document, oldFormData = globalThis.FormData;
  globalThis.document = dom.window.document;
  globalThis.FormData = dom.window.FormData;
  t.after(() => { globalThis.document = oldDocument; globalThis.FormData = oldFormData; dom.window.close(); });
  page.bind();
}

test('opening confirmation link sends nothing; explicit submission confirms and removes token', async t => {
  const calls = [], navigations = [];
  const ctx = { route: parseRoute('#/confirm-email?token=secret-token'), isCurrent: () => true,
    auth: { async confirmEmail(token) { calls.push(token); } }, navigate: (...args) => navigations.push(args) };
  mount(t, confirmEmailPage(ctx));
  assert.deepEqual(calls, []);
  await document.querySelector('form').onsubmit({ preventDefault() {} });
  assert.deepEqual(calls, ['secret-token']);
  assert.deepEqual(navigations, [['/confirm-email?confirmed=1', true]]);
});

for (const status of [400, 409, 429, 503]) {
  test(`confirmation failure ${status} leaves a retry and a new-letter link`, async t => {
    mount(t, confirmEmailPage({ route: parseRoute('#/confirm-email?token=expired'), isCurrent: () => true,
      auth: { async confirmEmail() { throw new ApiError(status, status === 400 ? { code: 'invalid_confirmation_token' } : {}); } } }));
    await document.querySelector('form').onsubmit({ preventDefault() {} });
    assert.ok(document.querySelector('#confirmation-status').textContent.length > 20);
    assert.equal(document.querySelector('button').disabled, false);
    assert.ok(document.querySelector('a[href="#/check-email"]'));
  });
}

test('resend failure and rate limit are explained; registration SMTP failure preserves account screen', async t => {
  let status = 429;
  mount(t, checkEmailPage({ route: parseRoute('#/check-email?email=a%40example.com&sent=0'), isCurrent: () => true,
    auth: { async resendConfirmation(email) { assert.equal(email, 'a@example.com'); if (status) throw new ApiError(status); } } }));
  assert.match(document.querySelector('.lead').textContent, /Аккаунт создан/);
  const form = document.querySelector('form');
  await form.onsubmit({ preventDefault() {} });
  assert.match(document.querySelector('#email-status').textContent, /минуту/);
  status = 503; await form.onsubmit({ preventDefault() {} });
  assert.match(document.querySelector('#email-status').textContent, /Попробуйте позже/);
  status = 0; await form.onsubmit({ preventDefault() {} });
  assert.match(document.querySelector('#email-status').textContent, /письмо отправлено/);
});

test('registration navigates to delivery status; unconfirmed login offers resend', async t => {
  const routes = [];
  const ctx = { route: parseRoute('#/register'), isCurrent: () => true, navigate: route => routes.push(route),
    auth: { async register() { return { confirmationEmailSent: false }; }, async login() { throw new ApiError(403, { code: 'email_not_confirmed' }); } } };
  mount(t, authPage(ctx, true));
  const form = document.querySelector('form');
  form.elements.displayName.value = 'Alice'; form.elements.email.value = 'a@example.com';
  form.elements.password.value = form.elements.passwordConfirmation.value = 'long password';
  await form.onsubmit({ preventDefault() {} });
  assert.equal(routes[0], '/check-email?email=a%40example.com&sent=0');
  document.querySelector('main').innerHTML = authPage(ctx, false).html;
  authPage(ctx, false).bind();
  const login = document.querySelector('form');
  login.elements.email.value = 'a@example.com'; login.elements.password.value = 'long password';
  await login.onsubmit({ preventDefault() {} });
  assert.equal(routes[1], '/check-email?email=a%40example.com');
});

test('auth confirmation and resend use anonymous POST bodies without creating local session', async () => {
  const calls = [];
  const auth = createAuth({ async request(...args) { calls.push(args); } });
  await auth.confirmEmail('token'); await auth.resendConfirmation('a@example.com');
  assert.deepEqual(calls, [
    ['/auth/confirm-email', { method: 'POST', body: { token: 'token' }, anonymous: true }],
    ['/auth/resend-confirmation', { method: 'POST', body: { email: 'a@example.com' }, anonymous: true }],
  ]);
  assert.equal(auth.user, null);
});

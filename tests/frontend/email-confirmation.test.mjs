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

function fillPassword(form) {
  form.elements.password.value = form.elements.passwordConfirmation.value = 'new long password';
  form.elements.username.value = 'atelier';
}

test('opening confirmation link sends nothing; explicit submission confirms and removes token', async t => {
  const calls = [], navigations = [];
  const ctx = { route: parseRoute('#/confirm-email?token=secret-token'), isCurrent: () => true,
    auth: { async confirmEmail(...args) { calls.push(args); } }, navigate: (...args) => navigations.push(args) };
  mount(t, confirmEmailPage(ctx));
  assert.deepEqual(calls, []);
  const form = document.querySelector('form');
  fillPassword(form);
  await form.onsubmit({ preventDefault() {} });
  assert.deepEqual(calls, [['secret-token', 'new long password', 'new long password', 'atelier']]);
  assert.deepEqual(navigations, [['/confirm-email?confirmed=1', true]]);
});

for (const status of [400, 409, 429, 503]) {
  test(`confirmation failure ${status} leaves a retry and a new-letter link`, async t => {
    mount(t, confirmEmailPage({ route: parseRoute('#/confirm-email?token=expired'), isCurrent: () => true,
      auth: { async confirmEmail() { throw new ApiError(status, status === 400 ? { code: 'invalid_confirmation_token' } : {}); } } }));
    const form = document.querySelector('form');
    fillPassword(form);
    await form.onsubmit({ preventDefault() {} });
    assert.ok(document.querySelector('#confirmation-status').textContent.length > 20);
    assert.equal(document.querySelector('button').disabled, false);
    assert.ok(document.querySelector('a[href="#/check-email"]'));
  });
}

test('confirmation instructions are generic and resend transport failures are explained', async t => {
  let status = 429;
  mount(t, checkEmailPage({ route: parseRoute('#/check-email?email=a%40example.com&sent=0'), isCurrent: () => true,
    auth: { async resendConfirmation(email) { assert.equal(email, 'a@example.com'); if (status) throw new ApiError(status); } } }));
  assert.match(document.querySelector('.lead').textContent, /Если адрес ожидает подтверждения/);
  const form = document.querySelector('form');
  await form.onsubmit({ preventDefault() {} });
  assert.match(document.querySelector('#email-status').textContent, /минуту/);
  status = 503; await form.onsubmit({ preventDefault() {} });
  assert.match(document.querySelector('#email-status').textContent, /Попробуйте позже/);
  status = 0; await form.onsubmit({ preventDefault() {} });
  assert.match(document.querySelector('#email-status').textContent, /письмо отправлено/);
});

test('registration requests only profile and email, then shows generic instructions', async t => {
  const routes = [];
  const requests = [];
  const ctx = { route: parseRoute('#/register'), isCurrent: () => true, navigate: route => routes.push(route),
    auth: { async register(...args) { requests.push(args); } } };
  mount(t, authPage(ctx, true));
  const form = document.querySelector('form');
  form.elements.displayName.value = 'Alice'; form.elements.email.value = 'a@example.com';
  assert.equal(form.elements.username, undefined);
  assert.equal(form.elements.password, undefined);
  await form.onsubmit({ preventDefault() {} });
  assert.deepEqual(requests, [['Alice', 'a@example.com']]);
  assert.deepEqual(routes, ['/check-email?email=a%40example.com']);
});

test('auth confirmation and resend use anonymous POST bodies without creating local session', async () => {
  const calls = [];
  const auth = createAuth({ async request(...args) { calls.push(args); } });
  await auth.confirmEmail('token', 'new long password', 'new long password', 'atelier'); await auth.resendConfirmation('a@example.com');
  assert.deepEqual(calls, [
    ['/auth/confirm-email', { method: 'POST', body: { token: 'token', password: 'new long password', username: 'atelier' }, anonymous: true }],
    ['/auth/resend-confirmation', { method: 'POST', body: { email: 'a@example.com' }, anonymous: true }],
  ]);
  assert.equal(auth.user, null);
});

test('confirmed users and successful confirmation do not offer another email', () => {
  const ctx = { auth: { user: { email: 'confirmed@example.test' } }, route: parseRoute('#/check-email') };
  assert.doesNotMatch(checkEmailPage(ctx).html, /resend-form/);
  assert.doesNotMatch(confirmEmailPage({ ...ctx, route: parseRoute('#/confirm-email?confirmed=1') }).html, /Получить новое письмо/);
});

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { passwordResetPage } from '../../src/frontend/src/password-reset.js';
import { parseRoute } from '../../src/frontend/src/router.js';
import { createAuth } from '../../src/frontend/src/auth.js';

const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
test('reset requires explicit submission and matching passwords; success removes token', async t => {
  const calls = [], routes = [];
  const ctx = { route: parseRoute('#/reset-password?token=one-time-token'), isCurrent: () => true,
    auth: { async resetPassword(...args) { calls.push(args); }, clear() { calls.push('clear'); } }, navigate: (...args) => routes.push(args) };
  const page = passwordResetPage(ctx, true);
  const dom = new JSDOM(page.html);
  const previous = globalThis.document;
  globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  page.bind();
  assert.deepEqual(calls, []);
  const form = document.querySelector('form');
  form.elements.password.value = 'new password';
  form.elements.confirmation.value = 'different';
  await form.onsubmit({ preventDefault() {} });
  assert.deepEqual(calls, []);
  form.elements.confirmation.value = 'new password';
  await form.onsubmit({ preventDefault() {} });
  assert.deepEqual(calls, [['one-time-token', 'new password'], 'clear']);
  assert.deepEqual(routes, [['/login', true]]);
});

test('reset commands use anonymous POST through the shared CSRF client', async () => {
  const calls = [];
  const auth = createAuth({ async request(...args) { calls.push(args); } });
  await auth.forgotPassword('reader@example.com');
  await auth.resetPassword('token', 'new password');
  assert.deepEqual(calls, [
    ['/auth/forgot-password', { method: 'POST', body: { email: 'reader@example.com' }, anonymous: true }],
    ['/auth/reset-password', { method: 'POST', body: { token: 'token', password: 'new password' }, anonymous: true }]
  ]);
});

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { passwordResetPage } from '../../src/frontend/src/password-reset.js';
import { parseRoute } from '../../src/frontend/src/router.js';
import { createAuth } from '../../src/frontend/src/auth.js';
import { createApi } from '../../src/frontend/src/api.js';

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

test('leaving during reset clears auth and fetches fresh CSRF for the new recovery form', async t => {
  let completeReset, resetStarted;
  const pendingResponse = new Promise(resolve => { completeReset = resolve; });
  const started = new Promise(resolve => { resetStarted = resolve; });
  let sessionRevoked = false, csrfRequests = 0, recoveryRequests = 0;
  const api = createApi(async (url, options) => {
    if (url === '/api/auth/me') return Response.json({ id: 'user-id', displayName: 'Alice' });
    if (url === '/api/auth/csrf') {
      csrfRequests++;
      return Response.json({ token: sessionRevoked ? 'anonymous-csrf' : 'session-csrf' });
    }
    if (url === '/api/auth/reset-password') {
      assert.equal(options.headers['X-CSRF-TOKEN'], 'session-csrf');
      resetStarted();
      return pendingResponse;
    }
    assert.equal(url, '/api/auth/forgot-password');
    assert.equal(options.headers['X-CSRF-TOKEN'], 'anonymous-csrf');
    recoveryRequests++;
    return Response.json({ message: 'If eligible, an email has been sent.' }, { status: 202 });
  });
  const auth = createAuth(api);
  await auth.restore();
  assert.notEqual(auth.user, null);
  const routes = [];
  let current = true;
  const ctx = { route: parseRoute('#/reset-password?token=one-time-token'), isCurrent: () => current,
    auth, navigate: (...args) => routes.push(args) };
  const page = passwordResetPage(ctx, true);
  const dom = new JSDOM(page.html);
  const previous = globalThis.document;
  globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  page.bind();
  const resetForm = document.querySelector('form');
  resetForm.elements.password.value = resetForm.elements.confirmation.value = 'new password';
  const submission = resetForm.onsubmit({ preventDefault() {} });
  await started;

  current = false;
  const recovery = passwordResetPage({ ...ctx, route: parseRoute('#/forgot-password'), isCurrent: () => true }, false);
  document.body.innerHTML = recovery.html;
  recovery.bind();
  const recoveryForm = document.querySelector('form');
  sessionRevoked = true;
  completeReset(new Response(null, { status: 204 }));
  await submission;

  assert.equal(auth.user, null);
  assert.deepEqual(routes, []);
  assert.equal(document.querySelector('#reset-status').textContent, '');
  recoveryForm.elements.email.value = 'reader@example.com';
  await recoveryForm.onsubmit({ preventDefault() {} });
  assert.equal(recoveryRequests, 1);
  assert.equal(csrfRequests, 2);
  assert.match(document.querySelector('#reset-status').textContent, /письмо отправлено/);
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

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { account, authPage, detail, discover } from '../../src/frontend/src/pages.js';
import { peoplePage, profilePage } from '../../src/frontend/src/profiles.js';
import { createAuth } from '../../src/frontend/src/auth.js';
import { ApiError } from '../../src/frontend/src/api.js';
import { parseRoute, profilePath, publicPath, privateRoute, shareAddress } from '../../src/frontend/src/router.js';
import { itemCard, outfitCard } from '../../src/frontend/src/components.js';

const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
function mount(t, page) {
  const dom = new JSDOM(`<main>${page.html}</main>`, { url: 'https://outfitly.test' });
  const previous = { document: globalThis.document, FormData: globalThis.FormData };
  globalThis.document = dom.window.document;
  globalThis.FormData = dom.window.FormData;
  t.after(() => { Object.assign(globalThis, previous); dom.window.close(); });
  page.bind?.();
}
const preventDefault = () => {};

test('public links contain the owner username while personal and token links stay compatible', () => {
  assert.equal(profilePath('anna'), '/@anna');
  assert.equal(publicPath('anna', 'items', 'coat'), '/@anna/items/coat');
  assert.equal(privateRoute('/@anna/items/coat'), false);
  const item = { id: 'coat', ownerUsername: 'anna', name: 'Coat', category: 'Top' };
  assert.match(itemCard(item, '/public/items'), /href="#\/@anna\/items\/coat"/);
  assert.match(itemCard(item), /href="#\/items\/coat"/);
  assert.match(outfitCard({ ...item, items: [] }, '/public/outfits'), /href="#\/@anna\/outfits\/coat"/);
  assert.equal(shareAddress('https://outfitly.test', 'items', 'secret'), 'https://outfitly.test/#/shared/items/secret');
});

test('registration sends username and explains a taken username separately from email', async t => {
  let input;
  mount(t, authPage({ route: parseRoute('#/register'), isCurrent: () => true,
    auth: { async register(...args) { input = args; throw new ApiError(409, { code: 'username_taken' }); } } }, true));
  const form = document.querySelector('form');
  Object.entries({ displayName: 'Анна', username: '@anna', email: 'anna@example.com', password: 'long password', passwordConfirmation: 'long password' })
    .forEach(([name, value]) => { form.elements[name].value = value; });
  await form.onsubmit({ preventDefault });
  assert.equal(input[4], '@anna');
  assert.match(document.querySelector('#form-error').textContent, /@username уже занят/);
  assert.equal(form.elements.username.value, '@anna');
});

test('people search preserves the tab, resets pagination and renders public profile links safely', async t => {
  const navigations = [], requests = [];
  const ctx = { route: parseRoute('#/discover?tab=people&search=%40ann&page=2'),
    async get(path) { requests.push(path); return { items: [{ username: 'anna', displayName: '<Анна>' }], total: 40, page: 2, pageSize: 20 }; },
    navigate: path => navigations.push(path) };
  const page = await discover(ctx);
  mount(t, page);
  assert.match(requests[0], /^\/users\?search=%40ann&page=2&pageSize=20$/);
  assert.equal(document.querySelector('article a').getAttribute('href'), '#/@anna');
  assert.match(page.html, /&lt;Анна&gt;/);
  const form = document.querySelector('form'); form.elements.search.value = '@new';
  form.onsubmit({ preventDefault });
  const next = parseRoute(navigations[0]);
  assert.equal(next.query.get('tab'), 'people');
  assert.equal(next.query.get('search'), '@new');
  assert.equal(next.query.has('page'), false);
});

test('empty people search and empty profile have useful empty states', async () => {
  const page = await peoplePage({ route: parseRoute('#/discover?tab=people'), get: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }) });
  assert.match(page.html, /Кого вы ищете/);
  const calls = [];
  const profile = await profilePage({ route: parseRoute('#/@anna?tab=outfits&page=2'), async get(path) {
    calls.push(path); return calls.length === 1 ? { username: 'anna', displayName: 'Анна' } : { items: [], total: 0, page: 2, pageSize: 20 };
  } }, 'anna');
  assert.deepEqual(calls, ['/users/anna', '/users/anna/outfits?page=2&pageSize=20']);
  assert.match(profile.html, /Публикаций пока нет/);
  assert.match(profile.html, /#\/@anna\?tab=outfits/);
});

test('username details fetch the resource directly without searching catalogs or private endpoints', async () => {
  const requests = [];
  const page = await detail({ auth: { user: null }, async get(path) {
    requests.push(path); return { id: 'coat', name: 'Coat', category: 'Top', ownerUsername: 'anna', isPublic: true };
  } }, 'items', 'coat', 'public', 'anna');
  assert.deepEqual(requests, ['/users/anna/items/coat']);
  assert.match(page.html, /Копировать публичную ссылку/);
  assert.match(page.html, /href="#\/@anna"/);
  assert.doesNotMatch(page.html, /id="delete-resource"/);
});

test('renaming updates the auth user and refreshed profile links; conflict preserves the form', async t => {
  let conflict = true, refreshes = 0;
  const auth = createAuth({ async request(path, options) {
    if (path === '/auth/me') return { username: 'anna', displayName: 'Анна', email: 'anna@example.com' };
    assert.equal(path, '/auth/me/profile');
    assert.equal(options.method, 'PATCH');
    if (conflict) throw new ApiError(409, { code: 'username_taken' });
    return { username: 'new_anna', displayName: 'Анна', email: 'anna@example.com' };
  } });
  await auth.restore();
  const ctx = { auth, isCurrent: () => true, updateHeader() { refreshes++; }, toast() {} };
  mount(t, account(ctx));
  const form = document.querySelector('form'); form.elements.username.value = 'new_anna';
  await form.onsubmit({ preventDefault });
  assert.match(document.querySelector('#profile-error').textContent, /уже занят/);
  assert.equal(auth.user.username, 'anna');
  assert.equal(form.elements.username.value, 'new_anna');
  conflict = false;
  await form.onsubmit({ preventDefault });
  assert.equal(refreshes, 2);
  assert.equal(auth.user.username, 'new_anna');
  assert.match(account(ctx).html, /href="#\/@new_anna"/);
});

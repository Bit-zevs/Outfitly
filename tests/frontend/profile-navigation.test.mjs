import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';

const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
const json = (body, status = 200) => new Response(JSON.stringify(body), { status });
async function until(predicate) {
  for (let i = 0; i < 100; i++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 10));
  }
  assert.fail('Expected navigation did not finish');
}

test('a late people search cannot overwrite a newer route, and @username detail routes load directly', async t => {
  const html = await readFile(new URL('../../src/frontend/index.html', import.meta.url), 'utf8');
  const dom = new JSDOM(html, { url: 'https://outfitly.test/#/discover?tab=people&search=old' });
  const previous = {};
  for (const name of ['window', 'document', 'location', 'history', 'FormData']) {
    previous[name] = globalThis[name]; globalThis[name] = dom.window[name];
  }
  previous.fetch = globalThis.fetch;
  dom.window.scrollTo = () => {};
  dom.window.HTMLDialogElement.prototype.close = function () { this.open = false; };
  let finishOld, oldSignal;
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push(url);
    if (url.endsWith('/engagement')) return json({ views: 0, likes: 0, comments: 0, canInteract: false });
    if (url.includes('/comments?')) return json({ items: [], total: 0, pageSize: 20 });
    if (url === '/api/auth/me') return json({}, 401);
    if (url.startsWith('/api/users?')) {
      if (url.includes('search=old')) {
        oldSignal = options.signal;
        return new Promise(resolve => { finishOld = () => resolve(json({ items: [{ username: 'old_person', displayName: 'Old result' }], total: 1, page: 1, pageSize: 20 })); });
      }
      return json({ items: [{ username: 'new_person', displayName: 'New result' }], total: 1, page: 1, pageSize: 20 });
    }
    if (url === '/api/users/new_person/items/00000000-0000-0000-0000-000000000001')
      return json({ id: '00000000-0000-0000-0000-000000000001', name: 'Named coat', category: 'Outerwear', ownerUsername: 'new_person', isPublic: true });
    throw new Error(`Unexpected request: ${url}`);
  };
  t.after(() => { finishOld?.(); Object.assign(globalThis, previous); dom.window.close(); });
  await import('../../src/frontend/src/main.js');
  await until(() => Boolean(finishOld));
  dom.window.location.hash = '#/discover?tab=people&search=new';
  await until(() => document.querySelector('#content').textContent.includes('New result'));
  assert.equal(oldSignal.aborted, true);
  finishOld();
  await new Promise(resolve => setTimeout(resolve, 20));
  assert.match(document.querySelector('#content').textContent, /New result/);
  assert.doesNotMatch(document.querySelector('#content').textContent, /Old result/);
  dom.window.location.hash = '#/@new_person/items/00000000-0000-0000-0000-000000000001';
  await until(() => document.querySelector('#content').textContent.includes('Named coat'));
  assert.match(document.querySelector('#content').textContent, /@new_person/);
  assert.equal(document.querySelector('#copy-public-address')?.textContent, 'Копировать публичную ссылку');
  assert.equal(requests.filter(url => url.startsWith('/api/items') && !url.endsWith('/engagement') && !url.includes('/comments?')).length, 0);
});

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { outfitEditor } from '../../src/frontend/src/editors.js';
import { ApiError } from '../../src/frontend/src/api.js';

const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
const deferred = () => {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
};
const flush = () => new Promise(resolve => setImmediate(resolve));

for (const fails of [false, true]) {
  test(`late catalog results cannot change outfit membership during a ${fails ? 'failed' : 'successful'} save`, async t => {
    const dom = new JSDOM('<main></main>');
    const previous = globalThis.document;
    globalThis.document = dom.window.document;
    t.after(() => { globalThis.document = previous; dom.window.close(); });
    const old = { id: 'old', name: 'Old coat', category: 'Outerwear' };
    const first = { id: 'first', name: 'Linen shirt', category: 'Top' };
    const late = { id: 'late', name: 'Late trousers', category: 'Bottom' };
    const initial = { name: 'Work', description: '', items: [old] };
    const search = deferred(), membership = deferred(), savingStarted = deferred();
    const commands = [], navigations = [], notifications = [];
    const ctx = {
      get: async () => initial, isCurrent: () => true,
      setDirty() {}, setSaving() {},
      navigate: path => navigations.push(path), toast: text => notifications.push(text),
      api: { async request(path, options = {}) {
        if (path.startsWith('/items?')) {
          if (new URLSearchParams(path.split('?')[1]).get('search')) return search.promise;
          return { items: [first, late], page: 1, pageSize: 8, total: 2 };
        }
        if (!options.method) return initial;
        commands.push([path, options.method]);
        if (path === '/outfits/look/items/first') { savingStarted.resolve(); return membership.promise; }
      } },
    };
    const editor = await outfitEditor(ctx, 'look');
    t.after(() => editor.cleanup());
    document.querySelector('main').innerHTML = editor.html;
    editor.bind(); await flush();
    document.querySelector('[data-remove="old"]').click();
    document.querySelector('[data-add="first"]').click();
    const staleAdd = document.querySelector('[data-add="late"]');
    const staleRemove = document.querySelector('[data-remove="first"]');
    document.querySelector('#editor-search').value = 'linen';
    const searching = document.querySelector('#find-items').onclick();
    const saving = document.querySelector('#outfit-form').onsubmit({ preventDefault() {} });
    await savingStarted.promise;
    search.resolve({ items: [first, late], page: 2, pageSize: 8, total: 24 });
    await searching;

    for (const selector of ['[data-add="late"]', '[data-add="first"]', '[data-remove="first"]', '#prev-items', '#next-items']) {
      assert.equal(document.querySelector(selector).disabled, true, `${selector} must stay disabled during saving`);
      document.querySelector(selector).click();
    }
    // A queued handler must also respect the save lock, even on a detached node.
    document.querySelector('[data-add="late"]').onclick();
    staleAdd.onclick(); staleRemove.onclick();
    assert.deepEqual([...document.querySelectorAll('[data-remove]')].map(el => el.dataset.remove), ['first']);
    assert.deepEqual(notifications, []);
    assert.deepEqual(navigations, []);

    if (fails) membership.reject(new ApiError(503));
    else membership.resolve();
    await saving;
    assert.equal(document.querySelector('[data-add="late"]').disabled, false);
    assert.equal(document.querySelector('[data-add="first"]').disabled, true, 'selected items stay disabled after unlocking');
    assert.equal(document.querySelector('[data-remove="first"]').disabled, false);
    assert.equal(document.querySelector('#next-items').disabled, false);
    assert.deepEqual(commands, [
      ['/outfits/look', 'PUT'], ['/outfits/look/items/first', 'POST'],
      ...(!fails ? [['/outfits/look/items/old', 'DELETE']] : []),
    ]);
    if (fails) {
      assert.deepEqual(navigations, []); assert.deepEqual(notifications, []);
      assert.equal(document.querySelector('#form-error').hidden, false);
      document.querySelector('[data-add="late"]').click();
      document.querySelector('[data-remove="first"]').click();
      assert.deepEqual([...document.querySelectorAll('[data-remove]')].map(el => el.dataset.remove), ['late']);
    } else {
      assert.deepEqual(navigations, ['/outfits/look']);
      assert.deepEqual(notifications, ['Образ сохранён']);
    }
  });
}

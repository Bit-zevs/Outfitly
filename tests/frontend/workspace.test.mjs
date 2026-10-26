import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createDraftSession, favoritesPage, bindWorkspaceActions } from '../../src/frontend/src/workspace.js';
import { ApiError } from '../../src/frontend/src/api.js';
const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
const flush = async () => { for (let i = 0; i < 10; i++) await new Promise(resolve => setImmediate(resolve)); };
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; };
function setup(t, request, extras = {}) {
  const dom = new JSDOM('<form><input name="name"><textarea name="description"></textarea><input name="category" value="Top"><input name="photoMode" value="file"><input name="photoUrl"><button type="submit">Save</button></form>');
  const previous = { document: globalThis.document, FormData: globalThis.FormData };
  globalThis.document = dom.window.document; globalThis.FormData = dom.window.FormData;
  t.after(() => { Object.assign(globalThis, previous); dom.window.close(); });
  let current = true; const dirty = [];
  const ctx = { isCurrent: () => current, get: path => request(path), api: { request }, setDirty: value => dirty.push(value), ...extras.ctx };
  const draft = { selected: [], file: null, removePhoto: false };
  const form = document.querySelector('form');
  const session = createDraftSession({ ctx, kind: extras.kind || 'outfits', id: extras.id, baseline: extras.baseline, form, draft, refresh() {} });
  t.after(() => session.dispose());
  return { dom, form, session, ctx, draft, dirty, leave() { current = false; } };
}
const remote = (id, body, version = 'version-1') => ({ id, kind: body.kind, targetId: body.targetId, version, baseVersion: body.baseVersion, content: body.content, photoUrl: null, updatedAtUtc: '2026-10-26T00:00:00Z' });

test('autosave waits one second, serializes requests and saves the latest input without clearing a newer draft', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const first = deferred(); const puts = []; let simultaneous = 0, maximum = 0;
  const state = setup(t, async (path, options = {}) => {
    if (path.startsWith('/drafts/active')) return { draft: null };
    puts.push(options.body); simultaneous++; maximum = Math.max(maximum, simultaneous);
    if (puts.length === 1) await first.promise;
    simultaneous--; return remote(path.split('/')[2], options.body, `v${puts.length}`);
  });
  await flush(); state.form.elements.name.value = 'First'; state.form.elements.name.dispatchEvent(new state.dom.window.Event('input', { bubbles: true }));
  t.mock.timers.tick(999); await flush(); assert.equal(puts.length, 0);
  t.mock.timers.tick(1); await flush(); assert.equal(puts.length, 1);
  state.form.elements.name.value = 'Newest'; state.form.elements.name.dispatchEvent(new state.dom.window.Event('input', { bubbles: true }));
  t.mock.timers.tick(1000); await flush(); assert.equal(puts.length, 1); assert.equal(state.dirty.at(-1), true);
  first.resolve(); await flush(); assert.equal(puts.length, 2); assert.equal(maximum, 1);
  assert.equal(puts[1].content.name, 'Newest'); assert.equal(puts[1].version, 'v1'); assert.equal(state.dirty.at(-1), false);
});

test('a conflicting autosave preserves local input until the server draft is reviewed explicitly', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] }); let activeReads = 0; const puts = [];
  const server = remote('server-id', { kind: 'outfits', targetId: null, content: { name: 'Other device', itemIds: [] } }, 'other-version');
  const state = setup(t, async (path, options = {}) => {
    if (path.startsWith('/drafts/active')) return { draft: ++activeReads === 1 ? null : server };
    puts.push(options.body); if (puts.length === 1) throw new ApiError(409); return remote('server-id', options.body);
  });
  await flush(); state.form.elements.name.value = 'My draft'; state.session.changed(); t.mock.timers.tick(1000); await flush();
  assert.equal(state.form.elements.name.value, 'My draft'); assert.equal(state.dirty.at(-1), true);
  document.querySelector('[data-retry]').click(); await flush(); assert.match(document.querySelector('.server-draft').textContent, /Other device/);
  document.querySelector('[data-keep]').click(); t.mock.timers.tick(1000); await flush();
  assert.equal(puts[1].version, 'other-version'); assert.equal(puts[1].content.name, 'My draft');
});

test('restoring a draft shows current resource data and explicitly rebases before atomic save', async t => {
  const saved = remote('draft-id', { kind: 'outfits', targetId: 'outfit', baseVersion: 'old', content: { name: 'Restored', description: 'My text', itemIds: ['item'] } });
  const writes = [];
  const state = setup(t, async (path, options = {}) => {
    if (path.startsWith('/drafts/active')) return { draft: saved };
    if (path === '/outfits/outfit') return { name: 'Current server name', description: 'Server text', editVersion: 'new', items: [] };
    if (path === '/items/item') return { id: 'item', name: 'Shirt' };
    writes.push([path, options]); return path.endsWith('/save') ? { id: 'outfit' } : remote('draft-id', options.body, 'next');
  }, { id: 'outfit', baseline: { editVersion: 'old' } });
  await flush(); assert.equal(state.form.elements.name.disabled, true); assert.match(document.querySelector('.server-draft').textContent, /Current server name/);
  document.querySelector('[data-restore]').click(); await flush();
  assert.equal(state.form.elements.name.value, 'Restored'); assert.equal(state.draft.selected[0].id, 'item');
  assert.equal(await state.session.save(), 'outfit');
  assert.equal(writes[0][1].body.baseVersion, 'new'); assert.deepEqual(writes[0][1].body.content.itemIds, ['item']);
  assert.equal(writes[1][0], '/drafts/draft-id/save');
});

test('a lost finalization response is recovered from the receipt without another creation or autosave', async t => {
  let id, finalizes = 0, puts = 0;
  const state = setup(t, async (path, options = {}) => {
    if (path.startsWith('/drafts/active')) return { draft: null };
    if (options.method === 'PUT') { puts++; id = path.split('/')[2]; return remote(id, options.body); }
    if (path.endsWith('/save')) { finalizes++; throw new Error('Connection lost'); }
    return { completedId: 'created-once' };
  });
  await flush(); state.form.elements.name.value = 'Saved';
  await assert.rejects(state.session.save()); assert.equal(await state.session.save(), 'created-once');
  assert.equal(puts, 1); assert.equal(finalizes, 1);
});

test('late responses after navigating away cannot change the new page', async t => {
  const pending = deferred(); const state = setup(t, () => pending.promise);
  state.leave(); document.querySelector('form').innerHTML = 'New page'; pending.resolve({ draft: null }); await flush();
  assert.equal(document.querySelector('form').textContent, 'New page');
});

test('the real outfit editor uses one atomic draft finalization instead of legacy partial commands', async t => {
  const { outfitEditor } = await import('../../src/frontend/src/editors.js');
  const dom = new JSDOM('<main></main>'); const previous = globalThis.document; globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  const writes = [], navigations = [];
  const ctx = { drafts: createDraftSession, isCurrent: () => true, setDirty() {}, setSaving() {}, toast() {}, navigate: path => navigations.push(path),
    api: { async request(path, options = {}) {
      if (path.startsWith('/items?')) return { items: [], total: 0, page: 1, pageSize: 8 };
      if (path.startsWith('/drafts/active')) return { draft: null };
      writes.push([path, options]);
      if (path.endsWith('/save')) return { id: 'saved' };
      assert.equal(options.method, 'PUT'); return remote(path.split('/')[2], options.body);
    } }
  };
  const page = await outfitEditor(ctx); document.querySelector('main').innerHTML = page.html; page.bind(); t.after(() => page.cleanup()); await flush();
  const form = document.querySelector('#outfit-form'); form.elements.name.value = 'My outfit'; await form.onsubmit({ preventDefault() {} }); await flush();
  assert.equal(writes.length, 2); assert.ok(writes[0][0].startsWith('/drafts/')); assert.ok(writes[1][0].endsWith('/save'));
  assert.deepEqual(navigations, ['/outfits/saved']);
});

test('an uploaded draft photo survives switching URL mode off again and is not shared with the resource early', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] }); let photoUrl = null; const writes = [];
  const state = setup(t, async (path, options = {}) => {
    if (path.startsWith('/drafts/active')) return { draft: null };
    writes.push([path, options]);
    if (path.includes('/photo?')) { assert.equal(options.method, 'PUT'); photoUrl = '/api/drafts/test/photo'; return { ...remote('test', { kind: 'items', content: {} }, 'photo-version'), photoUrl }; }
    return { ...remote('test', options.body, `v${writes.length}`), photoUrl };
  }, { kind: 'items' });
  await flush(); state.draft.file = new state.dom.window.File(['bytes'], 'shirt.png', { type: 'image/png' });
  state.session.changed(); t.mock.timers.tick(1000); await flush();
  assert.equal(writes.filter(([path]) => path.includes('/photo?')).length, 1);
  state.form.elements.photoMode.value = 'url'; state.session.changed(); t.mock.timers.tick(1000); await flush();
  state.form.elements.photoMode.value = 'file'; state.session.changed(); t.mock.timers.tick(1000); await flush();
  assert.equal(writes.filter(([, options]) => options.method === 'DELETE').length, 0);
  assert.ok(writes.every(([path]) => path.startsWith('/drafts/')));
});

test('uncertain finalization locks the editor until the receipt is checked, preserving newer input from accidental navigation', async t => {
  const navigated = [];
  const state = setup(t, async (path, options = {}) => {
    if (path.startsWith('/drafts/active')) return { draft: null };
    if (options.method === 'PUT') return remote(path.split('/')[2], options.body);
    if (path.endsWith('/save')) throw new Error('Unknown commit result');
    return { completedId: 'saved' };
  }, { ctx: { navigate: path => navigated.push(path) } });
  await flush(); state.form.elements.name.value = 'Submitted'; await assert.rejects(state.session.save()); state.session.lockUncertain();
  assert.equal(state.form.elements.name.disabled, true); assert.equal(document.querySelector('[data-retry]').disabled, false);
  document.querySelector('[data-retry]').click(); await flush(); assert.deepEqual(navigated, ['/outfits/saved']);
});


test('discarding a found draft deletes only that draft and does not save or overwrite the resource', async t => {
  const saved = remote('draft-id', { kind: 'outfits', content: { name: 'Unsaved', itemIds: [] } });
  const writes = [];
  const state = setup(t, async (path, options = {}) => {
    if (path.startsWith('/drafts/active')) return { draft: saved };
    writes.push([path, options.method]);
  });
  await flush(); document.querySelector('[data-discard]').click(); await flush();
  assert.deepEqual(writes, [['/drafts/draft-id?version=version-1', 'DELETE']]);
  assert.equal(state.form.elements.name.disabled, false); assert.equal(state.form.elements.name.value, '');
});

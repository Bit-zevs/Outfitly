import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { itemEditor } from '../../src/frontend/src/editors.js';
import { createDraftSession } from '../../src/frontend/src/workspace.js';

const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');

test('item creation links errors to the missing fields and focuses the first one', async t => {
  const page = await itemEditor({ setDirty() {} });
  const dom = new JSDOM(page.html);
  const previous = globalThis.document;
  globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  page.bind();

  const form = document.querySelector('#item-form');
  const status = document.querySelector('#item-required-status');
  form.requestSubmit();
  assert.match(status.textContent, /Заполните отмеченные поля/);
  assert.match(document.getElementById(form.elements.name.getAttribute('aria-describedby')).textContent, /Введите название/);
  assert.match(document.getElementById(form.elements.category.getAttribute('aria-describedby')).textContent, /Выберите категорию/);
  assert.equal(form.elements.name.getAttribute('aria-invalid'), 'true');
  assert.equal(form.elements.category.getAttribute('aria-invalid'), 'true');
  assert.equal(document.activeElement, form.elements.name);
  assert.equal(form.elements.visibility.getAttribute('role'), 'switch');
  assert.equal(form.elements.visibility.checked, false);

  for (const [name, value] of [['name', 'Рубашка'], ['category', 'Top']]) {
    const field = form.elements[name];
    field.value = value;
    field.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    assert.equal(field.hasAttribute('aria-invalid'), false);
    assert.equal(field.hasAttribute('aria-describedby'), false);
  }
  assert.equal(status.textContent, '');
});

test('rejecting a replacement clears the preview and removes an in-flight draft photo before saving', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const baseline = { id: 'shirt', name: 'Shirt', category: 'Top', photoUrl: '/api/items/shirt/photo' };
  let finishUpload, stagedPhoto = null, saved = false;
  const photoMethods = [];
  const page = await itemEditor({
    get: async () => baseline, drafts: createDraftSession, isCurrent: () => true,
    setDirty() {}, setSaving() {}, toast() {}, navigate() {},
    api: { async request(path, options = {}) {
      if (path.startsWith('/drafts/active')) return { draft: null };
      if (path.endsWith('/save')) {
        assert.equal(stagedPhoto, null);
        saved = true;
        return { id: baseline.id };
      }
      if (path.includes('/photo?')) {
        photoMethods.push(options.method);
        if (options.method === 'PUT') {
          await new Promise(resolve => { finishUpload = resolve; });
          stagedPhoto = '/api/drafts/photo';
        } else {
          assert.equal(options.method, 'DELETE');
          stagedPhoto = null;
        }
      } else {
        assert.equal(options.body.content.removePhoto, false);
      }
      return { version: 'version', photoUrl: stagedPhoto };
    } }
  }, baseline.id);
  const dom = new JSDOM(page.html);
  const previous = { document: globalThis.document, FormData: globalThis.FormData };
  Object.assign(globalThis, { document: dom.window.document, FormData: dom.window.FormData });
  t.mock.method(URL, 'createObjectURL', () => 'blob:old-draft');
  const revoke = t.mock.method(URL, 'revokeObjectURL', () => {});
  t.after(() => { page.cleanup(); Object.assign(globalThis, previous); dom.window.close(); });
  page.bind();
  await new Promise(resolve => setImmediate(resolve));

  const fileInput = document.querySelector('#file');
  const choose = (name, type) => {
    const file = new dom.window.File(['image'], name, { type });
    Object.defineProperty(fileInput, 'files', { configurable: true, value: [file] });
    fileInput.dispatchEvent(new dom.window.Event('change', { bubbles: true }));
  };
  choose('shirt.png', 'image/png');
  t.mock.timers.tick(1000);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(typeof finishUpload, 'function');
  choose('wrong.gif', 'image/gif');
  assert.equal(document.querySelector('#file-name').textContent, '');
  assert.equal(document.querySelector('#photo-preview img').getAttribute('src'), baseline.photoUrl);
  assert.equal(revoke.mock.calls[0].arguments[0], 'blob:old-draft');
  assert.match(document.querySelector('#file-error').textContent, /JPEG или PNG/);

  const save = document.querySelector('#item-form').onsubmit({ preventDefault() {} });
  finishUpload();
  await save;
  assert.deepEqual(photoMethods, ['PUT', 'DELETE']);
  assert.equal(saved, true);
});

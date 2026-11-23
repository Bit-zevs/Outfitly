import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { itemEditor } from '../../src/frontend/src/editors.js';

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

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { wardrobeFilters, bindWardrobeFilters, wardrobeQuery } from '../../src/frontend/src/wardrobe-filters.js';
import { discover, wardrobe } from '../../src/frontend/src/pages.js';
import { parseRoute } from '../../src/frontend/src/router.js';
import { pagination } from '../../src/frontend/src/components.js';
const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
test('sorting applies immediately and leaves edited filters for Apply', t => {
  const route = parseRoute('#/wardrobe?search=linen&color=removed&brandMissing=true&page=4&sort=nameDesc');
  const dom = new JSDOM(wardrobeFilters(route, { colors: ['<white>', null], brands: [null], sizes: ['m'] }, 2));
  const previous = globalThis.document; globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  let target; bindWardrobeFilters({ route, navigate: value => { target = value; } });
  const form = document.querySelector('form');
  assert.equal(form.elements.color.value, 'value:removed');
  assert.equal(form.elements.brand.value, 'missing');
  form.elements.search.value = ' cotton ';
  form.elements.category.value = 'Top'; form.elements.size.value = 'value:m'; form.elements.publication.value = 'private';
  form.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
  form.elements.sort.value = 'newest';
  form.elements.sort.dispatchEvent(new dom.window.Event('change'));
  const sorted = parseRoute('#' + target);
  assert.equal(sorted.query.get('sort'), 'newest');
  assert.equal(sorted.query.get('search'), 'linen');
  assert.equal(sorted.query.has('category'), false);
  assert.equal(sorted.query.has('page'), false);
  const apply = form.querySelector('button[type="submit"]');
  assert.equal(apply?.textContent, 'Применить');
  apply.click();
  const next = parseRoute(`#${target}`);
  assert.equal(next.query.get('search'), 'cotton'); assert.equal(next.query.get('brandMissing'), 'true');
  assert.equal(next.query.get('color'), 'removed'); assert.equal(next.query.get('size'), 'm');
  assert.equal(next.query.get('publication'), 'private'); assert.equal(next.query.get('category'), 'Top');
  assert.equal(next.query.has('page'), false);
  assert.equal(next.query.get('sort'), 'newest');
  assert.equal(document.querySelector('a').getAttribute('href'), '#/wardrobe');
  assert.match(dom.serialize(), /&lt;white&gt;/);
  assert.equal(wardrobeQuery(next.query).get('brandMissing'), 'true');
  assert.match(pagination(next, { page: 1, pageSize: 20, total: 30 }), /brandMissing=true/);
});
test('wardrobe loads options and filtered page and offers reset on zero matches', async () => {
  const calls = [];
  const page = await wardrobe({ route: parseRoute('#/wardrobe?size=m&publication=private'), get: async path => {
    calls.push(path); return path === '/items/filter-options' ? { colors: [], brands: [], sizes: [] } : { items: [], total: 0, page: 1, pageSize: 20 };
  } });
  assert.equal(calls.length, 2); assert.match(calls[0], /size=m/); assert.match(page.html, /Ничего не найдено/); assert.match(page.html, /Сбросить фильтры/);
  await assert.rejects(wardrobe({ route: parseRoute('#/wardrobe'), get: async () => { throw new Error('offline'); } }), /offline/);
});

test('an empty wardrobe offers adding a first item even with a sort applied', async () => {
  const page = await wardrobe({ route: parseRoute('#/wardrobe?sort=nameDesc'), get: async path => path === '/items/filter-options' ? { colors: [], brands: [], sizes: [] } : { items: [], total: 0, page: 1, pageSize: 20 } });
  assert.match(page.html, /Гардероб начинается с одной вещи/);
  assert.doesNotMatch(page.html, /Ничего не найдено/);
});
test('advanced selections stay visible when returning to a filtered wardrobe', () => {
  const options = { colors: ['Белый'], brands: [], sizes: [] };
  const dom = new JSDOM(wardrobeFilters(parseRoute('#/wardrobe?color=Белый&brandMissing=true'), options, 3));
  const document = dom.window.document;
  assert.equal(document.querySelector('details').open, true);
  assert.match(document.querySelector('.active-filters').textContent, /Цвет: Белый/);
  assert.match(document.querySelector('.active-filters').textContent, /Бренд: Не указан/);
  const chips = [...document.querySelector('.active-filters').children];
  assert.equal(chips.length, 2);
  assert.match(chips[0].getAttribute('aria-label'), /Цвет: Белый.*Убрать фильтр/);
  assert.equal(chips[0].getAttribute('href'), '#/wardrobe?brandMissing=true');
  dom.window.close();
});

test('public item filters retain the tab, apply missing values and clear pagination', async t => {
  const route = parseRoute('#/discover?tab=items&brand=studio&page=2');
  const calls = [];
  let destination;
  const page = await discover({ route, navigate: next => { destination = next; }, get: async path => {
    calls.push(path);
    return path.endsWith('/filter-options') ? { colors: [null], brands: ['studio'], sizes: [] }
      : { items: [], total: 0, page: 2, pageSize: 20 };
  } });
  const dom = new JSDOM(page.html);
  const previous = globalThis.document;
  globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  page.bind();
  const form = document.querySelector('form');
  assert.equal(calls[1], '/wardrobe/filter-options');
  assert.equal(form.elements.publication, undefined);
  assert.equal(form.elements.sort.value, 'newest');
  assert.equal(form.querySelector('[data-filter-focus="reset"]').hash, '#/discover?tab=items');
  assert.equal(form.querySelector('[data-filter-focus="remove-brand"]').hash, '#/discover?tab=items');
  assert.match(page.html, /Вещи не найдены/);
  form.elements.color.value = 'missing';
  form.elements.sort.value = 'likes';
  form.elements.sort.dispatchEvent(new dom.window.Event('change'));
  const sorted = parseRoute('#' + destination);
  assert.equal(sorted.query.get('sort'), 'likes');
  assert.equal(sorted.query.get('brand'), 'studio');
  assert.equal(sorted.query.has('colorMissing'), false);
  assert.equal(form.elements.color.value, 'missing');
  form.requestSubmit();
  const next = parseRoute('#' + destination);
  assert.equal(next.query.get('tab'), 'items');
  assert.equal(next.query.get('brand'), 'studio');
  assert.equal(next.query.get('colorMissing'), 'true');
  assert.equal(next.query.has('page'), false);
});

test('unavailable filter options can be retried without replacing results or edited fields', async t => {
  let optionsRequests = 0;
  const page = await discover({ route: parseRoute('#/discover?tab=items'), get: async path => {
    if (path.endsWith('/filter-options')) {
      if (++optionsRequests === 1) throw new Error('offline');
      return { colors: ['red'], brands: [], sizes: [] };
    }
    return { items: [{ id: 'one', name: 'Coat', category: 'Outerwear' }], total: 1, page: 1, pageSize: 20 };
  } });
  const dom = new JSDOM(page.html);
  const previous = globalThis.document;
  globalThis.document = dom.window.document;
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  page.bind();
  const card = document.querySelector('.item-card');
  const form = document.querySelector('#wardrobe-filters');
  form.elements.search.value = 'тренч';
  form.querySelector('.filter-options-retry button').click();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(document.querySelector('.item-card'), card);
  assert.equal(form.elements.search.value, 'тренч');
  assert.equal(form.elements.color.options[1].value, 'value:red');
  assert.equal(form.querySelector('.filter-options-retry button').hidden, true);
});

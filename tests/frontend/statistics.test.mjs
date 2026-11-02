import { test } from 'node:test';
import assert from 'node:assert/strict';
import { statisticsPage } from '../../src/frontend/src/statistics.js';
import { privateRoute } from '../../src/frontend/src/router.js';
const data = { items: 0, outfits: 0, publicItems: 0, publicOutfits: 0, itemsWithoutOutfits: 0, categories: [{ category: 'Top', count: 0 }], colors: [], brands: [], itemReactions: { views: 0, likes: 0 }, outfitReactions: { views: 0, likes: 0 } };
test('statistics is private and displays empty totals and zero categories', async () => {
  assert.equal(privateRoute('/statistics'), true);
  const page = await statisticsPage({ get: async path => { assert.equal(path, '/api/wardrobe/statistics'); return data; } });
  assert.match(page.html, /Гардероб пока пуст/);
  assert.match(page.html, /Верх/);
  assert.match(page.html, /value="0"/);
});
test('statistics escapes labels and keeps every group', async () => {
  const page = await statisticsPage({ get: async () => ({ ...data, items: 30, colors: Array.from({ length: 30 }, (_, i) => ({ label: `<color${i}>`, count: 1 })), brands: [{ label: null, count: 1 }] }) });
  assert.match(page.html, /&lt;color29&gt;/);
  assert.doesNotMatch(page.html, /<color/);
  assert.match(page.html, /Не указан/);
});
test('statistics propagates errors to the shared page retry handler', async () => {
  await assert.rejects(statisticsPage({ get: async () => { throw new Error('offline'); } }), /offline/);
});

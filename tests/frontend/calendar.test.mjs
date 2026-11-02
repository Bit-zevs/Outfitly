import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { localDate, validDate, calendarState, monthDays, calendarPage } from '../../src/frontend/src/calendar.js';
import { parseRoute, privateRoute } from '../../src/frontend/src/router.js';
const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
test('calendar dates stay local and month grid handles leap years and Monday offset', () => {
  assert.equal(localDate(new Date(2028, 1, 29, 0, 1)), '2028-02-29');
  assert.equal(localDate(new Date(2028, 1, 29, 23, 59)), '2028-02-29');
  assert.equal(validDate('2027-02-29'), false); assert.equal(validDate('2028-02-29'), true);
  assert.equal(monthDays('2028-02').dates.length, 29); assert.equal(monthDays('2028-05').offset, 0);
  assert.deepEqual(calendarState(new URLSearchParams('month=2028-02&date=2028-03-01'), '2026-11-02'), { month: '2028-02', date: '2028-02-01' });
  assert.equal(privateRoute('/calendar'), true);
});
function mount(t, html) {
  const dom = new JSDOM(`<dialog id="dialog"></dialog><main>${html}</main>`);
  const previous = globalThis.document; globalThis.document = dom.window.document;
  const dialog = document.querySelector('dialog');
  dialog.showModal = () => { dialog.open = true; };
  dialog.close = () => { dialog.open = false; dialog.dispatchEvent(new dom.window.Event('close')); };
  t.after(() => { globalThis.document = previous; dom.window.close(); });
  return dom;
}
test('month URL and selected day display current outfits with escaped names', async () => {
  const page = await calendarPage({ route: parseRoute('#/calendar?month=2028-02&date=2028-02-29'), get: async path => {
    assert.equal(path, '/calendar?month=2028-02');
    return [{ date: '2028-02-29', outfit: { id: 'one', name: '<coat>', items: [] } }];
  } });
  assert.match(page.html, /&lt;coat&gt;/); assert.match(page.html, /month=2028-03&amp;date=2028-03-01/);
  assert.match(page.html, /aria-current="date"/); assert.match(page.html, /data-calendar-remove="one"/);
});
test('picker excludes assigned outfits, searches and blocks duplicate saves', async t => {
  let saves = 0; let finish; let refreshed = 0;
  const entries = [{ date: '2028-02-29', outfit: { id: 'one', name: 'Assigned', items: [] } }];
  const ctx = { route: parseRoute('#/calendar?month=2028-02&date=2028-02-29'), isCurrent: () => true,
    get: async path => path.startsWith('/calendar') ? entries : [entries[0].outfit, { id: 'two', name: 'Вечер <test>', items: [] }],
    api: { request: async (path, options) => { saves++; assert.equal(options.method, 'PUT'); assert.equal(path, '/calendar/2028-02-29/outfits/two'); await new Promise(resolve => { finish = resolve; }); } }, refresh: () => { refreshed++; } };
  const page = await calendarPage(ctx); mount(t, page.html); page.bind(); document.querySelector('#calendar-add').click();
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(document.querySelectorAll('[data-calendar-pick]').length, 1);
  const search = document.querySelector('#calendar-search'); search.value = 'нет'; search.dispatchEvent(new document.defaultView.Event('input'));
  assert.equal(document.querySelectorAll('[data-calendar-pick]').length, 0);
  search.value = 'ВЕЧЕР'; search.dispatchEvent(new document.defaultView.Event('input'));
  const button = document.querySelector('[data-calendar-pick]'); button.click(); button.click(); assert.equal(saves, 1);
  finish(); await new Promise(resolve => setTimeout(resolve, 0)); assert.equal(refreshed, 1);
});
test('late picker response is ignored after navigation and failed month requests propagate', async t => {
  let current = true; let resolve;
  const page = await calendarPage({ route: parseRoute('#/calendar?month=2028-02'), isCurrent: () => current,
    get: path => path.startsWith('/calendar') ? [] : new Promise(done => { resolve = done; }) });
  mount(t, page.html); page.bind(); document.querySelector('#calendar-add').click(); current = false; resolve([{ id: 'late', name: 'Late' }]);
  await new Promise(done => setTimeout(done, 0)); assert.equal(document.querySelector('[data-calendar-pick]'), null);
  await assert.rejects(calendarPage({ route: parseRoute('#/calendar'), get: async () => { throw new Error('offline'); } }), /offline/);
});

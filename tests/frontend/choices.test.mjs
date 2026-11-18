import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { enhanceChoices } from '../../src/frontend/src/choices.js';
const { JSDOM } = createRequire(new URL('../../src/frontend/package.json', import.meta.url))('jsdom');
test('custom choices support arrows, selection, form data and Escape', t => {
 const dom = new JSDOM('<form><label>Размер<select name="size"><option>S</option><option>M</option><option>L</option></select></label></form>');
 const previous = { document: globalThis.document, MutationObserver: globalThis.MutationObserver };
 Object.assign(globalThis,{document:dom.window.document,MutationObserver:dom.window.MutationObserver});
 t.after(()=>{Object.assign(globalThis,previous);dom.window.close();});
 enhanceChoices(document); const trigger=document.querySelector('.choice-trigger'); trigger.click();
 const list=document.querySelector('.choice-list'); assert.equal(list.hidden,false);
 document.activeElement.dispatchEvent(new dom.window.KeyboardEvent('keydown',{key:'ArrowDown',bubbles:true,cancelable:true}));
 document.activeElement.click(); assert.equal(new dom.window.FormData(document.querySelector('form')).get('size'),'M');
 assert.equal(list.hidden,true); assert.equal(document.activeElement,trigger);
 trigger.click(); document.activeElement.dispatchEvent(new dom.window.KeyboardEvent('keydown',{key:'Escape',bubbles:true})); assert.equal(list.hidden,true);
});
test('color options show swatches and preserve accessible names', t => {
 const dom=new JSDOM('<label>Цвет<select name="color"><option>Белый</option><option>Синий</option></select></label>');
 const previous={document:globalThis.document,MutationObserver:globalThis.MutationObserver}; Object.assign(globalThis,{document:dom.window.document,MutationObserver:dom.window.MutationObserver});
 t.after(()=>{Object.assign(globalThis,previous);dom.window.close();}); enhanceChoices(document);
 const trigger=document.querySelector('.choice-trigger');assert.equal(trigger.getAttribute('aria-label'),'Цвет: Белый'); trigger.click();
 assert.equal(document.querySelectorAll('[role=option] .color-swatch').length,2);
});

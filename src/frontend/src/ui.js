import { escape } from './components.js';
import { errorMessage } from './api.js';

let toastTimer;
let focusGuardInstalled = false;
export function toast(message) {
  const element = document.querySelector('#toast');
  element.textContent = message;
  element.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { element.hidden = true; }, 5000);
}
export function showError(element, error, prefix = '') {
  element.hidden = false;
  element.textContent = prefix + errorMessage(error);
  element.focus();
}
export function busy(form, value) {
  form.setAttribute('aria-busy', String(value));
  form.querySelectorAll('button, input, select, textarea').forEach(el => {
    if (value) { el.dataset.wasDisabled = String(el.disabled); el.disabled = true; }
    else { el.disabled = el.dataset.wasDisabled === 'true'; delete el.dataset.wasDisabled; }
  });
}
export function modal(heading, content, bind = () => {}) {
  if (!focusGuardInstalled) {
    document.addEventListener('keydown', event => {
      const activeDialog = document.querySelector('#dialog');
      if (event.key !== 'Tab' || !activeDialog.open) return;
      const focusable = [...activeDialog.querySelectorAll('button, a[href], input, select, textarea, [tabindex="0"]')]
        .filter(element => !element.disabled && element.getClientRects().length);
      if (!focusable.length) { event.preventDefault(); return; }
      const current = focusable.indexOf(document.activeElement);
      const next = current < 0 ? (event.shiftKey ? focusable.length - 1 : 0) : (current + (event.shiftKey ? -1 : 1) + focusable.length) % focusable.length;
      event.preventDefault();
      focusable[next].focus();
    });
    focusGuardInstalled = true;
  }
  const dialog = document.querySelector('#dialog');
  const previous = document.activeElement;
  if (dialog.open) dialog.close();
  dialog.innerHTML = `<div class="modal-head"><h2 id="dialog-title">${escape(heading)}</h2><button type="button" class="icon-button" data-close aria-label="Закрыть диалог">×</button></div>${content}`;
  dialog.querySelector('[data-close]').onclick = () => dialog.close();
  dialog.onclick = event => { if (event.target === dialog && (event.clientX < dialog.getBoundingClientRect().left || event.clientX > dialog.getBoundingClientRect().right || event.clientY < dialog.getBoundingClientRect().top || event.clientY > dialog.getBoundingClientRect().bottom)) dialog.close(); };
  dialog.onclose = () => { if (previous?.isConnected) previous.focus(); };
  dialog.showModal();
  bind(dialog);
  return dialog;
}
export function confirmAction(heading, text, action) {
  modal(heading, `<p class="lead">${escape(text)}</p><p class="error" role="alert" tabindex="-1" hidden></p><form class="actions"><button class="btn outline" type="button" data-cancel>Отмена</button><button class="btn danger" type="submit">Удалить</button></form>`, dialog => {
    dialog.querySelector('[data-cancel]').onclick = () => dialog.close();
    dialog.querySelector('form').onsubmit = async event => {
      event.preventDefault();
      const form = event.currentTarget;
      if (form.getAttribute('aria-busy') === 'true') return;
      busy(form, true);
      try { await action(); dialog.close(); }
      catch (error) { showError(dialog.querySelector('.error'), error); }
      finally { busy(form, false); }
    };
  });
}

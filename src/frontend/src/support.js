import { title, escape, link, empty, pagination } from './components.js';
import { showError } from './ui.js';
export const supportCategories = { feedback: 'Отзыв', suggestion: 'Предложение', bug: 'Баг', problem: 'Проблема' };
export const supportStatuses = { new: 'Новое', inProgress: 'В работе', closed: 'Закрыто' };
const stamp = value => new Date(value.endsWith('Z') ? value : `${value}Z`).toLocaleString('ru-RU');
const choices = (name, values, current, all = false) => `<label class="field">${name === 'category' ? 'Категория' : 'Статус'}<select name="${name}">${all ? '<option value="">Все</option>' : ''}${Object.entries(values).map(([value, label]) => `<option value="${value}" ${value === current ? 'selected' : ''}>${label}</option>`).join('')}</select></label>`;
const attachments = '<label class="field">Скриншоты (до 3, по 5 МиБ)<input type="file" name="images" accept="image/jpeg,image/png,image/webp" multiple></label>';
const messageForm = (create = false) => `<form id="support-form">${create ? `${choices('category', supportCategories)}<label class="field">Тема<input name="subject" required maxlength="150"></label>` : ''}<label class="field">${create ? 'Описание' : 'Сообщение'}<textarea name="text" maxlength="5000" ${create ? 'required' : ''} rows="6"></textarea></label>${attachments}<p id="support-form-error" class="error" role="alert" tabindex="-1" hidden></p><button class="btn" type="submit">Отправить</button></form>`;
const current = ctx => !ctx.signal?.aborted && (ctx.isCurrent?.() ?? true);
export async function supportPage(ctx, admin = false, id = null) {
  if (admin && !ctx.auth.user?.isSupportAdmin) return { title: 'Нет доступа', html: empty('Нет доступа', 'Этот раздел доступен администраторам.') };
  const base = admin ? '/admin/support' : '/support'; const api = `${base}/tickets`;
  if (id === 'new' && !admin) return { title: 'Новое обращение', html: title('Поддержка', 'Новое обращение') + messageForm(true), bind() { bindMessage(ctx, api, null, false); } };
  if (!id) {
    const query = new URLSearchParams();
    for (const key of ['page', 'category', 'status']) if (ctx.route.query.get(key)) query.set(key, ctx.route.query.get(key));
    const page = await ctx.get(`${api}?${query}`);
    return { title: 'Поддержка', html: title('Обратная связь', admin ? 'Обращения пользователей' : 'Мои обращения', '', !admin ? link('/support/new', 'Написать в поддержку', 'btn') : '') +
      `${!admin && ctx.auth.user?.isSupportAdmin ? link('/admin/support', 'Обращения пользователей', 'btn outline') : ''}<form id="support-filters" class="support-filters">${choices('category', supportCategories, query.get('category'), true)}${choices('status', supportStatuses, query.get('status'), true)}<button class="btn">Применить</button>${link(base, 'Сбросить', 'btn outline')}</form>` +
      (page.items.length ? page.items.map(x => `<article class="linebox"><h2>${link(`${base}/${x.id}`, x.subject)}</h2><p>${supportCategories[x.category]} · ${supportStatuses[x.status]} · ${escape(stamp(x.updatedAtUtc))}</p></article>`).join('') : empty('Обращений пока нет', 'Здесь появятся обращения и ответы поддержки.')) + pagination(ctx.route, page), bind() {
        document.querySelector('#support-filters').onsubmit = event => { event.preventDefault(); const form = event.currentTarget; const q = new URLSearchParams(); for (const key of ['category', 'status']) if (form.elements[key].value) q.set(key, form.elements[key].value); ctx.navigate(`${base}?${q}`); };
      } };
  }
  let ticket = await ctx.get(`${api}/${id}`);
  const renderMessages = () => ticket.messages.map(m => `<article class="linebox"><p><strong>${m.isAdmin ? 'Поддержка' : 'Пользователь'}</strong> · ${escape(stamp(m.createdAtUtc))}</p><p class="support-text">${escape(m.text)}</p><div class="support-attachments">${m.attachments.map(a => `<a href="${escape(a.url)}" target="_blank" rel="noopener"><img src="${escape(a.url)}" alt="Скриншот к сообщению" loading="lazy"></a>`).join('')}</div></article>`).join('');
  return { title: ticket.subject, html: title('Поддержка', ticket.subject) + `<p id="support-status">${supportCategories[ticket.category]} · ${supportStatuses[ticket.status]}</p>${admin ? `<form id="support-status-form">${choices('status', { inProgress: 'В работе', closed: 'Закрыто' }, ticket.status)}<button class="btn outline">Изменить статус</button></form>` : ''}<p class="error" id="support-error" role="alert" tabindex="-1" hidden></p><div id="support-messages">${renderMessages()}</div><div id="support-reply">${ticket.status === 'closed' ? '<p>Обращение закрыто. Новые сообщения недоступны.</p>' : messageForm()}</div>`, bind() {
    const reloadConflict = async () => {
      ticket = await ctx.get(`${api}/${id}`); if (!current(ctx)) return;
      document.querySelector('#support-messages').innerHTML = renderMessages();
      document.querySelector('#support-status').textContent = `${supportCategories[ticket.category]} · ${supportStatuses[ticket.status]}`;
      if (ticket.status === 'closed') { document.querySelector('#support-form button')?.setAttribute('disabled', ''); }
    };
    if (ticket.status !== 'closed') bindMessage(ctx, `${api}/${id}/messages`, () => ticket.revision, ticket.status === 'closed', reloadConflict);
    const statusForm = document.querySelector('#support-status-form'); let savingStatus = false;
    if (statusForm) statusForm.onsubmit = async event => {
      event.preventDefault(); if (savingStatus) return; savingStatus = true; ctx.setSaving?.(true); statusForm.querySelector('button').disabled = true;
      try { await ctx.api.request(`${api}/${id}/status`, { method: 'PATCH', body: { revision: ticket.revision, status: statusForm.elements.status.value }, signal: ctx.signal }); if (current(ctx)) ctx.refresh(); }
      catch (error) { if (current(ctx)) { if (error.status === 409) { try { await reloadConflict(); } catch { /* Retry preserves the current page. */ } } if (current(ctx)) showError(document.querySelector('#support-error'), error); } }
      finally { savingStatus = false; if (current(ctx)) { ctx.setSaving?.(false); statusForm.querySelector('button').disabled = false; } }
    };
  } };
}
function bindMessage(ctx, path, revision, closed, conflict) {
  const form = document.querySelector('#support-form'); let saving = false;
  form.oninput = () => ctx.setDirty?.(true);
  form.onsubmit = async event => {
    event.preventDefault(); if (saving || closed) return;
    const errorElement = document.querySelector('#support-form-error'); errorElement.hidden = true;
    const files = [...form.elements.images.files];
    if (files.length > 3 || files.some(f => f.size > 5 * 1024 * 1024)) { showError(errorElement, new Error('attachments')); errorElement.textContent = 'Выберите до трёх изображений, каждое не больше 5 МиБ.'; return; }
    if (!form.elements.text.value.trim() && !files.length) { errorElement.hidden = false; errorElement.textContent = 'Введите сообщение или выберите изображение.'; return; }
    const body = new FormData();
    for (const key of ['category', 'subject', 'text']) if (form.elements[key]) body.append(key, form.elements[key].value);
    files.forEach(file => body.append('images', file)); if (revision) body.append('revision', revision());
    saving = true; ctx.setSaving?.(true); form.querySelector('button').disabled = true;
    try {
      const result = await ctx.api.request(path, { method: 'POST', body, signal: ctx.signal });
      if (!current(ctx)) return; ctx.setDirty?.(false);
      if (!revision) ctx.navigate(`/support/${result.id}`); else ctx.refresh();
    } catch (error) {
      if (!current(ctx)) return;
      if (error.status === 409 && conflict) { try { await conflict(); } catch { /* Preserve text even when refresh fails. */ } }
      if (current(ctx)) showError(errorElement, error);
    } finally { saving = false; if (current(ctx)) { ctx.setSaving?.(false); form.querySelector('button').disabled = document.querySelector('#support-status')?.textContent.includes('Закрыто') ?? false; } }
  };
}

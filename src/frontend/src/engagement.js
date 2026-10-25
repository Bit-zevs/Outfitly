import { avatar, escape } from './components.js';
import { errorMessage } from './api.js';

export function bindEngagement(ctx, kind, resource, shareToken) {
  const root = document.querySelector('#engagement');
  if (!root) return;
  const base = `/${kind}/${resource.id}`;
  const token = shareToken ? `shareToken=${encodeURIComponent(shareToken)}` : '';
  const path = (suffix, query = '') => `${base}${suffix}${token || query ? '?' : ''}${[token, query].filter(Boolean).join('&')}`;
  const request = (suffix, options, query) => ctx.api.request(path(suffix, query), options);
  const active = () => ctx.isCurrent() && root.isConnected;
  let stats;
  let statsRequest = 0;
  let likePending = false;
  root.innerHTML = '<h2>Обсуждение</h2><div class="engagement-stats"></div><p class="error" role="alert" tabindex="-1" hidden></p><div class="comment-compose"></div><div class="comment-list"></div>';
  const error = root.querySelector('.error');
  const fail = e => { if (active()) { error.textContent = errorMessage(e); error.hidden = false; } };
  async function action(button, operation) {
    button.disabled = true; error.hidden = true;
    try { await operation(); } catch (e) { fail(e); } finally { if (active()) button.disabled = false; }
  }
  async function loadStats() {
    const requestId = ++statsRequest;
    const value = await request('/engagement');
    if (!active() || requestId !== statsRequest) return;
    stats = value;
    root.querySelector('.engagement-stats').innerHTML = `<p>Просмотры: ${value.views} · Лайки: ${value.likes} · Комментарии: ${value.comments}</p>${value.canInteract ? `<button class="btn outline small" aria-pressed="${value.isLiked}" id="post-like">${value.isLiked ? 'Убрать лайк' : 'Нравится'}</button>` : '<p><a href="#/login">Войдите с подтверждённой почтой</a>, чтобы участвовать в обсуждении.</p>'}`;
    const like = root.querySelector('#post-like');
    if (like) {
      like.disabled = likePending;
      like.onclick = async () => {
        if (likePending) return;
        likePending = true;
        ++statsRequest; // Discard snapshots started before this mutation.
        try {
          await action(like, async () => {
            await request('/like', { method: value.isLiked ? 'DELETE' : 'PUT' });
            await loadStats();
          });
        } finally {
          likePending = false;
          const currentLike = active() && root.querySelector('#post-like');
          if (currentLike) currentLike.disabled = false;
        }
      };
    }
  }
  function compose(container, { parentId = null, comment = null, complete } = {}) {
    const form = document.createElement('form');
    form.className = 'comment-form';
    form.innerHTML = `<label>${comment ? 'Редактировать комментарий' : parentId ? 'Ваш ответ' : 'Ваш комментарий'}<textarea required maxlength="2000" rows="3"></textarea></label><button class="btn small" type="submit">${comment ? 'Сохранить' : 'Отправить'}</button><button class="btn outline small" type="button">Отмена</button>`;
    const input = form.querySelector('textarea'); input.value = comment?.text || '';
    if (comment || parentId) form.querySelector('[type=button]').onclick = () => form.remove();
    else form.querySelector('[type=button]').remove();
    let submitting = false;
    form.onsubmit = event => { event.preventDefault(); if (submitting) return; submitting = true; form.setAttribute('aria-busy', 'true'); form.querySelectorAll('textarea, button').forEach(control => { control.disabled = true; }); const button = form.querySelector('[type=submit]'); action(button, async () => {
      await request(comment ? `/comments/${comment.id}` : '/comments', { method: comment ? 'PATCH' : 'POST', body: { text: input.value, parentId } });
      if (!active()) return;
      input.value = ''; await complete(); await loadStats();
    }).finally(() => { submitting = false; form.removeAttribute('aria-busy'); form.querySelectorAll('textarea, button').forEach(control => { control.disabled = false; }); }); };
    container.append(form);
  }
  async function loadComments(container, parentId = null, page = 1) {
    const data = await request('/comments', undefined, `page=${page}${parentId ? `&parentId=${parentId}` : ''}`);
    if (!active()) return;
    if (page === 1) container.replaceChildren();
    container.querySelector(':scope > .load-more')?.remove();
    for (const comment of data.items) {
      const article = document.createElement('article'); article.className = 'post-comment';
      article.innerHTML = `<p class="author-line">${avatar(comment.author)}<a href="#/@${encodeURIComponent(comment.author.username)}">@${escape(comment.author.username)}</a> · ${escape(new Date(comment.createdAt).toLocaleString('ru-RU'))}${comment.updatedAt ? ' · изменён' : ''}</p><p class="comment-text">${comment.isDeleted ? 'Комментарий удалён' : escape(comment.text)}</p><div class="comment-actions"></div><div class="comment-editor"></div>${!parentId ? '<div class="comment-replies"></div>' : ''}`;
      const controls = article.querySelector('.comment-actions');
      const addButton = (label, handler) => { const button = document.createElement('button'); button.className = 'btn outline small'; button.textContent = label; button.onclick = () => handler(button); controls.append(button); };
      const refresh = () => loadComments(container, parentId);
      if (comment.canEdit) addButton('Изменить', () => { const editor = article.querySelector('.comment-editor'); editor.replaceChildren(); compose(editor, { comment, complete: refresh }); });
      if (comment.canDelete) addButton('Удалить', button => action(button, async () => { await request(`/comments/${comment.id}`, { method: 'DELETE' }); await refresh(); await loadStats(); }));
      if (!parentId) {
        const replies = article.querySelector('.comment-replies');
        addButton('Ответы', button => action(button, () => loadComments(replies, comment.id)));
        if (stats.canInteract && !comment.isDeleted) addButton('Ответить', () => { const editor = article.querySelector('.comment-editor'); editor.replaceChildren(); compose(editor, { parentId: comment.id, complete: async () => { editor.replaceChildren(); await loadComments(replies, comment.id); } }); });
      }
      container.append(article);
    }
    if (page * data.pageSize < data.total) {
      const more = document.createElement('button'); more.className = 'btn outline small load-more'; more.textContent = 'Показать ещё';
      more.onclick = () => action(more, () => loadComments(container, parentId, page + 1)); container.append(more);
    }
  }
  async function initialize() {
    await loadStats(); if (!active()) return;
    const list = root.querySelector('.comment-list');
    if (stats.canInteract) {
      compose(root.querySelector('.comment-compose'), { complete: () => loadComments(list) });
    }
    await loadComments(list);
    if (stats.canInteract && active()) {
      // A separate command keeps GET side-effect free. A conflict can be repeated in a fresh request.
      try { await request('/views', { method: 'POST' }); }
      catch (e) { if (e.status === 409) await request('/views', { method: 'POST' }); else throw e; }
      await loadStats();
    }
  }
  initialize().catch(fail);
}

import { escape, empty, link, pagination, photo, title } from './components.js';
import { errorMessage } from './api.js';
import { busy } from './ui.js';

export function bindWorkspaceActions(ctx, kind, resource, token) {
  const root = document.querySelector('#workspace-actions'); if (!root) return;
  if (!ctx.auth.user) { root.innerHTML = link('/login', 'Войдите, чтобы сохранить в избранное', 'btn outline'); return; }
  const base = `/${kind}/${resource.id}`, query = token ? `?shareToken=${encodeURIComponent(token)}` : '';
  let disposed = false, pending = false, saved = false, operationId;
  const active = () => !disposed && ctx.isCurrent() && root.isConnected;
  root.innerHTML = '<button class="btn outline" id="favorite-resource" disabled>Избранное…</button>' + (kind === 'outfits' ? '<button class="btn outline" id="copy-outfit">Копировать в мой гардероб</button>' : '') + '<p class="error" role="alert" id="workspace-error" hidden></p>';
  const favorite = root.querySelector('#favorite-resource'), error = root.querySelector('#workspace-error');
  const render = () => { favorite.textContent = saved ? 'Убрать из избранного' : 'В избранное'; favorite.setAttribute('aria-pressed', String(saved)); };
  ctx.api.request(base + '/favorite' + query).then(result => { if (active()) { saved = result.isFavorite; render(); favorite.disabled = false; } }).catch(e => { if (active()) { error.hidden = false; error.textContent = errorMessage(e); } });
  async function act(button, action) {
    if (pending) return; pending = true; button.disabled = true; error.hidden = true;
    try { await action(); } catch (e) { if (active()) { error.hidden = false; error.textContent = errorMessage(e); } }
    finally { pending = false; if (active()) button.disabled = false; }
  }
  favorite.onclick = () => act(favorite, async () => { await ctx.api.request(base + '/favorite' + (saved ? '' : query), { method: saved ? 'DELETE' : 'PUT' }); if (active()) { saved = !saved; render(); } });
  const copy = root.querySelector('#copy-outfit');
  if (copy) copy.onclick = () => act(copy, async () => {
    operationId ||= crypto.randomUUID();
    const result = await ctx.api.request(base + '/copies' + query, { method: 'POST', body: { operationId } });
    if (active()) { operationId = null; ctx.toast('Образ и его вещи скопированы'); ctx.navigate(`/outfits/${result.id}`); }
  });
  return () => { disposed = true; };
}
export async function favoritesPage(ctx) {
  const kind = ctx.route.query.get('kind') === 'items' ? 'items' : 'outfits';
  const page = await ctx.get(`/favorites?kind=${kind}&page=${Number(ctx.route.query.get('page')) || 1}`);
  return { title: 'Избранное', html: `${title('Сохранённое для вас', 'Избранное')}<nav class="tabs">${link('/favorites', 'Образы', kind === 'outfits' ? 'selected' : '')}${link('/favorites?kind=items', 'Вещи', kind === 'items' ? 'selected' : '')}</nav>${page.items.length ? `<div class="cards">${page.items.map(x => `<article class="linebox">${x.available ? `${photo({ name: x.name, photoUrl: x.photoUrl })}${link(x.route, x.name)}` : '<h2>Недоступно</h2><p>Оригинал удалён или доступ к нему закрыт.</p>'}<button class="btn outline small" data-remove-favorite="${escape(x.targetId)}">Убрать из избранного</button></article>`).join('')}</div>` : empty('В избранном пока пусто', 'Сохраняйте вещи и образы на их страницах.')}${pagination(ctx.route, page)}<p class="error" role="alert" hidden id="favorites-error"></p>`, bind() {
    document.querySelectorAll('[data-remove-favorite]').forEach(button => { button.onclick = async () => {
      if (button.disabled) return; button.disabled = true;
      try { await ctx.api.request(`/${kind}/${button.dataset.removeFavorite}/favorite`, { method: 'DELETE' }); if (ctx.isCurrent()) ctx.refresh(); }
      catch (e) { if (ctx.isCurrent()) { const error = document.querySelector('#favorites-error'); error.hidden = false; error.textContent = errorMessage(e); } }
      finally { button.disabled = false; }
    }; });
  } };
}
export async function draftsPage(ctx) {
  const page = await ctx.get(`/drafts?page=${Number(ctx.route.query.get('page')) || 1}`);
  return { title: 'Черновики', html: `${title('Незавершённое редактирование', 'Черновики', 'Здесь хранятся автоматически сохранённые изменения. Публикации изменяются только после явного сохранения.')} ${page.items.length ? page.items.map(d => `<article class="linebox"><h2>${escape(d.content.name || (d.kind === 'items' ? 'Новая вещь' : 'Новый образ'))}</h2><p>${escape(new Date(d.updatedAtUtc + (d.updatedAtUtc.endsWith('Z') ? '' : 'Z')).toLocaleString('ru-RU'))}</p><details><summary>Посмотреть черновик</summary><p class="profile-bio">${escape(d.content.description || '')}</p>${d.photoUrl ? photo({ name: d.content.name, photoUrl: d.photoUrl }) : ''}<p>${d.kind === 'outfits' ? `Выбрано вещей: ${(d.content.itemIds || []).length}` : escape([d.content.category, d.content.color, d.content.size, d.content.brand].filter(Boolean).join(' · '))}</p></details><div class="actions">${link(d.targetId ? `/${d.kind}/${d.targetId}/edit` : `/${d.kind}/new`, 'Продолжить', 'btn outline')}<button class="btn outline danger" data-delete-draft="${escape(d.id)}" data-version="${escape(d.version)}">Удалить черновик</button></div></article>`).join('') : empty('Черновиков пока нет', 'Изменения в редакторах сохраняются автоматически.')} ${pagination(ctx.route, page)}<p class="error" id="drafts-error" role="alert" hidden></p>`, bind() {
    document.querySelectorAll('[data-delete-draft]').forEach(button => { button.onclick = async () => {
      if (button.disabled) return; button.disabled = true;
      try { await ctx.api.request(`/drafts/${button.dataset.deleteDraft}?version=${button.dataset.version}`, { method: 'DELETE' }); if (ctx.isCurrent()) ctx.refresh(); }
      catch (e) { if (ctx.isCurrent()) { const error = document.querySelector('#drafts-error'); error.hidden = false; error.textContent = errorMessage(e); } }
      finally { button.disabled = false; }
    }; });
  } };
}

export function createDraftSession({ ctx, kind, id, form, draft, baseline, refresh }) {
  const panel = document.createElement('section'); panel.className = 'linebox server-draft'; form.prepend(panel);
  let stored = null, draftId = crypto.randomUUID(), baseVersion = baseline?.editVersion || null;
  let timer, running, generation = 0, savedGeneration = 0, uploadedFile = null, disposed = false, blocked = true, completed = false, finalizing = false;
  const active = () => !disposed && ctx.isCurrent() && form.isConnected;
  const status = text => { if (active()) panel.textContent = text; };
  const request = (path, options) => ctx.api.request(path, options);
  function snapshot() {
    const value = name => form.elements[name]?.value || null;
    return kind === 'items' ? { name: value('name'), description: value('description'), category: value('category'), color: value('color'), size: value('size'), brand: value('brand'), photoUrl: value('photoUrl'), photoMode: form.elements.photoMode.value, removePhoto: draft.removePhoto }
      : { name: value('name'), description: value('description'), itemIds: draft.selected.map(x => x.id), photoMode: 'file' };
  }
  async function apply(remote) {
    const content = remote.content;
    for (const name of ['name', 'description', 'category', 'color', 'size', 'brand', 'photoUrl']) if (form.elements[name]) form.elements[name].value = content[name] || '';
    if (kind === 'items') {
      form.elements.photoMode.value = content.photoMode; draft.photoMode = content.photoMode; draft.file = null; uploadedFile = null;
      draft.removePhoto = content.removePhoto; draft.restoredPhotoUrl = remote.photoUrl;
    } else {
      const items = await Promise.all((content.itemIds || []).map(async itemId => {
        try { return await ctx.get(`/items/${itemId}`); } catch (e) { if (e.status === 404 || e.status === 403) return { id: itemId, name: 'Недоступная вещь', category: '', photoUrl: null }; throw e; }
      }));
      if (!active()) return; draft.selected = items;
    }
    if (!active()) return;
    stored = remote; draftId = remote.id; blocked = false; generation = savedGeneration = 0; busy(form, false); refresh(); ctx.setDirty(false); status('Черновик восстановлен. Изменения ещё не применены к публикации.');
  }
  async function offer(remote, conflict = false) {
    blocked = true;
    let current = baseline;
    if (id) current = await ctx.get(`/${kind}/${id}`);
    if (!active()) return;
    const changed = id && remote.baseVersion !== current.editVersion;
    panel.innerHTML = `<p>Найден черновик от ${escape(new Date(remote.updatedAtUtc + (remote.updatedAtUtc.endsWith('Z') ? '' : 'Z')).toLocaleString('ru-RU'))}.</p><p>В черновике: ${escape(remote.content.name || 'Без названия')} — ${escape(remote.content.description || '')}</p>${changed ? `<p>Оригинал изменился. Сейчас на сервере: ${escape(current.name)} — ${escape(current.description || '')}.</p><p>Состав: ${escape((current.items || []).map(x => x.name).join(', '))}</p>` : ''}<button type="button" class="btn outline" data-restore>${changed ? 'Я сверил данные — восстановить' : 'Восстановить'}</button><button type="button" class="btn outline danger" data-discard>Удалить черновик</button>${conflict ? '<button type="button" class="btn outline" data-keep>Я сверил данные — сохранить мой вариант</button>' : ''}`;
    // The restore decision remains operable while the resource fields are locked.
    panel.querySelector('[data-restore]').onclick = async () => {
      try { await apply(remote); if (active()) { baseVersion = current?.editVersion || remote.baseVersion; if (changed) changedLocally(); } } catch (e) { showFailure(e); }
    };
    panel.querySelector('[data-discard]').onclick = async () => {
      const buttons = panel.querySelectorAll('button'); buttons.forEach(x => { x.disabled = true; });
      try { await request(`/drafts/${remote.id}?version=${remote.version}`, { method: 'DELETE' }); if (!active()) return; stored = null; draftId = crypto.randomUUID(); blocked = false; baseVersion = current?.editVersion || null; busy(form, false); refresh(); status('Черновик удалён.'); }
      catch (e) { showFailure(e); buttons.forEach(x => { x.disabled = false; }); }
    };
    const keep = panel.querySelector('[data-keep]');
    if (keep) keep.onclick = () => { stored = remote; draftId = remote.id; uploadedFile = null; baseVersion = current?.editVersion || remote.baseVersion; blocked = false; busy(form, false); refresh(); changedLocally(); };
  }
  function showFailure(error) {
    if (!active()) return;
    blocked ||= error.status === 409;
    panel.innerHTML = `<p class="error" role="alert">${escape(errorMessage(error))} Изменения остаются в редакторе.</p><button type="button" class="btn outline" data-retry>Повторить / сверить черновик</button>`;
    panel.querySelector('[data-retry]').onclick = async () => {
      try {
        if (finalizing) {
          const result = await request(`/drafts/${draftId}`);
          if (result.completedId) { completed = true; ctx.setDirty(false); ctx.navigate?.(`/${kind}/${result.completedId}`); return; }
          finalizing = false; stored = result; blocked = false; busy(form, false); refresh(); await flush(); return;
        }
        if (blocked) { const result = await request(`/drafts/active?kind=${kind}${id ? `&targetId=${id}` : ''}`); if (result.draft) await offer(result.draft, true); else { blocked = false; busy(form, false); refresh(); await flush(); } }
        else await flush();
      } catch (e) { showFailure(e); }
    };
  }
  function changedLocally(event) {
    if (event?.target && !['name', 'description', 'category', 'color', 'size', 'brand', 'photoUrl', 'photoMode'].includes(event.target.name) && event.target.id !== 'file') return;
    if (!active() || completed) return;
    generation++; ctx.setDirty(true); clearTimeout(timer);
    if (blocked || finalizing) return;
    status('Есть несохранённые изменения…'); timer = setTimeout(() => { flush().catch(showFailure); }, 1000);
  }
  async function flush() {
    clearTimeout(timer);
    if (blocked) throw new Error('Сначала выберите действие с черновиком.');
    if (finalizing || completed) return;
    if (running) { await running; if (active() && savedGeneration !== generation) return flush(); return; }
    if (stored && savedGeneration === generation) return;
    const mine = generation, content = snapshot(), file = draft.file;
    status('Сохраняется…');
    running = (async () => {
      stored = await request(`/drafts/${draftId}`, { method: 'PUT', body: { kind, targetId: id || null, version: stored?.version || null, baseVersion, content } });
      if (kind === 'items' && content.photoMode === 'file' && file && (file !== uploadedFile || !stored.photoUrl)) {
        const data = new FormData(); data.append('file', file);
        stored = await request(`/drafts/${draftId}/photo?version=${stored.version}`, { method: 'PUT', body: data }); uploadedFile = file;
      } else if (stored.photoUrl && content.removePhoto) {
        stored = await request(`/drafts/${draftId}/photo?version=${stored.version}`, { method: 'DELETE' }); uploadedFile = null;
      }
      savedGeneration = mine;
      if (!active()) return;
      if (mine === generation) { ctx.setDirty(false); status('Черновик сохранён на сервере.'); }
    })();
    try { await running; } finally { running = null; }
    if (active() && savedGeneration !== generation) return flush();
  }
  busy(form, true);
  // The panel itself must never be disabled by the initial resource lock.
  const ready = request(`/drafts/active?kind=${kind}${id ? `&targetId=${id}` : ''}`).then(async result => {
    if (!active()) return;
    if (result.draft) await offer(result.draft);
    else { blocked = false; busy(form, false); refresh(); status('Изменения автоматически сохраняются в черновик.'); }
  }).catch(showFailure);
  form.addEventListener('input', changedLocally);
  form.addEventListener('change', changedLocally);
  return {
    changed: changedLocally,
    lockUncertain() {
      if (finalizing && !completed && active()) {
        busy(form, true);
        panel.querySelectorAll('button').forEach(button => { button.disabled = false; });
      }
    },
    acceptBaseline(version) { baseVersion = version; blocked = false; if (stored) { generation++; savedGeneration = -1; } },
    async save() {
      try {
        await ready;
        if (finalizing) {
          const result = await request(`/drafts/${draftId}`);
          if (result.completedId) { completed = true; return result.completedId; }
          finalizing = false; stored = result;
        }
        await flush(); finalizing = true;
        const result = await request(`/drafts/${draftId}/save`, { method: 'POST', body: { version: stored.version } });
        completed = true; return result.id;
      } catch (e) {
        if (e.status && e.status < 500) finalizing = false;
        showFailure(e); throw e;
      }
    },
    dispose() { disposed = true; clearTimeout(timer); form.removeEventListener('input', changedLocally); form.removeEventListener('change', changedLocally); }
  };
}

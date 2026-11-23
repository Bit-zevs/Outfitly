import { activeWardrobeFilters, publicCollectionQuery, wardrobeQuery, wardrobeFilters, bindWardrobeFilters } from './wardrobe-filters.js';
import { bindWorkspaceActions } from './workspace.js';
import { avatar } from './components.js';
import { bindEngagement } from './engagement.js';

import { peoplePage, copyAddress, profilePage } from './profiles.js';
import { ApiError } from './api.js';
import { badge, categories, collage, crumbs, empty, escape, field, outfitSearch, itemCard, link, outfitCard, pagination, photo, title } from './components.js';
import { catalogQuery, changedFilter, safeReturn, shareAddress, profilePath, publicPath } from './router.js';
import { busy, confirmAction, modal, showError } from './ui.js';

export function home(ctx) {
  return { title: 'Ваш личный гардероб', html: `<div class="home-page"><section class="hero"><div class="hero-copy"><p class="home-intro">Ваш личный гардероб</p><h1>Любимые вещи.<br>Новые сочетания.</h1><p class="lead">Сохраняйте одежду, собирайте образы и делитесь ими — публично или только по ссылке.</p><div class="actions">${link(ctx.auth.user ? '/dashboard' : '/register', ctx.auth.user ? 'В мой гардероб' : 'Создать гардероб', 'btn')}${link('/discover', 'Открытая коллекция', 'btn outline')}</div></div><div class="hero-art"><img class="hero-image" width="1024" height="1536" fetchpriority="high" src="./src/assets/hero.webp" alt="Песочный тренч на деревянном стуле в светлой студии"></div></section></div>` };
}

export function authPage(ctx, register) {
  const { route } = ctx;
  const returnTo = route.query.get('return');
  return { title: register ? 'Регистрация' : 'Вход', html: `<section class="auth-grid"><div class="auth-art"><img width="800" height="1000" src="./src/assets/hero-rack.webp" alt="Гардероб в натуральных оттенках"><p>${register ? 'Ваш стиль.<br>Ваша коллекция.' : 'Хороший стиль<br>начинается здесь.'}</p></div><div class="auth-form"><h1>${register ? 'Создать аккаунт' : 'Войти'}</h1><p class="lead">${register ? 'Задайте ник и пароль после перехода по ссылке из письма.' : 'Войдите, чтобы продолжить собирать любимые сочетания.'}</p><form id="auth-form">${register ? field('Ваше имя', 'displayName', '', { required: true, autocomplete: 'name' }) : ''}${field('Email', 'email', route.query.get('email') || '', { type: 'email', required: true, autocomplete: 'email', max: 254 })}${register ? '' : `${field('Пароль', 'password', '', { type: 'password', required: true, autocomplete: 'current-password', max: 128 })}<label class="check-label"><input type="checkbox" id="show-password">Показать пароль</label>`}<p id="form-error" class="error" role="alert" tabindex="-1" hidden></p><button class="btn full" type="submit">${register ? 'Создать аккаунт' : 'Войти'}</button></form>${!register ? link('/forgot-password', 'Забыли пароль?') : ''}<p class="auth-foot">${register ? 'Уже есть аккаунт?' : 'Нет аккаунта?'} ${link(`/${register ? 'login' : 'register'}${returnTo ? `?return=${encodeURIComponent(returnTo)}` : ''}`, register ? 'Войти' : 'Зарегистрироваться')}</p></div></section>`, bind() {
    const form = document.querySelector('#auth-form');
    if (!register) document.querySelector('#show-password').onchange = ({ target }) => {
      form.elements.password.type = target.checked ? 'text' : 'password';
    };
    form.onsubmit = async event => {
      event.preventDefault();
      if (form.getAttribute('aria-busy') === 'true') return;
      const values = Object.fromEntries(new FormData(form));
      if (register && !values.displayName.trim()) { form.elements.displayName.setCustomValidity('Введите имя.'); form.elements.displayName.reportValidity(); return; }
      busy(form, true);
      document.querySelector('#form-error').hidden = true;
      try {
        if (register) {
          await ctx.auth.register(values.displayName, values.email);
          if (!ctx.isCurrent()) return;
          ctx.navigate(`/check-email?email=${encodeURIComponent(values.email)}${returnTo ? `&return=${encodeURIComponent(returnTo)}` : ''}`);
        } else {
          await ctx.auth.login(values.email, values.password);
          if (ctx.isCurrent()) ctx.navigate(safeReturn(returnTo));
        }
      } catch (error) {
        if (!ctx.isCurrent()) return;
        if (!register && error.status === 401) showError(document.querySelector('#form-error'), new ApiError(400), 'Email или пароль неверны. ');
        else showError(document.querySelector('#form-error'), error);
      } finally { busy(form, false); }
    };
    form.oninput = event => {
      if (!register) return;
      form.elements.displayName.setCustomValidity('');
    };
  } };
}

export async function dashboard(ctx) {
  const [items, outfits] = await Promise.all([ctx.get('/items?pageSize=6'), ctx.get('/wardrobe/statistics')]);
  return { title: 'Обзор', html: `${title('Ваше пространство', `Здравствуйте, ${ctx.auth.user.displayName}`, 'Всё, что нужно для вашего следующего образа.')}<div class="stats">${[[items.total, 'вещей в гардеробе', '/wardrobe'], [outfits.outfits, 'собранных образов', '/outfits'], [outfits.publicOutfits, 'опубликованных образов', '/outfits']].map(([number, label, path]) => `<a href="#${path}" class="stat"><span><strong>${number}</strong><span>${label}</span></span><span aria-hidden="true">↗</span></a>`).join('')}</div><h2 class="section-title">Быстрые действия</h2><div class="quick-grid"><a href="#/items/new" class="quick-card"><div><h3>Добавить вещь</h3></div><img width="800" height="1000" loading="lazy" src="./src/assets/shirt-large.webp" alt=""></a><a href="#/outfits/new" class="quick-card"><div><h3>Создать образ</h3></div><img width="800" height="1000" loading="lazy" src="./src/assets/outfit-business.webp" alt=""></a></div><div class="section-heading"><h2>Ваша коллекция</h2>${link('/wardrobe', 'Все вещи')}</div>${items.items.length ? `<div class="cards">${items.items.map(i => itemCard(i)).join('')}</div>` : empty('Гардероб начинается с одной вещи', 'Добавьте то, что любите носить.', link('/items/new', 'Добавить вещь', 'btn'))}` };
}

function bindOutfitSearch(ctx) {
  const form = document.querySelector('#catalog-search');
  form.onsubmit = event => { event.preventDefault(); ctx.navigate(changedFilter(ctx.route, 'search', form.elements.search.value.trim())); };
  form.elements.sort.onchange = () => ctx.navigate(changedFilter(ctx.route, 'sort', form.elements.sort.value));
}

function catalogResults(route, page, results) {
  return `<div class="catalog-update" data-catalog-update role="status"></div><div data-catalog-results>${results}${pagination(route, page)}</div>`;
}

async function catalogOptions(ctx, path) {
  try { return await ctx.get(path); }
  catch { return null; }
}

export async function wardrobe(ctx) {
  const { route } = ctx;
  const optionsPath = '/items/filter-options';
  const [page, options] = await Promise.all([ctx.get(`/items?${wardrobeQuery(route.query)}`), catalogOptions(ctx, optionsPath)]);
  const results = page.items.length ? `<div class="cards">${page.items.map(item => itemCard(item)).join('')}</div>`
    : activeWardrobeFilters(route.query).length
      ? empty('Ничего не найдено', 'Измените условия поиска или нажмите «Сбросить фильтры» выше.')
      : empty('Гардероб начинается с одной вещи', 'Добавьте любимую одежду, чтобы собирать из неё образы.', link('/items/new', 'Добавить вещь', 'btn'));
  return {
    title: 'Мой гардероб',
    html: `${title('Личная коллекция', 'Мой гардероб', 'Любимые вещи — всегда под рукой.', link('/items/new', '＋ Добавить вещь', 'btn'))}${wardrobeFilters(route, options, page.total)}${catalogResults(route, page, results)}`,
    bind: () => bindWardrobeFilters(ctx, optionsPath)
  };
}

export async function outfitsPage(ctx) {
  const { route } = ctx;
  const page = await ctx.get(`/outfits?${catalogQuery(route.query)}`);
  const results = page.items.length ? `<div class="outfit-grid">${page.items.map(outfit => outfitCard(outfit)).join('')}</div>`
    : route.query.get('search')
      ? empty('Образы не найдены', 'Измените название или сбросьте поиск.', link('/outfits', 'Сбросить поиск', 'btn outline'))
      : empty('Первый образ ещё впереди', 'Соберите комплект из своего гардероба.', link('/outfits/new', 'Создать образ', 'btn'));
  return {
    title: 'Мои образы',
    html: `${title('Искусство сочетать', 'Мои образы', 'Комплекты для ваших планов и настроения.', link('/outfits/new', '＋ Создать образ', 'btn'))}${outfitSearch(route, page)}${catalogResults(route, page, results)}`,
    bind: () => bindOutfitSearch(ctx)
  };
}

export async function discover(ctx) {
  const { route } = ctx;
  if (route.query.get('tab') === 'people') return peoplePage(ctx);
  const itemsTab = route.query.get('tab') === 'items';
  const endpoint = itemsTab ? '/wardrobe' : '/wardrobe/outfits';
  const optionsPath = `${endpoint}/filter-options`;
  const [data, options] = await Promise.all([
    ctx.get(`${endpoint}?${publicCollectionQuery(route.query)}`),
    catalogOptions(ctx, optionsPath)
  ]);
  const tabs = new URLSearchParams(route.query);
  tabs.delete('tab');
  tabs.delete('page');
  const outfitsPath = tabs.size ? `/discover?${tabs}` : '/discover';
  tabs.set('tab', 'items');
  const results = data.items.length
    ? itemsTab ? `<div class="cards">${data.items.map(item => itemCard(item, '/public/items')).join('')}</div>`
      : `<div class="outfit-grid">${data.items.map(outfit => outfitCard(outfit, '/public/outfits')).join('')}</div>`
    : activeWardrobeFilters(route.query, false).length
      ? empty(itemsTab ? 'Вещи не найдены' : 'Образы не найдены', 'Измените запрос или сбросьте фильтры.')
      : empty(itemsTab ? 'Публичных вещей пока нет' : 'Публичных образов пока нет', 'Здесь появятся публикации сообщества.');
  return {
    title: 'Открытая коллекция',
    html: `<section class="discover-intro"><div><h1>Открытая коллекция</h1><p class="lead">Вещи и образы сообщества Outfitly.</p></div><img width="800" height="1000" src="./src/assets/hero-rack.webp" alt=""></section>
      <div class="public-head"><nav class="tabs" aria-label="Тип коллекции">${link(outfitsPath, 'Образы', !itemsTab ? 'selected' : '')}${link(`/discover?${tabs}`, 'Вещи', itemsTab ? 'selected' : '')}</nav></div>
      ${wardrobeFilters(route, options, data.total)}${catalogResults(route, data, results)}`,
    bind: () => bindWardrobeFilters(ctx, optionsPath)
  };
}

async function publicResource(ctx, kind, id) {
  if (kind === 'outfits') {
    return ctx.get(`/wardrobe/outfits/${encodeURIComponent(id)}`);
  } else {
    for (let page = 1; ; page++) {
      const data = await ctx.get(`/wardrobe?page=${page}&pageSize=100`);
      const found = data.items.find(i => i.id === id);
      if (found) return found;
      if (!data.items.length || page * data.pageSize >= data.total) break;
    }
  }
  throw new ApiError(404);
}

export async function detail(ctx, kind, id, access = 'owner', username = null, embedded = null) {
  const item = kind === 'items';
  const owner = access === 'owner';
  const resource = embedded ? embedded.resource : username ? await ctx.get(`/users/${encodeURIComponent(username)}/${kind}/${encodeURIComponent(id)}`) : access === 'shared' ? await ctx.get(`/shared/${kind}/${encodeURIComponent(id)}`) : owner ? await ctx.get(`/${kind}/${id}`) : await publicResource(ctx, kind, id);
  const back = embedded ? embedded.back : owner ? (item ? '/wardrobe' : '/outfits') : username ? `${profilePath(username)}${item ? '' : '?tab=outfits'}` : '/discover';
  const backText = embedded ? embedded.name : owner ? (item ? 'Мой гардероб' : 'Мои образы') : 'Открытая коллекция';
  return { title: resource.name, html: `${crumbs(back, backText, resource.name)}<section class="detail ${item ? 'item-detail' : ''}"><header class="detail-heading"><h1>${escape(resource.name)}</h1><div class="detail-meta"><p class="eyebrow">${item ? `Вещь / ${escape(categories[resource.category])}` : 'Образ'}${access === 'shared' ? ' / По ссылке' : ''}</p>${owner ? badge(resource.isPublic) : `<span class="badge">${embedded ? 'Вещь из образа' : access === 'shared' ? 'Просмотр по ссылке' : 'Открытая коллекция'}</span>`}</div>${resource.ownerUsername ? `<p class="author-line">${avatar({ username: resource.ownerUsername, avatarUrl: resource.ownerAvatarUrl })}${link(profilePath(resource.ownerUsername), `@${resource.ownerUsername}`)}</p>` : ''}</header><div class="detail-media">${item ? photo(resource, 'detail-photo') : `<div class="detail-collage">${collage(resource)}</div><h2 class="section-title">Вещи в образе</h2><div class="pieces">${resource.items.map(i => owner ? `<a href="#/items/${escape(i.id)}">${photo(i, '', true)}<span>${escape(i.name)}</span></a>` : `<a href="#/${access === 'shared' ? 'shared' : 'public'}/outfits/${escape(id)}/items/${escape(i.id)}${username ? `?username=${encodeURIComponent(username)}` : ''}">${photo(i, '', true)}<span>${escape(i.name)}</span></a>`).join('')}</div>`}</div><div class="detail-description"><dl class="properties">${(item ? [['Категория', categories[resource.category]], ['Бренд', resource.brand], ['Размер', resource.size], ['Цвет', resource.color]] : [['Состав', `${resource.items.length} вещей`]]).map(([k, v]) => `<div><dt>${escape(k)}</dt><dd>${escape(v || 'Не указан')}</dd></div>`).join('')}</dl><h2 class="eyebrow">${item ? 'О вещи' : 'Об образе'}</h2><p class="description">${escape(resource.description || 'Описание пока не добавлено.')}</p>${owner ? `<div class="actions">${link(`/${kind}/${id}/edit`, item ? 'Редактировать' : 'Редактировать образ', 'btn')}<button class="btn outline danger" id="delete-resource">Удалить</button></div><div class="linebox access-panel"><h2>Доступ и ссылки</h2><label class="publication"><span><strong>Публичный доступ</strong><small>Все смогут найти ${item ? 'вещь' : 'образ'} в открытой коллекции.</small></span><input type="checkbox" role="switch" id="publication" ${resource.isPublic ? 'checked' : ''} ${!item && !resource.items.length ? 'disabled' : ''}></label>${!item && !resource.items.length ? '<p class="muted">Добавьте хотя бы одну вещь для публикации и создания ссылки.</p>' : ''}<p class="error" role="alert" tabindex="-1" id="detail-error" hidden></p><div class="access-links"><h3>Доступ по ссылке</h3><p>Любой, у кого есть активная ссылка, сможет открыть ${item ? 'вещь' : 'образ'}, даже если публичный доступ выключен. Ссылку можно отозвать.</p><button class="btn outline" id="share" ${!item && !resource.items.length ? 'disabled' : ''}>Управлять ссылками</button></div></div>` : ''}</div></section>${(!embedded || access === 'public') ? `<section class="linebox social-panel" aria-label="Действия и обсуждение"><div class="social-toolbar"><div class="actions" id="workspace-actions" aria-label="Сохранение и копирование"></div>${!embedded && resource.isPublic && (resource.ownerUsername || owner && ctx.auth.user.username) ? '<button class="btn outline small copy-link-button" id="copy-public-address">Копировать публичную ссылку</button>' : ''} </div><div id="engagement" aria-label="Реакции и обсуждение"></div></section>` : ''}`, bind() {
    bindWorkspaceActions(ctx, kind, resource, access === 'shared' ? id : null, embedded?.publicOutfitId);
    bindEngagement(ctx, kind, resource, access === 'shared' ? id : null, embedded?.publicOutfitId);
    const copyPublic = document.querySelector('#copy-public-address');
    if (copyPublic) copyPublic.onclick = () => copyAddress(`${location.origin}/#${publicPath(resource.ownerUsername || ctx.auth.user.username, kind, resource.id)}`, ctx);
    if (!owner) return;
    document.querySelector('#delete-resource').onclick = () => confirmAction(`Удалить ${item ? 'вещь' : 'образ'}?`, item ? 'Вещь исчезнет из всех образов, ссылки на неё перестанут работать. Если образ останется пустым, его публикация и ссылки будут отключены. Это действие нельзя отменить.' : 'Образ и ссылки на него станут недоступны. Вещи останутся в вашем гардеробе. Это действие нельзя отменить.', async () => {
      await ctx.api.request(`/${kind}/${id}`, { method: 'DELETE' });
      if (ctx.isCurrent()) { ctx.toast(item ? 'Вещь удалена' : 'Образ удалён'); ctx.navigate(back); }
    });
    document.querySelector('#publication').onchange = async event => {
      const toggle = event.currentTarget;
      toggle.disabled = true;
      try { await ctx.api.request(`/${kind}/${id}/publication`, { method: 'PATCH', body: { isPublic: toggle.checked } }); if (ctx.isCurrent()) ctx.refresh(); }
      catch (error) { if (ctx.isCurrent()) { toggle.checked = resource.isPublic; showError(document.querySelector('#detail-error'), error); } }
      finally { toggle.disabled = false; }
    };
    document.querySelector('#share').onclick = () => shareDialog(ctx, kind, id);
  } };
}

function shareDialog(ctx, kind, id) {
  const dialog = modal('Ссылки для просмотра', '<div id="share-list"></div><p id="share-status" role="status">Загружаем ссылки…</p><p class="error" role="alert" tabindex="-1" hidden></p><button type="button" class="btn outline" id="retry-links" hidden>Повторить загрузку</button><button type="button" class="btn" id="create-link" disabled>Создать ссылку</button>');
  const list = dialog.querySelector('#share-list');
  const status = dialog.querySelector('#share-status');
  const errorBox = dialog.querySelector('.error');
  const create = dialog.querySelector('#create-link');
  const retry = dialog.querySelector('#retry-links');
  const active = () => dialog.open && list.isConnected && ctx.isCurrent();
  let pending = false;

  function render(links) {
    list.innerHTML = links.length ? links.map(l => `<div class="share-row"><label><span class="sr-only">Ссылка для просмотра</span><input readonly value="${escape(shareAddress(location.origin, kind, l.token))}"></label><div class="actions"><button type="button" class="btn outline small" data-copy>Копировать</button><button type="button" class="btn outline danger small" data-revoke>Отозвать</button></div></div>`).join('') : '<p class="muted">Активных ссылок пока нет.</p>';
    Array.from(list.children).forEach((row, index) => {
      const link = links[index];
      if (!link) return;
      const input = row.querySelector('input');
      row.querySelector('[data-copy]').onclick = async () => {
        try { await navigator.clipboard.writeText(input.value); if (active()) status.textContent = 'Ссылка скопирована.'; }
        catch { if (active()) { input.focus(); input.select(); status.textContent = 'Скопируйте выделенную ссылку вручную.'; } }
      };
      const revoke = row.querySelector('[data-revoke]');
      revoke.onclick = () => {
        if (pending) return;
        list.querySelector('[data-cancel-revoke]')?.click();
        const confirmation = document.createElement('div');
        confirmation.className = 'share-confirmation';
        confirmation.innerHTML = '<p>После отзыва по этой ссылке нельзя будет открыть публикацию.</p><div class="actions"><button type="button" class="btn outline small" data-cancel-revoke>Отмена</button><button type="button" class="btn danger small" data-confirm-revoke>Отозвать ссылку</button></div>';
        row.append(confirmation);
        revoke.hidden = true;
        const cancel = confirmation.querySelector('[data-cancel-revoke]');
        const confirm = confirmation.querySelector('[data-confirm-revoke]');
        cancel.onclick = () => { confirmation.remove(); revoke.hidden = false; revoke.focus(); };
        confirm.onclick = () => update('Ссылка отозвана.', () => ctx.api.request(`/share-links/${link.id}`, { method: 'DELETE' }), index, confirm);
        cancel.focus();
      };
    });
  }

  async function update(message, mutation, index, trigger) {
    if (pending || !active()) return;
    pending = true;
    const buttons = dialog.querySelectorAll('button:not([data-close])');
    buttons.forEach(button => { button.disabled = true; });
    errorBox.hidden = retry.hidden = true;
    let loaded = false;
    let changed = false;
    try {
      if (mutation) { await mutation(); changed = true; }
      if (!active()) return;
      status.textContent = 'Загружаем ссылки…';
      const links = await ctx.api.request(`/${kind}/${id}/share-links`);
      if (!active()) return;
      render(links);
      loaded = true;
      status.textContent = message || `Активных ссылок: ${links.length}`;
    } catch (error) {
      if (!active()) return;
      if (!mutation || changed) {
        list.replaceChildren();
        retry.hidden = false;
        status.textContent = changed ? message : '';
      } else status.textContent = '';
      showError(errorBox, error);
    } finally {
      pending = false;
      if (active()) {
        buttons.forEach(button => { button.disabled = false; });
        create.disabled = !retry.hidden;
        if (loaded && trigger) {
          const remaining = list.querySelectorAll('[data-revoke]');
          (remaining.length && index !== undefined ? remaining[Math.min(index, remaining.length - 1)] : create).focus();
        }
      }
    }
  }

  create.onclick = () => update('Ссылка создана.', () => ctx.api.request(`/${kind}/${id}/share-links`, { method: 'POST' }), undefined, create);
  retry.onclick = () => update('', undefined, undefined, retry);
  update('');
}

export async function account(ctx) {
  const page = await profilePage(ctx, ctx.auth.user.username);
  return { ...page, title: 'Мой профиль', html: page.html };
}

export function notFound(shared = false) {
  return { title: shared ? 'Ссылка недоступна' : 'Страница не найдена', html: empty(shared ? 'Эта ссылка больше недоступна' : 'Страница не найдена', shared ? 'Возможно, владелец отозвал ссылку или удалил вещь либо образ.' : 'Возможно, страница была удалена или адрес указан неверно.', link('/discover', 'В открытую коллекцию', 'btn')) };
}

export async function outfitItemDetail(ctx, access, outfitId, itemId) {
  const username = ctx.route.query.get('username');
  const outfit = access === 'shared' ? await ctx.get(`/shared/outfits/${encodeURIComponent(outfitId)}`) : username ? await ctx.get(`/users/${encodeURIComponent(username)}/outfits/${encodeURIComponent(outfitId)}`) : await publicResource(ctx, 'outfits', outfitId);
  const resource = outfit.items.find(item => item.id === itemId);
  if (!resource) throw new ApiError(404);
  const back = access === 'shared' ? `/shared/outfits/${encodeURIComponent(outfitId)}` : username ? publicPath(username, 'outfits', outfitId) : `/public/outfits/${encodeURIComponent(outfitId)}`;
  return detail(ctx, 'items', itemId, access, null, { resource, back, name: outfit.name, publicOutfitId: access === 'public' ? outfitId : null });
}

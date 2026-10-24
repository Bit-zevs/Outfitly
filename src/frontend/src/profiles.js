import { empty, escape, itemCard, link, outfitCard, pagination, title } from './components.js';
import { changedFilter, profilePath } from './router.js';

export async function peoplePage(ctx) {
  const query = new URLSearchParams({ search: ctx.route.query.get('search') || '',
    page: String(Math.max(1, Number.parseInt(ctx.route.query.get('page'), 10) || 1)), pageSize: '20' });
  const page = await ctx.get(`/users?${query}`);
  return { title: 'Люди', html: `${title('Сообщество Outfitly', 'Люди', 'Найдите человека по @username и посмотрите его открытый гардероб.')}
    <nav class="tabs" aria-label="Тип коллекции">${link('/discover', 'Образы')}${link('/discover?tab=items', 'Вещи')}${link('/discover?tab=people', 'Люди', 'selected')}</nav>
    <form id="people-search" class="search-row" role="search"><label class="search-box"><span class="sr-only">Поиск по @username</span><input type="search" name="search" maxlength="31" placeholder="@username" value="${escape(query.get('search'))}"><button class="icon-button" aria-label="Найти">⌕</button></label></form>
    ${page.items.length ? `<div class="cards">${page.items.map(user => `<article class="linebox">${link(profilePath(user.username), user.displayName)}<p>${link(profilePath(user.username), `@${user.username}`)}</p></article>`).join('')}</div>`
      : empty(query.get('search') ? 'Никого не найдено' : 'Кого вы ищете?', query.get('search') ? 'Попробуйте другое начало ника.' : 'Введите ник или его начало, например @anna.')}${pagination(ctx.route, page)}`,
    bind() {
      const form = document.querySelector('#people-search');
      form.onsubmit = event => { event.preventDefault(); ctx.navigate(changedFilter(ctx.route, 'search', form.elements.search.value.trim())); };
    } };
}

export async function profilePage(ctx, username) {
  const user = await ctx.get(`/users/${encodeURIComponent(username)}`);
  const kind = ctx.route.query.get('tab') === 'outfits' ? 'outfits' : 'items';
  const query = new URLSearchParams({ page: String(Math.max(1, Number.parseInt(ctx.route.query.get('page'), 10) || 1)), pageSize: '20' });
  const page = await ctx.get(`/users/${encodeURIComponent(user.username)}/${kind}?${query}`);
  const base = profilePath(user.username);
  return { title: `@${user.username}`, html: `${title(`@${user.username}`, user.displayName, 'Опубликованные вещи и образы')}
    <nav class="tabs" aria-label="Коллекция пользователя">${link(base, 'Вещи', kind === 'items' ? 'selected' : '')}${link(`${base}?tab=outfits`, 'Образы', kind === 'outfits' ? 'selected' : '')}</nav>
    ${page.items.length ? `<div class="${kind === 'items' ? 'cards' : 'outfit-grid'}">${page.items.map(resource => kind === 'items' ? itemCard(resource, `${base}/items`) : outfitCard(resource, `${base}/outfits`)).join('')}</div>`
      : empty('Публикаций пока нет', 'Здесь появится открытая коллекция пользователя.')}${pagination(ctx.route, page)}` };
}

export async function copyAddress(address, ctx) {
  try { await navigator.clipboard.writeText(address); ctx.toast('Ссылка скопирована'); }
  catch { ctx.toast(`Скопируйте ссылку: ${address}`); }
}

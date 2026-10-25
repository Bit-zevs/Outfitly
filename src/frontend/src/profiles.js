import { showError } from './ui.js';
import { avatar, empty, escape, itemCard, link, outfitCard, pagination, title } from './components.js';
import { changedFilter, profilePath } from './router.js';

export async function peoplePage(ctx) {
  const query = new URLSearchParams({ search: ctx.route.query.get('search') || '',
    page: String(Math.max(1, Number.parseInt(ctx.route.query.get('page'), 10) || 1)), pageSize: '20' });
  const page = await ctx.get(`/users?${query}`);
  return { title: 'Люди', html: `${title('Сообщество Outfitly', 'Люди', 'Найдите человека по @username и посмотрите его открытый гардероб.')}
    <nav class="tabs" aria-label="Тип коллекции">${link('/discover', 'Образы')}${link('/discover?tab=items', 'Вещи')}${link('/discover?tab=people', 'Люди', 'selected')}</nav>
    <form id="people-search" class="search-row" role="search"><label class="search-box"><span class="sr-only">Поиск по @username</span><input type="search" name="search" maxlength="31" placeholder="@username" value="${escape(query.get('search'))}"><button class="icon-button" aria-label="Найти">⌕</button></label></form>
    ${page.items.length ? `<div class="cards">${page.items.map(user => `<article class="linebox">${avatar(user)}${link(profilePath(user.username), user.displayName)}<p>${link(profilePath(user.username), `@${user.username}`)}</p></article>`).join('')}</div>`
      : empty(query.get('search') ? 'Никого не найдено' : 'Кого вы ищете?', query.get('search') ? 'Попробуйте другое начало ника.' : 'Введите ник или его начало, например @anna.')}${pagination(ctx.route, page)}`,
    bind() {
      const form = document.querySelector('#people-search');
      form.onsubmit = event => { event.preventDefault(); ctx.navigate(changedFilter(ctx.route, 'search', form.elements.search.value.trim())); };
    } };
}

export async function profilePage(ctx, username) {
  let user = await ctx.get(`/users/${encodeURIComponent(username)}`);
  const requested = ctx.route.query.get('tab');
  const kind = ['outfits', 'followers', 'following'].includes(requested) ? requested : 'items';
  const people = kind === 'followers' || kind === 'following';
  const query = new URLSearchParams({ page: String(Math.max(1, Number.parseInt(ctx.route.query.get('page'), 10) || 1)), ...(!people ? { pageSize: '20' } : {}) });
  const page = await ctx.get(`/users/${encodeURIComponent(user.username)}/${kind}?${query}`);
  const base = profilePath(user.username);
  function heading() {
    return `${title(`@${user.username}`, user.displayName, 'Опубликованные вещи и образы', avatar(user))}<p class="profile-bio">${escape(user.bio || '')}</p><div class="actions">${link(`${base}?tab=followers`, `Подписчики: ${user.followersCount || 0}`, 'btn outline small')}${link(`${base}?tab=following`, `Подписки: ${user.followingCount || 0}`, 'btn outline small')}${user.canFollow ? `<button class="btn" id="follow-user" aria-pressed="${Boolean(user.isFollowing)}">${user.isFollowing ? 'Отписаться' : 'Подписаться'}</button>` : !ctx.auth?.user ? link('/login', 'Войти, чтобы подписаться', 'btn outline') : ''}</div>`;
  }
  return { title: `@${user.username}`, html: `<section id="public-profile-header">${heading()}</section><p id="follow-error" class="error" role="alert" tabindex="-1" hidden></p>
    <nav class="tabs" aria-label="Коллекция пользователя">${[['items', 'Вещи'], ['outfits', 'Образы'], ['followers', 'Подписчики'], ['following', 'Подписки']].map(([tab, label]) => link(tab === 'items' ? base : `${base}?tab=${tab}`, label, tab === kind ? 'selected' : '')).join('')}</nav>
    ${page.items.length ? `<div class="${kind === 'outfits' ? 'outfit-grid' : 'cards'}">${page.items.map(resource => people ? `<article class="linebox">${avatar(resource)}${link(profilePath(resource.username), resource.displayName)}<p>${link(profilePath(resource.username), `@${resource.username}`)}</p></article>` : kind === 'items' ? itemCard(resource, `${base}/items`) : outfitCard(resource, `${base}/outfits`)).join('')}</div>`
      : empty(people ? 'Список пока пуст' : 'Публикаций пока нет', people ? 'Здесь появятся пользователи.' : 'Здесь появится открытая коллекция пользователя.')}${pagination(ctx.route, page)}`, bind() {
      let pending = false;
      const root = document.querySelector('#public-profile-header');
      function bindFollow() {
        const button = root.querySelector('#follow-user'); if (!button) return;
        button.onclick = async () => {
          if (pending) return;
          pending = true; button.disabled = true;
          const error = document.querySelector('#follow-error'); error.hidden = true;
          try {
            await ctx.api.request(`/users/${encodeURIComponent(user.username)}/follow`, { method: user.isFollowing ? 'DELETE' : 'PUT' });
            const updated = await ctx.get(`/users/${encodeURIComponent(user.username)}`);
            if (!ctx.isCurrent() || !root.isConnected) return;
            user = updated; root.innerHTML = heading(); bindFollow();
          } catch (e) { if (ctx.isCurrent() && root.isConnected) showError(error, e); }
          finally { pending = false; button.disabled = false; }
        };
      }
      bindFollow();
    } };
}
export async function copyAddress(address, ctx) {
  try { await navigator.clipboard.writeText(address); ctx.toast('Ссылка скопирована'); }
  catch { ctx.toast(`Скопируйте ссылку: ${address}`); }
}

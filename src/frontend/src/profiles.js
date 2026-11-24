import { showError } from './ui.js';
import { avatar, empty, escape, itemCard, link, outfitCard, pagination, title } from './components.js';
import { changedFilter, profilePath } from './router.js';

function personCard(user) {
  return `<article class="linebox person-card"><a href="#${escape(profilePath(user.username))}">${avatar(user)}<span><strong>${escape(user.displayName)}</strong><small>@${escape(user.username)}</small></span></a></article>`;
}

export async function peoplePage(ctx) {
  const user = ctx.auth?.user;
  const view = !user || ctx.route.query.get('view') === 'search' || ctx.route.query.has('search') ? 'search' : ctx.route.query.get('view') === 'followers' ? 'followers' : 'following';
  const searchForm = `<form id="people-search" class="search-row" role="search"><div class="search-box"><label><span class="sr-only">Поиск по @username</span><input type="search" name="search" maxlength="31" autocomplete="off" spellcheck="false" autocapitalize="none" placeholder="Найти людей по @username…" value="${escape(ctx.route.query.get('search'))}"></label><button type="submit" class="icon-button" aria-label="Найти">⌕</button></div></form>`;
  const navigation = `<div class="people-navigation">${user ? `<nav class="tabs" aria-label="Ваши связи">${link('/people', 'Подписки', view === 'following' ? 'selected' : '')}${link('/people?view=followers', 'Подписчики', view === 'followers' ? 'selected' : '')}</nav>` : ''}${searchForm}</div>`;
  function bindSearch() {
    const form = document.querySelector('#people-search');
    form.onsubmit = event => {
      event.preventDefault();
      const search = form.elements.search.value.trim();
      ctx.navigate(search ? changedFilter({ ...ctx.route, path: '/people', query: new URLSearchParams({ view: 'search' }) }, 'search', search) : user ? '/people' : '/people?view=search');
    };
  }
  if (view !== 'search') {
    const number = Math.max(1, Number.parseInt(ctx.route.query.get('page'), 10) || 1);
    const page = await ctx.get(`/users/${encodeURIComponent(user.username)}/${view}?page=${number}`);
    return { title: 'Люди', html: `${title('', 'Люди')}${navigation}${page.items.length ? `<div class="cards people-cards">${page.items.map(personCard).join('')}</div>` : empty(view === 'following' ? 'Вы пока ни на кого не подписаны' : 'Подписчиков пока нет', view === 'following' ? 'Найдите людей, чьи образы вам интересны.' : 'Здесь появятся люди, которые подписались на ваш профиль.')}${pagination(ctx.route, page)}`, bind: bindSearch };
  }

  const query = new URLSearchParams({ search: ctx.route.query.get('search') || '',
    page: String(Math.max(1, Number.parseInt(ctx.route.query.get('page'), 10) || 1)), pageSize: '20' });
  const page = await ctx.get(`/users?${query}`);
  return { title: 'Люди', html: `${title('', 'Люди')}${navigation}

    ${page.items.length ? `<div class="cards">${page.items.map(personCard).join('')}</div>`
      : empty(query.get('search') ? 'Никого не найдено' : 'Кого вы ищете?', query.get('search') ? 'Попробуйте другое начало ника.' : 'Введите ник или его начало, например @anna.')}${pagination(ctx.route, page)}`,
    bind: bindSearch };
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
    return `<div class="profile-heading">${avatar(user)}<div class="profile-heading-copy"><h1>${escape(user.displayName)}</h1><p class="profile-handle">@${escape(user.username)}</p></div></div>${user.bio ? `<p class="profile-bio">${escape(user.bio)}</p>` : ''}<div class="actions">${user.canFollow ? `<button class="btn" id="follow-user" aria-pressed="${Boolean(user.isFollowing)}">${user.isFollowing ? 'Отписаться' : 'Подписаться'}</button>` : !ctx.auth?.user ? link('/login', 'Войти, чтобы подписаться', 'btn outline') : ''}</div>`;
  }
  return { title: `@${user.username}`, html: `<section id="public-profile-header">${heading()}</section><p id="follow-status" class="sr-only" role="status"></p><p id="follow-error" class="error" role="alert" tabindex="-1" hidden></p>
    <nav class="tabs" aria-label="Коллекция пользователя">${[['items', 'Вещи'], ['outfits', 'Образы'], ['followers', `Подписчики: ${user.followersCount || 0}`], ['following', `Подписки: ${user.followingCount || 0}`]].map(([tab, label]) => link(tab === 'items' ? base : `${base}?tab=${tab}`, label, tab === kind ? 'selected' : '')).join('')}</nav>
    ${page.items.length ? `<h2 class="sr-only">${{ items: 'Вещи', outfits: 'Образы', followers: 'Подписчики', following: 'Подписки' }[kind]}</h2><div class="${kind === 'outfits' ? 'outfit-grid' : 'cards'}">${page.items.map(resource => people ? personCard(resource) : kind === 'items' ? itemCard(resource, `${base}/items`) : outfitCard(resource, `${base}/outfits`)).join('')}</div>`
      : empty(people ? 'Список пока пуст' : 'Публикаций пока нет', people ? 'Здесь появятся пользователи.' : 'Здесь появится открытая коллекция пользователя.')}${pagination(ctx.route, page)}`, bind() {
      let pending = false;
      const root = document.querySelector('#public-profile-header');
      function bindFollow() {
        const button = root.querySelector('#follow-user'); if (!button) return;
        button.onclick = async () => {
          if (pending) return;
          pending = true; button.setAttribute('aria-disabled', 'true');
          const error = document.querySelector('#follow-error'); error.hidden = true;
          try {
            await ctx.api.request(`/users/${encodeURIComponent(user.username)}/follow`, { method: user.isFollowing ? 'DELETE' : 'PUT' });
            const updated = await ctx.get(`/users/${encodeURIComponent(user.username)}`);
            if (!ctx.isCurrent() || !root.isConnected) return;
            user = updated;
            button.textContent = user.isFollowing ? 'Отписаться' : 'Подписаться';
            button.setAttribute('aria-pressed', String(Boolean(user.isFollowing)));
            document.querySelector(`a[href="#${base}?tab=followers"]`).textContent = `Подписчики: ${user.followersCount || 0}`;
            document.querySelector(`a[href="#${base}?tab=following"]`).textContent = `Подписки: ${user.followingCount || 0}`;
            document.querySelector('#follow-status').textContent = user.isFollowing ? 'Подписка оформлена' : 'Подписка отменена';
          } catch (e) { if (ctx.isCurrent() && root.isConnected) showError(error, e); }
          finally { pending = false; button.removeAttribute('aria-disabled'); }
        };
      }
      bindFollow();
    } };
}
export async function copyAddress(address, ctx) {
  try { await navigator.clipboard.writeText(address); ctx.toast('Ссылка скопирована'); }
  catch { ctx.toast(`Скопируйте ссылку: ${address}`); }
}

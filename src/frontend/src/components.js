import { publicPath } from './router.js';
export const categories = { Top: 'Верх', Bottom: 'Низ', Dress: 'Платья', Outerwear: 'Верхняя одежда', Footwear: 'Обувь', Accessory: 'Аксессуары' };
export const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
export const link = (path, text, style = '') => `<a class="${style}" ${style.split(" ").includes("selected") ? 'aria-current="page"' : ''} href="#${escape(path)}">${escape(text)}</a>`;
export const title = (eyebrow, heading, description = '', action = '') => `<div class="page-head"><div><h1>${escape(heading)}</h1>${description ? `<p class="lead">${escape(description)}</p>` : ''}</div>${action}</div>`;
export const empty = (heading, text, action = '') => `<div class="empty"><span class="empty-icon" aria-hidden="true">◇</span><h2>${escape(heading)}</h2><p class="lead">${escape(text)}</p>${action}</div>`;
export const crumbs = (path, text, current) => `<nav class="breadcrumbs" aria-label="Хлебные крошки">${link(path, text)}<span aria-hidden="true">/</span><span>${escape(current)}</span></nav>`;
export const badge = value => `<span class="badge ${value ? 'public' : ''}">${value ? 'Публичный доступ' : 'Личное'}</span>`;
export function photo(item, className = '', decorative = false) {
  const url = item.photoUrl;
  const safe = url && (/^https?:\/\//i.test(url) || /^\/api\//.test(url));
  return `<div class="photo ${className}">${safe ? `<img src="${escape(url)}" alt="${decorative ? '' : escape(item.name)}" width="800" height="1000" ${className === "detail-photo" ? 'loading="eager" fetchpriority="high"' : 'loading="lazy"'} referrerpolicy="no-referrer">` : `${photoPlaceholder(decorative)}`}</div>`;
}
export function itemCard(item, base = '/items') {
  const personal = base === '/items';
  if (base === '/public/items' && item.ownerUsername) base = publicPath(item.ownerUsername, 'items', '').replace(/\/$/, '');
  return `<article class="item-card"><a href="#${base}/${escape(item.id)}">${photo(item, '', true)}<div class="card-heading"><h3>${escape(item.name)}</h3>${personal ? badge(item.isPublic) : ''}</div><p>${escape(categories[item.category] || item.category)}${item.brand ? ` · ${escape(item.brand)}` : ''}</p></a></article>`;
}
export function collage(outfit) {
  return `<div class="collage ${outfit.items.length === 1 ? 'single' : ''}">${outfit.items.length ? outfit.items.slice(0, 4).map(item => photo(item, '', true)).join('') : photoPlaceholder(true)}</div>`;
}
export function outfitCard(outfit, base = '/outfits') {
  const personal = base === '/outfits';
  if (base === '/public/outfits' && outfit.ownerUsername) base = publicPath(outfit.ownerUsername, 'outfits', '').replace(/\/$/, '');
  return `<article class="outfit-card"><a href="#${base}/${escape(outfit.id)}">${collage(outfit)}<div class="card-heading"><h3>${escape(outfit.name)}</h3>${personal ? badge(outfit.isPublic) : ''}</div><p>Вещей в образе: ${outfit.items.length}</p></a></article>`;
}
export function field(label, name, value = '', { type = 'text', required = false, hint = '', autocomplete = '', min = '', max = '' } = {}) {
  return `<div class="field"><label for="${name}">${escape(label)}${required ? ' <span aria-hidden="true">*</span>' : ''}</label><input id="${name}" name="${name}" type="${type}" ${type === 'email' || name === 'username' ? 'spellcheck="false" autocapitalize="none"' : ''} value="${escape(value)}" ${required ? 'required' : ''} ${autocomplete ? `autocomplete="${autocomplete}"` : ''} ${min ? `minlength="${min}"` : ''} ${max ? `maxlength="${max}"` : ''} ${hint ? `aria-describedby="${name}-hint"` : ''}>${hint ? `<small id="${name}-hint">${escape(hint)}</small>` : ''}</div>`;
}
export function filters(route, page, outfits = false, publicCatalog = false) {
  const query = route.query;
  return `${outfits ? '' : `<div class="filters" aria-label="Категории">${[['', 'Все'], ...Object.entries(categories)].map(([key, text]) => `<button type="button" class="chip ${(query.get('category') || '') === key ? 'selected' : ''}" data-filter-focus="category-${key}" data-category="${key}" aria-pressed="${(query.get('category') || '') === key}">${text}</button>`).join('')}</div>`}<form class="search-row" id="catalog-search" role="search"><div class="search-box"><label><span class="sr-only">Поиск по названию</span><input type="search" name="search" autocomplete="off" data-filter-focus="search" placeholder="${outfits ? 'Поиск по названию образа…' : 'Поиск по названию вещи…'}" value="${escape(query.get('search'))}"></label><button class="icon-button" type="submit" data-filter-focus="submit" aria-label="Найти">⌕</button></div><label class="sort-label">Порядок <select name="sort" id="sort" data-filter-focus="sort">${publicCatalog ? [['newest', 'Сначала новые'], ['likes', 'По лайкам'], ['views', 'По просмотрам']].map(([value, label]) => `<option value="${value}" ${(query.get('sort') || 'newest') === value ? 'selected' : ''}>${label}</option>`).join('') : ''}<option value="nameAsc" ${query.get('sort') === 'nameAsc' ? 'selected' : ''}>По имени: А–Я</option><option value="nameDesc" ${query.get('sort') === 'nameDesc' ? 'selected' : ''}>По имени: Я–А</option></select></label><span class="muted" role="status">Найдено: ${page.total}</span></form>`;
}
export function pagination(route, page) {
  const pages = Math.ceil(page.total / page.pageSize);
  if (pages < 2 && page.page === 1) return '';
  const address = number => { const q = new URLSearchParams(route.query); q.set('page', String(number)); return `${route.path}?${q}`; };
  return `<nav class="pagination" aria-label="Страницы каталога">${page.page > 1 ? link(address(page.page - 1), '← Назад', 'btn outline') : ''}<span>Страница ${page.page} из ${Math.max(1, pages)}</span>${page.page < pages ? link(address(page.page + 1), 'Далее →', 'btn outline') : ''}</nav>`;
}

export function avatar(user, className = '') {
  const name = user.displayName || user.username || '?';
  const initials = name.trim().split(/\s+/).slice(0, 2).map(part => Array.from(part)[0] || '').join('').toUpperCase();
  const url = user.avatarUrl;
  const safe = typeof url === 'string' && /^\/api\/users\//.test(url);
  return `<span class="user-avatar ${className}" role="img" aria-label="${escape(name)}"><span aria-hidden="true">${escape(initials)}</span>${safe ? `<img src="${escape(url)}" alt="" aria-hidden="true" width="44" height="44" loading="lazy">` : ''}</span>`;
}

export const itemSizes = ['XXS', 'XS', 'S', 'M', 'L', 'XL', 'XXL', '3XL', 'Единый размер'];
export const itemColors = ['Белый', 'Молочный', 'Бежевый', 'Коричневый', 'Чёрный', 'Серый', 'Синий', 'Голубой', 'Зелёный', 'Хаки', 'Красный', 'Бордовый', 'Розовый', 'Фиолетовый', 'Жёлтый', 'Оранжевый', 'Разноцветный'];
export function choiceField(label, name, current, values) {
  const options = current && !values.includes(current) ? [current, ...values] : values;
  return `<div class="field"><label for="${name}">${label}</label><select id="${name}" name="${name}"><option value="">Не указан</option>${options.map(value => `<option value="${escape(value)}" ${value === current ? 'selected' : ''}>${escape(value)}</option>`).join('')}</select></div>`;
}
export function photoPlaceholder(decorative = false) {
  return `<span class="photo-placeholder" ${decorative ? 'aria-hidden="true"' : 'role="img" aria-label="Фото не добавлено"'}><svg viewBox="0 0 120 100" fill="none" aria-hidden="true"><path d="M50 28a10 10 0 1 1 15 9l-5 4v9L18 75c-5 3-3 9 3 9h78c6 0 8-6 3-9L60 50" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/></svg><small>Фото не добавлено</small></span>`;
}

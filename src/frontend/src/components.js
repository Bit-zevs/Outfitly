export const categories = { Top: 'Верх', Bottom: 'Низ', Dress: 'Платья', Outerwear: 'Верхняя одежда', Footwear: 'Обувь', Accessory: 'Аксессуары' };
export const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
export const link = (path, text, style = '') => `<a class="${style}" href="#${escape(path)}">${escape(text)}</a>`;
export const title = (eyebrow, heading, description = '', action = '') => `<div class="page-head"><div><p class="eyebrow">${escape(eyebrow)}</p><h1>${escape(heading)}</h1>${description ? `<p class="lead">${escape(description)}</p>` : ''}</div>${action}</div>`;
export const empty = (heading, text, action = '') => `<div class="empty"><span class="empty-icon" aria-hidden="true">◇</span><h2>${escape(heading)}</h2><p class="lead">${escape(text)}</p>${action}</div>`;
export const crumbs = (path, text, current) => `<nav class="breadcrumbs" aria-label="Хлебные крошки">${link(path, text)}<span aria-hidden="true">/</span><span>${escape(current)}</span></nav>`;
export const badge = value => `<span class="badge ${value ? 'public' : ''}">${value ? 'Публичный доступ' : 'Личное'}</span>`;
export function photo(item, className = '', decorative = false) {
  const url = item.photoUrl;
  const safe = url && (/^https?:\/\//i.test(url) || /^\/api\//.test(url));
  return `<div class="photo ${className}">${safe ? `<img src="${escape(url)}" alt="${decorative ? '' : escape(item.name)}" loading="lazy" referrerpolicy="no-referrer">` : `<span class="photo-placeholder" ${decorative ? 'aria-hidden="true"' : 'role="img" aria-label="Без фотографии"'}>◇</span>`}</div>`;
}
export function itemCard(item, base = '/items') {
  return `<article class="item-card"><a href="#${base}/${escape(item.id)}">${photo(item, '', true)}<div class="card-heading"><h3>${escape(item.name)}</h3>${item.isPublic ? '<span class="public-dot" title="Опубликовано" aria-label="Опубликовано"></span>' : ''}</div><p>${escape(categories[item.category] || item.category)}${item.brand ? ` · ${escape(item.brand)}` : ''}</p></a></article>`;
}
export function collage(outfit) {
  return `<div class="collage ${outfit.items.length === 1 ? 'single' : ''}">${outfit.items.length ? outfit.items.slice(0, 4).map(item => photo(item, '', true)).join('') : '<span class="photo-placeholder">◇</span>'}</div>`;
}
export function outfitCard(outfit, base = '/outfits') {
  return `<article class="outfit-card"><a href="#${base}/${escape(outfit.id)}">${collage(outfit)}<div class="card-heading"><h3>${escape(outfit.name)}</h3>${badge(outfit.isPublic)}</div><p>Вещей в образе: ${outfit.items.length}</p></a></article>`;
}
export function field(label, name, value = '', { type = 'text', required = false, hint = '', autocomplete = '', min = '', max = '' } = {}) {
  return `<div class="field"><label for="${name}">${escape(label)}${required ? ' <span aria-hidden="true">*</span>' : ''}</label><input id="${name}" name="${name}" type="${type}" value="${escape(value)}" ${required ? 'required' : ''} ${autocomplete ? `autocomplete="${autocomplete}"` : ''} ${min ? `minlength="${min}"` : ''} ${max ? `maxlength="${max}"` : ''} ${hint ? `aria-describedby="${name}-hint"` : ''}>${hint ? `<small id="${name}-hint">${escape(hint)}</small>` : ''}</div>`;
}
export function filters(route, page) {
  const query = route.query;
  return `<div class="filters" aria-label="Категории">${[['', 'Все'], ...Object.entries(categories)].map(([key, text]) => `<button type="button" class="chip ${(query.get('category') || '') === key ? 'selected' : ''}" data-category="${key}" aria-pressed="${(query.get('category') || '') === key}">${text}</button>`).join('')}</div><form class="search-row" id="catalog-search" role="search"><label class="search-box"><span class="sr-only">Поиск по названию</span><input type="search" name="search" placeholder="Поиск по названию вещи" value="${escape(query.get('search'))}"><button class="icon-button" aria-label="Найти">⌕</button></label><label class="sort-label">Порядок <select name="sort" id="sort"><option value="nameAsc">По имени: А–Я</option><option value="nameDesc" ${query.get('sort') === 'nameDesc' ? 'selected' : ''}>По имени: Я–А</option></select></label><span class="muted">Всего: ${page.total}</span></form>`;
}
export function pagination(route, page) {
  const pages = Math.ceil(page.total / page.pageSize);
  if (pages < 2 && page.page === 1) return '';
  const address = number => { const q = new URLSearchParams(route.query); q.set('page', String(number)); return `${route.path}?${q}`; };
  return `<nav class="pagination" aria-label="Страницы каталога">${page.page > 1 ? link(address(page.page - 1), '← Назад', 'btn outline') : ''}<span>Страница ${page.page} из ${Math.max(1, pages)}</span>${page.page < pages ? link(address(page.page + 1), 'Далее →', 'btn outline') : ''}</nav>`;
}

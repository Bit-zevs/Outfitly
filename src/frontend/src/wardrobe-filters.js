import { categories, escape, link } from './components.js';

export function wardrobeQuery(query) {
  const result = new URLSearchParams();
  for (const key of ['search', 'category', 'color', 'brand', 'size', 'publication', 'colorMissing', 'brandMissing', 'sizeMissing', 'sort', 'page']) {
    if (query.has(key)) result.set(key, query.get(key));
  }
  result.set('pageSize', '20');
  return result;
}
export function wardrobeFilters(route, options, total) {
  const query = route.query;
  const select = (name, label, entries, current) => `<label class="field">${label}<select name="${name}" data-filter-focus="${name}">${entries.map(([value, text]) => `<option value="${escape(value)}" ${value === current ? 'selected' : ''}>${escape(text)}</option>`).join('')}</select></label>`;
  const attribute = (name, label, values) => {
    const selected = query.get(`${name}Missing`) === 'true' ? 'missing' : query.has(name) ? `value:${query.get(name)}` : '';
    const entries = [['', 'Все'], ...values.map(value => value === null ? ['missing', 'Не указан'] : [`value:${value}`, value])];
    if (selected && !entries.some(([key]) => key === selected)) entries.push([selected, selected === 'missing' ? 'Не указан' : query.get(name)]);
    return select(name, label, entries, selected);
  };
  const labels = { search: 'Поиск', category: 'Категория', color: 'Цвет', brand: 'Бренд', size: 'Размер', publication: 'Публикация', colorMissing: 'Цвет', brandMissing: 'Бренд', sizeMissing: 'Размер' };
  const active = [...query].filter(([key, value]) => labels[key] && value && !(key === 'publication' && value === 'all'));
  const advanced = active.some(([key]) => !['search', 'category'].includes(key));
  const selected = active.map(([key, value]) => escape(labels[key] + ': ' + (key.endsWith('Missing') ? 'Не указан' : key === 'category' ? categories[value] || value : key === 'publication' ? (value === 'public' ? 'Опубликованные' : 'Личные') : value))).join('</span><span class="badge">');
  const removable = active.map(([key]) => {
    const remaining = new URLSearchParams(query); remaining.delete(key); remaining.delete('page');
    return link('/wardrobe?' + remaining.toString(), 'Убрать: ' + labels[key], 'badge filter-remove');
  }).join('');
  return `<form id="wardrobe-filters" class="wardrobe-filters" role="search"><label class="field">Поиск<input type="search" name="search" data-filter-focus="search" autocomplete="off" placeholder="Название, бренд или описание…" value="${escape(query.get('search'))}"></label>${select('category', 'Категория', [['', 'Все'], ...Object.entries(categories)], query.get('category') || '')}<details class="advanced-filters" ${advanced ? 'open' : ''}><summary><span class="filter-title">Фильтры</span><span class="filter-hint">Цвет, размер, бренд и публикация</span><span class="filter-toggle" aria-hidden="true"></span></summary><div class="filter-fields">${attribute('color', 'Цвет', options.colors)}${attribute('brand', 'Бренд', options.brands)}${attribute('size', 'Размер', options.sizes)}${select('publication', 'Публикация', [['all', 'Все'], ['public', 'Опубликованные'], ['private', 'Личные']], query.get('publication') || 'all')}</div></details><div class="actions"><button class="btn" type="submit" data-filter-focus="submit">Применить</button>${link('/wardrobe', 'Сбросить', 'btn outline')}</div><div class="active-filters" aria-label="Активные условия">${active.length ? `<span class="badge">${selected}</span>${removable}` : ''}</div><div class="catalog-results"><p class="muted" role="status">Найдено: ${total}</p>${select('sort', 'Порядок', [['nameAsc', 'По имени: А–Я'], ['nameDesc', 'По имени: Я–А'], ['newest', 'Сначала новые']], query.get('sort') || 'nameAsc')}</div></form>`;
}
export function bindWardrobeFilters(ctx) {
  const form = document.querySelector('#wardrobe-filters');
  form.onsubmit = event => {
    event.preventDefault();
    const query = new URLSearchParams();
    for (const key of ['search', 'category', 'publication', 'sort']) {
      const value = form.elements[key].value.trim();
      if (value && !(key === 'publication' && value === 'all')) query.set(key, value);
    }
    for (const key of ['color', 'brand', 'size']) {
      const value = form.elements[key].value;
      if (value === 'missing') query.set(`${key}Missing`, 'true');
      else if (value.startsWith('value:')) query.set(key, value.slice(6));
    }
    ctx.navigate(`/wardrobe${query.size ? `?${query}` : ''}`);
  };
}

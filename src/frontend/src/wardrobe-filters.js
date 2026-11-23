import { categories, escape } from './components.js';

const filterLabels = {
  search: 'Поиск', category: 'Категория', color: 'Цвет', brand: 'Бренд', size: 'Размер',
  publication: 'Публикация', colorMissing: 'Цвет', brandMissing: 'Бренд', sizeMissing: 'Размер'
};

export function activeWardrobeFilters(query) {
  return Object.keys(filterLabels).flatMap(key => {
    const value = query.get(key);
    if (!value?.trim() || (key.endsWith('Missing') && value !== 'true') ||
        (key === 'publication' && !['public', 'private'].includes(value))) return [];
    const description = key.endsWith('Missing') ? 'Не указан'
      : key === 'category' ? categories[value] || value
      : key === 'publication' ? (value === 'public' ? 'Опубликованные' : 'Личные')
      : value;
    return [{ key, label: filterLabels[key] + ': ' + description }];
  });
}

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
  const active = activeWardrobeFilters(query);
  const advanced = active.some(({ key }) => !['search', 'category'].includes(key));
  const removable = active.map(({ key, label }) => {
    const remaining = new URLSearchParams(query);
    remaining.delete(key);
    remaining.delete('page');
    const path = remaining.size ? '/wardrobe?' + remaining : '/wardrobe';
    return `<a class="badge filter-remove" href="#${escape(path)}" data-filter-focus="remove-${key}" aria-label="${escape(label + '. Убрать фильтр')}"><span>${escape(label)}</span><span aria-hidden="true">×</span></a>`;
  }).join('');
  return `<form id="wardrobe-filters" class="wardrobe-filters" role="search">
    <label class="field">Поиск<input type="search" name="search" data-filter-focus="search" autocomplete="off" placeholder="Например, тренч…" value="${escape(query.get('search'))}"></label>
    ${select('category', 'Категория', [['', 'Все'], ...Object.entries(categories)], query.get('category') || '')}
    <details class="advanced-filters" ${advanced ? 'open' : ''}>
      <summary data-filter-focus="advanced">Дополнительные фильтры<span class="filter-toggle" aria-hidden="true"></span></summary>
      <div class="filter-fields">${attribute('color', 'Цвет', options.colors)}${attribute('brand', 'Бренд', options.brands)}${attribute('size', 'Размер', options.sizes)}${select('publication', 'Публикация', [['all', 'Все'], ['public', 'Опубликованные'], ['private', 'Личные']], query.get('publication') || 'all')}</div>
    </details>
    <div class="filter-actions"><button class="btn" type="submit" data-filter-focus="submit">Применить</button><a class="btn outline" href="#/wardrobe" data-filter-focus="reset">Сбросить фильтры</a></div>
    ${removable ? `<div class="active-filters" role="group" aria-label="Активные фильтры">${removable}</div>` : ''}
    <div class="catalog-results"><p class="muted" role="status">Найдено: ${total}</p>${select('sort', 'Сортировка', [['nameAsc', 'По имени: А–Я'], ['nameDesc', 'По имени: Я–А'], ['newest', 'Сначала новые']], query.get('sort') || 'nameAsc')}</div>
  </form>`;
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

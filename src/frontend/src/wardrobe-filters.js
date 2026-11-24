import { categories, escape } from './components.js';
import { catalogQuery, changedFilter } from './router.js';

const attributes = [['color', 'Цвет', 'colors'], ['brand', 'Бренд', 'brands'], ['size', 'Размер', 'sizes']];
const filterLabels = {
  search: 'Поиск', category: 'Категория', color: 'Цвет', brand: 'Бренд', size: 'Размер',
  publication: 'Публикация', colorMissing: 'Цвет', brandMissing: 'Бренд', sizeMissing: 'Размер'
};

export function activeWardrobeFilters(query, personal = true) {
  return Object.keys(filterLabels).flatMap(key => {
    const value = query.get(key);
    if (!value?.trim() || (key.endsWith('Missing') && value !== 'true') ||
        (key === 'publication' && (!personal || !['public', 'private'].includes(value)))) return [];
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
export function publicCollectionQuery(query) {
  const result = catalogQuery(query, 'newest');
  for (const [name] of attributes) {
    if (query.get(`${name}Missing`) === 'true') result.set(`${name}Missing`, 'true');
    else if (query.get(name)) result.set(name, query.get(name));
  }
  return result;
}

function filterPath(route, query) {
  if (route.path === '/discover' && route.query.get('tab') === 'items') query.set('tab', 'items');
  return `${route.path}${query.size ? `?${query}` : ''}`;
}

const attributeOptions = (values = []) => values.map(value =>
  value === null ? ['missing', 'Не указан'] : [`value:${value}`, value]);

export function wardrobeFilters(route, options, total) {
  const query = route.query;
  const personal = route.path === '/wardrobe';
  const outfits = !personal && query.get('tab') !== 'items';
  const resetPath = filterPath(route, new URLSearchParams());
  const select = (name, label, entries, current) => `<label class="field">${label}<select name="${name}" data-filter-focus="${name}">${entries.map(([value, text]) => `<option value="${escape(value)}" ${value === current ? 'selected' : ''}>${escape(text)}</option>`).join('')}</select></label>`;
  const attribute = (name, label, values) => {
    const selected = query.get(`${name}Missing`) === 'true' ? 'missing' : query.has(name) ? `value:${query.get(name)}` : '';
    const entries = [['', 'Все'], ...attributeOptions(values)];
    if (selected && !entries.some(([key]) => key === selected)) entries.push([selected, selected === 'missing' ? 'Не указан' : query.get(name)]);
    return select(name, label, entries, selected);
  };
  const active = activeWardrobeFilters(query, personal);
  const advanced = !options || active.some(({ key }) => !['search', 'category'].includes(key));
  const removable = active.map(({ key, label }) => {
    const remaining = new URLSearchParams(query);
    remaining.delete(key);
    remaining.delete('page');
    const path = filterPath(route, remaining);
    return `<a class="badge filter-remove" href="#${escape(path)}" data-filter-focus="remove-${key}" aria-label="${escape(label + '. Убрать фильтр')}"><span>${escape(label)}</span><span aria-hidden="true">×</span></a>`;
  }).join('');
  const sorts = [['nameAsc', 'По имени: А–Я'], ['nameDesc', 'По имени: Я–А'], ['newest', 'Сначала новые']];
  if (!personal) sorts.push(['likes', 'По лайкам'], ['views', 'По просмотрам']);
  return `<form id="wardrobe-filters" class="wardrobe-filters" role="search" aria-label="${outfits ? 'Поиск образов' : 'Поиск вещей'}">
    <label class="field">${outfits ? 'По названию или описанию' : 'По названию, бренду или описанию'}<input type="search" name="search" data-filter-focus="search" autocomplete="off" placeholder="${outfits ? 'Например, для прогулки…' : 'Например, тренч…'}" value="${escape(query.get('search'))}"></label>
    ${select('category', outfits ? 'Категория вещи в образе' : 'Категория', [['', 'Все'], ...Object.entries(categories)], query.get('category') || '')}
    <details class="advanced-filters" ${advanced ? 'open' : ''}>
      <summary data-filter-focus="advanced">${outfits ? 'Фильтры по вещи в образе' : 'Дополнительные фильтры'}<span class="filter-toggle" aria-hidden="true"></span></summary>
      <div class="filter-fields">${attributes.map(([name, label, key]) => attribute(name, label, options?.[key])).join('')}${personal ? select('publication', 'Публикация', [['all', 'Все'], ['public', 'Опубликованные'], ['private', 'Личные']], query.get('publication') || 'all') : ''}</div>
      ${options ? '' : '<div class="filter-options-retry"><p role="status">Не удалось загрузить варианты цвета, бренда и размера.</p><button class="btn outline small" type="button">Повторить загрузку фильтров</button></div>'}
    </details>
    <div class="filter-actions"><button class="btn" type="submit" data-filter-focus="submit">Применить</button><a class="btn outline" href="#${escape(resetPath)}" data-filter-focus="reset" aria-label="Сбросить фильтры">${personal ? 'Сбросить фильтры' : 'Сбросить'}</a></div>
    <div class="catalog-results">
      ${removable ? `<div class="active-filters" role="group" aria-label="Активные фильтры">${removable}</div>` : ''}
      <p class="muted">Найдено:&nbsp;${total}</p>${select('sort', 'Сортировка', sorts, query.get('sort') || (personal ? 'nameAsc' : 'newest'))}
    </div>
  </form>`;
}
export function bindWardrobeFilters(ctx, optionsPath) {
  const form = document.querySelector('#wardrobe-filters');
  const { route } = ctx;
  form.elements.sort.onchange = () => ctx.navigate(changedFilter(route, 'sort', form.elements.sort.value));
  form.onsubmit = event => {
    event.preventDefault();
    const query = new URLSearchParams();
    for (const key of ['search', 'category', 'publication', 'sort']) {
      const value = form.elements[key]?.value.trim();
      if (value && !(key === 'publication' && value === 'all')) query.set(key, value);
    }
    for (const [key] of attributes) {
      const value = form.elements[key].value;
      if (value === 'missing') query.set(`${key}Missing`, 'true');
      else if (value.startsWith('value:')) query.set(key, value.slice(6));
    }
    ctx.navigate(filterPath(route, query));
  };

  const retry = form.querySelector('.filter-options-retry');
  if (!retry) return;
  const button = retry.querySelector('button');
  const status = retry.querySelector('[role="status"]');
  button.onclick = async () => {
    button.disabled = true;
    status.textContent = 'Загружаем варианты фильтров…';
    try {
      const options = await ctx.get(optionsPath);
      if (!form.isConnected) return;
      for (const [name, , key] of attributes) {
        const field = form.elements[name];
        for (const [value, label] of attributeOptions(options[key])) {
          if ([...field.options].some(option => option.value === value)) continue;
          const option = document.createElement('option');
          option.value = value;
          option.textContent = label;
          field.add(option);
        }
      }
      if (document.activeElement === button || document.activeElement === document.body) {
        form.querySelector('.advanced-filters summary').focus();
      }
      status.textContent = 'Варианты фильтров загружены.';
      status.classList.add('sr-only');
      button.hidden = true;
    } catch {
      if (form.isConnected) status.textContent = 'Варианты фильтров всё ещё недоступны. Попробуйте снова.';
    } finally {
      if (form.isConnected && !button.hidden) button.disabled = false;
    }
  };
}

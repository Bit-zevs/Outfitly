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
  const select = (name, label, entries, current) => `<label class="field">${label}<select name="${name}">${entries.map(([value, text]) => `<option value="${escape(value)}" ${value === current ? 'selected' : ''}>${escape(text)}</option>`).join('')}</select></label>`;
  const attribute = (name, label, values) => {
    const selected = query.get(`${name}Missing`) === 'true' ? 'missing' : query.has(name) ? `value:${query.get(name)}` : '';
    const entries = [['', 'Все'], ...values.map(value => value === null ? ['missing', 'Не указан'] : [`value:${value}`, value])];
    if (selected && !entries.some(([key]) => key === selected)) entries.push([selected, selected === 'missing' ? 'Не указан' : query.get(name)]);
    return select(name, label, entries, selected);
  };
  return `<form id="wardrobe-filters" class="wardrobe-filters" role="search"><label class="field">Поиск<input type="search" name="search" placeholder="Название, бренд или описание" value="${escape(query.get('search'))}"></label>${select('category', 'Категория', [['', 'Все'], ...Object.entries(categories)], query.get('category') || '')}${attribute('color', 'Цвет', options.colors)}${attribute('brand', 'Бренд', options.brands)}${attribute('size', 'Размер', options.sizes)}${select('publication', 'Публикация', [['all', 'Все'], ['public', 'Опубликованные'], ['private', 'Личные']], query.get('publication') || 'all')}${select('sort', 'Порядок', [['nameAsc', 'По имени: А–Я'], ['nameDesc', 'По имени: Я–А']], query.get('sort') || 'nameAsc')}<div class="actions"><button class="btn" type="submit">Применить</button>${link('/wardrobe', 'Сбросить', 'btn outline')}</div><p class="muted" role="status">Найдено: ${total}</p></form>`;
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

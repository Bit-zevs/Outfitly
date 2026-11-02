import { categories, escape, title, empty, link } from './components.js';

export async function statisticsPage(ctx) {
  const data = await ctx.get('/api/wardrobe/statistics');
  const card = (label, value) => `<div class="statistics-card"><strong>${escape(value)}</strong><span>${escape(label)}</span></div>`;
  const distribution = (heading, groups) => `<section class="statistics-distribution"><h2>${heading}</h2>${groups.length ? `<ul>${groups.map(g => `<li><div><span>${escape(g.label ?? 'Не указан')}</span><strong>${escape(g.count)}</strong></div><meter min="0" max="${Math.max(1, data.items)}" value="${g.count}" aria-label="${escape(g.label ?? 'Не указан')}">${g.count}</meter></li>`).join('')}</ul>` : '<p>Нет данных</p>'}</section>`;
  return { title: 'Статистика', html: title('Ваш гардероб', 'Статистика', 'Весь сохранённый гардероб, включая личные вещи и образы.') +
    `<div class="statistics-cards">${card('Вещей', data.items)}${card('Образов', data.outfits)}${card('Опубликовано вещей', data.publicItems)}${card('Опубликовано образов', data.publicOutfits)}${card('Вещей без образов', data.itemsWithoutOutfits)}</div>` +
    (!data.items && !data.outfits ? empty('Гардероб пока пуст', 'Добавьте первую вещь, чтобы увидеть состав гардероба.', link('/items/new', 'Добавить вещь', 'btn')) : '') +
    `<div class="statistics-distributions">${distribution('Категории', data.categories.map(g => ({ label: categories[g.category] || g.category, count: g.count })))}${distribution('Цвета', data.colors)}${distribution('Бренды', data.brands)}</div>` +
    `<section><h2>Просмотры и лайки</h2><p class="lead">За всё время для существующих вещей и образов, включая доступ по ссылкам и снятые публикации. Просмотры уникальны для каждой вещи или образа: один человек может учитываться несколько раз в общей сумме.</p><div class="statistics-cards">${card('Просмотры вещей', data.itemReactions.views)}${card('Лайки вещей', data.itemReactions.likes)}${card('Просмотры образов', data.outfitReactions.views)}${card('Лайки образов', data.outfitReactions.likes)}</div></section>` };
}

import { escape, title, empty, link, outfitCard } from './components.js';
import { modal, showError } from './ui.js';
export function localDate(now = new Date()) {
  return `${String(now.getFullYear()).padStart(4, '0')}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}
export function validDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value || '') || value.startsWith('0000')) return false;
  const date = new Date(`${value}T12:00:00`);
  return !Number.isNaN(date.getTime()) && localDate(date) === value;
}
export function calendarState(query, today = localDate()) {
  const raw = query.get('month');
  const month = validDate(`${raw}-01`) ? raw : validDate(query.get('date')) ? query.get('date').slice(0, 7) : today.slice(0, 7);
  const date = validDate(query.get('date')) && query.get('date').startsWith(month) ? query.get('date') : today.startsWith(month) ? today : `${month}-01`;
  return { month, date };
}
export const calendarPath = (date) => `/calendar?month=${date.slice(0, 7)}&date=${date}`;
export function monthDays(month) {
  const first = new Date(`${month}-01T12:00:00`);
  const count = new Date(first.getFullYear(), first.getMonth() + 1, 0).getDate();
  return { offset: (first.getDay() + 6) % 7, dates: Array.from({ length: count }, (_, i) => `${month}-${String(i + 1).padStart(2, '0')}`) };
}
export async function calendarPage(ctx) {
  const today = localDate(); const { month, date } = calendarState(ctx.route.query, today);
  const entries = await ctx.get(`/calendar?month=${month}`);
  const dayEntries = entries.filter(x => x.date === date);
  const { offset, dates } = monthDays(month);
  const pretty = value => new Date(`${value}T12:00:00`).toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' });
  const adjacent = delta => {
    const value = new Date(`${month}-01T12:00:00`); value.setMonth(value.getMonth() + delta); return localDate(value);
  };
  let saving = false;
  return { title: 'Календарь образов', html: title('Ваши планы', 'Календарь образов') +
    `<nav class="actions calendar-navigation" aria-label="Месяц">${month > '0001-01' ? link(calendarPath(adjacent(-1)), '← Предыдущий', 'btn outline') : ''}<h2>${escape(new Date(`${month}-01T12:00:00`).toLocaleDateString('ru-RU', { month: 'long', year: 'numeric' }))}</h2>${month < '9999-12' ? link(calendarPath(adjacent(1)), 'Следующий →', 'btn outline') : ''}${link(calendarPath(today), 'Сегодня', 'btn outline')}</nav>` +
    `<div class="calendar-grid" aria-label="Дни месяца">${['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'].map(x => `<span class="calendar-weekday">${x}</span>`).join('')}${Array.from({ length: offset }, () => '<span aria-hidden="true"></span>').join('')}${dates.map(value => { const count = entries.filter(x => x.date === value).length; return `<a class="calendar-day ${value === date ? 'selected' : ''}" href="#${calendarPath(value)}" aria-label="${escape(pretty(value))}, образов: ${count}" ${value === date ? 'aria-current="date"' : ''}><strong>${Number(value.slice(-2))}</strong>${count ? `<span>${count}<span class="sr-only"> образов</span></span>` : ''}${value === today ? '<small>Сегодня</small>' : ''}</a>`; }).join('')}</div>` +
    `<section><div class="section-heading"><h2>${escape(pretty(date))}</h2><button class="btn" id="calendar-add">Добавить образ</button></div><p id="calendar-error" class="error" role="alert" tabindex="-1" hidden></p>${dayEntries.length ? `<div class="outfit-grid">${dayEntries.map(x => `<div>${outfitCard(x.outfit)}<button class="btn outline" data-calendar-remove="${escape(x.outfit.id)}">Убрать</button></div>`).join('')}</div>` : empty('На этот день пока нет образов', 'Выберите образ из своей коллекции.')}</section>`, bind() {
      const current = () => !ctx.signal?.aborted && (ctx.isCurrent?.() ?? true);
      async function change(id, assigned, dialog) {
        if (saving || !current()) return;
        saving = true; ctx.setSaving?.(true);
        document.querySelectorAll('[data-calendar-remove], #calendar-add, [data-calendar-pick]').forEach(x => { x.disabled = true; });
        try {
          await ctx.api.request(`/calendar/${date}/outfits/${id}`, { method: assigned ? 'PUT' : 'DELETE', signal: ctx.signal });
          if (!current()) return;
          dialog?.close(); ctx.refresh();
        } catch (error) {
          if (!current() || error.name === 'AbortError') return;
          showError(document.querySelector(dialog?.open ? '#calendar-picker-error' : '#calendar-error'), error);
        } finally {
          saving = false;
          if (current()) { ctx.setSaving?.(false); document.querySelectorAll('[data-calendar-remove], #calendar-add, [data-calendar-pick]').forEach(x => { x.disabled = false; }); }
        }
      }
      document.querySelectorAll('[data-calendar-remove]').forEach(button => { button.onclick = () => change(button.dataset.calendarRemove, false); });
      document.querySelector('#calendar-add').onclick = () => {
        modal('Выбрать образ', '<p role="status">Загрузка образов…</p>', dialog => {
          let active = true; dialog.addEventListener('close', () => { active = false; }, { once: true });
          const load = async () => {
            try {
              const outfits = await ctx.get('/outfits');
              if (!active || !current()) return;
              const choices = outfits.filter(x => !dayEntries.some(e => e.outfit.id === x.id));
              const body = dialog.querySelector('.dialog-body') || dialog;
              // Keep the modal heading and its close controls intact.
              let container = dialog.querySelector('#calendar-picker');
              if (!container) { container = document.createElement('div'); container.id = 'calendar-picker'; body.append(container); }
              const loading = dialog.querySelector('[role="status"]'); loading?.remove();
              container.innerHTML = `<label class="field">Поиск по названию<input type="search" id="calendar-search"></label><p id="calendar-picker-error" class="error" role="alert" tabindex="-1" hidden></p><div id="calendar-choices"></div>`;
              const render = () => {
                const search = container.querySelector('input').value.trim().toLocaleLowerCase('ru-RU');
                const filtered = choices.filter(x => x.name.toLocaleLowerCase('ru-RU').includes(search));
                container.querySelector('#calendar-choices').innerHTML = filtered.length ? filtered.map(x => `<button class="btn outline calendar-choice" data-calendar-pick="${escape(x.id)}">${escape(x.name)}</button>`).join('') : !outfits.length ? empty('Пока нет образов', 'Сначала создайте образ.', link('/outfits/new', 'Создать образ', 'btn')) : '<p>Подходящих образов нет или все уже назначены.</p>';
                container.querySelectorAll('[data-calendar-pick]').forEach(button => { button.onclick = () => change(button.dataset.calendarPick, true, dialog); });
              };
              container.querySelector('input').oninput = render; render();
            } catch (error) {
              if (!active || !current()) return;
              const status = dialog.querySelector('[role="status"]');
              if (status) { status.innerHTML = 'Не удалось загрузить образы. <button class="btn outline" id="calendar-retry">Повторить</button>'; status.querySelector('button').onclick = load; }
            }
          }; load();
        });
      };
    } };
}

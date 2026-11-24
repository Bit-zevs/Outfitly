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
  const renderCount = count => count ? `<span>${count}<span class="sr-only"> образов</span></span>` : '';
  const dayLabel = (value, count) => `${pretty(value)}, образов: ${count}${value === today ? ', сегодня' : ''}`;
  const renderDay = () => dayEntries.length
    ? `<div class="outfit-grid">${dayEntries.map(({ outfit }) => `<div>${outfitCard(outfit)}<button class="btn outline" data-calendar-remove="${escape(outfit.id)}" aria-label="Убрать «${escape(outfit.name)}» из календаря">Убрать из календаря</button></div>`).join('')}</div>`
    : empty('На этот день пока нет образов', 'Выберите образ из своей коллекции.');
  let saving = false;
  return { title: 'Календарь образов', html: title('Ваши планы', 'Календарь образов') +
    `<nav class="actions calendar-navigation" aria-label="Месяц">${month > '0001-01' ? link(calendarPath(adjacent(-1)), '← Назад', 'btn outline') : ''}<h2>${escape(new Date(`${month}-01T12:00:00`).toLocaleDateString('ru-RU', { month: 'long', year: 'numeric' }))}</h2>${month < '9999-12' ? link(calendarPath(adjacent(1)), 'Вперёд →', 'btn outline') : ''}${link(calendarPath(today), 'Сегодня', 'btn outline')}</nav>` +
    `<div class="section-heading calendar-day-heading"><h2>${escape(pretty(date))}</h2><button class="btn" id="calendar-add">Добавить образ</button></div>` +
    `<div class="calendar-grid" aria-label="Дни месяца">${['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'].map(x => `<span class="calendar-weekday">${x}</span>`).join('')}${Array.from({ length: offset }, () => '<span aria-hidden="true"></span>').join('')}${dates.map(value => { const count = entries.filter(x => x.date === value).length; return `<a class="calendar-day ${value === date ? 'selected' : ''}" href="#${calendarPath(value)}" aria-label="${escape(dayLabel(value, count))}" ${value === date ? 'aria-current="date"' : ''}><strong>${Number(value.slice(-2))}</strong>${renderCount(count)}${value === today ? '<small class="calendar-today" aria-hidden="true">Сегодня</small>' : ''}</a>`; }).join('')}</div>` +
    `<div class="calendar-feedback"><p id="calendar-status" role="status"></p><p id="calendar-error" class="error" role="alert" tabindex="-1" hidden></p></div><div id="calendar-outfits">${renderDay()}</div>`, bind() {
      const current = () => !ctx.signal?.aborted && (ctx.isCurrent?.() ?? true);
      const add = document.querySelector('#calendar-add');
      const dayList = document.querySelector('#calendar-outfits');
      const selected = document.querySelector('.calendar-day.selected');
      const dayStatus = document.querySelector('#calendar-status');
      const dayError = document.querySelector('#calendar-error');
      dayList.onclick = event => {
        const button = event.target.closest('[data-calendar-remove]');
        if (!button) return;
        const entry = dayEntries.find(({ outfit }) => outfit.id === button.dataset.calendarRemove);
        if (entry) change(entry.outfit, false);
      };
      async function change(outfit, assigned, dialog) {
        if (saving || !current()) return;
        saving = true; ctx.setSaving?.(true);
        dayError.hidden = true;
        dayStatus.textContent = '';
        document.querySelectorAll('[data-calendar-remove], #calendar-add, [data-calendar-pick]').forEach(x => { x.disabled = true; });
        let focusTarget;
        try {
          await ctx.api.request(`/calendar/${date}/outfits/${outfit.id}`, { method: assigned ? 'PUT' : 'DELETE', signal: ctx.signal });
          if (!current()) return;
          const index = dayEntries.findIndex(entry => entry.outfit.id === outfit.id);
          if (assigned) dayEntries.push({ date, outfit });
          else dayEntries.splice(index, 1);
          dayList.innerHTML = renderDay();
          selected.querySelector('span')?.remove();
          selected.querySelector('strong').insertAdjacentHTML('afterend', renderCount(dayEntries.length));
          selected.setAttribute('aria-label', dayLabel(date, dayEntries.length));
          dayStatus.textContent = assigned ? `«${outfit.name}» добавлен в календарь.` : `«${outfit.name}» убран из календаря.`;
          const remaining = dayList.querySelectorAll('[data-calendar-remove]');
          focusTarget = assigned ? add : remaining[Math.min(index, remaining.length - 1)] || add;
          if (dialog?.open) {
            dialog.addEventListener('close', () => queueMicrotask(() => {
              if (current()) add.focus();
            }), { once: true });
            dialog.close();
          }
        } catch (error) {
          if (!current() || error.name === 'AbortError') return;
          showError(document.querySelector(dialog?.open ? '#calendar-picker-error' : '#calendar-error'), error);
        } finally {
          saving = false;
          if (current()) {
            ctx.setSaving?.(false);
            document.querySelectorAll('[data-calendar-remove], #calendar-add, [data-calendar-pick]').forEach(x => { x.disabled = false; });
            focusTarget?.focus();
          }
        }
      }
      add.onclick = () => {
        modal('Выбрать образ', `<div id="calendar-picker"><form id="calendar-picker-search"><label class="field">Поиск по названию<input type="search" name="search" id="calendar-search" autocomplete="off"></label><button class="btn outline" id="calendar-find" type="submit">Найти</button></form><p id="calendar-picker-status" role="status"></p><p id="calendar-picker-error" class="error" role="alert" tabindex="-1" hidden></p><button class="btn outline" type="button" id="calendar-retry" hidden>Повторить</button><div id="calendar-choices"></div><div class="actions" id="calendar-pages"></div></div>`, dialog => {
          let active = true; dialog.addEventListener('close', () => { active = false; }, { once: true });
          let requestVersion = 0;
          const form = dialog.querySelector('#calendar-picker-search');
          const input = form.elements.search;
          const status = dialog.querySelector('#calendar-picker-status');
          const errorMessage = dialog.querySelector('#calendar-picker-error');
          const retry = dialog.querySelector('#calendar-retry');
          const list = dialog.querySelector('#calendar-choices');
          const pages = dialog.querySelector('#calendar-pages');
          let result;
          function renderChoices() {
            if (!result) return;
            const query = input.value.trim().toLocaleLowerCase('ru-RU');
            const choices = result.items.filter(outfit => !dayEntries.some(entry => entry.outfit.id === outfit.id)
              && outfit.name.toLocaleLowerCase('ru-RU').includes(query));
            status.textContent = `Доступно на этой странице: ${choices.length}`;
            list.innerHTML = choices.map(outfit => `<button type="button" class="btn outline calendar-choice" data-calendar-pick="${escape(outfit.id)}">${escape(outfit.name)}</button>`).join('');
            if (!choices.length) {
              list.innerHTML = !query && !result.total
                ? empty('Пока нет образов', 'Сначала создайте образ.', link('/outfits/new', 'Создать образ', 'btn'))
                : query ? '<p>Совпадений на этой странице нет. Измените запрос или нажмите «Найти» для поиска по всей коллекции.</p>'
                  : '<p>Образы на этой странице уже назначены. Проверьте другие страницы или выберите другой день.</p>';
            }
            list.querySelectorAll('[data-calendar-pick]').forEach((button, index) => {
              button.disabled = saving;
              button.onclick = () => change(choices[index], true, dialog);
            });
          }
          async function load(page = 1, search = '') {
            const mine = ++requestVersion;
            const restore = pages.contains(document.activeElement) || document.activeElement === retry;
            if (restore) input.focus({ preventScroll: true });
            status.textContent = 'Загрузка образов…';
            errorMessage.hidden = true;
            retry.hidden = true;
            result = null;
            list.replaceChildren();
            pages.replaceChildren();
            try {
              const loaded = await ctx.get(`/outfits?${new URLSearchParams({ page: String(page), pageSize: '20', search })}`);
              if (!active || !current() || mine !== requestVersion) return;
              result = loaded;
              for (const [number, label] of [[page - 1, 'Назад'], [page + 1, 'Далее']]) {
                if (number < 1 || number > Math.ceil(result.total / result.pageSize)) continue;
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'btn outline';
                button.textContent = label;
                button.onclick = () => load(number, search);
                pages.append(button);
              }
              renderChoices();
            } catch {
              if (!active || !current() || mine !== requestVersion) return;
              status.textContent = '';
              errorMessage.textContent = 'Не удалось загрузить образы.';
              errorMessage.hidden = false;
              retry.hidden = false;
              retry.onclick = () => load(page, search);
            }
          }
          input.oninput = renderChoices;
          form.onsubmit = event => {
            event.preventDefault();
            load(1, input.value.trim());
          };
          load();
        });
      };
    } };
}

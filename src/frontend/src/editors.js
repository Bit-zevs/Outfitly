import { categories, crumbs, escape, field, link, photo, title } from './components.js';
import { errorMessage } from './api.js';
import { saveItem, saveOutfit, uploadedPhoto } from './save.js';
import { busy, showError } from './ui.js';

export function validatePhoto(file) {
  if (!['image/jpeg', 'image/png'].includes(file.type)) return 'Выберите фотографию JPEG или PNG.';
  if (!file.size || file.size > 5 * 1024 * 1024) return 'Файл должен быть непустым и не больше 5 МБ.';
  return '';
}

export async function itemEditor(ctx, id) {
  const baseline = id ? await ctx.get(`/items/${id}`) : null;
  const draft = { id, baseline, photoMode: baseline?.photoUrl && !uploadedPhoto(baseline) ? 'url' : 'file', file: null, removePhoto: false, metadataSaved: false };
  let previewUrl;
  const heading = id ? 'Редактировать вещь' : 'Добавить вещь';
  return { title: heading, html: `${crumbs('/wardrobe', 'Мой гардероб', heading)}${title('Ваша коллекция', heading, 'Начните с основных деталей. Остальное можно добавить позже.')}<form id="item-form"><div class="form-layout"><div>${field('Название', 'name', baseline?.name, { required: true })}<div class="form-grid"><div class="field"><label for="category">Категория *</label><select name="category" id="category" required><option value="">Выберите категорию</option>${Object.entries(categories).map(([key, label]) => `<option value="${key}" ${baseline?.category === key ? 'selected' : ''}>${label}</option>`).join('')}</select></div>${field('Цвет', 'color', baseline?.color)}${field('Размер', 'size', baseline?.size)}${field('Бренд', 'brand', baseline?.brand)}</div><div class="field"><label for="description">Описание</label><textarea id="description" name="description" rows="4">${escape(baseline?.description)}</textarea></div><fieldset class="photo-source"><legend>Фотография</legend><div class="source-options"><label><input type="radio" name="photoMode" value="file" ${draft.photoMode === 'file' ? 'checked' : ''}>С устройства</label><label><input type="radio" name="photoMode" value="url" ${draft.photoMode === 'url' ? 'checked' : ''}>По ссылке</label></div><div id="file-panel" ${draft.photoMode !== 'file' ? 'hidden' : ''}><label class="file-label" for="file">Выбрать фотографию<input id="file" type="file" accept="image/jpeg,image/png" aria-describedby="file-hint"></label><small id="file-hint">JPEG или PNG, до 5 МБ</small><p id="file-name" class="muted"></p></div><div id="url-panel" ${draft.photoMode !== 'url' ? 'hidden' : ''}>${field('Ссылка на фото', 'photoUrl', uploadedPhoto(baseline) ? '' : baseline?.photoUrl, { type: 'url', hint: 'Полный адрес, начинающийся с http:// или https://' })}</div><button type="button" class="text-button danger" id="remove-photo">Убрать фотографию</button></fieldset></div><aside class="preview-panel"><div id="photo-preview">${baseline ? photo(baseline) : '<div class="photo"><span class="photo-placeholder">◇</span></div>'}</div><p class="eyebrow">Ваша вещь, ваши детали</p><p class="muted">Предварительный просмотр фотографии</p></aside></div><p class="error" role="alert" tabindex="-1" id="form-error" hidden></p><div id="conflict-review" hidden></div><div class="footer-actions">${link(id ? `/items/${id}` : '/wardrobe', 'Отмена', 'btn outline')}<button class="btn" type="submit" id="save-item">Сохранить вещь ↗</button></div></form>`, cleanup() { if (previewUrl) URL.revokeObjectURL(previewUrl); }, bind() {
    const form = document.querySelector('#item-form');
    const error = document.querySelector('#form-error');
    const saveButton = document.querySelector('#save-item');
    let locked = false;
    function preview(url) {
      const target = document.querySelector('#photo-preview');
      target.replaceChildren();
      const frame = document.createElement('div'); frame.className = 'photo';
      if (url) { const img = document.createElement('img'); img.src = url; img.alt = 'Предварительный просмотр'; img.referrerPolicy = 'no-referrer'; frame.append(img); }
      else { const placeholder = document.createElement('span'); placeholder.className = 'photo-placeholder'; placeholder.textContent = '◇'; frame.append(placeholder); }
      target.append(frame);
    }
    function mode() {
      draft.photoMode = form.elements.photoMode.value;
      document.querySelector('#file-panel').hidden = draft.photoMode !== 'file';
      document.querySelector('#url-panel').hidden = draft.photoMode !== 'url';
      form.elements.photoUrl.disabled = draft.photoMode !== 'url';
      preview(draft.photoMode === 'file' ? (previewUrl || (!draft.removePhoto ? draft.baseline?.photoUrl : null)) : (/^https?:\/\//i.test(form.elements.photoUrl.value) ? form.elements.photoUrl.value : null));
    }
    mode();
    form.oninput = () => { ctx.setDirty(true); form.elements.name.setCustomValidity(''); form.elements.photoUrl.setCustomValidity(''); };
    form.querySelectorAll('[name="photoMode"]').forEach(input => { input.onchange = mode; });
    form.elements.photoUrl.oninput = () => { mode(); };
    document.querySelector('#file').onchange = event => {
      const file = event.target.files[0];
      if (!file) return;
      const message = validatePhoto(file);
      if (message) { event.target.value = ''; error.hidden = false; error.textContent = message; error.focus(); return; }
      if (previewUrl) URL.revokeObjectURL(previewUrl);
      draft.file = file; draft.removePhoto = false; previewUrl = URL.createObjectURL(file);
      document.querySelector('#file-name').textContent = file.name;
      error.hidden = true; mode(); ctx.setDirty(true);
    };
    document.querySelector('#remove-photo').onclick = () => {
      draft.file = null; draft.removePhoto = true;
      if (previewUrl) URL.revokeObjectURL(previewUrl);
      previewUrl = null; form.elements.photoUrl.value = ''; document.querySelector('#file').value = ''; document.querySelector('#file-name').textContent = '';
      preview(null); ctx.setDirty(true);
    };
    form.onsubmit = async event => {
      event.preventDefault();
      if (locked || form.getAttribute('aria-busy') === 'true') return;
      if (!form.elements.name.value.trim()) { form.elements.name.setCustomValidity('Введите название вещи.'); form.elements.name.reportValidity(); return; }
      if (draft.photoMode === 'url' && form.elements.photoUrl.value && !/^https?:\/\//i.test(form.elements.photoUrl.value)) { form.elements.photoUrl.setCustomValidity('Используйте адрес http:// или https://'); form.elements.photoUrl.reportValidity(); return; }
      draft.values = Object.fromEntries(['name', 'category', 'color', 'size', 'brand', 'description', 'photoUrl'].map(name => [name, form.elements[name].value.trim() || null]));
      error.hidden = true; busy(form, true); ctx.setSaving(true);
      try {
        await saveItem(ctx.api, draft);
        if (!ctx.isCurrent()) return;
        ctx.setDirty(false); ctx.toast('Вещь сохранена'); ctx.navigate(`/items/${draft.id}`);
      } catch (failure) {
        if (!ctx.isCurrent()) return;
        showError(error, failure, draft.metadataSaved ? 'Основные данные вещи сохранены. Фотография не сохранена; можно повторить сохранение. ' : '');
        if (draft.id && failure.status === 409) {
          locked = true;
          const review = document.querySelector('#conflict-review'); review.hidden = false;
          review.innerHTML = '<button type="button" class="btn outline" id="review-current">Сверить актуальную вещь</button><div id="current-item"></div>';
          document.querySelector('#review-current').onclick = async () => {
            try {
              const actual = await ctx.get(`/items/${draft.id}`);
              if (!ctx.isCurrent()) return;
              document.querySelector('#current-item').innerHTML = `<div class="linebox"><h2>Сейчас на сервере</h2><dl class="properties">${[['Название', actual.name], ['Категория', categories[actual.category]], ['Бренд', actual.brand], ['Цвет', actual.color], ['Размер', actual.size]].map(([label, value]) => `<div><dt>${escape(label)}</dt><dd>${escape(value || 'Не указан')}</dd></div>`).join('')}</dl><p>${escape(actual.description || 'Без описания')}</p>${photo(actual)}<p>Ваши поля выше сохранены в редакторе. При повторении они заменят данные на сервере.</p><button type="button" class="btn outline" id="accept-current">Я сверил данные — разрешить сохранение</button></div>`;
              draft.baseline = actual;
              document.querySelector('#accept-current').onclick = () => { locked = false; saveButton.disabled = false; review.hidden = true; };
            } catch (e) { if (ctx.isCurrent()) showError(error, e); }
          };
        }
      } finally { if (ctx.isCurrent()) { busy(form, false); saveButton.disabled = locked; ctx.setSaving(false); } }
    };
  } };
}

export async function outfitEditor(ctx, id) {
  const initial = id ? await ctx.get(`/outfits/${id}`) : { name: '', description: '', items: [] };
  const draft = { id, name: initial.name, description: initial.description, selected: [...initial.items], metadataSaved: false };
  const heading = id ? 'Редактировать образ' : 'Создать образ';
  let searchController;
  let searchVersion = 0;
  return { title: heading, html: `${crumbs('/outfits', 'Мои образы', heading)}${title('Искусство сочетать', heading, 'Добавляйте вещи из своего гардероба. Сохраните пустой образ, если хотите вернуться к нему позже.')}<form id="outfit-form"><div class="editor-grid"><div class="editor-panel"><h2>Информация об образе</h2>${field('Название', 'name', initial.name, { required: true })}<div class="field"><label for="description">Описание</label><textarea name="description" id="description" rows="3">${escape(initial.description)}</textarea></div><h2 class="section-title">Вещи из гардероба</h2><div class="editor-search"><label><span class="sr-only">Найти вещь</span><input type="search" id="editor-search" placeholder="Поиск по названию"></label><label><span class="sr-only">Категория вещей</span><select id="editor-category"><option value="">Все категории</option>${Object.entries(categories).map(([k, v]) => `<option value="${k}">${v}</option>`).join('')}</select></label><button type="button" class="btn outline small" id="find-items">Найти</button></div><div id="available-items" aria-live="polite"></div><div class="actions" id="editor-pagination"></div><p class="error" id="catalog-error" role="alert" hidden></p></div><div class="editor-panel composition"><div class="section-heading"><h2>Состав образа</h2><span class="muted" id="selected-count"></span></div><div id="selected-items" aria-live="polite"></div><div class="linebox"><h3>Всё начинается с сочетания</h3><p>После сохранения вы сможете опубликовать образ или поделиться им по ссылке. Для этого нужна хотя бы одна вещь.</p></div></div></div><p class="error" role="alert" tabindex="-1" id="form-error" hidden></p><div id="save-review" hidden></div><div class="footer-actions">${link(id ? `/outfits/${id}` : '/outfits', 'Отмена', 'btn outline')}<button class="btn" id="save-outfit" type="submit">Сохранить образ ↗</button></div></form>`, cleanup() { searchController?.abort(); }, bind() {
    const form = document.querySelector('#outfit-form');
    const error = document.querySelector('#form-error');
    const saveButton = document.querySelector('#save-outfit');
    let available = [];
    let currentPage = 1;
    let locked = false;
    const isSaving = () => form.getAttribute('aria-busy') === 'true';
    function renderSelected() {
      document.querySelector('#selected-count').textContent = `${draft.selected.length} вещей`;
      document.querySelector('#selected-items').innerHTML = draft.selected.length ? `<div class="selected-grid">${draft.selected.map(item => `<div class="selected-item">${photo(item, '', true)}<button class="remove-item" type="button" ${isSaving() ? 'disabled' : ''} data-remove="${escape(item.id)}" aria-label="Убрать ${escape(item.name)}">×</button><p>${escape(item.name)}</p></div>`).join('')}</div>` : '<div class="empty small-empty"><span class="empty-icon">◇</span><h3>Место для ваших сочетаний</h3><p>Добавьте вещи из списка слева.</p></div>';
      document.querySelectorAll('[data-remove]').forEach(button => { button.onclick = () => { if (isSaving()) return; draft.selected = draft.selected.filter(i => i.id !== button.dataset.remove); ctx.setDirty(true); renderSelected(); renderAvailable(); }; });
    }
    function renderAvailable() {
      document.querySelector('#available-items').innerHTML = available.length ? available.map(item => {
        const selected = draft.selected.some(i => i.id === item.id);
        return `<div class="editor-choice">${photo(item, '', true)}<div><strong>${escape(item.name)}</strong><small>${escape(categories[item.category])}${item.brand ? ` · ${escape(item.brand)}` : ''}</small></div><button class="icon-button" type="button" data-add="${escape(item.id)}" ${selected || isSaving() ? 'disabled' : ''} aria-label="${selected ? 'Уже добавлено:' : 'Добавить'} ${escape(item.name)}">${selected ? '✓' : '+'}</button></div>`;
      }).join('') : `<div class="empty small-empty"><p>Вещи не найдены.</p>${link('/items/new', 'Добавить вещь ↗')}</div>`;
      document.querySelectorAll('[data-add]').forEach(button => { button.onclick = () => {
        if (isSaving()) return;
        if (!draft.selected.some(i => i.id === button.dataset.add)) draft.selected.push(available.find(i => i.id === button.dataset.add));
        ctx.setDirty(true); renderSelected(); renderAvailable();
      }; });
    }
    async function load(page = 1) {
      if (isSaving()) return;
      const version = ++searchVersion;
      searchController?.abort(); searchController = new AbortController();
      const params = new URLSearchParams({ page: String(page), pageSize: '8', search: document.querySelector('#editor-search').value, category: document.querySelector('#editor-category').value });
      if (!params.get('category')) params.delete('category');
      const target = document.querySelector('#available-items');
      target.setAttribute('aria-busy', 'true');
      const catalogError = document.querySelector('#catalog-error'); catalogError.hidden = true;
      try {
        const data = await ctx.api.request(`/items?${params}`, { signal: searchController.signal });
        if (!ctx.isCurrent() || version !== searchVersion) return;
        available = data.items; currentPage = data.page; renderAvailable();
        const nav = document.querySelector('#editor-pagination');
        nav.innerHTML = `${data.page > 1 ? '<button type="button" class="btn outline small" id="prev-items">← Назад</button>' : ''}<span class="muted">Всего: ${data.total}</span>${data.page * data.pageSize < data.total ? '<button type="button" class="btn outline small" id="next-items">Далее →</button>' : ''}`;
        nav.querySelectorAll('button').forEach(button => { button.disabled = isSaving(); });
        nav.querySelector('#prev-items')?.addEventListener('click', () => load(currentPage - 1));
        nav.querySelector('#next-items')?.addEventListener('click', () => load(currentPage + 1));
      } catch (failure) { if (failure.name !== 'AbortError' && ctx.isCurrent() && version === searchVersion) { catalogError.hidden = false; catalogError.textContent = `${errorMessage(failure)} Нажмите «Найти», чтобы повторить.`; } }
      finally { if (ctx.isCurrent() && version === searchVersion) target.removeAttribute('aria-busy'); }
    }
    form.oninput = event => { if (['name', 'description'].includes(event.target.name)) { ctx.setDirty(true); form.elements.name.setCustomValidity(''); } };
    document.querySelector('#find-items').onclick = () => load();
    document.querySelector('#editor-category').onchange = () => load();
    document.querySelector('#editor-search').onkeydown = event => { if (event.key === 'Enter') { event.preventDefault(); load(); } };
    renderSelected(); load();
    form.onsubmit = async event => {
      event.preventDefault();
      if (locked || form.getAttribute('aria-busy') === 'true') return;
      if (!form.elements.name.value.trim()) { form.elements.name.setCustomValidity('Введите название образа.'); form.elements.name.reportValidity(); return; }
      draft.name = form.elements.name.value.trim(); draft.description = form.elements.description.value.trim();
      error.hidden = true; busy(form, true); ctx.setSaving(true);
      try {
        await saveOutfit(ctx.api, draft);
        if (!ctx.isCurrent()) return;
        ctx.setDirty(false); ctx.toast('Образ сохранён'); ctx.navigate(`/outfits/${draft.id}`);
      } catch (failure) {
        if (!ctx.isCurrent()) return;
        showError(error, failure, draft.metadataSaved ? 'Образ сохранён частично. Выбранный состав остаётся в редакторе. ' : '');
        if (draft.id) {
          locked = true;
          const review = document.querySelector('#save-review'); review.hidden = false;
          review.innerHTML = '<button class="btn outline" type="button" id="read-outfit">Сверить сохранённый образ</button><div id="actual-outfit"></div>';
          document.querySelector('#read-outfit').onclick = async () => {
            try {
              const actual = await ctx.get(`/outfits/${draft.id}`);
              if (!ctx.isCurrent()) return;
              document.querySelector('#actual-outfit').innerHTML = `<div class="linebox"><h2>Сейчас на сервере: ${escape(actual.name)}</h2><p>${escape(actual.description || 'Без описания')}</p><p>${actual.isPublic ? 'Опубликован' : 'Личный образ'}</p><ul>${actual.items.map(i => `<li>${escape(i.name)}</li>`).join('') || '<li>Пустой образ</li>'}</ul><p>В редакторе остался ваш выбранный состав. Повторное сохранение приведёт серверный состав к нему.</p><button class="btn outline" type="button" id="confirm-review">Я сверил данные — разрешить сохранение</button></div>`;
              document.querySelector('#confirm-review').onclick = () => { locked = false; saveButton.disabled = false; review.hidden = true; };
            } catch (e) { if (ctx.isCurrent()) showError(error, e); }
          };
        }
      } finally { if (ctx.isCurrent()) { busy(form, false); renderSelected(); renderAvailable(); saveButton.disabled = locked; ctx.setSaving(false); } }
    };
  } };
}

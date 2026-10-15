import { ApiError } from './api.js';
import { passwordConfirmationError } from './auth.js';
import { badge, categories, collage, crumbs, empty, escape, field, filters, itemCard, link, outfitCard, pagination, photo, title } from './components.js';
import { catalogQuery, changedFilter, safeReturn, shareAddress } from './router.js';
import { busy, confirmAction, modal, showError } from './ui.js';

export function home(ctx) {
  return { title: 'Ваш личный гардероб', html: `<section class="hero"><div class="hero-copy"><p class="eyebrow">Ваш личный стиль в одном месте</p><h1>Сохраняйте вещи.<br>Создавайте образы.<br><em>Делитесь стилем.</em></h1><p class="lead">Любимые вещи, продуманные сочетания и немного вдохновения. Ваш гардероб — теперь в одном пространстве.</p><div class="actions">${link(ctx.auth.user ? '/dashboard' : '/register', ctx.auth.user ? 'В мой гардероб ↗' : 'Начать свой гардероб ↗', 'btn')}${link('/discover', 'Посмотреть коллекцию', 'btn outline')}</div><div class="hero-notes"><div><strong>06</strong><span>категорий одежды</span></div><div><strong>Ваши</strong><span>любимые сочетания</span></div><div><strong>01</strong><span>пространство для стиля</span></div></div></div><div class="hero-art"><img class="hero-image" src="./src/assets/hero.webp" alt="Песочный тренч на деревянном стуле в светлой студии"><img class="hero-float" src="./src/assets/hero-bag.webp" alt="Сумка как завершающая деталь образа"></div></section><section class="home-bottom"><p class="eyebrow">Своя коллекция. Свой ритм.</p><h2>Больше возможностей<br>для вещей, которые вы любите.</h2><p class="lead">Добавляйте одежду, собирайте комплекты и делитесь находками — публично или только по ссылке.</p></section>` };
}

export function authPage(ctx, register) {
  const { route } = ctx;
  const returnTo = route.query.get('return');
  return { title: register ? 'Регистрация' : 'Вход', html: `<section class="auth-grid"><div class="auth-art"><img src="./src/assets/hero-rack.webp" alt="Гардероб в натуральных оттенках"><p>${register ? 'Ваш стиль.<br>Ваша коллекция.' : 'Хороший стиль<br>начинается здесь.'}</p></div><div class="auth-form"><p class="eyebrow">${register ? 'Начните с Outfitly' : 'Добро пожаловать'}</p><h1>${register ? 'Создать аккаунт' : 'С возвращением'}</h1><p class="lead">${register ? 'Пространство для ваших вещей и образов.' : 'Войдите, чтобы продолжить собирать любимые сочетания.'}</p><form id="auth-form">${register ? field('Ваше имя', 'displayName', '', { required: true, autocomplete: 'name' }) : ''}${field('Email', 'email', route.query.get('email') || '', { type: 'email', required: true, autocomplete: 'email', max: 254 })}${field('Пароль', 'password', '', { type: 'password', required: true, autocomplete: register ? 'new-password' : 'current-password', min: register ? 8 : '', max: 128, hint: register ? 'От 8 до 128 символов. Можно использовать пробелы.' : '' })}${register ? `${field('Повторите пароль', 'passwordConfirmation', '', { type: 'password', required: true, autocomplete: 'new-password', max: 128 })}<p id="password-match-error" class="field-error" role="status" hidden></p>` : ''}<label class="check-label"><input type="checkbox" id="show-password">${register ? 'Показать пароли' : 'Показать пароль'}</label><p id="form-error" class="error" role="alert" tabindex="-1" hidden></p><button class="btn full" type="submit">${register ? 'Создать аккаунт ↗' : 'Войти ↗'}</button></form>${!register ? link('/forgot-password', 'Забыли пароль?') : ''}<p class="auth-foot">${register ? 'Уже есть аккаунт?' : 'Нет аккаунта?'} ${link(`/${register ? 'login' : 'register'}${returnTo ? `?return=${encodeURIComponent(returnTo)}` : ''}`, register ? 'Войти' : 'Зарегистрироваться')}</p></div></section>`, bind() {
    const form = document.querySelector('#auth-form');
    if (register) form.elements.passwordConfirmation.setAttribute('aria-describedby', 'password-match-error');
    document.querySelector('#show-password').onchange = event => {
      const type = event.target.checked ? 'text' : 'password';
      form.elements.password.type = type;
      if (register) form.elements.passwordConfirmation.type = type;
    };
    function validateConfirmation(show = false) {
      if (!register) return true;
      const confirmation = form.elements.passwordConfirmation;
      const message = passwordConfirmationError(form.elements.password.value, confirmation.value);
      confirmation.setCustomValidity(message);
      confirmation.setAttribute('aria-invalid', String(Boolean(message && (show || confirmation.value))));
      const notice = document.querySelector('#password-match-error');
      notice.hidden = !message || (!show && !confirmation.value);
      notice.textContent = message;
      return !message;
    }
    form.onsubmit = async event => {
      event.preventDefault();
      if (form.getAttribute('aria-busy') === 'true') return;
      const values = Object.fromEntries(new FormData(form));
      if (register && !values.displayName.trim()) { form.elements.displayName.setCustomValidity('Введите имя.'); form.elements.displayName.reportValidity(); return; }
      if (!validateConfirmation(true)) { form.elements.passwordConfirmation.reportValidity(); return; }
      busy(form, true);
      document.querySelector('#form-error').hidden = true;
      try {
        if (register) {
          const result = await ctx.auth.register(values.displayName, values.email, values.password, values.passwordConfirmation);
          if (!ctx.isCurrent()) return;
          ctx.navigate(`/check-email?email=${encodeURIComponent(values.email)}&sent=${result.confirmationEmailSent ? '1' : '0'}${returnTo ? `&return=${encodeURIComponent(returnTo)}` : ''}`);
        } else {
          await ctx.auth.login(values.email, values.password);
          if (ctx.isCurrent()) ctx.navigate(safeReturn(returnTo));
        }
      } catch (error) {
        if (!ctx.isCurrent()) return;
        if (!register && error.problem?.code === 'email_not_confirmed') ctx.navigate(`/check-email?email=${encodeURIComponent(values.email)}${returnTo ? `&return=${encodeURIComponent(returnTo)}` : ''}`);
        else if (!register && error.status === 401) showError(document.querySelector('#form-error'), new ApiError(400), 'Email или пароль неверны. ');
        else if (register && error.status === 409) { const el = document.querySelector('#form-error'); el.hidden = false; el.textContent = 'Этот email уже зарегистрирован. Войдите в аккаунт.'; el.focus(); }
        else showError(document.querySelector('#form-error'), error);
      } finally { busy(form, false); }
    };
    form.oninput = event => {
      if (!register) return;
      form.elements.displayName.setCustomValidity('');
      if (['password', 'passwordConfirmation'].includes(event.target.name)) validateConfirmation();
    };
  } };
}

export async function dashboard(ctx) {
  const [items, outfits] = await Promise.all([ctx.get('/items?pageSize=6'), ctx.get('/outfits')]);
  return { title: 'Обзор', html: `${title('Ваше пространство', `Здравствуйте, ${ctx.auth.user.displayName}`, 'Всё, что нужно для вашего следующего образа.')}<div class="stats">${[[items.total, 'вещей в гардеробе', '/wardrobe'], [outfits.length, 'собранных образов', '/outfits'], [outfits.filter(o => o.isPublic).length, 'опубликованных образов', '/outfits']].map(([number, label, path]) => `<a href="#${path}" class="stat"><span><strong>${number}</strong><span>${label}</span></span><span aria-hidden="true">↗</span></a>`).join('')}</div><h2 class="section-title">С чего начнём?</h2><div class="quick-grid"><a href="#/items/new" class="quick-card"><div><p class="eyebrow">Ваша коллекция</p><h3>Добавить вещь</h3><p>Новая находка или давний фаворит.</p><span aria-hidden="true">↗</span></div><img src="./src/assets/shirt-large.webp" alt=""></a><a href="#/outfits/new" class="quick-card"><div><p class="eyebrow">Искусство сочетать</p><h3>Создать образ</h3><p>Соберите вещи в любимый комплект.</p><span aria-hidden="true">↗</span></div><img src="./src/assets/outfit-business.webp" alt=""></a></div><div class="section-heading"><h2>Ваша коллекция</h2>${link('/wardrobe', 'Все вещи ↗')}</div>${items.items.length ? `<div class="cards">${items.items.map(i => itemCard(i)).join('')}</div>` : empty('Гардероб начинается с одной вещи', 'Добавьте то, что любите носить.', link('/items/new', 'Добавить вещь', 'btn'))}` };
}

export function bindFilters(ctx) {
  document.querySelectorAll('[data-category]').forEach(button => { button.onclick = () => ctx.navigate(changedFilter(ctx.route, 'category', button.dataset.category)); });
  const form = document.querySelector('#catalog-search');
  if (!form) return;
  form.onsubmit = event => { event.preventDefault(); ctx.navigate(changedFilter(ctx.route, 'search', form.elements.search.value.trim())); };
  form.elements.sort.onchange = () => ctx.navigate(changedFilter(ctx.route, 'sort', form.elements.sort.value));
}

export async function wardrobe(ctx) {
  const page = await ctx.get(`/items?${catalogQuery(ctx.route.query)}`);
  return { title: 'Мой гардероб', html: `${title('Личная коллекция', 'Мой гардероб', 'Любимые вещи — всегда под рукой.', link('/items/new', '＋ Добавить вещь', 'btn'))}${filters(ctx.route, page)}${page.items.length ? `<div class="cards">${page.items.map(i => itemCard(i)).join('')}</div>` : empty('Здесь пока нет вещей', 'Добавьте первую вещь или измените условия поиска.', link('/items/new', 'Добавить вещь', 'btn'))}${pagination(ctx.route, page)}`, bind: () => bindFilters(ctx) };
}

export async function outfitsPage(ctx) {
  const outfits = await ctx.get('/outfits');
  return { title: 'Мои образы', html: `${title('Искусство сочетать', 'Мои образы', 'Комплекты для ваших планов и настроения.', link('/outfits/new', '＋ Создать образ', 'btn'))}${outfits.length ? `<div class="outfit-grid">${outfits.map(o => outfitCard(o)).join('')}</div>` : empty('Первый образ ещё впереди', 'Выберите вещи из гардероба и соберите их в комплект.', link('/outfits/new', 'Создать образ', 'btn'))}` };
}

export async function discover(ctx) {
  const itemsTab = ctx.route.query.get('tab') === 'items';
  const data = await ctx.get(itemsTab ? `/wardrobe?${catalogQuery(ctx.route.query)}` : '/wardrobe/outfits');
  const query = new URLSearchParams(ctx.route.query); query.set('tab', 'items');
  return { title: 'Открытая коллекция', html: `<section class="discover-intro"><div><p class="eyebrow">Идеи и сочетания</p><h1>Открытая коллекция</h1><p class="lead">Вдохновение в деталях. Вещи и образы, которыми поделились пользователи Outfitly.</p></div><img src="./src/assets/hero-rack.webp" alt=""></section><div class="public-head"><nav class="tabs" aria-label="Тип коллекции">${link('/discover', 'Образы', !itemsTab ? 'selected' : '')}${link(`/discover?${query}`, 'Вещи', itemsTab ? 'selected' : '')}</nav><span class="muted">Открыто для просмотра без входа</span></div>${itemsTab ? `${filters(ctx.route, data)}${data.items.length ? `<div class="cards">${data.items.map(i => itemCard(i, '/public/items')).join('')}</div>` : empty('Пока ничего не найдено', 'Попробуйте другую категорию или измените поиск.')}${pagination(ctx.route, data)}` : data.length ? `<div class="outfit-grid">${data.map(o => outfitCard(o, '/public/outfits')).join('')}</div>` : empty('Коллекция ждёт первых образов', 'Опубликованные образы появятся здесь.')}`, bind: () => bindFilters(ctx) };
}

async function publicResource(ctx, kind, id) {
  if (kind === 'outfits') {
    const found = (await ctx.get('/wardrobe/outfits')).find(o => o.id === id);
    if (found) return found;
  } else {
    for (let page = 1; ; page++) {
      const data = await ctx.get(`/wardrobe?page=${page}&pageSize=100`);
      const found = data.items.find(i => i.id === id);
      if (found) return found;
      if (!data.items.length || page * data.pageSize >= data.total) break;
    }
  }
  throw new ApiError(404);
}

export async function detail(ctx, kind, id, access = 'owner') {
  const item = kind === 'items';
  const owner = access === 'owner';
  const resource = access === 'shared' ? await ctx.get(`/shared/${kind}/${encodeURIComponent(id)}`) : owner ? await ctx.get(`/${kind}/${id}`) : await publicResource(ctx, kind, id);
  const back = owner ? (item ? '/wardrobe' : '/outfits') : '/discover';
  const backText = owner ? (item ? 'Мой гардероб' : 'Мои образы') : 'Открытая коллекция';
  return { title: resource.name, html: `${crumbs(back, backText, resource.name)}<section class="detail"><div>${item ? photo(resource, 'detail-photo') : `<div class="detail-collage">${collage(resource)}</div><h2 class="section-title">Вещи в образе</h2><div class="pieces">${resource.items.map(i => owner ? `<a href="#/items/${escape(i.id)}">${photo(i, '', true)}<span>${escape(i.name)}</span></a>` : `<div>${photo(i)}<span>${escape(i.name)}</span></div>`).join('')}</div>`}</div><div class="detail-description"><p class="eyebrow">${item ? `Вещь / ${escape(categories[resource.category])}` : 'Ваш готовый комплект'}${access === 'shared' ? ' / По ссылке' : ''}</p><h1>${escape(resource.name)}</h1>${owner ? badge(resource.isPublic) : `<span class="badge">${access === 'shared' ? 'Просмотр по ссылке' : 'Открытая коллекция'}</span>`}<dl class="properties">${(item ? [['Категория', categories[resource.category]], ['Бренд', resource.brand], ['Размер', resource.size], ['Цвет', resource.color]] : [['Состав', `${resource.items.length} вещей`]]).map(([k, v]) => `<div><dt>${escape(k)}</dt><dd>${escape(v || 'Не указан')}</dd></div>`).join('')}</dl><h2 class="eyebrow">${item ? 'О вещи' : 'Об образе'}</h2><p class="description">${escape(resource.description || 'Описание пока не добавлено.')}</p>${owner ? `<div class="actions">${link(`/${kind}/${id}/edit`, item ? 'Редактировать' : 'Редактировать образ', 'btn')}<button class="btn outline danger" id="delete-resource">Удалить</button></div><div class="linebox"><label class="publication"><span><strong>Публичный доступ</strong><small>Показывать ${item ? 'вещь' : 'образ'} в открытой коллекции.</small></span><input type="checkbox" role="switch" id="publication" ${resource.isPublic ? 'checked' : ''} ${!item && !resource.items.length ? 'disabled' : ''}></label>${!item && !resource.items.length ? '<p class="muted">Добавьте хотя бы одну вещь для публикации и создания ссылки.</p>' : ''}<p class="error" role="alert" tabindex="-1" id="detail-error" hidden></p></div><div class="linebox"><h2>Поделиться по ссылке</h2><p>Ссылки работают независимо от публикации. Вы можете отозвать их в любой момент.</p><button class="btn outline" id="share" ${!item && !resource.items.length ? 'disabled' : ''}>Управление ссылками ↗</button></div>` : `<div class="linebox"><h2>У каждого свой стиль</h2><p>${access === 'shared' ? 'С вами поделились этой коллекцией. Здесь доступен только просмотр.' : 'Вдохновляйтесь сочетаниями и собирайте свой гардероб.'}</p>${link(ctx.auth.user ? '/dashboard' : '/register', ctx.auth.user ? 'Мой гардероб ↗' : 'Создать свой гардероб ↗', 'btn outline')}</div>`}</div></section>`, bind() {
    if (!owner) return;
    document.querySelector('#delete-resource').onclick = () => confirmAction(`Удалить ${item ? 'вещь' : 'образ'}?`, item ? 'Вещь исчезнет из всех образов, ссылки на неё перестанут работать. Если образ останется пустым, его публикация и ссылки будут отключены. Это действие нельзя отменить.' : 'Образ и ссылки на него станут недоступны. Вещи останутся в вашем гардеробе. Это действие нельзя отменить.', async () => {
      await ctx.api.request(`/${kind}/${id}`, { method: 'DELETE' });
      if (ctx.isCurrent()) { ctx.toast('Удалено'); ctx.navigate(back); }
    });
    document.querySelector('#publication').onchange = async event => {
      const toggle = event.currentTarget;
      toggle.disabled = true;
      try { await ctx.api.request(`/${kind}/${id}/publication`, { method: 'PATCH', body: { isPublic: toggle.checked } }); if (ctx.isCurrent()) ctx.refresh(); }
      catch (error) { if (ctx.isCurrent()) { toggle.checked = resource.isPublic; showError(document.querySelector('#detail-error'), error); } }
      finally { toggle.disabled = false; }
    };
    document.querySelector('#share').onclick = () => shareDialog(ctx, kind, id);
  } };
}

function shareDialog(ctx, kind, id) {
  const dialog = modal('Поделиться по ссылке', '<p class="lead">Ссылки доступны для просмотра без входа и не зависят от публикации.</p><div id="share-list" aria-live="polite">Загружаем ссылки…</div><p class="error" role="alert" tabindex="-1" hidden></p><button class="btn" id="create-link">Создать ссылку ↗</button>');
  const list = dialog.querySelector('#share-list');
  const errorBox = dialog.querySelector('.error');
  async function load() {
    const links = await ctx.api.request(`/${kind}/${id}/share-links`);
    if (!dialog.open || !ctx.isCurrent()) return;
    list.innerHTML = links.length ? links.map(l => `<div class="share-row"><label><span class="sr-only">Ссылка для просмотра</span><input readonly value="${escape(shareAddress(location.origin, kind, l.token))}"></label><div class="actions"><button class="btn outline small" data-copy="${escape(l.token)}">Копировать</button><button class="btn outline danger small" data-revoke="${escape(l.id)}">Отозвать</button></div></div>`).join('') : '<p class="muted">Активных ссылок пока нет.</p>';
    list.querySelectorAll('[data-copy]').forEach(button => { button.onclick = async () => {
      try { await navigator.clipboard.writeText(shareAddress(location.origin, kind, button.dataset.copy)); ctx.toast('Ссылка скопирована'); }
      catch { button.closest('.share-row').querySelector('input').select(); ctx.toast('Скопируйте выделенную ссылку вручную'); }
    }; });
    list.querySelectorAll('[data-revoke]').forEach(button => { button.onclick = () => mutate(button, () => ctx.api.request(`/share-links/${button.dataset.revoke}`, { method: 'DELETE' })); });
  }
  async function mutate(button, action) {
    dialog.querySelectorAll('button:not([data-close])').forEach(b => { b.disabled = true; });
    errorBox.hidden = true;
    try { await action(); await load(); }
    catch (error) { if (dialog.open && ctx.isCurrent()) showError(errorBox, error); }
    finally { if (dialog.open && ctx.isCurrent()) dialog.querySelectorAll('button').forEach(b => { b.disabled = false; }); }
  }
  dialog.querySelector('#create-link').onclick = event => mutate(event.currentTarget, () => ctx.api.request(`/${kind}/${id}/share-links`, { method: 'POST' }));
  load().catch(error => { list.textContent = 'Не удалось загрузить ссылки.'; showError(errorBox, error); });
}

export function account(ctx) {
  return { title: 'Мой аккаунт', html: `<section class="account">${title('Личные данные', 'Мой аккаунт', 'Ваше пространство в Outfitly.')}<div class="account-panel"><div class="account-identity"><span class="avatar large">${escape(ctx.auth.user.displayName.slice(0, 1).toUpperCase())}</span><div><h2>${escape(ctx.auth.user.displayName)}</h2><p class="muted">${escape(ctx.auth.user.email)}</p></div></div><dl class="properties"><div><dt>Отображаемое имя</dt><dd>${escape(ctx.auth.user.displayName)}</dd></div><div><dt>Email</dt><dd>${escape(ctx.auth.user.email)}</dd></div></dl><p class="muted">Здесь можно посмотреть данные аккаунта и завершить текущую сессию.</p><p class="error" role="alert" tabindex="-1" hidden></p><button class="btn outline danger" id="logout">Выйти из аккаунта ↗</button></div></section>`, bind() {
    document.querySelector('#logout').onclick = async event => {
      const button = event.currentTarget; button.disabled = true;
      try { await ctx.auth.logout(); if (ctx.isCurrent()) ctx.navigate('/'); }
      catch (error) { if (ctx.isCurrent()) showError(document.querySelector('.error'), error); }
      finally { button.disabled = false; }
    };
  } };
}

export function notFound(shared = false) {
  return { title: shared ? 'Ссылка недоступна' : 'Страница не найдена', html: empty(shared ? 'Эта ссылка больше недоступна' : 'Страница не найдена', shared ? 'Возможно, владелец отозвал ссылку или удалил вещь либо образ.' : 'Возможно, страница была удалена или адрес указан неверно.', link('/discover', 'В открытую коллекцию ↗', 'btn')) };
}

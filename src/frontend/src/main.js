import { enhanceChoices, installChoices } from './choices.js';
import { supportPage } from './support.js';
import { calendarPage } from './calendar.js';
import { statisticsPage } from './statistics.js';
import { createDraftSession, favoritesPage, draftsPage } from './workspace.js';
import { accountSettings, confirmEmailChangePage } from './account-settings.js';
import { profilePage } from './profiles.js';
import { createApi, errorMessage } from './api.js';
import { createAuth } from './auth.js';
import { avatar, escape, link, photoPlaceholder } from './components.js';
import { itemEditor, outfitEditor } from './editors.js';
import { outfitItemDetail, account, authPage, dashboard, detail, discover, home, notFound, outfitsPage, wardrobe } from './pages.js';
import { parseRoute, privateRoute } from './router.js';
import { checkEmailPage, confirmEmailPage } from './email-confirmation.js';
import { passwordResetPage } from './password-reset.js';
import { modal, toast } from './ui.js';

const content = document.querySelector('#content');
let dirty = false;
let saving = false;
let controller;
let cleanup;
let version = 0;
let acceptedHash = location.hash || '#/';
let authRestored = false;
let renderedRoute;
const api = createApi(undefined, () => {
  auth.clear(); dirty = false; saving = false;
  toast('Сессия завершилась. Войдите снова.');
  const route = parseRoute(location.hash);
  if (privateRoute(route.path)) navigate(`/login?return=${encodeURIComponent(route.path + (route.query.size ? `?${route.query}` : ''))}`);
});
const auth = createAuth(api);

function navigate(path, replace = false) {
  const hash = `#${path}`;
  if (hash === location.hash) { render(); return; }
  if (replace) { history.replaceState(null, '', hash); acceptedHash = hash; render(); }
  else location.hash = path;
}

function header(route) {
  const nav = auth.user
    ? { primary: [['/dashboard', 'Обзор'], ['/wardrobe', 'Гардероб'], ['/outfits', 'Образы'], ['/discover', 'Открытая коллекция']], secondary: [['/statistics', 'Статистика'], ['/calendar', 'Календарь'], ['/favorites', 'Избранное'], ['/drafts', 'Черновики'], ['/support', 'Поддержка']] }
    : { primary: [['/', 'Главная'], ['/discover', 'Открытая коллекция']], secondary: [] };
  const isCurrent = path => route.path === path || path === '/wardrobe' && route.path.startsWith('/items') || path === '/outfits' && route.path.startsWith('/outfits/');
  const renderLinks = links => links.map(([path, name]) => `<a href="#${path}" ${isCurrent(path) ? 'aria-current="page"' : ''}>${name}</a>`).join('');
  document.querySelector('#header').innerHTML = `<div class="header-inner"><a class="wordmark" href="#/" aria-label="Outfitly — главная">Outfitly<span>atelier</span></a><button class="menu-button icon-button" id="menu-button" aria-expanded="false" aria-controls="primary-nav" aria-label="Открыть меню">☰</button><nav id="primary-nav" class="primary-nav" aria-label="Основная навигация">${renderLinks(nav.primary)}</nav><div class="header-account">${auth.user ? `<details class="profile-menu"><summary aria-label="Меню профиля: ${escape(auth.user.displayName)}">${avatar(auth.user)}</summary><nav class="profile-menu-panel" aria-label="Меню профиля">${renderLinks([['/account', 'Мой профиль'], ['/settings', 'Настройки'], ...nav.secondary])}</nav></details>` : `${link('/login', 'Войти', 'login-link')}${link('/register', 'Начать', 'btn small register-link')}`}</div></div>`;
  const button = document.querySelector('#menu-button');
  button.onclick = () => { const open = button.getAttribute('aria-expanded') !== 'true'; button.setAttribute('aria-expanded', String(open)); button.setAttribute('aria-label', open ? 'Закрыть меню' : 'Открыть меню'); button.textContent = open ? '×' : '☰'; document.querySelector('#primary-nav').classList.toggle('open', open); };
  document.querySelector('#header').onkeydown = event => { if (event.key === 'Escape') { const profile = document.querySelector('.profile-menu'); if (profile?.open) { profile.open = false; profile.querySelector('summary').focus(); return; } button.setAttribute('aria-expanded', 'false'); button.setAttribute('aria-label', 'Открыть меню'); button.textContent = '☰'; document.querySelector('#primary-nav').classList.remove('open'); button.focus(); } };
}

async function resolve(ctx) {
  const { path, parts } = ctx.route;
  if (path === '/') return home(ctx);
  if (path === '/check-email') return checkEmailPage(ctx);
  if (path === '/confirm-email') return confirmEmailPage(ctx);
  if (path === '/confirm-email-change') return confirmEmailChangePage(ctx);
  if (path === '/forgot-password' || path === '/reset-password') return passwordResetPage(ctx, path === '/reset-password');
  if (path === '/login' || path === '/register') return auth.user ? dashboard(ctx) : authPage(ctx, path === '/register');
  if (path === '/statistics') return statisticsPage(ctx);
  if (path === '/dashboard') return dashboard(ctx);
  if (path === '/wardrobe') return wardrobe(ctx);
  if (path === '/calendar') return calendarPage(ctx);
  if (path === '/outfits') return outfitsPage(ctx);
  if (path === '/discover') return discover(ctx);
  if (path === '/favorites') return favoritesPage(ctx);
  if (path === '/drafts') return draftsPage(ctx);
  if (parts[0] === 'support' && parts.length <= 2) return supportPage(ctx, false, parts[1]);
  if (parts[0] === 'admin' && parts[1] === 'support' && parts.length <= 3) return supportPage(ctx, true, parts[2]);
  if (path === '/account') return account(ctx);
  if (path === '/settings') return accountSettings(ctx);
  if (parts[0]?.startsWith('@')) {
    const username = decodeURIComponent(parts[0].slice(1));
    if (parts.length === 1) return profilePage(ctx, username);
    if (parts.length === 3 && ['items', 'outfits'].includes(parts[1])) return detail(ctx, parts[1], parts[2], 'public', username);
  }
  if (path === '/items/new') return itemEditor(ctx);
  if (path === '/outfits/new') return outfitEditor(ctx);
  if (parts.length === 3 && ['items', 'outfits'].includes(parts[0]) && parts[2] === 'edit') return parts[0] === 'items' ? itemEditor(ctx, parts[1]) : outfitEditor(ctx, parts[1]);
  if (parts.length === 2 && ['items', 'outfits'].includes(parts[0])) return detail(ctx, parts[0], parts[1]);
  if (parts.length === 3 && ['public', 'shared'].includes(parts[0]) && ['items', 'outfits'].includes(parts[1])) return detail(ctx, parts[1], decodeURIComponent(parts[2]), parts[0]);
  if (parts.length === 5 && ['public', 'shared'].includes(parts[0]) && parts[1] === 'outfits' && parts[3] === 'items') return outfitItemDetail(ctx, parts[0], decodeURIComponent(parts[2]), decodeURIComponent(parts[4]));
  return notFound();
}

async function render() {
  const nextRoute = parseRoute(location.hash);
  const preserve = renderedRoute?.path === nextRoute.path && ['/wardrobe', '/discover'].includes(nextRoute.path) && renderedRoute.query.get('tab') === nextRoute.query.get('tab') && renderedRoute.query.get('page') === nextRoute.query.get('page');
  const scroll = window.scrollY;
  const focusId = document.activeElement?.id;
  const mine = ++version;
  controller?.abort(); controller = new AbortController();
  cleanup?.(); cleanup = null;
  document.querySelector('#dialog').close();
  dirty = false; saving = false;
  const route = parseRoute(location.hash);
  const signal = controller.signal;
  const isCurrent = () => version === mine && !signal.aborted;
  header(route);
  content.innerHTML = '<div class="loading" role="status"><span class="spinner"></span>Собираем вашу коллекцию…</div>';
  content.setAttribute('aria-busy', 'true');
  try {
    if (!authRestored) {
      try { await auth.restore({ signal }); authRestored = true; }
      catch (error) {
        if (privateRoute(route.path)) throw new Error('Session unavailable', { cause: error });
        if (isCurrent()) toast('Сервер недоступен. Личные данные появятся после подключения к API.');
      }
      if (!isCurrent()) return;
      header(route);
    }
    if (privateRoute(route.path) && !auth.user) { navigate(`/login?return=${encodeURIComponent(route.path + (route.query.size ? `?${route.query}` : ''))}`, true); return; }
    const ctx = { route, api, auth, drafts: createDraftSession, signal, isCurrent, get: path => api.request(path, { signal }), navigate, refresh: render, updateHeader: () => { if (isCurrent()) header(parseRoute(location.hash)); }, toast, setDirty: value => { if (isCurrent()) dirty = value; }, setSaving: value => { if (isCurrent()) saving = value; } };
    const page = await resolve(ctx);
    if (!isCurrent()) return;
    header(route);
    content.innerHTML = page.html;
    document.title = `${page.title} — Outfitly`;
    cleanup = page.cleanup;
    page.bind?.();
    window.scrollTo({ top: 0, behavior: 'instant' });
    content.focus({ preventScroll: true });
  } catch (error) {
    if (!isCurrent() || error.name === 'AbortError') return;
    if (error.status === 404) { const page = notFound(route.parts[0] === 'shared'); content.innerHTML = page.html; document.title = `${page.title} — Outfitly`; }
    else { content.innerHTML = `<div class="empty"><h1>Не удалось открыть страницу</h1><p class="lead">${escape(errorMessage(error))}</p><button class="btn" id="retry-page">Попробовать снова</button></div>`; document.querySelector('#retry-page').onclick = render; }
  } finally { if (isCurrent()) content.removeAttribute('aria-busy'); }
}

function requestLeave(action) {
  if (saving) { toast('Дождитесь окончания сохранения.'); return; }
  if (!dirty) { action(); return; }
  modal('Оставить изменения?', '<p class="lead">В форме есть несохранённые изменения. Если уйти, они будут потеряны.</p><div class="actions"><button class="btn outline" id="stay">Остаться</button><button class="btn" id="leave">Уйти без сохранения</button></div>', dialog => {
    dialog.querySelector('#stay').onclick = () => dialog.close();
    dialog.querySelector('#leave').onclick = () => { dirty = false; dialog.close(); action(); };
  });
}
document.addEventListener('click', event => {
  const anchor = event.target.closest('a[href^="#/"]');
  if (!anchor || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
  event.preventDefault();
  requestLeave(() => navigate(anchor.getAttribute('href').slice(1)));
});
window.addEventListener('hashchange', () => {
  const target = location.hash;
  if (dirty || saving) {
    history.replaceState(null, '', acceptedHash);
    requestLeave(() => { history.replaceState(null, '', target); acceptedHash = target; render(); });
  } else { acceptedHash = target; render(); }
});
window.addEventListener('beforeunload', event => { if (dirty || saving) { event.preventDefault(); event.returnValue = ''; } });
document.addEventListener('error', event => {
  if (event.target instanceof HTMLImageElement) {
    const img = event.target;
    if (img.closest('.user-avatar')) { img.remove(); return; }
    const placeholder = document.createElement('span'); placeholder.className = 'photo-placeholder'; placeholder.innerHTML = photoPlaceholder(); placeholder.setAttribute('role', 'img'); placeholder.setAttribute('aria-label', img.alt ? `Фото недоступно: ${img.alt}` : 'Фото недоступно'); img.replaceWith(placeholder);
  }
}, true);
document.querySelector('.skip-link').onclick = event => { event.preventDefault(); content.focus(); };
render();

// Keep native constraint validation and provide persistent, associated field errors.
document.addEventListener('invalid', event => {
  const input = event.target;
  if (!input.matches('input,textarea,select') || !input.id) return;
  if (input.tagName === 'SELECT' && input.hidden) { event.preventDefault(); input.parentElement.querySelector('.choice-trigger')?.focus(); }
  const id = input.id + '-validation';
  let notice = document.getElementById(id);
  if (!notice) { notice = document.createElement('small'); notice.id = id; notice.className = 'field-error'; input.after(notice); }
  notice.textContent = input.validationMessage;
  input.setAttribute('aria-invalid', 'true');
  const descriptions = new Set((input.getAttribute('aria-describedby') || '').split(' ').filter(Boolean));
  descriptions.add(id); input.setAttribute('aria-describedby', [...descriptions].join(' '));
}, true);
document.addEventListener('input', event => {
  const input = event.target;
  if (!input.id || !input.matches('input,textarea,select')) return;
  const notice = document.getElementById(input.id + '-validation');
  if (notice) { notice.remove(); input.removeAttribute('aria-invalid'); input.setAttribute('aria-describedby', (input.getAttribute('aria-describedby') || '').split(' ').filter(id => id !== notice.id).join(' ')); }
});

installChoices(document);

document.addEventListener('click', event => { const menu = document.querySelector('.profile-menu[open]'); if (menu && !menu.contains(event.target)) menu.open = false; });

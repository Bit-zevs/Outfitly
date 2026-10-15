import { field, link } from './components.js';
import { busy } from './ui.js';
import { passwordConfirmationError } from './auth.js';

export function passwordResetPage(ctx, reset) {
  const token = ctx.route.query.get('token');
  return { title: reset ? 'Новый пароль' : 'Восстановление пароля', html: `<section class="account"><h1>${reset ? 'Новый пароль' : 'Восстановление пароля'}</h1><form id="password-reset-form">${reset
    ? field('Новый пароль', 'password', '', { type: 'password', required: true, autocomplete: 'new-password', min: 8, max: 128 }) + field('Повторите пароль', 'confirmation', '', { type: 'password', required: true, autocomplete: 'new-password', min: 8, max: 128 })
    : field('Email', 'email', '', { type: 'email', required: true, autocomplete: 'email', max: 254 })}<button class="btn" type="submit">${reset ? 'Сохранить пароль' : 'Получить письмо'}</button></form><p id="reset-status" role="status"></p>${link('/login', 'Войти')}${reset ? ` · ${link('/forgot-password', 'Получить новую ссылку')}` : ''}</section>`, bind() {
    const form = document.querySelector('#password-reset-form');
    form.onsubmit = async event => {
      event.preventDefault();
      if (form.getAttribute('aria-busy') === 'true') return;
      const status = document.querySelector('#reset-status');
      if (reset && !token) { status.textContent = 'Ссылка недействительна. Запросите новое письмо.'; return; }
      if (reset) {
        const mismatch = passwordConfirmationError(form.elements.password.value, form.elements.confirmation.value);
        if (mismatch) { status.textContent = mismatch; return; }
      }
      busy(form, true);
      try {
        if (reset) {
          await ctx.auth.resetPassword(token, form.elements.password.value);
          ctx.auth.clear();
        }
        else await ctx.auth.forgotPassword(form.elements.email.value);
        if (!ctx.isCurrent()) return;
        if (reset) ctx.navigate('/login', true);
        else status.textContent = 'Если аккаунт доступен для восстановления, письмо отправлено. Проверьте почту. Повторить запрос можно через минуту.';
      } catch (error) {
        if (ctx.isCurrent()) status.textContent = error.problem?.code === 'invalid_password_reset_token'
          ? 'Ссылка недействительна, уже использована или срок её действия истёк. Запросите новое письмо.'
          : error.status === 429 ? 'Слишком много запросов. Подождите минуту.' : 'Не удалось выполнить запрос. Попробуйте позже.';
      } finally { busy(form, false); }
    };
  } };
}

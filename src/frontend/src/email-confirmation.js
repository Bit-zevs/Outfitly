import { field, link } from './components.js';
import { busy } from './ui.js';
import { passwordConfirmationError } from './auth.js';

export function checkEmailPage(ctx) {
  if (ctx.auth?.user) return { title: 'Почта подтверждена', html: `<section class="account email-flow"><h1>Почта подтверждена</h1><p class="lead">Вы уже вошли в аккаунт. Повторное подтверждение не требуется.</p>${link('/settings', 'Настройки аккаунта', 'btn')}</section>` };
  const email = ctx.route.query.get('email') || '';
  const returnTo = ctx.route.query.get('return');
  return { title: 'Подтвердите почту', html: `<section class="account email-flow"><h1>Проверьте почту</h1><p class="lead">Если адрес ожидает подтверждения, вы получите письмо со ссылкой для создания пароля. Проверьте папку «Спам».</p><form id="resend-form">${field('Email', 'email', email, { type: 'email', required: true, autocomplete: 'email', max: 254 })}<button class="btn" type="submit">Отправить письмо повторно</button></form><p id="email-status" role="status"></p><div class="actions">${link(`/login?email=${encodeURIComponent(email)}${returnTo ? `&return=${encodeURIComponent(returnTo)}` : ''}`, 'Перейти ко входу', 'btn outline')}${link(`/forgot-password?email=${encodeURIComponent(email)}`, 'Восстановить пароль', 'btn outline')}</div></section>`, bind() {
    const form = document.querySelector('#resend-form');
    form.onsubmit = async event => {
      event.preventDefault();
      if (form.getAttribute('aria-busy') === 'true') return;
      busy(form, true);
      try {
        await ctx.auth.resendConfirmation(form.elements.email.value);
        if (ctx.isCurrent()) document.querySelector('#email-status').textContent = 'Если этот адрес ожидает подтверждения, письмо отправлено. Ссылка действует 24 часа.';
      } catch (error) {
        if (ctx.isCurrent()) document.querySelector('#email-status').textContent = error.status === 429
          ? 'Подождите минуту перед повторной отправкой.' : 'Не удалось отправить письмо. Попробуйте позже.';
      } finally { busy(form, false); }
    };
  } };
}

export function confirmEmailPage(ctx) {
  const token = ctx.route.query.get('token');
  const success = ctx.route.query.get('confirmed') === '1';
  const passwordFields = token && !success ? `<form id="confirm-email-form">
    ${field('Пароль', 'password', '', { type: 'password', required: true, autocomplete: 'new-password', min: 8, max: 128, hint: 'От 8 до 128 символов.' })}
    ${field('Повторите пароль', 'passwordConfirmation', '', { type: 'password', required: true, autocomplete: 'new-password', max: 128 })}
    <button class="btn" type="submit">Сохранить пароль и подтвердить почту</button>
  </form>` : '';
  return { title: 'Подтверждение почты', html: `<section class="account email-flow"><h1>Подтверждение почты</h1><p id="confirmation-status" role="status">${success ? 'Почта подтверждена. Войдите с вашим паролем.' : token ? 'Придумайте пароль, чтобы подтвердить почту и завершить регистрацию.' : 'Ссылка недействительна. Запросите новое письмо.'}</p>${passwordFields}<div class="actions">${link('/login', 'Войти', 'btn outline')}${!success && !ctx.auth?.user ? link('/check-email', 'Получить новое письмо', 'btn outline') : ''}</div></section>`, bind() {
    const form = document.querySelector('#confirm-email-form');
    if (!form) return;
    const { password, passwordConfirmation } = form.elements;
    form.oninput = () => passwordConfirmation.setCustomValidity('');
    form.onsubmit = async event => {
      event.preventDefault();
      if (form.getAttribute('aria-busy') === 'true') return;
      const mismatch = passwordConfirmationError(password.value, passwordConfirmation.value);
      if (mismatch) {
        passwordConfirmation.setCustomValidity(mismatch);
        passwordConfirmation.reportValidity();
        return;
      }
      busy(form, true);
      try {
        await ctx.auth.confirmEmail(token, password.value, passwordConfirmation.value);
        if (ctx.isCurrent()) ctx.navigate('/confirm-email?confirmed=1', true);
      } catch (error) {
        if (ctx.isCurrent()) document.querySelector('#confirmation-status').textContent =
          error.problem?.code === 'invalid_confirmation_token' ? 'Ссылка недействительна, уже использована или срок её действия истёк. Запросите новое письмо.'
            : error.status === 429 ? 'Слишком много попыток. Подождите минуту.'
              : error.status === 409 ? 'Состояние подтверждения изменилось. Используйте последнее письмо или попробуйте войти.'
                : 'Не удалось подтвердить почту. Попробуйте позже.';
      } finally { busy(form, false); }
    };
  } };
}

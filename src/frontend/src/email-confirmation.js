import { field, link } from './components.js';
import { busy } from './ui.js';

export function checkEmailPage(ctx) {
  const email = ctx.route.query.get('email') || '';
  const failed = ctx.route.query.get('sent') === '0';
  const returnTo = ctx.route.query.get('return');
  return { title: 'Подтвердите почту', html: `<section class="account"><h1>Подтвердите почту</h1><p class="lead">${failed ? 'Аккаунт создан, но письмо отправить не удалось. Повторите отправку через минуту.' : 'Для входа подтвердите почту по ссылке из письма. Проверьте также папку «Спам».'}</p><form id="resend-form">${field('Email', 'email', email, { type: 'email', required: true, autocomplete: 'email', max: 254 })}<button class="btn" type="submit">Отправить письмо повторно</button></form><p id="email-status" role="status"></p>${link(`/login?email=${encodeURIComponent(email)}${returnTo ? `&return=${encodeURIComponent(returnTo)}` : ''}`, 'Перейти ко входу', 'btn outline')}</section>`, bind() {
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
  return { title: 'Подтверждение почты', html: `<section class="account"><h1>Подтверждение почты</h1><p id="confirmation-status" role="status">${success ? 'Почта подтверждена. Войдите с вашим паролем.' : token ? 'Нажмите кнопку, чтобы подтвердить электронную почту.' : 'Ссылка недействительна. Запросите новое письмо.'}</p>${token ? '<form id="confirm-email-form"><button class="btn" type="submit">Подтвердить почту</button></form>' : ''}<div class="actions">${link('/login', 'Войти', 'btn outline')}${link('/check-email', 'Получить новое письмо', 'btn outline')}</div></section>`, bind() {
    const form = document.querySelector('#confirm-email-form');
    if (!form) return;
    form.onsubmit = async event => {
      event.preventDefault();
      if (form.getAttribute('aria-busy') === 'true') return;
      busy(form, true);
      try {
        await ctx.auth.confirmEmail(token);
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

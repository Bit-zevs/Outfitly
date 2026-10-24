import { ApiError } from './api.js';
import { passwordConfirmationError } from './auth.js';
import { escape, field, link, title } from './components.js';
import { profilePath } from './router.js';
import { copyAddress } from './profiles.js';
import { busy, showError } from './ui.js';

function identity(user) {
  return `<h2>${escape(user.displayName)}</h2><p>${escape(user.email)}</p><p>${link(profilePath(user.username), `@${user.username}`)}</p><div class="actions">${link(profilePath(user.username), 'Открыть мой профиль', 'btn outline')}<button class="btn outline" id="copy-profile">Копировать ссылку</button></div>`;
}

const notice = id => `<p class="error" id="${id}-error" role="alert" tabindex="-1" hidden></p><p id="${id}-status" role="status"></p>`;
const passwordField = (label, name, autocomplete = 'current-password') => field(label, name, '', { type: 'password', required: true, autocomplete, max: 128 });

export function accountSettings(ctx) {
  const user = ctx.auth.user;
  return { title: 'Мой аккаунт', html: `<section class="account">${title('Личные данные', 'Мой аккаунт', 'Управляйте профилем и данными для входа.')}
    <div class="account-panel" id="account-identity">${identity(user)}</div>
    <section class="account-panel"><h2>Имя и @username</h2><form id="profile-form">${field('Отображаемое имя', 'displayName', user.displayName, { required: true, autocomplete: 'name' })}${field('@username', 'username', user.username, { required: true, max: 31, autocomplete: 'username', hint: '3–30 символов: латиница, цифры и _. Начните с буквы. После смены ника старые ссылки с ним перестанут работать.' })}${notice('profile')}<button class="btn" type="submit">Сохранить профиль</button></form></section>
    <section class="account-panel"><h2>Электронная почта</h2><p>Текущий адрес: <span id="current-email">${escape(user.email)}</span></p><p>До подтверждения нового адреса работает старый. После подтверждения потребуется войти снова на всех устройствах.</p><p id="pending-email" role="status"></p><form id="email-form">${field('Новый email', 'email', user.pendingEmail || '', { type: 'email', required: true, autocomplete: 'email', max: 254 })}${passwordField('Текущий пароль', 'emailCurrentPassword')}${notice('email')}<button class="btn" type="submit">Отправить подтверждение</button></form><button class="btn outline" id="cancel-email-change" hidden>Отменить смену почты</button></section>
    <section class="account-panel"><h2>Пароль</h2><p>После смены пароля потребуется войти снова на всех устройствах.</p><form id="password-form">${passwordField('Текущий пароль', 'currentPassword')}${passwordField('Новый пароль', 'newPassword', 'new-password')}${passwordField('Повторите новый пароль', 'passwordConfirmation', 'new-password')}<label class="check-label"><input type="checkbox" id="show-settings-passwords">Показать пароли</label>${notice('password')}<button class="btn" type="submit">Изменить пароль</button></form></section>
    <div class="account-panel"><p class="error" id="logout-error" role="alert" tabindex="-1" hidden></p><button class="btn outline danger" id="logout">Выйти из аккаунта ↗</button></div></section>`, bind() {
      const dirty = new Set();
      let saving = false;
      const forms = ['profile', 'email', 'password'].map(id => document.querySelector(`#${id}-form`));
      const markClean = form => { dirty.delete(form); ctx.setDirty?.(dirty.size > 0); };
      for (const form of forms) form.oninput = () => { dirty.add(form); ctx.setDirty?.(true); };

      function updateIdentity() {
        document.querySelector('#account-identity').innerHTML = identity(ctx.auth.user);
        document.querySelector('#copy-profile').onclick = () => copyAddress(`${location.origin}/#${profilePath(ctx.auth.user.username)}`, ctx);
        const pending = ctx.auth.user.pendingEmail;
        document.querySelector('#pending-email').textContent = pending ? `Ожидает подтверждения: ${pending}. Ссылка действует 24 часа. Для повторной отправки введите текущий пароль.` : '';
        document.querySelector('#cancel-email-change').hidden = !pending;
        ctx.updateHeader?.();
      }
      updateIdentity();

      async function submit(id, action) {
        if (saving) { ctx.toast('Дождитесь окончания сохранения.'); return; }
        const form = document.querySelector(`#${id}-form`);
        saving = true; busy(form, true); ctx.setSaving?.(true);
        document.querySelector(`#${id}-error`).hidden = true;
        document.querySelector(`#${id}-status`).textContent = '';
        try { await action(form); }
        catch (error) {
          if (!ctx.isCurrent()) return;
          const text = { invalid_current_password: 'Текущий пароль неверен.', username_taken: 'Этот @username уже занят. Выберите другой.', email_taken: 'Этот email уже используется.', confirmation_too_frequent: 'Подождите минуту перед повторной отправкой.' }[error.problem?.code];
          const el = document.querySelector(`#${id}-error`);
          if (text) { el.hidden = false; el.textContent = text; el.focus(); }
          else showError(el, error);
        } finally {
          form.querySelectorAll('[autocomplete="current-password"]').forEach(input => { input.value = ''; });
          if (id === 'password') form.querySelectorAll('[autocomplete="new-password"]').forEach(input => { input.value = ''; });
          busy(form, false); saving = false; ctx.setSaving?.(false);
        }
      }

      forms[0].onsubmit = event => {
        event.preventDefault();
        const form = forms[0];
        return submit('profile', async () => {
          const updated = await ctx.auth.changeProfile(form.elements.displayName.value, form.elements.username.value);
          if (!ctx.isCurrent()) return;
          form.elements.displayName.value = updated.displayName; form.elements.username.value = updated.username;
          markClean(form); updateIdentity(); document.querySelector('#profile-status').textContent = 'Профиль сохранён.';
        });
      };
      forms[1].onsubmit = event => {
        event.preventDefault();
        const form = forms[1];
        return submit('email', async () => {
          const result = await ctx.auth.requestEmailChange(form.elements.email.value, form.elements.emailCurrentPassword.value);
          if (!ctx.isCurrent()) return;
          form.elements.email.value = result.pendingEmail;
          markClean(form); updateIdentity();
          document.querySelector('#email-status').textContent = result.confirmationEmailSent ? 'Письмо отправлено на новый адрес. Подтвердите его по ссылке.' : 'Запрос сохранён, но письмо отправить не удалось. Повторите отправку через минуту.';
        });
      };
      forms[2].onsubmit = event => {
        event.preventDefault();
        const form = forms[2];
        const message = passwordConfirmationError(form.elements.newPassword.value, form.elements.passwordConfirmation.value);
        if (message) { showError(document.querySelector('#password-error'), new ApiError(400), message + ' '); return; }
        return submit('password', async () => {
          await ctx.auth.changePassword(form.elements.currentPassword.value, form.elements.newPassword.value);
          if (!ctx.isCurrent()) return;
          dirty.clear(); ctx.setDirty?.(false); ctx.toast('Пароль изменён. Войдите снова.'); ctx.navigate('/login');
        });
      };
      document.querySelector('#show-settings-passwords').onchange = event => {
        forms[2].querySelectorAll('input[autocomplete]').forEach(input => { input.type = event.target.checked ? 'text' : 'password'; });
      };
      document.querySelector('#cancel-email-change').onclick = () => submit('email', async () => {
        await ctx.auth.cancelEmailChange();
        if (ctx.isCurrent()) { updateIdentity(); document.querySelector('#email-status').textContent = 'Смена почты отменена.'; }
      });
      document.querySelector('#logout').onclick = async event => {
        if (saving) return;
        const button = event.currentTarget; button.disabled = true;
        try { await ctx.auth.logout(); if (ctx.isCurrent()) { ctx.setDirty?.(false); ctx.navigate('/'); } }
        catch (error) { if (ctx.isCurrent()) showError(document.querySelector('#logout-error'), error); }
        finally { button.disabled = false; }
      };
    } };
}

export function confirmEmailChangePage(ctx) {
  const token = ctx.route.query.get('token');
  const success = ctx.route.query.get('confirmed') === '1';
  return { title: 'Смена электронной почты', html: `<section class="account"><h1>Смена электронной почты</h1><p id="email-change-status" role="status">${success ? 'Адрес изменён. Все сессии аккаунта завершены. Войдите с новой почтой и вашим паролем.' : token ? 'Нажмите кнопку, чтобы подтвердить новый адрес и завершить все сессии аккаунта.' : 'Ссылка недействительна. Запросите новое письмо в аккаунте.'}</p>${token ? '<form id="confirm-email-change-form"><button class="btn" type="submit">Подтвердить новый адрес</button></form>' : ''}<div class="actions">${link('/login', 'Войти', 'btn outline')}${link('/account', 'Настройки аккаунта', 'btn outline')}</div></section>`, bind() {
    const form = document.querySelector('#confirm-email-change-form');
    if (!form) return;
    form.onsubmit = async event => {
      event.preventDefault();
      if (form.getAttribute('aria-busy') === 'true') return;
      busy(form, true);
      try {
        await ctx.auth.confirmEmailChange(token);
        if (ctx.isCurrent()) ctx.navigate('/confirm-email-change?confirmed=1', true);
      } catch (error) {
        if (!ctx.isCurrent()) return;
        const message = { invalid_email_change_token: 'Ссылка недействительна, уже использована или срок её действия истёк. Запросите новое письмо в аккаунте.', email_taken: 'Этот адрес уже занят. Выберите другой email в настройках аккаунта.' }[error.problem?.code];
        document.querySelector('#email-change-status').textContent = message || (error.status === 409 ? 'Запрос изменился. Используйте последнее письмо или повторите действие.' : error.status === 429 ? 'Слишком много попыток. Подождите минуту.' : 'Не удалось подтвердить адрес. Попробуйте позже.');
      } finally { busy(form, false); }
    };
  } };
}

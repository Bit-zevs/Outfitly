import { ApiError } from './api.js';

export function passwordConfirmationError(password, confirmation) {
  return password && password === confirmation ? '' : 'Пароли не совпадают.';
}

export function createAuth(api) {
  let user = null;
  return {
    get user() { return user; },
    clear() { user = null; api.resetCsrf(); },
    async restore({ signal } = {}) {
      try { user = await api.request('/auth/me', { anonymous: true, signal }); }
      catch (error) { if (error.status !== 401) throw error; user = null; }
      return user;
    },
    async login(email, password) {
      user = await api.request('/auth/login', { method: 'POST', body: { email, password }, anonymous: true });
      api.resetCsrf();
      return user;
    },
    async register(displayName, email, password, confirmation) {
      const mismatch = passwordConfirmationError(password, confirmation);
      if (mismatch) throw new ApiError(400, { detail: mismatch });
      return api.request('/auth/register', { method: 'POST', body: { displayName, email, password }, anonymous: true });
    },
    async logout() {
      await api.request('/auth/logout', { method: 'POST' });
      user = null;
      api.resetCsrf();
    },
    confirmEmail(token) {
      return api.request('/auth/confirm-email', { method: 'POST', body: { token }, anonymous: true });
    },
    forgotPassword(email) {
      return api.request('/auth/forgot-password', { method: 'POST', body: { email }, anonymous: true });
    },
    resetPassword(token, password) {
      return api.request('/auth/reset-password', { method: 'POST', body: { token, password }, anonymous: true });
    },
    resendConfirmation(email) {
      return api.request('/auth/resend-confirmation', { method: 'POST', body: { email }, anonymous: true });
    },
  };
}

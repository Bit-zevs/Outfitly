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
    async register(displayName, email, password, confirmation, username) {
      const mismatch = passwordConfirmationError(password, confirmation);
      if (mismatch) throw new ApiError(400, { detail: mismatch });
      return api.request('/auth/register', { method: 'POST', body: { displayName, email, password, username }, anonymous: true });
    },
    async changeUsername(username) {
      user = await api.request('/auth/me/username', { method: 'PATCH', body: { username } });
      return user;
    },
    async changeProfile(displayName, username) {
      user = await api.request('/auth/me/profile', { method: 'PATCH', body: { displayName, username } });
      return user;
    },
    async changePassword(currentPassword, newPassword) {
      await api.request('/auth/me/change-password', { method: 'POST', body: { currentPassword, newPassword } });
      user = null; api.resetCsrf();
    },
    async requestEmailChange(email, currentPassword) {
      const result = await api.request('/auth/me/email-change', { method: 'POST', body: { email, currentPassword } });
      if (user) user = { ...user, pendingEmail: result.pendingEmail };
      return result;
    },
    async cancelEmailChange() {
      await api.request('/auth/me/email-change', { method: 'DELETE' });
      if (user) user = { ...user, pendingEmail: null };
    },
    async confirmEmailChange(token) {
      await api.request('/auth/confirm-email-change', { method: 'POST', body: { token }, anonymous: true });
      api.resetCsrf();
      // Confirmation can revoke this browser's session, including after navigation away.
      try { user = await api.request('/auth/me', { anonymous: true }); }
      catch { user = null; }
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

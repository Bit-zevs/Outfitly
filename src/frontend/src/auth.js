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
    async register(displayName, email, password) {
      return api.request('/auth/register', { method: 'POST', body: { displayName, email, password }, anonymous: true });
    },
    async logout() {
      await api.request('/auth/logout', { method: 'POST' });
      user = null;
      api.resetCsrf();
    },
  };
}

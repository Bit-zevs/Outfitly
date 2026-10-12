export class ApiError extends Error {
  constructor(status, problem = {}) {
    super(problem.detail || problem.title || 'Не удалось выполнить запрос.');
    this.status = status;
    this.problem = problem;
  }
}

export function createApi(fetcher = globalThis.fetch.bind(globalThis), onUnauthorized = () => {}) {
  let csrf;
  let csrfPending;
  async function request(path, { method = 'GET', body, signal, anonymous = false } = {}) {
    const headers = { Accept: 'application/json' };
    if (!['GET', 'HEAD'].includes(method)) {
      headers['X-CSRF-TOKEN'] = await token();
      if (body !== undefined && !(body instanceof FormData)) {
        headers['Content-Type'] = 'application/json';
        body = JSON.stringify(body);
      }
    }
    const response = await fetcher(`/api${path}`, { method, body, headers, credentials: 'same-origin', signal, cache: 'no-store' });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      if (response.status === 401 && !anonymous) onUnauthorized();
      throw new ApiError(response.status, problem);
    }
    return response.status === 204 ? undefined : response.json();
  }
  async function token() {
    if (csrf) return csrf;
    csrfPending ??= request('/auth/csrf', { anonymous: true }).then(result => { csrf = result.token; return csrf; }).finally(() => { csrfPending = undefined; });
    return csrfPending;
  }
  return { request, resetCsrf() { csrf = undefined; }, token };
}

export function errorMessage(error) {
  if (!(error instanceof ApiError)) return 'Не удалось связаться с сервером. Проверьте подключение и попробуйте ещё раз.';
  return ({
    400: 'Проверьте введённые данные. Если они верны, обновите страницу и повторите действие.',
    401: 'Сессия завершилась. Войдите снова.',
    403: 'Нет доступа к этому ресурсу.',
    404: 'Ресурс больше недоступен.',
    409: 'Данные изменились или действие сейчас невозможно. Сверьте актуальное состояние перед повторением.',
    413: 'Фотография слишком большая. Максимум — 5 МБ.',
    415: 'Не удалось прочитать фото. Выберите обычное изображение JPEG или PNG.',
    429: 'Слишком много попыток. Подождите минуту и повторите.',
  })[error.status] || 'Ошибка сервера. Попробуйте позже.';
}

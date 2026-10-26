export function parseRoute(hash = '') {
  const [pathname, query = ''] = hash.replace(/^#/, '').split('?');
  const path = pathname || '/';
  return { path, parts: path.split('/').filter(Boolean), query: new URLSearchParams(query) };
}
export function catalogQuery(query) {
  const result = new URLSearchParams();
  for (const key of ['search', 'category']) if (query.get(key)) result.set(key, query.get(key));
  result.set('sort', query.get('sort') === 'nameDesc' ? 'nameDesc' : 'nameAsc');
  result.set('page', String(Math.max(1, Number.parseInt(query.get('page'), 10) || 1)));
  result.set('pageSize', '20');
  return result;
}
export function changedFilter(route, key, value) {
  const query = new URLSearchParams(route.query);
  value ? query.set(key, value) : query.delete(key);
  query.delete('page');
  return `${route.path}?${query}`;
}
export function privateRoute(path) {
  return /^\/(dashboard|wardrobe|items|outfits|account|favorites|drafts)(\/|$)/.test(path);
}
export function safeReturn(value) {
  return value && privateRoute(value.split('?')[0]) ? value : '/dashboard';
}
export function shareAddress(origin, kind, token) {
  return `${origin}/#/shared/${kind}/${encodeURIComponent(token)}`;
}

export function profilePath(username) {
  return `/@${encodeURIComponent(username)}`;
}

export function publicPath(username, kind, id) {
  return `${profilePath(username)}/${kind}/${encodeURIComponent(id)}`;
}

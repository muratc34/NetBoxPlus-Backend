// Shared by the k6 scenarios: addresses, sessions through the gateway, and the demo catalog.
import http from 'k6/http';
import { check, fail } from 'k6';

export const BASE = __ENV.BASE_URL || 'http://localhost:5100';
export const USERS = Number(__ENV.LOAD_USERS || 20);
export const PASSWORD = __ENV.LOAD_USER_PASSWORD || '';
export const RUN = __ENV.LOAD_RUN || `${Date.now()}`;

const PUBLIC_STORAGE = (__ENV.PUBLIC_STORAGE_ENDPOINT || '').replace(/\/$/, '');
const STORAGE = (__ENV.STORAGE_ENDPOINT || '').replace(/\/$/, '');
const JSON_HEADERS = { 'Content-Type': 'application/json' };

/** The prepared, subscribed accounts: load-user-01 ... load-user-NN. */
export function userEmail(index) {
  return `load-user-${String(index).padStart(2, '0')}@load.netboxplus.test`;
}

/** A storage address handed out for browsers, made reachable from inside the compose network. */
export function reachable(url) {
  return PUBLIC_STORAGE && STORAGE && url.startsWith(PUBLIC_STORAGE + '/') ? STORAGE + url.slice(PUBLIC_STORAGE.length) : url;
}

/**
 * Request parameters: a request name for the metrics (never the address with its ids), and an
 * empty cookie jar, so only the session the caller passes is sent.
 */
export function params(name, session, extraHeaders) {
  const headers = Object.assign({}, JSON_HEADERS, extraHeaders || {});
  if (session) {
    headers.Cookie = session.cookie;
    headers['X-CSRF-Token'] = session.csrf;
  }
  return { headers, tags: { name }, jar: new http.CookieJar() };
}

/** Signs in through the gateway like the web app; null when the address or password is wrong. */
export function signIn(email, password) {
  const response = http.post(`${BASE}/session/sign-in`, JSON.stringify({ email, password }), params('POST /session/sign-in'));
  if (response.status !== 200) {
    return null;
  }

  const cookie = response.cookies['__Host-nbp-session'];
  if (!cookie || cookie.length === 0) {
    fail('The sign-in answered 200 without a session cookie.');
  }

  return { cookie: `__Host-nbp-session=${cookie[0].value}`, csrf: response.json('csrfToken'), userId: response.json('userId') };
}

export function register(email, password) {
  return http.post(`${BASE}/session/register`, JSON.stringify({ email, password }), params('POST /session/register'));
}

/** The published demo titles, read once in setup. */
export function catalog() {
  const response = http.get(`${BASE}/catalog/titles?pageSize=100`, params('GET /catalog/titles'));
  check(response, { 'catalog answers': r => r.status === 200 }) || fail(`The catalog answered ${response.status}.`);
  const items = response.json('items');
  const movies = items.filter(item => item.kind === 'Movie').map(item => ({ id: item.id, title: item.title }));
  const series = items.filter(item => item.kind === 'Series').map(item => ({ id: item.id, title: item.title }));
  if (movies.length === 0) {
    fail('No published movies: run ./deploy/dev.ps1 seed first.');
  }

  return { movies, series };
}

export function pick(items) {
  return items[Math.floor(Math.random() * items.length)];
}

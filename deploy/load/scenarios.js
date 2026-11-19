// The three kinds of traffic; browse.js, watch.js and mixed.js run them alone or together.
import http from 'k6/http';
import { check, fail, sleep } from 'k6';
import { Counter } from 'k6/metrics';
import { BASE, PASSWORD, RUN, USERS, params, pick, reachable, register, signIn, userEmail } from './lib.js';

const registrations = new Counter('registrations');
const playbackRefused = new Counter('playback_refused');

/** Someone looking around the catalog without an account. */
export function browse(data) {
  const lang = Math.random() < 0.3 ? 'en' : 'tr';
  const list = http.get(`${BASE}/catalog/titles?lang=${lang}&pageSize=24`, params('GET /catalog/titles'));
  check(list, { 'titles 200': r => r.status === 200 });
  sleep(1 + Math.random());

  const isMovie = Math.random() < 0.7 || data.series.length === 0;
  const title = isMovie ? pick(data.movies) : pick(data.series);
  const details = isMovie
    ? http.get(`${BASE}/catalog/movies/${title.id}?lang=${lang}`, params('GET /catalog/movies/{id}'))
    : http.get(`${BASE}/catalog/series/${title.id}?lang=${lang}`, params('GET /catalog/series/{id}'));
  check(details, { 'details 200': r => r.status === 200 });
  sleep(1 + Math.random() * 2);

  const word = encodeURIComponent(title.title.split(' ')[0]);
  const search = http.get(`${BASE}/catalog/search?q=${word}&lang=${lang}`, params('GET /catalog/search'));
  check(search, { 'search 200': r => r.status === 200 });
  sleep(1 + Math.random() * 2);
}

/**
 * The published movies one can actually watch, tried once in setup with the first prepared account:
 * older local data may hold movies published before a video was required, which streaming refuses.
 */
export function playable(data) {
  const session = signIn(userEmail(1), PASSWORD);
  if (!session) {
    return Object.assign({}, data, { watchable: [] });
  }

  const profileId = http.get(`${BASE}/auth/profiles`, params('GET /auth/profiles', session)).json('0.id');
  const watchable = data.movies.filter(movie => {
    const start = http.post(`${BASE}/streaming/playback-sessions`, JSON.stringify({ profileId, contentId: movie.id }), params('POST /streaming/playback-sessions', session));
    if (start.status !== 201 && start.status !== 200) {
      return false;
    }

    http.del(`${BASE}/streaming/playback-sessions/${start.json('sessionId')}`, null, params('DELETE playback-session', session));
    return true;
  });
  return Object.assign({}, data, { watchable });
}

// Per virtual user: its account's session and main profile, made on its first iteration.
let viewer = null;

/** A subscriber watching a movie for about a minute and a half, as the player does. */
export function watch(data) {
  if (data.watchable.length === 0) {
    fail('No movie can be watched: run ./deploy/dev.ps1 load -Scenario prepare (and seed) first.');
  }

  if (!viewer) {
    const email = userEmail(((__VU - 1) % USERS) + 1);
    const session = signIn(email, PASSWORD);
    if (!check(session, { 'load user signs in': s => s !== null })) {
      sleep(10);
      return;
    }

    const profiles = http.get(`${BASE}/auth/profiles`, params('GET /auth/profiles', session));
    viewer = { session, profileId: profiles.json('0.id') };
  }

  const movie = pick(data.watchable);
  const start = http.post(
    `${BASE}/streaming/playback-sessions`,
    JSON.stringify({ profileId: viewer.profileId, contentId: movie.id }),
    params('POST /streaming/playback-sessions', viewer.session));
  if (!check(start, { 'playback starts': r => r.status === 201 || r.status === 200 })) {
    playbackRefused.add(1, { code: start.status === 0 ? 'none' : String(start.json('code') || start.status) });
    sleep(5);
    return;
  }

  const started = start.json();
  const master = http.get(`${BASE}/streaming/${started.playlist}`, params('GET master.m3u8'));
  check(master, { 'master playlist 200': r => r.status === 200 });
  const renditions = master.body.split('\n').filter(line => line && !line.startsWith('#'));
  const folder = `${BASE}/streaming/${started.playlist.split('?')[0].replace(/master\.m3u8$/, '')}`;
  const rendition = http.get(folder + pick(renditions), params('GET index.m3u8'));
  check(rendition, { 'rendition playlist 200': r => r.status === 200 });

  const segments = rendition.body.split('\n').filter(line => line.startsWith('http'));
  for (const segment of segments.slice(0, 3)) {
    const file = http.get(reachable(segment), Object.assign(params('GET segment'), { headers: {} }));
    check(file, { 'segment 200': r => r.status === 200 });
  }

  for (let beat = 1; beat <= 3; beat++) {
    sleep(started.heartbeatSeconds || 30);
    const heartbeat = http.post(
      `${BASE}/streaming/playback-sessions/${started.sessionId}/heartbeat`,
      JSON.stringify({ positionSeconds: beat * 30 }),
      params('POST heartbeat', viewer.session));
    check(heartbeat, { 'heartbeat 2xx': r => r.status >= 200 && r.status < 300 });
  }

  const end = http.del(`${BASE}/streaming/playback-sessions/${started.sessionId}?positionSeconds=90`, null, params('DELETE playback-session', viewer.session));
  check(end, { 'playback ends': r => r.status === 204 });
}

/** A new account: the event that sets billing, notification and streaming to work. */
export function signUp() {
  const email = `load-r-${RUN}-${__VU}-${__ITER}@load.netboxplus.test`;
  const response = register(email, PASSWORD || 'Load-test password 1!');
  if (check(response, { 'registration 201': r => r.status === 201 })) {
    registrations.add(1);
  }
}

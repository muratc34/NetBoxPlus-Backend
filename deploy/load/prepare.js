// Makes the accounts the watch scenario signs in with, each with a Premium subscription, as a
// customer would end up after checkout: ./deploy/dev.ps1 load -Scenario prepare
// Stripe test mode only. The subscription reaches billing through Stripe's webhook, which the
// Stripe CLI forwards while this runs (dev.ps1 starts it). Accounts that already have an active
// subscription are left alone, so running it again costs nothing.
import http from 'k6/http';
import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { BASE, PASSWORD, USERS, catalog, params, register, signIn, userEmail } from './lib.js';

const STRIPE = 'https://api.stripe.com/v1';
const KEY = __ENV.STRIPE_SECRET_KEY || '';

export const options = {
  scenarios: { prepare: { executor: 'shared-iterations', vus: 1, iterations: 1, maxDuration: '30m' } },
};

export default function () {
  if (!PASSWORD) {
    abort('LOAD_USER_PASSWORD is not set in deploy/.env.');
  }
  if (!KEY.startsWith('sk_test_') && !KEY.startsWith('rk_test_')) {
    abort('A test-mode STRIPE_SECRET_KEY is required.');
  }

  const { movies } = catalog();
  let premium = null;

  for (let index = 1; index <= USERS; index++) {
    const email = userEmail(index);
    let session = signIn(email, PASSWORD);
    if (!session) {
      const registered = register(email, PASSWORD);
      check(registered, { 'registered or already there': r => r.status === 201 || r.status === 409 });
      session = signIn(email, PASSWORD) || abort(`${email} cannot sign in.`);
    }

    if (!hasPremium(session)) {
      premium = premium || findPremium();
      subscribe(session, email, premium);
      waitUntil(() => hasPremium(session), `${email}: the subscription did not reach billing. Is stripe listen forwarding?`);
    }

    // Streaming learns of the subscription and the profile through events; wait until it lets the account watch.
    const profileId = http.get(`${BASE}/auth/profiles`, params('GET /auth/profiles', session)).json('0.id');
    waitUntil(() => canWatch(session, profileId, movies[0].id), `${email}: streaming does not let the account watch yet.`);
    console.log(`${email} ready`);
  }
}

function hasPremium(session) {
  const response = http.get(`${BASE}/billing/subscription`, params('GET /billing/subscription', session));
  return response.status === 200 && response.json('status') === 'Active' && response.json('plan.quality') === 'Hd';
}

/** The billing plan sold with the demo Premium price, and that price. */
function findPremium() {
  const plans = http.get(`${BASE}/billing/plans`, params('GET /billing/plans'));
  const plan = (plans.json() || []).find(item => item.quality === 'Hd');
  if (!plan) {
    abort('No Hd plan on sale: run ./deploy/dev.ps1 seed with a Stripe key first.');
  }

  const prices = stripe('GET', 'prices?lookup_keys[]=netboxplus-demo-premium&active=true', null, null);
  const price = prices.json('data.0.id') || abort('The Stripe price netboxplus-demo-premium is missing: run ./deploy/dev.ps1 seed.');
  return { planId: plan.id, priceId: price };
}

/** What checkout would leave behind: a Stripe customer with a card and a subscription, tagged with the user. */
function subscribe(session, email, premium) {
  const key = `netboxplus-load-${session.userId}`;
  const customer = stripe('POST', 'customers', { email, 'metadata[userId]': session.userId }, `${key}-customer`).json('id');
  const method = stripe('POST', 'payment_methods/pm_card_visa/attach', { customer }, `${key}-card`).json('id');
  stripe('POST', `customers/${customer}`, { 'invoice_settings[default_payment_method]': method }, `${key}-default`);
  const subscription = stripe('POST', 'subscriptions', {
    customer,
    'items[0][price]': premium.priceId,
    'metadata[userId]': session.userId,
    'metadata[planId]': premium.planId,
  }, `${key}-subscription`).json('id');

  // A repeated run gets the same subscription back from the idempotency key, and no new event; a
  // change to it sends one, so billing (which reads the subscription from Stripe) catches up either way.
  stripe('POST', `subscriptions/${subscription}`, { 'metadata[loadPreparedAt]': `${Date.now()}` }, null);
}

function canWatch(session, profileId, contentId) {
  const start = http.post(`${BASE}/streaming/playback-sessions`, JSON.stringify({ profileId, contentId }), params('POST /streaming/playback-sessions', session));
  if (start.status !== 201 && start.status !== 200) {
    return false;
  }

  http.del(`${BASE}/streaming/playback-sessions/${start.json('sessionId')}`, null, params('DELETE playback-session', session));
  return true;
}

function stripe(method, path, form, idempotencyKey) {
  const headers = { Authorization: `Bearer ${KEY}` };
  if (idempotencyKey) {
    headers['Idempotency-Key'] = idempotencyKey;
  }

  const response = http.request(method, `${STRIPE}/${path}`, form, { headers, tags: { name: `stripe ${method}` } });
  if (response.status >= 300) {
    abort(`Stripe ${method} ${path.split('?')[0]} answered ${response.status}: ${response.json('error.message')}`);
  }

  return response;
}

function waitUntil(condition, message) {
  for (let attempt = 0; attempt < 60; attempt++) {
    if (condition()) {
      return;
    }

    sleep(2);
  }

  abort(message);
}

/** Stops the whole run with a non-zero exit code; a thrown error would only end the iteration. */
function abort(message) {
  exec.test.abort(message);
}

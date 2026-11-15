// Browsing, watching and signing up at once, as the baseline and for the outage test:
// ./deploy/dev.ps1 load (needs ./deploy/dev.ps1 load -Scenario prepare once, for the watchers).
import { catalog, USERS } from './lib.js';
import { browse, playable, signUp, watch } from './scenarios.js';

export const options = {
  scenarios: {
    browse: {
      executor: 'ramping-vus',
      exec: 'browse',
      stages: [
        { duration: '1m', target: 10 },
        { duration: '2m', target: 50 },
        { duration: '2m', target: 100 },
        { duration: '3m', target: 100 },
        { duration: '30s', target: 0 },
      ],
    },
    watch: { executor: 'constant-vus', exec: 'watch', vus: USERS, duration: '8m30s' },
    signUp: {
      executor: 'constant-arrival-rate',
      exec: 'signUp',
      rate: 1,
      timeUnit: '1s',
      duration: '8m',
      preAllocatedVUs: 5,
      maxVUs: 20,
    },
  },
};

export function setup() {
  return playable(catalog());
}

export { browse, signUp, watch };

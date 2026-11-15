// Subscribers watching, one per prepared account: ./deploy/dev.ps1 load -Scenario watch
// Needs ./deploy/dev.ps1 load -Scenario prepare once.
import { catalog, USERS } from './lib.js';
import { playable, watch } from './scenarios.js';

export const options = {
  scenarios: {
    watch: { executor: 'constant-vus', exec: 'watch', vus: USERS, duration: '6m' },
  },
};

export function setup() {
  return playable(catalog());
}

export { watch };

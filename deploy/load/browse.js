// Anonymous catalog browsing, rising to 100 virtual users: ./deploy/dev.ps1 load -Scenario browse
import { catalog } from './lib.js';
import { browse } from './scenarios.js';

export const options = {
  scenarios: {
    browse: {
      executor: 'ramping-vus',
      exec: 'browse',
      stages: [
        { duration: '1m', target: 10 },
        { duration: '2m', target: 50 },
        { duration: '2m', target: 100 },
        { duration: '2m', target: 100 },
        { duration: '30s', target: 0 },
      ],
    },
  },
};

export function setup() {
  return catalog();
}

export { browse };

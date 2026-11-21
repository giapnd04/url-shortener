// k6 load test for GET /{code} (US-04).
//
// Workload, fixed so runs are comparable:
//   80% of requests -> "hot" links   (seed index 1..20000, i.e. 20% of the 100k links)
//   15% of requests -> "cold" links  (seed index 20001..100000)
//    5% of requests -> codes that do not exist (seed index > 100000) -> 404
// Constant VUs for a fixed duration; warm-up is a separate short run done by run-baseline.ps1.
//
// Env: BASE_URL (default http://api:8080), VUS (default 20), DURATION (default 60s).
import http from 'k6/http';
import { check } from 'k6';

const ALPHABET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';
const MOD = 3521614606208;   // 62^7
const MULT = 1000000007;     // same as benchmarks/sql/seed.sql
const LINK_COUNT = 100000;
const HOT_COUNT = 20000;

const BASE_URL = __ENV.BASE_URL || 'http://api:8080';

export const options = {
  scenarios: {
    redirect: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 20),
      duration: __ENV.DURATION || '60s',
    },
  },
  // Percentiles reported in the summary (p99 is not in the k6 default list).
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  // A redirect is the answer we want to time, not something to follow.
  maxRedirects: 0,
  discardResponseBodies: true,
};

// 302 for existing links and 404 for missing ones are both "expected"; anything else is an error.
http.setResponseCallback(http.expectedStatuses(302, 404));

// Must match the T-SQL in seed.sql exactly. n * MULT stays below 2^53 for n < 9e6, so Number is exact.
export function codeFor(n) {
  let x = (n * MULT) % MOD;
  let code = '';
  for (let i = 0; i < 7; i++) {
    code = ALPHABET[x % 62] + code;
    x = Math.floor(x / 62);
  }
  return code;
}

function randInt(min, max) {
  return min + Math.floor(Math.random() * (max - min + 1));
}

export default function () {
  const r = Math.random();
  let n;
  let kind;
  if (r < 0.80) {
    n = randInt(1, HOT_COUNT);
    kind = 'hot';
  } else if (r < 0.95) {
    n = randInt(HOT_COUNT + 1, LINK_COUNT);
    kind = 'cold';
  } else {
    n = randInt(LINK_COUNT + 1, LINK_COUNT * 2);
    kind = 'missing';
  }

  const res = http.get(`${BASE_URL}/${codeFor(n)}`, { tags: { kind, name: 'GET /{code}' } });

  check(res, {
    'status is expected': (x) => (kind === 'missing' ? x.status === 404 : x.status === 302),
  });
}

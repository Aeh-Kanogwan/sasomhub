# E2E tests (Playwright)

End-to-end browser tests for the Marketplace web app (`src/Marketplace.Web`, "Neon Vault").
Runs **headed by default** so you can watch the browser drive the app.

## Prerequisites
- Node.js 18+ (tested on v24)
- .NET 8 SDK + the local **SQL Server** `MarketplaceDb` already set up
  (see [`../docs/BUILD_RUN.md`](../docs/BUILD_RUN.md))

## Install (first time)
```bash
cd e2e
npm install
npx playwright install chromium
```

## Run
```bash
npm test            # headed (browser visible) — config default
npm run test:ui     # Playwright UI mode (step through, time-travel)
npm run report      # open the last HTML report
npm run codegen     # record selectors against the running app
```

The config's `webServer` block **auto-starts** `Marketplace.Web` on
`http://localhost:5080` (wiring `MARKETPLACE_CONNECTION` to the local SQL Server)
if it isn't already running, and reuses it if it is.

## What is covered
| Spec | Flow |
|---|---|
| `01-smoke.spec.ts` | Guest browsing: home, explore, membership pricing, login form |
| `02-auth.spec.ts` | Register (instant trial) → logout → login; admin RBAC nav |
| `03-membership-payment.spec.ts` | **Flow B**: member renew → upload slip → admin approve → membership confirmed (across two browser contexts) |

## Notes
- `workers: 1` — one window, and avoids races on the shared admin review queue.
- The admin account (`admin@neonvault.test` / `Admin@12345`) is seeded by
  `DevAdminSeeder` on Web startup **in Development only**.
- Each run registers a fresh member (`e2e_<timestamp>@neonvault.test`) so reruns don't collide.
- On CI (`CI=1`): headless, retries, no slowMo.

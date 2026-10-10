# Saakh

**Saakh turns a small vendor's trading history into a portable trust record a brand-new counterparty can act on.**

Unorganized vendors build years of reliable trading relationships with specific suppliers and financiers, but that trust is illiquid — it cannot be shown to a *new* counterparty. Formal credit bureaus require a registered credit file most informal vendors were never in; lending-facing infrastructure like OCEN scores a vendor for a *bank*, not for a fellow trader. Saakh sits in that gap: a vendor-controlled record of trading reliability, built from deals that actually settled, that a new trading partner can check before extending credit.

Saakh never holds, moves or guarantees money or material. It is a discovery, agreement-tracking and trust layer; the transfer happens off-platform between the two parties.

---

## Contents

- [What is built](#what-is-built)
- [Stack](#stack)
- [Running locally](#running-locally)
  - [With Docker Compose](#option-a--docker-compose-the-primary-path)
  - [Without Docker](#option-b--without-docker)
- [Deploying](#deploying)
- [Demo accounts](#demo-accounts)
- [Walking through the product](#walking-through-the-product)
- [Business rules worth knowing](#business-rules-worth-knowing)
- [Design system](#design-system)
- [Mapping](#mapping)
- [Tests](#tests)
- [Project layout](#project-layout)
- [Configuration](#configuration)
- [Verification](#verification)
- [Known limits](#known-limits)

---

## What is built

A working end-to-end implementation of the v1 scope in `spec.md`.

**Agreeing a deal.** A ticket proposes terms rather than creating a deal. The other party agrees them, or answers with amended terms that supersede them and pass the turn back; the deal is created, in Open, only once both sides have agreed. This exists because of the settlement date: the platform acts on that date by itself, so a date only one party chose must not be able to bind the other.

**Settlement the platform enforces.** When the agreed date passes, both parties are warned once. If the deal is still unsettled after a grace period of seven days, the platform closes it and records the closure on both trust records. It is not a halt — nobody is marked as having triggered it — and no rating is invented on anyone's behalf: the closure is counted as its own fact next to the stars counterparties actually gave. Deals frozen by an administrator are left alone, since the parties cannot act on them. Closing is opt-out via `Settlement__AutoCloseOverdue`; the warning is not.

**Registration and verification.** Two signup paths. A GSTIN is validated against the government registry in real time and an invalid one blocks account creation; a valid one makes the profile Active immediately. Without a GSTIN the account is created but every tab except its own Profile is locked until an Admin reviews uploaded evidence. A mobile number is collected as a contact detail. The four verification states (Needs Approval → Pending → Active/Rejected) are a real access boundary, enforced in the API as well as in the Angular route guards.

**Discovery.** A symmetric Opportunity dashboard: a Lender sees Seekers, a Seeker sees Lenders. Default ranking blends location proximity, matching supply/need category and settled history, so the first page is useful with no filters applied. Multi-select filters on location, category, sub-type, capacity range and minimum rating. Inactive, Suspended and Removed profiles are excluded entirely.

**Interest → chat → ticket → deal.** Interest is a one-sided signal that notifies the other party. Accepting unlocks chat; declining creates nothing and affects no rating. Once both sides agree, either can raise a ticket, which creates the Deal in Open state and carries the negotiation chat into the deal workspace.

**Deal lifecycle.** Open → Progress → Completed, with Halted as a detour from either. Completion requires *both* parties to confirm settlement. A halted deal does not auto-resolve: it stays stalled until both parties agree to resume, via a request the other side has to accept. Every transition is recorded with who triggered it and when.

**Ratings.** One 5-star rating per party per deal, available once a deal reaches Completed or Halted, feeding search ranking and the profile's trust record. Profiles show the full history rather than a single blended average.

**Analytics.** Two dashboard charts over the user's own deals: cumulative opened vs closed, and per-period settlement quality (settled clean vs halted). Each carries the caveat the spec attaches to it, in the UI next to the chart.

**Admin console.** A verification queue with approve/reject and a stated 48-hour turnaround target, a user directory filterable by region, category, status and profile type, the four moderation actions (warning, suspend with a fixed window, remove, lift suspension), and a platform-wide action log. Every decision is logged with actor and timestamp.

**Real-time.** SignalR drives live chat, interest notifications, deal state changes and verification decisions. Notifications are also persisted, so a vendor on patchy mobile data sees what arrived while they were disconnected.

**Background jobs.** Hangfire sends the approval email on Admin approval, retries GSTIN lookups that failed for transient reasons, and lifts suspensions whose window has passed.

---

## Stack

| Layer | Choice |
| --- | --- |
| Frontend | Angular 20 (standalone components, signals, zoneless), Angular Material, ng2-charts / Chart.js, `@microsoft/signalr` |
| Backend | ASP.NET Core 9 Web API, ASP.NET Core Identity + JWT (access + rotating refresh tokens), SignalR, Hangfire, FluentValidation, Swashbuckle |
| Database | SQL Server via EF Core code-first migrations (the only supported provider) |
| Storage | Local disk volume behind `IFileStorageService` |
| Email | SendGrid or SMTP (Mailtrap) behind `IEmailSender`; logs to console by default |
| GSTIN | `gstinapi.in` behind `IGstinVerifier`, with a mock used by default outside Production |
| Infrastructure | Docker Compose: nginx-served Angular build, API, SQL Server |

---

## Running locally

### Prerequisites

- **Docker Desktop** for the primary path, or
- **.NET 9 SDK**, **Node.js ≥ 20.19** (22 LTS recommended), and a **SQL Server** instance to run the two projects directly.

### Option A — Docker Compose (the primary path)

```bash
cp .env.example .env
# Edit .env: set DB_PASSWORD (SQL Server needs 8+ chars with upper, lower,
# digit and symbol) and JWT_KEY (any long random string).

docker compose up -d --build
```

The API applies the committed migrations itself as it starts, so the stack comes up usable from cold. Load the demo data once it is healthy:

```bash
# Load the demo data
docker compose exec api dotnet Saakh.Api.dll --seed
```

Wait for `docker compose ps` to show `api` as `healthy` first — that is the API reporting that the schema is actually in place, not just that the process is running.

| URL | What |
| --- | --- |
| <http://localhost> | The app |
| <http://localhost/health> | Readiness — `200` only when the schema is in place, `503` with the reason when it is not |
| <http://localhost:5000/health/live> | Liveness — the process is up; asks the database nothing |
| <http://localhost:5000> | The API directly, if you want to bypass the proxy |

Swagger and the Hangfire dashboard are developer tools, so they are only mapped outside Production. `.env.example` sets `ASPNETCORE_ENVIRONMENT=Production`, which means they are **off by default** — set it to `Development` and restart the `api` service to get `/swagger` and `/hangfire`.

`docker compose down` stops everything; `docker compose down -v` also drops the database and uploaded evidence.

> **Applying the schema separately.** `Database__MigrateOnStartup` is `true` in this compose file because it runs one API instance. Two replicas migrating the same database concurrently is not safe, so set `MIGRATE_ON_STARTUP=false` there and apply the schema once as its own step:
>
> ```bash
> docker compose exec api sh -c './migrate --connection "$ConnectionStrings__Default"'
> ```
>
> `./migrate` rather than `dotnet ef database update` because the runtime image is `aspnet`, per `tech-stack.md`, and it has no SDK. The build stage produces an EF Core *migration bundle* — a self-contained executable that applies exactly the committed migrations. The connection string is passed explicitly because a bundle reads `appsettings.json` from its working directory, and the real password lives only in the environment.

### Option B — Without Docker

SQL Server is the only supported database, so this path needs an instance to point at — a local SQL Server or Express install, or a remote one. There is no embedded-database fallback; see [Known limits](#known-limits) for why.

```bash
# Terminal 1 — API on :5080
cd backend/Saakh.Api
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Default="Server=localhost,1433;Database=Saakh;User=sa;Password=<your password>;TrustServerCertificate=True"
export ASPNETCORE_URLS=http://0.0.0.0:5080

dotnet tool install --global dotnet-ef
dotnet ef database update     # apply migrations
dotnet run --seed             # load the demo data
dotnet run                    # start the API

# Terminal 2 — Angular dev server on :4200
cd frontend
npm install
npm start
```

Open <http://localhost:4200>. The development build points at `http://localhost:5080` via a file replacement in `angular.json`; the production build calls `/api` on its own origin, which nginx proxies.

If you only want SQL Server and would rather not install it, the compose file's `db` service is exactly that — `docker compose up -d db` gives you one on `localhost:1433` to point the commands above at.

### Seeding

```bash
dotnet run --seed            # no-ops if the database already holds profiles
dotnet run --seed --reset    # wipe the demo data and reload it
```

The seeder writes profiles **directly to the database**, so it never calls the live GSTIN API and never spends a credit. Seeded GSTINs are plausible strings, not registry-verified numbers — profiles that came in through the real validation flow are the only ones that carry a genuine `GstinVerifiedAt`.

What you get: 13 Lender and 18 Seeker profiles across the category taxonomy and ten Indian states, 28 deals spread over the last nine months (16 Completed, 4 Halted, 4 in Progress, 4 Open), ratings on most closed deals spanning 1–5 stars, three accounts deliberately left unverified so the Admin queue has real work, one Inactive profile, and two Admin accounts.

---

## Deploying

`docker-compose.yml` is the deployment: the same file runs unchanged on a laptop
or on any x86_64 Linux host with Docker. Put Caddy or nginx in front of the `web`
service for TLS and that is a working public deployment.

Two things to know before picking a host:

- **SQL Server has no ARM64 Linux image**, and the migrate bundle is built
  `--target-runtime linux-x64`. So an ARM host — including Oracle Cloud's
  Always Free Ampere shapes, which `tech-stack.md` suggests — cannot run this
  stack as written. It needs x86_64, or a swap to PostgreSQL (EF provider *and*
  Hangfire storage).
- **SQL Server wants 2 GB of RAM**, which rules out the 1 GB free micro shapes.

For a free-tier public demo on Azure Container Apps + Azure SQL, including the
free-grant arithmetic that forces scale-to-zero and disabled background jobs,
see **[deploy/azure/README.md](deploy/azure/README.md)**.

---

## Demo accounts

Password for every seeded account: **`Saakh@2026`**

| Account | What it shows |
| --- | --- |
| `lender1@saakh.demo` | A Lender with settled history, deals in flight, and one halt at their own fault |
| `seeker1@saakh.demo` | The Seeker side of the same market |
| `pending1@saakh.demo` | Pending verification: evidence submitted, every tab except Profile locked |
| `needsapproval@saakh.demo` | Needs Approval: no GSTIN, nothing submitted yet |
| `admin@saakh.app` | Admin console: verification queue, directory, action log |

The sign-in screen lists these and fills the form on click.

---

## Walking through the product

A ten-minute path that touches every rule:

1. **Sign in as `lender1@saakh.demo`.** The Opportunity dashboard: trust record, both charts, ranked Seekers, and open deals.
2. **Send interest** to someone with no existing relationship. Sign in as that Seeker, accept it, and the chat opens — try opening the chat *before* accepting to see the gate.
3. **Raise a ticket** from the negotiation screen. The deal appears in Open state for both parties, with the chat carried across.
4. **Mark it in Progress**, then confirm settlement from one side only — the deal stays in Progress until the other party confirms too.
5. **Halt a different deal.** The confirmation states plainly that halting records *you* at fault; afterwards your profile's "halts at fault" count goes up.
6. **Send a resume request** from History. Nothing changes until the other party accepts.
7. **Rate a closed deal.** Try rating twice to hit the one-per-party constraint.
8. **Sign in as `admin@saakh.app`.** Approve one pending account (the approval email appears in the API logs) and reject the other with a reason, then sign in as the rejected user to see the reason on their banner.
9. **Suspend a profile** from the directory and confirm it disappears from discovery and can no longer receive interest.

---

## Business rules worth knowing

These are from `spec.md` and are implemented as written:

- **Halt fault (v1).** Whoever triggers a halt is auto-flagged at fault for that deal and it counts against their profile. This is acknowledged in the spec as potentially unfair — a Seeker who halts because the Lender went quiet still takes the hit — and shipped as-is. The UI states the consequence before the click rather than hiding it.
- **No cap on concurrent deals.** A profile can hold any number of simultaneous Open/Progress deals across different counterparties.
- **One rating per party per deal.** Enforced by a unique index on `(DealId, RaterProfileId)` as well as in the service.
- **An invalid GSTIN blocks profile creation.** A *transient* registry failure does not: the account is created on the manual-review path and a Hangfire job retries, so a third-party outage never leaves a vendor stuck.
- **Resuming a halted deal needs both parties.** A one-sided request changes no state.
- **Suspension freezes in-flight deals.** Neither party can move a deal whose counterparty is Suspended or Removed — including the counterparty, who may have done nothing wrong. The Admin console says so before the action is taken.
- **Inactive is not suspension.** Inactive is self-chosen, reversible, and leaves in-flight deals running. Suspended is Admin-imposed and time-bound.

---

## Design system

No design file existed, so one was established and applied across every screen. It lives in `frontend/src/styles/` and `frontend/src/app/shared/`.

**Tone.** Warm and grounded rather than cold fintech blue, because the product's value proposition is interpersonal trust, not transaction processing. The ground is warm paper, never pure white — less glare outdoors, and it reads as a ledger rather than a web app.

**Colour.** Deep pine (`--sk-brand`) carries primary actions and the Active state; marigold (`--sk-accent`) carries ratings and anything in progress. Every status has its own named token, chosen so no two are distinguishable by hue alone.

**No status is ever conveyed by colour alone.** Every status pill renders three things together: a token-driven colour, an icon whose *silhouette* is unique to that status, and the status name in words. The vocabulary is defined once in `core/models/status-vocabulary.ts`, so a status cannot look one way in a table and another in a banner.

**Type.** Fraunces for headings and trust moments — a trust record should have a voice. IBM Plex Sans for every control, table and figure, with tabular numerals on by default so amounts and counts line up column to column and stay legible at 12px on a budget Android screen.

**Components defined once and reused:** buttons (primary / secondary / destructive / quiet / disabled), status pills, the verification badge, the 5-star rating in both display and input modes, the four-state verification banner, form fields with valid/invalid states, filter chips, cards, stat tiles, the deal state timeline, the chat thread, the deal table, empty states and loading skeletons.

**Accessibility.** Real `<button>`, `<a href>`, `<input>` + `<label>` and `<fieldset>`/`<legend>` elements throughout — the star-rating input is radio buttons, filter chips are buttons with `aria-pressed`. 4.5:1 minimum text contrast, 44px minimum touch targets, one visible focus ring, and `prefers-reduced-motion` respected.

**Mobile-first.** The phone layout is the designed case, not a squeeze of the desktop one. Navigation sits in a bottom bar within thumb reach; data tables become card lists below their breakpoint rather than scrolling ten columns sideways.

**Sample data is never flattering.** Seeded ratings span 1–5 stars, halts carry a real at-fault party, and accounts with no history show "No ratings yet" rather than a fabricated score.

---

## Mapping

Entity-to-DTO mapping is hand-written, in `backend/Saakh.Api/Mapping/`, split by area:

| File | Covers |
| --- | --- |
| `ProfileMappers.cs` | Profiles and the shared category taxonomy |
| `DealMappers.cs` | Deals, chat messages, ratings, state history, resume requests |
| `AdminMappers.cs` | Evidence documents, the admin action log, notifications |

`tech-stack.md` nominated AutoMapper, and the first build used it. It was replaced for two reasons. The practical one: every AutoMapper release below 15.1.1 carries a published DoS advisory, and 15+ requires a licence key outside development — a free registration for this revenue bracket, but a registration, which sits badly against a hard "$0, no time bombs" requirement.

The better reason is that these particular maps carry rules worth reading. `ToSummary` decides whether a caller sees a counterparty's full GSTIN or a masked one, and whether their phone number is included at all; `ToDto` on an evidence document is the reason a storage key never reaches a client. Those are access-control decisions. Written out, each is a line you can point at in review. Expressed as mapper configuration, they become a convention you have to trust. Half of these shapes also depend on *who is asking* — which side of a deal you are on, whether you were the one who halted it — which needed a hand-written path regardless.

The trade is real: adding a DTO field now means adding a line here too, and the compiler will not remind you. For roughly twenty mappings over a stable domain model, that is the cheaper side of the trade.

---

## Tests

```bash
# Backend
dotnet test backend/Saakh.Tests/Saakh.Tests.csproj                                   # everything
dotnet test backend/Saakh.Tests/Saakh.Tests.csproj --filter FullyQualifiedName~Unit  # fast, no Docker

# Frontend
cd frontend && npm test -- --watch=false --browsers=ChromeHeadless
```

The integration half needs a working Docker daemon, because it starts its own SQL Server. The frontend run needs Chrome; if Karma reports `Cannot start ChromeHeadless`, point it at the binary — `CHROME_BIN="C:\Program Files\Google\Chrome\Application\chrome.exe"` on Windows, `/usr/bin/chromium` on most Linux images.

**Unit tests** cover the logic that carries the most weight per line: GSTIN format and the mock verifier's three outcomes, the mapper rules that decide whether a counterparty sees a masked or a full GSTIN, and the deal-row projection — side, counterparty, halt attribution, settlement confirmation and rating eligibility, each asserted from *both* parties' perspectives.

**Integration tests** start a real SQL Server through Testcontainers, apply the committed migrations, and drive the application over HTTP exactly as the Angular client does. That choice is deliberate and was paid for the hard way: of the four bugs found when this stack first ran in Docker, three were invisible to anything less — the schema's cascade-path rule is enforced by SQL Server alone, the startup-ordering failure only appears against an un-migrated database, and the globalization crash needed a Linux container. An in-memory provider would have reported all green.

They cover the rules `spec.md` calls final: the chat and ticket gates before an interest is accepted, settlement requiring both parties, halt-fault attribution and its effect on the trust record, resume needing both sides, one rating per party per deal, the verification gate across every locked endpoint, discovery's exclusion rules, the moderation actions with their audit log, and the readiness endpoint's two answers.

**Frontend specs** cover the parts of the client that can be wrong on their own. The route guards get the spec's access rules asserted directly — a locked account is redirected to its own profile from every blocked URL, each role is kept out of the other's screens, and a cold reload restores the session rather than bouncing to sign-in. The HTTP interceptor is driven through a real 401: it rotates the refresh token exactly once for several requests failing together, replays each with the new token, and clears the session only when the refresh itself is refused. `DealActions` is asserted on the thing that cannot be undone — the halt dialog states the fault consequence before the click, and a dismissed dialog issues no call at all. The status pill and the verification banner are rendered and read back, because "never signal status by colour alone" and "the banner disappears once there is nothing to do" are claims about the DOM, not about a constant.

---

## Project layout

```
backend/Saakh.Api/
  Domain/          Entities and enums — the data model from spec.md
  Data/            DbContext, migrations, DataSeeder (Bogus)
  Mapping/         Hand-written entity-to-DTO mapping, grouped by area
  Services/        Business rules: deals, interest, discovery, ratings,
                   analytics, admin, trust stats, GSTIN, storage, email, OTP
  Controllers/     REST surface
  Hubs/            SignalR hub and the server-side publisher
  Jobs/            Hangfire jobs (approval email, GSTIN retry, suspension expiry)
  Validators/      FluentValidation rules
  Infrastructure/  Exception handling, the verification access gate, rate limits

backend/Saakh.Tests/
  Unit/            Mapping, GSTIN format and the deal-row projection
  Integration/     The app booted against a real SQL Server via Testcontainers

frontend/src/
  styles/          Design tokens, the Material bridge, base element styles
  app/core/        Models, API clients, auth (store, interceptor, guards), SignalR
  app/shared/      The reusable component system
  app/features/    auth · dashboard · profile · interests · deals · admin
```

---

## Configuration

Everything is environment-driven; `.env.example` lists every variable the compose file reads.

| Setting | Default | Notes |
| --- | --- | --- |
| `Jwt__Key` | — | Required; the API refuses to start in Production without it |
| `Gstin__Provider` | `Mock` | `GstinApi` for live registry lookups |
| `Email__Provider` | `Log` | `Smtp` (Mailtrap) or `SendGrid` |
| `Storage__EvidenceRoot` | `storage/evidence` | Mounted as a Docker volume |
| `Database__MigrateOnStartup` | Development only | `true` in compose; set `false` for multi-replica and use the migrate bundle |
| `Sms__Provider` | `Log` | `Fast2Sms`, `Msg91` or `Twilio` to actually send the code |
| `Jobs__Enabled` | `true` | Runs the Hangfire sweeps. Off for a scale-to-zero or time-metered deployment — see [deploy/azure](deploy/azure/README.md) |
| `Otp__ResendCooldownSeconds` | `60` | Enforced server-side, not just in the form |
| `RateLimits__GstinPermitLimit` | `10` per 10 min | Protects the metered GSTIN quota |
| `RateLimits__SignupPermitLimit` | `30` per 5 min | Blunts automated signup abuse |

**The GSTIN mock** is deterministic, so the failure paths can be rehearsed without the live registry: a well-formed number verifies, one starting `00` comes back unregistered, and one starting `99` simulates a registry timeout and exercises the retry job. Live verification spends real credits from the 100/month free tier — rehearse with the mock and switch over only for an actual demo.

**Phone OTP was removed from signup.** `spec.md` §5 makes it mandatory on both paths; this build does not implement it, by decision. The number is still collected as a contact detail and still format-validated, but nothing verifies that the person signing up controls it.

The delivery machinery is still in the codebase and still tested — `IOtpChannel` with `Log`, `Email`, `Fast2Sms`, `Msg91` and `Twilio` behind it, plus the issue/expiry/resend-window service. Only the signup gate and its two endpoints are gone, so restoring the step is a matter of re-exposing them rather than rebuilding anything.

Worth knowing if it is ever restored: Indian A2P SMS is DLT-regulated, and every gateway gates its API behind payment or verification. Fast2SMS refuses `route=q` with *"complete one transaction of 100 INR or more before using API route"* and `route=otp` with *"complete website verification"*, regardless of the free signup credit sitting in the wallet. The `Email` channel exists because it is the only delivery that is both real and free at this volume — it verifies an inbox rather than a handset.
---

## Verification

The stack was brought up with `docker compose up --build` and exercised there, which surfaced four bugs that no amount of local running would have found:

| Bug | Why only a container showed it |
| --- | --- |
| `InvariantGlobalization=true` crash-looped the API | `Microsoft.Data.SqlClient` asks for the `en-us` culture when opening a connection. Windows always has it; a Linux container in invariant mode throws `CultureNotFoundException`. |
| Startup died against an un-migrated database | Migrations are a deliberate one-off step, so on a first deploy the schema does not exist yet — and the API queried it during startup. It now logs a clear warning and starts anyway. |
| `RecurringJob.AddOrUpdate` threw on boot | The static Hangfire API reads `JobStorage.Current`, which SQL Server storage does not populate until the server starts. Now resolved through `IRecurringJobManager`. |
| SQL Server rejected the schema (error 1785) | Deleting a user cascades to both `Profiles` and `Admins`, and both reached `EvidenceDocuments` — two cascade paths. The reviewing-admin FK is now `Restrict`, which is also the right rule for an audit trail. |

Building the test suite then surfaced a fifth, and it turned out to be one mistake made in four places. `Program.cs` read the JWT signing key, the rate-limit thresholds and the database connection string into locals during startup, then used those captured values to configure components that run much later. In the shipped configuration the captured values happen to agree with what is bound, so nothing looked wrong — but any configuration source registered after those lines was silently ignored. The symptoms were varied and none of them pointed at the cause: tokens signed with one secret and validated with another (a blanket 401), limits enforced that nobody had configured, and — worst of the three — Hangfire job storage still dialling the connection string from startup while every EF query used the right one, so background work failed against an unreachable server behind a bare 500.

All four now resolve their values at the point of use: JWT validation and the rate limiter read the bound options, and the `DbContext` and Hangfire storage both take the connection string from `IConfiguration` through DI via a single `ConnectionStringFor` helper. The pattern is worth stating plainly, because it is easy to repeat: **in `Program.cs`, configuration read into a local is a snapshot, and anything that runs later should resolve it instead.**

After the fixes, the documented path was run from scratch — `docker compose down -v`, then `up -d --build`, migrate, seed — and the whole product was exercised against the containers: the two rule suites (76 assertions) through the published API port, the UI rendered through nginx on port 80 with no console errors, live SignalR chat and deal-state pushes delivered over nginx's WebSocket upgrade, hashed assets served `immutable` with `index.html` `no-cache`, gzip negotiated, and the evidence volume mounted and writable.

The business rules were exercised end-to-end against the running API. Confirmed: the chat gate before acceptance, the ticket gate before acceptance, interest acceptance only by the recipient, Open→Progress, settlement needing both confirmations, the one-rating-per-party constraint and the 1–5 star bound, the halt-fault attribution and its effect on the profile, resume requiring the other party's agreement, discovery excluding non-Active profiles, every locked tab returning 403 for Needs Approval / Pending / Rejected accounts, admin approval sending the email and unlocking every tab, rejection recording a reason the user sees, suspension removing a profile from discovery and blocking interest, the Active/Inactive toggle leaving in-flight deals alone, and GSTIN format rejection before any registry call.

Every screen was rendered headlessly against seeded data and checked for console errors.

---

## Known limits

- **Signup does not verify the mobile number.** `spec.md` §5 requires a phone OTP on both paths; it was removed at the owner's direction after the cost of real SMS delivery in India became clear. The number is collected and format-checked, nothing more. The OTP service and its delivery channels remain in the codebase and under test, so the step can be restored without rebuilding it.
- **An auto-closed deal flags both parties, including one who was ready.** If a lender confirmed settlement and the seeker never did, both records carry the closure equally. That is the rule as specified, and it is a known unfairness: the trust record is meant to be a signal worth checking, and this adds noise to it. Flagging only the party who did not confirm would be the fairer rule.
- **Halt-fault fairness** ships unresolved, per the spec. A reason-based fault model is deferred.
- **Accepted evidence and review SLA** were left open by the spec. This build states an explicit accepted-document list in the UI and targets 48 hours, flagging over-SLA rows in the Admin queue; neither is enforced by the platform.
- **Discovery ranking** is computed in memory over the filtered candidate set. That is fine at pilot scale and would move into SQL (or a materialised trust-stats table) before it had to serve a large directory.
- **There is no embedded-database fallback.** An earlier build carried an opt-in SQLite provider so the API could start with no SQL Server present. It was removed: EF Core's SQLite provider pulls `SQLitePCLRaw`, whose bundled native SQLite carries `GHSA-2m69-gcr7-jv3q` (CVE-2025-6965) with **no patched release available**, and keeping it meant a conditional branch at every database touchpoint to support a provider the product never ships on. SQL Server is now the single path, `dotnet list package --vulnerable --include-transitive` reports nothing, and running the app locally means running the `db` container (or any SQL Server instance).
- **Seeded evidence documents** are database rows with placeholder storage keys. The Admin console lists them and reports the file as missing rather than rendering a fake licence.
- **The API still starts when its schema is missing**, by design: a service that crash-loops until someone migrates it cannot be inspected, and its logs are the only place the reason is written. It is no longer silent about it — `/health` answers `503` with the pending migration named and the command to apply it, so an unmigrated stack shows as `unhealthy` in `docker compose ps` rather than looking fine while every write fails. Readiness is re-checked until it passes, so applying the schema afterwards clears it with no restart. `/health/live` stays `200` throughout, which is what an orchestrator should restart on.
- **There are no end-to-end tests.** The frontend specs cover the guards, the token-refresh interceptor, the irreversible deal actions, the shared status components, the status vocabulary and the figure formatting — the logic that can be wrong without anyone noticing. What they do not cover is a whole screen wired to a live API: the pages are verified by rendering each one against seeded data and checking for console errors, which is a weaker guarantee than a Playwright pass over signup → interest → deal → rating. That pass is the next addition.

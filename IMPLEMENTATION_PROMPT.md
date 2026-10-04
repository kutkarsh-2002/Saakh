# Saakh — Implementation Prompt for Claude Code

## The README will be written during implementation, not beforehand.

## Prompt

You are building **Saakh**, a web platform that lets small, often informal vendors in India turn years of reliable trading history into a portable trust record a new counterparty can act on. This repo already contains the complete product and technical specification. Before writing any code, read these files in full, in this order:

1. `spec.md` — the functional specification: domain model, user roles, the Deal state machine, the verification-tier logic, rating system, Admin moderation rules, and the data model. Every business rule here is final — do not reinterpret or simplify it.
2. `tech-stack.md` — the technical architecture: Angular + ASP.NET Core + SQL Server + SignalR + Hangfire, the entity-to-table mapping, the GSTIN verification approach (`IGstinVerifier` interface, real + mock implementations), the seed-data plan, and the Docker Compose infrastructure (including the compose file and Dockerfiles already specified there — use them as given).
3. `design-brief.md` — the UI/UX design principles: legibility in bright outdoor light, verification/trust state treated as the most important visual element on screen, mobile-first design, no fabricated trust signals in sample data, admin tooling built with back-office density rather than consumer-app polish.

### Visual design — build this yourself, consistently

No design-system file exists in this repo. Establish one yourself, in the opening work of the frontend build, and apply it consistently across every screen rather than styling each one ad hoc:

- **Tone:** warm and trustworthy rather than cold corporate-blue fintech — this product's whole value proposition is interpersonal trust, not transaction processing. Avoid generic AI-template patterns (gradient washes, left-border accent cards, Inter/Roboto/Arial as the only typeface, emoji as UI glyphs).
- **Color:** a warm, non-pure-white background; a confident primary brand color used for Active state and primary actions; a second accent for ratings and in-progress states; distinct, named colors for Open / Halted / Needs-Approval / Suspended — chosen so no two statuses are told apart by hue alone (pair every status with its own icon shape and label text, never color alone, for colorblind users and bright-sunlight legibility).
- **Typography:** one typeface (or a considered pairing) for headings and trust-moments (the verification banner, rating summary), and a highly legible, tabular-friendly typeface for every control, table, and number — figures need to stay crisp at small sizes on budget Android screens.
- **Components to define once, then reuse everywhere:** buttons (primary/secondary/destructive/disabled), status pills (Open/Progress/Halted/Completed/Needs Approval/Pending/Inactive/Suspended), a verification badge, a 5-star rating display, verification banners (Needs Approval/Pending/Rejected/Approved), form fields with valid/invalid states, filter chips, and cards. Build these as real, reusable Angular components or a shared stylesheet — never copy-pasted inline styles per screen.
- **Accessibility:** real `<button>`/`<a href>`/`<input>`+`<label>` elements even for simple interactions, 4.5:1 text contrast minimum, 44px minimum touch targets, no color-only status signaling.

### What to build

A working, runnable, end-to-end implementation:

- **Backend** (`/backend`): ASP.NET Core Web API per `tech-stack.md` — Identity + JWT auth with Lender/Seeker/Admin roles, EF Core code-first models and migrations matching the Data Model section of `spec.md` exactly (Profile, Interest, Deal + DealStateHistory, Message, Rating, EvidenceDocument, Admin + AdminActionLog), FluentValidation on inputs (including GSTIN format), AutoMapper for DTOs, Swashbuckle for API docs, SignalR hub for live status/chat/notifications, Hangfire for the approval email and GSTIN-retry jobs, and the `IGstinVerifier` interface with a real `gstinapi.in` implementation plus a mock used by default in non-production environments.
- **Frontend** (`/frontend`): Angular (standalone components, Angular Material), role-guarded routing (including the Needs-Approval/Pending tab-locking behavior from `spec.md`), and these screens: Opportunity dashboard (Lender/Seeker symmetric view with both analytics charts, filters, opportunity table, open-deals table), Profile & Verification (GSTIN vs. no-GSTIN paths, all four banner states), Deal workspace with chat (ticket terms, state timeline, SignalR-driven live chat), Admin console (user directory with filters, verification queue with approve/reject), and a mobile-responsive version of the dashboard.
- **Database**: SQL Server via the Docker Compose `db` service already specified in `tech-stack.md`.
- **Seed data**: implement the `dotnet run --seed` command per the Seed data section of `tech-stack.md` — Bogus-generated profiles, deals, and ratings so the dashboards are populated on first run, not empty.
- **Infrastructure**: use the `docker-compose.yml`, `frontend/Dockerfile`, and `backend/Dockerfile` already specified in `tech-stack.md`'s Infrastructure section verbatim — don't redesign the container topology. `.env.example` should list every variable the compose file references (`DB_PASSWORD`, `GSTIN_API_KEY`, etc.) without real secrets.

### How to work

- Build in dependency order: data model + migrations → auth → core domain endpoints (profiles, interest, deals, ratings, evidence) → SignalR + Hangfire → Angular shell + routing/guards → shared design system/components → each screen → seed data → Docker Compose wiring → a smoke-test pass (does `docker compose up` actually serve a working app with seeded data visible on the dashboard).
- Treat `spec.md`'s business rules as non-negotiable: the halt-fault rule (whoever triggers a halt is auto-flagged at fault), the no-cap-on-concurrent-deals rule, the one-rating-per-party-per-deal constraint, GSTIN real-time validation blocking profile creation on failure, and the exact verification state machine (Needs Approval → Pending → Active/Rejected) are all already decided — implement them as written rather than asking whether they're correct.
- Where `spec.md` or `tech-stack.md` is genuinely silent on an implementation detail (e.g., exact validation error message wording, specific HTTP status codes), make a reasonable, consistent decision and keep moving — don't stop to ask unless an actual business rule is ambiguous or contradictory.
- Work autonomously through the full stack in this session rather than delivering one layer and stopping; the goal is a runnable product at the end, not a partial scaffold.
- Write `README.md` as part of this implementation — project summary, stack, and a "Running locally" section with the actual `docker compose up` / seed / migrate commands once they exist.

Start by reading the three reference files in full, then begin with the EF Core data model and migrations.

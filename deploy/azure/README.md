# Deploying Saakh to Azure (free tiers)

A public, clickable demo of Saakh on Azure for no monthly cost: the Angular/nginx
container and the API as two Container Apps, against an Azure SQL free-offer
database. TLS and a hostname come free with Container Apps ingress.

```
browser ──https──► web (Container App, external ingress :80)
                     │  nginx serves the SPA, proxies /api and /hubs
                     ▼
                   api (Container App, internal ingress → :5000)
                     │
                     ▼
                   Azure SQL (serverless, free offer, auto-pause)
```

## Read this part first

The free tiers will host this, but not as an always-warm service. Three numbers
decide the whole design:

| Grant | Allowance | What it buys |
| --- | --- | --- |
| Azure SQL free offer | 100,000 vCore-sec/month | At the 0.5-vCore floor, ~55 hours **online** per month — about 1.8 h/day |
| Container Apps | 180,000 vCPU-sec/month | Not even one always-on replica (24/7 at 0.25 vCPU needs ~658,000) |
| Container Apps | 2M requests/month | Far more than a demo will use |

Two consequences, both baked into `provision.sh`:

**Everything scales to zero.** `--min-replicas 0` on both apps. Idle costs
nothing; the first request after an idle spell pays a cold start. Expect **30–60
seconds** on a cold hit — container start plus the database resuming from
auto-pause. After that it is responsive until it goes idle again.

**Background jobs are off.** `Jobs__Enabled=false`. Hangfire polls its storage
every 15 seconds whether or not there is work, which holds the database open
permanently; at 0.5 vCore that is 43,200 vCore-sec/day and the entire monthly
grant is gone in **about two days**, after which Azure pauses the database for
the rest of the month.

So `Jobs__Enabled=false`, and no Hangfire server runs. Jobs can still be
*enqueued* — the storage is still configured, so nothing throws — but nothing
ever picks them up. Concretely, these stop happening:

| Dropped | Effect on the demo |
| --- | --- |
| `settlement-overdue` (hourly) | Nothing warns the parties when a settlement date passes, and no deal is auto-closed after the grace period. Deals stay Open until someone acts on them |
| `suspension-expiry` (hourly) | An admin suspension never lifts by itself |
| `gstin-retry-sweep` (hourly) | — |
| `GstinRetryJob` (scheduled, +2 min) | A GSTIN that times out is never retried. With the mock provider that is the `99…` path, which stays stuck mid-verification |
| `ApprovalEmailJob` (on admin approval) | The approval email is never sent. Harmless here: `Email__Provider=Log` means it would only have written to the log anyway. **The approval itself still takes effect** |

For a portfolio demo that is usually the right trade — a visitor signs up,
agrees deals and reads trust records, and none of that waits on a sweep. The one
rough edge a visitor could actually hit is the `99…` GSTIN path, which the root
README documents as the way to rehearse a registry timeout. Avoid that number
when demoing, or see [Getting the jobs back](#getting-the-jobs-back).

## If you have Azure credit, skip the compromises

Everything above is shaped by the free grants. A student subscription removes
that constraint: **Azure for Students** gives $100 of credit a year on a
university email with **no credit card**, renewable while you are enrolled.
That is far more than this demo burns, so there is no reason to live inside the
free tiers if you qualify.

Sign up at [azure.microsoft.com/free/students](https://azure.microsoft.com/free/students)
with your institutional address and complete the academic verification. Then:

```bash
ALWAYS_ON=1 API_IMAGE=ghcr.io/YOURUSER/saakh-api:latest WEB_IMAGE=ghcr.io/YOURUSER/saakh-web:latest ./provision.sh
```

`ALWAYS_ON=1` changes three things:

| | Free (default) | `ALWAYS_ON=1` |
| --- | --- | --- |
| Replicas | 0 — cold start of 30–60s | 1 each — always warm |
| `Jobs__Enabled` | `false` — no sweeps | `true` — settlement closure and the rest run |
| Free-grant exhaustion | `AutoPause` — database stops | `BillOverUsage` — billed to your credit |

This is the configuration that actually behaves like the product. Set a budget
alert so a forgotten deployment cannot quietly eat the year's credit:

```bash
az consumption budget create --budget-name saakh-demo --amount 20   --time-grain Monthly --category Cost
```

When the credit expires the subscription stops rather than billing a card, so
the failure mode is the demo going offline, not a surprise invoice.

---

## Deploy

Prerequisites: Docker, the `az` CLI, a GitHub account, and an Azure subscription.
Neither the Azure CLI nor a login exists in this repo's dev container, so both
steps are run from your own machine.

```bash
az login

cd deploy/azure
chmod +x push-images.sh provision.sh

# 1. build and push both images to GHCR (free for public packages)
echo "$GITHUB_TOKEN" | docker login ghcr.io -u YOURUSER --password-stdin
REGISTRY=ghcr.io/YOURUSER ./push-images.sh
```

Then **make both packages public** on GitHub — *Packages → saakh-api → Package
settings → Change visibility*. Container Apps pulls anonymously here, and a
private package fails with a pull error that does not mention permissions.

```bash
# 2. provision Azure and deploy
API_IMAGE=ghcr.io/YOURUSER/saakh-api:latest \
WEB_IMAGE=ghcr.io/YOURUSER/saakh-web:latest \
./provision.sh
```

The script prints the URL, the generated SQL password and the generated JWT key.
**Save the password** — Azure will not show it again.

```bash
# 3. seed the demo accounts
az containerapp exec -g saakh-demo -n api --command "dotnet Saakh.Api.dll --seed"
```

Sign in as `lender1@saakh.demo` / `Saakh@2026`. The other seeded accounts are in
the root [README](../../README.md#demo-accounts).

## What the script sets, and why

| Setting | Value | Why |
| --- | --- | --- |
| `Jobs__Enabled` | `false` | Hangfire polling would spend the SQL grant in ~2 days |
| `Database__MigrateOnStartup` | `true` | One replica, so the API can safely bring its own schema up; no second step on a cold deploy |
| `API_UPSTREAM` / `API_HOST` / `API_SCHEME` (web) | the api's full internal FQDN, same again, `https` | **Not** `api` and **not** `api:5000`. nginx cannot resolve the bare app name at startup, and ingress routes on the `Host` header, so nginx must send the FQDN as `Host` or the request loops back to the web app. Express environments reject `allowInsecure`, so plain HTTP gets a 301 to HTTPS; nginx therefore talks https and sends SNI |
| `Otp__RevealCodeInResponse` | `true` | Demo stack. See the warning below |
| `Gstin__Provider` | `Mock` | Keeps the demo off the metered registry quota and makes the failure paths reproducible |
| `--min-replicas` | `0` | The free compute grant does not cover an always-on replica |
| `--free-limit-exhaustion-behavior` | `AutoPause` | Pause rather than bill when the monthly grant runs out |

## Things this deployment is not

**It is a demo, and it is not secure as configured.** `REVEAL_OTP`/
`Otp__RevealCodeInResponse` is on, every seeded account shares one password, and
the SQL firewall is open to all Azure services rather than to a pinned address.
That is deliberate for something whose purpose is to be clicked by strangers,
but it is not a configuration to put real vendor data into. Note also that, per
the root README, **signup phone verification is currently removed** — the number
is collected and format-checked, but nothing proves the person controls it.

**Uploaded evidence does not survive a restart.** The compose stack keeps
evidence documents on a named volume; these Container Apps have no volume, so
`/app/storage/evidence` is container-local and a scale-to-zero wipes it. Seeded
evidence is re-created by `--seed`; anything uploaded through the running demo is
not. Fixing it properly means an Azure Files share mounted into the api app:

```bash
az storage account create -g saakh-demo -n saakhevidence --sku Standard_LRS
az storage share create --account-name saakhevidence -n evidence
# then: az containerapp env storage set ... && az containerapp update --...
```

That is left out of `provision.sh` because a storage account is the one piece
here with no always-free grant — it is a few cents a month, not nothing.

**SignalR works, but reconnects after a scale-to-zero.** Container Apps supports
WebSockets, and nginx upgrades `/hubs/` correctly. An idle scale-down drops open
hub connections; the client reconnects on the next interaction.

## Getting the jobs back

Pick one:

1. **Accept a warm database.** Set `Jobs__Enabled=true` and `--min-replicas 1` on
   the api app, and expect the SQL free grant to run out in ~2 days each month.
   Only sensible if you move the database off the free offer.
2. **Raise the poll interval.** `Jobs__QueuePollIntervalSeconds=3600` cuts the
   polling cost by 240×, but it still keeps the database from ever auto-pausing,
   so the grant still drains — slower, not never. Half a fix.
3. **Trigger the sweeps externally** (the approach that actually fits this
   architecture). Keep `Jobs__Enabled=false`, and have something off-platform
   call the API hourly so the work happens while the app is awake anyway. This
   needs the jobs exposed behind an authenticated endpoint, which the codebase
   does not currently do — the jobs are only reachable through Hangfire.
4. **Move the database.** A $5/month VPS running the original
   `docker-compose.yml` unchanged has none of these constraints. If the demo ever
   needs to behave like the real product, that is the cheaper answer in effort.

## Teardown

```bash
az group delete -n saakh-demo --yes --no-wait
```

Deletes everything including the database. The GHCR packages are separate —
delete those from GitHub if you want them gone.

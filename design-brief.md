# Saakh — Product UI/UX Design Brief

I'm a product manager with 20+ years of experience building reporting dashboards and lending platforms — trade finance tools, supply-chain credit systems, SME lending portals. I'm briefing you on the UI/UX for Saakh, a new product, and I want you to design it the way I'd expect from a team that's shipped this category before, not a generic SaaS template.

## The product in one sentence

Saakh lets small, often informal vendors in India turn years of reliable trading history with existing suppliers and financiers into a portable trust record a brand-new counterparty can act on immediately — it sits in the gap between formal credit bureaus (which require registered businesses) and lending-facing credit infrastructure like OCEN (which scores for banks, not peer traders).

## Who's using this, and under what conditions

- Small vendors, street/local retailers, informal service providers — many with limited formal education, low digital literacy, and older or budget Android phones.
- Used outdoors, in bright sunlight, often one-handed, often on patchy mobile data.
- Two primary roles (Lender, Seeker) with symmetric needs, plus an Admin console for platform moderation.
- Trust and verification status are the product's core currency — every screen needs to make a user's standing (Active/Verified, Needs Approval, Pending, Rejected, Suspended) impossible to miss or misread.

## What I need from you, drawing on lending-platform conventions

1. **Legibility over cleverness.** Every status, every number, every rating needs to be readable at a glance in direct sunlight, at arm's length, by someone who isn't a power user. No status conveyed by color alone — pair it with an icon and a label, the way good underwriting dashboards do for risk tiers.
2. **Trust is the hero, not the chrome.** Verification state, GSTIN-verified badges, and star ratings should read with the same weight a credit bureau report gives a credit score — prominent, unambiguous, impossible to scroll past by accident.
3. **Data density without clutter.** This is closer to a reporting/underwriting tool than a consumer app — opportunity tables, deal-state history, and settlement analytics need to sit comfortably alongside each other without feeling like a dashboard built by committee. Borrow the discipline of a good trade-finance back office, not the playfulness of a consumer fintech app.
4. **State machines need to be visible, not implied.** A deal moves Open → Progress → Halted/Completed. Design the status representation so a user can reconstruct a deal's history at a glance — this is the single most important interaction pattern in the product, the way a loan's repayment schedule is the most important view in a lending platform.
5. **Mobile-first, not mobile-adapted.** Design the phone experience first and scale up, not the reverse — most of this user base will never open this on a desktop.
6. **No invented trust signals.** Never fabricate a rating, a completion number, or a verification state in mockups — use realistic placeholder data grounded in the actual taxonomy (categories: Money, Fruits, Dairy, Medicine, Industrial Equipment; Indian cities/states) so stakeholders aren't reviewing fiction.
7. **Admin tooling is a back-office tool, not an afterthought.** Design the verification queue and user directory with the density and filter rigor of a compliance or ops console — the Admin isn't a casual user, they're triaging a queue all day.

## Deliverable

A cohesive visual language (color, type, component system) plus key screens — Opportunity dashboard (Lender/Seeker), Profile & verification states, Deal workspace with chat, Admin console — built to hold up as "a product that could be trusted with money," because in the vendors' eyes, that's exactly what it is.

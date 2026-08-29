# 1. Record architecture decisions

Date: 2026-08-29

## Status

Accepted

## Context

CTMS is being built out by several parallel workstreams (domain/backend, admin
UI, client SDK, infrastructure, CI/CD). Decisions that are expensive to reverse -
the data model, the persistence technology, the auth model, how translations are
delivered to clients - need to be written down with their rationale so that
future contributors (and future Claude sessions) understand *why* the system is
shaped the way it is, not just *what* it does.

`CLAUDE.md` captures the current commands and architecture snapshot, but it is
deliberately terse and always reflects "now"; it is not a history and does not
carry rationale or rejected alternatives.

## Decision

We will keep a log of Architecture Decision Records (ADRs) in `docs/adr/`.

- One file per decision, numbered sequentially: `NNNN-short-title.md`.
- Format: Michael Nygard's template - **Context**, **Decision**, **Consequences**,
  plus a **Status** (Proposed / Accepted / Deprecated / Superseded by NNNN) and a
  date.
- An ADR is immutable once Accepted. To change a decision, add a new ADR that
  supersedes the old one and update the old one's Status to point at it.
- ADRs are for decisions that are hard to reverse or that have repo-wide
  consequences. Routine, easily-changed choices do not need one.
- The index in `docs/README.md` lists every ADR and its status.

## Consequences

- Contributors have a durable record of why key choices were made, including
  the trade-offs that were knowingly accepted.
- Each hard-to-reverse decision costs a short writeup; this is cheap relative to
  the cost of re-litigating undocumented decisions.
- Reviewers can push back on significant changes that arrive without an ADR.
- The first real entry is [ADR 0002](0002-mongodb-as-primary-store.md).

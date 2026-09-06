# Architecture Decision Records

One short file per significant decision: context, decision, consequences. **Why**, not how. When a decision changes, write a new ADR that supersedes the old one — never edit history.

| # | Decision |
|---|---|
| [0001](0001-hexagonal-pure-core.md) | Hexagonal architecture; pure-C# core owns all game logic (brain / body / senses) |
| [0002](0002-vcontainer-no-service-locator.md) | VContainer for composition; no statics, no service locator |
| [0003](0003-commands-facts-tick-events-intents.md) | Commands, facts, per-frame tick in; events and intents out |
| [0004](0004-scoped-domain-events.md) | Domain events through a scoped outbound port; no static bus |
| [0005](0005-per-agent-typed-blackboards.md) | Blackboards: per-agent, typed, never global |
| [0006](0006-content-catalog-from-scriptableobjects.md) | ScriptableObjects author; immutable core specs consume |
| [0007](0007-save-store-async-local-first-versioned.md) | `ISaveStore`: async, local JSON now, local-first sync later, versioned with migration tests |
| [0008](0008-stat-modifier-system.md) | Every gameplay number is a `Stat` with a modifier stack — **non-negotiable** |
| [0009](0009-effect-primitives-open-set.md) | Effects are an open set of primitives, not a switch |
| [0010](0010-stable-content-ids-and-tags.md) | Stable content IDs and a tag set |
| [0011](0011-random-streams.md) | `IRandom` with independent named streams |
| [0012](0012-localization-keys-from-day-one.md) | Localisation keys from the first string |
| [0013](0013-git-flow-owner-commits.md) | Git-flow with PR review; the owner makes every commit |

Template for new ones:

```
# ADR-NNNN — Title
**Status:** Proposed | Accepted | Superseded by ADR-MMMM · **Date:** YYYY-MM-DD
## Context
## Decision
## Consequences
```

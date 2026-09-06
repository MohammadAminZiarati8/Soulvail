# ADR-0008 — Every gameplay number is a `Stat` with a modifier stack

**Status:** Accepted · **Date:** 2026-09-06 · **Non-negotiable**

## Context

Ten sources modify *damage* alone: tree passives, Pact nodes, class signatures, Veilrot thresholds, Elite affixes, Ordeals, Focus, the Claiming, difficulty modifiers, depth scaling. If each is an `if`, nobody can answer "why is my damage 47?" and the game collapses at roughly fifty effects. This is the single most common way roguelites become unmaintainable.

## Decision

From the first line of combat code, every gameplay number — damage, fire rate, speed, max HP, cooldown, XP gain, Veilrot gain — is a `Stat`: a base value plus an ordered modifier stack (`Flat` → `PercentAdd` → `PercentMult`), each modifier tagged with its source so it can be removed when the source ends and inspected when a designer asks where a number came from. `Value` is cached and recomputed on change.

## Consequences

- **+** Every future mechanic that changes a number plugs in without touching combat code.
- **+** A free debug panel: `47 = 13 + 15% (node) + 45% (Pact) × 1.2 (Focus)`.
- **+** Buffs and thresholds are removable by source, so nothing "sticks."
- **−** Slightly more ceremony than a `float`. Trivial next to the alternative.

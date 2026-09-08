# ADR-0009 — Effects are an open set of primitives, not a switch

**Status:** Accepted · **Date:** 2026-09-06

## Context

Skills, tree nodes, Pact variants, Elite affixes, Ordeals, class signatures — all are "an effect that does something." The trap is `switch (effect.Type)`: every new effect edits the switch, and content growth becomes code churn in the most central file.

## Decision

A small library of **effect primitives** (`ModifyStat`, `OnKillTrigger`, `SpawnZone`, `ApplyStatus`, `ChainDamage`, …) that spec data composes. Each primitive is its own type with a registered handler — open for extension, closed for modification. New effects are new types, never edits to a dispatcher.

Build the first ten primitives as they are needed. **No effect DSL.**

## Consequences

- **+** Content growth doesn't touch existing code.
- **+** Primitives are individually testable.
- **+** Same mechanism serves nodes, affixes, Ordeals, and signatures.
- **−** A registry and an interface up front where a switch would have been quicker for the first three effects. Worth it by the tenth.

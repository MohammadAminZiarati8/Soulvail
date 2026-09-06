# ADR-0010 — Stable content IDs and a tag set

**Status:** Accepted · **Date:** 2026-09-06

## Context

Content referenced by enum ordinal or array index breaks every save the moment something is inserted in the middle. Mechanics that check `if (enemy is Wight)` multiply into type checks nobody can extend.

## Decision

- Every spec carries a `ContentId` — a stable string (`skill.oathbound.consecrate`). Saves, analytics, and remote config use it. **Enums are for closed sets only** (FSM states, modifier kinds), never for content.
- Entities and effects carry a `TagSet` — registered names (`Undead`, `Minion`, `Fire`, `Projectile`, `Elite`) backed by a bitset. Future rules ("+20% vs Undead", "Wights count as Minions for auras") are tag queries.

## Consequences

- **+** Inserting, renaming, or reordering content never breaks a save.
- **+** Cross-cutting mechanics compose through tags, not type checks.
- **+** Analytics and remote config have a stable vocabulary.
- **−** IDs are strings and must be unique; an editor validation pass enforces it.

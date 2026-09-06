# ADR-0006 — Definitions authored as ScriptableObjects, consumed as immutable core specs

**Status:** Accepted · **Date:** 2026-09-06

## Context

Designers tune in the inspector; that means ScriptableObjects. But SOs are `UnityEngine.Object`, so a pure core cannot reference them. We also want the option of delivering balance data from a server later.

## Decision

SOs (`CharacterDefinition`, `EnemyDefinition`, `SkillDefinition`, `ModeDefinition`, `TuningConfig`) live in `Soulvail.Game/Authoring`. At boot, each is converted once via `ToSpec()` into an immutable plain record (`CharacterSpec`, …) defined in core, and the set is registered in `BootScope` as the `ContentCatalog`.

Specs are immutable. Runtime state never lives in a spec. Every spec carries a stable `ContentId` (ADR-0010).

## Consequences

- **+** Inspector tuning while the game runs; data is diffable YAML in git.
- **+** Core stays pure and sees only records.
- **+** The source can become server JSON (live balance) as an adapter change.
- **−** Two shapes per definition (SO + spec), ten lines of conversion each. Accepted.
- **−** Play-mode edits to a spec don't propagate until reconversion. Provide a "reload catalog" editor action.

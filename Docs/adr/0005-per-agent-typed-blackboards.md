# ADR-0005 — Blackboards: per-agent, typed, never global

**Status:** Accepted · **Date:** 2026-09-06

## Context

Enemy and boss behaviours need perception (distance to player, path direction, line of sight) and working memory (target, timers, the direction chosen at telegraph time). The blackboard pattern decouples sensors from behaviours. Its common form — one global `Dictionary<string, object>` everything reads and writes — is a god object with string keys, boxing, and untraceable writers, and it quietly becomes a second communication channel that competes with events.

## Decision

A blackboard is **one agent's perception plus its own working memory**: a plain typed class with named fields, one per enemy, one per boss, one `CombatBlackboard` for the player. Snapshot ingestion writes perception; the agent's own FSM writes working memory; nothing else writes.

Auto-cast trigger conditions are pure predicates over the `CombatBlackboard`, carried by the `SkillSpec` as data.

Not a bus: cross-agent communication is events. Not string-keyed: if data-driven authoring ever needs dynamic keys, upgrade to `BlackboardKey<T>` over a typed store.

## Consequences

- **+** Sensors and behaviours decoupled; FSM states share context without parameter passing.
- **+** Trigger conditions become one-line testable predicates and data-drivable.
- **+** No strings, no boxing, refactor-safe, fast.
- **−** Adding a perception field is a code change, not a config change. Acceptable for V1.
- **−** Unity's Behavior package blackboard is unused; logic is in core.

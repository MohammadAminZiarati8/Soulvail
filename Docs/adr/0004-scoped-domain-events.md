# ADR-0004 — Domain events through a scoped outbound port; no static bus

**Status:** Accepted · **Date:** 2026-09-06

## Context

When an enemy dies, at least five things care — XP, HUD, haptics, audio, the director's alive count, later analytics. Core must tell the outside what happened without knowing who listens. A global static bus is the common answer and it's a hidden singleton that leaks across scene loads and can't be scoped per run. Two-way buses (Unity also publishing *into* core) make flow untraceable.

## Decision

`IDomainEvents` is an **outbound port** in core: `Publish<T>(in T evt)`. Its adapter, `DomainEventHub`, is a small typed dispatcher registered in `RunScope` and disposed with it. Payloads are `readonly struct`s; publish allocates nothing.

Rules: **outbound only** (Unity → core is always a method call on an inbound port); events describe what happened, never what to do; subscribe/unsubscribe is tied to `OnEnable`/`OnDisable` or scope disposal. Tests use `RecordingEvents`, which collects a list.

MessagePipe is the upgrade path if filters or async handlers are ever needed. Not adopted now — one dependency, not two.

## Consequences

- **+** Core has zero references to UI, audio, haptics, analytics. Adding a listener touches only the listener.
- **+** Scoped to the run; nothing leaks. Tests assert on exact event sequences.
- **+** No GC pressure in hot paths.
- **−** "Who handles this?" is a search for `Subscribe<EnemyDied>`. Accepted.
- **−** Leaked subscriptions are a real bug class. Mitigated by the lifecycle rule and scope disposal.

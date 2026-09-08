# ADR-0007 — `ISaveStore`: async port, local JSON now, local-first sync later, versioned DTOs with migration tests

**Status:** Accepted · **Date:** 2026-09-06

## Context

Persistent data is a player profile (Shards, unlocks, settings) and a run snapshot for resume (Android kills backgrounded apps). We save locally now and will probably add a server later. A launched game's saves are a contract that can never be broken.

## Decision

Core defines the DTOs (`PlayerProfile`, `RunSnapshot`) and the `ISaveStore` port with **async signatures from day one**, even though the first adapter (`LocalJsonSaveStore` → `persistentDataPath`) is a synchronous file write. Every DTO carries `int Version`; migrations are pure core functions, each shipped with a test that loads a fixture of the previous format.

When a server arrives: **local-first with sync** — write locally, push in the background, reconcile on launch — as a `SyncingSaveStore` wrapping the local one. Core never learns it happened.

## Consequences

- **+** Server is an adapter; not one core signature changes.
- **+** Offline-safe, zero latency inside a run.
- **+** Migrations are tested; old saves keep loading.
- **−** Async plumbing for what is currently a file write. Cheap.
- **−** Sync conflict resolution is a real design task when it comes. Deferred, not forgotten.

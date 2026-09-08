# ADR-0003 — Data flow: commands, facts, and a per-frame tick in; events and intents out

**Status:** Accepted · **Date:** 2026-09-06

## Context

If core owns all logic, the boundary has to carry everything the brain needs and everything the body must do — without the core ever reaching out to query the world (which makes ports leaky and tests impossible) and without latency on input.

## Decision

Three inbound channels, two outbound:

- **Commands** (discrete input) and **facts** (discrete physical results: overlap hits, contacts) are method calls on inbound ports, sent **the instant they happen.**
- **Tick** runs **every frame** with `dt` and a `WorldSnapshot` — the only thing core can't know: where things are. Player and enemy positions/velocities, NavMesh path directions, the move-stick vector. Preallocated structs, `System.Numerics` vectors, zero allocations.
- Core emits **events** (what happened, via `IDomainEvents`) and **intents** (what the body should do this tick, via a preallocated buffer).

Core throttles its own expensive work internally (targeting scorer every 0.1 s, director every 0.5 s). Frequency is a tuning detail inside core, not a boundary contract.

**Variable `dt`, not a fixed timestep.** Determinism is unavailable anyway while `CharacterController` resolves collisions. Core never reads wall-clock or `UnityEngine.Random` (`IClock`, `IRandom`), which keeps replay/verification possible later without paying now.

## Consequences

- **+** Core never queries the world. Data in, decisions out — "functional core, imperative shell." Tests feed a snapshot and assert on events and intents.
- **+** Zero input latency: commands don't wait for a tick.
- **+** Snapshot is small because core already owns HP, cooldowns, states, targets.
- **−** Every new mechanic that needs a new sense adds a snapshot field. Acceptable and explicit.
- **−** One-frame lag on positions. Irrelevant at 60 Hz.
- **−** No replays for free. Revisit with a superseding ADR if ever required.

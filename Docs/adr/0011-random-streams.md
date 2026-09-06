# ADR-0011 — `IRandom` exposes independent named streams

**Status:** Accepted · **Date:** 2026-09-06

## Context

A seeded run (Daily mode, bug reproduction) must produce the same spawns from the same seed. With one shared RNG, adding *any* mechanic that consumes randomness — a new drop roll, a new affix — shifts every subsequent value and silently changes the spawn sequence of every seeded run.

## Decision

`IRandom` provides named streams — `Spawn`, `Offers`, `Affixes`, `Drops`, `Misc` — each independently seeded from the run seed. Core code draws from the stream that matches its concern. `UnityEngine.Random` is never used in core.

## Consequences

- **+** Adding a mechanic in one stream leaves the others' sequences untouched.
- **+** Seeded Daily runs and reproducible bug reports are free.
- **−** Ten lines of code and the discipline to pick the right stream. Trivial now, unfixable after launch.

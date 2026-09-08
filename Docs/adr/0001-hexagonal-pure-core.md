# ADR-0001 — Hexagonal architecture with a pure-C# core that owns all game logic

**Status:** Accepted · **Date:** 2026-09-06

## Context

The game has a lot of rules — difficulty curves, spawn budgets, skill trees, Veilrot thresholds, enemy behaviours, boss phases — and every one of them must be tuned per class against a death-horizon target. Rules tangled into MonoBehaviours are untestable without a scene, slow to iterate, and impossible to reason about once there are ten sources modifying one number.

We also expect to grow: more classes, more modes, a server, possibly live balance. Each of those is painful if game logic is welded to Unity.

## Decision

Hexagonal (ports and adapters). `Soulvail.Core` is pure C# (`noEngineReferences: true`) and owns **all game logic, including enemy and boss behaviour.** `Soulvail.Game` is Unity: adapters, views, composition roots. Dependency arrow: `Game → Core`, enforced by the compiler.

The boundary is *decision vs. execution*: **core is the brain; Unity is the body and the senses.** Unity reports facts and positions, core decides outcomes and emits intents, Unity executes and renders. Pathfinding is treated as a sense (Unity supplies `PathDirectionToPlayer`); presentation is not logic.

## Consequences

- **+** Every rule is a unit test that runs in milliseconds with no scene. Enemy behaviour, boss phases, the director, the whole run — testable.
- **+** Growth vectors (classes, modes, server, live balance) are adapter changes.
- **+** Core cannot accidentally grow a `GameObject` dependency.
- **−** Snapshot/intent plumbing at the boundary: a few hundred lines that a flat design wouldn't need.
- **−** Core cannot use `UnityEngine.Vector3`, `Mathf`, `Random`, `Time`. We use `System.Numerics` and ports. This is a feature.
- **−** Core grows fastest and can become a monolith. Mitigated by modules from day one.
- **−** Physics-heavy mechanics fit awkwardly. Accepted as the rare exception.

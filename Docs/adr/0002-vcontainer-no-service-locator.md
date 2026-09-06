# ADR-0002 — VContainer for composition; no statics, no service locator

**Status:** Accepted · **Date:** 2026-09-06

## Context

With a pure core, core classes use constructor injection — that needs nothing. But MonoBehaviours have no constructors, and *something* must wire adapters to ports, hand core objects to scene components, tick core objects from Unity's loop, and tear a run down cleanly.

Options: a service locator (`Services.Get<T>()`), hand-written pure DI in a composition root, or a container. Zenject/Extenject is heavier and less maintained; Microsoft DI has no Unity lifecycle.

## Decision

**VContainer.** A root `BootScope` (clock, random, save, localiser, content catalog, settings) and a child `RunScope` per run (session, events, intents, director, pools, presenters). Core objects participate in Unity's lifecycle through VContainer entry points (`ITickable`, `IStartable`, `IDisposable`) without being MonoBehaviours. MonoBehaviours receive `[Inject]`. Pooled prefabs are instantiated through the resolver once at pool creation.

**No service locator, no static state anywhere.** A service locator is a hidden global that lies about dependencies — the opposite of what hexagonal exists for.

This reverses an earlier "no DI framework" suggestion made for a flat MonoBehaviour design. The architecture changed; the answer changed.

## Consequences

- **+** Scope = lifetime. Disposing `RunScope` disposes the session, events, pools, and every subscription. This is the answer to "what owns run state."
- **+** Core objects get ticked and disposed without touching Unity.
- **+** Scene-object injection — the thing pure DI does badly — is solved.
- **+** Small, fast, IL2CPP-friendly, MIT, actively maintained.
- **−** One third-party dependency and a day of learning curve.
- **−** Injection failures surface at scope build time, not compile time. Mitigated by keeping registrations in one readable block per scope.

# M0-12 — `BootInstaller`, `RunInstaller`, `PendingRun`, container tests

**Size:** M · **Depends on:** M0-10, M0-11 · **Branch:** `m0-12-composition-installers`
**Design refs:** AR §7, ADR-0002

## Goal

Every registration for the Boot and Run scopes lives in two scene-free static installers that a test can build into a real VContainer container and resolve — so wiring mistakes surface in the Test Runner, not on a phone.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Composition/PendingRun.cs` | Game | What the next run should be (set by Menu, read by Run) |
| `Game/Composition/BootInstaller.cs` | Game | Root registrations |
| `Game/Composition/RunInstaller.cs` | Game | Per-run registrations |
| `Tests/Game/Composition/InstallerTests.cs` | Tests.Game | Build containers headlessly and resolve |

## Public API

```csharp
namespace Soulvail.Game.Composition;

public sealed class PendingRun
{
    public bool      IsSet       { get; }
    public ContentId CharacterId { get; }
    public int       Seed        { get; }
    public void Set(ContentId characterId, int seed);
    public void Clear();
}

public static class BootInstaller
{
    public const int SnapshotEnemyCapacity = 64;     // becomes a TuningConfig field later
    public static void Install(IContainerBuilder builder, IReadOnlyList<CharacterDefinition> definitions);
}

public static class RunInstaller
{
    public static void Install(IContainerBuilder builder);
}
```

## Behaviour

**BootInstaller**
1. Builds a `ContentCatalog` from `definitions.Select(d => d.ToSpec())` and registers the instance. An invalid definition fails scope construction loudly (exception propagates).
2. Registers `PendingRun` as `Lifetime.Singleton`.

**RunInstaller** (all `Lifetime.Scoped`)
3. `DomainEventHub` as `IDomainEvents` and as itself.
4. `IntentBuffer` as `IIntentSink` and as itself.
5. `WorldSnapshot` instance with `BootInstaller.SnapshotEnemyCapacity`.
6. `IRandom` via factory: resolve `PendingRun`; if `!IsSet` throw `InvalidOperationException("No pending run")`; else `new SeededRandom(pending.Seed)`.
7. `RunSession` as `IRunSession`.
8. Disposing the run scope disposes `DomainEventHub` (VContainer disposes `IDisposable` registrations owned by the scope).

**PendingRun**
9. `Set` records both values and flips `IsSet`; `Clear` resets. Reading `CharacterId`/`Seed` when `!IsSet` throws `InvalidOperationException`.

Nothing scene-related is registered here — `PlayerView`, `SnapshotBuilder`, `RunTicker`, `InputAdapter` are registered by `RunScope` itself in M0-16, because they need serialized scene references.

## Tests

| Test | Given / When / Then |
|---|---|
| `Boot_ResolvesCatalog_WithOathbound` | Install(builder, [Oathbound.asset]) / Build, Resolve<ContentCatalog>() / `Character(oathbound)` succeeds |
| `Boot_InvalidDefinition_FailsBuild` | definition with bad id (SerializedObject) / Install + Build / throws |
| `Boot_PendingRun_IsSingleton` | built / Resolve twice / same instance |
| `Run_ResolvesSession_Scoped` | boot container, `CreateScope(RunInstaller.Install)`, pending set / Resolve<IRunSession>() twice / same instance; `IsRunning == false` |
| `Run_Random_SeededFromPendingRun` | pending.Set(oathbound, 123) / Resolve<IRandom>() / `Seed == 123` |
| `Run_WithoutPendingRun_ResolveRandomThrows` | pending not set / Resolve<IRandom>() / throws (VContainer wraps; assert inner `InvalidOperationException`) |
| `Run_EventsPortAndHub_SameInstance` | scope / Resolve<IDomainEvents>(), Resolve<DomainEventHub>() / same |
| `Run_IntentPortAndBuffer_SameInstance` | scope / Resolve<IIntentSink>(), Resolve<IntentBuffer>() / same |
| `Run_Snapshot_HasConfiguredCapacity` | scope / Resolve<WorldSnapshot>() / `EnemyCapacity == 64` |
| `Run_ScopeDispose_DisposesHub` | hub resolved / dispose scope / `hub.Subscribe` throws `ObjectDisposedException` |
| `PendingRun_ReadBeforeSet_Throws` | new / `CharacterId` / `InvalidOperationException` |

Containers are built with `new ContainerBuilder()` and `builder.Build()`; run scopes with `container.CreateScope(b => RunInstaller.Install(b))`. No `LifetimeScope` MonoBehaviours in these tests.

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- `LifetimeScope` subclasses and scenes — M0-13.
- `SceneLoader`, `BootFlow` — M0-13.
- Anything that ticks — M0-16 `RunTicker`.

## As built

_Filled at merge._

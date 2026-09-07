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
6. `IRandom` via factory: resolve `PendingRun`; if `IsSet` → `new SeededRandom(pending.Seed)`. If **not** set (pressing Play directly in the Run scene, M0-13 rule 1), fall back to `Environment.TickCount` and log one warning naming the fallback — never throw. `RunTicker` (M0-16) applies the same fallback for the character: `pending.IsSet ? pending.CharacterId : catalog.Characters[0].Id`.
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
| `Run_WithoutPendingRun_UsesFallbackSeed` | pending not set / Resolve<IRandom>() / resolves; `LogAssert.Expect(LogType.Warning, …)` consumed |
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

Four files, exactly the Files table. 11 new EditMode tests, 131 in the suite, green; zero errors, zero analyzer warnings.

**Deviations:**

1. **`WorldSnapshot` is registered through a factory at `Lifetime.Scoped`, not as an instance.** Rule 5 says "`WorldSnapshot` instance"; the section header says "all `Lifetime.Scoped`". VContainer's `RegisterInstance` is hardwired to `Lifetime.Singleton` — `InstanceRegistrationBuilder` passes it to its base constructor — so the two cannot both hold. Resolved toward the header. Behaviour is identical either way (a Singleton registered in a child scope's own registry resolves scope-locally), but the label would otherwise have been the one registration in the block that did not mean what it said.
2. **Three guards in `BootInstaller.Install` that rules 1–2 do not list**: null `builder`, null `definitions`, and a null element in `definitions`. All at a public boundary in M0-09's sense. The element guard is load-bearing rather than decorative — see *Learned*.
3. **The element guard is spelled `definition == null`, not `is null`.** `CharacterDefinition` is a `UnityEngine.Object`, whose lifetime operator `is null` bypasses; a destroyed asset would read as non-null and fail an NRE later. Also what `UNT0029` asks for.
4. **A private nested `RunStartedProbe` struct in the fixture.** `Run_ScopeDispose_DisposesHub` needs some `struct` to subscribe with, and using a real run event would couple the row to the event module for no gain. Second use of M0-10's nested-test-helper precedent.
5. **`PendingRun_ReadBeforeSet_Throws` also covers `Set` and `Clear`.** The row names only the throw, but rule 9's other two sentences have no row of their own and `Clear` is the half most likely to be dropped in a refactor. Assertions inside a row that already owns the behaviour, per M0-05 — the Tests table did not grow.
6. **`Boot_InvalidDefinition_FailsBuild` also asserts `builder.Count == 0`.** "Throws" is the row; that nothing was registered is what makes the failure safe, and it is what says the catalog is built before anything is installed.
7. **No LINQ.** Rule 1 sketches `definitions.Select(d => d.ToSpec())`; built as a `for` loop, which is also where the index in the null-element message comes from.

**Learned:** see the PROGRESS entry for 2026-09-07 · M0-12. The finding that matters beyond this task: **VContainer only disposes what it constructed**, so an adapter that owns resources must be registered as a type, never through `RegisterInstance`.

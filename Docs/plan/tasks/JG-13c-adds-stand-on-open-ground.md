# JG-13c — Adds stand on open ground

**Size:** M · **Depends on:** JG-13b (both edit `MotherPitcherBehaviour.cs`) · **Branch:** `dev` (direct)
**Design refs:** AR §4.2, ADR-0003, ADR-0011; GD §9.1 rule 4; JG-12b rule 2

## Goal

Core places Mother Pitcher's adds on ground Unity reports open, 4–8 m out, clear of enemies and the
player, seeded; a 4 m ring until JG-13d fills the report.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/OpenGroundPicker.cs` | Core | the placement rule |
| `Core/Ai/ISummonPlacer.cs` | Core | an inner boss behaviour that places its own adds |
| `Tests/Core/Ai/OpenGroundPickerTests.cs` | Tests.Core | rules 2–5, 8 |
| `Tests/Core/Ai/MotherPitcherPlacementTests.cs` | Tests.Core | rules 6–7 |
| *small edits* | | `Core/Run/WorldSnapshot.cs`, `Core/Ai/IEnemyBehaviour.cs` (`EnemyTickContext`), `Core/Run/RunSession.cs` (rule 1); `Core/Ai/BossBehaviour.cs` (rule 6); `Core/Ai/MotherPitcherBehaviour.cs` (rule 7); `Tests/Core/Run/WorldSnapshotTests.cs`, `BossDispatchTests.cs` (rule 1 rows) |

## Public API

```csharp
public IReadOnlyList<Vector3> OpenGround = NoSpawnPoints;   // WorldSnapshot
public IReadOnlyList<Vector3> OpenGround { get; }          // EnemyTickContext, optional last arg
public interface ISummonPlacer { Vector3 PlaceAdd(in EnemyTickContext ctx, Vector3 ring); }
public sealed class OpenGroundPicker
{
    public const float MinDistance = 4f, MaxDistance = 8f, EnemyClearance = 1.5f, PlayerClearance = 3f;
    public OpenGroundPicker(IRandomStream random);
    public bool TryPick(in EnemyTickContext ctx, Vector3 centre, Vector3 player,
        ReadOnlySpan<Vector3> reserved, out Vector3 spot);
}
```

## Behaviour

1. **The report reaches behaviours.** `WorldSnapshot.OpenGround` is empty by default and after
   `Clear`; `RunSession.Tick` hands it to every `EnemyTickContext`.
2. **The rule.** A spot qualifies when it is in `OpenGround`, `MinDistance`–`MaxDistance` from
   `centre`, ≥ `EnemyClearance` from every live enemy and `reserved` spot, ≥ `PlayerClearance` from
   `player` (all XZ).
3. **Seeded.** One `NextFloat` picks index ⌊r·n⌋ among qualifying spots in list order.
4. **Crowded.** None qualifies but some lie in the band: one draw among the band's spots.
5. **No ground.** Nothing in the band, or an empty report: `false`, no draw.
6. **A boss asks its inner.** `Summon` places each add at `PlaceAdd(ctx, ring)` when `Inner` is an
   `ISummonPlacer`, else on the 3 m ring; the Warden and Croaker draw nothing.
7. **Mother places by the rule.** Her buds and phase Frogs stand where `TryPick` says, each spawned
   before the next pick so it counts as an enemy; on `false`, at the ring's direction `MinDistance` out.
8. **`TryPick` allocates nothing** over a 300-spot report.

## Tests

| Test | Given / When / Then |
|---|---|
| `Snapshot_OpenGround_EmptyUntilFilledAndAfterClear` | new, filled, cleared / read / empty, list, empty |
| `Dispatch_MotherSummons_StandOnTheReportedGround` | stage-10 session, lattice reported / first ticks / buds on lattice points |
| `Pick_RefusesEveryUnclearSpot` | 3.9 m, 8.1 m; 1.4 m from an enemy, a reserved spot; 2.9 m from the player / pick / never chosen |
| `Pick_SameSeedSameSpot` | two pickers, one seed / pick / same spot, one draw each |
| `Pick_Crowded_FallsBackToTheBand` | band spots all near the player / pick / true, a band spot |
| `Pick_NoGround_FalseWithoutADraw` | empty; all at 2 m / pick / false, stream unmoved |
| `Boss_WithoutAPlacer_KeepsTheRing` | Warden summons / tick / 3 m ring as today |
| `Mother_Summons_StandOnOpenGround` | lattice, player near / opening tick / buds 4–8 m out, ≥ 1.5 m apart, ≥ 3 m from player |
| `Mother_NoGround_SummonsFourMetresOut` | empty report / opening tick / buds on a 4 m ring |
| `Pick_AllocatesNothing` | warm, 300 spots / `AllocationAssert.None` / passes |

## Out of scope

- Unity filling `OpenGround` (JG-13d); pods (JG-13e); Warden/Croaker placement.

## As built

Built 2026-10-30. `OpenGround` resets with the snapshot and reaches each behaviour. The picker
counts qualifying spots, then selects in list order with one draw; crowded reports use the band,
empty bands draw nothing. XZ clearances include live enemies and reserved spots. Mother uses her
injected Misc stream for buds and phase Frogs, spawning each before the next pick; no report gives
a 4 m ring. Warden/Croaker retain their 3 m ring without placement draws. No Unity report writer
until JG-13d; pods remain JG-13e.

Verification: full Unity EditMode 4 680 passed, 0 failed (names: none), 1 known inconclusive;
all 10 spec rows pass, including `AllocationAssert.None` over 300 spots. Compiler errors 0,
new analyzer warnings 0; whitespace format clean (11 C# files). TimeManager reverted.
Outside Files table: this footer, PROGRESS, ROADMAP and four required `.meta` companions only.
Manual Editor checks owed: stage 10 opening/phase adds at 4 m with the empty report, buds still
face Mother; Warden/Croaker summon rings unchanged. Open-ground/stone clearance awaits JG-13d.

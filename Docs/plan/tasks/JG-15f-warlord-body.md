# JG-15f — The Warlord's body: animator, guard arc, daze and shatter

**Size:** M · **Depends on:** JG-15d, JG-15e · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 7; AR §18.2 (animation never gates damage; no Animation Events)

## Goal

The Warlord moves like the Mossguard with its own moves: every telegraph plays a readable clip, the
ground shows its guarded front, a stone daze and the shield's shatter are visible at phone size.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Animation/Controllers/AC_MossguardWarlord.controller` | — | **New.** `AC_Mossguard`'s layers + a `Move` int and the move states |
| `Game/Views/WarlordView.cs` | Game | **New.** Guard-arc wedge, daze, shatter; on the variant's root |
| `Prefabs/Vfx/VFX_ShieldShatter.prefab` | — | **New.** Pooled one-shot: shards and dust |
| `Tests/Game/Views/MossguardWarlordBodyTests.cs` | Tests.Game | **New** |
| `Tests/Game/Views/WarlordViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `Mossguard_Warlord.prefab` (controller, `WarlordView`); `RunScope` injects `WarlordView` like the Mossguard's views |

## Public API

```csharp
namespace Soulvail.Game.Views {   // block namespace (Traps §5)
  public sealed class WarlordView : MonoBehaviour, IPoolable {
    [Inject] public void Construct(DomainEventHub hub);
    public float WedgeHalfArcDeg { get; }   // what is drawn now
    public bool IsDazed { get; }
    public bool IsShattered { get; }
    public void Step(float dt);
  }
}
```

## Behaviour

1. **Clips** (Rig_Medium, `EnemyAnimatorView`'s `Move` path, write-defaults off, root motion off):
   `Move` 1 sweep and 5 combo first → `Melee_2H_Attack_Slice`; 6 combo second →
   `Melee_2H_Attack_Chop`; 2 charge → `Melee_Blocking` held through wind-up and dash, legs
   `Running_A`; 3 war cry → `Skeletons_Taunt`; 4 lob → `Throw`. Each blow lands at its wind-up's
   end (the Mossguard's stab timing rule). Guard layer and legs as `AC_Mossguard`; death `Death_A`.
2. **Guard wedge:** a flat ground wedge at the feet, `Palette.Neutral` at 0.35 alpha, half-angle =
   the last `WarlordGuardArc`, eased over 0.15 s; 0 hides it. It follows the body's facing.
3. **Daze** (`WarlordDazed`): `Hit_B` then `Idle_B` for the seconds given, a dust puff at `At`, a
   small ring of three circling bone-white motes over the head; ends with the seconds.
4. **Shatter** (`WarlordShattered`): the `Shield` object hides, `VFX_ShieldShatter` plays once at
   it, the guard layer's weight goes to 0 for the rest of the life.
5. **Colour law:** no cyan anywhere; danger stays the existing lane, cone and ring telegraphs.
6. **Pooling:** `OnDespawn` restores shield, wedge, guard layer and motes; a fresh spawn is whole.
   Events for other ids are ignored.

## Tests

| Test | Given / When / Then |
|---|---|
| `WarlordBody_EveryMoveHasItsClip` / `WarlordBody_NoWriteDefaults_NoRootMotion` (1) |
| `WarlordBody_SweepLandsAtTheWindupsEnd` / `WarlordBody_ChargeHoldsTheBlock` (1) |
| `WarlordView_WedgeFollowsGuardArc` / `WarlordView_ZeroHidesWedge` (2) |
| `WarlordView_DazePlaysAndEnds` (3) |
| `WarlordView_ShatterHidesShieldOnce_PlaysVfx` (4) |
| `WarlordView_NoCyan` (5) |
| `WarlordView_DespawnRestores` / `WarlordView_OtherIdIgnored` (6) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 start at stage 15 (after 15g): each move's clip is readable before it lands;
   the wedge shows the guarded front and widens when a guard stands close.
2. **[Editor]** Charge into a stone: the daze reads; at 33 % the shield shatters visibly.
3. **[device]** All of the above at phone size (GD §9.1 rule 7).

## Out of scope

- Audio cues (no audio yet); the data and arena (15g); polish passes.

## As built

_Filled at merge._

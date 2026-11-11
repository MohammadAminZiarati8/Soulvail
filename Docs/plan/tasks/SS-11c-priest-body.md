# SS-11c — The body: prefab, animator, the lime mend beam

**Size:** M · **Depends on:** SS-11a, SS-11b · **Branch:** `ss-11c-priest-body`
**Design refs:** GD §8.11, §16.4, JG-16 (the shaman's lime)

## Goal

`JackalPriest.prefab` plays SS-11b's clips to core's beats. While it mends, a lime beam runs from the staff's sun disc to the ally and the disc glows. A broken beam snaps off with a short flicker.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/JackalPriest.prefab` | — | `Jackal.prefab`'s component set on `JackalPriest.fbx`, staff in `handslot.r`, plus `PriestMendView` |
| `Animation/Controllers/AC_JackalPriest.controller` | — | Locomotion (Idle/Walk/Run), Mend loop, Hit, Death |
| `Game/Views/PriestMendView.cs` | Game | reads `PriestMendStarted`/`Ended` for its id; beam, disc glow |
| `Materials/Enemies/M_MendBeam.mat` | — | unlit additive lime, scrolling, `_Intensity` |
| `Tests/Game/Views/PriestBodyTests.cs` | Tests.Game | view, prefab, controller rows |
| *small edits* | | `EnemyAnimatorView`: opt-in bool `Mending` (`ShinePhase`'s guard) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class PriestMendView : MonoBehaviour   // block namespace (Traps §5)
    {
        [SerializeField] private LineRenderer _beam;     // M_MendBeam, two points
        [SerializeField] private Transform _glow;        // the staff's Glow empty
        [SerializeField] private Renderer _disc;         // the staff, for the emissive glow
        [Inject] public void Construct(IEventSource events, IEnemyPositions positions); // whatever the views already read ally positions from
        public void Bind(int enemyId);
    }
}
```

## Behaviour

1. `PriestMendStarted`: the beam is on, from `_glow` to the ally's chest height, updated every frame. The disc's emission goes to the shaman's lime (`ShamanView`'s value, shared, not copied). `Mending` is true.
2. `PriestMendEnded`: `Done`, `Healed` and `Lost` fade the beam out over 0.2 s. `Broken` flickers it twice in 0.15 s, then off. The disc glow goes out, and `Mending` is false.
3. The beam is about 0.15 m wide, lime, unlit, scrolling toward the ally so its direction reads. It draws above the Sunscar storm (SS-08c's queue rule). Never cyan, gold or red-orange.
4. Pooled reuse and death turn everything off. A `MaterialPropertyBlock` per renderer, so the shared material never changes. No allocation per frame.
5. `AC_JackalPriest`: Locomotion by speed (`Idle_B` / `Walking_B` / `Running_A`), `Mending` true → `Ranged_Magic_Spellcasting` loop, false → Locomotion. `Hit_A`, `Death_A` as `AC_Jackal`'s. No write defaults.
6. The prefab: `EnemyHitFeedback` with `_telegraphGlow` 0 (it never attacks). The staff sits in `handslot.r` as the sling does.
7. `Jackal.prefab`, `Shaman.prefab` and every other enemy view are unchanged.

## Tests

| Test | Given / When / Then |
|---|---|
| `Mend_Started_BeamOn` | start event / step / beam on, ends at glow and ally |
| `Mend_BeamFollowsAlly` | ally moves 2 m / step / end point follows |
| `Mend_Done_Fades` | `Done` / 0.2 s / off |
| `Mend_Broken_Flickers` | `Broken` / sample over 0.15 s / two on-off flickers, then off |
| `Mend_OtherId_Ignored` | another id / publish / unchanged |
| `Mend_LimeIsTheShamans` | view vs `ShamanView` / read / same colour |
| `Mend_SharedMaterialUntouched` | full cycle / read asset / unchanged |
| `Mend_DrawsOverTheStorm` | beam material vs storm / queue / higher |
| `Animator_Mending_Set` | prefab / start, end / bool matches |
| `Controller_EveryStateIsRigMedium` | controller / read / rule 5's clips; no write defaults |
| `Prefab_WiresTheView` | prefab / load / refs set, staff in `handslot.r`, `_telegraphGlow` 0 |
| `Pooled_Reuse_ResetsOff` | reuse / bind / beam off, glow off |

## Manual verification (Editor / device)

Not in play until SS-11d, whose steps cover the body.

## Out of scope

- Data, roster, localisation (SS-11d). Any core change. Sound.

## As built

_Commit body._

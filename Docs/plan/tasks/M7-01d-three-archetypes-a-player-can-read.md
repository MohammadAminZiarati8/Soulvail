# M7-01d — Three archetypes a player can read

**Size:** M · **Depends on:** M7-01c · **Branch:** `m7-01d-three-archetypes-a-player-can-read`
**Design refs:** GD §8.1, §12.4, §16.2, §16.3, §16.4, §17.1; CC §3.6; AR §18.4 · **Ledger rows:** none moved

## Goal

The Lunger draws its lane before it dashes. The Warden wears its shield where its guard points. A hit
the shield turns away looks turned away rather than landed. A Weaver's children grow where it fell.
The three archetypes M7-01a to c built in core can now be read on screen, before they hurt anyone.

## What core already says, and what the views get wrong today

Every fact this task draws is already published. It is the views that misread one of them:

- **`LungeTelegraphed`** (M7-01a rule 4) carries origin, direction, length and duration, **and, as of
  this spec group, the lane's half-width** (rule 1). Nothing subscribes yet, and this task is the
  reader M7-01a named.
- **`EnemyDamaged(id, 0, fraction, killed: false)`** is what a guard (M7-01c rule 3) and a ward
  (M7-02c rule 8) publish for a hit turned away. **`EnemyHitFeedback.OnDamaged` never reads
  `Amount`**, so today a blocked hit flashes the body white exactly as a wound does, and
  `EnemyHealthBar` shows the bar for 2.25 s. A Warden struck face-on would look like it was being
  hurt. `DamageResult.Blocked`'s own remarks say *"views flash a shrug rather than a wound"*, and no
  shrug exists.
- **`TargetChanged.IsBlocked`** already draws the reticle's ✕ and thins its ring (M1-04). M7-01c is the
  first thing that raises it, and no row pins it.
- **`EnemyMoveIntent.FacingXZ`** already yaws the body's root (`EnemyView.Face`). The body is the
  built-in capsule, so a facing is invisible. The Warden's facing *is* its mechanic.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/TelegraphRings.cs` | Game | The lane: subscribes to `LungeTelegraphed`, and withdraws a lane when its Lunger dies (rules 1–2) |
| `Game/Views/TelegraphRingView.cs` | Game | `BindLane` — a yawed rectangle from the same quad (rule 1) |
| `Game/Views/EnemyHitFeedback.cs` | Game | The shrug (rule 3), the shield plate (rule 4), the arrival (rule 5) |
| `Tests/Game/Views/ArchetypeReadingTests.cs` | Tests.Game | **New.** Every rule below |
| *small edits* | Game, Prefabs, Docs | `Game/Authoring/EnemyLook.cs` — `GuardArcDegrees`; `Game/Authoring/EnemyDefinition.cs` — `ToLook` reads the guard block M7-01c authored; `Game/Views/EnemyHealthBar.cs` — a turned-away hit does not show the bar; `Game/Presentation/HapticsListener.cs` — nor buzz; `Game/Presentation/Palette.cs` — `Guard`; `Prefabs/Enemies/Enemy.prefab` — an inactive *Guard* child; `Tests/Game/Views/ReticleViewTests.cs` — `Reticle_ABlockedTargetIsCrossed`; `Tests/Game/Presentation/PaletteTests.cs` — `Guard` joins the lists it must. `LungeTelegraphed`'s half-width is M7-01a's to publish, written into that spec by M7-00b |
| *ripple* | Tests.Game | `EnemyLookTests.Assets_AreTellableApart` walks seven assets; `Telegraph_SixPrefabsShareOneMaterial` is untouched (no prefab is added) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class TelegraphRingView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// A lane for the Lunger <paramref name="ownerId"/>: a flat rectangle from <paramref name="origin"/> along <paramref name="directionXZ"/>,
        /// <paramref name="length"/> long and twice <paramref name="halfWidth"/> wide, filling over
        /// <paramref name="duration"/>. The same quad and material as a ring (rule 1).
        /// </summary>
        public void BindLane(int ownerId, Vector3 origin, Vector2 directionXZ, float length, float halfWidth, float duration, Color colour);

        /// <summary>The Lunger this lane belongs to, or 0 for a ring. What rule 2 withdraws by.</summary>
        public int OwnerId { get; }
    }
}

namespace Soulvail.Game.Authoring
{
    public readonly struct EnemyLook
    {
        public EnemyLook(Color tint, float bodyScale, float guardArcDegrees = 0f);

        /// <summary>The front arc a guard covers, or 0 for a body that wears no shield (rule 4).</summary>
        public float GuardArcDegrees { get; }
    }
}

namespace Soulvail.Game.Presentation
{
    public static class Palette
    {
        /// <summary>
        /// Turned away — a shield, a shrug, a ward (M7-02e). Steel, #8FA3B8: GD §16.4's "everything
        /// else" family, and far from danger, the player and the hit flash (rule 6).
        /// </summary>
        public static readonly Color Guard;
    }
}
```

```csharp
// Core, amended by M7-00b before M7-01a is built — see M7-01a's Public API:
public readonly struct LungeTelegraphed
{
    public LungeTelegraphed(int id, Vector3 origin, Vector2 directionXZ, float length, float halfWidth, float duration);
    /// <summary>Metres either side of the line a dash hurts in: the Lunger's <c>Spec.Reach</c>. 0.9.</summary>
    public readonly float HalfWidth;
}
```

## Behaviour

1. **The Lunger's lane is a rectangle on the floor, drawn from the pool that draws every ring.**
   `TelegraphRings` subscribes to `LungeTelegraphed` and rents a `TelegraphRingView` bound as a lane.
   - **Placement:** its centre is `origin + direction × length / 2`, and it is `2 × HalfWidth` wide
     and `length` long, yawed along the direction and still flat (`Euler(90, yaw, 0)`).
   - **Look:** it uses `Palette.Danger` and fills over `Duration`, which is the spawn ring's fill
     because both say *"this is about to happen here"*.
   - **Same pool, prefab and material.** The untextured quad that makes every *"ring"* a square
     (Known issue 1) is exactly the right shape for a lane, and `Telegraph_SixPrefabsShareOneMaterial`
     stays true.
   - **The half-width is core's number, carried on the event.** The view has no catalog, and a lane
     drawn to a view-side constant would drift from `Spec.Reach`, which is the band the dash really
     hurts in (M7-01a rule 6). M7-00b amended M7-01a's event to carry it before either was built.
2. **A lane is withdrawn when its Lunger dies.** AR §18.4 says *"a telegraph is a promise"*, and a lane
   left on the floor after its Lunger has dissolved promises a dash that will never come. So
   `TelegraphRings` subscribes to `EnemyDied` and releases any live lane whose `OwnerId` is that id.
   **A ring has owner 0 and is never withdrawn**, because a spawn ring and a blast ring belong to no
   body. A Lunger that dashes and lives needs nothing: its lane runs out with the windup, as
   `EnemyTelegraph`'s *"no telegraph ended"* remark intends.
3. **A hit turned away shrugs rather than wounds.** When `EnemyHitFeedback.OnDamaged` sees an event
   with `Amount == 0` and not `Killed`, it flashes `Palette.Guard` for the flash time instead of
   `Palette.HitFlash`. The damage tint is left alone because the fraction did not move.
   - `EnemyHealthBar` ignores that event, so a turned-away hit does not show a basic enemy's bar. An
     Elite's bar is shown anyway.
   - `HapticsListener` does not buzz for it. GD §16.3's *"light on hit dealt"* is for a hit that was
     dealt.
   - **A killing blow still starts the dissolve** whatever its amount.
   - **The reticle's ✕ is already built.** `Reticle_ABlockedTargetIsCrossed` pins it, since M7-01c
     is the first thing ever to reach it.
4. **The Warden wears its shield where its guard points.** `EnemyLook` gains `GuardArcDegrees`, read by
   `EnemyDefinition.ToLook` from the guard block M7-01c authored, and zero for every other archetype.
   - `Enemy.prefab` gains an inactive *Guard* child: a thin box in front of the capsule, on
     `M_BoneGrey`, coloured `Palette.Guard` through its own property block.
   - `SetArchetypeLook` enables it for a look with an arc and sizes it to the arc's chord at the
     capsule's radius. At 120° that spans the front two-thirds of the body's width.
   - `ResetVisuals` disables it, so a recycled agent that comes back as a Husk wears no shield.
   - **No new facing is needed.** `EnemyView.Face` already yaws the root to the intent's facing, and
     M7-01c rule 2 makes that facing the guard's own, so the plate and the arc the guard tests are
     one direction.
5. **A body arrives rather than blinks into being, which is what makes a split readable.**
   `SetArchetypeLook` starts an arrival. The *Mesh* child grows from 0.4 of its size to full over
   0.2 s, while the root and its colliders stay at full size, so a cone can hit a body on its first
   frame.
   - **Every rental gets it**, so a composed body arrives at the end of its spawn ring, a boss's add
     at the end of the beat, and a Weaver's two children beside the corpse.
   - **The split therefore reads with no new event.** Two small pale bodies grow on either side of a
     large pale one as it dissolves upward: M7-01b's colour at two sizes, placed by M7-01b rule 5's
     ring.
   - A spawn flag that told the view *"born of a split"* was weighed and refused. It would be a core
     edit in a view task, and the arrival is right for every body anyway.
6. **One colour means "turned away".** `Palette.Guard` is steel `#8FA3B8`. It is used for the plate,
   the shrug and, from M7-02e, a ward, so a player learns one colour for *"hits do not land here"*.
   It sits in GD §16.4's desaturated *"everything else"* family, at least 0.15 from `Danger`, the
   player's cyan and `HitFlash` by `PaletteTests`' own distance. A shrug in the player's cyan would say
   the player was protected, and one in white would say the hit landed.
7. **Silhouettes are not solved here, and this task says so.** GD §17.1 wants every enemy
   *"distinguishable as pure black shapes"*, which a capsule at seven scales cannot be. The look stays
   tint, scale, the Warden's plate and the Lunger's lane. Bodies are **M7-00e's** art ruling, and the
   KayKit skeletons in the parking lot are one candidate. `Assets_AreTellableApart` walks all seven
   archetypes: Husk, Spitter, Bloater, Lunger, Weaver, Weaverling and Warden. The tints M7-01a to c
   authored stand unless that row fails, in which case the losing tint moves and *As built* says which.
8. **Nothing on a frame path allocates.** A lane is a pooled rental, the arrival is a float on a
   component that already ticks, and the shrug is the existing property-block write with another
   colour.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Lane_IsARectangleAlongTheDash` | `LungeTelegraphed` from (0, 0, 0) east, 15 long, half-width 0.9 / — / one live view centred at (7.5, 0, 0), scale (1.8, 15), yawed east, flat — rule 1 |
| `Lane_IsDangerAndFills` | the same / half its duration / `Colour` is `Palette.Danger`, `Fill` 0.5 — rule 1 |
| `Lane_RunsOutWithTheWindup` | the same / its duration / released to the pool — rule 1 |
| `Lane_UsesTheRingPoolAndPrefab` | a lane and a ring / — / one pool, both `TelegraphRingView` — rule 1 |
| `Lane_IsWithdrawnWhenItsLungerDies` | a live lane for id 7 / `EnemyDied(7)` / released — rule 2 |
| `Lane_ADifferentDeathLeavesIt` | a live lane for id 7 / `EnemyDied(8)` / still live — rule 2 |
| `Ring_IsNeverWithdrawnByADeath` | a spawn ring and a blast ring / any `EnemyDied` / both live — rule 2 |
| `Shrug_ATurnedAwayHitFlashesGuard` | `EnemyDamaged(id, 0, 1, false)` / — / the body's colour is `Palette.Guard` for the flash, then its tint — rule 3 |
| `Shrug_AWoundStillFlashesWhite` | `EnemyDamaged(id, 5, 0.8, false)` / — / `Palette.HitFlash` — rule 3 |
| `Shrug_LeavesTheTintAlone` | a Husk at 0.5, then a shrug / — / the tint reads 0.5 throughout — rule 3 |
| `Shrug_DoesNotShowABasicEnemysBar` | a basic enemy / a shrug / `IsShown` false — rule 3 |
| `Shrug_DoesNotBuzz` | a shrug / — / no haptic offered — rule 3 |
| `Reticle_ABlockedTargetIsCrossed` | `TargetChanged(isBlocked: true)` / — / both strokes on, the ring at its blocked width — rule 3 |
| `Guard_AWardenWearsItsPlate` | the Warden's look (arc 120) / `SetArchetypeLook` / *Guard* active, width the 120° chord, colour `Palette.Guard` — rule 4 |
| `Guard_EveryOtherArchetypeWearsNone` | each other shipped look / — / *Guard* inactive — rule 4 |
| `Guard_ARecycledBodyTakesItOff` | a Warden released and rented as a Husk / — / *Guard* inactive — rule 4 |
| `Guard_FacesWhereTheIntentFaces` | a Warden view / an intent facing north / the plate's forward is north — rule 4 |
| `Look_TheWardenAssetCarriesItsArc` | `Warden.asset` / `ToLook` / arc 120; `Husk.asset` 0 — rule 4 |
| `Arrival_TheMeshGrowsAndTheColliderDoesNot` | a fresh rental / 0.1 s / mesh at 0.7 of full, the capsule collider at full — rule 5 |
| `Arrival_IsFullAfterItsTime` | / 0.2 s / mesh at full, and a telegraph swell after it scales from full — rule 5 |
| `Palette_GuardIsFarFromEverythingReserved` | `Guard` / against `Danger`, `Player`, `HitFlash` / each ≥ 0.15 — rule 6 |
| `Assets_AreTellableApart` | *(walks seven)* every shipped archetype pair / — / distinct by tint or scale — rule 7 |
| `ArchetypeViews_AllocateNothing` | 1 000 lanes, shrugs and arrivals on pooled views / after the first / zero — rule 8 |

**Guard rows are implied, not listed:** `BindLane`'s zero direction, non-positive length or half-width.

## Manual verification (Editor / device)

1. **[Editor]** Stage 6. *Expected: a red-orange strip on the floor from the Lunger, filling for most
   of a second, then the dash along it. Killing the Lunger mid-windup takes the strip away.*
2. **[Editor]** Stage 11, face-on to a Warden. *Expected: a steel plate on the side facing you that
   turns as it turns; your hits flash it steel, not white, and no bar appears. Circling behind: white
   flashes and the bar.*
3. **[Editor]** Stage 8, kill a Weaver. *Expected: two small pale bodies growing either side of it as
   it dissolves.*
4. **[device]** The lane and the plate at 400 dpi in daylight — deferred with [M7 row 1](../ROADMAP.md#carry-forward-into-m7).

## Out of scope

- **Bodies.** Rule 7; M7-00e's ruling.
- **The circle texture that would make a ring round.** Known issue 1 is art, and M7-00e's.
- **Anything about an Elite or an affix.** [M7-02e](M7-02e-an-elite-you-can-see.md).

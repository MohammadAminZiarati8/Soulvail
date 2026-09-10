# M2-12a — Where the thing you cannot see is: screen-edge arrows, and a focus that reads as held

**Size:** M · **Depends on:** M2-07b (something that hurts you from off screen) · **Branch:** `m2-12a-threat-arrows-and-held-focus`
**Design refs:** GD §12.4 (the on-screen rule, the thumb rule), §16.1 (off-screen threats), §16.4 (colour language), §7.3 (the 8-second stall); CC §3.4, §3.5; AR §3, §14, §18.1, §18.2 · **Ledger rows:** **12** and **14** — both, together, because they are one question

## Goal

Two things the player cannot see stop being invisible in the same breath and in the same visual language: an enemy that can hurt you from off screen, and the enemy you tapped that is too far away to shoot.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/ThreatArrows.cs` | Game | The census of off-screen things worth an arrow, and where on the edge each one goes |
| `Tests/Game/Presentation/ThreatArrowsTests.cs` | Tests.Game | The projection, the edge rect, the thumb exclusion, the stall rule, the census |
| `Game/Views/ReticleView.cs` | Game | **Rewrite**: a second, dimmer marker for a focus that is held but not being shot at |
| *small edits* | | `TargetChanged` + `HeldFocusId` (rule 1); `PlayerCombat`'s one publish site derives it; `Reticle.prefab` gains the second ring and chevron; `Hud.prefab` gains the arrow root; `RunScope` + the arrow prefab; `RunInstaller` registers `ThreatArrows`; `DebugOverlay` shows the arrow count |
| *assets* | | `Prefabs/UI/ThreatArrow.prefab` — listed, not counted |
| *ripple* | | `PlayerCombatTests` and `ReticleView`'s existing rows gain the fourth field; every `new TargetChanged(...)` in the test assemblies takes one more argument, and the compiler enumerates them |

Only these files change. Anything else is a deviation: say so in *As built*.

**Not split from [M2-12b](M2-12b-telegraph-rings.md) by row.** Rows 12 and 14 stay together in this one task because the ledger's whole complaint is that answering them separately gets two indicators that do not agree. What went to 12b is the *pooled ground decal* family — the spawn ring and the blast ring — which shares M2-09's machinery and none of this task's question. Both specs cite GD §16.4, which is where the two vocabularies are reconciled.

## Public API

```csharp
namespace Soulvail.Core.Events;

// TargetChanged (added) — the fourth field, and the whole of ledger row 12's fix in core.
/// The enemy the player has focused **when it is not the one being shot at** — a focus held on
/// something out of `acquireRange`, which scoring has stood down from (CC §3.4, `Targeter.Select`).
/// −1 when no focus is held, and −1 when the focus *is* the current target, because that case is
/// already <see cref="IsFocused"/>. The two are never both set.
public readonly int HeldFocusId;

public TargetChanged(int id, bool isFocused, bool isBlocked, int heldFocusId);
```

```csharp
namespace Soulvail.Game.Presentation;

/// The screen-edge arrows GD §16.1 lists and GD §12.4's on-screen rule requires. A listener and a
/// projector: it owns no simulation, answers core nothing, and decides only where on the border of
/// a phone screen a thing that is off it should be pointed at from.
public sealed class ThreatArrows : IDisposable
{
    /// Metres. An enemy further than this is off screen but cannot reach you, so it is scenery,
    /// not a threat — rule 4.
    public const float ThreatRange = 16f;

    /// GD §7.3: when this many or fewer survive and `StallTime` passes, every survivor gets an
    /// arrow whatever the range.
    public const int StallSurvivors = 3;
    public const float StallTime = 8f;

    public ThreatArrows(
        IObjectResolver resolver,
        RectTransform arrowPrefab,
        RectTransform parent,
        Camera camera,
        EnemyViews views,
        DomainEventHub hub,
        int prewarm = 8);

    /// Arrows on screen right now. `DebugOverlay` reads it.
    public int Count { get; }

    public void Dispose();
}
```

```csharp
namespace Soulvail.Game.Views
{
    // ReticleView (added serialized fields — the marker is geometry the view already builds)
    //   _heldVisual          GameObject  the second marker, toggled like _visual
    //   _heldRing            LineRenderer
    //   _heldChevron         LineRenderer
    //   _heldScale   = 0.8f              fraction of _radius, so the two never read as one object
    //   _heldAlpha   = 0.35f             below _autoAlpha (0.6), which is below the focused 1.0
}
```

## Behaviour

**Row 12 — a tap that registered**

1. **`TargetChanged` gains one field and `PlayerCombat` derives it at the publish site it already has:** `Targeter.FocusedTargetId` when a focus is held *and* it is not `CurrentTargetId`, else −1. Nothing new is computed and nothing in core changes its mind — both values are already public on `Targeter`, and the event has simply never carried the second one.
2. **It cannot go stale, and that is provable rather than hoped.** `Targeter.ChangedThisTick` is the triple (current, blocked, focused), and `HeldFocusId` is a pure function of two members of that triple — so every transition of `HeldFocusId` is accompanied by a publish, by construction. A focus landing out of range moves `FocusedTargetId`; it coming into range and becoming current moves `CurrentTargetId`; the 2 s drop moves `FocusedTargetId` back. There is no fourth way for it to change.
3. **The reticle grows a second marker: dimmer, smaller, and it does not pulse.** Three levels of the same cyan, and the ordering is the information — **focused-and-firing** is bright and pulsing, **auto-selected** is subtle and still, **held-but-inactive** is fainter still and parked on the enemy the player actually tapped. A chevron sits over it, because the chevron is what CC §3.5 makes the "you picked this" glyph and the player picked this one; it is the *ring* that says "and the gun is on it", which this one does not claim.
   **The two rejected alternatives were costed in the ledger and are recorded here so the choice is not re-opened:** holding the focus regardless of range reintroduces the turn-away M1-04 deliberately rejected (`Targeter.Select` stands the override down out of range so *"the player keeps the focus, the gun does not stand idle"*), and raising `acquireRange` moves auto-targeting everywhere to fix one indicator.
4. **Core still decides nothing new.** CC §3.4's rules are untouched: the focus still expires after 2 s out of range, the gun still shoots what scoring picked, and `Targeter` gains not one line. Row 12 was never a targeting bug — it was that the game had no way to *say* what it had already decided, which is exactly what CC §3.5 exists to prevent.

**Row 14 — damage from somewhere you were not looking**

5. **An arrow for every living enemy that is outside the camera frustum and within `ThreatRange`.** GD §12.4: *"No damage originates from outside the camera frustum without a visible edge indicator."* 16 m is the Spitter's 14 m standoff plus margin — the longest reach any archetype in M2 has — and it is deliberately a single number rather than a per-archetype question: a Husk closing at 13 m off screen is also worth an arrow, and an archetype table the presentation layer has to consult is a second copy of the roster.
6. **The threat is the shooter, not the shot.** A bolt's *target* is the ground under the player and is therefore on screen (M2-07a rule 4 — it does not track), so the damage lands where you can see it; what is off screen is where it came from. One arrow on the Spitter answers the rule, and a bolt outliving its shooter (M2-07a rule 5) needs no arrow of its own because it is already in the frame it will land in.
7. **GD §7.3's stall: `StallSurvivors` or fewer alive for `StallTime` seconds, and every survivor gets an arrow regardless of range.** *"If fewer than 3 enemies remain and 8 seconds pass, survivors get a waypoint arrow."* This is the answer M2-05 refused to paper over with a wave timeout — the fix for hunting the last Husk is telling the player where it is, not spawning something else. The timer is a **cosmetic view timer**, which AR §18.2 names as the deliberate exception to snapshot-`Dt`: nothing about it is simulated and no outcome depends on it.
8. **Arrows are pinned to a border rect that excludes the bottom corners.** GD §12.4's thumb rule — *"nothing critical may resolve in the bottom-left or bottom-right ~15 % of the screen"* — so an arrow whose natural edge point lands under a thumb slides along the border to the nearest point that is not. Inset by `Screen.safeArea`, because a notch eats an arrow as effectively as a thumb does.
9. **Colour is the vocabulary, and the two rows use opposite ends of it deliberately.** GD §16.4 reserves saturated red-orange for danger *"and nothing else, ever"*, and cyan for the player. So a threat arrow is red-orange and **the held-focus marker is cyan** — and when the held-focus enemy is itself off screen, `ThreatArrows` draws it a **cyan** arrow. One renderer, one edge rect, two meanings, and an enemy that is both gets both: *the thing you picked is over there, and it can hurt you.* That is what "answer rows 12 and 14 together" comes to in the end — not one indicator, but one grammar.
10. **Proximity is alpha and size, not a number.** A threat at 15 m is faint and small, one at 6 m is bright and large; nothing on the arrow is text. GD §11.3 and P1: a phone screen is small and eight arrows with distances on them is noise.
11. **A fixed set of instances, not a `ViewPool`, and it owns its own `LateUpdate`.** `prewarm` arrows are instantiated at construction and toggled active — **not** `ViewPool<T>`, which constrains `T : Component, IPoolable` and exists so that a rented body can be told to forget its last life. An arrow remembers nothing: every frame rewrites its position, rotation, colour and alpha from scratch, so an `IPoolable` pair of callbacks would be ceremony around an empty method. Named rather than left looking like an oversight beside M2-09 and M2-12b, which both do use the pool and both need to.
    `LateUpdate` for `ReticleView`'s reason: the bodies move inside `RunTicker.Tick`, so an arrow positioned in `Update` points at where its threat was last frame. It answers core no fact, so AR §18.1's *"a view that answers core with a fact cannot own its own `Update`"* does not apply — worth saying, because it is the first view in the project that legitimately owns one.
12. `Dispose` unsubscribes and disposes the pool. Allocates nothing per frame after prewarm: the census is a preallocated array sized to the enemy cap, the projection is struct arithmetic.

## Tests

| Test | Given / When / Then |
|---|---|
| **Row 12 — core** | |
| `Focus_OutOfRange_CarriesHeldId` | focus an enemy at 14 m, `acquireRange` 12, another at 5 m / `Tick` / `TargetChanged.Id` is the near one, `IsFocused` false, `HeldFocusId` is the far one (rule 1) |
| `Focus_InRange_HeldIdIsMinusOne` | focus an enemy at 5 m / `Tick` / `IsFocused` true, `HeldFocusId` −1 (rule 1) |
| `Focus_None_HeldIdIsMinusOne` | no focus / `Tick` / `HeldFocusId` −1 |
| `Focus_HeldAndFocusedAreNeverBothSet` | every combination the targeter can reach / — / not both (rule 1) |
| `Focus_HeldIdPublishesOnEveryTransition` | focus out of range, walk it into range, walk it out, let it expire / — / a `TargetChanged` at each of the four, and `HeldFocusId` correct in each (rule 2) |
| `Focus_TargeterUnchanged` | the whole M1-04 suite / — / green, unmodified (rule 4) |
| **Row 12 — the reticle** | |
| `Reticle_ShowsHeldMarkerOnTheTappedEnemy` | `HeldFocusId` 7, `Id` 3 / — / the main ring is on 3, the held marker on 7 |
| `Reticle_HeldMarkerDoesNotPulse` | as above / several frames / the held marker's scale is constant, the main ring's is too (only `IsFocused` pulses) |
| `Reticle_HeldMarkerIsFainterThanTheAutoRing` | as above / — / its alpha is below `_autoAlpha` (rule 3) |
| `Reticle_HeldMarkerHidesWhenTheFocusLands` | `HeldFocusId` 7 → `Id` 7, `IsFocused` true / — / one bright pulsing ring, no second marker |
| `Reticle_HeldMarkerHidesWhenTheBodyGoes` | `HeldFocusId` 7, enemy 7 despawns / `LateUpdate` / hidden, no throw (the existing miss-is-not-an-error rule) |
| `Reticle_AllocatesNothingPerFrame` | both markers up, warm-up / 10 000 × `LateUpdate` / allocated-bytes delta == 0 |
| **Row 14 — the arrows** | |
| `Arrow_ForAnOffScreenEnemyInRange` | an enemy behind the camera at 12 m / — / one arrow, pointing at it |
| `Arrow_NoneForAnOnScreenEnemy` | an enemy in frame at 12 m / — / no arrow (rule 5) |
| `Arrow_NoneBeyondThreatRange` | off screen at 17 m / — / no arrow |
| `Arrow_AppearsAsItClosesToRange` | off screen at 17 m, walks to 15 / — / an arrow appears |
| `Arrow_PointsAtTheThreat` | off screen to the left / — / the arrow is on the left border, rotated towards it |
| `Arrow_NoneForABoltInFlight` | a bolt in the air whose shooter is dead and whose target is on screen / — / no arrow: the threat is the shooter, and this shot lands where you can see it (rule 6) |
| `Arrow_BehindTheCameraIsNotMirrored` | a threat directly behind / — / the arrow is at the bottom edge, not the top (the classic projection sign bug) |
| `Arrow_AvoidsTheThumbCorners` | a threat whose natural edge point is the bottom-left corner / — / the arrow sits outside the excluded 15 %, still on the border (rule 8, GD §12.4) |
| `Arrow_RespectsTheSafeArea` | a simulated notch / — / no arrow inside it (rule 8) |
| `Arrow_FadesWithDistance` | the same threat at 15 m and at 6 m / — / alpha and scale both larger at 6 (rule 10) |
| `Arrow_IsDangerColoured` | a threat arrow / — / GD §16.4's red-orange (rule 9) |
| `Arrow_HeldFocusIsCyan` | `HeldFocusId` 7, enemy 7 off screen / — / one **cyan** arrow on it (rule 9) |
| `Arrow_HeldFocusAndThreatBothDrawn` | enemy 7 is the held focus **and** within `ThreatRange` off screen / — / it gets the cyan arrow; a separate off-screen Spitter gets a red-orange one (rule 9) |
| `Arrow_ReleasedOnDespawn` | an arrow up / `EnemyDespawned` / `Count` 0, the pool has it back |
| `Stall_ArrowsForAllSurvivorsAfterEightSeconds` | 2 alive, both off screen at 30 m / 7.9 s / no arrows; 8.1 s / two arrows (rule 7) |
| `Stall_ResetsWhenTheWaveGrows` | 2 alive for 6 s, then 5 spawn / +4 s / no stall arrows |
| `Stall_DoesNotFireWithFourAlive` | 4 alive for 20 s / — / no stall arrows (rule 7) |
| `Arrows_PrewarmInstantiatesUpFront` | prewarm 8 / ctor / 8 inactive instances, and the first 8 arrows instantiate nothing (rule 11) |
| `Arrows_BeyondPrewarm_GrowOnce` | prewarm 2, three threats / — / a third instance is made and kept; the next frame with three threats instantiates nothing (rule 11) |
| `Arrows_AllocateNothingPerFrame` | 8 up, warm-up / 10 000 × `LateUpdate` / allocated-bytes delta == 0 |
| `Dispose_UnsubscribesAndDestroys` | two up / `Dispose`, then `EnemySpawned` / nothing rented, no throw |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Tap an enemy well outside 12 m. A faint cyan ring and chevron appear **on it**, the bright ring stays on whatever the gun is shooting, and walking towards it hands the bright ring over. That is ledger row 12 — the CC §8 row that failed on behaviour in M1-09 and again in M1-21 — being answered.
2. **[Editor]** Tap it and then stand still for more than two seconds. The faint marker disappears when the focus expires (CC §3.4), and nothing else changes. The two-second rule is untouched; only its visibility is new.
3. **[Editor]** Let a Spitter plant behind the camera and throw. A red-orange arrow is on the screen edge before the bolt lands — GD §12.4's on-screen rule, and the first archetype in this project that could break it.
4. **[Editor]** Kill a stage down to two survivors and hide from them. After eight seconds both get arrows, wherever they are (GD §7.3). Before eight seconds, nothing.
5. **[Editor]** Drive a threat around you in a circle. The arrow slides along the border and **skips the bottom two corners** rather than sitting under a thumb.
6. **[device]** The whole of this task, twice over: whether an edge arrow reads at a phone's size and in glare, and whether the faint held marker is legible at 40 % HUD opacity against a busy arena. Also whether a threat arrow ever reads as *danger on the screen edge* rather than *danger over there*. Deferred with the rest of the device list.

## Out of scope

- **Spawn telegraph rings and the blast ring** — [M2-12b](M2-12b-telegraph-rings.md). Ground decals with a pool, not screen-space indicators.
- **A minimap.** GD §16.1 has no room for one, and the arrows exist precisely so a 6-inch screen does not need one.
- **Arrows for anything that is not an enemy** — a Gate, a pickup, a Sanctum. The rule they satisfy is about *damage*; a waypoint system is M6's if anything ever needs one.
- **Damage-direction indicators** (the "you were hit from there" arc). A different mechanic answering a different question — after the hit, not before it — and GD §16.3's checklist does not list one.
- **Making the focus reach further.** Ledger row 12's third option, rejected in rule 3: `acquireRange` and `FocusResolver.RadiusMetres` both stay exactly as CC §7 authored them.
- **Per-class focus numbers.** AR §18.5 notes the 2 s delay and the 3 m radius are `const`s rather than authored data; still true, still no second class.

## As built

_Filled at merge. Deviations from the above with their reasons, or "as specified". This footer owns the deviations; the PROGRESS entry only counts them and links here._

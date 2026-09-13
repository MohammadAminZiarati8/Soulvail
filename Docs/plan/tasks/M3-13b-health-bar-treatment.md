# M3-13b — Health that reads at a glance: the damage tint, the Elite's bar, and the shield you were granted

**Size:** M (one new view, its suite, and a substantial change to the file that owns an enemy's colour) · **Depends on:** M3-13a (`Palette`), M3-11a (`PlayerGrantedShield`, `ShieldGranted`) · **Branch:** `m3-13b-health-bar-treatment`
**Design refs:** GD §8.1, §12.4, §16.1, §16.2, §16.3, §16.4; CC §7; CH §3.1; AR §8, §14, §18.4; ADR-0003 · **Ledger rows:** **6** (one new member in `Palette`, by M3-13a rule 7 — the row stays closed), 4 (whether a tint reads at phone scale, and whether twenty-eight bodies with bars cost anything, are both device-only)

## Goal

GD §16.2's table, all four rows of it: a basic enemy that darkens as it dies and shows a bar only while it is being hit, an Elite whose bar never leaves, and the player's own bar finally showing the shield Bulwark put on them.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EnemyHealthBar.cs` | Game | one bar above one body: transient for a basic enemy, persistent for an Elite — **block namespace** (Traps §5) |
| `Tests/Game/Views/EnemyHealthBarTests.cs` | Tests.Game | appearance, the two-second fade, the Elite rule, and the pooled reset |
| *small edits* | | `Game/Views/EnemyHitFeedback.cs` — the damage tint folded into the colour it already owns (rules 1–4) — **substantial**; `Core/Events/EnemyEvents.cs` — `EnemySpawned` gains `IsElite` (rule 6); `Core/Ai/EnemySystem.cs` — passes it from the spec; `Game/Views/EnemyViews.cs` — hands it to the bar on the rental; `Game/Presentation/Palette.cs` + `EnemyDying` (rule 3); `Game/Presentation/HpBarView.cs` — the granted-shield segment (rules 9–11); `Game/Presentation/HudPresenter.cs` — two subscriptions and the opening read (rule 10); `Prefabs/Enemies/Enemy.prefab` and `Prefabs/UI/Hud.prefab` gain their new children; `Tests/Game/Views/EnemyHitFeedbackTests.cs` gains the tint rows |

Only these files change. Anything else is a deviation: say so in *As built*. **If the tint turns out to need a second writer of `_baseColor` rather than a third term in the existing one, the split is the enemy (`M3-13b-i`) and the player (`M3-13b-ii`) — decided before continuing, never after.**

## Public API

```csharp
namespace Soulvail.Game.Views
{
    /// <summary>GD §16.2's two enemy treatments: a bar that fades, and one that does not.</summary>
    public sealed class EnemyHealthBar : MonoBehaviour
    {
        // [SerializeField] Image _fill; CanvasGroup _root; float _secondsShown = 2f, _fadeSeconds = 0.25f;
        // [SerializeField] float _widthDp = 28f, _heightDp = 3f, _heightMetres = 2.1f;

        [Inject] public void Construct(DomainEventHub hub);

        /// <summary>Told what it is standing over, on the rental. Before the body is in service.</summary>
        public void Bind(int id, bool isElite);

        public void Unbind();          // pooled reset: hidden, forgotten, not an Elite (rule 8)
        public bool IsShown { get; }
    }
}
```

```csharp
// EnemySpawned (added) — rule 6
public readonly bool IsElite;
```

```csharp
// EnemyHitFeedback (changed) — rules 1–4
/// <summary>Darkens the body toward Palette.EnemyDying as HP falls. 1 is unhurt.</summary>
private void SetDamageTint(float hpFraction);
```

```csharp
// HpBarView (added) — rules 9–11
/// <summary>Granted shield points over the live maximum, drawn ahead of the fill. 0 hides it.</summary>
public void SetGrantedShield(float fraction);
```

## Behaviour

**The basic enemy: the tint**

1. **The tint lives in `EnemyHitFeedback`, because that file already owns the body's colour and nothing else may write it.** Every effect there is written relative to `_liveColour` and every write goes through one `MaterialPropertyBlock`; the file's own remarks say why the archetype tint landed there rather than in a component of its own — *"a second component writing the tint straight onto the renderer would be overwritten by the first flash that ended, and the enemy would turn grey the first time it was hit."* A damage tint is the same argument with the same answer, so it is a **third term in the existing composition**, not a new writer.
2. **Composed from `HpFraction`, which the event already carries.** `EnemyDamaged.HpFraction` has been on the struct since M1-11 (*"its HP over its live maximum, after the hit"*), so no event changes and no health is held in the view layer. The body's base becomes the archetype colour lerped toward `Palette.EnemyDying` by `1 − HpFraction`; the flash, the telegraph swell and the dissolve all keep working against that base unchanged. **At full health the lerp is identity**, so a freshly spawned body looks exactly as M2-06 authored it and the Husk/Spitter/Bloater tints still tell three archetypes apart (GD §8.1).
3. **It darkens toward a desaturated deep maroon and never toward `#FF4A1F`, and the two design sections disagree about this.** GD §16.2 says the body *"darkens and shifts toward red"*; GD §16.4 reserves saturated red-orange for danger *"for nothing else, ever."* A Husk at 20 % HP drawn in the telegraph colour would be the single worst readability bug the project could ship — and this file has already made the call once, in a comment: the telegraph is a *shape* change *"deliberately… because saturated red-orange is reserved for danger (GD §16.4) and that palette belongs to M7's VFX."* So §16.4 wins, `Palette.EnemyDying` is a dark low-saturation maroon, and `Palette.IsDanger` is the test (M3-13a rule 3). **The contradiction is flagged for the owner and not edited here** — the M3-02a rule 7 precedent for a design-doc line, and a [parking lot](../ROADMAP.md#parking-lot) entry.
4. **The pooled reset forgets it, which is AR §18.4 gaining a third thing.** M2-06 gave the invariant two (`_liveColour`, `_liveScale`); a damage tint is the third, and the failure it prevents is specific: a Bloater that died at 5 % HP returns to the pool nearly black, and the next Husk rented from it spawns looking half dead. `ResetVisuals` clears the tint and `SetArchetypeLook` re-bases it, so the restore is *absolute* rather than accumulated — the same reason the archetype look is passed on every rental instead of being undone.

**The basic enemy: the transient bar**

5. **A thin bar above the body, shown on damage and faded two seconds later** (GD §16.2: *"a thin bar appears above the unit only while it's being damaged, then fades after 2 s"*). Every hit restarts the two seconds, so a flurry leaves one bar rather than a stutter — `HpBarView`'s ghost rule, for its reason. It runs on `Time.deltaTime`, which means it **freezes under a pause** (`Time.timeScale` is 0 — M3-08a rule 13) and that is correct: the world is stopped and a bar draining over a frozen arena would be the only thing moving. This is the deliberate opposite of M3-10b's Overflow toast, which counts *unscaled* seconds precisely because it must outlive a screen.
6. **Elite-ness comes off the event, and that is a one-field core change rather than a catalog lookup.** `EnemyViews` holds an `EnemyLookBook` and not a `ContentCatalog`, so it cannot answer the question today; handing it the catalog would answer it **wrongly for M7-02**, whose Elites are a body *upgraded at spawn* by spending `WavePlan.UnspentThreat` (GD §8.3 — *"2.5× the cost for one body"*), not an archetype. Elite-ness is therefore a per-spawn fact and belongs on `EnemySpawned`, which is also the pattern the project already uses everywhere a view needs a fact: `ShieldGranted.Total`, `EnemyDamaged.HpFraction`, `EnemyTelegraph.Duration`. `EnemySystem` reads `Spec.IsElite` — a property that has existed since M1-05 and that `TargetScorer` already scores on — and puts it on the event.
7. **An Elite's bar is persistent, and it is on at full health.** GD §16.2 states the reason as a decision rather than a style: *"they're priority targets — you're making decisions about them, so you need the number."* A bar that only appeared after the first hit would hide exactly the body the player is deciding about. **Nothing ships as an Elite** — `EnemySpec.IsElite` exists and no authored archetype sets it, because affixes are M7-02 — so this rule is tested and unseen, the same bargain M3-03 rule 10's null-tree branch makes and M3-12c rule 7 describes.
8. **One bar per body, a child of the prefab, and no pool of its own.** `EnemyHitFeedback`'s shape exactly: injected once when the pool builds the body, subscribed for the life of the pool, filtering every event on the bound id — and an unbound body matches nothing, *"having been handed the id of nobody."* `Bind` is called on the rental before the body is in service, so a bar is never drawn for one frame over the wrong enemy; `Unbind` on despawn hides it and forgets the Elite flag. Nothing is instantiated during a run.
9. **It is oriented once, not every frame.** `FollowCamera`'s rotation *"is authored, not derived — the camera never rotates"*, so a bar aligned to the camera at `Start` stays aligned for the whole run and a per-frame billboard would be twenty-eight quaternions a frame for no change. Named rather than assumed, because the day the camera earns a rotation (the fixed whole-arena framing the owner has in mind) is the day this becomes a per-frame cost, and this rule is where that shows up.

**The player**

10. **The granted shield is a segment on the HP bar, not a second arc on the Aegis ring.** M3-11a rule 5 is explicit that granted points are *"deliberately not the Aegis"* — the ring draws `ShieldSpec`'s 30 points, its 4 s delay and its 15/s refill, and CH §3.1 makes that the Oathbound's signature. A second arc would make one readout mean two pools with different rules. The segment is drawn **ahead of the fill**, which is also where the points actually are: `ApplyDamage` spends granted shield first, then the Aegis, then hit points, so the bar reads in absorption order.
11. **Driven by the two events, with the read for the opening frame.** `ShieldGranted` carries the new `Total` and `ShieldGrantExpired` carries what is left (M3-11a rule 9), so `HudPresenter` gains two subscriptions and never polls. `RunStarted` draws the opening state from `RunState.PlayerGrantedShield` — `HudPresenter`'s standing reason, that whether `RunStarted` has already been published depends on an order Unity does not give. **That read is zero on every resume there will ever be**, because `TimedEffects.Clear` forgets at the run's end (M3-11a rule 8) and nothing restores a grant; using it anyway is what keeps the HUD from having to know that.
12. **The numeric readout stays `current/max` and does not learn about the shield.** GD §16.2's player row is *"never ambiguous"*, and `140/140 (+35)` is a third number in a 320 dp row that M3-10b has just added a level label to. The segment says it; the text does not say it again.
13. **Nothing here decides anything, and no treatment holds health.** Three views, three inputs — an event's fraction, an event's total, one read on the opening frame — and no clock any rule depends on. GD §16.2's whole table is presentation over facts core has already settled.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tint_FullHealthIsTheArchetypeColour` | a Husk-tinted body / `EnemyDamaged(hpFraction: 1)` / the property block's colour is the archetype tint exactly (rule 2) |
| `Tint_DarkensAsHealthFalls` | the same / `HpFraction` 0.75, 0.5, 0.25 / monotonically darker, and 0.25 is nearer `Palette.EnemyDying` than 0.75 (rule 2) |
| `Tint_NeverApproachesDanger` | every `HpFraction` from 1 to 0 in 0.05 steps / — / no sampled colour satisfies `Palette.IsDanger`, and none is within 0.15 of it per channel — **the row rule 3 exists for** |
| `Tint_SurvivesAFlash` | a body at 0.3 / `EnemyDamaged`, then the flash expires / it returns to the *tinted* colour, not to the archetype's (rules 1, 2) |
| `Tint_SurvivesATelegraph` | a body at 0.3 / `EnemyTelegraph`, the swell, the snap back / the colour never changed and the scale returned to `_liveScale` (rule 1) |
| `Tint_DissolveFadesFromTheTintedColour` | a body at 0.1 / `EnemyDied` / the dissolve's alpha ramps on the tinted base (rule 1) |
| `Tint_KillingBlowDoesNotFlash` | *the existing M1-12 row* / — / unchanged: a killed body goes straight to the dissolve (rule 1) |
| `Tint_ResetForgetsIt` | a body tinted to 0.05 / `ResetVisuals` / the archetype colour, not the tint (rule 4) |
| `Tint_ArchetypeLookRebasesIt` | a body tinted as a Bloater / `SetArchetypeLook(husk, 1f)` / the Husk's colour, undarkened — the rental, not the reset, is what undoes it (rule 4) |
| `Bar_ShowsOnDamage` | a bound basic body, bar hidden / `EnemyDamaged(hpFraction: 0.6)` / shown, fill 0.6 (rule 5) |
| `Bar_HidesAfterTwoSeconds` | shown / `_secondsShown` + `_fadeSeconds` of `Time.deltaTime` / hidden (rule 5) |
| `Bar_EachHitRestartsTheClock` | shown, 1.5 s elapsed / a second hit, then 1.5 s / still shown — one bar, not a stutter (rule 5) |
| `Bar_FreezesUnderAPause` | shown / `Time.timeScale` 0 for 5 s, then 1 / still shown, and it hides 2 s after the resume (rule 5) |
| `Bar_IsHiddenUntilTheFirstHit` | a bound basic body / — / hidden; GD §16.2 gives a basic enemy no standing bar (rule 5) |
| `Bar_EliteIsShownFromTheRental` | `Bind(id, isElite: true)` / — / shown at fill 1, no damage needed (rule 7) |
| `Bar_EliteNeverFades` | an Elite, one hit / 10 s / still shown (rule 7) |
| `Bar_EliteStillTracksItsHealth` | an Elite / `EnemyDamaged(0.4)` / fill 0.4 (rule 7) |
| `Bar_IgnoresOtherEnemies` | bound to 7 / `EnemyDamaged(id: 8)` / nothing shown (rule 8) |
| `Bar_UnboundMatchesNothing` | `Unbind` / any event / nothing shown, nothing thrown (rule 8) |
| `Bar_UnbindForgetsTheEliteFlag` | an Elite / `Unbind`, then `Bind(id, isElite: false)` / hidden until the first hit — AR §18.4 (rule 8) |
| `Bar_IsOrientedOnceNotPerFrame` | a bound body / 60 frames / the transform's rotation was written once (rule 9) |
| `Bar_IsSizedInDp` | a canvas with a known scale / `Bind` / `_widthDp × pxPerDp` across (rule 9, `SkillButton`'s argument) |
| `Bar_InstantiatesNothingDuringARun` | the pool prewarmed / rent, hit and return 50 bodies / no instantiation after `Start` (rule 8) |
| `Spawn_CarriesTheEliteFlag` | a spec with `IsElite` true / `EnemySystem` spawns it / `EnemySpawned.IsElite` true, and false for a Husk (rule 6) |
| `Spawn_ViewsHandTheFlagToTheBar` | an Elite spawned / `EnemyViews.OnSpawned` / the body's bar was bound with `isElite` true, **before** `Bind` put it in service (rules 6, 8) |
| `Spawn_NoCatalogIsConsulted` | reflection over `EnemyViews` / — / no `ContentCatalog` field — the flag rides the event (rule 6) |
| `Player_ShowsAGrantedShield` | a run, no grant / `ShieldGranted(35, 35, 5)` / the segment is drawn at 35 over the live maximum (rules 10, 11) |
| `Player_SegmentSitsAheadOfTheFill` | hp 0.5, granted 35 of 140 / — / the segment's rect begins where the fill ends (rule 10) |
| `Player_ShrinksAsItIsSpent` | granted 35 / `ShieldGranted(35, 15, 5)` on a partial spend / the segment follows the total, not the grant (rule 11) |
| `Player_HidesWhenTheTotalIsZero` | shown / `ShieldGrantExpired(35, 0)` / hidden (rule 11) |
| `Player_StaysWhileAnotherGrantRuns` | two grants / `ShieldGrantExpired(35, 20)` / still shown at 20 — the row `Total` exists for (rule 11) |
| `Player_DrawsTheOpeningStateOnRunStarted` | a resumed run / `RunStarted` / the segment reads `PlayerGrantedShield`, which is 0 — and the test says out loud that it always is (rule 11) |
| `Player_AegisRingIsUntouched` | granted 35 on a full Aegis / — / `ShieldRingView`'s fraction is 1 and its value is the Aegis's 30 (rule 10, M3-11a rule 5 from this side) |
| `Player_TextDoesNotMentionIt` | granted 35, hp 140 of 140 / — / the readout is `"140/140"` (rule 12) |
| `Player_HoldsNoHealth` | reflection over the three views / — / no field named for hit points or shield points beyond the drawn fraction (rule 13) |
| `Prefab_IsDressed` | `Enemy.prefab` and `Hud.prefab` / read the fields back after a save (Traps §5) / the bar's fill and root, and the HP bar's shield segment, present and wired |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend. Hit a Husk once: a thin bar appears over it and is gone about two seconds later. Keep hitting it and the bar stays up throughout (rule 5).
2. **[Editor]** Watch a Husk die over several swings. The body visibly darkens as it goes, and at no point does it look like a telegraph ring — hold one in frame beside a spawn ring to check (rule 3). **This is the readability comparison the whole task turns on.**
3. **[Editor]** Kill a Bloater at low health, then wait for the pool to rent that body again. The new enemy spawns at its archetype's full colour (rule 4).
4. **[Editor]** Temporarily tick `IsElite` on `Husk.asset`, Play, and confirm every Husk carries a standing bar from the moment it spawns. **Untick it before committing** — nothing ships as an Elite until M7-02 (rule 7).
5. **[Editor]** With Bulwark authored, walk into a Spitter's line. A segment appears on the front of the health bar, shrinks as the bolt is absorbed, and vanishes when the grant lapses. The Aegis ring does not move (rules 10, 11).
6. **[Editor]** Pause mid-fade. The bar stops where it is; resume and it finishes fading (rule 5).
7. **[device]** Ledger row 4, three questions the Editor cannot answer: whether a 3 dp bar over a 6-inch screen reads as a bar or a smudge; whether the tint is legible in peripheral vision, which is GD §16.2's entire claim for it (*"it reads instantly in peripheral vision — which is where most enemies are"*); and what twenty-eight world-space canvases cost at the concurrency cap.
8. **[device]** GD §16.2's settings question, recorded rather than answered: *"we should watch playtests to see whether the default is right — if most people turn it on immediately, the tint isn't doing its job."* There is no toggle yet (M8-03), so what a playtest can say here is whether anyone *wants* one.

## Out of scope

- **The boss bar.** GD §16.2's segmented bar, one segment per phase, **already has a task: M4-04, *Segmented boss HUD bar*, which depends on M4-01's phases** — and the owner ruled at M3-00d that it stays there. Phases, and every event that would drive such a bar, arrive with the boss; a stub here would be a prefab nobody can see, tested against events that do not exist, and its one real design claim — a segment per phase — unverifiable without phases. M3-12c rule 6's argument, applied to a view.
- **The `"Always show enemy health bars"` toggle** (GD §16.2) — **M8-03**, with HUD opacity, by M3-09c rule 3's rule: a second profile field costs a `PlayerProfile` bump and a migration step, and that is a review of its own.
- **Damage numbers, the low-HP vignette, hit-stop and screen shake** (GD §16.3) — M8-01's game-feel pass. Two of its items shipped here would set the pattern by accident (M3-11c's Out of scope, for the same reason).
- **Elite affixes, and anything that makes an Elite exist** — M7-02. Rule 7 ships the treatment; the content that triggers it is three milestones out and `IsElite` has been on the spec since M1-05 waiting for it.
- **A world-space bar for the player**, or moving the HUD's bar into the world. GD §16.1 puts the player's health top-left and *"never ambiguous"*; a second copy over the body would be two answers to one question.
- **Touching the Aegis ring, `ShieldSpec`, or making its numbers addressable** — M3-11a rule 5 and M3-05 rule 5. Rule 10 is the whole of this task's relationship with the Aegis: it leaves it alone.
- **A new colour beyond `EnemyDying`.** M3-13a rule 7 sets the price of one member; this task pays it once.

## As built

_Filled at merge._

# M3-11c — What a cast looks like, and the untested view that has been waiting for it

**Size:** M · **Depends on:** M3-11a (`ShieldGranted`), M3-11b (`ZoneSpawned`) · **Branch:** `m3-11c-skill-views`
**Design refs:** GD §16.3, §16.4; CC §6.2, §6.4; CH §3.1; AR §4.3, §7, §8, §18.1 · **Ledger rows:** **5 — closed by this task** (`PlayerAnimatorView` has no tests and M3-11 is what promotes it), 6 (two more colour readers — rule 10), 4 (two new pooled effects on a phone's fill rate)

## Goal

Bulwark and Consecrate stop being numbers on a debug overlay: a shield the player can see and ground they can see themselves standing in — and `PlayerAnimatorView`, which has driven the player's body since M2-art with no test on it, gets the suite it should have shipped with.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ZoneView.cs` | Game | one decal on the floor: placed, sized, pulsed, retired — `TelegraphRingView`'s shape, **block namespace** (Traps §5) |
| `Game/Views/ZoneViews.cs` | Game | the pool and the subscriptions, stepped on the snapshot's `Dt` — `TelegraphRings`' shape |
| `Game/Views/BulwarkView.cs` | Game | the shell on the player while a grant is up — **block namespace** |
| `Tests/Game/Views/SkillViewTests.cs` | Tests.Game | both views, one fixture: both render two events and hold no state a run owns |
| `Tests/Game/Views/PlayerAnimatorViewTests.cs` | Tests.Game | **ledger row 5** — the derived `AttackSpeed`, the flinch rules, and the new cast trigger |
| `Prefabs/Vfx/VFX_ConsecrateZone.prefab` · `Prefabs/Vfx/VFX_Bulwark.prefab` | — | assets, not code files — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | | `Game/Views/PlayerAnimatorView.cs` — a `Cast` trigger on `SkillCast` (rule 7); `Game/Composition/RunScope.cs` + `_zoneViews` and `_bulwarkView`; `Game/Composition/RunTicker.cs` — `_zoneViews.Step(_snapshot.Dt)` beside the rings (rule 3); `Prefabs/Player/Player.prefab` gains the Bulwark shell |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class ZoneView : MonoBehaviour, IPoolable
    {
        public void Place(Vector3 position, float radius, float duration);
        public void Pulse();                     // one flash, on ZoneHealed with a non-zero amount
        public bool Step(float dt);              // false when its own countdown is over
        public void OnReturnedToPool();
    }

    public sealed class ZoneViews : IDisposable
    {
        public ZoneViews(DomainEventHub hub, ViewPool<ZoneView> pool);
        public int Count { get; }
        public void Step(float dt);
        public void Dispose();
    }

    public sealed class BulwarkView : MonoBehaviour
    {
        // [SerializeField] GameObject _shell; float _fadeSeconds = 0.15f;
        [Inject] public void Construct(DomainEventHub hub);
    }
}
```

## Behaviour

**The zone**

1. **A pooled decal, `TelegraphRings`' shape and for its reasons.** M2-12b already built a pool of ground decals that place themselves, run a countdown and return themselves by running out; a zone is the same object with a longer life and no fill. The pool is `ViewPool<ZoneView>`, prewarmed, and nothing is instantiated during a run.
2. **It renders three events and owns no zone.** `ZoneSpawned` takes one from the pool and places it; `ZoneHealed` with a non-zero amount flashes it; `ZoneExpired` returns it. The decal's own `Step` is a **cosmetic** countdown that exists only so a view orphaned by a missed event cannot live for ever — core's expiry is the authority and `ZoneExpired` is what normally retires it. That is `TelegraphRings`' bargain: *"it returns itself by running out"*, with core's event as the real answer.
3. **Stepped on the snapshot's `Dt`, beside the rings and the bolts.** `RunTicker` already steps two purely cosmetic things on the clamped step (M2-09 rule 3, M2-12b rule 4) for one reason — core timed the thing they stand for on that step — and a zone decal whose fade ran on `Time.deltaTime` would out-live or under-live the zone on exactly the hitching frames the clamp exists for. Nothing below it reads it: a decal has no collider, so it joins the same purely-cosmetic pair rather than the physical half of the frame.
4. **A pulse flashes only when something was actually healed.** `ZoneHealed` carries the amount and it is zero at full health (M3-11b rule 8); flashing on a wasted pulse would tell the player they were being healed when they were not, which is the one thing a feedback view must never do. GD §16.3's game-feel list is built on exactly this distinction.
5. **The decal is sized from the event, not from a prefab.** `ZoneSpawned` carries the radius, so a 3.5 m Consecrate and whatever M7 authors at 6 m use the same prefab scaled — the number lives in the asset core reads (M3-11b rule 11), never twice.

**The shield**

6. **A shell on the player, up while a grant is up.** `ShieldGranted` shows it, `ShieldGrantExpired` with `Total` at zero hides it — the **total**, not the removal, because two overlapping grants must not leave the player looking unshielded while one is still running (M3-11a rule 9 carries the total for exactly this). A short fade in and out rather than a pop, because it appears mid-fight and a hard pop reads as a glitch; 0.15 s, a serialized field.
7. **The animator gets a cast, and that is the second half of ledger row 5.** `PlayerAnimatorView` gains a `Cast` trigger fired on `SkillCast` — one hash, one line, the shape it already uses for `Charge` and the flinch. It is deliberately not per-skill: there is one cast clip and twelve nodes, and a trigger per skill would be an animator parameter per content id, which is content leaking into a controller.

**Ledger row 5**

8. **`PlayerAnimatorView` gets the tests it shipped without.** It landed at M2-art with no spec and therefore without the behaviour-rules ↔ tests pairing the protocol asks for, and the ledger's own words are that *"the derived `AttackSpeed` and the 'no flinch on a blocked hit' rule are both testable in isolation."* They are, and so is everything else it does. **The rules are not re-derived here** — the file's behaviour is what it is, and this task writes the suite that pins it; anything the tests find that looks wrong is reported in *As built* and fixed only if it is a bug, because changing behaviour and writing its first test in one PR is how a regression gets blessed.
9. **Two untested Animator drivers is where the pattern sets**, which is the ledger's reason for promoting it *now* rather than at M7. `BulwarkView` is the second driver and it arrives with tests; leaving the first one bare would have made "views do not get tests" the house rule by example.

**Both**

10. **Colours are serialized, and that is two more readers for ledger row 6.** The zone decal wants a heal colour and the shell wants the player's cyan (GD §16.4 reserves `#22D3EE` for *"the player… safe things"*, which is exactly what both of these are). Ledger row 6's owner now inherits **eight** readers across five files. M3-08b rule 8's argument holds for the third and fourth time: inventing `Palette` here is M3-13 arriving early, and eight is the number to size it against. **The one hard rule this task does obey:** neither view may use `#FF4A1F`, which GD §16.4 reserves for danger *"for nothing else, ever"* — a healing zone in the telegraph colour would be the single worst readability bug the project could ship.
11. **Neither view holds a handle to anything core owns.** Both take `DomainEventHub` and nothing else; neither takes `IRunSession`. There is no number to poll — a grant and a zone both have durations the events carry — so these are the two purest event-rendering views in the project, and the tests say so with a reflection row.

## Tests

| Test | Given / When / Then |
|---|---|
| `Zone_SpawnPlacesADecal` | — / `ZoneSpawned((3,0,−2), 3.5, 6)` / one active view at that position, scaled to 3.5 m (rules 1, 5) |
| `Zone_ExpiredReturnsItToThePool` | one live / `ZoneExpired` / `Count` 0, `PooledCount` back up (rule 2) |
| `Zone_StepRetiresAnOrphan` | one live, no `ZoneExpired` / `Step` past its duration / returned anyway (rule 2) |
| `Zone_HealedFlashes` | one live / `ZoneHealed(0, 3)` / one flash (rule 4) |
| `Zone_ZeroHealDoesNotFlash` | one live / `ZoneHealed(0, 0)` / no flash — the row that keeps the view honest (rule 4) |
| `Zone_TwoZonesAreIndependent` | two spawned / expire the first / the second is still live and still at its own position |
| `Zone_StepsOnTheSnapshotDt` | a recording ticker / one `Tick` / `ZoneViews.Step` ran with `_snapshot.Dt`, not `Time.deltaTime` — `ProjectileViews`' row (rule 3) |
| `Zone_InstantiatesNothingDuringARun` | the pool prewarmed / spawn and expire 50 zones / no instantiation after `Start` (rule 1) |
| `Zone_DisposeReturnsEverything` | three live / `Dispose` / all returned, subscriptions dropped |
| `Bulwark_ShowsOnGrant` | hidden / `ShieldGranted(35, 35, 5)` / the shell on (rule 6) |
| `Bulwark_HidesWhenTheTotalReachesZero` | shown / `ShieldGrantExpired(35, 0)` / off (rule 6) |
| `Bulwark_StaysUpWhileAnotherGrantRuns` | two grants / `ShieldGrantExpired(35, 20)` / **still on** — the row the `Total` field exists for (rule 6) |
| `Bulwark_FadesRatherThanPops` | — / `ShieldGranted` / alpha climbs over `_fadeSeconds` and is not 1 on the first frame (rule 6) |
| `Views_HoldNoRunHandle` | reflection over `ZoneViews` and `BulwarkView` / — / neither has an `IRunSession` or a `RunState` field (rule 11) |
| `Views_UseNoDangerColour` | the two prefabs' serialized colours / — / none equals `#FF4A1F` (rule 10) |
| `Animator_AttackSpeedIsDerivedFromTheInterval` | `PlayerAttacked` with a 0.333 s interval / — / `AttackSpeed` is `AuthoredSwingSeconds / 0.333` (rule 8, **ledger row 5**) |
| `Animator_AttackSpeedIsCapped` | an interval so short the ratio exceeds 6 / — / `AttackSpeed` 6 (rule 8) |
| `Animator_FlinchesOnARealHit` | `PlayerDamaged(blocked: false, toHp: 90)` / — / the flinch trigger fired once (rule 8) |
| `Animator_DoesNotFlinchOnABlockedHit` | `PlayerDamaged(blocked: true)` / — / no flinch — **the ledger's named row** (rule 8) |
| `Animator_DoesNotFlinchOnAKillingHit` | `PlayerDamaged(toHp: 0)` / — / no flinch; the death clip owns that frame (rule 8) |
| `Animator_DoesNothingAfterDeath` | `PlayerDied`, then `PlayerDamaged` and `PlayerAttacked` / — / nothing fired (rule 8) |
| `Animator_ChargeFires` | `ChargeStarted` / — / the charge trigger once (rule 8) |
| `Animator_CastFires` | `SkillCast` / — / the cast trigger once (rule 7) |
| `Animator_CastIsNotPerSkill` | two different `SkillCast` ids / — / the same trigger both times, and no parameter named after a content id (rule 7) |
| `Animator_SubscribesFromConstruct` | constructed after `RunStarted` / publish `PlayerAttacked` / it reacts (`HudPresenter`'s reason) |
| `Animator_DestroyDropsSubscriptions` | — / `OnDestroy`, then publish / nothing throws |
| `Animator_UndressedIsSilent` | no `Animator` assigned / every event / nothing throws — the existing null guards, pinned |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend with Consecrate authored. Drop below 60 %: a disc appears at your feet, pulses every half second while you stand in it, and fades when it runs out. Walk out — it keeps pulsing where you left it, and stops flashing, because nothing is being healed (rule 4).
2. **[Editor]** Stand in it at full health. The disc is there and **does not flash** (rule 4).
3. **[Editor]** With Bulwark authored, walk into a Spitter's line. A shell fades in before the bolt lands and fades out five seconds later (rule 6).
4. **[Editor]** Watch the player's body on a cast: the cast clip plays, and it is the same clip for both skills (rule 7).
5. **[Editor]** Confirm the Console is clean and the zone pool never grows: spawn and expire a dozen zones and watch `PooledCount` return to its prewarm each time.
6. **[device]** Ledger row 4: two more pooled, additive, floor-hugging effects on a phone's fill rate — and M2's open question about whether overlapping additive decals read as separate things or one smear now has a second kind of decal in the pile.
7. **[device]** GD §16.4's real test: whether a cyan healing disc and a red-orange telegraph ring overlapping on the same floor are still tellable apart. Deferred, and ledger row 6's best argument.

## Out of scope

- **Icons for either skill** — M7's art pass. M3-10a rule 9 and M3-10b rule 8 carry what their absence costs.
- **Haptics on cast** — `SkillCast` is an event and `HapticsListener` is where a line would go; M1-20's coalescing window is unmeasured on a phone (ledger row 4) and adding a fourth source before the first three are tested is how that measurement gets harder.
- **Hit-stop or screen shake on a cast** (GD §16.3) — M8-01's game-feel pass owns the checklist as a whole; two of its items on two skills would be the pattern set by accident.
- **A `Palette` file** — M3-13, ledger row 6. Rule 10 brings the count to eight.
- **Changing anything `PlayerAnimatorView` does** — rule 8. The tests pin the behaviour; a bug found is reported and fixed on its own terms.
- **Drawing the granted shield as a number or a bar** — M3-13, GD §16.2. This is a shell in the world, not a readout.

## As built

_Filled at merge._

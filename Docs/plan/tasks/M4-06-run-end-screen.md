# M4-06 — The run-end screen: what the descent was worth, and the overlay it replaces

**Size:** S · **Depends on:** M4-05a (`ShardsAwarded`), M4-05b (the number is banked before anybody is told it exists), M3-13a (`Palette`), M3-14a (`ILocalizer`) · **Branch:** `m4-06-run-end-screen`
**Design refs:** GD §14.1, §16.1, §16.4; AR §11.5, §18.1; ADR-0012 · **Ledger rows:** **[1](../ROADMAP.md#carry-forward-into-m4)** — the second new readout to land on the HUD M3-15 found unreadable, and the first that is a *screen with words*; **[3](../ROADMAP.md#carry-forward-into-m4)** (whether a payout reads at thumb distance is device-only)

## Goal

A run ends and the player is told what it was worth, in English, on a screen — instead of the two-word overlay that has stood in for one since M1-17.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/RunEndPresenter.cs` | Game | the screen: opens on `ShardsAwarded`, draws the payout and its two terms, one button back to the Menu — **block namespace** (Traps §5) |
| `Tests/Game/Presentation/RunEndPresenterTests.cs` | Tests.Game | which signal opens it, what it draws, the exit, and the raw-string sweep |
| `Game/Presentation/HudPresenter.cs` | Game | **the death overlay is removed, not stacked** (rule 2) — a substantial deletion, so it counts |
| `Prefabs/UI/RunEnd.prefab` | — | the screen. An asset, not a code file — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | | `Game/Composition/RunScope.cs` + `_runEndPresenter` and its `RegisterComponent`, **required rather than optional** (rule 7); `Scenes/Run.unity` dressed with the prefab; `Prefabs/UI/Hud.prefab` loses the overlay; `Data/Localisation/English.asset` gains three rows (rule 5) |
| *tests* | Tests.Game | `HudPresenterTests` — the death rows move out and `Construct`'s arity drops (rule 2); `TableLocalizerTests` — the *"no English typed into a prefab"* row now reads `RunEnd.prefab`; `InstallerTests` if the field is required |

Only these files change. Anything else is a deviation: say so in *As built*.

**Pre-declared tripwire, in M3-11a's shape:** if the payout rows want a component of their own — an
`OfferCard`-like `PayoutRow` with its own tests — **this task splits into `M4-06a` / `M4-06b` before a line is
written**, never after. Three labels and two numbers on one prefab is the bet; a fourth term would lose it.

## Public API

```csharp
namespace Soulvail.Game.Presentation
{
    public sealed class RunEndPresenter : MonoBehaviour
    {
        // [SerializeField] CanvasGroup _root; Button _return;
        // [SerializeField] TMP_Text _title, _returnLabel, _depthLabel, _bossLabel, _shardLabel;
        // [SerializeField] TMP_Text _depth, _bosses, _shards;   // the three numbers
        // No RunPause: the tick is already gated by !IsRunning (rule 4). Its absence is pinned.
        [Inject] public void Construct(DomainEventHub hub, SceneLoader loader, ILocalizer localizer);
    }
}
```

```csharp
namespace Soulvail.Game.Presentation
{
    public sealed class HudPresenter : MonoBehaviour
    {
        // Construct drops from five parameters to two: SceneLoader, InputAdapter and ILocalizer
        // all leave with the overlay, because the death path was their only reader (rule 2).
        [Inject] public void Construct(DomainEventHub hub, IRunSession session);
    }
}
```

## Behaviour

1. **`ShardsAwarded` is what opens it, and both alternatives are refused by name.** Not `RunEnded`: that fires
   whenever `RunScope` is torn down, which is every ordinary exit from the Run scene, so a screen hung off it
   would appear over a scene already unloading — `SaveWriter.cs:78`, `HudPresenter.cs:50` and
   `PausePresenter.cs:64` have each refused it for this, and a fourth class disagreeing would be the bug. Not
   `PlayerDied` either, and that is the *new* half of the ruling: it is published one line earlier and carries
   no number, so a screen opened on it would be up for a frame with three empty rects. `ShardsAwarded` is
   published only inside `RunSession.Tick`'s `IsDead` branch ([M4-05a](M4-05a-shard-payout.md) rule 5) and
   carries everything this screen draws, **so the screen cannot be on screen before its numbers are.**
2. **The death overlay is replaced, not stacked, and `HudPresenter` keeps none of it.** Two screens for one
   death is the worst outcome available, and a payout the player taps past to reach a *second* dismissal is
   worse still. So `OnPlayerDied`, `WriteDeathStrings`, `_deathOverlay`, `_deathTitle`, `_deathHint`,
   `_awaitingTap`, `_deathFrame`, `ReturnToMenu`, `DeathTitleKey` and `DeathHintKey` all leave that class —
   and with them `SceneLoader`, `InputAdapter` and `ILocalizer`, **because the death path was the only reader
   of all three** (`HudPresenter.cs:428`, `:930`, `:739`). `HudPresenter.Construct` drops from five parameters
   to two, its `Update` stops doing anything but `TickFade`, and **the HUD stops being an `ILocalizer` reader
   entirely** — `HpFormat` is a number format rather than a sentence and survives localisation unchanged,
   which is the distinction M3-14a drew and this task inherits.
3. **`ui.death.title` and `ui.death.hint` keep their keys and move to the new screen.** They are the right
   words, `English.asset` already carries them, and `TableLocalizerTests` already asserts both resolve —
   retiring them to coin synonyms would be churn that breaks a shipped row for nothing. *"Tap to return"*
   becomes the exit button's label.
4. **The screen holds no pause and needs none.** `RunTicker.Tick` already returns on `!_session.IsRunning`
   (`RunTicker.cs:334`), so the simulation is stopped before this screen exists and a `PauseReason` would be a
   second answer to a question already settled. `RunPause` is **not** a dependency of this class and a test
   pins its absence — `LevelUpPresenter`'s rule 3, reached from the other side.
5. **Every string is a `LocKey` through `ILocalizer`, and every number is drawn beside a label rather than
   inside a sentence.** `English.asset` gains **three** rows — `ui.runend.depth`, `ui.runend.bosses`,
   `ui.runend.shards` — and no row in this table carries a `{0}`, because nothing in this project's
   localisation takes arguments and inventing that here would be M6-10 arriving early. The numbers go through
   TMP's `SetText` with a `"{0:0}"` format, `HudPresenter.HpFormat`'s reason: an interpolated string allocates,
   and TMP's overload writes into its own backing array. **A null localizer falls back to the key** —
   `MenuPresenter.Write`'s answer — because a run-end screen missing a word is still a screen a player can
   leave, and throwing here would strand them on a run that has already ended.
6. **`Palette.Essence` is the Shard total's colour, and it is that member's first reader.** `Palette.cs:91`
   reads *"rewards, Essence, Gates. GD §16.4. No reader yet (M6)"* — a Shard payout is a reward, so this needs
   no eleventh member and no serialized `Color` anywhere on the screen. **It may not be `Palette.Danger`**
   (GD §16.4's *"nothing else, ever"*), and the depth and boss figures are `Palette.Neutral`: they are facts
   about the run rather than the reward, and three amber numbers would say nothing about which one matters.
7. **The screen is a *required* component of `RunScope`, unlike every optional one before it.**
   `FirstActiveHint`, `TreeViewPresenter` and the reticle are optional because a scene dressed without them
   still plays; a scene dressed without **this** strands the player on a dead run with no way out, because
   rule 2 takes the tap away from `HudPresenter`. So the field is required, the scope fails to compose
   without it, and `InstallerTests` says so — which is `RunScope.cs:419`'s *"fails loudly rather than
   silently"* applied where the cost is the app rather than a hint.
8. **It draws this run's payout and no lifetime total.** The banked figure lives in `ProfileStore.Current`
   one scope up, and drawing it here would race [M4-05b](M4-05b-profile-v3-and-shard-writer.md)'s writer:
   both hang off the same event, the hub guarantees no order between a scoped service and an injected
   component, and a screen that showed the total *before* the write would be wrong every second run and right
   every other. **A lifetime total belongs beside the thing that spends it** — M5-07's class select or
   M6-02b's Sanctum — and it is out of scope here rather than deferred by accident.
9. **The exit is a `Button`, not a full-screen tap.** `MenuPresenter`, `PausePresenter` and `SkillsPresenter`
   are all buttons, and this is the first screen in the game the player is meant to *read*: a stray thumb
   still travelling from the last dodge would dismiss a full-screen tap before a word of it landed. The button
   goes dead the moment it is hit, so a second tap arriving during the scene load cannot start a second one —
   `LevelUpPresenter` rule 5's guard, and `HudPresenter.ReturnToMenu`'s existing `_awaitingTap` lowering.
10. **A failed load re-arms the button rather than stranding the player** — `HudPresenter.cs:934`'s existing
    `catch`, moved with the rest of the path and not re-invented.
11. **This task may not tick a readability row, and it is the second in the milestone under that rule.**
    [Ledger row 1](../ROADMAP.md#carry-forward-into-m4) says the HUD is too cramped to read; M4-04's rule 8
    forbade it from judging its own band and this inherits the same refusal — with one difference that makes
    it sharper: **M4-04's band had no words and this screen is nothing but words.** The task states what it
    shipped — how many labels, at what size, in what safe area — and hands the judgement to
    [M4-07](M4-07-acceptance-and-tag.md). It is a **full-screen** canvas over a stopped run, so unlike the
    band it collides with nothing; what it risks is being unreadable on its own terms.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Screen_IsHiddenUntilTheRunEnds` | a live run / ticked / the root is down and takes no touches (rule 1) |
| `Screen_OpensOnShardsAwarded` | `ShardsAwarded(220, 12, 2)` / published / the screen is up and the three numbers read 12, 2, 220 (rules 1, 5) |
| `Screen_IgnoresRunEnded` | `RunEnded` alone / published / the screen stays down — rule 1's refusal, asserted (rule 1) |
| `Screen_IgnoresPlayerDied` | `PlayerDied` alone / published / the screen stays down, because it carries no number (rule 1) |
| `Screen_DrawsEveryStringFromTheTable` | a localizer with known rows / opened / the five labels read the table's words, not the keys (rule 5) |
| `Screen_FallsBackToTheKey` | a null localizer / opened / the labels read the keys and nothing throws (rule 5) |
| `Screen_DrawsNoRawString` | `RunEnd.prefab` and the class / swept / no authored English on the asset, every label a `LocKey` (rule 5, AR §11.5) |
| `Screen_CarriesNoSerializedColour` | the class / reflected / no `[SerializeField] Color`; the Shard figure is `Palette.Essence` (rule 6) |
| `Screen_IsNotTheDangerColour` | every colour it draws / compared / none satisfies `Palette.IsDanger` (rule 6) |
| `Screen_TakesNoRunPause` | the class / reflected / no `RunPause` field or parameter (rule 4) |
| `Screen_ReturnsToTheMenuOnce` | the button hit twice / / one `LoadAsync`, and the button is dead after the first (rule 9) |
| `Screen_RearmsAfterAFailedLoad` | a loader that throws / the button hit / logged, the screen still up, the button live again (rule 10) |
| `Screen_DrawsNoLifetimeTotal` | the class / reflected / no `ProfileStore` dependency — rule 8's race, refused by construction (rule 8) |
| `Hud_NoLongerOwnsTheDeathOverlay` | `HudPresenter` / reflected / no `PlayerDied` subscription, no `SceneLoader`, no `InputAdapter`, no `ILocalizer`, and `Construct` takes two parameters (rule 2) |
| `Hud_PrefabHasNoOverlay` | `Hud.prefab` / swept / no object named for the death overlay and no authored English at all (rules 2, 3) |
| `Table_CarriesTheRunEndRows` | `English.asset` / read / rows for `ui.death.title`, `ui.death.hint`, `ui.runend.depth`, `ui.runend.bosses`, `ui.runend.shards` (rules 3, 5) |
| `RunScope_RefusesToComposeWithoutTheScreen` | the run container with the field unset / built / it fails, loudly (rule 7) |

**Guard rows are implied, not listed:** a null hub, a null loader, a non-finite or negative figure on the event, and the prefab's five labels all being bound.

## Manual verification (Editor / device)

1. **[Editor]** Play, die on stage 1. *Expected: the run-end screen, "You died", Depth 1, Bosses 0, Soul
   Shards 10, and one button.* The old two-word overlay must not appear at any point.
2. **[Editor]** Hit the button. *Expected: the Menu scene, exactly as the overlay's tap used to reach it.*
3. **[Editor]** Reach stage 5, kill the Warden, walk through, then die on stage 6. *Expected: Depth 6,
   Bosses 1, Soul Shards 110.*
4. **[Editor]** Die, then hit Continue from the Menu. *Expected: no resume is offered* — `SaveWriter` clears
   the run on `PlayerDied` and nothing here changed that.
5. **[Editor]** Record the arrangement as numbers rather than a verdict (rule 11): label point sizes, the
   canvas's safe-area insets, and how many of the five labels fit above the fold in the Device Simulator's
   landscape frame. **No readability row is ticked here.**
6. **[device]** Whether five labels and three numbers read at thumb distance on a six-inch screen in
   landscape, and whether the button clears a landscape cutout on **both** rotations.
   [M4 row 3](../ROADMAP.md#carry-forward-into-m4) — the seventh and eighth device questions.

## Out of scope

- **Fixing the HUD.** Rule 11. This task inherits [row 1](../ROADMAP.md#carry-forward-into-m4) and must not
  pretend to answer it; the owner ruled the UI redesign out of M4 immediately after `m3` was tagged.
- **A lifetime Shard total, a wallet readout, or anything that can be spent** — rule 8, and GD §14.2 is M6's.
- **Run statistics.** Kills, damage dealt, time survived, a best-depth record. GD §14.1 names three terms and
  two of them ship; a stats screen is a design decision nobody has made and a fourth term would trip the
  tripwire above.
- **A victory screen.** Descent is endless (GD §7.1), so the only way a run ends is death; a finite mode
  running out of stages already publishes `RunEnded` without a `ShardsAwarded` and correctly shows nothing.
- **Animation, a fade, a count-up.** `Time.timeScale` is untouched and the run is already stopped; a tween
  here is a second clock in a screen whose job is to be read and left.
- **Retiring `ui.death.title` / `ui.death.hint`** — rule 3.

## As built

**Three deviations, all about where a test row lives, and none about behaviour.** Every rule shipped as
written; the tripwire was not reached — the payout rows are three `Label`/`Figure` pairs authored on the one
prefab, with no component and no fixture of their own.

**1. `RunScope_RefusesToComposeWithoutTheScreen` is in `RunEndPresenterTests`, not `InstallerTests`.** The
Files table says *"`InstallerTests` if the field is required"*, and the field is — but that fixture's own
remarks open with *"No `LifetimeScope` MonoBehaviours anywhere here"*, which is its stated argument for the
installers being scene-free statics. Rule 7's claim is about the MonoBehaviour rather than about an
installer, so it went to the fixture that owns the screen. M4-05b's deviation 2, with the nouns swapped.

**2. The Files table names `HudPresenterTests` and no such fixture has ever existed** —
`TableLocalizerTests`' own remarks say so in as many words, which is why three of its rows are claims about
presenters rather than about an adapter. So `Hud_NoLongerOwnsTheDeathOverlay` and `Hud_PrefabHasNoOverlay`
went into `RunEndPresenterTests`: what they assert is that *this* screen took the death path off the HUD,
which is this task's claim rather than the HUD's. Spec versus code, and the code won — M4-05b's deviation 3.

**3. Three fixtures the tests row does not name took a one-line edit, and the compiler forced it.**
`GrantedShieldTests`, `XpBarViewTests` and `BossBarViewTests` each called `HudPresenter.Construct` with five
arguments. Two became `Construct(_hub, _session)`; the third also owned the arity assertion — M4-04's
`Construct_RefusesANullHub` pinned *five* and said *"Construct grew an argument"* — and that row was
**rewritten rather than deleted**, now asserting both null guards and pointing at the fixture that owns the
reason the arity is two. The five-argument claim it made is M4-04's and is not lost: it said the band needs
no dependency of its own, and that sentence is still in the row.

**Rule 11's numbers, stated rather than judged.** Five words and three figures, on a full-screen canvas at
`sortingOrder` 200 — above the HUD's 0, the pause icon's 50, the level-up screen's 100 and the tree view's
110, so nothing can be drawn over a payout. Point sizes: title **96**, the three row labels **48**, the depth
and boss figures **48**, the Shard figure **64**, the exit button's label **32**. The button is **360 × 88**
reference px, authored rather than placed in dp — `PausePresenter`'s four panel buttons' precedent, and the
reason there is no `Place` method on this class: nothing on this screen has to agree with anything else about
where it is, which is the argument `HudPresenter.Place` and `LevelUpPresenter.Place` both make in the
opposite direction. Driven through a live canvas on the saved asset at 2 560 × 1 440, **all eight labels sit
inside the frame** — the title's top edge at 1 213.3 and the button's label bottom at 341.3, so 226.7 px of
headroom above and 341.3 below. **Safe-area inset is 0 on all four edges in the Editor**, which is the whole
of what an Editor can say: the labels hang under a `SafeAreaFitter` that is inert without a cutout. Whether
any of this reads at thumb distance is **M4-07's**, and the two device questions are on
[row 3](../ROADMAP.md#carry-forward-into-m4).

**One decision inside `RunScope.Configure` worth naming: the guard sits immediately after the camera's**,
above every optional registration, rather than beside the HUD's. That is where a *required* reference belongs
on this scope, and it is what makes the refusal reachable in a test with five prerequisites dressed instead
of fifteen — the row asserts both directions, so a guard that refused everything would fail it.

**And one thing the deletion removed that the spec did not count:** `HudPresenter.Update` no longer reads the
Input System at all. It polled `InputAdapter.FocusPressedThisFrame` every frame of every run so that the
death overlay could take a tap; `TickFade` is the whole method now.

**One file outside the table, on the protocol's own instruction:** `Docs/Traps.md` §4 gained two findings
about the MCP — that `RegisterCallbacks` plus a file write in one command is refused as a *user interaction*,
and that Unity's own `TestResults.xml` in the persistent data path is a better suite-count source than any
harness — plus a correction to §4's `Unity_GetConsoleLogs` note, which said `Debug.Log` never comes back and
is true only of a call that filters for it by name. A toolchain trap belongs there rather than in a Log
entry, which is what [PROGRESS › How to write an entry](../PROGRESS.md#how-to-write-an-entry) says; every
milestone's feature tasks have edited that file the same way.

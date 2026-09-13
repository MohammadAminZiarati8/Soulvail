# M3-09c — Profile v2: the first thing the player keeps, and the hint that needs it

**Size:** M · **Depends on:** M3-09a (the icon it points at), M3-09b (the screen it points to), M3-08a (`LevelUpClosed`), M3-03 (`NodeTaken`) · **Branch:** `m3-09c-profile-v2-and-first-active-hint`
**Design refs:** CC §6.1, §6.3; GD §16.1; AR §10.3, §11.6, §18.2, §18.3; ADR-0007, ADR-0012; M2-13a, M2-13b, M1-20 · **Ledger rows:** **2** — its third bump and the first on the *other* format; **9** (the hint is one more unresolved `LocKey`); 4 (whether a corner callout is noticed mid-fight is device-only)

## Goal

`PlayerProfile` becomes a record with more than one field in it, the profile migration chain runs a step for the first time since it was written, and CC §6.3's *"Once. Never again."* is literally true.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/ProfileStore.cs` | Game | the one holder of the live profile, and the only thing that writes one (rule 3) |
| `Game/Presentation/FirstActiveHint.cs` | Game | the callout: hears the first Active land, points at the pause icon, spends the flag — **block namespace** (Traps §5) |
| `Tests/Game/Adapters/ProfileStoreTests.cs` | Tests.Game | the copy-through, and the field a second writer used to erase |
| `Tests/Game/Presentation/FirstActiveHintTests.cs` | Tests.Game | once, never again, and never for a Passive |
| `Prefabs/UI/FirstActiveHint.prefab` | — | the callout. An asset, not a code file — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | | `Core/Save/SaveDtos.cs` — `PlayerProfile.CurrentVersion` 2, `SeenFirstActiveHint`, and the two `With` helpers (rules 1, 2); `Core/Save/SaveMigrations.cs` — the `if (version < 2)` profile step (rule 2); `Game/Adapters/LocalJsonSaveStore.cs` — `ProfileMirror` gains `seenFirstActiveHint`; `Game/Adapters/HapticsSettings.cs` — writes through `ProfileStore` (rule 3); `Game/Composition/BootFlow.cs` — hands the loaded profile to the store rather than straight to haptics; `Game/Composition/BootInstaller.cs` registers `ProfileStore`; `Game/Composition/RunScope.cs` + `_firstActiveHint` |
| *tests* | Tests.Core / Tests.Game | `SaveDtoTests` (the field, the guards, the helpers), `SaveMigrationTests` (`CanReadProfile`, the step, the profile chain looping over two for the first time), `LocalJsonSaveStoreTests` (a v1 profile literal migrates, a v2 literal is what this build writes), `HapticsSettingsTests` (the erasure row) |

Only these files change. Anything else is a deviation: say so in *As built*. **`RunSnapshot.CurrentVersion` stays 3** — the two formats version independently (M2-13b).

## Public API

```csharp
namespace Soulvail.Core.Save;

public readonly struct PlayerProfile
{
    public const int CurrentVersion = 2;

    public PlayerProfile(int version, bool hapticsEnabled, bool seenFirstActiveHint);

    public bool SeenFirstActiveHint { get; }          // v2

    /// <summary>This profile with one field moved. The shape a multi-field record needs (rule 3).</summary>
    public PlayerProfile WithHaptics(bool value);
    public PlayerProfile WithSeenFirstActiveHint(bool value);
}

// SaveMigrations.MigrateProfile (changed): `if (version < 2)` rebuilds the profile at v2 with
// SeenFirstActiveHint false, keeping hapticsEnabled as decoded.
```

```csharp
namespace Soulvail.Game.Adapters;

/// The live profile, and the only writer of one. Built in BootScope, so it outlives every run.
public sealed class ProfileStore
{
    public ProfileStore(ISaveStore store);

    public PlayerProfile Current { get; }             // PlayerProfile.Default until a load lands

    /// <summary>What BootFlow's load calls. Does not write back.</summary>
    public void Adopt(in PlayerProfile profile);

    /// <summary>Replaces the profile and persists it, fire-and-forget like every other save.</summary>
    public void Save(in PlayerProfile profile);
}
```

```csharp
namespace Soulvail.Game.Presentation
{
    public sealed class FirstActiveHint : MonoBehaviour
    {
        // [SerializeField] CanvasGroup _root; TMP_Text _text; float _secondsShown = 4f;
        [Inject] public void Construct(DomainEventHub hub, IRunSession session, ProfileStore profile);
    }
}
```

## Behaviour

**The format**

1. **One field, and it is the first thing the profile has ever had to remember about a *player* rather than a *setting*.** `hapticsEnabled` is something the player chose; `seenFirstActiveHint` is something the game noticed. Both belong in the same file for the reason M2-13b gave when it made two formats rather than one: a profile is what survives a run, and this survives runs.
2. **The field and the migration ship together, and the profile chain runs a step for the first time.** `CurrentVersion` → 2, the `if (version < 2)` step, the v1 fixture proving it through the real adapter, and the v2 fixture — one PR, which is ledger row 2's rule for the third time this milestone and the first time on this format. `CanReadProfile`, `MigrateProfile` and `OldestSupportedProfileVersion` have existed since M2-13b and have **only ever run the identity**; `ProfileChain_IsUnbrokenFromOldestToCurrent` now loops over two, and its text does not change. `OldestSupportedProfileVersion` stays 1. A v1 profile was written by a build with no skills in it, so its v2 form is `seenFirstActiveHint: false` — the hint has not been seen, which is true.
3. **A multi-field record needs one writer, and this task exists partly because there was not one.** `HapticsSettings` currently persists with `new PlayerProfile(PlayerProfile.CurrentVersion, value)` (`HapticsSettings.cs:85`) — it authors the **whole struct** from the one field it knows. That is correct for a record with one field and silently destructive the moment there are two: toggling haptics would reset `seenFirstActiveHint` to whatever the constructor defaults it to, and the hint would come back for a player who had already dismissed it. So `ProfileStore` holds the live profile, both features copy-with (`WithHaptics`, `WithSeenFirstActiveHint`) and hand it back, and `Save` is the only call that reaches `ISaveStore.SaveProfile`. `HapticsSettingsTests` gains the row that would have caught it. **The general rule, written where it will be read:** a writer that knows one field must never author the whole DTO, and the `With` helpers are what make the right thing the easy thing.
4. **`ProfileStore` lives in `BootScope`, not `RunScope`.** A profile outlives a run by definition, and the hint is spent during one — a store rebuilt per run would forget the write between the level-up that spent it and the boundary that saved it. It is `Game`-side rather than core for the reason `HapticsSettings` is: nothing in a simulation asks whether a hint has been seen.
5. **`Save` is fire-and-forget with a logged failure**, `HapticsSettings`' existing shape and `SaveWriter`'s argument: a profile write that fails must not take down a run, and a hint shown twice because a disk was full is the smallest possible consequence in the project.

**The hint**

6. **It fires on the first Active the player ever owns, and it asks the run rather than counting.** Subscribed to `NodeTaken` (M3-03 rule 7 carries the `Kind`, so no catalog lookup); when the kind is `Active` **and** `RunState.OwnedActiveCount` is 1 **and** `Current.SeenFirstActiveHint` is false, it arms. Reading the count rather than keeping one is what makes a resumed run behave: a run restored with two Actives already taken never arms, and it should not — the player has had actives for a while.
7. **It appears after the level-up screen closes, not over it.** M3-08b's canvas is full-screen and modal with the tick gated; a callout underneath it would be invisible and a callout above it would compete with the three cards for the two seconds GD §13.1 budgets. So it arms on `NodeTaken` and shows on the next `LevelUpClosed`, which is the frame the game starts again — the first moment the pause icon it points at is on screen and reachable.
8. **It points at the pause icon, because that is where the Skills screen is.** GD §5.2's top-right icon, M3-09a's target; the callout is anchored under it and reads *"Skills can be set to Manual — Pause → Skills"* as a `LocKey`, drawn unresolved like every other (ledger row 9's third reader). A hint that named a screen the player cannot find would be worse than none.
9. **It dismisses on a tap or after `_secondsShown`, and takes no touches from the game.** The run is ticking again by then (rule 7), so the callout must not sit in the raycast path of the stick or the Charge: `blocksRaycasts` stays false and the dismissal tap is read the way `HudPresenter` reads the death tap — through the run's `InputAdapter`, the one class allowed to. An auto-dismiss is what keeps a player who ignored it from carrying a box around for the rest of the stage.
10. **The flag is spent when the hint is *shown*, not when it is dismissed**, and saved on that frame. A player who sees the callout and is killed two seconds later has seen it; re-showing it would be the game disagreeing with them about their own memory. Spending it on dismissal would also mean an app killed mid-callout shows it again, which is exactly the *"Never again"* that CC §6.3 is asking for.
11. **Once is once across installs, not once across runs.** That is the whole of why this task bumps a format, and it is the difference between the two readings of CC §6.3. Stated so that a later reader does not "simplify" the flag into a field on a presenter.

## Tests

| Test | Given / When / Then |
|---|---|
| `Profile_RecordsTheNewField` | version 2, haptics false, seen true / ctor / all three read back |
| `Profile_DefaultHasNotSeenIt` | `PlayerProfile.Default` / — / `SeenFirstActiveHint` false, `Version` 2 |
| `Profile_WithHelpersMoveOneFieldEach` | a profile with haptics true, seen true / `WithHaptics(false)` / haptics false, **seen still true**; and the mirror row (rule 3) |
| `Profile_WithHelpersKeepTheVersion` | a v2 profile / either helper / `Version` still 2 |
| `ProfileGate_AcceptsOneAndTwoRefusesThree` | — / `CanReadProfile(1)`, `(2)`, `(3)` / true, true, false |
| `ProfileChain_IsUnbrokenFromOldestToCurrent` | *the existing row, unchanged* / — / loops 1..2 and passes — **the first time it has looped over more than one** (rule 2) |
| `MigrateProfile_V1_HasNotSeenTheHint` | a v1 profile carrying haptics false and `seen` true / `MigrateProfile(1, …)` / `Version` 2, haptics false, `SeenFirstActiveHint` **false** — the step is the authority (rule 2) |
| `MigrateProfile_V2_IsIdentity` | the existing identity row, extended to the new field |
| `Fixture_V1Profile_DecodesToTheExpectedProfile` | *the existing v1 literal* / `LoadProfile` / haptics as before, `SeenFirstActiveHint` false — the step through the real adapter (rule 2) |
| `Fixture_V2Profile_IsWhatThisBuildWrites` | `SaveProfile` of a v2 profile / read the file / text equals a hand-typed v2 literal byte for byte (rule 2) |
| `Store_StartsAtDefault` | a fresh `ProfileStore` / `Current` / `PlayerProfile.Default`, and the store asked the disk for nothing |
| `Store_AdoptDoesNotWrite` | a loaded profile / `Adopt` / `Current` is it; `SaveProfile` was never called (rule 4) |
| `Store_SaveWritesAndUpdates` | — / `Save(p)` / `Current` is `p`, one `SaveProfile` with `p` (rule 5) |
| `Store_SaveFailureIsLoggedNotThrown` | a store whose `SaveProfile` faults / `Save` / no throw, one logged exception, `Current` still updated (rule 5) |
| `Haptics_ToggleKeepsTheHintFlag` | a profile with `SeenFirstActiveHint` true / toggle haptics off / the persisted profile has haptics false **and the flag still true** — the row that would have caught rule 3 (rule 3) |
| `Haptics_StillPersists` | *the existing M1-20 rows* / — / unchanged: the setting survives, now through the store |
| `Hint_ShowsOnTheFirstActive` | a run, `SeenFirstActiveHint` false / take an Active, then `LevelUpClosed` / the root is on, the text is the key (rules 6, 7, 8) |
| `Hint_WaitsForTheScreenToClose` | the row above / between `NodeTaken` and `LevelUpClosed` / the root is still off (rule 7) |
| `Hint_IgnoresAPassive` | take a Passive / `LevelUpClosed` / the root stays off (rule 6) |
| `Hint_IgnoresTheSecondActive` | one Active already owned / take a second / the root stays off — `OwnedActiveCount` is 2 (rule 6) |
| `Hint_NeverShowsWhenTheFlagIsSpent` | a profile with `SeenFirstActiveHint` true / take the first Active / the root stays off (rules 6, 11) |
| `Hint_SpendsTheFlagOnShow` | the flag false / the hint shows / `ProfileStore.Current.SeenFirstActiveHint` true and one `SaveProfile`, **before** any dismissal (rule 10) |
| `Hint_DismissesOnTap` | the hint up / a tap / the root off (rule 9) |
| `Hint_DismissesOnItsOwn` | the hint up, no tap / `_secondsShown` of unscaled time / the root off (rule 9) |
| `Hint_TakesNoTouches` | the hint up / — / `blocksRaycasts` false on the root (rule 9) |
| `Hint_ResumedRunWithActivesDoesNotArm` | a snapshot naming two taken Actives / `StartResumed`, then take a third / the root stays off (rule 6) |
| `Hint_SurvivesARestart` | show it, then build a fresh `ProfileStore` over the same store / load / `SeenFirstActiveHint` true, the hint never arms — *"Never again"*, and the reason this task bumps a format (rule 11) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Delete `profile.json` under `persistentDataPath`. Descend with a hand-authored tree, take the first Active. The level-up screen closes and a callout appears under the pause icon; it fades on its own after four seconds.
2. **[Editor]** Take a second Active. No callout (rule 6). Stop Play, Play again, take an Active in a fresh run: still no callout — the flag is on disk (rule 11).
3. **[Editor]** Open `profile.json`: `"version":2` and `"seenFirstActiveHint":true`. Toggle haptics in the menu, then re-read it: haptics moved and **the hint flag is still true** (rule 3).
4. **[Editor]** Keep a `profile.json` written by the M2 build. Drop it in, boot. Haptics come back as they were, the hint is unseen, and the next write is at v2.
5. **[device]** Ledger row 4: whether a corner callout is noticed at all in the seconds after a level-up, when the player is re-orienting to a fight that just restarted. If it is not, the honest answer is a longer dwell, not a modal. Deferred.
6. **[device]** Ledger row 9: the callout reads as a key until M6-10, so what it is actually telling the player is nothing. Same verdict M3-15 has to reach for the cards and the rows.

## Out of scope

- **A tutorial, or any second hint.** CC §6.3 asks for one, once. A hint framework is M8's if it is ever anything's; a second flag in this profile is how one starts by accident.
- **Settings in the profile** — GD §18's sliders and toggles are M8-03's, and each is a field and a bump by rule 2's own rule.
- **Meta-progression** (GD §14's Shards and unlocks) — M4-07 and M6-09. Those are profile fields too, and rule 3 is the reason this task builds the store now rather than leaving four writers to discover each other.
- **Resolving the hint's `LocKey`** — M6-10, ledger row 9.
- **Showing the hint outside a level-up.** A player who takes their first Active from some future source (an Ordeal, a Sanctum) gets nothing; rule 6's condition is `NodeTaken`, and widening it is the job of whoever adds the second source.

## As built

_Filled at merge._

# M6-09a — Profile v4: the classes you own, the archetypes you have met, and a field for a language nobody speaks yet

**Size:** M · **Depends on:** M6-08 · **Branch:** `m6-09a-profile-v4`
**Design refs:** GD §8.2, §14.1, §14.2, §14.3, §19; CH §3; AR §10.3, §11.6, §18.3; ADR-0007, ADR-0011; M2-13b, M3-09c, M4-05a, M4-05b · **Ledger rows:** [7](../ROADMAP.md#carry-forward-into-m6) — the locale field lands here so M6-10 fills it rather than bumping again

## Goal

`PlayerProfile` becomes v4 carrying **everything the rest of this game will ask an install to
remember** — which classes it owns, which archetypes it has ever met, and what language it reads —
the v3 → v4 step ships in the same PR, and the profile is bumped once.

## Why all three land now, counted rather than argued

This is [M6-01b](M6-01b-save-format-v4.md)'s ruling one format over, and
[that spec named this task in writing](M6-01b-save-format-v4.md): *"GD §14.2's unlocks, GD §14.1's
archetype set and M6-10's locale are all profile fields, and the two formats version independently.
**M6-09** is the profile's one bump, by the same argument this task makes for the run's."*

**The number that makes it not a matter of taste:** `new PlayerProfile(...)` has **38 call sites
across 13 files**, and `SaveDtoTests.Profile_HasNoConstructorThatOmitsAField` forbids the convenience
overload that would hide them — *"one would compile at every existing call site on the day a fourth
field lands and quietly reset it, which is exactly the bug v2 exists to have fixed rather than
repeated."* So a bump is 38 sites whether it carries one field or three. Bumping once per feature —
unlocks here, the archetype set at M7, the locale at M6-10 — is **three** steps, three fixture sets
and **114** edited call sites, for a format that has never left this machine.

**The third field has no reader in this milestone and ships anyway**, which is exactly what M3-01b
and M6-01b each did once: `Locale` is written by nothing until
[M6-10](M6-10-the-rest-of-localisation.md), two tasks away in the same milestone, and the
alternative is a second step in the chain for ever.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Save/SaveDtos.cs` | Core | **Substantial.** Three fields on `PlayerProfile`, three `With` helpers, and the guards |
| `Core/Progression/ClassUnlocks.cs` | Core | GD §14.2's gate: what a class costs, what proves it, and what this run earned |
| `Core/Run/ShardPayout.cs` | Core | **Substantial.** GD §14.1's third term, and the absence it has guarded since M4-05a |
| `Tests/Core/Progression/ClassUnlocksTests.cs` | Tests.Core | The gate, the two routes, the deed nobody can do, and the grandfathering |
| *small edits* | Core, Game | `Core/Content/CharacterSpec.cs` — `UnlockSpec` beside it and an `unlock` argument, optional and last (rule 2); `Core/Save/SaveMigrations.cs` — the `if (version < 4)` step, below the `< 3` one (rule 6); `Core/Run/RunConfig.cs` — `archetypesAlreadyMet`, optional and last (rule 4); `Core/Run/RunSession.cs` — the payout's third term; `Core/Events/RunEvents.cs` — `ShardsAwarded.NewArchetypes`; `Game/Adapters/LocalJsonSaveStore.cs` — `ProfileMirror` gains three flat fields (rule 7); `Game/Adapters/ProfileStore.cs` — `Unlock`, and the lifetime set handed to a starting run; `Game/Adapters/ShardWriter.cs` — the set and the deeds recorded beside the total (rule 8); `Game/Authoring/CharacterDefinition.cs` — an Unlock foldout; `Data/Characters/*.asset` ×3 — rule 2's table |
| *ripple* | Tests.Core, Tests.Game | **38 `new PlayerProfile(...)` sites across 13 files** gain three arguments — compiler-guided, M6-01b's 36 at a third the file count; **76 `new RunConfig(...)` sites are untouched** (rule 4); `ShardPayoutTests` loses `Payout_HasNoArchetypeTerm` and gains rule 5's rows; `SaveDtoTests`, `SaveMigrationTests`, `ProfileStoreTests`, `ShardWriterTests` and `LocalJsonSaveStoreTests` each grow |
| *docs* | — | `GameDesign.md` §14.1's deferral note, which names this task and is now spent (rule 5) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Save;

public readonly struct PlayerProfile
{
    public const int CurrentVersion = 4;

    // ... the four v3 arguments, then:
    //     IReadOnlyList<ContentId> unlockedCharacterIds   // v4. Never null; never a default id
    //     IReadOnlyList<ContentId> metArchetypeIds        // v4. Never null; never a default id
    //     string locale                                   // v4. Never null; empty is "the device's"

    /// <summary>
    /// Which classes this install may pick, in the order they were earned (GD §14.2). **The
    /// starter is not in this list and does not need to be** — rule 3.
    /// </summary>
    public IReadOnlyList<ContentId> UnlockedCharacterIds { get; }

    /// <summary>
    /// Every enemy archetype this install has ever met, in first-meeting order — GD §14.1's third
    /// term, which is a <em>lifetime</em> fact and is why it is here rather than on a run.
    /// </summary>
    public IReadOnlyList<ContentId> MetArchetypeIds { get; }

    /// <summary>
    /// Which language table to read, or empty for the device's — <see href="M6-10-the-rest-of-localisation.md">M6-10</see>'s.
    /// </summary>
    /// <remarks>
    /// <b>A <see langword="string"/> rather than a <c>LocKey</c> or an enum</b>, and each was
    /// weighed. A <c>LocKey</c> is a key into a table and a locale names the table. An enum is a
    /// closed set, and ADR-0012's whole promise is that *"adding a language is adding a table"* — a
    /// member per language would make it adding a table and an enum member and a migration. A BCP-47
    /// tag is what Unity's own <c>Application.systemLanguage</c> maps to and what a file name can be.
    /// </remarks>
    public string Locale { get; }

    public PlayerProfile WithUnlocked(IReadOnlyList<ContentId> value);
    public PlayerProfile WithMetArchetypes(IReadOnlyList<ContentId> value);
    public PlayerProfile WithLocale(string value);

    /// <summary>
    /// A profile for a player who has never had one: the current format, GD §16.3's defaults,
    /// nothing seen, nothing banked, **nothing unlocked, nothing met, and no locale** — rule 3.
    /// </summary>
    public static PlayerProfile Default { get; }
}

// SaveMigrations.MigrateProfile (changed): a third step, `if (version < 4)`, below the other two —
// rule 6, and it is the one migration in this project that is not empty.
```

```csharp
namespace Soulvail.Core.Content;

/// <summary>
/// GD §14.2's two routes to a class: pay, or prove. Null on the starter, which is CH §3's
/// <em>"Free — the starter"</em> said in one word.
/// </summary>
public sealed class UnlockSpec
{
    /// <param name="shardPrice">What it costs (GD §14.2). Above zero — a free class authors null.</param>
    /// <param name="deedStage">
    /// The depth that proves it, or 0 for none. The Emberwright's 20.
    /// </param>
    /// <param name="deedBossId">
    /// The boss whose death proves it, or <c>default</c> for none. The Gravecaller's Choirmother —
    /// **and this build ships no such boss**, see the refusal below.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shardPrice"/> is not above zero.</exception>
    /// <exception cref="ArgumentException">Both deeds are set, which is two proofs for one class.</exception>
    public UnlockSpec(int shardPrice, int deedStage = 0, ContentId deedBossId = default);

    public int ShardPrice { get; }
    public int DeedStage { get; }
    public ContentId DeedBossId { get; }

    /// <summary>Whether any deed can prove this class — false for a price-only unlock.</summary>
    public bool HasDeed { get; }
}

public sealed class CharacterSpec
{
    // ... M6-07c's `veilrot`, then, optional and last — the third block in three tasks (rule 2):
    //     UnlockSpec unlock = null

    /// <summary>What this class costs, or <see langword="null"/> for the starter.</summary>
    public UnlockSpec Unlock { get; }
}
```

```csharp
namespace Soulvail.Core.Progression;

/// <summary>
/// GD §14.2, and the whole of GD §14's job: which classes an install may pick. Pure functions over a
/// profile and the catalog — <c>ShardPayout</c>'s shape and its argument for being static (rule 9).
/// </summary>
public static class ClassUnlocks
{
    /// <summary>
    /// Whether <paramref name="characterId"/> may be picked. True for any class with no
    /// <see cref="UnlockSpec"/>, and for any id in <c>profile.UnlockedCharacterIds</c> — rule 3.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
    public static bool IsUnlocked(ContentId characterId, in PlayerProfile profile, ContentCatalog catalog);

    /// <summary>
    /// Whether it could be bought right now: locked, priced, and affordable — rule 3's predicate,
    /// and the invariant behind <c>ProfileStore.Unlock</c>.
    /// </summary>
    public static bool CanBuy(ContentId characterId, in PlayerProfile profile, ContentCatalog catalog);

    /// <summary>
    /// Which locked classes a run just proved — rule 8. Writes into
    /// <paramref name="destination"/> and returns how many.
    /// </summary>
    /// <param name="deepestStage"><c>ShardsAwarded.DeepestStage</c>.</param>
    /// <param name="mode">The run's mode, for its authored boss roster.</param>
    /// <returns>How many entries were written. Zero on almost every death of almost every run.</returns>
    public static int Earned(
        int deepestStage, ModeSpec mode, in PlayerProfile profile, ContentCatalog catalog,
        Span<ContentId> destination);
}
```

```csharp
namespace Soulvail.Core.Run;

public static class ShardPayout
{
    /// <summary>GD §14.1's third coefficient: Shards per archetype met for the first time.</summary>
    public const int PerNewArchetype = 25;

    /// <param name="alreadyMet">
    /// Every archetype this install has met before this run — <c>PlayerProfile.MetArchetypeIds</c>.
    /// <see langword="null"/> is legal and means *none*, which over-pays rather than under-pays
    /// (rule 5).
    /// </param>
    public static int For(
        int deepestStage, ModeSpec mode, IReadOnlyCollection<ContentId> alreadyMet = null);

    /// <summary>
    /// Which archetypes a run to <paramref name="deepestStage"/> met that
    /// <paramref name="alreadyMet"/> does not hold. <b>Inclusive of the depth reached</b> — rule 5.
    /// </summary>
    /// <returns>How many entries were written.</returns>
    public static int NewArchetypes(
        int deepestStage, ModeSpec mode, IReadOnlyCollection<ContentId> alreadyMet,
        Span<ContentId> destination);
}
```

```csharp
namespace Soulvail.Core.Events;

public readonly struct ShardsAwarded
{
    // ... Total, DeepestStage and BossesKilled, then:

    /// <summary>
    /// The archetypes this run met for the first time, in the order they were met. Empty on almost
    /// every death; what <c>ShardWriter</c> adds to the profile's set — rule 8.
    /// </summary>
    public readonly IReadOnlyList<ContentId> NewArchetypes;
}
```

## Behaviour

1. **`PlayerProfile` grows three fields and no convenience constructor**, which is the rule v2 grew
   teeth for and v3 kept: `Profile_HasNoConstructorThatOmitsAField` stays green and all **38** sites
   are edited. Both lists are copied and wrapped on the way in, refuse `null` and refuse
   `default(ContentId)` — [M6-01b](M6-01b-save-format-v4.md) rule 4's contrast, and it holds for the
   same reason: neither list has an *empty entry* state to express. **An id this build no longer
   ships is not refused**, for `takenNodeIds`' reason — a class deleted from the catalog is content
   validation's answer, and a profile that refuses to load because a designer renamed an asset is the
   worse failure. `Locale` refuses null and accepts empty, because *"the device's"* is a real answer
   and *"nobody set this field"* is not.
2. **`UnlockSpec` is optional and last on `CharacterSpec`, which makes three blocks appended in three
   tasks — and the accumulation is named rather than left to be noticed.** `kindling` (M6-07a),
   `veilrot` (M6-07c) and `unlock` are each one optional argument on a constructor with **64 sites
   across 45 files**, and each costs those sites nothing. What it *does* cost is that
   `CharacterSpec`'s parameter list is now fifteen long, four of them nullable blocks. That is the
   price of CH §3's five identity axes being real, and the day it wants restructuring is the day a
   fourth class is authored — **M7**, where the count stops being three.
3. **The starter is unlocked by having no `UnlockSpec`, not by being in the list.** GD §14.2's table
   has two rows and CH §3's has three, and the Oathbound's cell reads *"Free — the starter"*. So the
   gate is *"this class authors a price"* rather than *"this id is in the profile"*, which means a
   fresh `PlayerProfile.Default` with an **empty** list is a playable game rather than one with no
   classes. Writing the starter into the list instead would make the empty profile unplayable and
   would put a content id in a migration for ever.
4. **The three authored unlocks, and one of them cannot be earned.**

   | Class | Price | Deed | GD §14.2 / CH §3 |
   |---|---|---|---|
   | **Oathbound** | — | — | *Free — the starter* |
   | **Gravecaller** | **2 000** | `boss.choirmother` | *2,000 Shards or kill Choirmother* |
   | **Emberwright** | **3 500** | stage **20** | *3,500 Shards or reach stage 20* |

   **The Gravecaller's deed is unreachable in V1 and it is refused in writing rather than silently
   inert.** GD §9.2's Choirmother is M7-03's, GD §19 lists it under the content pass, and grepped,
   `Descent.asset`'s boss roster authors the Warden of Ash and nothing else — so
   `UnlockSpec.DeedBossId` names an id `ContentCatalog` cannot resolve. `ClassUnlocks.Earned`
   therefore never matches it, and **the Gravecaller has exactly one route until M7-03 merges**.
   `Unlock_TheGravecallersDeedCannotBeDoneYet` asserts the absence with M7-03 in its message, which
   is [M6-04](M6-04-veilrot-thresholds-and-the-claiming.md)'s treatment of the Revenant: *the honest
   version of a thing nobody can build is one that does nothing and says so.*
   `ContentValidationTests` does **not** refuse the dangling id, because refusing it would mean
   deleting the design's own second route from the asset and re-adding it at M7-03.
5. **GD §14.1's third term ships, and the note that deferred it is spent.** `ShardPayout` was named
   for what it computes rather than for the formula precisely so this day would not read as a
   forgotten term; its remarks and GD §14.1's block-quote both name **M6-09**. The walk is the one
   both already describe: `ModeSpec.TryGetIntroduction` over `[1, deepestStage]`, minus what the
   profile holds, times 25.
   - **Inclusive of the depth reached, where `BossesKilled` is exclusive, and the contrast is the
     rule.** A boss must be *killed*, which only leaving the stage proves (M4-05a); an archetype is
     merely *met*, and a body spawns on arrival. A player who dies on stage 17 to the archetype
     introduced at stage 17 has met it.
   - **A null or empty set pays for everything, which is the direction GD §14.1 already chose:** *"an
     empty set pays 25 on the next Husk a returning player ever sees … shipping the term late
     **over**-pays them."* `RunConfig`'s argument is therefore optional (rule 6's counting), and a
     mis-wired run is generous rather than robbed.
   - **`ShardPayoutTests.Payout_HasNoArchetypeTerm` is deleted, not amended.** Its whole value was
     the absence and the comment explaining it; a row asserting the *presence* of the term is a
     different claim and gets its own name. This is M5-06a rule 8's `ContentValidationTests.Written`
     situation with the sign reversed — an entry whose absence was the point, retired by the task it
     named.
6. **`RunConfig` takes the lifetime set optional and last, and `ShardsAwarded` carries what was new.**
   `new RunConfig(...)` has **76 call sites**, so the argument is appended and defaults to null; the
   legitimate null is *"an install that has met nothing"*, which is every fixture and every first
   run, and rule 5 makes it the generous direction rather than the destructive one
   ([M6-06b](M6-06b-four-ordeals-and-two-refusals.md) rule 5's test for when a null may default).
   `ShardsAwarded` gains the **ids** rather than a count, because `ShardWriter` has to add them to a
   set and a count cannot say which. **One allocation per run**, on the death tick, which is not a
   frame path (AR §14) and is said here so the `AllocationAssert` rows are not read as broken.
7. **`ProfileMirror` flattens the three into three flat fields rather than nesting**, which is
   `RunMirror`'s treatment and its reason ([M6-01b](M6-01b-save-format-v4.md) rule 6): `JsonUtility`
   serialises a nested `[Serializable]`, and the mirror's job is to be a flat document a human can
   read in a bug report. The two lists are `string[]` defaulted to `Array.Empty<string>()` and the
   locale is a `string` defaulted to `""`, so a v3 document decodes without any of them.
8. **`ShardWriter` writes all three things a death produces, and that is one writer rather than
   three.** M3-09c rule 3 and M4-05b rule 2 gave the profile exactly one holder and one writer
   because *"a writer that authored the whole struct from the one field it knew would reset both on
   the frame the player died"*, and the `With` helpers are what make the right thing easy. A death
   produces a banked total, a set of archetypes met and possibly a class earned — **one run's
   outcome, three fields, one subscriber**, and splitting it would be three subscribers to one event
   racing to write one struct. It reads `ShardsAwarded`, adds the total, unions `NewArchetypes` into
   the set, and asks `ClassUnlocks.Earned` for what the depth proved. **The ordering is stated**: the
   archetypes go in before the deeds, so a run that both met a new archetype and reached stage 20 is
   paid for both and the save is written once.
9. **`ClassUnlocks` is static and it is not a violation, for `ShardPayout`'s reason and with its
   caveat.** No fields to mutate, nothing to reset under a disabled domain reload, nothing anybody
   could reach a dependency through — `SaveMigrations` and `ShardPayout` are the shipped precedents
   and each carries the argument. `Unlocks_HoldNoState` is what keeps it true rather than remembered,
   and **if it ever needs a collaborator it becomes an injected object that day**.
10. **The v3 → v4 step grandfathers what was playable, and that is a ruling with a direction behind
    it.** A v3 profile has no unlock list, and the shape-driven reading (`SaveMigrations`' own rule:
    *a v3 document is a v3 document*) would give it an **empty** one — which takes the Gravecaller
    away from an install that has been playing it since the `m5` tag. That is `PlayerProfile.Shards`'
    failure direction exactly (M4-05b rule 8): **a thing not written is data destroyed.** So the step
    writes `character.oathbound`'s peers — **`character.gravecaller`** — into the list, because those
    are the classes a v3 build could pick, and leaves the rest of the profile as read.
    - **The Emberwright is deliberately not grandfathered.** It is free to pick for four unmerged
      tasks inside this milestone and no build anyone has played outside this branch has ever had it,
      so grandfathering it would gate nothing in the only install that exists. **The owner's own
      profile will find it locked at 3 500**, which is the gate working and is manual step 3.
    - **Two literal `ContentId`s in a migration step is the right amount of content in a migration**,
      and it is bounded: this step runs once per install for ever and never again mentions a class.
11. **Nothing here allocates on a tick.** Two copied lists at load, one at each write, one
    `Span<ContentId>` walk per death, and `ClassUnlocks` is three dictionary probes.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Profile_CarriesItsThreeNewFields` | a v4 built with two lists and a locale / — / all three read back, both lists copied |
| `Profile_DefaultIsAFreshInstall` | `PlayerProfile.Default` / — / version 4, both lists **empty and not null**, locale `""`, shards 0 — rule 3 |
| `Profile_RefusesNullForAnyOfThem` | null for each in turn / — / `ArgumentNullException`, whose message says empty and null differ |
| `Profile_RefusesADefaultedId` | a list holding `default(ContentId)` / — / throws for each, naming which list and the index — rule 1 |
| `Profile_AnUnshippedIdIsNotRefused` | a list naming `character.deleted` / — / constructs — rule 1 |
| `Profile_ListsAreCopied` | a caller's `List<ContentId>` passed then mutated / — / the profile is unmoved |
| `Profile_HasNoConstructorThatOmitsAField` | *(existing)* `typeof(PlayerProfile)` / reflection / exactly one constructor, seven parameters — rule 1 |
| `Profile_TheThreeWithHelpersTouchNothingElse` | a full profile / each `With` in turn / only the named field moves — M4-05b rule 2's rule at seven fields |
| `Migrate_V3GrandfathersWhatWasPlayable` | a v3 profile with 650 shards / migrated / version 4, `UnlockedCharacterIds` is exactly **`character.gravecaller`**, shards **650**, locale `""` — rule 10 |
| `Migrate_V3DoesNotGrandfatherTheEmberwright` | the same / — / `character.emberwright` is **not** in the list — rule 10's stated cost |
| `Migrate_V4IsTheIdentity` | a v4 profile / migrated at 4 / byte-identical |
| `Migrate_V1RunsEveryStepInOrder` | a v1 profile / migrated / v4, haptics on, hint unseen, 0 shards, the grandfathered list, empty archetypes, empty locale — the chain, now three steps |
| `Migrate_RefusesAVersionAboveThis` | version 5 / `CanReadProfile` / false, and `MigrateProfile` throws |
| `Store_WritesAV4ProfileLiteral` | this build's save / the file on disk / three flat fields present — rule 7 |
| `Store_ReadsTheV3ProfileLiteral` | the checked-in v3 JSON / loaded / decodes, migrates, and the Gravecaller is unlocked |
| `Store_ADocumentWithoutTheNewFieldsDecodes` | a v4 document with all three absent / loaded / empty, empty, `""` — not null — rule 7 |
| `Spec_UnlockCarriesItsThree` | `new UnlockSpec(3500, deedStage: 20)` / — / all three read back, `HasDeed` true |
| `Spec_RefusesAFreePrice` | 0 and negative / — / throws: a free class authors null — rule 3 |
| `Spec_RefusesTwoDeeds` | a stage **and** a boss / — / throws — one proof per class |
| `Spec_PriceOnlyHasNoDeed` | `new UnlockSpec(2000)` / — / `HasDeed` false |
| `Unlock_TheStarterIsAlwaysPlayable` | `PlayerProfile.Default` / `IsUnlocked(character.oathbound, …)` / true, and the profile's list is still empty — rule 3 |
| `Unlock_TheOtherTwoAreNotByDefault` | the same / both ids / false |
| `Unlock_AnIdInTheListIsUnlocked` | a profile naming `character.emberwright` / — / true |
| `Unlock_ThePricesAreTheDocumentsNumbers` | the three shipped assets / converted / null, **2 000**, **3 500** — rule 4 |
| `Unlock_CanBuyIsAboutTheBalance` | 3 499 and 3 500 shards / `CanBuy(character.emberwright, …)` / false then true |
| `Unlock_CanBuyIsFalseForWhatIsOwned` | an unlocked class / — / false: there is nothing to buy |
| `Unlock_CanBuyIsFalseForTheStarter` | any balance / `CanBuy(character.oathbound, …)` / false — rule 3 |
| `Unlock_TheEmberwrightsDeedIsDepth` | a death at stage 19, then one at 20 / `Earned` / nothing, then `character.emberwright` — rule 4 |
| `Unlock_ADeedEarnedTwiceIsEarnedOnce` | an already-unlocked class / a stage-25 death / `Earned` writes 0 |
| `Unlock_TheGravecallersDeedCannotBeDoneYet` | a run killing every boss `Descent.asset` authors / `Earned` / the Gravecaller is **not** earned, and the row's message names **M7-03** and the unresolvable `boss.choirmother` — rule 4's refusal, pinned |
| `Unlocks_HoldNoState` | `typeof(ClassUnlocks)` / reflection / no field — rule 9, `Payout_HoldsNoState`'s row |
| `Payout_HasTheArchetypeTerm` | *(replaces `Payout_HasNoArchetypeTerm`)* stage 1, an empty set / — / **35**, not 10 — rule 5 |
| `Payout_PaysNothingForAnArchetypeAlreadyMet` | stage 1, a set holding `enemy.husk` / — / **10** — rule 5 |
| `Payout_IsInclusiveOfTheDepthReached` | an archetype introduced at exactly the death stage / — / paid, where `BossesKilled` excludes the same stage — rule 5's contrast, both asserted in one row |
| `Payout_ANullSetPaysForEverything` | stage 20, `alreadyMet: null` / — / every archetype introduced at or below 20 — rule 5's stated direction |
| `Payout_NewArchetypesWritesTheIds` | stage 12, a set holding the Husk / `NewArchetypes` / the Spitter and the Bloater, in introduction order |
| `Payout_ThirtySpecTermIsTwentyFive` | `PerNewArchetype` / — / 25 — GD §14.1, pinned against the coefficient drifting |
| `Awarded_CarriesTheNewArchetypes` | a run to stage 12 on a fresh install / the death tick / `ShardsAwarded.NewArchetypes` holds three, and `Total` includes 75 — rule 6 |
| `Awarded_IsEmptyForAReturningPlayer` | the same run on a profile that has met all three / — / empty, and `Total` is the two-term figure M4-05a shipped |
| `Writer_BanksAllThree` | a death at stage 20 on a fresh install / `ShardWriter` / shards added, the archetypes unioned, `character.emberwright` unlocked, **one** save — rule 8 |
| `Writer_DoesNotDuplicateAnArchetype` | a second run meeting the same three / — / the set is still three long |
| `Writer_TouchesNothingElse` | a full profile / a death / haptics, the hint and the locale are unmoved — rule 8, M4-05b rule 2's claim at seven fields |
| `Run_TheLifetimeSetReachesThePayout` | a live `RunSession` built with a set / a death / the payout used it — rule 6 |
| `Run_ANullSetIsLegal` | `new RunConfig(...)` as 76 sites write it / a death / no throw, and everything is new — rule 6 |
| `Profile_CarriesNoNumberThatAffectsARun` | `typeof(PlayerProfile)`'s seven members / a sweep of `Soulvail.Core` for readers / the only one read outside `Core/Save` is `MetArchetypeIds`, and its one reader is `ShardPayout` — **nothing reaches a `Stat`, a `ThreatBudget` or a `Health`** — GD §14.3's *"no permanent power progression"*, asserted in the task that could smuggle one in |
| `Payout_AllocatesNothingPerTick` | 100 000 `IsUnlocked` and `CanBuy` calls / `AllocationAssert.None` / zero — rule 11 |

**Guard rows are implied, not listed:** version below 1, a negative shard total, nulls to
`ClassUnlocks`, a `deepestStage` below 1, and every v3 profile guard firing unchanged.

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 3 and die on a fresh profile (delete `profile.json` first). Open the
   file. *Expected: `"version": 4`, `"unlockedCharacterIds": []`, `"metArchetypeIds"` holding the
   Husk, `"locale": ""`, and the payout on the run-end screen is **55** rather than 30 — rule 5.*
2. **[Editor]** Replace it with the checked-in v3 literal and boot. *Expected: it loads, banks its
   shards, and `unlockedCharacterIds` reads `character.gravecaller` — rule 10.*
3. **[Editor]** Boot on the owner's own profile. *Expected: the Oathbound and the Gravecaller are
   there and **the Emberwright is not**, which is rule 10's stated cost and the gate working. Nothing
   is drawn about it yet —
   [M6-09b](M6-09b-a-class-you-cannot-pick-yet.md) is what puts a price on the card.*
4. **[Editor]** Reach stage 20 and die. *Expected: `unlockedCharacterIds` gains
   `character.emberwright` without a Shard being spent — rule 4.*

## Out of scope

- **Drawing any of it.** [M6-09b](M6-09b-a-class-you-cannot-pick-yet.md): the locked card, the price,
  the deed and the purchase. After this task the gate is real and no screen asks it, which is
  [M6-01a](M6-01a-essence-wallet-and-drops.md)'s bargain for the fourth time in this milestone.
- **The Choirmother.** M7-03. Rule 4 authors the id the design names and says it resolves to nothing.
- **Cosmetics.** GD §14.2's third row (100–300 Shards). There is no cosmetic, no slot and no task
  that owns one; GD §19's V1 list says *"Soul Shards → class unlocks **and cosmetics** only"* and
  what ships is the first half. A [parking-lot](../ROADMAP.md#parking-lot) line, promoted by **M7-05/06**,
  which is the first art that could produce one.
- **Anything that reads `Locale`.** [M6-10](M6-10-the-rest-of-localisation.md). The field is written
  by nothing after this task, which is rule 6's stated bargain.
- **A permanent power layer.** GD §14.3 forbids it and this task is the one that could smuggle one
  in: the profile now carries two sets, and neither is a stat.
  `Profile_CarriesNoNumberThatAffectsARun` is a guard row rather than a comment.
- **Re-locking a class.** There is no path down. GD §14.2 is a ratchet.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._

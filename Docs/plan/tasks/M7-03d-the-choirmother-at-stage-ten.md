# M7-03d — The Choirmother at stage 10, standing where the room was built for her

**Size:** M · **Depends on:** M7-03c — nothing rosters a song until something draws it · **Branch:** `m7-03d-the-choirmother-at-stage-ten`
**Design refs:** GD §7.2, §9, §9.1 (rules 2, 6), §9.2, §12.4, §14.2, §19; CH §3; AR §18.2, §18.3; ADR-0011 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m7) — her telegraphs join the device list; [2](../ROADMAP.md#carry-forward-into-m7) — the Warden's fight-length series loses stages 10, 20 and 30 to her (rule 4)

## Goal

Descent holds the Choirmother at every tenth stage and the Warden of Ash at the fifth between. Every
boss stands up on a mark its arena authors, so a boss that never walks stands in the middle of the
room, with pillars around her. And the Gravecaller's card finally says *"or kill the Choirmother"*,
because now she can be killed.

## Why it is last, and why a mark

**Last**, because a roster row is what puts a boss in front of the player. [M4-02](M4-02-warden-behaviours.md)
rostered the Warden a task before anything drew its attacks. [M4-03](M4-03-boss-arena-and-views.md)'s
*As built* records the result: a build in which the owner lost 22 hit points *"with nothing on the floor
to say it was coming"*. Her core ([M7-03a](M7-03a-the-ring.md), [M7-03b](M7-03b-the-choirmother.md)) and
her views ([M7-03c](M7-03c-a-song-you-can-see.md)) are all merged before this row exists.

**A mark**, because a boss that never walks cannot stand on a drawn spawn point. `SpawnDirector.TickBoss`
puts a boss on whichever point the `Spawn` stream draws, and the Warden walks to the player from
anywhere. Measured on the shipped prefabs, she cannot use that rule:

- **`Arena_Pillars`' `Spawn_04` at (−6, 0, 12)** is 4.5 m from `Pillar_NW`'s centre, so the pillar's
  1.5 m radius leaves its face 3.0 m away. Her ring's 3 m radius (`BossBehaviour.AddRingRadius`, where
  M7-03a rule 5 binds each shield) would walk a shield into the pillar.
- **The fight's geometry would change with the draw.** A song from a point by a wall has no pillar to
  break it on one side and three on the other.

**Every** boss uses the mark, not only her, because *"where a boss stands"* is one rule. The Warden,
standing up in the middle of the room and walking out, is the same fight from a better first frame.

## What was already built for it

- **`ModeSpec.TryGetBossFor`'s first-match rule was written for this row.** Its remarks say *"GD §9
  puts an Archon on every 20th stage and a boss on every 5th … the Archon's row is authored first."*
  The Choirmother is that sentence one decade early. `every 10` authored before `every 5` makes 5
  the Warden, 10 her, 15 the Warden, 20 her. `CopyBossRoster` refuses a row naming no boss, a boss
  listed twice, and two rows on one interval. Two bosses on 10 and 5 are none of those.
- **The Gravecaller's deed has named `boss.choirmother` since M6-09a**, and two rows pin that it cannot
  be done yet, each with a message saying to retire it when M7-03 lands.
  [M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md) rule 4 has *"exactly one route until M7-03
  merges"*. [M6-09b](M6-09b-a-class-you-cannot-pick-yet.md) rule 4 has *"the line arrives with the
  boss"*. **`ClassUnlocks.DeedDone` needs no edit**: it already matches a boss stage below the deepest.
- **Where a body may stand has been a property of the arena since M2-11a** (AR §18.2). The spawn
  points ride `WorldSnapshot.SpawnPoints`, and the door rides `GatePosition` / `HasGate`. A mark is that
  pattern once more.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Director/SpawnDirector.cs` | Core | `Begin` takes the arena's mark; `TickBoss` stands a boss on it (rules 1–2) |
| `Game/Arena/ArenaView.cs` | Game | `_bossMark`, `HasBossMark`, `BossMark`, and its two faults in `DescribeFaults` (rule 3) |
| `Game/Presentation/ClassSelectPresenter.cs` | Game | The deed line for a boss that can be killed (rules 6–7) |
| `Tests/Core/Director/BossMarkTests.cs` | Tests.Core | **New.** Rules 1, 2 and 8: the director and the flow |
| *small edits* | Core, Game, Data, Docs | `Core/Run/WorldSnapshot.cs` — `BossMark`, `HasBossMark`, lowered by `Clear`; `Core/Stage/StageFlow.cs` — `EnterWaves` hands the mark over; `Game/Arena/ArenaPool.cs` — adopts it on each raise, beside the spawn points; `Game/Adapters/SnapshotBuilder.cs` — copies it beside `SpawnPoints`; `Core/Progression/ClassUnlocks.cs` — `DeedCanBeDone` (rule 6); `Game/Controls/ClassCard.cs` — `BindLocked` takes the deed's argument (rule 7); `Prefabs/Arenas/Arena_Pillars.prefab` and `Arena_Tiered.prefab` — a *BossMark* child each (rule 3); `Data/Modes/Descent.asset` — the boss roster (rule 4); `Data/Localisation/English.asset` — `ui.classselect.locked.deed.boss`; `Pseudo.asset` regenerated; `Docs/GameDesign.md` — §9.2's stage column read as V1 authors it (rule 4); `Docs/Architecture.md` — one §18.2 row (rule 1). **The rows that are not the director's go beside their subjects**, and every one that opens an asset, a prefab or a Game type is in Tests.Game, because Tests.Core cannot reach them (M6-07c's reason): `Tests/Game/Adapters/SnapshotBuilderTests.cs` — the `Snapshot_*` row; `Tests/Game/Arena/ArenaViewTests.cs` — the three `Arena_*` rows; `Tests/Game/Authoring/ModeDefinitionTests.cs` — the `Descent_*` row; `Tests/Game/Authoring/ContentValidationTests.cs` — `Run_ADescentRunResolvesHerFight`; `Tests/Game/Authoring/CharacterDefinitionTests.cs` — the Gravecaller's replaced row; `Tests/Core/Progression/ClassUnlocksTests.cs` — `Unlock_ADeedCanBeDoneOnlyIfAModeFightsIt`; `Tests/Game/Presentation/ClassSelectPresenterTests.cs` — the three `Card_*` rows; `Tests/Game/LocalisationSweepTests.cs` — the `Localisation_*` row |
| *ripple* | Tests.Core, Tests.Game | `CharacterDefinitionTests.Unlock_TheGravecallersDeedCannotBeDoneYet` and `ClassSelectPresenterTests.Card_ABossDeedDrawsNoLine` are **retired** by the task their messages name, and replaced (rules 6–7). Fixtures that start a run over the real `Descent.asset` with a catalog built by path gain her three assets and the Weaverling: the three tree fixtures, which are also M7-01c's (rule 5). `ClassSelectPresenterTests`' 7 `BindLocked` sites pass the argument. `ClassUnlocksTests`' *"Descent's boss schedule"* fixture and its remark follow the asset. **Every `SpawnDirector.Begin(...)` site is untouched** (30 in `SpawnDirectorTests` alone at M7-00c): the mark is optional and last |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Run;

public sealed class WorldSnapshot
{
    /// <summary>Where a boss stands up in the arena standing right now. Meaningless when <see cref="HasBossMark"/> is false.</summary>
    public Vector3 BossMark;

    /// <summary>This arena authors a mark. False in an undressed scene, which falls back to the draw (rule 1).</summary>
    public bool HasBossMark;
}
```

```csharp
namespace Soulvail.Core.Director;

public sealed class SpawnDirector
{
    /// <param name="bossMark">
    /// Where a boss stands up in this stage's arena, or null for an arena that authors none.
    /// Finite, or refused with the spawn points' exception (rule 1).
    /// </param>
    public void Begin(WavePlan plan, IReadOnlyList<Vector3> spawnPoints, float now, ContentId bossId = default, Vector3? bossMark = null);
}
```

```csharp
namespace Soulvail.Core.Progression;

public static class ClassUnlocks
{
    /// <summary>
    /// Whether <paramref name="unlock"/>'s deed can be done in this build: a depth deed always, and a
    /// boss deed when the catalog holds the boss and some mode's boss roster names it (rule 6).
    /// </summary>
    public static bool DeedCanBeDone(UnlockSpec unlock, ContentCatalog catalog);
}
```

```csharp
// Game/Arena/ArenaView.cs — block namespace (Traps §5).
namespace Soulvail.Game.Arena
{
    public sealed class ArenaView : MonoBehaviour
    {
        /// <summary>Metres of floor a mark keeps clear of cover: a boss's ring and a body. <c>BossBehaviour.AddRingRadius</c> + 1.</summary>
        public const float BossMarkClearance = 4f;

        public bool HasBossMark { get; }
        public Vector3 BossMark { get; }
    }
}

// Game/Controls/ClassCard.cs — the deed's argument is the presenter's to supply (rule 7).
public void BindLocked(
    CharacterSpec spec, int price, bool affordable, LocKey deed, object deedArgument,
    ILocalizer localizer, Action<ContentId> onUnlockTapped);
```

## Behaviour

1. **A boss stands up on the arena's mark when the arena authors one.**
   - **The handover.** `StageFlow.EnterWaves` hands `snapshot.BossMark` to `Begin` when `HasBossMark`
     is true, and null otherwise. That is the spawn points' path: read off the snapshot of the frame
     the waves begin, because a run has one room per stage (M2-11a rule 6).
   - **The stand.** `TickBoss` spawns the boss at the mark instead of the drawn point.
   - **No mark.** An arena that authors none, which is every core fixture and the undressed Run scene,
     keeps today's drawn point, rule for rule.
   - **AR §18.2 gains a row:** *where a boss stands is a property of the arena, `WorldSnapshot.BossMark`,
     and the draw decides only where none is authored.*
2. **The mark owes the player what a point owes, and costs what a point costs.**
   - **Clearance.** A boss is not stood on a mark within `MinPlayerDistance` (6 m, XZ) of the player.
     The attempt waits a tick, which is GD §12.4's spawn safety and today's bargain for a point.
   - **The draws a point would have cost, and no more.** Each attempt on a mark makes the draw
     `TryPickPoint` would have made, and discards it: one on an arena that authors spawn points, none on
     one that authors none (`TryPickPoint`'s own inertness). That is the *"one draw, then walk"* rule of
     AR §18.3, kept by counting rather than by walking. On the usual first tick, with the player clear
     of the mark and some point clear too, both paths draw once, so adding a mark moves no seed's
     later stages. They part only on a tick one path would refuse and the other would not.
   - **The points' answer is not the mark's.** A mark stands the boss whenever the player is clear of
     it, even on a tick when every spawn point is claimed or inside 6 m. An arena with a mark and no
     spawn points still gets its boss, where today it would wait for ever.
   - **The claim.** A mark claims no spawn point, because a boss stage composes no waves to claim
     against.
3. **Both shipped arenas author a mark, and validation holds it to two faults.**
   - **The marks.** `Arena_Pillars` at **(0, 0, 0)**: the four pillars stand 11.3 m out at the
     diagonals, one shadow per quadrant. `Arena_Tiered` at **(0, 0.6, 0)**, on the dais: the ring's
     3 m stays inside the dais's 7 m, and the five pillars stand 14–17 m out at the rim.
   - **Fault one, player clearance.** A mark within `SpawnDirector.MinPlayerDistance` of `PlayerStart`.
     The shipped starts are 14 m and 18.4 m away.
   - **Fault two, cover clearance.** Any body on the `Cover` layer whose footprint comes within
     `BossMarkClearance` of the mark, which would put a shield into a pillar (M7-03a rule 5).
     - **Measured to the face, not the centre.** The distance runs on XZ from the mark to the nearest
       point of the body's footprint: the ±0.5 square of its own transform, which is every shipped
       pillar, a unit cube or cylinder scaled. `Arena_Pillars`' nearest face is 9.2 m away.
     - **Found by walking transforms, never by a physics query.** It is the walk `CoverCount` already
       makes. `DescribeFaults` runs from `OnValidate` on a prefab outside any scene, where an overlap
       query finds nothing and would pass every mark.
   - **Where it bites.** `DescribeFaults` warns, as every arena rule does, and `ArenaViewTests` asserts
     it on the shipped prefabs. A mark is optional in code and required of every shipped arena:
     M7-05/06's sixteen to twenty-four rooms inherit that row.
4. **Descent holds her at every tenth stage.** `_bossRoster` becomes `boss.choirmother` every **10**,
   then `boss.warden` every **5**, in that order. Stages 5, 15, 25 and 35 are the Warden; 10, 20, 30 and
   40 are her.
   - **GD §9.2's table puts Gravemaw at 15 and the Archon at 20**, and GD §19 puts both in V2. So V1's
     two bosses alternate, and §9.2 gains the sentence that says so.
   - **Ledger row 2's Warden series** (89 → ~126 s over 5–35) was measured with a Warden at every fifth
     stage. From this task it has none at 10, 20 or 30. The row says so, and its instrument reads both
     bosses.
5. **A Descent run resolves her whole fight before it announces anything.**
   `RunSession.Start` over the real `Descent.asset` and every shipped definition succeeds. M7-03a rule
   11 walks her body, her three phases' `enemy.weaver_shield` and its `enemy.weaverling`. A catalog one
   of those short is refused by name at `Start`, which is the check working: the tree fixtures that
   build their catalog by path find out that way and gain the four assets. No test reads
   `BootScope.prefab`'s lists, so manual step 2 is what proves the two new ones are wired there.
6. **A deed line is drawn exactly when the deed can be done.** `ClassUnlocks.DeedCanBeDone` answers:
   - **a depth deed**: always;
   - **a boss deed**: when `catalog.TryGetBoss(DeedBossId)` resolves **and** some `catalog.Modes`
     entry's `BossRoster` names it;
   - **no deed**: false.

   **M6-09b rule 4's ruling survives as the predicate.** A deed that names a boss no mode fights draws
   nothing, and `Card_ADeedNoModeFightsDrawsNoLine` keeps it true for every later boss. **`Earned` is
   unchanged**: past stage 10, `DeedDone` finds her at 10, so reaching stage 11 earns the Gravecaller.
7. **The boss line names the boss, and the sentence carries the article.**
   `ClassSelectPresenter.DeedOf` returns a key and its argument:
   - **a stage deed**: `ui.classselect.locked.deed` with the stage, as today;
   - **a boss deed that can be done**: `ui.classselect.locked.deed.boss`, *"or kill the {0}"*, with
     `localizer.Get(bossBody.NameKey)`, the body `BossSpec.EnemySpecId` names;
   - **otherwise**: `default`, and no line.

   So the Gravecaller's card reads *"or kill the Choirmother"*. **The article is in the sentence and not
   in the name**: *"Choirmother"* is how GD §14.2 and CH §3 write it, and a name that carries its own
   article is right in one grammatical position only. `enemy.warden_of_ash.name`'s *"The Warden of Ash"*
   (M7-01c) is read by nothing and is left alone.

   `ClassCard.BindLocked` formats `deed` with the argument it is handed rather than reading
   `DeedStage` itself. The presenter decides what the line says, and the card draws it (M6-09b rule 1).
8. **Nothing on a frame path allocates.** The mark is a struct copy per frame and a nullable per stage.
   The deed line is resolved once per draw of a screen nobody plays on.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Mark_ABossStandsOnIt` | `Begin` with a boss and a mark at (0, 0, 0), the player 14 m away / the first tick / the boss at the mark — rule 1 |
| `Mark_WithoutOneTheDrawDecides` | the same with no mark / — / the boss on the drawn point, exactly as M4-01b's rows have it — rule 1 |
| `Mark_WaitsForThePlayerToStepOff` | the player 4 m from the mark / ticks / no boss; the player walks to 7 m / the next tick / the boss at the mark — rule 2 |
| `Mark_CostsWhatAPointWouldHave` | one seed, with and without a mark, one attempt each / — / the `Spawn` stream advanced by one in both; by none over an arena with no spawn points — rule 2 |
| `Mark_StandsHerWhenEveryPointIsRefused` | every spawn point claimed or inside 6 m, the mark clear / a tick / the boss on the mark; and on an arena with a mark and no points — rule 2 |
| `Mark_ClaimsNothing` | a boss stood on a mark / — / no spawn point claimed — rule 2 |
| `Mark_ANonFiniteMarkIsRefused` | `Begin` with a NaN mark / — / `ArgumentOutOfRangeException`, the director as it was — rule 1 |
| `Snapshot_CarriesTheMarkAndClearLowersIt` | an arena with a mark raised / a frame built, then `Clear` / `HasBossMark` and the position, then false — rule 1 |
| `Flow_HandsTheMarkToTheDirector` | a boss stage whose snapshot carries a mark / `EnterWaves` / the boss stands on it — rule 1 |
| `Arena_EveryShippedArenaAuthorsAMark` | both prefabs / — / a mark each, at (0, 0, 0) and (0, 0.6, 0) — rule 3 |
| `Arena_TheMarkIsClearOfThePlayerAndTheCover` | both prefabs / `DescribeFaults` / no fault: ≥ 6 m from `PlayerStart`, and no `Cover` face within 4 m — rule 3 |
| `Arena_AMarkInCoverIsAFault` | a fixture arena, never entered into a scene, with a unit `Cover` cube whose near face is 2 m from its mark / `DescribeFaults` / names the mark and the clearance — rule 3 |
| `Descent_RostersTheChoirmotherAtTen` | `Descent.asset` / converted / her row first at 10, the Warden's at 5; `TryGetBossFor` 5, 10, 15, 20 is Warden, her, Warden, her — rule 4 |
| `Run_ADescentRunResolvesHerFight` | `Descent.asset` and every enemy and boss definition on disk (`PathsOf<T>`) / `Start` / announced, nothing refused — rule 5 |
| `Unlock_TheGravecallersDeedIsDoneAtEleven` | *(replaces `…CannotBeDoneYet`)* `Earned(10, …)` and `Earned(11, …)` over the shipped assets / — / not earned at 10, earned at 11 — rule 6 |
| `Unlock_ADeedCanBeDoneOnlyIfAModeFightsIt` | a boss deed naming a boss the catalog holds and no mode rosters / `DeedCanBeDone` / false; rostered, true; a depth deed, true — rule 6 |
| `Card_ABossDeedNamesTheBoss` | *(replaces `Card_ABossDeedDrawsNoLine`)* the shipped catalog and English / opened / the Gravecaller's deed reads *"or kill the Choirmother"* — rule 7 |
| `Card_ADeedNoModeFightsDrawsNoLine` | a catalog whose modes roster no `boss.choirmother` / opened / no deed line on the Gravecaller — rule 6 |
| `Card_TheStageDeedIsUnchanged` | the Emberwright / opened / *"or reach stage 20"* — rule 7 |
| `Localisation_TheBossLineIsInBothTables` | `ui.classselect.locked.deed.boss` / English and Pseudo / present, one `{0}` each — rule 7 |
| `Mark_TheDirectorAllocatesNothing` | 10 000 boss ticks on a mark / `AllocationAssert.None` / zero — rule 8 |

**Guard rows are implied, not listed:** `DeedCanBeDone`'s null unlock and null catalog.

## Manual verification (Editor / device)

1. **[Editor]** Stage 5. *Expected: the Warden of Ash stands up in the middle of the room and walks out
   at you. It is otherwise the fight M4 shipped.*
2. **[Editor]** Stage 10. *Expected:*
   - *A tall grey-blue figure in the middle of the room, ringed by six pale shields turning slowly, with
     a steel circle on the floor under them.*
   - *Five seconds after each song, a red-orange wedge fills toward you for a second, with
     pillar-shaped notches cut out of it. Standing in a notch, you are not hit.*
   - *Hits on her from behind a shield flash steel. The gun picks the shield in the way.*
3. **[Editor]** Kill one shield. *Expected:*
   - *two Weaverlings where it fell, a gap in the steel circle, and the gap turning with the ring;*
   - *standing in the gap, your hits land on her and flash white.*
4. **[Editor]** Take her past 66 %. *Expected:*
   - *a beat;*
   - *on its first frame, every shield and every Weaverling gone and a ring of seven standing in
     their place;*
   - *no wedge left on the floor through it.*
5. **[Editor]** As the Oathbound or the Emberwright, die past stage 10, then open class select.
   *Expected: the Gravecaller owned, without the Shards being spent.* On a profile that has not done
   it: *the Gravecaller's card reads "or kill the Choirmother".*
6. **[device]** GD §9.1 rule 7 for her whole fight, and the frame time with a ring of eight, sixteen
   Weaverlings and a wedge up at once. Deferred with [M7 row 1](../ROADMAP.md#carry-forward-into-m7).

## Out of scope

- **Gravemaw and the Archon.** GD §19 puts both in V2. Rule 4 authors V1's two.
- **An authored arena per boss.** The mark makes any room with a clear middle hers. A room built for
  one boss is the [parking lot](../ROADMAP.md#parking-lot)'s stage editor, promoted by M7-05/06 if a
  biome wants one.
- **What killing her pays.** GD §14.1's 50 Shards and GD §15's 60 Essence are per boss and already
  paid by `ShardPayout` and `EssenceWallet`, which ask `TryGetBossFor` and care nothing for which boss.
- **Her music.** M7-07.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._

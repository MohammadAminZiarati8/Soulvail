# M5-05a — Wights on screen, and the frame step a body costs

**Size:** M · **Depends on:** M5-04a · **Branch:** `m5-05a-wight-views`
**Design refs:** CH §3.2, §8 q1; GD §11.1, §11.3, §16.4; AR §3, §4.2, §4.3, §14, §18.1, §18.2; ADR-0003 · **Ledger rows:** [4](../ROADMAP.md#carry-forward-into-m5)

## Goal

A Wight has a body: Unity draws one where core says one stands, reports back where it actually
ended up, and walks it on the tick core told it to — and the frame order that now has a fifth kind
of body in it is written down in the fixture that asserts orderings.

## Why this is a task and not the whole of M5-05

The ROADMAP's single M5-05 row owns the Wight's body, M5-03's corpse, [row 1](../ROADMAP.md#carry-forward-into-m5)'s
font sizes, [row 3](../ROADMAP.md#carry-forward-into-m5)'s material keyword and [row 4](../ROADMAP.md#carry-forward-into-m5)'s
fixture instrumentation. Counted against the shipped code that is six counted files before the
assets, and [M5's own table](../ROADMAP.md#m5--second-class) said so in advance rather than leaving
the split to be discovered.

**The seam is what can be seen.** This half changes the *frame* — a new intent door, a new census, a
new apply step, and the PlayMode fixture that asserts the order of the ones that already exist —
and **nothing it ships can be looked at in play**, because no run this build can start raises a
Wight (rule 12). [M5-05b](M5-05b-decoy-view-and-the-look.md) is the half where somebody stands in
front of the screen: a corpse, four labels at a readable size, and one keyword that changes how six
prefabs blend. Putting them in one PR would mean one reviewer holding a frame-order argument and a
legibility measurement at the same time, and the ledger rows would be read as decoration on a
views task.

Rows 1 and 3 go to M5-05b for the same reason and row 4 stays here: this is the half that opens
`Tests/PlayMode/FrameOrderTests.cs`, and it opens it because the frame gains a step, not because
somebody went looking.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/MinionView.cs` | Game | One Wight's body: bound, walked, returned. `EnemyView`'s shape and deliberately not `EnemyView` (rule 1) — **block namespace** ([Traps §5](../../Traps.md)) |
| `Game/Views/MinionViews.cs` | Game | The friendly census: the pool, the two events, and `CopyInto` |
| `Tests/Game/Views/MinionViewsTests.cs` | Tests.Game | Rental, binding, the snapshot, the cap, and what a Wight is not |
| `Tests/PlayMode/FrameOrderTests.cs` | Tests.PlayMode | **Substantial.** The minion step placed in the asserted order, and [ledger row 4](../ROADMAP.md#carry-forward-into-m5)'s instrumentation (rules 9, 10) |
| `Prefabs/Minions/Wight.prefab` | — | The body. An asset, not a code file — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | Game | `IntentBuffer` implements `IIntentSink.MinionMove` and exposes `MinionMoves` (rule 7); `SnapshotBuilder` takes `MinionViews` and copies it in (rule 3); `RunTicker` takes it and gains `ApplyMinionMoves` (rule 6); `RunScope` gains `_minionPrefab` and `_minionParent` and registers the census |
| *ripple* | Tests.Game | `SnapshotBuilderTests` and `InstallerTests` — the builder's constructor and the scope's field list both grow by one |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Game/Views/MinionView.cs
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One Wight's body. EnemyView's shape — an id, a reported position, an applied move — and
    /// deliberately a different type, for rule 1's reason.
    /// </summary>
    public sealed class MinionView : MonoBehaviour, IPoolable
    {
        public const int Unbound = 0;

        public int Id { get; }
        public bool IsBound { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }

        /// <summary>Walks this frame's slice. EnemyView.Apply's contract, minus the knockback.</summary>
        public void Apply(in EnemyMoveIntent intent, float dt);

        public void Bind(int id, Vector3 position);
        public void Unbind();

        public void OnSpawn();
        public void OnDespawn();
    }
}

// Game/Views/MinionViews.cs
namespace Soulvail.Game.Views
{
    public sealed class MinionViews : IDisposable
    {
        /// <param name="prewarm">
        /// MinionSystem.MaxConcurrent, so the only Instantiate calls of a whole run happen while the
        /// scene is loading rather than on the tick a kill raises something.
        /// </param>
        public MinionViews(
            IObjectResolver resolver,
            MinionView prefab,
            Transform parent,
            DomainEventHub hub,
            int prewarm = 0);

        public int Count { get; }

        public bool TryGet(int id, out MinionView view);

        /// <summary>Writes one EnemySense per standing Wight into the snapshot's minion slots.</summary>
        public void CopyInto(WorldSnapshot snapshot);

        public void Dispose();
    }
}

// Game/Adapters/IntentBuffer.cs — widened
/// <summary>
/// Every minion core gave a direction to this tick. A second list beside EnemyMoves, never a
/// shared one — rule 7.
/// </summary>
public IReadOnlyList<EnemyMoveIntent> MinionMoves { get; }
```

## Behaviour

1. **A Wight's body is its own type, and the reason is one dictionary.** `EnemyViews` keys
   `_idByColliderInstance` by `Collider.GetInstanceID()`, and that index is the whole of what
   `ConeOverlapQuery.Query` turns a physics hit into an enemy id with. A Wight rented from the enemy
   pool would be in it, so **the player's own swing would report its id to
   `RunSession.ReportConeHits`, and core would apply the swing's damage to whichever *enemy* holds
   that number** — not to the Wight, which is not in `EnemyRegistry` at all (M5-04a rule 1), but to a
   stranger. It would be silent, it would be wrong by an amount nobody could predict, and it is
   reachable on the first swing of the first Gravecaller run. `EnemyViews.CopyInto` filling the
   snapshot's *enemy* slots with friendly bodies is the second of the same mistake. **The cost of a
   separate view is about eighty lines and a second pool; the cost of reuse is the targeting system
   quietly lying.**
2. **It rents and returns, and the pool is prewarmed to the ceiling.** `ViewPool<MinionView>` at
   `MinionSystem.MaxConcurrent` — eight — so a run allocates nothing after the scene has loaded,
   which is `EnemyViews`' M1-19 bargain and AR §14's ban on the frame path. `MinionSpawned` rents
   and binds, `MinionDespawned` and `MinionDied` both return: **two events, one release**, because a
   clock running out and a killing blow are different facts about the run (M5-04a rule 10) and the
   same fact about the body.
3. **It reports where it is, and it writes every field of the slot it is given.** `CopyInto` fills
   `Id`, `Position`, `Velocity` and `PathDirectionToPlayer` on each minion slot for
   `EnemyViews.CopyInto`'s reason: slots are reused and `WorldSnapshot.Clear` leaves their contents
   alone (AR §4.2), so a field left unwritten carries whatever the Wight that last occupied that slot
   put there. **`SnapshotBuilder.WriteSenses` is deliberately not extended to the minion slots**:
   M5-04a rule 4 says `HasLineOfSight` and `PathDirectionToPlayer` are unread on a minion — a Wight
   walks in a straight line at something under 12 m away, and `NavPathSense` only ever paths to the
   *player* — so a path computed here would be a route to the wrong place, costed per Wight per
   frame, and read by nothing. The zero is written; the search is not run. A row pins both halves.
4. **The census is copied in after the enemies and before the second pass.**
   `SnapshotBuilder.Build` already ends with `_enemies.CopyInto(snapshot)` then `WriteSenses`;
   `_minions.CopyInto(snapshot)` goes between them. After the enemies because the enemy count is what
   `LineOfSightSense`'s per-frame budget is sized from and a Wight must not inflate it; before
   `WriteSenses` because that method walks `snapshot.EnemyCount` and would otherwise run on a count
   this line had not finished settling. The builder owns the frame's single `Clear` and this adds no
   second one.
5. **[CH §8 q1] A Wight does not count against GD §11.1's enemy cap, and its own cap is eight.**
   The question is *"rendering says yes, fairness says no"*, and three facts settle it. **The
   snapshot already separates them** — M5-04a gave `WorldSnapshot` a `Minions` array with its own
   capacity, so a Wight cannot displace an enemy from the census even by accident. **The director
   spends a threat budget** (`ThreatBudget.Budget(stage)`) and a Wight costs none of it, so counting
   one against `ConcurrencyCurve` would make raising an army *reduce* the stage's difficulty — the
   exact inversion GD §11.2's device-independence rule exists to prevent, arriving from inside the
   game rather than from the hardware. And CH §8 q1's own leaning is *"separate pool with a tighter
   cap"*, which is what `MinionSystem.MaxConcurrent` = 8 against a mid-tier 28 already is.
   **What is refused here is the rest of that leaning:** *"low-tier devices get fewer Wights plus a
   compensating minion damage buff"* needs a device tier, and there is no tiering in this build
   (M8-03 owns it) — a buff keyed to a tier nothing detects is a number nothing reads. **What is
   carried instead is the arithmetic**: at the mid-tier cap this build can now put **28 enemies + 8
   Wights + the player = 37 bodies** in one arena against GD §11.3's draw-call ceiling, and whether
   that holds is a phone's answer, on [ledger row 3](../ROADMAP.md#carry-forward-into-m5).
6. **`RunTicker.ApplyMinionMoves` sits immediately after `ApplyEnemyMoves` and above
   `Physics.SyncTransforms()`.** *Beside the enemies* because both are bodies core decided a velocity
   for this tick and both must step on `snapshot.Dt` rather than `Time.deltaTime` — the whole of
   M1-18's argument, and the reason `ApplyEnemyMoves` is where it is. *Above the flush* because the
   flush is what makes *"everything has finished moving"* true of the physics scene and not only of
   the call order (M2-15a): nothing sweeps a Wight today, and a body written after the flush would be
   the one exception nobody remembered on the day something does.
7. **`MinionMove` is a second list on the buffer, never a shared one with `EnemyMove`.** The two
   structs are identical and the two id spaces are not: `RunTicker.ApplyEnemyMoves` resolves an id
   through `EnemyViews.TryGet`, so a minion id arriving in that list would either find no body and be
   skipped in silence, or — worse and just as likely, since both registries number from 1 — find an
   *enemy* with the same number and walk it. M5-04a rule 4 argued this from the port's side; this is
   the same argument standing at the reader.
8. **A Wight is cyan, and cyan is the player's, and that collision is the point.** GD §16.4 reserves
   `#22D3EE` for *"the player. Projectiles, dash trail, safe things"*, and CH §3.2's watch item asks
   for exactly that: *"Wights must be unmistakable from enemies at phone scale. Cyan-tinted, per the
   colour language."* So the tint is `Palette.Player` and the language is honoured. **Hue therefore
   cannot also say which cyan thing is the player**, and the separation is size: the Wight prefab is
   the shared body at **0.7** of its scale — ours, chosen so a Wight reads as smaller than every
   archetype including the Husk rather than as a particular one. Whether *smaller and the same cyan*
   is enough at six inches is a device question and goes on [row 3](../ROADMAP.md#carry-forward-into-m5)
   as CH §3.2's own watch item, worded as the class failing rather than as a tint being wrong.
9. **[Ledger row 4] The fixture is opened because the frame grew a step, and the instrumentation
   goes in while it is open.** `FrameOrderTests` builds a `RunTicker` by hand, so the new
   constructor argument forces this file whatever else happens. The asserted order gains a row:
   a `MinionMove` reaches a minion body and an `EnemyMove` does not, on the same frame, which is
   rule 7 proved against real components rather than argued.
10. **[Ledger row 4] On an empty cone report, the same query is re-issued in the same frame, and the
    message says which of two things happened.** `Ticker_RunsTheStepsInOrder`'s assertion 5 —
    *"the cone found nothing where the body had just walked to"* — fails about one run in ten, and
    M4-07's experiment retired the only hypothesis left. **This is one field and one `if`:** the
    fixture's `cone` local becomes a field, `RecordingCore` keeps the last `ConeHitIntent` it wrote,
    and the assertion re-runs `ConeOverlapQuery.Query` against it before it fails. **If the second
    query finds the body, the transform sync at the seam was late; if it does not, the wedge was in
    the wrong place** — and the failure message says so by name instead of restating what was
    expected. Nothing in `RunTicker` changes and no re-run count is added: thirteen tasks of tallies
    have diagnosed nothing, and this converts every future failure into one of two named answers.
    **It is not a fix and must not be reported as one.** If the row goes green for a whole milestone
    that is luck, not evidence.
11. **Nothing here allocates on the frame path.** One pool of eight built at construction, a
    dictionary keyed by `int`, the concrete dictionary's struct value enumerator in `CopyInto`
    (`EnemyViews`' rule — `IEnumerable<T>` would box it every frame), a `for` over the buffer's
    `IReadOnlyList` rather than a `foreach`, and struct intents taken by `in`.
12. **No run this build plays raises a Wight, and that is stated rather than discovered.** The
    Gravecaller is unreachable until [M5-07](M5-07-class-select-screen.md) — the menu writes
    `Characters[0]` and `RunTicker.FallbackCharacterId` does the same — so in every run that can be
    started the minion pool prewarms eight bodies, deactivates them, and nothing ever rents one.
    **The first time anyone sees a Wight is M5-07**, two tasks after the body is built, and the
    intermediate state is honest rather than broken.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Minions_SpawnRentsAndBinds` | a `MinionSpawned` at (4, 0, 4) / published / one standing body, at that point, bound to the event's id |
| `Minions_DespawnReturnsToThePool` | one standing / `MinionDespawned` / `Count` 0, `CountInactive` back up, and the instance is the same object on the next rental |
| `Minions_ADeathReturnsItToo` | one standing / `MinionDied` / returned exactly as a despawn returns it — rule 2, so a keystone that listens to one event cannot leave a body standing |
| `Minions_AnUnknownIdIsNotAnError` | a despawn for an id nothing bound / published / no throw, no log — `EnemyViews.OnDespawned`'s rule |
| `Minions_PrewarmAllocatesNothingLater` | prewarm 8 / eight rentals and eight releases, 1 000 cycles / `AllocationAssert.None` |
| `Minions_CopyIntoWritesEveryFieldOfItsSlot` | a slot last used by a Wight at (9, 0, 9) with a non-zero path / a new Wight at (1, 0, 1) / `Position`, `Velocity` and a **zeroed** `PathDirectionToPlayer` — rule 3, the stale-slot rule |
| `Minions_AreNotInTheEnemySlots` | three enemies and two Wights / `Build` / `EnemyCount` is 3 and the two Wights are in the minion slots — rule 4 |
| `Minions_DoNotInflateTheSightBudget` | the same frame / — / `LineOfSightSense` was asked about three ids, not five — rule 4's ordering, asserted rather than assumed |
| `Minions_HaveNoPathSearch` | a `NavPathSense` that counts calls / a frame with two Wights standing / zero searches for a minion id — rule 3 |
| `Minions_AreNotInTheColliderIndex` | a Wight and an enemy standing / `EnemyViews.TryGetId` for the Wight's collider / false — **rule 1, the row that proves the swing cannot reach one** |
| `Minions_AreNotRentedFromTheEnemyPool` | eight Wights standing / — / `EnemyViews.Count` unchanged, and the two pools hold different instances |
| `Minions_CapIsEightAndTheEnemyCapIsUntouched` | `MinionSystem.MaxConcurrent` Wights and a full mid-tier arena / — / 8 and 28 stand together and neither displaces the other — rule 5 |
| `Minions_AreTintedThePlayersCyan` | `Wight.prefab` / loaded / its tint is `Palette.Player` and its scale is below every shipped `EnemyLook.BodyScale` — rule 8, pinned so a recolour is deliberate |
| `Minions_AreLinkedToAMonoScript` | `Wight.prefab` / loaded / `MinionView` resolves — [Traps §5](../../Traps.md), the row every authored prefab owes |
| `Buffer_MinionMovesAreASecondList` | one `EnemyMove` and one `MinionMove` in a tick / — / each list holds exactly its own, and `Clear` empties both — rule 7 |
| `Ticker_AMinionMoveWalksAMinion` | a Wight and an enemy with the same id / one frame / the Wight moved by the minion intent and the enemy by the enemy intent, neither by the other's — **rule 7 against real components** |
| `Ticker_MinionsMoveBeforeTheFlush` | a Wight walking into a cone / one frame / the sweep finds it — rule 6 |
| `Ticker_RunsTheStepsInOrder` | *(existing, extended)* the six assertions, plus the minion step / — / unchanged, and assertion 5 now re-issues the query on an empty report and names which failure it was — rules 9, 10 |
| `Ticker_AnEmptyConeReportIsDiagnosed` | a cone deliberately placed where the body is not / — / the re-issued query also finds nothing, and the message says *"the wedge was in the wrong place"* — [ledger row 4](../ROADMAP.md#carry-forward-into-m5), the control that proves the instrument reads both ways |
| `Views_DisposeDestroysEveryBody` | eight standing, four pooled / `Dispose` / every instance destroyed and the subscriptions dropped — `EnemyViews.Dispose`'s rule |

**Guard rows are implied, not listed:** a null `IObjectResolver`, prefab or `DomainEventHub` to the
census; a negative `prewarm`; a destroyed `parent` normalised to the scene root; and a non-finite
`dt` to `Apply`.

## Manual verification (Editor / device)

1. **[Editor]** Open `Run.unity` and press Play. *Expected: eight inactive bodies under the minion
   parent from the first frame, none of them ever activated, and the run identical to `m5-04b` in
   every respect — rule 12.*
2. **[Editor]** Select one of the pooled bodies. *Expected: cyan, and visibly smaller than a Husk
   (rule 8). This is the only look at a Wight anybody gets before M5-07.*
3. **[device]** CH §3.2's watch item, reworded as the class failing: **at phone scale, in a fight,
   can the player tell their army from the swarm?** [Ledger row 3](../ROADMAP.md#carry-forward-into-m5).
4. **[device]** 37 bodies in one arena against GD §11.3's draw-call ceiling (rule 5).

## Out of scope

- **The decoy's view, the Hud font sizes and the material keyword.** [M5-05b](M5-05b-decoy-view-and-the-look.md).
- **A health bar, a hit flash or a dissolve on a Wight.** `EnemyHealthBar` and `EnemyHitFeedback`
  are `EnemyView`'s children and nothing damages a Wight in M5 (M5-04a rule 9). A body that cannot be
  hurt does not need a bar, and building one would be a readout for a number that never moves.
- **Enemies retargeting onto a Wight, or a Wight in the cone query's answer.** Rule 1 keeps it out of
  the index; M5-04a rule 9 keeps it out of the fight.
- **Device tiering, and CH §8 q1's compensating damage buff.** Rule 5. M8-03.
- **Fixing `FrameOrderTests`.** Rule 10 adds an instrument. There is nothing known to be wrong, so
  there is nothing to fix, and a task that could only report is what M4-00b already refused.
- **Animation.** `PlayerAnimatorView`'s shape on a minion is M7's art pass; a Wight slides, as every
  enemy did until M2-06.

## As built

_Filled at merge, 6 000 bytes or fewer, measured._

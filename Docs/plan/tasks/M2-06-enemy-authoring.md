# M2-06 — The Spitter and the Bloater as authored data, and three archetypes you can tell apart

**Size:** M · **Depends on:** M2-04 (`ThreatCost`), M2-02 (`Descent.asset`) · **Branch:** `m2-06-enemy-authoring`
**Design refs:** GD §8.1 (archetypes), §8.2 (schedule), §11.3 (instancing, one shared material), §12.3, §12.4 (the one-shot rule); AR §10.1, §18.4; ADR-0006, ADR-0010 · **Ledger rows:** none — the [parking lot](../ROADMAP.md#parking-lot)'s KayKit-roster line is answered in rule 12

## Goal

Three archetypes exist as content — Husk, Spitter, Bloater — each with the numbers its behaviour will need and a look that says which one is walking at you, before any of that behaviour is written.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ProjectileSpec.cs` | Core | `ProjectileSpec` + `ExplosionSpec` — the two optional blocks, grouped (`EnemyEvents.cs`' precedent) |
| `Game/Authoring/EnemyLook.cs` | Game | `EnemyLook` + `EnemyLookBook`: id → tint and scale, built once at boot |
| `Tests/Core/Content/ProjectileSpecTests.cs` | Tests.Core | Both blocks, and the kind-block agreement rule |
| `Tests/Game/Authoring/EnemyLookTests.cs` | Tests.Game | The book, and that the three assets say what GD §8.1 says |
| *small edits* | | `EnemySpec` + `AggroRange`, `Projectile`, `Explosion` and two `EnemyBehaviourKind` values; `EnemyDefinition` + the same, + `_tint` and `_bodyScale`; `Data/Enemies/Spitter.asset` and `Bloater.asset` (new assets, listed not counted); `Descent.asset` + two roster rows (M2-02 rule 10); `ChaserBehaviour`'s `AggroRange` const becomes a spec read; `EnemyViews.OnSpawned` applies the look; `EnemyHitFeedback` + `SetArchetypeLook`; `EnemyView.OnDespawn` restores it; `BootInstaller` builds the book; `RunInstaller`/`RunScope` hand it to `EnemyViews` |
| *ripple* | | every `new EnemySpec(...)` in Tests.Core and Tests.Game — `aggroRange` is a required parameter, so the compiler enumerates them; `EnemyDefinitionTests` and `ContentTests` gain the new fields; `ChaserBehaviourTests` fixtures set an aggro range instead of relying on the const |

Only these files change. Anything else is a deviation: say so in *As built*.

**If `EnemyHitFeedback` turns out to need restructuring rather than one method — if the archetype tint cannot be layered under the flash and the dissolve without rewriting how `_liveColour` is captured — split into `M2-06a` (the data) and `M2-06b` (the look) before continuing, never after.**

## Public API

```csharp
namespace Soulvail.Core.Content;

/// What an archetype throws. Present on an archetype that fires and absent on one that does not
/// (rule 3). It carries no damage: the shot deals the agent's ContactDamage stat, so M2-03's
/// depth scaling reaches it without a second mechanism (rule 5).
public sealed class ProjectileSpec
{
    public ProjectileSpec(float standoffRange, float speed, float radius);

    public float StandoffRange { get; }   // metres it stops at and fires from — 14 for the Spitter
    public float Speed { get; }           // m/s of flight; with StandoffRange it decides the dodge window
    public float Radius { get; }          // metres from the impact point that still count as a hit
}

/// What an archetype does when it goes off. Absent on one that does not.
public sealed class ExplosionSpec
{
    public ExplosionSpec(float radius);

    public float Radius { get; }          // metres — 3 for the Bloater (GD §8.1)
}

// EnemyBehaviourKind (added)
Spitter,   // keeps its distance and throws — M2-07b. Nothing authors it until then (rule 11).
Bloater,   // waddles in, lights a fuse, goes off — M2-08. Same.

// EnemySpec (added)
public float AggroRange { get; }          // metres within which it notices the player. 30 for all three.
public ProjectileSpec Projectile { get; } // null on an archetype that throws nothing
public ExplosionSpec Explosion { get; }   // null on an archetype that does not explode
```

```csharp
namespace Soulvail.Game.Authoring
{
    /// How one archetype is told apart on screen. Game-side only: core has no opinion about colour.
    public readonly struct EnemyLook
    {
        public EnemyLook(Color tint, float bodyScale);

        public Color Tint { get; }
        public float BodyScale { get; }

        /// Grey, scale 1 — what an id with no authored look gets (rule 9).
        public static EnemyLook Default { get; }
    }

    /// Archetype id → its look. Built once at boot from the same definitions the catalog reads.
    public sealed class EnemyLookBook
    {
        public EnemyLookBook(IReadOnlyDictionary<ContentId, EnemyLook> looks);

        /// The archetype's look, or EnemyLook.Default for an id nobody authored one for.
        public EnemyLook For(ContentId specId);
    }
}
```

```csharp
// EnemyHitFeedback (added)
/// Re-bases the colour and scale this body returns to, so the flash, the telegraph swell and the
/// dissolve all play over the archetype's look rather than over the prefab's. Called by
/// `EnemyViews` on the rental, before the body is activated.
public void SetArchetypeLook(Color tint, float bodyScale);
```

## Behaviour

**The two blocks**

1. `ProjectileSpec` and `ExplosionSpec` are separate objects on `EnemySpec` rather than eight more constructor parameters, for the reason `CharacterSpec` carries an optional `ShieldSpec`: a null block says *this archetype does not do this*, where a zeroed field says nothing at all and has to be read against the behaviour kind to be understood. `EnemySpec`'s own remarks said an enemy had no optional block; as of this task it has two, and that line is corrected with them.
2. Every field of both is a finite number greater than zero — `!(value > 0f)`, never `value <= 0f` (AR §18.3). A zero `Speed` is a shot that never arrives, a zero `Radius` is one that can only hit a mathematical point, and a zero `StandoffRange` is an archer that walks into melee.
3. **A kind requires its block; a block does not require its kind.** `Behaviour == Spitter` with a null `Projectile` is refused, and so is `Behaviour == Bloater` with a null `Explosion`. The reverse is deliberately legal, because that is exactly what this task ships: `Spitter.asset` carries a projectile block and is authored `Static` until M2-07b can run it (rule 11).
4. **`AggroRange` moves onto `EnemySpec`** from `ChaserBehaviour`'s `public const AggroRange = 30f`, which predicted the move in its own remarks: *"the day an archetype wants to be genuinely unaware until approached, this moves onto the spec with it."* Two behaviours now need it, and a Spitter reaching into `ChaserBehaviour` for a constant would be the wrong dependency in the wrong direction. All three archetypes author 30, so nothing changes on screen.
5. **Neither block carries damage.** A shot and a blast both deal the agent's `ContactDamage` — the stat M2-03 rule 7 put on `EnemyAgent` — so depth scaling reaches an enemy's ranged attack for free and an Elite affix (M7-02) will reach it the same way. A `damage` field here would be a second number the depth curve does not know about, and the failure would be silent: Bloaters that stop mattering at stage 20.

**The numbers, and the guardrail they are checked against**

6. `Spitter.asset` ↔ `enemy.spitter`: HP **28**, threat cost **7**, target priority **3** (all GD §8.1); move speed 2.8, contact damage **12**, reach 1.2, windup 0.7, recover 0.9, aggro 30. Projectile: standoff **14** (GD §8.1), speed 12, radius 1.6. Reach is unused by a Spitter and authored anyway because `EnemySpec` requires it positive — noted rather than hidden.
7. `Bloater.asset` ↔ `enemy.bloater`: HP **24**, threat cost **8**, target priority **2** (GD §8.1); move speed 2.2 — it *waddles* — contact damage **15**, reach 2.0 (where the fuse starts, M2-08), windup 0.8 (the fuse), recover 0, aggro 30. Explosion radius **3** (GD §8.1).
8. **Both damage numbers are chosen against GD §12.4's one-shot rule, and the binding constraint is M2-03's damage cap.** No non-boss attack may exceed 35 % of the player's max HP *at any depth*; the Oathbound has 140, so the ceiling is **49**, and `d(n)` caps at 3.0× — which makes the real ceiling on any authored `contactDamage` **16.3**. Husk 8 → 24 at depth (17.1 %); Spitter 12 → 36 (25.7 %); Bloater 15 → **45 (32.1 %)**, the biggest single hit in M2 and still inside the rule. Anything above 16.3 is a guardrail violation that only shows up forty stages into a run.
   *Flagged for the owner, not fixed here:* at the Censer's 13 damage a 24 HP Bloater dies in **2 hits**, one below GD §12.4's TTK band of 3–5. Either the 24 or the band's lower bound wants a one-line ruling; M2-15 is where acceptance would otherwise fail on it.

**Telling them apart**

9. **The look is a tint and a scale on the one shared body**, not a prefab per archetype. This is what GD §11.3 already asks for — *"one shared material and texture atlas across all enemy variants, with colour/variation driven by per-instance properties"* — so it costs no draw call and no new art, and it is what makes a fat rust-coloured Bloater legible as *do not melee this* next to a grey Husk. **Ruled by the owner at M2-00c**, against importing the KayKit skeletons now — the argument, and what promotes them, is the [parking lot](../ROADMAP.md#parking-lot) line and the first bullet of *Out of scope*. An unauthored id gets `EnemyLook.Default` rather than throwing: a missing colour is not worth ending a run over.
10. **`EnemyHitFeedback` owns the reset and therefore owns the look.** It captures `_liveColour` and `_liveScale` in `Awake`, and every effect it plays — the hit flash, the telegraph swell, the dissolve's stretch — is written relative to those two fields. `SetArchetypeLook` re-bases them, `ResetVisuals` restores them, and `EnemyView.OnDespawn` calls it as it already does. **This is AR §18.4's pooled-reset invariant gaining two more things to forget**: a tint left behind produces a Husk that spawns Bloater-red, which reads as a rendering bug three systems from its cause. The invariant's list grows with this task. **Only half of it is provable here**: `Awake` does not run on a body instantiated in EditMode, so the test below asserts what `SetArchetypeLook` and `ResetVisuals` do to each other, and **the full rent → kill → re-rent assertion is [M2-09](M2-09-projectile-views-and-pooling.md) rule 10's**, which is ledger row 8 and needs PlayMode by construction.

**What is deliberately not finished here**

11. **The two new `EnemyBehaviourKind` values are added, and nothing authors them.** Both assets ship `_behaviour: Static`; M2-07b flips `Spitter.asset` in the PR that can run a Spitter, and M2-08 flips `Bloater.asset`. `EnemySystem.Tick`'s dispatch is **not touched** — an unhandled kind still throws there, which is `EnemyBehaviourKind`'s own documented rule (*the loud place for an unrecognised kind is the dispatch*), and nothing can reach it because nothing authors the kind. The alternative, a `case Spitter: break;` placeholder, would make a Spitter that ignores the player *silent* instead of loud, and silence is the failure mode this project keeps paying for.
12. **`Descent.asset` gains its Spitter and Bloater rows** (stages 2 and 4, GD §8.2) — M2-02 rule 10 named this task as the one that adds them, and rule 8 of that spec's roster validation is what would otherwise refuse the mode. The consequence is visible and intended: for the length of M2-07 and M2-08, a stage-2 run spawns pale capsules that stand still. Manual step 4 is where that is checked to be *inertness* rather than breakage.

## Tests

| Test | Given / When / Then |
|---|---|
| `Projectile_RecordsAllThree` | 14, 12, 1.6 / ctor / every property reads back |
| `Projectile_NonPositiveField_Throws` | speed 0, then radius −1, then standoff NaN / ctor / all throw |
| `Explosion_RadiusMustBePositive` | 0, −3, NaN, ∞ / ctor / all throw |
| `Spec_SpitterWithoutProjectile_Throws` | kind `Spitter`, `Projectile` null / `new EnemySpec` / throws (rule 3) |
| `Spec_BloaterWithoutExplosion_Throws` | kind `Bloater`, `Explosion` null / `new EnemySpec` / throws |
| `Spec_StaticWithProjectile_IsLegal` | kind `Static`, projectile block present / `new EnemySpec` / no throw — this is what `Spitter.asset` is until M2-07b (rule 3) |
| `Spec_AggroRangeMustBePositive` | 0, −1, NaN / ctor / all throw |
| `Spec_BlocksAreNullByDefaultOnAHusk` | `Husk.asset` / `ToSpec()` / both blocks null |
| `Chaser_UsesSpecAggroRange` | a spec with aggro 5, player 6 m away / `Tick` / stays `Idle`; player 4 m away / `Tick` / `Chase` (rule 4) |
| `Look_RecordsTintAndScale` | red, 1.35 / ctor / both read back |
| `Look_UnknownId_IsDefault` | an empty book / `For(enemy.ghost)` / `EnemyLook.Default`, no throw (rule 9) |
| `Look_DuplicateId_Throws` | two entries for `enemy.husk` / ctor / throws — the catalog's own rule |
| `Definition_ToSpec_CarriesBothBlocks` | an authored definition with both / `ToSpec()` / both survive with every field |
| `Definition_ToSpec_OmitsAnUnauthoredBlock` | neither block filled in / `ToSpec()` / both null rather than zeroed objects |
| `Spitter_MatchesDesign` | `Data/Enemies/Spitter.asset` / load / id `enemy.spitter`, HP 28, cost 7, priority 3, standoff 14 (GD §8.1) |
| `Bloater_MatchesDesign` | `Data/Enemies/Bloater.asset` / load / id `enemy.bloater`, HP 24, cost 8, priority 2, explosion radius 3 |
| `Assets_AuthoredStaticUntilTheirBehaviourExists` | both assets / load / `Behaviour == Static` (rule 11) |
| `Look_SurvivesSetAndRestore` | a body given `SetArchetypeLook(red, 1.35)`, then flashed and swelled / `ResetVisuals` / back to red and 1.35, not to the prefab's grey and 1.0 (rule 10; the full-life half is M2-09's) |
| `Assets_ObeyTheOneShotRule` | all three assets / `contactDamage × 3.0` (M2-03's `d` cap) / each ≤ 49 — 35 % of the Oathbound's 140. **The damage read is `contactDamage`, because neither block carries one** (rules 5, 8; GD §12.4) |
| `Descent_RostersAllThree` | `Descent.asset` / load / Husk 1, Spitter 2, Bloater 4 (rule 12, GD §8.2) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Play the Run scene: the Husks look exactly as they did before. The look system is a no-op for an archetype whose tint is the prefab's own, which is what rule 10 has to be true for.
2. **[Editor]** Drop a Spitter and a Bloater into the scene's dressed spawn plan and Play: one pale and small, one rust and fat, both obviously not Husks at a glance from the play camera — which is the whole claim of rule 9. Kill each: the flash, the swell and the dissolve all play over the archetype's colour rather than reverting to grey mid-effect.
3. **[Editor]** Let a Bloater be killed, wait for its body to be recycled as a Husk, and check the Husk is grey and normal-sized. A tint that survives a rental is rule 10's failure and it looks like a rendering bug.
4. **[Editor]** Start a run at stage 2. Spitters spawn and **stand still** — inert, not throwing, nothing in the Console. That is rule 11 being visible rather than broken.

## Out of scope

- **Any behaviour.** The Spitter throws in M2-07b and the Bloater goes off in M2-08. This task authors what they will read.
- **Enemy models and animation, and therefore a prefab and a `ViewPool` per archetype.** `Skeleton_Minion` → Husk and `Skeleton_Mage` → Spitter are both a good fit and both already sitting in `ThirdParty/KayKit/Skeletons/`, but **the pack has no Bloater**, and three bodies means three prefabs, three animator controllers and a pool each — an M2-art-sized task, which this is not. [Parking lot](../ROADMAP.md#parking-lot), promoted when the owner brings enemy art in the way M2-art was brought in. One shared tinted body is GD §11.3's own plan in the meantime.
- **Elites** (GD §8.3): `IsElite` already exists and stays false on all three. M7-02.
- **The TTK ruling** in rule 8 — flagged, owned by the owner, checked at M2-15.
- **`ExplosionSpec` on anything but the Bloater**, and a `TagSet` — the first has no second archetype and the second has no reader (M7-02).

## As built

Built as specified, with **five deviations**. The split rule did not fire: `EnemyHitFeedback` needed one method and one new field, and `_liveColour`'s capture in `Awake` is untouched.

**1. `BootScope.prefab` was edited, and the Files table did not list it. This is the one that changes a decision.**
`BootInstaller`'s enemy list is a serialized array on the boot prefab, so a definition that is not in it never reaches the `ContentCatalog` — and `Descent.asset`'s new roster rows then name `enemy.spitter` and `enemy.bloater`, which `RunSession.Start` resolves against the catalog before it announces anything (ledger row 3's guard, M2-02). Authoring the two assets without adding them to the boot list does not produce a run with Husks only; it produces **a game that refuses to start**. So the assets, the roster rows and the boot list are one change or none. Edited through the Editor with `SerializedObject` + `SaveAsPrefabAsset` rather than by hand, and the diff is the two added references and nothing else — the fake-null and re-serialisation traps of Traps §5 are exactly why. **Anything that authors a new content asset owes the same third edit**, which the Files table of the next such task should say out loud.

**2. `EnemyView.Feedback` became public.**
`EnemyViews.OnSpawned` has to reach the component that owns the look, and this cached, flag-guarded lookup is the only one in the project that does not cost a `GetComponent` per spawn. Made a public read — symmetrical with `Body`, which the census already reads the same way — rather than adding a forwarding method to `EnemyView` for every effect anyone adds later.

**3. `EnemyLook.cs` uses a file-scoped namespace where the spec's Public API block shows a block one.**
Neither type in it derives from `UnityEngine.Object`, so Traps §5's importer rule does not apply and the project convention for pure C# is file-scoped. The spec's snippet was formatting, not a ruling.

**4. `SetArchetypeLook` builds the `MaterialPropertyBlock` itself when `Awake` has not.**
Without it the colour half of rule 10 is unobservable in EditMode — `Awake` never runs there, so the block is null and nothing is ever written to a renderer. One `??=` guarded by a Unity null check on `_renderer`; every hot path is untouched, `Awake` still builds the block at load, and the result is that `Look_SurvivesSetAndRestore` asserts the tint as the spec's row asks rather than only the scale. `ResetVisuals` also restores the scale **ahead of** its property-block guard, because a body can now be rescaled before `Awake` — guarded by `_liveScale != Vector3.zero`, which is true only of a fixture that has neither woken up nor been given a look.

**5. `EnemyHitFeedback` gained a second field, `_prefabScale`, beside `_liveScale`.**
`BodyScale` is a multiple of the prefab's own scale, so multiplying `_liveScale` would compound across rentals — a Bloater at 1.35, then 1.82, then 2.46. The new field is the base the multiply is applied against, initialised to `Vector3.one` so it is readable in EditMode and overwritten by `Awake` with the prefab's real scale. `Look_DoesNotCompoundAcrossRentals` is the row that holds it.

**Three things the spec left to the implementation**

- **`EnemyDefinition.ToLook()` is the second conversion**, beside `ToSpec()`. A `Color` cannot cross into `Soulvail.Core` (AR §2), so the tint and the scale leave the asset by their own door and `BootInstaller` pairs them with the spec ids it has just built.
- **`EnemyLook.Default` is `M_BoneGrey`'s own base colour**, not an arbitrary grey. That is what makes manual step 1 true — the look system is a genuine no-op for the Husk — and it is asserted by `Husk_TintIsTheSharedMaterialsOwn`, which is also what will say so if the material is ever recoloured.
- **`ContentTests` needed no change**, despite the ripple row naming it: it builds no `EnemySpec`. `EnemyDefinitionTests` needed none either — its Husk row asserts no new field, and `Husk_EveryYamlKeyBindsToAField` passed unmodified against the hand-written YAML keys, which is what proves all six of them bind.

**Two notes that are not deviations**

- **The invariant list in AR §18.4 grew**, as rule 10 said it would. That is an edit outside the Files table and is deliberate: the rule is now two-sided — `ResetVisuals` restores the *rented* look, and `EnemyViews` re-applies unconditionally on the next rental — and a reader who knows only the first half would conclude a Husk can inherit a Bloater's tint.
- **`ModeDefinitionTests.Descent_MatchesDesign` failed on the first run and was updated**, which is M2-02 rule 10 working exactly as written: that row asserted a one-entry roster *until M2-06 authors the Spitter and the Bloater*. It now asserts all three of GD §8.2's rows by index, because `ModeSpec.RosterFor` answers in roster order.

**Flagged for the owner, as rule 8 says, and not fixed here:** at the Censer's 13 damage a 24 HP Bloater dies in **2 hits**, one below GD §12.4's TTK band of 3–5. Either the 24 or the band's lower bound wants a one-line ruling; M2-15 is where acceptance fails on it otherwise.

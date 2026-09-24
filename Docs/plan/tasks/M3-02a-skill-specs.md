# M3-02a — `SkillSpec`, `TriggerSpec`, `SkillTreeSpec`: the tree as data

**Size:** M · **Depends on:** M3-05 (`IEffect` is what a node carries) · **Branch:** `m3-02a-skill-specs`
**Design refs:** CH §4, §4.1, §4.2, §4.3, §5, §5.1, §8; GD §6.3, §13.1; CC §6.1, §6.4; AR §5, §9, §10.1, §11.3, §11.5, §18.3; ADR-0005, ADR-0006, ADR-0009, ADR-0010, ADR-0012 · **Ledger rows:** none — row 2's fields are M3-01b's, and this task writes nothing a save carries

## Goal

A node, an active's cooldown and trigger, and a class's tree all exist as immutable records the catalog resolves by id — with the tree's shape as data, so the level-up offer is a real draw and a partial v1 tree is legal content.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/SkillSpec.cs` | Core | `SkillKind`, `ActiveSpec`, `SkillSpec` — grouped, `ModeSpec.cs`' precedent |
| `Core/Content/TriggerSpec.cs` | Core | `TriggerField`, `TriggerComparison`, `TriggerClause`, `TriggerSpec` and its `IsMet` |
| `Core/Content/SkillTreeSpec.cs` | Core | `SkillBranchSpec`, `SkillTreeSpec` |
| `Tests/Core/Content/SkillSpecTests.cs` | Tests.Core | skill and trigger rows together — the trigger is the active's, and the active is the skill's |
| `Tests/Core/Content/SkillTreeSpecTests.cs` | Tests.Core | shape, guards, locate |
| *small edits* | | `ContentCatalog` + `skills` and `trees` (optional lists, `enemies`' shape), `Skill` / `TryGetSkill`, `Tree` / `TryGetTree`, `TryGetTreeFor(characterId)`; `ContentTests` mirror rows |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// CH §4's four kinds. Closed: content is named by ContentId, this only says what taking it does.
public enum SkillKind { Passive, Active, Upgrade, Keystone }

/// The blackboard fields a CC §6.4 condition may read. Closed: these are code, not content.
public enum TriggerField
{
    HpFraction, ShieldFraction, EnemiesWithin6m, EnemiesWithin8m, EnemiesInAcquireRange,
    IncomingProjectiles, Veilrot, StationaryTime, FocusRampLevel,
}

public enum TriggerComparison { Below, AtLeast }     // `<` and `>=`

public readonly struct TriggerClause
{
    public TriggerClause(TriggerField field, TriggerComparison comparison, float threshold);   // threshold finite

    public TriggerField Field { get; }
    public TriggerComparison Comparison { get; }
    public float Threshold { get; }

    public bool IsMet(CombatBlackboard blackboard);   // allocates nothing
}

/// CC §6.4's authored condition: one or two clauses, all of which must hold.
public sealed class TriggerSpec
{
    public const int MaxClauses = 2;

    public TriggerSpec(IReadOnlyList<TriggerClause> clauses);   // 1..2, copied

    public IReadOnlyList<TriggerClause> Clauses { get; }
    public bool IsMet(CombatBlackboard blackboard);              // allocates nothing
}

/// What an Active is when it fires. Present only on an Active (rule 1).
public sealed class ActiveSpec
{
    public ActiveSpec(float cooldown, TriggerSpec trigger, IReadOnlyList<IEffect> onCast);

    public float Cooldown { get; }                    // > 0, finite; seeds M3-06's Stat
    public TriggerSpec Trigger { get; }
    public IReadOnlyList<IEffect> OnCast { get; }     // >= 1, copied
}

public sealed class SkillSpec
{
    public SkillSpec(
        ContentId id,
        LocKey nameKey,
        LocKey descriptionKey,
        SkillKind kind,
        IReadOnlyList<IEffect> effects,        // applied once, on take
        ActiveSpec active = null,              // required iff kind == Active
        ContentId parentId = default);         // required iff kind == Upgrade

    public ContentId Id { get; }
    public LocKey NameKey { get; }
    public LocKey DescriptionKey { get; }
    public SkillKind Kind { get; }
    public IReadOnlyList<IEffect> Effects { get; }
    public ActiveSpec Active { get; }
    public ContentId ParentId { get; }
}

/// One of a tree's three branches: tiers of node ids, tier 1 first. A tier may hold several.
public sealed class SkillBranchSpec
{
    public SkillBranchSpec(LocKey nameKey, IReadOnlyList<IReadOnlyList<ContentId>> tiers);   // 1..MaxTiers, each >= 1

    public LocKey NameKey { get; }
    public int TierCount { get; }
    public int NodeCount { get; }
    public IReadOnlyList<ContentId> Tier(int tier);   // 1-based
}

public sealed class SkillTreeSpec
{
    public const int BranchCount = 3;   // CH §5: identical skeleton for every class
    public const int MaxTiers = 8;      // CH §5's ceiling

    public SkillTreeSpec(ContentId id, ContentId characterId, IReadOnlyList<SkillBranchSpec> branches);   // exactly 3

    public ContentId Id { get; }
    public ContentId CharacterId { get; }
    public IReadOnlyList<SkillBranchSpec> Branches { get; }
    public int NodeCount { get; }

    public bool TryLocate(ContentId skillId, out int branch, out int tier);   // 0-based branch, 1-based tier
}

// ContentCatalog (added)
public IReadOnlyList<SkillSpec> Skills { get; }
public IReadOnlyList<SkillTreeSpec> Trees { get; }
public SkillSpec Skill(ContentId id);                         // KeyNotFoundException, naming the id
public bool TryGetSkill(ContentId id, out SkillSpec spec);
public SkillTreeSpec Tree(ContentId id);
public bool TryGetTree(ContentId id, out SkillTreeSpec spec);
public bool TryGetTreeFor(ContentId characterId, out SkillTreeSpec spec);
```

## Behaviour

**Skills**

1. **A kind requires its block, and a block requires its kind — both directions, unlike `EnemySpec`.** An Active without an `ActiveSpec` is a skill that cannot fire; an `ActiveSpec` on a Passive is a cooldown nothing will ever run. M2-06 let a block precede its kind so numbers could be authored before the code that runs them existed; here the runner (M3-06) exists before the first active (M3-11), and M3-11 ships Consecrate's block and its kind in one PR, so there is nothing to stage and the looser rule would only admit mistakes. The same in both directions for `ParentId`: required and non-default on an Upgrade, default on everything else.
2. **A node that does nothing is refused.** `Effects` is copied, admits no null entry, and must hold at least one for a Passive, an Upgrade or a Keystone — GD §13.1's *"every node must change how you play"* has a weaker cousin the constructor can enforce, which is that every node must at least do *something*. An Active may take with no effects, because its power is on cast, and `OnCast` must then hold at least one: an active that casts nothing goes on cooldown and does nothing, which is the silence M2-06 rule 11 refuses.
3. **Both `LocKey`s are required.** GD §13.1's two-second rule is about the *text*, which is M6-10's table; the key exists from the first node (ADR-0012), and `default(LocKey)` for either is a forgotten field rather than a decision. M3-14 checks the keys resolve.
4. **`Cooldown` is finite and greater than zero** — `MovementSkillSpec`'s argument: a skill with no cooldown is not a skill. CH §4.1's 40 % floor belongs to the runner, which is the layer that knows what "too short" means; the spec carries the authored base and nothing else.

**Triggers**

5. **A trigger is one or two clauses, all of which must hold.** Two, because Rot Nova is *"Veilrot ≥ 50 and ≥ 4 enemies within 8 m"* (CH §4.2) and one clause cannot say it; not more, because a condition a designer cannot read in one line on the Skills screen (CC §6.3 writes them out in plain language) is one the player cannot predict, and predictability is what makes Auto feel deliberate rather than random. `Below` is `<`, `AtLeast` is `>=`; `threshold` is finite.
6. **`IsMet` reads the named blackboard field through a switch over `TriggerField`** — a closed enum of code fields, the `Stat.Pool` kind of switch AR §13 permits, with a loud `default`. It is ADR-0005's *"pure predicates over the `CombatBlackboard`, carried by the `SkillSpec` as data"* made authorable: a lambda cannot come out of an asset, an enum-and-threshold can. **A NaN field fails every clause** (`!(value < threshold)` spelling, AR §18.3) — a broken blackboard must not cast every skill at once. Allocation-free, because M3-06 asks it for every owned active on every tick.

**Trees**

7. **Layered branches — ruled by the owner at M3-00a.** CH §5 says eight nodes a branch and twenty-seven a class, and its gating (tier N needs N − 1 taken in the branch) would make a one-node-per-tier branch a straight chain: the offer would always be the same three heads, and §5.1's *"random from everything available"* and §8 Q3's variety rules would do nothing. So **a tier holds one or more nodes**, and the shape is data: the shipped Oathbound tree (M3-12) is **two nodes a tier for four tiers plus a keystone — nine a branch, twenty-seven a class** — which makes every sentence in CH §5–5.1 true. CH §5's *"8 (7 + 1 Keystone)"* is a slip for 9 (8 + 1) and is **flagged for the owner, not edited here** — the M2-03 precedent for a design-doc number.
8. **Exactly three branches** (CH §5: *"identical skeleton for every class, so the UI is built once"*); each branch one to eight tiers, each tier at least one id; an id appears once in the whole tree; no id is `default`. **A partial tree is legal** — three branches of four is M3-12's v1 and has no keystone — because refusing it would refuse the milestone's own content.
9. **Keystone placement is a cross-spec rule and lives elsewhere.** The tree holds ids, not kinds, so "a Keystone is the sole node of its branch's last tier" cannot be checked here without the catalog; `TreeRules` (M3-03) checks it at `Start`, before `RunStarted`, and M3-14 checks it over every authored tree. Stated so nobody adds a catalog to a spec constructor.
10. **`TryLocate` is a dictionary probe built once**, so M3-03 and M3-04 ask *"which branch, which tier"* per candidate without a walk. `NodeCount` is the sum.

**Catalog**

11. **Two more kinds through `Index`**, optional lists like `enemies` and `modes`, the same lookups and the same messages. **`TryGetTreeFor(characterId)`** is the door a run uses: a class has one tree (CH §5), so two trees naming one character is refused at construction, and a character with none is a legal catalog — it is every catalog between this task and M3-12, and M3-03 rule 10 says what a run does with it. M3-14 pins that every *shipped* character has one.

## Tests

| Test | Given / When / Then |
|---|---|
| `Skill_RecordsFields` | every argument distinct / ctor / each reads back |
| `Skill_DefaultId_Throws` · `Skill_DefaultNameKey_Throws` · `Skill_DefaultDescriptionKey_Throws` | one default / ctor / throws each (rule 3) |
| `Skill_ActiveWithoutBlock_Throws` · `Skill_BlockWithoutActive_Throws` | Active + null; Passive + block / ctor / throws each (rule 1) |
| `Skill_UpgradeWithoutParent_Throws` · `Skill_ParentOnNonUpgrade_Throws` | Upgrade + default; Passive + a parent / ctor / throws each (rule 1) |
| `Skill_PassiveWithNoEffects_Throws` · `Skill_UpgradeWithNoEffects_Throws` · `Skill_KeystoneWithNoEffects_Throws` | empty `effects` / ctor / throws (rule 2) |
| `Skill_ActiveMayTakeWithNoEffects` | Active, empty `effects`, one cast effect / ctor / no throw (rule 2) |
| `Skill_EffectsAreCopied` · `Skill_NullEffect_Throws` | — / — / `ContentCatalog`'s copy and null rows again |
| `Active_CooldownGuards` | 0, −1, NaN, ∞ / ctor / throws each (rule 4) |
| `Active_EmptyOnCast_Throws` · `Active_NullTrigger_Throws` | — / ctor / throws (rule 2) |
| `Trigger_Below` | `HpFraction Below 0.6` / blackboard at 0.59, 0.6 / true, false (rule 5) |
| `Trigger_AtLeast` | `EnemiesWithin6m AtLeast 3` / 3, 2 / true, false |
| `Trigger_TwoClausesAreAnded` | `Veilrot AtLeast 50` and `EnemiesWithin8m AtLeast 4` / (50, 4), (50, 3), (49, 4) / true, false, false |
| `Trigger_EveryFieldReads` | for each `Enum.GetValues(typeof(TriggerField))` / a blackboard with only that field set / the clause reads it — a member added without a case fails here (rule 6) |
| `Trigger_NaNFieldFails` | `HpFraction` NaN, `Below 0.6` and `AtLeast 0` / `IsMet` / false, false (rule 6) |
| `Trigger_ClauseCountGuards` | 0 clauses; 3 clauses; null / ctor / throws each (rule 5) |
| `Trigger_ThresholdGuards` | NaN, ∞ / ctor / throws |
| `Trigger_ClausesAreCopied` | — / — / the copy row |
| `Trigger_AllocatesNothing` | two clauses, warm-up / 10 000 × `IsMet` / allocated-bytes delta == 0 (rule 6) |
| `Tree_RecordsShape` | 3 branches of 2 / 2 / 2 / 2 / 1 / ctor / `NodeCount` 27, `TierCount` 5, `Tier(5).Count` 1, `Tier(1)` two ids (rule 7) |
| `Tree_TwoBranches_Throws` · `Tree_FourBranches_Throws` | — / ctor / throws (rule 8) |
| `Branch_NineTiers_Throws` · `Branch_EmptyTier_Throws` · `Branch_NoTiers_Throws` | — / ctor / throws |
| `Tree_DuplicateIdAcrossBranches_Throws` · `Tree_DuplicateIdWithinATier_Throws` · `Tree_DefaultNodeId_Throws` | — / ctor / throws |
| `Tree_PartialBranchesAreLegal` | three branches of 2 / 2 with no keystone / ctor / `NodeCount` 12 (rule 8) |
| `Tree_TryLocate` | the 27-tree / a branch-1 tier-3 id; a stranger / (1, 3) true; false |
| `Tree_Guards` | default id; default character; null branches; a null branch / ctor / throws each |
| `Catalog_SkillLookupAndDuplicates` · `Catalog_TreeLookupAndDuplicates` | — / — / mirror the character rows (rule 11) |
| `Catalog_TreeForCharacter` | one tree for `character.oathbound` / `TryGetTreeFor` / true; a class with none / false |
| `Catalog_TwoTreesForOneCharacter_Throws` | — / ctor / throws naming the character (rule 11) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

None. Pure C#, nothing registered, nothing authored; six assemblies compiling is the check.

## Out of scope

- **Authoring** — M3-02b, which is where the Inspector shape and the boot lists live.
- **Gating, availability, the live tree** — M3-03. This task says what a tree *is*; that one says what may be taken.
- **Offers** — M3-04.
- **Evaluating a trigger on a cadence, the 40 % floor, Auto/Manual** — M3-06 and M3-07. `IsMet` is a predicate; when it is asked is the runner's.
- **Pact variants** (GD §13.2, M6-05a). A `SkillSpec` has no Pact field: a Pact is a *variant the offer produces*, roughly 1.8× the clean node plus Veilrot, and its shape is M6-05a's to decide against a generator that exists by then.
- **Content** — the Oathbound's nodes are M3-12's. Nothing here is a node anyone can take.

## As built

**Built exactly the Files table** — three Core files, two test files, the `ContentCatalog` edits and
the four `ContentTests` mirror rows. Both folders already existed, so no asmdef, no `csc.rsp`, no
folder `.meta`. All five new files are pure C# with file-scoped namespaces, and all seven touched
files were confirmed in their intended assembly through `GetAssemblyNameFromScriptPath` rather than
assumed (Traps §5).

**Verified:** **1191 EditMode / 0 / 0, twice consecutively**, against M3-05's 1148 — **43 new rows**,
which is 26 + 13 + 4 and matches this Tests table plus the implied guard rows below, exactly.
PlayMode 11/11; the M2-15a `FrameOrderTests` intermittency did not fire on this run, which one run
is not evidence about either way. Six assemblies, zero compile errors, zero analyzer warnings,
Console clean of everything. `git status` clean of anything unasked: two modified `.cs`, five new
ones and their `.meta`s, no `ProjectSettings/` diff, no scene or asset churn.

### Deviations — three, one of which corrects this spec

1. **Rule 6's parenthetical spelling is wrong, and rule 6's own test row is what proves it.** The
   rule says *"a NaN field fails every clause (`!(value < threshold)` spelling, AR §18.3)"* — but
   `!(value < threshold)` is precisely the spelling of `AtLeast` as the negation of `Below`, and
   `!(NaN < 0.6)` is `!false` = **true**. Written literally, a broken blackboard would *satisfy*
   every `AtLeast` clause the player owns and fire every active at once, which is the exact failure
   the rule's own sentence forbids — and `Trigger_NaNFieldFails` demands `AtLeast 0` on a NaN return
   **false**. Shipped as the two natural comparisons written out rather than derived from each
   other: `value < threshold` and `value >= threshold`, both of which fail NaN because every
   comparison against NaN is false. **AR §18.3 is not contradicted, it is applied**: its rule is
   *put NaN on the safe side of the predicate*, and the `!` in `!(value > 0f)` is there because a
   **guard** wants NaN on the *throw* side. A predicate that wants NaN on the *false* side needs no
   `!` at all. The prose of rule 6, its reason, and its test row all agree; only the parenthetical
   is a slip. `TriggerClause.IsMet`'s remarks carry the argument, and the test row names the trap it
   avoids.
2. **`SkillBranchSpec` guards its `NameKey`, which no rule asked for.** Additive. Rule 3's argument
   one type over: M3-09d draws the branch name above its column, `default(LocKey)` carries a null
   past the struct's own constructor (AR §18.3), and a nameless branch is silent rather than loud
   (M2-06 rule 11). `Branch_Guards` is the row. Worth knowing it is *stricter* than the three
   shipped specs beside it — `CharacterSpec`, `EnemySpec` and `ModeSpec` all take a `nameKey` and
   guard none of them, which is a gap M3-14b already owns.
3. **One parking-lot line added to the ROADMAP** (see the AR §5 finding below). No `Architecture.md`
   edit: nothing in it is wrong.

### AR §5's module table — checked, and it needs no line

`Core/Content/` held **zero** `using Soulvail.*` before this task: it was the leaf every other module
depended on. It now names `Core.Combat` (`CombatBlackboard`, on `IsMet`) and `Core.Effects`
(`IEffect`), and since `Core/Combat` has depended on `Core/Content` since M0-07 — `Health`, `Weapon`
and `Targeter` all take specs — **`Content ↔ Combat` is now a namespace cycle** inside the one
assembly.

**No row is wrong, so no row is edited.** §5's `Content` row already lists `SkillSpec` in its *Key
types*, and the table says what a module *owns*, not what it depends on — it has no dependency
column for this to be missing from. AR §12 makes no acyclicity promise either. This is not the
`XpCurve` case: there, a type was in the wrong module and §5 said so; here both types are content
(authored data resolved from a `ContentId`, AR §10.1) and the Files table placed them correctly.

What is new is that the cycle would block AR §5's own escape hatch — *"split into separate
assemblies only if compile times demand it"* — so it is recorded as a **parking-lot line**, one
sentence, rather than a ledger row: nothing owns it, and it bites only if that split is ever wanted.

### Implementation choices the spec left open

- **Node-id uniqueness is the tree's, not the branch's.** A branch cannot see its siblings, so
  checking it there would catch half the cases and give the other half a different message.
  `SkillTreeSpec` builds the `TryLocate` index and detects duplicates in the same pass, so rule 10's
  dictionary costs nothing extra.
- **`TryLocate` misses with `branch = -1, tier = 0`.** −1 is how "nobody" is spelled everywhere in
  this project (`CombatBlackboard.CurrentTargetId`, `EnemyRegistry`'s ids from 1), and a caller that
  ignored the `bool` would index out of range rather than silently read branch 0.
- **Both enum dispatches in `TriggerClause.IsMet` are switches with loud `default`s, and neither
  enum is validated at construction** — `EnemyBehaviourKind`'s documented argument (AR §18.4): the
  loud place for an unrecognised member is the dispatch that knows the full set, and a guard at the
  door would only repeat that list somewhere it could drift.
- **`IndexTreesByCharacter` is its own method rather than a fourth `Index` call**, because the key
  is not the entry's id: the message has to name the *character*, and "duplicate tree id" would
  point at two assets that are correctly named.
- **`CopyEffects` is one `internal static` on `SkillSpec`, shared with `ActiveSpec`** — the two
  lists are the same kind of thing, and a second copy of the loop would be a second place for the
  null rule to drift. The empty case returns a wrapper over `Array.Empty<IEffect>()` (M3-01b's
  lesson) and **is not a cached static** — AR §7, and `Palette` (M3-13a) stays the only sanctioned
  one.
- **`default(TriggerClause)` is left legal** and is *HpFraction Below 0* — a clause that can never
  hold rather than an invalid one. AR §18.3's "check at both ends" does not bite: the invariant is
  only that the threshold is finite, and zero is.

### Flagged for the owner, not edited

**CH §5's *"8 (7 + 1 Keystone)"* is a slip for 9 (8 + 1)** — 3 × 8 is 24 and the same table says 27.
Rule 7 says flag it; it has been a [ROADMAP parking-lot](../ROADMAP.md#parking-lot) line since
M3-00a and `Tree_RecordsShape` now asserts the shipped shape (2/2/2/2/1 = nine a branch,
twenty-seven a class) against it. Promoted by **M7-04**, which authors all eighty-one.

### One seam left open on purpose

M3-05 flagged that `EffectRegistry.CanApply` answers *"is there a handler for this type"* and not
*"does this effect's address resolve"*. Nothing here closes it — a `SkillSpec` never looks inside an
`IEffect`, which is the whole of ADR-0009 — and `SkillSpec.Effects`' remarks now say so at the place
a reader would ask. Still M3-02b's or M3-14b's call.

# M3-02b — Skill authoring: `SkillDefinition`, `SkillTreeDefinition`, effect definitions, and the boot lists

**Size:** M · **Depends on:** M3-02a · **Branch:** `m3-02b-skill-authoring`
**Design refs:** AR §10.1, §12, §13; ADR-0006, ADR-0009; Traps §5 · **Ledger rows:** none

## Goal

A designer can author a node, an effect and a class's tree as assets, and boot turns them into the catalog — polymorphic on the effect side without a switch anywhere, and with nothing shipped in `Data/` until M3-12 has nodes worth shipping.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Authoring/EffectDefinition.cs` | Game | the abstract SO every primitive's authoring half derives from — **block namespace** (Traps §5) |
| `Game/Authoring/ModifyStatDefinition.cs` | Game | the first concrete one — **block namespace** |
| `Game/Authoring/SkillDefinition.cs` | Game | the node — **block namespace** |
| `Game/Authoring/SkillTreeDefinition.cs` | Game | the tree, three branches of tiers of node references — **block namespace** |
| `Tests/Game/Authoring/SkillAuthoringTests.cs` | Tests.Game | all four, one fixture: conversion, validation, the boot lists |
| *small edits* | | `BootInstaller.Install` + `skills`, `trees` (required lists, `enemies`' argument); `BootScope` + `_skills`, `_trees` serialized arrays, **empty until M3-12**; `BootScope.prefab` gains the two empty fields; `InstallerTests` (4 sites) and `ResumeFlowTests` (1) gain two arguments |

Only these files change. Anything else is a deviation: say so in *As built*. **No `Data/` asset ships** — see rule 7.

## Public API

```csharp
namespace Soulvail.Game.Authoring
{
    /// One primitive's Inspector half. A subclass per primitive; a node references instances.
    public abstract class EffectDefinition : ScriptableObject
    {
        public abstract IEffect ToEffect();
    }

    [CreateAssetMenu(menuName = "Soulvail/Effects/Modify Stat", fileName = "ModifyStat")]
    public sealed class ModifyStatDefinition : EffectDefinition
    {
        // [SerializeField] PlayerStat _stat; ModifierKind _kind = PercentAdd; float _value;
        public override IEffect ToEffect();
    }

    /// One clause of a CC §6.4 condition, as it sits in the Inspector.
    [Serializable]
    public sealed class TriggerClauseField
    {
        // [SerializeField] TriggerField _field; TriggerComparison _comparison; float _threshold;
        public TriggerClause ToClause();
    }

    [CreateAssetMenu(menuName = "Soulvail/Content/Skill", fileName = "Skill")]
    public sealed class SkillDefinition : ScriptableObject
    {
        // _id, _nameKey, _descriptionKey, _kind, EffectDefinition[] _effects
        // [Header("Active")] float _cooldown = 8f; TriggerClauseField[] _trigger; EffectDefinition[] _onCast
        // [Header("Upgrade")] SkillDefinition _parent
        public string Id { get; }
        public SkillSpec ToSpec();
    }

    [Serializable] public sealed class TierField   { /* SkillDefinition[] _nodes */ }
    [Serializable] public sealed class BranchField { /* string _nameKey; TierField[] _tiers */ }

    [CreateAssetMenu(menuName = "Soulvail/Content/Skill Tree", fileName = "SkillTree")]
    public sealed class SkillTreeDefinition : ScriptableObject
    {
        // _id, CharacterDefinition _character, BranchField[] _branches (three)
        public string Id { get; }
        public SkillTreeSpec ToSpec();
    }
}

// BootInstaller (changed)
public static void Install(
    IContainerBuilder builder,
    IReadOnlyList<CharacterDefinition> characters,
    IReadOnlyList<EnemyDefinition> enemies,
    IReadOnlyList<ModeDefinition> modes,
    IReadOnlyList<SkillDefinition> skills,
    IReadOnlyList<SkillTreeDefinition> trees);
```

## Behaviour

1. **Polymorphism by asset type.** An effect is a `ScriptableObject` subclass per primitive, and a node references effect *assets*. Two alternatives were rejected: `[SerializeReference]` with a polymorphic list — the Inspector has no built-in type picker for it in Unity 6, so it needs a custom drawer, which is tooling this task should not grow; and a flat union struct with a kind enum — whose `ToEffect` is a `switch (kind)`, the ADR-0009 ban arriving on the authoring side. An effect asset reads in the Inspector as what it is, is reusable across nodes ("+15 % fire rate" is one asset three nodes may point at), and a new primitive is a new subclass with no edit to any existing file.
2. **`ToEffect` and `ToSpec` are the only ways out**, and neither validates what the core constructor already validates (M2-02 rule 9): `ModifyStatDefinition` hands its three fields to `ModifyStat` and rewraps the exception with the asset's name; `SkillDefinition` and `SkillTreeDefinition` do the same with theirs. One account of what legal content is, in core.
3. **`SkillDefinition.ToSpec`** builds `Effects` from `_effects` (each `ToEffect()`; an empty slot is an error naming the asset and the index), the `ActiveSpec` from `_cooldown`, `_trigger` and `_onCast` **only when `_kind` is Active**, and `ParentId` from `_parent.Id` **only when `_kind` is Upgrade**. Fields the kind does not use are ignored — the two `[Header]`s say which — so a Passive with a stray cooldown is not an error, and the spec's both-directions rule (M3-02a rule 1) is met by construction rather than by a designer keeping fields in step.
4. **A parent is a reference to the asset, never a typed id.** `_parent.Id` is read at conversion, so a renamed parent follows; a string would be a second copy of the id that drifts. The same reasoning puts node references, not ids, in the tree.
5. **`SkillTreeDefinition.ToSpec`** turns three `BranchField`s of `TierField`s of `SkillDefinition` references into ids, and the `CharacterDefinition` reference into the tree's `CharacterId`. Empty slots are named; shape, duplicates and count are `SkillTreeSpec`'s to refuse. Nested serializable classes rather than jagged arrays, because Unity serialises the former and not the latter.
6. **Boot lists, required.** `Install` takes `skills` and `trees` for the reason it takes `enemies` — a call site that could omit them would produce a catalog whose only symptom is a level-up screen with nothing on it. `BootScope` carries two more serialized arrays. Neither list resolves the other at conversion (a tree holds ids, and the catalog is built from both at once); the cross-check is `TreeRules`' at `Start` and M3-14's over every asset.
7. **Nothing ships in `Data/`.** The Oathbound's twelve nodes are M3-12's, and a placeholder node here would be content offered to a player two tasks before anything can offer it — or deleted two tasks later. The boot arrays are empty and `Boot_EmptyListsAreLegal` says that is a boot before M3-12, not a broken one.
8. **`OnValidate` warns**, the established shape: a malformed id, and two cheap kind-mismatch warnings — Active with an empty `_onCast`, Upgrade with no `_parent` — because both fail at boot with a message a designer will otherwise meet one scene later. Warnings, never fixes; `ToSpec` is where refusal lives.

## Tests

| Test | Given / When / Then |
|---|---|
| `ModifyStat_ToEffect_RoundTrip` | `WeaponDamage`, `PercentAdd`, 0.15 through `SerializedObject` / `ToEffect` / a `ModifyStat` with the three |
| `ModifyStat_Invalid_NamesTheAsset` | value NaN / `ToEffect` / `ArgumentException`, message starts with the asset name (rule 2) |
| `Effect_IsPolymorphic` | an `EffectDefinition[]` holding a `ModifyStatDefinition` / `ToEffect` on each / an `IEffect` per entry, no cast anywhere in the caller (rule 1) |
| `Skill_Passive_ToSpec` | two effect assets / `ToSpec` / `Kind` Passive, ids and keys, two effects, `Active` null, `ParentId` default (rule 3) |
| `Skill_Active_ToSpec` | cooldown 8, one clause, one cast effect / `ToSpec` / `Active.Cooldown` 8, one clause, one cast effect |
| `Skill_PassiveIgnoresActiveFields` | Passive with a cooldown and a cast effect authored / `ToSpec` / `Active` null, no throw (rule 3) |
| `Skill_Upgrade_ParentIdFollowsTheAsset` | a parent asset with id `skill.oathbound.consecrate` / `ToSpec`; rename the parent's `_id`; `ToSpec` again / `ParentId` equals the parent's current id both times (rule 4) |
| `Skill_EmptyEffectSlot_NamesTheAssetAndIndex` | `_effects[1]` null / `ToSpec` / throws, message names the asset and `1` |
| `Skill_Invalid_NamesTheAsset` | Active with no cast effects / `ToSpec` / `ArgumentException` starting with the asset name |
| `Tree_ToSpec_Shape` | three branches of 2 / 2 / 2 / 2 / 1 node references, a character / `ToSpec` / `NodeCount` 27, `CharacterId` from the character asset (rule 5) |
| `Tree_EmptyNodeSlot_NamesTheAsset` | a null in a tier / `ToSpec` / throws naming the asset, branch and tier |
| `Tree_Invalid_NamesTheAsset` | two branches / `ToSpec` / `ArgumentException` starting with the asset name |
| `Install_RegistersSkillsAndTrees` | one skill, one tree / `Install`, resolve the catalog / `Skill(id)` and `TryGetTreeFor` answer (rule 6) — `InstallerTests` |
| `Install_NullSkills_Throws` · `Install_NullTrees_Throws` | — / `Install` / throws — the `Boot_NullXList_Throws` shape |
| `Install_EmptySkillSlot_Throws` | `[null]` / `Install` / throws naming the slot |
| `Boot_EmptyListsAreLegal` | empty arrays / `Install` / a catalog with no skills and no trees, no throw (rule 7) |
| `Boot_ScopeCarriesTheTwoLists` | `BootScope.prefab` / read the two fields back after a save (Traps §5) / both present, both empty |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Create → Soulvail → Effects → Modify Stat; set a stat and a value. Create → Soulvail → Content → Skill; drag the effect in. Create → Soulvail → Content → Skill Tree; the Inspector shows three branches of tiers of node slots. Delete all three — nothing ships (rule 7).
2. **[Editor]** Boot → Menu → Descend. The run plays exactly as before; the two empty lists compose.

## Out of scope

- **Content** — M3-12 authors the Oathbound's nodes and tree and adds them to `BootScope`.
- **Validation across assets** — M3-14: every tree node is in the skill list, every Keystone is a last-tier singleton, every parent is in the same branch, every key resolves.
- **A tree editor.** Three nested arrays in the default Inspector is enough for twelve nodes; M7-04's eighty-one may want more, and that is the day to write one — parking lot.
- **Authoring halves for primitives that do not exist** — each arrives with its primitive (M3-05's rule).

## As built

**Built as specced, five files and the six small edits, nothing outside the table.** The four definitions are in `Game/Authoring/`, the fixture is `Tests/Game/Authoring/SkillAuthoringTests.cs`, and the edits are `BootInstaller.Install` (+2 required parameters, +2 null guards, +2 `Convert` calls), `BootScope` (+2 serialized arrays), `BootScope.prefab` (+`_skills: []`, +`_trees: []` — two lines, nothing else), `InstallerTests` (4 argument sites + 1 new row) and `ResumeFlowTests` (1 argument site). **No `Data/` asset ships** (rule 7).

**One decision the owner ruled before a line was written: M3-02b closes the address seam, not M3-14b.** `EffectRegistry.CanApply` answers *"is there a handler for this type"* and `ModifyStat`'s constructor deliberately does not validate `PlayerStat`, so a node authored against an address that is no longer an enum member passed every door and would have thrown out of `PlayerStats.Resolve` at the moment a player picked it. The gap is narrower than it reads — a member with no resolver line is already caught by M3-05's `Stats_ResolveEveryMember`, and the enum is a dropdown, so the only live vector is a serialized `int` left behind by a removed or reordered member. `Enum.IsDefined` in `ModifyStatDefinition.ToEffect` plus an `OnValidate` warning is five lines in a file this task was writing anyway, against M3-14b's version landing ten tasks *after* M3-12 authors the content it guards and being test-only by that spec's rule 11. **The door is per primitive by design** — each primitive's authoring half answers for its own fields, which is the same open set ADR-0009 buys one layer down. **The general version was not built and is not this task's**: widening `IEffectHandler<T>` with a `CanApply(TEffect)` member so the registry's door means what its name says is a Core change to two files M3-05 just shipped, and its natural owner is **M3-03** — the task that actually calls that door at `Start`.

**Deviations, all additive and all in the tests:**

1. **Eight test rows beyond the table**, which is M3-05's precedent (four implied guard rows and one instance test). Two are the owner's ruling — `ModifyStat_UndefinedAddress_NamesTheAsset`, `ModifyStat_OnValidate_UndefinedAddress_Warns`. Two are implied non-finite rows the guard sentence asks for and the table does not list: `Skill_NonFiniteCooldown_NamesTheAsset` and `Skill_NonFiniteThreshold_NamesTheAsset`, the two `float` doors this task opens. **Four are rule 8's, which has no row of its own in the Tests table** — `Skill_OnValidate_InvalidId_Warns`, `Skill_OnValidate_ActiveWithNoCastEffect_Warns`, `Skill_OnValidate_UpgradeWithNoParent_Warns`, `Tree_OnValidate_InvalidId_Warns` — and a behaviour rule with no test is the one thing the session protocol forbids. The eighth is `Skill_ToSpec_ReturnsNewInstanceEachCall`, ADR-0006's habit since `EnemyDefinition`, and it matters more here than on any earlier definition type because the `SkillSpec` is the *source* M3-03 hands `EffectRegistry.Apply`.
2. **`Skill_Active_ToSpec` authors cooldown 5, not the table's 8.** `_cooldown` initialises to 8, so asserting 8 would pass identically whether the YAML key binds or the field is holding its C# initialiser — Traps §7, the same hole `Husk_EveryYamlKeyBindsToAField` exists to close. `ModifyStat_ToEffect_RoundTrip` has the same problem with `_kind` (authored `PercentAdd`, initialiser `PercentAdd`) and answers it in the row rather than beside it: it flips the field to `Flat` and converts again.
3. **The boot rows are split the way the two tables ask.** `Install_RegistersSkillsAndTrees` is in `InstallerTests`, as its row says; the other five are in `SkillAuthoringTests`, as the Files table says. `InstallerTests._created` widened from `List<CharacterDefinition>` to `List<ScriptableObject>` so its row's five throwaway assets are destroyed with everything else.

**One correction to this spec's own Public API, not a behaviour change:** the ScriptableObject is declared **first** in `SkillDefinition.cs` and `SkillTreeDefinition.cs`, with `TriggerClauseField` / `BranchField` / `TierField` following it, rather than the order the API block lists. Traps §5's failure mode is Unity's script importer parsing a file to find the type it declares, and the type whose name matches the file leading is the cheap insurance. **It was checked rather than assumed** — `MonoScript.FromScriptableObject` answers non-null for all three concrete types, so the public top-level serializable classes do not confuse the importer.

**A finding for [M3-14b](M3-14b-content-validation.md), which owns the id sweep and should not discover this at its own first red run:** `ContentId.IsValid` accepts `[a-z0-9]` then `.[a-z0-9_-]+`, so **camelCase ids are refused**. M3-14b rule 3 and its `EveryFileName_MatchesTheLastSegmentOfItsId` row both use `skill.oathbound.temperedVow` as the worked example, and that id cannot be constructed. The convention M3-12 has to author against is `skill.oathbound.tempered-vow`, and rule 3's file-name comparison needs to be hyphen-to-PascalCase rather than case-insensitive on the segment. Found the honest way: two rows failed on it.

**Verified:** **1218 EditMode / 0 / 0, twice consecutively on the final code** against M3-02a's 1191 — **27 new rows**, which is the Tests table's 18 plus the eight above plus nothing else. Six assemblies, zero compile errors, zero analyzer warnings. All nine touched files confirmed in their intended assembly through `GetAssemblyNameFromScriptPath`, and all seven new types confirmed in `Soulvail.Game` by direct `typeof` reference. `git status` clean of anything unasked: five modified files, five new `.cs` and their five `.meta`s, no `ProjectSettings/` diff, no folder `.meta` (both folders existed), no scene churn. The fourteen Console errors are pre-existing `LogAssert`-expected ones from the save-store and boot-flow fixtures; the eight warnings are runtime `Debug.LogWarning`s from this task's own `OnValidate` rows and M0-13's fallback-seed row — none is a compiler or analyzer warning.

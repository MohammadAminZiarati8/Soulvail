# MK-01a — A path's shape

**Size:** M · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; AR §18.3 (branch indices)

## Goal

Core describes a class's **path** (ranked skills with unlock levels), installs it after the class's
tree, and refuses a node below its level.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/PathSpec.cs` | Core | **New.** `PathSpec` + `PathSkillSpec` (one file, `SkillTreeSpec`'s precedent) |
| `Core/Progression/TreeRules.cs` | Core | `InstallPath`, `PathId`, `UnlockLevelOf` |
| `Core/Progression/SkillTree.cs` | Core | `OnPathInstalled`, `SyncLevel`, the level gate |
| `Tests/Core/Content/PathSpecTests.cs` | Tests.Core | **New** |
| `Tests/Core/Progression/PathInstallTests.cs` | Tests.Core | **New** |
| *small edits* | | `ContentCatalog` (paths list + lookups), `CharacterSpec` (`PathIds`, `PathOpensAtLevel`), `RunSession` (calls `SyncLevel` wherever the level moves and on restore), `Docs/Invariants.md` §18.3 |
| *ripple* | | fixtures constructing `ContentCatalog` / `CharacterSpec` |

## Public API

```csharp
public sealed class PathSkillSpec { PathSkillSpec(SkillBranchSpec ranks, int unlockLevel);
  SkillBranchSpec Ranks { get; } int UnlockLevel { get; } }
public sealed class PathSpec {
  public const int MaxSkills = 8;
  PathSpec(ContentId id, ContentId characterId, LocKey nameKey, LocKey descriptionKey,
           IReadOnlyList<PathSkillSpec> skills);
  ContentId Id, CharacterId; LocKey NameKey, DescriptionKey;
  IReadOnlyList<PathSkillSpec> Skills; int NodeCount;
  bool TryLocate(ContentId skillId, out int skill, out int rank); }
// ContentCatalog: IReadOnlyList<PathSpec> Paths; PathSpec Path(ContentId); bool TryGetPath(ContentId, out PathSpec)
// CharacterSpec: IReadOnlyList<ContentId> PathIds; int PathOpensAtLevel   // 0 = the class has no paths
// TreeRules: void InstallPath(PathSpec path, ContentCatalog catalog); ContentId PathId { get; }
//            int UnlockLevelOf(ContentId id)   // 1 for every node outside a path
// SkillTree: void OnPathInstalled(); internal void SyncLevel(int level)
```

## Behaviour

1. Rank N is the branch's tier N (Volley's shape: rank 1 Active/Passive, ranks 2+ `Upgrade`s parented
   to the rank below). 1–8 ranks; `unlockLevel` ≥ 1.
2. A `PathSpec` holds 1–`MaxSkills` skills; ids unique across the path; guards as `SkillTreeSpec`'s.
3. `InstallPath` appends each skill's branch after the class's branches (indices 3, 4, …), in path
   order; `BranchCount` grows by the skill count; `PathId` becomes the path's id. A second install,
   an install on a run that borrowed a splash branch, or a path of another class throws.
4. A path node is **available** only when its tier rule holds **and** the level last given to
   `SyncLevel` is ≥ its skill's `UnlockLevel`. Every availability reader (`IsAvailable`, `Available`,
   offers, free pick) agrees, because they all go through the tree.
5. `UnlockLevelOf` answers the skill's level for a path node and 1 otherwise; an unknown id throws.
6. `CharacterSpec`: `PathOpensAtLevel` is 0 with no `PathIds`, else ≥ 2; ids unique.
7. `IsAvailable` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `PathSkill_RanksAreTiers` | 3-tier branch / rank 2 / tier-2 id (1) |
| `PathSpec_Validates` | 0 skills, 9 skills, duplicate id, level 0 / throws (1, 2) |
| `Install_AppendsAfterTheClass` | Ranger fixture + 2-skill path / branches 3 and 4, `BranchCount` 5, `PathId` (3) |
| `Install_Twice/AfterSplash/OtherClass_Throws` | (3) |
| `Gate_BelowLevel_Unavailable` | skill at 14, `SyncLevel(13)` / rank 1 unavailable; `SyncLevel(14)` / available (4) |
| `Gate_AppliesToOffers` | offers never draw a gated node (4) |
| `UnlockLevelOf_ClassNodeIsOne` | Ranger node / 1; path node / its level (5) |
| `Character_PathFields_Validate` | (6) |
| `IsAvailable_NoAlloc` | (7) |

## Out of scope

- Choosing, saving, Overflow (MK-01b); authoring; screens.

## As built

_Filled at merge._

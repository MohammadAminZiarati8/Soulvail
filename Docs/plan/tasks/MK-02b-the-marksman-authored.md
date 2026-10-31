# MK-02b — The Marksman authored

**Size:** M · **Depends on:** MK-01b, MK-02a · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; Workflow §5 (asset naming)

## Goal

The Ranger offers three path slots from level 11; the first is the Marksman, holding Steady Aim
(Lv 11, 5 ranks) and Eagle Eye (Lv 14, 3 ranks). Later MK tasks insert their skills into this asset.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Authoring/PathDefinition.cs` | Game | **New.** ScriptableObject → `PathSpec` (**block namespace**, Traps §5) |
| `Data/Paths/Marksman.asset` | asset | **New.** `path.ranger.marksman` |
| `Data/Skills/Marksman/` | assets | **New.** `SteadyAim`, `SteadyAim2`–`5`, `EagleEye`, `EagleEye2`–`3` + their `ModifyStat` effect assets |
| `Tests/Game/Authoring/MarksmanPathTests.cs` | Tests.Game | **New** |
| *small edits* | | `CharacterDefinition` (`_paths`: three `PathDefinition` slots, null = coming soon; `_pathOpensAtLevel`), `Data/Characters/Ranger.asset` (slot 1 Marksman, 2–3 empty, level 11), the catalog's builder in `BootInstaller` (collects paths), `English.asset`/`Pseudo.asset` (keys below), `SkillIconBook` (placeholder icons) |

## Public API

```csharp
namespace Soulvail.Game.Authoring {
  public sealed class PathDefinition : ScriptableObject {
    // _id, _nameKey, _descriptionKey, _skills: [{ SkillDefinition[] ranks; int unlockLevel }]
    public PathSpec ToSpec(ContentId characterId); } }
// CharacterDefinition: IReadOnlyList<PathDefinition> PathSlots  (length 3, nulls allowed)
```

## Behaviour

1. `path.ranger.marksman`: skill 1 Steady Aim (`skill.marksman.steady-aim`, `-2`…`-5`), unlock 11;
   skill 2 Eagle Eye (`skill.marksman.eagle-eye`, `-2`, `-3`), unlock 14. Rank 1 `Passive`, the rest
   `Upgrade` with the rank below as parent.
2. Effects are `ModifyStat` on the player with MK-02a's rank table exactly.
3. Ranger: `PathOpensAtLevel` 11; `PathIds` holds only non-null slots; the empty slots are kept on the
   definition so the screen can draw "coming soon" cards (MK-03).
4. Loc (English): Marksman — *"Stand your ground. Every arrow you loose standing still hits
   harder."*; each rank's name and one-line description, e.g. Steady Aim 1: *"Each arrow you loose
   standing still deals +5% damage, up to 5 times. Moving resets it."* Pseudo mirrors every key.
5. Icons: placeholders from the existing Ranger set, tinted, until the owner's art lands. Each
   rank shares its skill's icon.

## Tests

| Test | Given / When / Then |
|---|---|
| `Marksman_HasItsSkillsInOrder` | asset / ids, ranks, unlock 11 and 14 (1) |
| `Marksman_RanksChainByParent` | (1) |
| `Marksman_EffectsMatchTheTable` | each rank's stat and amount (2) |
| `Ranger_OffersThreeSlots_OneFilled` | 3 slots, slot 1 Marksman, level 11 (3) |
| `Catalog_HasTheMarksman` | boot catalog / `TryGetPath` (3) |
| `Loc_EveryKeyInBothTables` | (4) |
| `Icons_EverySkillHasOne` | (5) |

## Manual verification (Editor / device)

1. **[Editor]** No visible change yet; the console stays clean on boot and on a Jungle run.

## Out of scope

- The tree screen (MK-03) — until then a path cannot be chosen in play. Final icon art.

## As built

_Filled at merge._

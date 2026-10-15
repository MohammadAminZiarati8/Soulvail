# UI-12c — The hero sheet's model

**Size:** S · **Depends on:** UI-12a (the `CharacterLook` constructor it extends) · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** owner's ruling of 2026-10-15 (the hero page shows everything about the class, stats as numbers), CH §5 (the tree), UI-05d (`SkillStrip.Build`, which this supersedes) · **Ledger rows:** none

## Goal

One plain-C# builder turns a class into everything its page will show — the description, five base stats as numbers, the basic attack, the Space move, and every rank of every branch with its icon, name and description — and every label it needs exists in `English.asset`.

## Why it lives in Game, not Core

The sheet carries sprites (`SkillIconBook`, `CharacterLook.Emblem` and `SpaceIcon`) and the Space move's keys, which are on `CharacterLook`, not on `CharacterSpec`. So it is `SkillStrip.Build`'s kind of thing: a pure function in `Soulvail.Game.Presentation`, no `MonoBehaviour`, no scene, tested in Tests.Game. Every number and key it reads comes from core types.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/HeroSheet.cs` | Game | the model and its `Build` |
| `Tests/Game/Presentation/HeroSheetTests.cs` | Tests.Game | the rule rows |
| *small edits* | | `CharacterDefinition.cs`: `_attackNameKey`, `_attackDescriptionKey` (strings, empty by default), passed by `ToLook`; `CharacterLook.cs`: `AttackNameKey`, `AttackDescriptionKey`, two optional constructor parameters after `portrait`; `Ranger.asset`: the two keys; `English.asset`: the keys below; `Pseudo.asset`: regenerated (`Soulvail ▸ Localisation ▸ Regenerate Pseudo-locale`); `CharacterLookTests.cs`: the pass-through row. *Ripple:* the three other character assets gain the two empty fields if a row reserialises them. |

## Public API

```csharp
// HeroSheet.cs — plain C#, file-scoped namespace Soulvail.Game.Presentation.
public sealed class HeroSheet
{
    public static HeroSheet Build(CharacterSpec spec, ContentCatalog catalog, CharacterLook look, SkillIconBook icons);

    public LocKey DescriptionKey { get; }          // CharacterSpec.DescriptionKey
    public IReadOnlyList<Stat> Stats { get; }      // always five, in rule 2's order
    public bool HasAttack { get; }
    public Entry Attack { get; }                   // default when !HasAttack
    public bool HasSpace { get; }
    public Entry Space { get; }                    // default when !HasSpace
    public IReadOnlyList<Branch> Branches { get; } // empty for a class with no tree

    public readonly struct Stat
    {
        public LocKey Label { get; }               // "Health"
        public LocKey Format { get; }              // "{0:0}", fed to ILocalizer.Format
        public float Value { get; }                // the base number, no modifier applied
    }

    public readonly struct Entry
    {
        public Sprite Icon { get; }                // null: the page draws Icon_Placeholder
        public LocKey NameKey { get; }
        public LocKey DescriptionKey { get; }
        public int Rank { get; }                   // 1-based tier for a tree node; 0 for the attack and the Space move
    }

    public sealed class Branch
    {
        public LocKey NameKey { get; }             // SkillBranchSpec.NameKey
        public IReadOnlyList<Entry> Ranks { get; } // tier order, then node order within a tier
    }

    // The keys the page draws as headings, public so the view and the tests share them.
    public static readonly LocKey StatsHeading;    // ui.hero.stats
    public static readonly LocKey AttackHeading;   // ui.hero.attack
    public static readonly LocKey SpaceHeading;    // ui.hero.space
    public static readonly LocKey TreeHeading;     // ui.hero.tree
    public static readonly LocKey RankFormat;      // ui.hero.rank
}

// CharacterLook — gains, after `Sprite portrait = null`:
//   LocKey attackNameKey = default, LocKey attackDescriptionKey = default
public LocKey AttackNameKey { get; }
public LocKey AttackDescriptionKey { get; }
```

`static readonly` keys are immutable values (`ClassCard.HpKey`'s shape), not state.

## Behaviour

1. **`Build` reads, never writes**, and returns a new sheet each call. A null `spec` or `catalog` throws `ArgumentNullException`; a null `icons` is allowed and draws every icon null; a `default` look gives no attack and no Space entry.
2. **Five stats, always, in this order, from the base `CharacterSpec`:**

   | Label key | Text | Value | Format key | Text |
   |---|---|---|---|---|
   | `ui.hero.stat.health` | Health | `MaxHp` | `ui.hero.value.whole` | `{0:0}` |
   | `ui.hero.stat.damage` | Damage | `Weapon.Damage` | `ui.hero.value.whole` | `{0:0}` |
   | `ui.hero.stat.rate` | Attacks/s | `Weapon.SwingsPerSecond` | `ui.hero.value.tenths` | `{0:0.0}` |
   | `ui.hero.stat.range` | Range | `Weapon.Range` | `ui.hero.value.metres` | `{0:0} m` |
   | `ui.hero.stat.speed` | Speed | `Movement.Speed` | `ui.hero.value.speed` | `{0:0.0} m/s` |

   For the Ranger today: 90, 13, 2.2, 10 m, 3.2 m/s. **Base means authored**: no tree node, Pact or Ordeal is applied, because the page is outside a run.
3. **The attack entry** is the look's `AttackNameKey` and `AttackDescriptionKey`, with the class emblem as its icon and rank 0. **A look with no attack name key has no attack entry** (`HasAttack` false) — today the three off-roster classes.
4. **The Space move entry** is the look's `SpaceIcon`, `SpaceNameKey` and `SpaceDescriptionKey`, rank 0, **present when `MovementSkill.Kind` is not `None` and the name key is set** — `SkillStrip.Build`'s condition, plus the key.
5. **One branch per `SkillTreeSpec` branch, in branch order**, each with its `NameKey` and **every node of every tier**: for each tier from 1, each node in the tier's order, an entry with the node's `SkillSpec.NameKey` and `DescriptionKey` and `Rank` = the tier. The Ranger gives three branches of three. A class with no tree gives none.
6. **A node's icon** is `icons.For(node)`; when that is null, the icon of its branch's tier-1 node; when that is null too, null. **A node the catalog does not hold is skipped**, never thrown on, and the ranks after it keep their own tier numbers.
7. **The description** is `CharacterSpec.DescriptionKey`, which is guarded non-default by its constructor.
8. **Every key the sheet emits for the Ranger resolves in `English.asset`**: the headings, the stat labels and formats, the attack's and Space move's, the branch names and every rank's name and description. The new rows, written by Claude:

   | Key | English |
   |---|---|
   | `ui.hero.stats` | Stats |
   | `ui.hero.attack` | Basic attack |
   | `ui.hero.space` | Special move |
   | `ui.hero.tree` | Skill tree |
   | `ui.hero.rank` | Rank {0} |
   | `ui.hero.stat.health` · `.damage` · `.rate` · `.range` · `.speed` | Health · Damage · Attacks/s · Range · Speed |
   | `ui.hero.value.whole` · `.tenths` · `.metres` · `.speed` | `{0:0}` · `{0:0.0}` · `{0:0} m` · `{0:0.0} m/s` |
   | `character.ranger.attack.name` | Longbow |
   | `character.ranger.attack.description` | Looses an arrow at the nearest enemy in range. The Ranger only shoots while standing still. |

   **"Special move", not "Space move"**: a phone has no Space bar, and the slot is the Ranger's roll, a future Knight's shield and a future Necrophos's turn ethereal, so its heading names the slot, not the key. The existing `ui.classselect.hp`, `.speed` and `.dps` stay: `ClassCard` reads them.

## Tests

| Test | Given / When / Then |
|---|---|
| `Build_Guards` | null spec, null catalog / throws; null icons and a default look / builds (rule 1) |
| `Stats_FiveInOrder_FromTheBaseSpec` | a spec with distinct numbers / `Build` / the five labels, formats and values of rule 2, in order |
| `Stats_Ranger_AreItsAuthoredNumbers` | `Ranger.asset`'s spec / `Build` / 90, 13, 2.2, 10, 3.2 (rule 2) |
| `Attack_FromTheLook_EmblemIcon` · `Attack_NoKey_NoEntry` | rule 3 |
| `Space_PresentWithAKindAndAKey` · `Space_NoneKind_NoEntry` | rule 4 |
| `Branches_EveryTierEveryNode_InOrder` | a two-branch tree, one tier holding two nodes / `Build` / entries in tier then node order, `Rank` the tier (rule 5) |
| `Branches_Ranger_ThreeOfThree` | the Ranger / `Build` / three branches, ranks 1, 2, 3 in each, names `skill.ranger.*` (rule 5) |
| `Branches_NoTree_Empty` | a spec with no tree in the catalog (rule 5) |
| `Icon_FallsBackToTheBranchFirst_ThenNull` | rule 6, both steps |
| `Node_MissingFromCatalog_Skipped` | rule 6 |
| `Description_IsTheSpecs` | rule 7 |
| `Ranger_EveryKeyResolvesInEnglish` | the Ranger's sheet / every key it emits, the five headings included / present in `English.asset` (rule 8) |
| `CharacterLook_CarriesTheAttackKeys` (in `CharacterLookTests`) | `ToLook` passes both, empty → default |

`Pseudo_IsTheGeneratorsOutput` and `Pseudo_HasARowForEveryEnglishRow` (existing) cover the regenerated pseudo-locale.

## Manual verification

None: nothing on screen changes in this task. UI-12d draws it.

## Out of scope

- **Drawing the sheet**, and removing `SkillStrip` — UI-12d.
- **Attack text for the Oathbound, Gravecaller and Emberwright**: they are off the roster (focus build), and rule 3 omits their entry. Their two keys are a few lines in `English.asset` and their assets whenever they come back.
- **Live numbers** (a stat with the tree's modifiers): the page is outside a run.
- **Cooldowns, damage per rank, or any number in a rank's description beyond its own text**: the rank's description key is the text.
- **Removing the `ui.classselect.hp`/`.speed`/`.dps` keys**: `ClassCard` still reads them.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`). Deviations from the above with their reasons, or "as specified"._

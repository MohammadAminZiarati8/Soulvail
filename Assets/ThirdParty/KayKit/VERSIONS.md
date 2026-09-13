# KayKit — third-party art

Three packs by Kay Lousberg (<www.kaylousberg.com>), all **CC0**. Free for commercial use;
credit optional. Each pack keeps its own `License.txt` because each names its own pack and
release date, and that is what an attribution check would want to read.

**Versions live in this file, not in folder names.** A pack folder called
`KayKit_Adventurers_2.0_FREE` looks harmless until 2.1 arrives, lands beside it, and every
GUID in the project still points at the old copy. That already happened once here — a
re-download arrived as `KayKit_Adventurers_2 1.0_FREE` and was a whole duplicate pack.
**To update: overwrite the files in place, keep the folder name, and bump the row below.**

| Pack | Version | Released | Folder |
|---|---|---|---|
| Adventurers | 2.0 | 2025-10-22 | `Adventurers/` |
| Character Animations | 1.1 | 2025-12-10 | `Animations/` |
| Skeletons | 1.1 | 2025-10-22 | `Skeletons/` |

## What is here

| Folder | Contents |
|---|---|
| `Adventurers/Characters` | 6 characters: Barbarian, Knight, Mage, Ranger, Rogue, Rogue_Hooded |
| `Adventurers/Props` | 20 weapons and held items — swords, axes, bows, shields, staff, wand, spellbook |
| `Animations/Rig_Medium` | **139 clips** in 8 files: CombatMelee, CombatRanged, MovementBasic, MovementAdvanced, General, Special, Simulation, Tools |
| `Animations/Rig_Large` | 34 clips in 6 files, for large enemies and bosses |
| `Animations/Mannequin` | The two untextured reference bodies, Medium and Large |
| `Skeletons/Characters` | 4 skeletons: Minion, Warrior, Mage, Rogue |
| `Skeletons/Props` | 13 skeleton-specific props — blade, axe, crossbow, staff, shields, arrows, quiver |

## The one fact that matters

**Every character in `Adventurers/Characters`, every skeleton in `Skeletons/Characters` and
the Medium mannequin share one skeleton: `Rig_Medium`, 23 joints, identical bone names.**
So all 139 Rig_Medium clips play on any of them, bone for bone, with no retargeting.

That is why the rigs are imported **Generic** rather than Humanoid: Humanoid would buy
cross-skeleton retargeting nobody needs here, and charge a per-frame retargeting cost on
every one of the enemies M2-04 caps at 28.

`Rig_Large` shares the bone *names* but not the proportions (3.98 m against 2.20 m), so it
needs its own avatar.

## Import rules these packs are set up under

- **Root node is empty and `applyRootMotion` is off.** Core owns velocity; no clip may move a
  body. Only 9 of the 173 clips carry root translation anyway — the four `Dodge_*` and five
  `Skeletons_*` ones.
- **"Optimize Game Objects" is off.** It strips the transform hierarchy, and `handslot.l` /
  `handslot.r` — the weapon attach sockets everything in `Props/` is built for — vanish with it.
- **Textures are Point-filtered.** They are palette atlases, and bilinear bleeds neighbouring
  swatches across the cell borders.
- **`loopTime` is set on 38 idle and locomotion clips.** They import non-looping, which parks a
  blend tree on a clip's last frame and freezes the body mid-stride.

Deleted on import, and safe to delete again on any update: the `gltf/` and `obj/` exports
(they need a package this project has not approved), `samples/`, `contents.png`, and the
duplicate animation FBXs the Adventurers and Skeletons packs both ship — those two files are
byte-identical to the copies in `Animations/Rig_Medium`.

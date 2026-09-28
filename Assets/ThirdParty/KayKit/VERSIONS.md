# KayKit — third-party art

Five packs by Kay Lousberg (<www.kaylousberg.com>), all **CC0**. Free for commercial use;
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
| Dungeon | 1.1 | 2026-07-16 | `Dungeon/` |
| Forest Nature | 1.0 | 2025-04-29 | `ForestNature/` |

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
| `Dungeon/Floors` | 34 tiles — dirt, stone, foundation edges, grates, spikes, wood |
| `Dungeon/Walls` | 40 pieces — walls (arched, windowed, gated, broken, cracked, scaffolded, sloped), pillars, columns, barriers |
| `Dungeon/Stairs` | 15 — straight, long, wide, walled, modular left/centre/right, wood |
| `Dungeon/Props` | 36 — barrels, boxes, crates, candles, torches, rubble, wall crests, and banners in white and brown |
| `ForestNature/Rocks` | 43 rocks in three families |
| `ForestNature/Trees` | 20 — 14 leafy, 6 bare |

The two environment packs are kept for the biomes (GD §3): stone and rubble for the Ashen
Reach, arches and stairs for the Drowned Choir's cathedral, rocks and trees for the Bone
Orchard once they are recoloured to its white.

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
- **The environment packs render through two project materials, remapped at the importer.**
  Every model in `Dungeon/` maps its embedded `texture` material to
  `_Project/Materials/Environment/M_Dungeon`, and every model in `ForestNature/` maps `forest` to
  `M_ForestNature`. The embedded ones are URP Lit at smoothness 0.55 and 0.4, which shines like
  plastic; the project's are copies of `M_Knight` at 0.05. Remapped on the importer rather than on
  each renderer, so a piece dragged into any arena is already right, and every piece of one atlas
  shares one material. **An update must re-add the remap to any new model.**

The environment packs otherwise import at the model importer's defaults: a Generic rig,
animation import on, no lightmap UVs. Whether static pieces want no rig, and whether
GD §17.1's baked lighting wants lightmap UVs, is M7-05's to settle with the biome's atlas.

Deleted on import, and safe to delete again on any update: the `gltf/` and `obj/` exports
(they need a package this project has not approved), `samples/`, `contents.png`, and the
duplicate animation FBXs the Adventurers and Skeletons packs both ship — those two files are
byte-identical to the copies in `Animations/Rig_Medium`.

**The environment packs keep only their `fbx(unity)` export**, the one `Skeletons/Props` was
taken from (hash-identical); `fbx/` is a second export of the same models. Their texture is the
copy that ships beside the models; the Dungeon pack's `textures/` copy differs in bytes only.
Also deleted, by rule, so an update deletes them again:

- **Dungeon — the household and the loot:** `bed_*`, `table_*`, `chair`, `stool`, `shelf*`,
  `shelves`, `plate*`, `bottle_*`, `keg*`, `key*`, `chest*`, `trunk_*`, `coin*`, and
  `ceiling_tile`, which a top-down camera never sees. Nothing in the game design uses them.
- **Dungeon — the reserved colours:** every `banner_*` in blue, green, red or yellow, and
  `sword_shield_gold`. GD §16.4 gives red-orange to danger, gold to rewards and cyan to the
  player, and keeps the environment desaturated; white and brown stay.
- **Forest Nature — `Grass_*` and `Bush_*`.** No biome in GD §3 has ground vegetation.

**Looked at on 2026-09-26 and declined, whole:** *Block Bits 1.0* (voxel blocks in saturated
primaries, another visual language); *Medieval Hexagon 1.0* (a hex strategy-map kit —
team-coloured buildings and hex tiles no square arena can use); and a second download of
*Skeletons 1.1*, byte-identical to `Skeletons/`.

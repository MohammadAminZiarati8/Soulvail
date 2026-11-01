# JG-15e — The Warlord's model: the Mossguard grown, war-painted, glaive and big shield

**Size:** M · **Depends on:** — (art only; parallel to 15a–d) · **Branch:** `dev` (direct)
**Design refs:** GD §17.1; colour law (`Palette.Player` cyan is the player's alone, `Palette.Danger` is telegraphs')

## Goal

A ready Warlord body: the Mossguard sculpt on Rig_Medium, repainted, with a bigger shield and a
glaive, built so the owner's own sculpt can replace the mesh without touching anything else.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/Enemies/Mossguard_Warlord.prefab` | — | **New.** Variant of `Mossguard.prefab`: `Model` child, material, props |
| `Art/Enemies/Textures/T_MossguardWarlord_Albedo.png` | — | **New.** The Mossguard's atlas with its swatches remapped |
| `Art/Enemies/M_MossguardWarlord.mat` | — | **New.** The enemy material on the new albedo |
| `Prefabs/Enemies/WarlordGlaive.prefab` | — | **New.** `MossguardSpear.fbx` haft + KayKit `axe_2handed.fbx` head at its tip |
| `Tests/Game/Art/MossguardWarlordModelTests.cs` | Tests.Game | **New** |

## Behaviour

1. **Body:** `Mossguard.fbx` unchanged, under the base's `Model` child; every body renderer is
   under `Model`. Its scale stays 1 — the 1.6× is the data's `BodyScale` (15g: Mossguard's
   1.27 × 1.6 = 2.03).
2. **War paint:** the albedo copies the atlas `Mossguard.fbx` wears, remapping its swatches only —
   skin darker olive, cloth umber, and bone-white stripe swatches where the sculpt's trim was. No
   texel within the cyan hue band (175–200°, saturation > 0.35) and none within ΔE 20 of `Danger`.
3. **Shield:** `MossguardShield.fbx` on the Mossguard's shield socket at 1.35×, wearing the new
   material; the base's `Shield` object, overridden (15f hides it on the shatter).
4. **Glaive:** the base's `Spear` is deactivated; the glaive takes its socket, head on the haft's
   tip, edge along the sweep plane; named `Glaive`. Within the spear's budget plus the axe's.
5. **Sculpt slot:** the body is one `SkinnedMeshRenderer` on Rig_Medium bones. The owner's sculpt,
   when it comes, replaces that renderer's mesh and the albedo; bones, sockets and props stay, and
   nothing in code names the mesh.
6. Budget: body + props within the crowd budget `MossguardModelTests` uses; every vertex weighted.

## Tests

| Test | Given / When / Then |
|---|---|
| `Warlord_IsAVariantOfTheMossguard` / `Warlord_BodyIsOneModelChild` (1) |
| `Warlord_WearsTheWarPaint` / `Warlord_PaintHasNoCyanAndNoDanger` (2) |
| `Warlord_ShieldIsBigger_OnTheSocket` (3) |
| `Warlord_GlaiveOnTheSpearSocket_NoSpear` (4) |
| `Warlord_OneSkinnedBodyOnRigMedium` / `Warlord_SwappedMesh_KeepsProps` (5) |
| `Warlord_StaysInBudget_EveryVertexWeighted` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Scene view, the variant beside a Mossguard at their data scales: reads as the same
   kind, clearly bigger, painted; glaive and shield readable at phone zoom.

## Out of scope

- Animator and views (15f); the data asset and its scale (15g); a new sculpt.

## As built

_Filled at merge._

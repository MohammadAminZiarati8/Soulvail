# MK-11 — The Marksman's look

**Size:** M · **Depends on:** MK-02b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (*a chosen path changes the hero's look*); art ruling (KayKit + generated
models on KayKit rigs; props are props)

## Goal

Choosing a path visibly changes the hero, driven by data so Skirmisher and Echo Archer only add an
asset. The Marksman gains a hood-and-cloak tint, a quiver on the back and a glowing bowstring.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Authoring/PathLook.cs` | Game | **New.** ScriptableObject (block namespace): material swaps per renderer slot, props per socket, bowstring/arrow tint |
| `Game/Views/PathLookView.cs` | Game | **New.** Applies a `PathLook` to the player rig on `PathChosen` and on resume |
| `Data/Paths/MarksmanLook.asset` | asset | **New** |
| `Tests/Game/Views/PathLookViewTests.cs` | Tests.Game | **New** |
| *small edits* | | `PathDefinition` (`_look`), `Data/Paths/Marksman.asset`, the Ranger's player prefab (sockets: `back`, `head` if missing), `ProjectileViews` (arrow trail tint from the look), `RunScope` (registers the view) |

## Behaviour

1. A `PathLook` lists: material overrides by renderer name and slot; props (prefab + socket +
   local pose); a bow tint; an arrow-trail tint. Every entry is optional.
2. **On `PathChosen`** the view applies the chosen path's look to the live hero within the frame,
   with a short cyan shimmer (0.4 s) so the change is noticed; **on resume** it applies silently.
3. Applying is **additive and reversible**: the view keeps what it replaced; a new run starts from the
   plain Ranger.
4. **The Marksman:** hood and cloak a deep forest green (atlas swatch, not a new texture) with a
   cyan trim; a KayKit quiver prop (or the owner's generated one) on `back`; the bowstring carries a
   faint cyan glow; ordinary arrow trails shift a touch brighter. Nothing red; the player stays
   readable as cyan on the dark floor.
5. The hero page and class select show the plain Ranger (a path is per run).
6. A missing prop or socket logs one warning and skips that entry; the rest still applies.

## Tests

| Test | Given / When / Then |
|---|---|
| `Chosen_AppliesLook` / `Resume_AppliesSilently` (2) |
| `NewRun_PlainRanger` (3) |
| `Marksman_LookHasQuiverAndTint` (4) |
| `HeroPage_Unchanged` (5) |
| `MissingSocket_WarnsOnce_RestApplies` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Choose the Marksman: shimmer, then hood/cloak green, quiver on the back, glowing
   string — readable at the game camera's height. Judge against the plain Ranger in a capture.

## Out of scope

- Looks for Skirmisher and Echo Archer (their own tasks add an asset). New sculpts.

## As built

_Filled at merge._

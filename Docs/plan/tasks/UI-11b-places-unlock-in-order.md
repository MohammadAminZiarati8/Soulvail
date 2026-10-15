# UI-11b — Places unlock in order

**Size:** M · **Depends on:** UI-11a · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** GD §3, GD §4.5 · **Ledger rows:** none

## Goal

Each place names the place that opens it, core answers "is this place open?" from the profile, and the Jungle is a place that can be finished.

**The owner's ruling (2026-10-15): every place unlocks when the one before it is finished.** Finished means its final stage was cleared.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ModeSpec.cs` | Core | `UnlockedBy`, `IsListed` |
| `Core/Progression/PlaceUnlocks.cs` | Core | `IsUnlocked`, `Listed` — `ClassUnlocks`' shape |
| `Game/Authoring/ModeDefinition.cs` | Game | `_unlockedBy`, `_isListed` |
| `Tests/Core/Progression/PlaceUnlocksTests.cs` | Tests.Core | the rule rows |
| `Tests/Game/Authoring/ContentValidationTests.cs` | Tests.Game | rule 4 over the shipped catalog (M3-14b: there is no validator class, by rule) |
| *small edits* | | `Jungle.asset` finite, final stage 30, listed; `Descent.asset` unlisted; GD §3 *Biomes* and §4.5 gain the places paragraph |

## Public API

```csharp
// ModeSpec gains (constructor arguments after hasFreePick):
public ContentId UnlockedBy { get; }   // default = open from the start
public bool IsListed { get; }          // shown on the Places tab

public static class PlaceUnlocks
{
    public static bool IsUnlocked(ContentId modeId, in PlayerProfile profile, ContentCatalog catalog);
    public static bool IsFinished(ContentId modeId, in PlayerProfile profile);
    // Listed modes in catalog order, written into `into`; returns the count.
    public static int Listed(ContentCatalog catalog, ContentId[] into);
}
```

## Behaviour

1. **A place with no `UnlockedBy` is open.** Every other place is open once its `UnlockedBy` is in `FinishedModeIds`.
2. **Unlocking is not transitive by accident**: C, opened by B, stays locked while B is unlisted or unfinished, even if A is finished.
3. **`Listed` returns only `IsListed` modes, in the catalog's order**, which is BootScope's list order.
4. **Content validation refuses**: an `UnlockedBy` naming itself (`ModeSpec`'s constructor), and, over the shipped catalog in `ContentValidationTests`, one naming a mode not in the catalog, naming an endless mode (it can never be finished), or a chain that loops.
5. **Data:** the Jungle is finite with a final stage of 30, a Warden stage, so a place ends on a boss kill. It is listed and open. Descent stays in the catalog, unlisted: a switch, not a removal ([focus build](../ROADMAP.md#ui--the-hud-in-the-new-art-a-side-track-the-top-band-first)).

## Tests

| Test | Given / When / Then |
|---|---|
| `IsUnlocked_NoUnlockedBy_True` | rule 1 |
| `IsUnlocked_PreviousFinished_True` · `IsUnlocked_PreviousNotFinished_False` | rule 1 |
| `IsUnlocked_GrandparentFinishedOnly_False` | rule 2 |
| `Listed_SkipsUnlisted_KeepsOrder` | rule 3 |
| `Ctor_UnlockedBySelf_Throws` (Tests.Core, `ModeSpecTests`) | rule 4 |
| `Modes_UnlockedByIsKnown` · `Modes_UnlockedByIsFinite` · `Modes_UnlockChainHasNoLoop` (`ContentValidationTests`) | rule 4 |
| `Jungle_IsFiniteAt30_Listed_Open` · `Descent_IsUnlisted` (Tests.Game, `ModeDefinitionTests`) | rule 5 |

## Out of scope

- Any second place's content. With one place, the chain is just the Jungle.
- Writing `FinishedModeIds`: UI-11c and UI-11d.

## As built

Built to the Files table, with two test edits outside it, both forced by the data change:

- `JungleTests.SharedBlocks` no longer lists `_isEndless` and `_finalStage`; the Jungle now differs from Descent there on purpose.
- `ClassUnlocksTests.Profile_CarriesNoNumberThatAffectsARun` expects two readers of `PlayerProfile`, `ClassUnlocks` and `PlaceUnlocks`, and checks both return only `bool` or `int`.

`isListed` defaults to `true` and `unlockedBy` to `default` in the constructor, so every existing fixture compiles. Both assets carry the new keys explicitly, so `Descent_EveryYamlKeyBindsToAField` binds them. `Listed` throws on a buffer that is too short rather than truncating. The three catalog rows live in `ContentValidationTests` as `Modes_UnlockedByIsKnown`, `Modes_UnlockedByIsFinite` and `Modes_UnlockChainHasNoLoop`.

Suite: 4 130 EditMode, 4 128 passed, 1 failed (the known D3D12 depth row), 1 inconclusive.

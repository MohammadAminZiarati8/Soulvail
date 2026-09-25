# RS-02b — A body per class

**Size:** M · **Depends on:** RS-02a · **Branch:** `rs-02b-a-body-per-class`
**Design refs:** AR §18 (the M2-art rows); GD §16.4 · **Ledger rows:** none

## Goal

A run's player wears the body its class names. The Oathbound, the Gravecaller and the Emberwright
still wear the Knight, and the Ranger (RS-03c) will wear its own body with the bow. Core learns
nothing about models.

## Why this shape

- **Every class plays as the Knight today.** `Player.prefab` builds the Knight in, unpacked, and
  `RunScope` registers one `PlayerAnimatorView` from the scene. No `CharacterSpec` names a body, and
  none should: a model is the Game side's business (AR §3).
- **The pattern exists already.** `EnemyLookBook` maps an archetype id to how it looks. It is built
  at boot from each `EnemyDefinition`, registered at the root, and resolved by `EnemyViews`. A
  `CharacterLookBook` does the same for classes.
- **The id is known only once the container exists.** `PendingRun` is set before the Run scene
  loads, except on a direct Play, where `RunTicker.FallbackCharacterId` picks the catalog's first
  class. So the body is raised in a build callback, and the choice of class is made in one place
  that both the callback and `RunTicker` call.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Authoring/CharacterLook.cs` | Game | **New.** `CharacterLook` and `CharacterLookBook`, `EnemyLook.cs`'s shape (rules 1, 2) |
| `Game/Composition/RunCharacter.cs` | Game | **New.** The one choice of which class a run plays (rule 3) |
| `Tests/Game/Authoring/CharacterLookTests.cs` | Tests.Game | **New.** Rules 1–3 and 5 |
| `Tests/PlayMode/RunBodyTests.cs` | Tests.PlayMode | **New.** Rules 4 and 6, on the Run scene |
| *small edits* | Game | `CharacterDefinition` gains `_body` and `ToLook()`. `BootInstaller` builds and registers the book. `RunScope` raises the body (rule 4) and loses `_playerAnimator`. `RunTicker.FallbackCharacterId` calls `RunCharacter`. `PlayerAnimatorView` and `RangerAnimatorView` find `PlayerView` in their parents (rule 5) |
| *assets* | | `Prefabs/Player/Bodies/Knight.prefab` and `Bodies/Ranger.prefab` (new folder); `Player.prefab` loses `Body` and `PlayerAnimatorView`; `Player_Ranger.prefab` nests `Bodies/Ranger`; the three `Data/Characters` assets name `Bodies/Knight` |

## Public API

```csharp
namespace Soulvail.Game.Authoring;
public readonly struct CharacterLook
{
    public CharacterLook(GameObject body);
    public GameObject Body { get; }                 // null: the run's default body
}
public sealed class CharacterLookBook
{
    public CharacterLookBook(IReadOnlyDictionary<ContentId, CharacterLook> looks);
    public CharacterLook For(ContentId characterId); // an unknown id is the default look
}
// CharacterDefinition: [SerializeField] private GameObject _body;  public CharacterLook ToLook();

namespace Soulvail.Game.Composition;
public static class RunCharacter
{
    /// The pending run's class, or the catalog's first. Throws on an empty catalog, as RunTicker did.
    public static ContentId Choose(PendingRun pending, ContentCatalog catalog);
}
```

## Behaviour

1. **The book is `EnemyLookBook`'s rules:** a default id is refused, a duplicate id is refused, and
   an id with no entry reads as the default look.
2. **`CharacterDefinition.ToLook()` carries `_body` as authored.** An empty `_body` is not an error.
   It is the default.
3. **One choice of class.** `RunCharacter.Choose` returns `PendingRun.CharacterId` when a run is
   pending, otherwise `catalog.Characters[0].Id`. `RunTicker.FallbackCharacterId` becomes a call to
   it, so the class started and the body worn cannot disagree.
4. **The run raises its body before anything starts.** In a `RegisterBuildCallback`, `RunScope`
   chooses the class (rule 3), instantiates its look's body under the player (the `PlayerView`'s
   transform, at identity), and injects it with `InjectGameObject`. A look with no body uses
   `RunScope`'s required `_defaultBody`, `Bodies/Knight`. Exactly one body stands under the player.
   A missing `_defaultBody` throws `MissingReferenceException` in `Configure`, as `_playerView` does.
5. **An animator view lives on its body, and finds the body's `PlayerView` above it.** Both views
   drop `[RequireComponent(typeof(PlayerView))]` and take `GetComponentInParent<PlayerView>()` in
   `Awake` and lazily in `Step`/`Update`. With no `PlayerView` above them they throw in `Start`,
   naming the prefab.
6. **Nothing that plays changes.** A run as the Oathbound, the Gravecaller or the Emberwright looks
   and animates as it did: the same Knight, the same `AC_Player`, the same `PlayerAnimatorView`, now
   on `Bodies/Knight`. `PaletteTests` still finds `PlayerView` and `FocusGlowView` on
   `Player.prefab`'s root.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Book_RefusesADefaultId`, `Book_RefusesADuplicateId` | a bad map / constructed / `ArgumentException` — rule 1 |
| `Book_AnUnknownIdIsTheDefaultLook` | an empty book / `For("character.x")` / `Body` null — rule 1 |
| `Definition_CarriesItsBody`, `Definition_NoBodyIsTheDefault` | a definition with and without `_body` / `ToLook()` / as authored — rule 2 |
| `Choose_ThePendingClassWins`, `Choose_NoPendingRunIsTheFirstClass`, `Choose_AnEmptyCatalogThrows` | — rule 3 |
| `Shipped_EveryClassNamesTheKnightBody` | the three class assets / — / `Bodies/Knight` — rule 6 |
| `View_FindsPlayerViewInItsParent` (both views) | a body under a `PlayerView` / `Step` / reads its velocity — rule 5 |
| `Run_WearsTheBodyItsClassNames` (PlayMode) | Boot → Menu → a run as the Gravecaller / scene up / one body under the player, `Bodies/Knight`, its view injected — rules 4, 6 |
| `Run_ADirectPlayWearsTheFirstClassesBody` (PlayMode) | `Run.unity` loaded with no pending run / — / the Oathbound's body — rules 3, 4 |

## Manual verification (Editor / device)

1. **[Editor]** Play a run as each of the three classes. *Expected: the Knight, animating exactly as
   before.*
2. **[Editor]** Play `RangerShowcase.unity`. *Expected: unchanged. Its Ranger is now
   `Bodies/Ranger` nested in `Player_Ranger`.*

## Out of scope

- **The Ranger in class select.** RS-03c names `Bodies/Ranger` on its class asset.
- **An arrow per shooter.** RS-02c.
- **A body per class for the other three.** They keep the Knight until M7's art rulings give them
  their own.

## As built

_6 000 bytes or fewer, measured._

**Deviation 1, the Tests table over the Public API: `PlayerAnimatorView.Step(float dt)`.** The
`View_FindsPlayerViewInItsParent` row steps both views, and only `RangerAnimatorView` had a `Step`.
`Update` now calls `Step(Time.deltaTime)`. A zero, negative or non-finite step does nothing, where
`Update` used to write the unchanged speed. The legs play the same.

**Deviation 2, rule 4's "at identity" is position and rotation.** A body's scale is the model's:
0.6615 on the prefab root, where the Knight carried it as `Player.prefab`'s `Body`. `RaiseBody`
leaves it as authored. The instance is named after its prefab (`Knight`, not a clone), which the
PlayMode rows read.

**Deviation 3, `RunTicker.FallbackCharacterId` is gone rather than a call to `RunCharacter`.** Its
call site is `RunCharacter.Choose(_pending, _catalog)`. A wrapper would repeat the `IsSet` test.
Its remarks moved to `Choose`.

**The two bodies.** `Bodies/Knight` is `Player.prefab`'s `Body` saved as it stood: unpacked since
M2-art, 78 objects out plus the view in. `Player.prefab` changed nowhere else. `Bodies/Ranger` is a
variant of KayKit's `Characters/Ranger.prefab` carrying RS-02a's overrides (scale, `AC_Ranger`,
culling, the bow in `handslot.l`) and the view. `Player_Ranger` nests it as `Ranger`.

**Rule 5's messages.** Both `Start` guards name the body (*"on 'Knight'"*). The not-injected
message told the reader to drag the object onto `RunScope`'s Player View field. It now says
`RunScope` injects the body it raises.

**A row beyond the table:** `View_WithNoPlayerViewAboveThrowsInStart`, rule 5's second half.

**Edits outside the table:**
- `Run.unity`: `_defaultBody` dressed, `_playerAnimator` gone.
- `RangerShowcase.unity`: the scope's Animator View points at the nested view. Manual step 2
  needs it. Saving also added URP light data to two lights and dead AABB overrides on a bow fileID
  `Player_Ranger` no longer has. Both were dropped, so the diff is the reference plus
  `_shootWhileMoving`'s first serialisation.
- `RunEndPresenterTests.RunScope_RefusesToComposeWithoutTheScreen` dresses `_defaultBody`. The new
  guard sits above rule 7's, the third time that list has grown (M5-05a, M5-05b). It was the one red
  row of the first full pass.
- `RangerSandboxTests.Ranger_HoldsItsBowInTheLeftHandslot` finds the view in children.
  `RangerAnimatorViewTests` adds the `PlayerView` the dropped `RequireComponent` used to add.
  `PlayerAnimatorViewTests`' setup comment is corrected.
- `RangerSandboxScope`'s tooltip and guard message name the nested body.
- The three class assets were re-serialised when `_body` was written. Fields added since they were
  last saved, such as `_weaponFiresWhileMoving` and the minion and pool blocks, are now written at
  the values Unity already loaded. No number a class plays with moved.

**The Gravecaller row sets `PendingRun` where `ClassSelectPresenter.OnCardChosen` would.** On a
profile without the Gravecaller its card buys rather than descends. The direct-Play row expects
`RunInstaller`'s seed warning, which is also the evidence that nothing was pending.
`RunBodyTests.AssertTheRunWearsTheKnight` asserts the Knight's name, `AC_Player` and
`PlayerAnimatorView`. RS-03c's Ranger case generalises it.

**How it was checked.** Red check A, the views back on `GetComponent`: 46 / 1, the view row alone,
*"Expected: 2 But was: 0"*. C, the ticker on the catalog's first class: the Gravecaller row alone,
*"Expected: character.gravecaller But was: character.oathbound"*. B, no `InjectGameObject`: both
PlayMode rows, on the view's own guard naming `'Knight'`. `RangerSandboxTests` passed every pass.

**Not done here:** the spec's two Editor walkthroughs are the owner's to play.

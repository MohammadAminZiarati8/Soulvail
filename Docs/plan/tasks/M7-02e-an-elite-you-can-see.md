# M7-02e — An Elite you can see

**Size:** M · **Depends on:** M7-02f · **Branch:** `m7-02e-an-elite-you-can-see`
**Design refs:** GD §8.3, §11.3, §15, §16.2, §16.4, §17.1; AR §18.2; ADR-0012 · **Ledger rows:** [12](../ROADMAP.md#carry-forward-into-m7) — opened by M7-00b and named here, not discharged (rule 8)

## Goal

An Elite wears GD §8.3's outline from the frame it arrives. The one you are aiming at names its
affixes. A Warded one shows its ward while the ward is up, and a Siphoning one draws a line to you each
time it drinks. Everything core has said about an Elite since M7-02a is now on screen.

## What core publishes, and who has been waiting for it

Every reader here was named as the stated exception when its event was written:

- **`EnemySpawned.IsElite`** (M7-02a) — already read by `EnemyHealthBar`, whose Elite bar never fades
  (M3-13b).
- **`EnemySpawned.Affix` / `SecondAffix`** (M7-02c rule 4, M7-02f rule 6).
- **`EnemyWardChanged`** (M7-02c rule 7), published on the ward's edges only.
- **`EnemyHealed`** (M7-02c rule 11) — without a reader, a Siphoning Elite's bar shows it lower than
  it is.
- **`PlayerDrained(sourceId, …)`** (M7-02c rule 10), already read by the HUD. A source of 0 is a
  pool (M7-02d).
- **M7-02b rule 9's open question**: whether a spawn ring should say *"Elite"*. Rule 2 answers it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EliteMarker.cs` | Game | **New.** On every enemy body: the outline, the ward, the siphon's line, and the affix label (rules 1, 3–5) |
| `Game/Authoring/AffixNameBook.cs` | Game | **New.** Each affix id → its `LocKey`, and a cached label per pair — `EnemyLookBook`'s pattern (rule 3) |
| `Tests/Game/Views/EliteMarkerTests.cs` | Tests.Game | **New.** Every rule below |
| *small edits* | Game, Prefabs | `Game/Views/EnemyViews.cs` — binds the marker from `EnemySpawned`, and releases it; `Game/Views/EnemyHealthBar.cs` — `EnemyHealed` moves the fill (rule 6); `Game/Composition/BootInstaller.cs` — builds the name book from every converted mode's `Affixes` and registers it; `Prefabs/Enemies/Enemy.prefab` — *Outline* and *Ward* under *Mesh*, a *Tether* line, a *Label* under *HealthBar*, and the component; `Materials/M_EliteOutline.mat`, `M_Ward.mat`, `M_Tether.mat` — **new**, URP Unlit (rule 1); `Tests/Game/Presentation/PaletteTests.cs` — `Views_CarryNoSerializedColour` walks the marker |
| *ripple* | Tests.Game, Tests.PlayMode | `EnemyHealthBarTests.Prefab_IsDressed` and `PoolingLifecycleTests.EnemyView_ComesBackCleanAfterAFullLife` see the new children and must still pass unedited, which is what they are for |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Views
{
    /// <summary>
    /// What makes an Elite legible: an outline, a ward, a siphon's line, and — on the target — its
    /// affixes by name. Every visual is dormant on a plain body.
    /// </summary>
    public sealed class EliteMarker : MonoBehaviour
    {
        /// <param name="label">The localised affix names, or null for none. From <see cref="Authoring.AffixNameBook"/>.</param>
        public void Bind(int id, bool isElite, string label);
        public void Unbind();

        public bool IsOutlined { get; }
        public bool IsWardShown { get; }
        public bool IsTetherShown { get; }
        public bool IsLabelShown { get; }
    }
}

namespace Soulvail.Game.Authoring
{
    public sealed class AffixNameBook
    {
        /// <param name="names">Every affix any shipped mode can roll, id → name key.</param>
        public AffixNameBook(IReadOnlyDictionary<ContentId, LocKey> names, ILocalizer localizer);

        /// <summary>
        /// "Warded", or "Warded · Splintered" — localised, built once per pair and cached, so a second
        /// rental of the same pair allocates nothing. Null when <paramref name="first"/> is default.
        /// </summary>
        public string LabelFor(ContentId first, ContentId second);
    }
}
```

## Behaviour

1. **An Elite wears an outline, and it is an inverted hull rather than a new shader.** The *Outline*
   child is the built-in capsule at 1.12× the body's mesh, on `M_EliteOutline`. That material is URP
   Unlit with *Render Face: Back* (`_Cull` 1), so only the rim that shows past the body is drawn, which
   is an outline.
   - **The project has no custom shader and gains none.** A renderer feature or a shader graph would
     be a pipeline change for one effect, and GD §11.3's *"one shared material"* is kept by adding one
     more shared material.
   - **It sits under *Mesh***, so M7-01d's arrival grows it with the body.
   - `EnemyViews` enables it on `EnemySpawned.IsElite`, and `Unbind` disables it.
   - Unlit reads as emissive, and the mid and high tiers' bloom lifts it further.

   **Its colour is `Palette.Essence` gold, and it is the only reserved colour that is true of an
   Elite.** GD §16.4 reserves gold for *"Essence, rewards"*, and an Elite is the one body that pays
   Essence: 15 at the clear, GD §15. Danger means an attack, cyan means the player, violet means the
   Veil, and an Elite is none of those. **This is the look the owner should judge in Play** (manual
   step 1). If the gold reads as a pickup, the next candidate is bone-white, and nothing else here
   moves.
2. **A spawn ring does not say "Elite".** That is M7-02b rule 9's question, answered no. A danger decal
   with gold in it would say two things at once, and the outline arrives with the body a second later.
   `SpawnRing_NeverSaysElite` pins it.
3. **The affixes are named on the target, and nowhere else.** The *Label* is a TMP text under the
   Elite's bar, sized in dp (Traps §9). It shows the affix names only while this body is the current
   target (`TargetChanged.Id`) and carries at least one affix, and hides on the next `TargetChanged`.
   **Not on every Elite**: at depth, M7-02b makes every body of a capped wave an Elite, and twenty-eight
   labels would be exactly the noise GD §16.2 refuses for bars.

   Names come from `AffixNameBook`, which `BootInstaller` builds from every converted mode's
   `ModeSpec.Affixes`. Those are core specs that already carry `NameKey`, so no definition is read
   twice. The book composes each pair's label once, through the localiser, and caches it, so the
   string is a boot-and-first-meeting cost rather than a spawn cost (AR §14). A locale change
   mid-run is M6-10 rule 6's known gap, inherited rather than solved.
4. **A Warded body shows its ward while it is up.** The *Ward* child is the built-in sphere around the
   body on `M_Ward` (URP Unlit, transparent), in `Palette.Guard` at 0.25 alpha. It is toggled by
   `EnemyWardChanged` for this id and hidden by `Unbind`. **Guard, because a warded body's hits shrug in
   Guard** (M7-01d rule 6), so the bubble and the flash it makes are one colour and one lesson.
5. **A Siphon draws its line on every pulse.** On a `PlayerDrained` whose `SourceId` is this body, the
   *Tether* line is drawn from the body to the player's position for 0.2 s, in `Palette.Danger`.
   - **Danger**, because it is damage reaching the player (GD §16.4's *"hazard"*).
   - The endpoint is `IRunSession.State.PlayerPosition`, a narrow read (AR §18.2), updated in
     `LateUpdate` while the line is shown.
   - **A source of 0 draws nothing**, since a pool has no body to draw from, and another body's drain
     is ignored.
6. **An Elite's bar follows a heal.** `EnemyHealthBar` subscribes to `EnemyHealed` and sets the fill
   to its fraction. Without it, a Siphoning Elite that drank back to full reads as wounded until its
   next hit.
7. **Hasted, Volatile and Splintered are named and nothing more until they act.** Hasted reads as speed.
   Volatile's pool and Splintered's shards are drawn when they land (M7-02d rule 12, and the Spitter's
   arc). The label (rule 3) is their tell before the death. A glyph per affix is art, and M7-00e's.
8. **What this does to a screen at depth is measured, not argued, and it is [ledger row 12](../ROADMAP.md#carry-forward-into-m7).**
   By about stage 40 on the mid tier (31 on the low, 60 on the high), M7-02b's promotion has made every body in a capped wave an Elite,
   so every body wears an outline and GD §16.2's never-fading bar. That is the density §16.2 was
   written against. **This task ships §16.2 and §8.3 as written**, since the target-only label is
   already rule 3's concession. The number that decides whether more is needed is M7-08's, from its
   instrument (outlined bodies and persistent bars per frame at stage 40). The remedy would be GD
   §16.2's own settings toggle, which is M8-02's.
9. **Nothing on a frame path allocates.** Each toggle is a `SetActive` on an edge, the line writes two
   positions into a `LineRenderer` it owns, and the label's string is cached (rule 3).

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Outline_AnEliteWearsIt` | `EnemySpawned(isElite: true)` / — / `IsOutlined` — rule 1 |
| `Outline_APlainBodyDoesNot` | `isElite: false` / — / not outlined — rule 1 |
| `Outline_ARecycledBodyTakesItOff` | an Elite released and rented plain / — / not outlined — rule 1 |
| `Outline_IsGoldAndBackFaced` | `M_EliteOutline.mat` / — / URP Unlit, `_Cull` 1, `_BaseColor` `Palette.Essence` — rule 1 |
| `Outline_GrowsWithTheArrival` | a fresh Elite rental / 0.1 s / the outline's world scale tracks the mesh's — rule 1 |
| `SpawnRing_NeverSaysElite` | a `SpawnTelegraphed` for a body that will be an Elite / — / the ring is `Palette.Danger`, nothing gold — rule 2 |
| `Label_ShownOnlyOnTheTarget` | an affixed Elite / `TargetChanged` to it, then away / shown, then hidden — rule 3 |
| `Label_NamesBothAffixes` | Warded + Splintered / targeted / the English label reads *"Warded · Splintered"* — rule 3 |
| `Label_AnUnaffixedEliteShowsNone` | a stage-8 Elite / targeted / not shown — rule 3 |
| `Label_IsLocalised` | the pseudo-locale / targeted / the pseudo names — rule 3, ADR-0012 |
| `NameBook_BuildsFromEveryModesPool` | Descent / boot / all five ids resolve — rule 3 |
| `NameBook_ASecondRentalAllocatesNothing` | the same pair twice / `AllocationAssert.None` on the second / zero — rules 3, 9 |
| `Ward_ShownOnItsEdges` | `EnemyWardChanged(id, true)`, then `false` / — / shown, then hidden; another id's edge leaves it — rule 4 |
| `Ward_IsGuardColoured` | `M_Ward.mat` / — / `Palette.Guard` at 0.25 alpha — rule 4 |
| `Ward_ARecycledBodyHidesIt` | shown, then released / — / hidden — rule 4 |
| `Tether_DrawnOnAPulseFromItsSource` | `PlayerDrained(sourceId: id)` / — / shown, from the body to the player — rule 5 |
| `Tether_GoneAfterItsTime` | / 0.2 s / hidden — rule 5 |
| `Tether_APoolsDrainDrawsNone` | `PlayerDrained(sourceId: 0)` / — / every marker's line hidden — rule 5 |
| `Tether_IsDanger` | `M_Tether.mat` / — / `Palette.Danger` — rule 5 |
| `Bar_FollowsAHeal` | an Elite bar at 0.4 / `EnemyHealed(id, 10, 0.7)` / fill 0.7 — rule 6 |
| `EliteMarker_AllocatesNothingAfterTheFirstRental` | 1 000 rentals, ward edges and pulses / — / zero — rule 9 |
| `Prefab_CarriesTheMarkerDormant` | `Enemy.prefab` / — / the component and four children present, every visual inactive — rules 1, 3–5 |

**Guard rows are implied, not listed.**

## Manual verification (Editor / device)

1. **[Editor]** Stage 8, the first Elite. *Expected: a gold rim around it from the moment it lands.
   **The owner's eye decides whether gold reads as "worth killing first" or as a pickup** — rule 1
   names the fallback.*
2. **[Editor]** Stage 12, target an affixed Elite. *Expected: its affix names under its bar, only while
   it is the target.* A Warded one near Husks: a steel bubble that pops when the Husks die. A
   Siphoning one within ten metres: a red-orange line to you twice a second, and its bar creeping up.
3. **[device]** The label at 400 dpi — deferred with [M7 row 1](../ROADMAP.md#carry-forward-into-m7).

## Out of scope

- **A glyph per affix.** Rule 7; art, M7-00e.
- **Turning the persistent bar or the outline off at depth.** Rule 8; ledger row 12's instrument
  first, then M8-02's toggle.
- **An Elite telegraph ring.** Rule 2.

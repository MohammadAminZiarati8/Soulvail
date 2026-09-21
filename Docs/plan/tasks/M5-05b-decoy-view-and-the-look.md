# M5-05b — The corpse, twelve font values, and one keyword that dims six prefabs

**Size:** M · **Depends on:** M5-03, M5-05a · **Branch:** `m5-05b-decoy-view-and-the-look`
**Design refs:** CH §3.2; GD §11.3, §13.1, §16.4; AR §8, §11.5, §18.1 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m5), [3](../ROADMAP.md#carry-forward-into-m5)

## Goal

Everything in M5 that a person has to look at and judge, in one review: the corpse a Shroudstep
leaves, the four HUD labels that have been 66 % of Android's floor since M3-10a, and the material
keyword that has made every telegraph, both boss hazards, the beat shell, Bulwark and the Consecrate
zone draw at full brightness whatever their alpha said.

## Why these three are one task

They are the milestone's whole *look* review, and two of them can be judged the day this merges
while the third cannot. **Rows 1 and 3 are visible in an Oathbound run right now** — the slot labels
are on screen in every run, and `M_TelegraphRing.mat` is shared by `VFX_ConsecrateZone` and
`VFX_Bulwark`, which are Oathbound content — so the before and the after can be held side by side by
the only person who ever will. **The decoy cannot be seen at all until [M5-07](M5-07-class-select-screen.md)**
makes the Gravecaller selectable (rule 10), and that is exactly why it belongs beside two things
that can: a PR whose every claim was unverifiable would be a PR nobody could review.

[M5-05a](M5-05a-wight-views-and-concurrency.md) took the other half — the frame, the census, and
[row 4](../ROADMAP.md#carry-forward-into-m5) — because that half is an ordering argument and this
one is a judgement about pixels.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/DecoyView.cs` | Game | The corpse: a marker with no collider and no life of its own — **block namespace** ([Traps §5](../../Traps.md)) |
| `Game/Views/DecoyViews.cs` | Game | Two decoys' worth of census, rented on `DecoySpawned` and returned on `DecoyExpired` |
| `Tests/Game/Views/DecoyViewsTests.cs` | Tests.Game | The two events, the capacity, the clear, and what a decoy is not |
| `Prefabs/Vfx/VFX_Decoy.prefab` · `Prefabs/UI/Hud.prefab` · `Materials/M_TelegraphRing.mat` | — | The corpse, twelve serialized values, one keyword. Assets — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *ledger* | Tests.Game | `Tests/Game/Presentation/SkillBarPresenterTests.cs` gains [row 1](../ROADMAP.md#carry-forward-into-m5)'s rows (rules 5–7); `Tests/Game/Views/TelegraphRingsTests.cs` gains [row 3](../ROADMAP.md#carry-forward-into-m5)'s sweep (rules 8, 9) |
| *small edits* | Game | `RunScope` gains `_decoyPrefab` and registers the census; `RunTicker` takes it, for the subscription guarantee and nothing else (rule 3) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Game/Views/DecoyView.cs
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One corpse standing in the arena. It has no collider, no health bar and no Step: a decoy
    /// does not move, does not fade and cannot be hit (M5-03 rule 7), so there is nothing per-frame
    /// for this to own — rule 2.
    /// </summary>
    public sealed class DecoyView : MonoBehaviour, IPoolable
    {
        public const int Unbound = 0;

        public int Id { get; }
        public bool IsBound { get; }
        public Vector3 Position { get; }

        public void Bind(int id, Vector3 position);
        public void Unbind();

        public void OnSpawn();
        public void OnDespawn();
    }
}

// Game/Views/DecoyViews.cs
namespace Soulvail.Game.Views
{
    public sealed class DecoyViews : IDisposable
    {
        /// <param name="prewarm">LureSystem.Capacity — two, and there is never a third.</param>
        public DecoyViews(
            IObjectResolver resolver,
            DecoyView prefab,
            Transform parent,
            DomainEventHub hub,
            int prewarm = 0);

        public int Count { get; }

        public bool TryGet(int id, out DecoyView view);

        public void Dispose();
    }
}
```

## Behaviour

1. **The corpse is a body, not a decal.** `VFX_TelegraphRing` and `VFX_ConsecrateZone` are flat
   quads on the floor because what they mark is *ground*; a decoy is a thing an enemy walks at and
   swings at, and a flat patch would read as a hazard — which is `Palette.Danger`'s reserved meaning
   and the one colour GD §16.4 says is *"used for nothing else, ever."* So `VFX_Decoy.prefab` is the
   shared body at the player's silhouette, tinted `Palette.Player` and drawn at **50 %** alpha —
   ours, and the number the keyword in rule 8 finally makes mean something.
2. **It has no `Step`, and the absence is the design.** `ProjectileViews`, `TelegraphRings`,
   `ZoneViews` and `BossViews` are all stepped with `snapshot.Dt` because each is interpolating
   something core is also counting down. **A decoy interpolates nothing**: M5-03 rule 1 says
   *"nothing about one changes after it is dropped"*, so the view's whole life is *appear* and
   *disappear*, and the two moments arrive as `DecoySpawned` and `DecoyExpired`. A `Step` here would
   be a clock running beside core's for no reason, and the class that had one would eventually be
   given something to do with it.
3. **`RunTicker` takes it and never calls it.** `SaveWriter` and `ShardWriter`'s bargain, for its
   reason (M4-05b rule 6): being on this object's dependency chain is what guarantees the census is
   subscribed before `Start` can let core drop anything, and a VContainer `Scoped` registration
   nothing resolves is never constructed at all. The parameter is discarded with `_ =` and the
   constructor's own remark says why, because a field assigned and never read is what the compiler is
   right to object to.
4. **The decoy expires by event, never by a timer of its own.** `DecoyExpired` arrives from
   `LureSystem.Tick`, which runs above `EnemySystem.Ingest` (M5-03 rule 10), so the body is gone on
   the tick the taunt stops rather than a frame later. `LureSystem.Clear` publishes nothing (M5-03
   rule 9), so **a stage boundary leaves a corpse standing** unless the census is told — it is, by
   `Dispose`, and the window is one scene. A row pins that a cleared stage does not orphan a body.

### Ledger row 1 — twelve serialized values, and the word that has to fit in them

5. **The four slot labels go from 18 pt to 28 pt, and it is three fields each, not one.** The
   measurement is M4-07's and the mechanism is M5-00a's correction: the objects are
   `S1`–`S4`'s `Label` children on `Hud.prefab`, each carrying **`m_fontSize: 18`,
   `m_fontSizeBase: 18`, `m_enableAutoSizing: 1`, `m_fontSizeMin: 8`, `m_fontSizeMax: 18`**. Setting
   `m_fontSize` alone changes nothing that survives the first layout pass, because auto-sizing
   rewrites it from the base against the max. **So the edit is `m_fontSize`, `m_fontSizeBase` and
   `m_fontSizeMax` on four objects — twelve values — all to 28**, which at the project's reference
   density is **12.3 dp**, the smallest value that clears Android's 12 sp floor inside a 60 dp
   circle. At 18 pt they sit at 7.9 dp, 66 % of the floor.
6. **`m_fontSizeMin` stays at 8, and a test is what makes that safe.** Raising the floor to 28 would
   turn auto-sizing off, and a word too long for its rect would then *overflow* the circle rather
   than shrink inside it — which is a different wrong answer, not a right one. Leaving it at 8 keeps
   the shrink, so the honest guard is a row rather than a value: **`Slots_TheShippedWordsFitAtTwentyEight`**
   measures each of the two shipped Active names against the label's rect with
   `TMP_Text.GetPreferredValues` at 28 pt and fails if either needs to shrink. *"Consecrate"* is the
   longest thing the build can put there, at ten characters; if it does not fit, [row 1](../ROADMAP.md#carry-forward-into-m5)'s
   ruling is binding and the answer is **a shorter `LocKey` in `English.asset` or M7's icon, never a
   smaller font** — the change that put this row here in the first place. A future name that will not
   fit reddens this row instead of quietly rendering at 3.5 dp.
7. **Nothing else on `Hud.prefab` moves.** M4-07 measured eight text elements and seven clear the
   floor — run-end title 42.4 dp, Shard figure 28.3, its labels and figures 21.2, its exit hint 14.1,
   HUD level 17.7, HP readout 15.9, Overflow toast 15.0. **The redesign is not this task's and has
   not moved**: every taken node shown, an Auto skill marked by something orbiting it, still blocked
   on M7's icons, sequencing **icons → readout → rotation**, and the owner's standing ruling since
   the `m3` tag is that it is neither M4's nor M5's. This task closes the *legibility* half of row 1
   and says out loud that the other half is untouched.

### Ledger row 3's one finding — the keyword

8. **`_ALPHAPREMULTIPLY_ON` goes on `M_TelegraphRing.mat`, and it changes six prefabs at once.**
   The material carries `_Blend: 1`, `_SrcBlend: 1`, `_DstBlend: 10` and `_Surface: 1` — premultiplied
   alpha — with `m_ValidKeywords: [_SURFACE_TYPE_TRANSPARENT]` and nothing else, so URP's shader never
   takes the premultiply branch and **every surface using it draws at full brightness whatever its
   alpha says**. Its GUID is referenced by `VFX_TelegraphRing`, `VFX_Shockwave`, `VFX_Fissure`,
   `VFX_BossBeat`, `VFX_Bulwark` and `VFX_ConsecrateZone`. **One keyword therefore dims the whole VFX
   language in one edit**, which is why it is here rather than folded into a task whose reviewer has
   no reason to look, and why the manual steps put a before and an after side by side.
9. **The sweep is a row, so a seventh reader shows up.** `TelegraphRingsTests` gains
   `Telegraph_MaterialIsPremultiplied` — the keyword is present — and
   `Telegraph_SixPrefabsShareOneMaterial`, which walks `Prefabs/Vfx/` and asserts exactly those six
   reference the GUID. It lives there rather than in `ContentValidationTests` because the material is
   the telegraph's and that fixture already owns the decal; the count is the point, so a seventh
   prefab joining the family is a considered diff rather than a silent one.
10. **The corpse still cannot be seen in play, and the two ledger rows can.** No run this build can
    start plays a Gravecaller, so nothing drops a decoy (M5-02 rule 9, M5-05a rule 12) — the first
    eye on one is M5-07's. The font sizes and the keyword are visible in the next Oathbound run, and
    that asymmetry is the argument for the Files table rather than an awkwardness in it.
11. **Nothing here allocates on the frame path.** A pool of two, a dictionary keyed by `int`, no
    `Step`, no per-frame work of any kind.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Decoy_SpawnRentsAndBinds` | a `DecoySpawned` at (5, 0, 5) / published / one body, at that point, bound to the event's id |
| `Decoy_ExpiryReturnsIt` | one standing / `DecoyExpired` / `Count` 0, and the next rental is the same instance |
| `Decoy_TwoStandAtOnce` | two spawns / — / two bodies — `LureSystem.Capacity`, and the pool never grows past it |
| `Decoy_HasNoCollider` | `VFX_Decoy.prefab` / loaded / no `Collider` anywhere on it — M5-03 rule 7, so nothing can be swept into a corpse |
| `Decoy_IsNotInTheEnemyColliderIndex` | one standing / `EnemyViews.TryGetId` over every collider in the scene / it is not there — the swing cannot reach it |
| `Decoy_IsThePlayersCyanAtHalfAlpha` | the prefab / loaded / `Palette.Player`, alpha 0.5, and **not** `Palette.Danger` — rule 1 |
| `Decoy_HasNoStep` | `DecoyViews` / its public surface / no `Step`, and `RunTicker` does not call it — rule 2, asserted so a later task has to argue with this row |
| `Decoy_ClearLeavesNoOrphan` | two standing / the census disposed / both bodies destroyed — rule 4 |
| `Decoy_IsLinkedToAMonoScript` | the prefab / loaded / `DecoyView` resolves — [Traps §5](../../Traps.md) |
| `Slots_LabelsAreTwentyEightPoint` | `Hud.prefab`'s four `Label` objects / read / `m_fontSize`, `m_fontSizeBase` and `m_fontSizeMax` are **28** on all four — **twelve values**, rule 5 |
| `Slots_AutoSizingIsStillOn` | the same four / read / `m_enableAutoSizing` 1 and `m_fontSizeMin` 8, unchanged — rule 6, pinned so the floor is a decision rather than an oversight |
| `Slots_TheShippedWordsFitAtTwentyEight` | *Bulwark* and *Consecrate* off `English.asset`, against the label's own rect at 28 pt / `GetPreferredValues` / both fit without shrinking — **rule 6, the row that makes the floor safe** |
| `Slots_ClearTheAndroidFloor` | 28 pt at the project's reference density / — / **12.3 dp**, above 12 sp, and the arithmetic is in the row rather than in a comment — [ledger row 1](../ROADMAP.md#carry-forward-into-m5) |
| `Hud_NothingElseMoved` | every other `TMP_Text` on `Hud.prefab` / read / the sizes M4-07 measured, unchanged — rule 7 |
| `Telegraph_MaterialIsPremultiplied` | `M_TelegraphRing.mat` / read / `m_ValidKeywords` holds `_ALPHAPREMULTIPLY_ON` beside `_SURFACE_TYPE_TRANSPARENT` — rule 8 |
| `Telegraph_SixPrefabsShareOneMaterial` | `Prefabs/Vfx/` / walked / exactly `VFX_TelegraphRing`, `VFX_Shockwave`, `VFX_Fissure`, `VFX_BossBeat`, `VFX_Bulwark` and `VFX_ConsecrateZone` reference it — rule 9 |
| `Telegraph_BlendStateIsUnchanged` | the material / read / `_Blend` 1, `_SrcBlend` 1, `_DstBlend` 10, `_Surface` 1 — the keyword is the edit and the blend is not |

**Guard rows are implied, not listed:** a null resolver, prefab or hub to the census; a negative
`prewarm`; a `DecoyExpired` for an id nothing bound.

## Manual verification (Editor / device)

1. **[Editor]** Play a run and look at S1–S4 the moment an Active is granted. *Expected: the word is
   legible at arm's length in the Editor and clearly larger than it was; nothing overflows its
   circle. This is [row 1](../ROADMAP.md#carry-forward-into-m5)'s legibility half, and the Editor can
   only say **larger**, never **large enough** — `Screen.dpi` reads 120 here against a phone's 400
   ([Traps §9](../../Traps.md)).*
2. **[Editor]** Take Consecrate and drop below 60 % HP, then take Bulwark and stand in front of a
   Spitter. *Expected: the zone and the shield are visibly **dimmer** than in `m5-05a` — translucent
   rather than solid. Screenshot both before and after the keyword; this is the whole of
   [row 3](../ROADMAP.md#carry-forward-into-m5)'s finding and it is a look change across six prefabs
   (rule 8).*
3. **[Editor]** Reach stage 5 and watch a shockwave, a fissure and the beat shell. *Expected: the
   same dimming, on the two hazards and the shell. If any of the six now reads as **too faint to see
   in time**, that is a tuning answer on the prefab's alpha and not a reason to take the keyword back
   — the keyword makes alpha mean what it says, which is what lets the alpha be tuned at all.*
4. **[Editor]** Play a run without touching anything else. *Expected: identical to `m5-05a` —
   no decoy, no Wight, rule 10.*
5. **[device]** The whole of steps 1–3 at six inches, plus GD §11.3's fill-rate ceiling with a ring,
   eight cracks, a zone and a telegraph all now blending correctly for the first time.
   [Row 3](../ROADMAP.md#carry-forward-into-m5).

## Out of scope

- **The Wight's body and the frame step it costs.** [M5-05a](M5-05a-wight-views-and-concurrency.md).
- **The HUD redesign** — every taken node shown, an Auto skill marked by something orbiting it.
  Rule 7: blocked on M7's icons, and the owner's standing ruling puts it in neither M4 nor M5.
- **The 24 dp auto-cast cells**, which hold neither an icon nor a word. The one reader localisation
  cannot reach; M7, on the [parking lot](../ROADMAP.md#parking-lot).
- **Retuning any of the six prefabs' alphas.** Rule 8 makes alpha mean what it says; what each
  surface *should* be is manual step 3's finding and a number on a prefab, not this task's.
- **The two player cyans** — `#22D3EE` against `ReticleView`/`FocusGlowView`/`PlayerView`'s `#4CE6FF`.
  A [parking-lot](../ROADMAP.md#parking-lot) line since M3-13a, promoted by M7's art pass.
- **A circle texture for the five "rings" that are squares.** `VFX_TelegraphRing` is an untextured
  Quad; one texture fixes all five and it is art, M7 (PROGRESS → Known issues).

## As built

**Nine deviations. Three change something, and the largest is that this task's own probe refuted
[ledger row 3](../ROADMAP.md#carry-forward-into-m5)'s ruling before implementing it.**

1. **[Ledger row 3] The keyword does not exist, and rules 8 and 9 are inverted.**
   `Universal Render Pipeline/Unlit` declares **no `_ALPHAPREMULTIPLY_ON`** in Unity 6.3 — its
   keyword space holds `_SURFACE_TYPE_TRANSPARENT`, `_ALPHATEST_ON` and `_ALPHAMODULATE_ON`, and no
   premultiply branch. `EnableKeyword` accepted it, wrote it to **`m_InvalidKeywords`**, logged
   nothing, and left `IsKeywordEnabled` answering false; the render was byte-identical. **Rule 8's
   fix, applied exactly as written, would have shipped a green suite and an unchanged game.** The
   row's *diagnosis* was right: `_SrcBlend: One` over a straight-colour fragment contributes rgb at
   full strength while halving only the background. **The fix is `_Blend: 1 → 0` and
   `_SrcBlend: 1 → 5`** — two fields, which is the entire delta between this material and
   `M_Reticle.mat`, the same shader as written by URP's own Inspector. The owner ruled it in.
   `Telegraph_MaterialIsPremultiplied` → **`Telegraph_AlphaMeansWhatItSays`**, and
   `Telegraph_BlendStateIsUnchanged` → **`Telegraph_BlendStateIsAlpha`**, which now asserts the
   keyword is **absent** — its presence would mean somebody re-applied the ruling without reading
   the row. Filed as [Traps §1](../../Traps.md) and [§9](../../Traps.md).
2. **`Materials/M_DecoyCyan.mat` is a new asset the Files table does not list.** Rule 1 asks for
   50 % alpha and no shipped material was both translucent and the corpse's: `M_BoneGrey` and
   `M_PlayerCyan` are `_Surface: 0`, so the tint would have drawn solid (Traps §9's first bullet),
   and `M_TelegraphRing` is refused by rule 9's own count — `VFX_Decoy` sits in `Prefabs/Vfx/`, so
   sharing it would redden the six-prefab row on this PR. The owner chose a new material over
   reusing `M_Reticle.mat`, which would have coupled every corpse to the reticle's recolours.
3. **`DecoyViews` tears down on `StageArrived`, because rule 4's answer cannot work.** The rule says
   the census is told about the boundary *"by `Dispose`"*; `Dispose` runs when `RunScope` is
   disposed, and **a stage crossing does not dispose the run** — it swaps an arena underneath one.
   A 3 s corpse against a boundary's 2 s of gate and arrival is reachable, which is M5-04b's
   arithmetic one class over, so the fourth subscription is `MinionViews`' exactly.
   **`Decoy_IsSweptAtAStageBoundary` is a row the Tests table does not list**;
   `Decoy_ClearLeavesNoOrphan` stays, because leaving the scene is a different claim.
4. **`ProjectileViews` was promoted, not fixed** — the owner's call. It has the identical gap and is
   inert by arithmetic rather than design (a bolt lives well under two seconds), so the
   [parking-lot](../ROADMAP.md#parking-lot) line is rewritten to say the decoy's half is closed and
   the bolts' is not, rather than struck.
5. **The ripple was four test files, not the two the Files table names, and the fourth repeats
   M5-05a's deviation 7 word for word.** `RunTicker`'s constructor grew, so `ResumeFlowTests`,
   `SkillBarPresenterTests` and `FrameOrderTests` moved. **`RunEndPresenterTests` went red** —
   `RunScope_RefusesToComposeWithoutTheScreen` read back the *decoy's* refusal and looked like rule
   7 deleted, because the new guard sits with the body prefabs above the run-end guard. That is the
   second consecutive task to grow that list, which M5-05a's own comment predicted.
6. **`Run.unity` was dressed** — outside the Files table, and manual step 4 requires it. One line:
   `_decoyPrefab`. **No new scene object**, because the corpse parents under the existing `Decals`
   root like the rings and the zones (M2-11a's rule).
7. **Rule 6's measurement is tight, and it was taken before the row was written.** *"Consecrate"* at
   28 pt needs **143.6 px of S1's 150** — it fits with 4 % to spare, and *"Bulwark"* needs 99.6.
   **One thing in row 1 is corrected by it:** auto-sizing at the shipped 8–18 was already rendering
   both words at a full **18 pt**, so 7.9 dp was the *actual* size rather than an upper bound. The
   upper-bound caveat is real for a future longer name and was not true of today's content.
8. **`VFX_Decoy.prefab` is scale 1.0 with shadows off.** Rule 1 says *"the player's silhouette"*, so
   unlike the Wight's 0.7 (M5-05a rule 8) it is not shrunk; the mesh child is the enemy body's 0.8
   at y 0.8, which is what makes it the *shared* body. Shadow casting is off, which the spec does
   not mention: a solid shadow under a half-alpha body is the one thing that would read it as solid
   again.
9. **`Hud_NothingElseMoved` asserts the *set* as well as the sizes.** A new `TMP_Text` appearing on
   `Hud.prefab` reddens it, because every text element on that prefab is a legibility question
   against Android's floor and the seven M4-07 measured are the only ones anybody has measured.

# M4-03 — What the Warden looks like, and the arena that helps it

**Size:** M · **Depends on:** M4-02, M2-11a, M3-11c, M3-13a · **Branch:** `m4-03-boss-arena-and-views`
**Design refs:** GD §9.1 (rules 6, 7), §11.3, §16.2, §16.4; AR §7 · **Ledger rows:** [M4 row 1](../ROADMAP.md#carry-forward-into-m4) inherited; [row 3](../ROADMAP.md#carry-forward-into-m4) gains four

## Goal

Everything M4-01b and M4-02 decided becomes something on screen: a Warden with a body and a scale that says
*boss*, a shockwave you can see coming, fissures that arm before they bite, a visible invulnerable beat — and
**one arena hazard the boss can weaponise** (GD §9.1 rule 6).

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ShockwaveView.cs` | Game | A pooled expanding ring, driven by the event's origin and speed |
| `Game/Views/FissureView.cs` | Game | Arms, fires, closes — the arm state is the telegraph |
| `Game/Views/BossBeatView.cs` | Game | What an invulnerable beat looks like on the body |
| `Tests/Game/Views/BossViewTests.cs` | Tests.Game | Binding, pooling, the palette, the arm/fire seam |
| *small edits* | Game | `EnemyViews` gains the Warden's look entry; `RunScope` registers the three pools |
| *assets* | — | `Warden.prefab`, `VFX_Shockwave.prefab`, `VFX_Fissure.prefab`, `M_WardenAsh.mat`, and the hazard on one arena |

Only these files change. Anything else is a deviation: say so in *As built*.

## Behaviour

1. **The Warden is the shared enemy body at a larger scale with its own tint**, the way all three archetypes
   already are (M2-06 rule 9). **It is not new art** — the KayKit pack has no Warden, `Skeleton_Warrior` is the
   nearest, and importing a boss model is an art task the owner brings in the way M2-art was brought in
   ([parking lot](../ROADMAP.md#parking-lot)). What this task ships is legible *placement and scale*, not a
   sculpture.
2. **Every colour comes from `Palette`** (M3-13a) and **no view carries a serialized `Color`** — the rule that
   task closed ledger row 6 with, and `Views_CarryNoSerializedColour` is scoped by name, so these three add
   their own guard rather than joining that array. **The fissure's arm state must not use `#FF4A1F`** unless it
   *is* danger — and it is, so it may, which makes this the first task since M3-13b to use `Palette.Danger`
   deliberately rather than avoid it.
3. **The arm state and the fire state must be distinguishable at a glance and not by brightness alone.** GD
   §9.1 rule 1 wants a telegraph that reads; a fissure that merely gets brighter is the failure mode M2-12b's
   rings were shaped to avoid, and a *shape* change is what `EnemyHitFeedback` chose for the same reason.
4. **The beat is drawn on the body rather than as a full-screen effect.** A screen-wide flash every phase
   change on a phone is GD §11.3's fill-rate warning and a readability problem; what the player needs to know
   is *this thing is not taking damage*, which is a property of the thing.
5. **Everything is pooled and stepped on the snapshot's clamped `Dt`**, beside the bolts, the rings and the
   zones — never on `Time.deltaTime`, which is the standing rule and the reason M3-11b's pulses land the same
   at 30 and 120 fps.
6. **The hazard is authored on one arena, not on all of them, and it is the arena's rather than the boss's.**
   `ArenaSpec` already exists (M2-11a); a hazard is a thing in the room that the Warden's shockwave can set
   off. **One arena gets it and the other does not**, so the contract is proved without claiming content that
   does not exist — M2-11a rule 11's own bargain, which shipped two arenas rather than twelve.
7. **Views read events and hold no simulation state.** A `ShockwaveView` knows its origin, speed and age and
   nothing else; if core's `ShockwavePassed` never arrives, the view's own countdown retires it — the net
   M3-11c put under `ZoneView` for the same reason.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Shock_BindsToTheEventsOrigin` | `ShockwaveEmitted` at A / bound / the view is at A and expands from radius 0 |
| `Shock_RetiresOnItsOwnCountdown` | no `ShockwavePassed` ever / the authored lifetime / returned to the pool (rule 7) |
| `Fissure_ArmAndFireDifferByShape` | an armed fissure and a fired one / compared / they differ by more than alpha (rule 3) |
| `Fissure_ArmUsesTheDangerColour` | the arm state / read / `Palette.Danger`, deliberately (rule 2) |
| `Views_CarryNoSerializedColour` | the three new views / reflected / none has a `[SerializeField] Color` (rule 2) |
| `Beat_IsDrawnOnTheBody` | `BossBeatStarted` / bound / the effect's transform is parented to the agent, not the canvas (rule 4) |
| `Beat_EndsOnTheEvent` | `BossBeatEnded` / received / the effect is gone |
| `Views_StepOnTheSnapshotDt` | a view ticked / reflected / no `Time.deltaTime` in `Soulvail.Game`'s boss views (rule 5) |
| `Pool_ReturnsEverything` | a full fight's worth of rings and fissures / ended / every instance is back in its pool |
| `Warden_PrefabIsDressed` | `Warden.prefab` / loaded / every serialized reference the views need is non-null (Traps §5's block-namespace trap is what this catches) |
| `Arena_HazardIsOnOneArenaOnly` | both shipped arenas / inspected / exactly one carries the hazard (rule 6) |

## Manual verification (Editor / device)

**Steps 1a and 1b are [M4-02](M4-02-warden-behaviours.md)'s own steps 1 and 2, struck through there and
flagged *"→ M4-03"*. They are quoted verbatim, they are this task's acceptance rather than new steps, and
nothing below them replaces them.** A subscriber for both events now exists (`BossViews`), which is the check
M4-02's handover skipped.

1a. **[Editor]** Reach stage 5 and let the Warden slam. *Expected: a ring leaves the point it slammed and
   passes through you if you stand still; walking out of it costs you nothing.*
1b. **[Editor]** Let a fissure arm under you and step off it. *Expected: it fires where you were and misses.*

1. **[Editor]** Reach stage 5. *Expected: a visibly larger body in its own tint; you can tell at a glance it is
   not a Husk.*
2. **[Editor]** Watch a slam. *Expected: a ring leaves the slam point and you can see it coming in time to walk.*
3. **[Editor]** Watch a fissure arm. *Expected: the arm reads as a warning, and differs from the fired state by
   shape, not only brightness.*
4. **[Editor]** Cross 66 %. *Expected: something on the body says "not taking damage", and it goes away.*
5. **[device]** **GD §9.1 rule 7 — whether any of it reads on a 6-inch screen.** The rule says to test every
   telegraph at phone size *before calling it done*; this project has no phone, so **the rule is unmet and the
   task says so** rather than ticking it. [M4 row 3](../ROADMAP.md#carry-forward-into-m4).
6. **[device]** Whether a ring, a fissure, a Consecrate zone and a telegraph ring **overlapping at once** cost
   frames — GD §11.3 names overlapping transparent VFX as *the* mobile fill-rate cost, and a boss fight is the
   first time this many can coincide.

## Out of scope

- **Real boss art.** Rule 1 — a scaled, tinted shared body. The owner brings art in.
- **Audio.** No audio system exists; M7.
- **The segmented health bar** — M4-04.
- **Hazards on the other arena, or a second hazard** — rule 6, and M7-05/06's art pass.
- **M4 row 1's HUD problem.** These are world-space views, not HUD; they neither help nor worsen it.

## As built

**The fight is visible. Until this task not one line in `Soulvail.Game` subscribed to any of M4-02's five
hazard events or M4-01b's two beat events, and the build the owner playtested took 22 hit points off them with
nothing on the floor to say it was coming.** Seven subscriptions now exist, all in one place.

**Three deviations from the Files table, and the first is the largest.**

1. **`BossViews.cs` was added — a fourth file, and the census the three views needed.** The table lists three
   pooled bodies and no listener, and something has to hold the subscriptions, the three pools and the
   id → body index. Three censuses (this project's shape: `ZoneView` ↔ `ZoneViews`) would have been three more
   files *and* three more arguments on `RunTicker`; folding the census into each view's file would have put a
   `ViewPool` and a `DomainEventHub` inside a `MonoBehaviour` that every other view in the project keeps out.
   So one census owns all three pools, one `RunTicker` argument, one `Step`. **Five new code files, size M
   unchanged** by [the ROADMAP's own criterion](../ROADMAP.md#how-to-read-this).
2. **`Warden.prefab`, `M_WardenAsh.mat` and `Warden_PrefabIsDressed` were dropped, because they had no
   subject.** Checked in the Editor before a line was written: `Warden.asset` already authors `_tint`
   `(0.247, 0.231, 0.255)` and `_bodyScale` **2.2**, `BootInstaller.BuildLookBook` builds the look book from
   *every* `EnemyDefinition` in the boot list and `Warden.asset` is in it, and `EnemyViews.OnSpawned` applies
   `look.Tint` and `look.BodyScale` on every rental without exception. **So rule 1 was already true of the
   shipped build** — the Warden is the shared enemy body at 2.2× in its own dark tint — and the Files table's
   *"`EnemyViews` gains the Warden's look entry"* has no edit behind it either. **`EnemyViews.cs` is
   untouched.** A prefab and a material authored anyway would have been a second body nothing rents.
3. **`ArenaSpec` does not exist, and the spec's rule 6 assumes it does.** M2-11a shipped `ArenaView` in
   `Soulvail.Game` and no core arena type at all, so the hazard is authored on the view. **`ArenaView.cs`,
   `RunTicker.cs` and three unrelated test fixtures took small additive edits** the table does not list:
   `ArenaView` gains `_hazards` / `HazardCount` / `HazardPosition` / `IsToppled` / `Topple` and restores them
   in `Seal`; `RunTicker` gains a twenty-third argument and a fourth cosmetic `Step`; and `ResumeFlowTests`,
   `SkillBarPresenterTests` and `FrameOrderTests` each construct one more census because that argument is
   positional. None is a new file.

**Rule 6 shipped at the grain the owner ruled, and the part that is *not* built is stated rather than
implied.** `Arena_Pillars` gains a `Brazier` at (−4, 4) and `Arena_Tiered` gains nothing, which is
`Arena_HazardIsOnOneArenaOnly`. A shockwave whose **front edge crosses** it this frame knocks it over — a
crossing rather than a containment, so a second ring over the wreckage changes nothing — and `Seal` stands it
back up, because an arena is the same instance at every stage boundary. **It costs the player nothing, and
that is deliberate**: hazard *damage* would need an id, a circle and a number in core, and there is no core
file in this task. The brazier is on the Default layer rather than Cover so GD §7.2's 3–6 pillar band is
untouched, and it carries no collider because the NavMesh was baked without it.

**Rule 3's shape change is a direction as well as a size.** The arm is a disc opening from nothing to the
circle core will test — `TelegraphRingView`'s *"filling means coming"*, the only telegraph vocabulary this game
has taught — and the bite snaps to **1.35×** the radius and shuts to nothing over 0.35 s.
`Fissure_ArmAndFireDifferByShape` asserts both halves: 1.25 m against 3.375 m at the same instant, and then
that one is growing while the other is shrinking. A size test alone would pass for a fired state that merely
started larger.

**Rule 2 is honoured and `Palette.Danger` is used on purpose, which is the first time since M3-13b.** The arm
*is* danger. The ring is too. The **beat is not**, and that is the rule read from the other side: a boss being
safe is not the player being in danger, so the shell is `Palette.Neutral` — GD §16.4's *"everything else"* —
with `Palette.Player`'s cyan refused for the mirror reason. `Views_CarryNoSerializedColour` reflects over all
three for a serialized `Color` and finds none.

**Rule 7's net is derived, never authored.** A ring's lifetime is `MaxRadius / Speed` — 0.875 s at the shipped
7 m and 8 m/s — read off the event, and `Shock_RetiresOnItsOwnCountdown` never publishes `ShockwavePassed` at
all. A crack whose `FissureFired` never arrives **fires itself** when the arm runs out, which is the one thing
such a crack can safely be assumed to have done. No constant of `WardenBehaviour`'s is re-declared anywhere in
`Soulvail.Game`; `Shock_ExpandsAtTheEventsSpeed` and `Fissure_ArmsForTheEventsWindow` are the rows that would
fail if one ever were.

**One finding the Editor produced that the spec did not ask for, and it is about six files rather than three.**
`M_TelegraphRing.mat` blends `One / OneMinusSrcAlpha` — premultiplied alpha — and does **not** carry URP's
`_ALPHAPREMULTIPLY_ON` keyword, so the shader never scales the albedo itself. Every view sharing that material
writes the palette's colour unscaled, which means **alpha only decides how much floor shows through and never
dims the decal**: a ring at alpha 0.5 draws at full brightness. A camera capture is what found it — the beat's
shell was an opaque grey ball over the boss. **M4-03's three views premultiply and `ZoneView`,
`TelegraphRingView` and `BulwarkView` do not**, which is the inconsistency this task chose over changing three
shipped looks; one keyword on one material makes all six agree and is the owner's call. Recorded in
[M4 ledger row 3](../ROADMAP.md#carry-forward-into-m4).

**A second finding of the same kind, and it predates this task by a milestone:** `VFX_TelegraphRing.prefab` is
an untextured built-in **Quad**, so every *"ring"* in this game — spawn telegraph, blast ring, Consecrate
ground, and now the slam wave and the crack — is drawn as a **square**. The two new decals inherit it rather
than departing from it, because one round hazard beside four square ones is worse than five square ones. One
circle texture on `_BaseMap` turns all five into discs in one asset; it is art, so it is M7's.

**2 124 EditMode / 0 / 0, five green runs** (31.5 s, 23.8 s, 27.0 s, 24.1 s, 24.1 s, and two more after the
premultiply change at 28.4 s and 24.6 s), against M4-02's **2 093** — **+31, and the arithmetic lands to the
row**: every one of them is `BossViewTests`, and no existing row was removed or changed. **The first run was
red at 2 121 / 3** and all three were the fixture's arithmetic rather than the code's — two assertions written
against a prewarm of one where the census builds two, and a step of 0.125 s asserted as *"a third of the
0.9 s arm"* when a third is 0.3.

**The spec's Tests table is 11 rows and 31 shipped.** Nine of the eleven are named exactly;
`Warden_PrefabIsDressed` has no subject (deviation 2) and its job — Traps §5's block-namespace trap, which
loads a `MonoBehaviour` as null off every asset that references it with nothing reported anywhere — is done by
`Prefabs_AreDressed` against the three prefabs that *do* exist. The twenty over the table are the doors
(`Bind_InvalidArgument_Throws`, `Constructor_NullDependency_Throws`, `Step_NonFiniteDt_ChangesNothing`), the
per-frame cost (`Step_AllocatesNothing`, through `AllocationAssert` and never a hand-rolled probe), the
pooling rules each body has (`Fissure_ComesBackClean`, `Beat_ComesOffTheBodyWhenItIsReturned` — the shell's
*parent* is the field a pooled body would otherwise inherit), and the four hazard rows rule 6 needed once it
had behaviour.

**What was verified, and what was not.** Compile clean through the MCP; the suite green five times; zero new
analyzer warnings (the four Console errors are `LocalJsonSaveStore` tests feeding deliberately corrupt saves,
unchanged from M4-02); `dotnet format whitespace --folder --verify-no-changes` clean over all eleven changed
files. **A camera capture of all four bodies over a floor is the evidence that they draw at all** — it is what
caught the premultiply bug and what the alphas were retuned against. **Nobody has played to stage 5**: manual
steps 1–4 are the owner's, and they are M4-02's steps 1 and 2 re-run as originally written plus the two this
spec adds. The device rows stay unmet and are [M4 ledger row 3](../ROADMAP.md#carry-forward-into-m4)'s.

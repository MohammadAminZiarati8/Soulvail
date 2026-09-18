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

_Filled at merge._

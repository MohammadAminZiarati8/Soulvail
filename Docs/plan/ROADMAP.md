# Soulvail — Roadmap

**The map.** Milestones → tasks, one line each. Where we are *right now* lives in [PROGRESS.md](PROGRESS.md). What each task is exactly lives in [tasks/](tasks/).

## How to read this

- One milestone at a time. Every milestone ends with something **playable on a phone** and a tag on `main`.
- A task is **one branch, one PR, 1–5 files.** Size S = 1–3 files, M = 4–5, L = must be split before starting. Size counts **new code files and substantial rewrites**; small additive edits to existing files (a new field on a spec, a registration, an event struct) are listed in the spec but don't count — they're trivial to review.
- Task IDs are `M<milestone>-<nn>`. Branch = lowercase ID + slug: `m0-03-domain-events`. PR title = the spec's H1.
- **A milestone's specs are written by its own first tasks** — `M<n>-00a…`, themed groups of 3–5 specs each, against the previous milestone's carry-forward ledger — before its `-01` starts. Later milestones are titles + one-line goals. (The earlier rule, "spec the next milestone when this one is ~75 % merged", missed twice; nothing in the protocol could check it.)
- A box is ticked **in the PR that closes the task**; it becomes true when that PR merges into `dev`. The PR number lives in the merge commit, not here.
- Splits keep the parent ID: `M0-07a`, `M0-07b`.

**Spec pointers:** GD = [GameDesign.md](../GameDesign.md) · CH = [Characters.md](../Characters.md) · CC = [CoreCombat.md](../CoreCombat.md) · AR = [Architecture.md](../Architecture.md) · ADR = [adr/](../adr/)

## Milestones

| | Milestone | Ends when | Tasks |
|---|---|---|---|
| **M0** | **Walking skeleton** | Floating stick → core motor → intent → capsule moves in a grey box **on the phone**, through VContainer scopes, with events/snapshot/intent plumbing real and tested | 21 |
| **M1** | **Combat feel** | Stat, Health/Aegis, targeting + reticle, tap-to-focus, Censer, Charge, Focus, chaser dummies. [CC §8](../CoreCombat.md) checklist passes on device | 21 |
| **M2** | **Stage loop** | Mode as data, threat budget, director, Husk/Spitter/Bloater, arenas, seal/gate, run persistence across app kill | 27 |
| **M3** | **Levelling and the tree** | XP, tree rules, offers, level-up screen, SkillRunner + auto-cast, effect primitives, first Oathbound nodes, health-bar treatment | 22 |
| **M4** | **First boss and run end** | Boss phases, Warden of Ash, death → Shard payout, profile persisted | 7 |
| **M5** | **Second class** | Gravecaller: projectile weapon + leading, Shroudstep, Wights, its tree, class select | 8 |
| **M6** | **Systems complete** | Sanctum shop, Veilrot + Pacts + Claiming, Ordeals, Emberwright, unlocks, localisation tables | 11 |
| **M7** | **Content pass** | Full V1 roster, Elites/affixes, Choirmother, all 81 nodes, both biomes' art, audio | 8 |
| **M8** | **Feel, perf, ship** | Game-feel checklist, options/accessibility, device tiering, thermal, per-class balance, store build | 6 |

---

## M0 — Walking skeleton

**Goal:** prove the architecture end to end on a real device with the thinnest possible slice: one input, one core system, one intent, one view.
**Done when:** every item in the [M0-20](tasks/M0-20-acceptance-and-tag.md) device checklist passes and `m0` is tagged on `main`.

**Status: complete, tagged on Editor evidence.** Every architectural claim M0 set out to prove is verified — 153 EditMode + 3 PlayMode tests green, all three scenes playable, zero analyzer warnings — and the feel question is answered. Two things are carried as explicit debt rather than met: the APK does not yet launch outside the Editor ([M0-20a](tasks/M0-20a-apk-runs-on-bluestacks.md), diagnosed to BlueStacks' Vulkan-through-houdini bridge, not to game code), and the four `[device]` rows have no hardware to run on.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M0-01](tasks/M0-01-project-skeleton.md) | Project skeleton: folders, five assembly definitions, VContainer 1.19.0 | M | — | ☑ |
| [M0-02](tasks/M0-02-state-machine.md) | `StateMachine<T>` + core-purity guard test | S | 01 | ☑ |
| [M0-03](tasks/M0-03-domain-events.md) | Domain events: `IDomainEvents`, `DomainEventHub`, `RecordingEvents` | M | 01 | ☑ |
| [M0-04](tasks/M0-04-random-streams.md) | `IRandom` with named streams, `SeededRandom`, `FixedRandom` fake | M | 01 | ☑ |
| [M0-05](tasks/M0-05-world-snapshot.md) | `WorldSnapshot`, `EnemySense`, `Num` vector conversions | M | 01 | ☑ |
| [M0-06](tasks/M0-06-intents.md) | `PlayerMoveIntent`, `IIntentSink`, `IntentBuffer` | M | 05 | ☑ |
| [M0-07](tasks/M0-07-player-motor.md) | `MovementSpec`, `PlayerMotor`: accel/decel, no inertia, facing | S | 01 | ☑ |
| [M0-08](tasks/M0-08-content-catalog.md) | `ContentId`, `LocKey`, `CharacterSpec`, `ContentCatalog` | M | 07 | ☑ |
| [M0-09](tasks/M0-09-run-contracts.md) | `IRunSession`, `RunConfig`, `RunState`, run events | M | 05 06 07 08 | ☑ |
| [M0-10](tasks/M0-10-run-session.md) | `RunSession` + `RecordingIntents` fake + tests | S | 03 04 09 | ☑ |
| [M0-11](tasks/M0-11-character-authoring.md) | `CharacterDefinition` SO, `Oathbound.asset`, conversion + validation tests | S | 08 | ☑ |
| [M0-12](tasks/M0-12-composition-installers.md) | `BootInstaller`, `RunInstaller`, `PendingRun`, container tests | M | 10 11 | ☑ |
| [M0-13](tasks/M0-13-scopes-and-scenes.md) | `BootScope`, `RunScope`, `SceneLoader`, `BootFlow`; Boot/Menu/Run scenes | M | 12 | ☑ |
| [M0-14](tasks/M0-14-input-actions.md) | `Soulvail.inputactions` + generated class + `InputAdapter` | S | 01 | ☑ |
| [M0-15](tasks/M0-15-floating-stick.md) | `StickShaper` (pure) + `FloatingStick` control + HUD prefab | M | 14 | ☑ |
| [M0-16](tasks/M0-16-snapshot-builder-player-view.md) | `SnapshotBuilder`, `PlayerView`, `RunTicker`, player + grey-box prefabs | M | 06 12 14 | ☑ |
| [M0-17](tasks/M0-17-menu-stub.md) | `MenuScope`, `MenuPresenter`, Descend button | S | 13 | ☑ |
| [M0-18](tasks/M0-18-camera-and-debug-overlay.md) | `FollowCamera`, `DebugOverlay` | S | 16 | ☑ |
| [M0-19](tasks/M0-19-android-build.md) | Player settings, `AndroidBuild` script, APK on device | S | 13 16 17 18 | ☑ |
| [M0-20](tasks/M0-20-acceptance-and-tag.md) | M0 device acceptance, tuning, tag `m0` | S | all | ☑ |
| [M0-20a](tasks/M0-20a-apk-runs-on-bluestacks.md) | Make the APK actually run on BlueStacks (release build / Vulkan deny filter) | S | 19 20 | ☐ |

---

## M1 — Combat feel

**Goal:** the moment-to-moment loop — target, swing, dodge — feels good on device with untextured capsules.
**Done when:** [CC §8](../CoreCombat.md) checklist passes on device and `m1` is tagged.

**Status: complete, tagged on Editor evidence — the same terms as `m0`, for the same missing phone.** 452 EditMode + 3 PlayMode tests green, zero errors, zero analyzer warnings, and no tuning edit needed anywhere: every CC §7 number already matched the asset shipping it. The owner playtested the fight and called it good, which is M1's question answered — but answered *in the Editor*, so the feel verdict is provisional and the three `[device]` rows (multi-touch, haptics, sustained 60 fps with 12 chasers) are carried as debt, joined by the whole of M1-20's checklist. One checklist row is deliberately unresolved rather than passed: the out-of-range focus tap, still in the parking lot with its three ways out.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M1-01](tasks/M1-01-stat-and-modifier.md) | `Stat` + `Modifier` stack: Flat → PercentAdd → PercentMult, by source, cached, describable | S | M0 | ☑ |
| [M1-02](tasks/M1-02-health-and-aegis.md) | `Health`, `ShieldSpec`, `DamageResult`: HP, Aegis recharge, hit i-frames | M | 01 | ☑ |
| [M1-03](tasks/M1-03-target-scorer.md) | `TargetCandidate`, `TargetingSpec`, `TargetScorer` | M | 01 | ☑ |
| [M1-04](tasks/M1-04-targeter.md) | `Targeter`: cadence, immediate retarget, focus override, all-blocked state | S | 03 | ☑ |
| [M1-05](tasks/M1-05-enemy-entities.md) | `EnemySpec`, `EnemyAgent`, `EnemyBlackboard`, `EnemyRegistry` | M | 02 | ☑ |
| [M1-06](tasks/M1-06-enemy-system.md) | `EnemySystem`: spawn plan, snapshot ingestion, perception, enemy events | M | 05 | ☑ |
| [M1-07](tasks/M1-07-enemy-authoring-and-views.md) | `EnemyDefinition`, `Husk.asset`, `EnemyView`, `EnemyViews`, dummy prefab | M | 06 | ☑ |
| [M1-08](tasks/M1-08-player-combat.md) | `PlayerCombat`: health, targeting, `CombatBlackboard`, combat events; `RunSession` composes | M | 02 04 06 | ☑ |
| [M1-09](tasks/M1-09-tap-to-focus-and-reticle.md) | `IPlayerCommands.FocusTarget/ClearFocus`, 3 m resolver, `TapToFocusAdapter`, `ReticleView` | M | 07 08 | ☑ |
| [M1-10](tasks/M1-10-weapon-and-cone-intent.md) | `WeaponSpec`, `Weapon` cadence + damage frame, `ConeHitIntent` | M | 08 | ☑ |
| [M1-11](tasks/M1-11-cone-hits-to-damage.md) | `ReportConeHits` fact → damage → `EnemyDamaged` / `EnemyDied`, despawn | S | 10 | ☑ |
| [M1-12](tasks/M1-12-cone-overlap-and-hit-feedback.md) | `ConeOverlapQuery`, `RunTicker` fact loop, hit flash, dissolve death | M | 07 11 | ☑ |
| [M1-13](tasks/M1-13-focus-ramp.md) | `FocusSpec`, `FocusTracker` → fire-rate modifier; ground glow | M | 10 | ☑ |
| [M1-14](tasks/M1-14-charge-skill.md) | `MovementSkillSpec`, `ChargeSkill` (pure): cooldown, buffer, i-frame window | S | 02 | ☑ |
| [M1-15](tasks/M1-15-charge-integration.md) | `ChargeIntent`, `IPlayerCommands.MovementSkill`, `ReportChargeHits`, knockback intent | M | 11 14 | ☑ |
| [M1-16](tasks/M1-16-charge-view-and-skill-button.md) | `PlayerView` charge motion + sweep, `SkillButton`, movement-skill input | M | 15 | ☑ |
| [M1-17](tasks/M1-17-hud.md) | `HudPresenter`, HP bar with ghost trail, shield ring, cooldown, death → Menu | M | 08 16 18 | ☑ |
| [M1-18](tasks/M1-18-chaser-ai.md) | `ChaserBehaviour` FSM, `EnemyMoveIntent`, strike damage, telegraph event | M | 06 08 | ☑ |
| [M1-19](tasks/M1-19-pooling-respawn-navmesh.md) | `ViewPool`, `RespawnPolicy` (keep 12 alive), NavMesh bake + path sense | M | 12 18 | ☑ |
| [M1-20](tasks/M1-20-haptics.md) | `HapticsListener`, Android vibrator, toggle | S | 11 15 | ☑ |
| [M1-21](tasks/M1-21-acceptance-and-tag.md) | M1 acceptance: CC §8 on device, tuning, tag `m1` | S | all | ☑ |

---

## M2 — Stage loop

**Status: complete (27/27), acceptance passed on Editor evidence — the same terms as `m0` and `m1`, for the same missing phone.** 1077 EditMode tests green (run twice), six assemblies, zero errors, zero analyzer warnings, and **the whole fourteen-row carry-forward ledger closed with nothing carried into M3**. The owner playtested the descent and called it good, which is M2's question — *is descending, stage after stage, worth doing for ten minutes?* — answered yes. Three things are carried as explicit debt rather than met: the **TTK invariant breaks at stage 15** with an unlevelled class, which is M3's to close and is now [row 1](#carry-forward-into-m3) with a formula on both sides; the **PlayMode frame-order intermittency** reproduced on `dev` itself and its one-line fix is still the owner's call ([row 3](#carry-forward-into-m3)); and the **device rows** have now gone three milestones unrun ([row 4](#carry-forward-into-m3)). `m2` is the owner's to tag.

**Detailing is overdue and is its own task, split: hygiene first (00a, 00f), then the specs in four themed groups (00b–e).** The ~75 % trigger passed unfired during M1, so every spec below is written before M2-01 starts — fifteen tasks as first counted, **twenty** after M2-07, M2-11, M2-12, M2-13 and M2-14 were each split before starting — carved out of [M1-21](tasks/M1-21-acceptance-and-tag.md), whose Files table originally promised them, because acceptance and design want different reviews. **M2-00 is split for the same reason it was carved out:** fifteen specs in one PR is the review the five-file rule exists to prevent, so it goes out in themed groups of 3–5 that each hang together.

Everything those specs must absorb is in the [carry-forward ledger](#carry-forward-into-m2) below — four measurements M1 took, seven findings from the M0+M1 audit, the one playtest row that failed on behaviour, and the two prices M2-00c's ruling named out loud. **A spec that does not name its ledger rows is not finished.**

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| M2-00a | Plan hygiene: archive M0/M1 logs, split the watch list into `Traps.md` + `Architecture.md §18`, carry-forward ledger | S | — | ☑ |
| M2-00f | Doc-system audit: template precedence and self-check, PROGRESS entry cap, §18.5 → ledger, staleness after #46 | S | 00a | ☑ |
| M2-00b | Specs for M2-01…M2-05 — the spawning spine | S | 00f | ☑ |
| M2-00c | Specs for M2-06…M2-09 — enemies and projectiles | S | 00f | ☑ |
| M2-00d | Specs for M2-10…M2-12 — stage flow, arenas, telegraphs | S | 00f | ☑ |
| M2-00e | Specs for M2-13…M2-15 — persistence, resume, acceptance | S | 00f | ☑ |

**Specced by M2-00b:** the spawning spine, M2-01…M2-05. Sizes, dependencies and boxes below; the rest of the milestone stays titles until 00c–e write them.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M2-01](tasks/M2-01-clock.md) | `IClock` + `UnityClock`: wall-clock, for persistence only | M | — | ☑ |
| [M2-02](tasks/M2-02-mode-spec.md) | `ModeSpec` + `ModeDefinition`, Descent, and the `RunConfig` reshape | M | 01 | ☑ |
| [M2-03](tasks/M2-03-threat-budget.md) | `ScalingSpec` + `ThreatBudget`, and depth scaling a recycled enemy forgets | M | 02 | ☑ |
| [M2-04](tasks/M2-04-wave-composer.md) | `WaveComposer`: budget → waves, under a cap that is priced | M | 03 | ☑ |
| [M2-05](tasks/M2-05-spawn-director.md) | `SpawnDirector`: waves, telegraphs, spawn safety, path budget | M | 04 | ☑ |

**Unplanned, merged out of order.** The owner brought art in mid-milestone, so this ran without a
spec, the way M2-00a and M2-00f did. It is a row rather than a footnote because the ROADMAP had
**no home for character art or animation anywhere** — M7-05/06 are biome art only — and "later"
was therefore nowhere.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| M2-art | KayKit import, `ThirdParty/` layout, Knight as the player, `PlayerAnimatorView` | M | — | ☑ |

**Specced by M2-00c:** enemies and projectiles, M2-06…M2-09. **M2-07 is split before it starts, not after** — the shots and the shooter are seven files together, and the projectile system is what settles [ledger row 7](#carry-forward-into-m2) for all three archetypes, so it goes first and alone. The row's title also changed: there is no projectile *fact*, and the reason is M2-07a rule 1.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M2-06](tasks/M2-06-enemy-authoring.md) | The Spitter and the Bloater as authored data, and three archetypes you can tell apart | M | 04 | ☑ |
| [M2-07a](tasks/M2-07a-projectile-system.md) | `ProjectileSystem`: shots already in the air, and who decides they landed | S | 06 | ☑ |
| [M2-07b](tasks/M2-07b-spitter-ai.md) | `IEnemyBehaviour`, and the Spitter that keeps its distance | S | 07a | ☑ |
| [M2-08](tasks/M2-08-bloater-ai.md) | `BloaterBehaviour`: a fuse you have to walk away from, and a blast the corpse owns | S | 07b | ☑ |
| [M2-09](tasks/M2-09-projectile-views-and-pooling.md) | Projectile views, and the first test that proves a pooled body forgets its last life | M | 07b | ☑ |

**Specced by M2-00d:** stage flow, arenas and telegraphs, M2-10…M2-12. **Both M2-11 and M2-12 are split before they start.** M2-11 is the arena as a *place* and the arena as something core can *ask about*, which is seven files together and two different reviews; the sense also has to land after the pillars it raycasts against exist. M2-12 is split by machinery, not by ledger row — rows 12 and 14 stay together in 12a because [the ledger](#carry-forward-into-m2) says answering them apart gets two indicators that disagree, while the ground decals share M2-09's pool and none of that question.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M2-10](tasks/M2-10-stage-flow.md) | `StageFlow`: arrival, seal, clear, a door that opens once — and `RespawnPolicy` retired | M | 05 04 02 | ☑ |
| [M2-11a](tasks/M2-11a-arena-contract-and-pool.md) | The arena as a contract: a pool of them, a barrier, and a door | M | 10 | ☑ |
| [M2-11b](tasks/M2-11b-line-of-sight-sense.md) | `LineOfSightSense`: a pillar you can hide behind, and the frame order under test | S | 11a 07b | ☑ |
| [M2-12a](tasks/M2-12a-threat-arrows-and-held-focus.md) | Screen-edge threat arrows, and a focus that reads as held | M | 07b | ☑ merged (PR #66). **The physics-sync question it opened is still the owner's and is now tracked in [PROGRESS → Known issues](PROGRESS.md#current-state)**, because it outlived the task: `dev` itself fails one `FrameOrderTests` row and M2-12b measured the one-line fix green. |
| [M2-12b](tasks/M2-12b-telegraph-rings.md) | The ring that says *something is about to happen here* | S | 05 08 09 | ☑ |

**Specced by M2-00e:** persistence, resume and acceptance, M2-13…M2-15. **Both M2-13 and M2-14 are split before they start.** M2-13 was four production files and four test files in one row — the port and the DTOs are a review about *what a run is*, and the JSON, the atomic write and the migration harness are a review about *how it is spelled on disk*; the second cannot be judged without the first being settled. M2-14 splits at the seam between writing and reading: 13a…14a can all be verified by looking at a file, while 14b is the menu, the container and the six-field `RunConfig` sweep. [Ledger row 1](#carry-forward-into-m2) is answered across three of them — captured in **14a**, restored in **14b**, and made sayable at all by **13a**'s format v1, which is the last free moment for it.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M2-13a](tasks/M2-13a-save-port-and-dtos.md) | `ISaveStore`, the DTOs, and the stream position that makes a resume honest | M | 01 02 | ☑ |
| [M2-13b](tasks/M2-13b-local-json-save-store.md) | `LocalJsonSaveStore`: format v1 on disk, an atomic write, and the migration harness | M | 13a | ☑ |
| [M2-14a](tasks/M2-14a-snapshot-at-the-boundary.md) | The snapshot at the boundary: what a run writes, when, and what it forgets | M | 13a 10 | ☑ |
| [M2-14b](tasks/M2-14b-resume-and-continue.md) | Resume: a `Continue` that means it, and `PendingRun.Clear`'s first caller | M | 14a 13b | ☑ |
| [M2-15](tasks/M2-15-acceptance-and-tag.md) | M2 acceptance, the ledger closed, tag `m2` | S | everything | ☑ |
| [M2-15a](tasks/M2-15a-physics-sync.md) | `Physics.SyncTransforms()`: make the frame order true of the code | S | 15 | ☑ **After the tag, by M2-15's own rule** — *"a bug found here becomes `M2-15a…` with its own PR."* The owner ruled [M3 ledger row 3](#carry-forward-into-m3) rather than carrying it into M3's specs, which is why it lands here rather than as M3-00-something. |

### Carry-forward into M2

**Closed at M2-15. All fourteen rows are struck and none was carried into M3.** Rows 2, 3, 4, 5, 7 and 9 were answered by M2-02, M2-03, M2-04, M2-05 and M2-07a, rows 8 and 13 by M2-09 and M2-11b, row 11 by M2-13b, rows 1, 6 and 10 by M2-14b, and **rows 12 and 14 by M2-12a, verified rather than built at M2-15** — both were already in the code, and what M2-15 owed them was a playtest that said the indicators read. Their numbers are not reused, so a PROGRESS entry that cites "row 2" still points at the right thing.

**Row 1 is the one that took three tasks** — the format at M2-13a, the capture at M2-14a, the restore at M2-14b — and it is the reason the save format could carry a stream position before anything shipped that would have to be migrated to let it. Four rows were measurements M1 took, seven were findings from the M0+M1 audit (2026-09-10), which checked spec-against-code across all 42 tasks and found the two milestones sound; row 12 came from the parking lot once it had an owner, and rows 13 and 14 were added at M2-00c as the named price of ledger row 7's ruling.

**The mechanism worked and is worth keeping for exactly one reason:** every row named the task that had to deal with it, and no row left this table without its owner's *As built* saying it was answered. Nothing was silently dropped, which is the failure M2-00a built it against. [M3's table](#carry-forward-into-m3) is opened below on the same terms.

| # | Finding | Owner | Cost of leaving it |
|---|---|---|---|
| ~~2~~ | ~~**`Stat` removes modifiers by source reference only** — no "drop everything" — and `EnemyAgent` is recycled with its `Health` intact.~~ **Answered by [M2-03](tasks/M2-03-threat-budget.md):** `Stat.RemoveAll()` with no argument, called by `EnemyAgent.Initialise` on all three of an agent's stats before it re-bases them. The agent-owned source token was rejected because a token covers only its own source, so M7-02's affixes and M3's debuffs would each have to be listed at the recycle point — and that list gets one entry short. **A recycled agent forgets everything**, foreign sources included, which is a rule that cannot be half-applied (AR §18.1). | ~~M2-03~~ | **Closed.** |
| ~~3~~ | ~~**`EnemySystem.SpawnAll` runs *after* `RunStarted`**, so an unauthored archetype mid-plan throws with the run announced and enemies standing.~~ **Answered by [M2-02](tasks/M2-02-mode-spec.md):** `Start` resolves the mode, the class, and every archetype the plan, the respawn policy **and the mode's roster** name — all before `RunStarted`. The roster walk is the half with no other line of defence, since nothing spawns from a roster until M2-05 and the failure would otherwise arrive forty seconds into a run. *(M2-10 deleted `RespawnPolicy`, so there are two lists to walk rather than three; the rule and its ordering are unchanged.)* | ~~M2-02~~ | **Closed.** |
| ~~4~~ | ~~**`AlliesNearby` is `n² − n` comparisons a frame** over the *registered* count — free at 12, **3,540 a frame at 60**.~~ **Answered by [M2-04](tasks/M2-04-wave-composer.md):** the cap was chosen against the number rather than inferred, and the arithmetic is written down where the cap lives (`BootInstaller.DeviceEnemyCap`). At 28 it is **756 comparisons a frame**, 1,560 at GD's high tier of 40 and 4,032 at the snapshot's capacity of 64 — so the O(n²) stays and **a cap above 40 needs a spatial hash first** (parking lot, M8-03's if a high tier ever ships). | ~~M2-04~~ | **Closed.** |
| ~~5~~ | ~~**Path refresh is hard-capped at 4 a frame against a 10 Hz cadence, so routes go stale silently above 24 concurrent enemies.** Nothing reports it at runtime.~~ **Measured by [M2-04](tasks/M2-04-wave-composer.md)** — 24 sustained at 60 fps, **12 at 30** — and **fixed by [M2-05](tasks/M2-05-spawn-director.md):** `PathRefreshBudget.ForFrame(activeCount, dt)` = `ceil(n · refreshHz · dt)`, floored at 1 and capped at 16, which is **5 a frame at 60 fps and 10 at 30** for 28 enemies — the same 280 recomputes a second however the frames are sliced. The ceiling of 16 is what stops a hitch feeding itself, and when it binds the shortfall is *reported* rather than lost: `NavPathSense.StalePathCount` counts the enemies overdue by more than a period, and `DebugOverlay` shows it. **The row's real complaint was never the number 4 — it was that nothing said the ceiling had been reached**, which is why the fix is a budget *and* a counter. | ~~M2-04~~ · ~~M2-05~~ | ~~Medium.~~ Closed. |
| ~~7~~ | ~~**The fact-versus-direct-call rule is unsettled.**~~ **Answered by [M2-07a](tasks/M2-07a-projectile-system.md):** core decides every outcome an enemy causes and calls `PlayerCombat.ApplyDamage` directly; Unity owes a *fact* only when the answer depends on colliders core does not hold, and a *sense* when the geometric question is a standing one rather than an instant. **`IRunSession` gained no member and the stale comment is gone** — the `ReportContact` "with the chasers of M1-18" and `ReportProjectileHit` "with the Spitter in M2-07" it promised had been wrong since M1-18 merged, and they were deleted along with AR §5's matching row. Written down in three places so it cannot drift back: the port's remarks, **AR §18.1**'s ordering table (the projectile step) and **AR §18.2** (the rule itself, with the three counts the fact route was rejected on). **Its price is named rather than hidden** and has its own owner: core holds no walls, so a shot passes through cover — row 13. | ~~M2-07a~~ · applied by **M2-07b** and **M2-08** | ~~Medium.~~ Closed. |
| ~~8~~ | ~~**Views and `RunTicker`'s frame order have no automated coverage at all.**~~ **The pooled-view half was closed by [M2-09](tasks/M2-09-projectile-views-and-pooling.md)** — `Tests/PlayMode/PoolingLifecycleTests.cs` takes both families through a full life and asserts scale, material, tint, alpha, collider, id and velocity against a body the pool has never handed out; it had to be PlayMode by construction, which is why it was never written by accident. **The frame order is closed by [M2-11b](tasks/M2-11b-line-of-sight-sense.md):** `Tests/PlayMode/FrameOrderTests.cs` builds a real `RunTicker` — sixteen arguments, six of them scene objects, which is why nothing in the project had ever constructed one — and pins AR §18.1's order **by observation rather than by reading the method**. A recording `IRunSession` writes its intents where core writes them, and each adjacency becomes a consequence a reordering removes: core is told this frame's positions, not last frame's; the flag it raised last frame is down when it is ticked; the arena it is asked a fact about has already moved, which the real cone sweep confirms through physics; and the shove written from *inside* the cone answer still reaches the body. **One step is deliberately not asserted — `CommandPhase`** — because it reaches core only through the Input System, which `Soulvail.Tests.PlayMode` does not reference, and moving it costs a frame of latency rather than a wrong outcome. | ~~M2-09~~ · ~~M2-11b~~ | ~~Medium.~~ Closed. |
| ~~9~~ | ~~**Spawn-position occupancy is unmodelled** — nothing stops two spawns landing on the same point.~~ **Answered by [M2-05](tasks/M2-05-spawn-director.md):** a point is **claimed** from the moment it is telegraphed until `TelegraphTime` after its body appears, and a candidate must be `MinSpawnSeparation` (2 m) from every claimed point — which subsumes the same-point case, since a point is nought metres from itself. **The claim expires on a clock rather than being held for the enemy's life**, because a Husk walks away from where it arrived: holding it any longer would make a small arena run out of places to put things. | ~~M2-05~~ | ~~Low.~~ Closed. |
| ~~11~~ | ~~**`HapticsSettings` persists through `PlayerPrefs`** as an explicit stopgap.~~ **Closed by [M2-13b](tasks/M2-13b-local-json-save-store.md):** `FromStore(ISaveStore)` replaces `FromPlayerPrefs()`, `PrefsKey` and `FromPlayerPrefs` are **deleted and pinned deleted** by a reflection row, and the key is abandoned rather than migrated — nothing has shipped, so the population to carry over was the owner's dev machine. The class kept the shape its own comment promised since M1-20, which `HapticsListenerTests` proves by being untouched. **One thing the row did not say and the task had to decide: `FromStore` does not read.** A factory that loaded would have to block on I/O or return before the value it promised had arrived, so it starts at GD §16.3's default and `BootFlow` — the one place in the app entitled to wait on a disk — hands the stored profile over through `Apply`, which deliberately writes nothing back. | ~~M2-13b~~ | ~~Low.~~ Closed. |
| ~~12~~ | ~~**A focus tap beyond `acquireRange` is silent.**~~ **Closed by [M2-12a](tasks/M2-12a-threat-arrows-and-held-focus.md) and verified at M2-15.** The owner's M2-00d ruling was built as specified: `TargetChanged.HeldFocusId` carries the enemy the player actually tapped, and `ReticleView` parks a second, dimmer, non-pulsing marker on it, so *held but out of range* is a state the screen has rather than a tap that looks like a miss. **This is the CC §8 row M1 could not tick**, and M2-15's playtest is the first time it has been answered yes — on Editor evidence, like the rest of that checklist. `acquireRange` was not raised and the focus is still not held regardless of range, so M1-04's turn-away rejection stands untouched. | ~~M2-12a~~ | ~~Medium.~~ Closed. |
| ~~13~~ | ~~**Cover does not block enemy projectiles.**~~ **Answered by [M2-11a](tasks/M2-11a-arena-contract-and-pool.md) (the layer) and closed by [M2-11b](tasks/M2-11b-line-of-sight-sense.md) (the sense).** The ruling held: a *sense*, not a fact, so core still holds no walls. `LineOfSightSense` caches one answer per enemy, refreshed round-robin at 10 Hz inside the `PathRefreshBudget` M2-05 already built — **5 raycasts a frame at 28 enemies and 60 fps, 10 at 30**, exactly the price this row quoted — and `SnapshotBuilder` fills `EnemySense.HasLineOfSight` from it. A Spitter with a pillar in the way plants, faces, and **does not begin the wind-up**, which keeps AR §18.4's *a telegraph is a promise* intact; an aim already running still fires, because M2-07b rule 7 is unchanged. Three things the row did not say and the task had to decide: the ray is the project's **one 3D perception** and became a named exception in AR §18.4; an unmeasured sight line means **can see**, because the other way round switches the archetype off in silence; and `EnemyViews` **stopped writing the field**, because a census that knows only where its own bodies are is not entitled to state what is between them. | ~~M2-11a~~ · ~~M2-11b~~ | ~~Medium.~~ Closed. |
| ~~14~~ | ~~**A Spitter can damage the player from off-screen.**~~ **Closed by [M2-12a](tasks/M2-12a-threat-arrows-and-held-focus.md) and verified at M2-15.** A threat outside the frustum is pointed at from the border in red-orange, the held focus in cyan, and an enemy that is both gets both — so GD §12.4's on-screen rule has the visible edge indicator it demands, and the Spitter's 14 m stand-off no longer breaks it. Deciding this with row 12 in one spec was right for the reason the row gave: the two indicators share a border rect, a palette and a thumb-corner exclusion, and retrofitting either to the other would have produced two that disagreed. | ~~M2-12a~~ | ~~Medium.~~ Closed. |

## M3 — Levelling and the tree

**Goal:** a run gets *better* as it goes — XP, a tree with real choices, skills that fire, and a level-up screen that interrupts the fight well.
**Done when:** M3-15's checklist passes and `m3` is tagged.

**Specs first, as M2 did, and for the reason M2 proved rather than assumed.** Writing all fifteen before M3-01 starts is what let M2 split five tasks *before* they began instead of after, and a split before starting costs a paragraph while a split after costs a re-review. The groups below are themed rather than sequential, because a group is a review: the tree as data is one argument, the screens are another.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| M3-00a | Specs for M3-01…M3-05 — the tree as data: XP, specs, rules, offers, effects | S | — | ☑ |
| M3-00b | Specs for M3-06…M3-08 — casting and choosing: runner, auto/manual, level-up screen | S | 00a | ☑ |
| M3-00c | Specs for M3-09…M3-12 — the screens and the first nodes | S | 00a | ☐ |
| M3-00d | Specs for M3-13…M3-15 — legibility, content validation, acceptance | S | 00a | ☐ |

Everything those specs must absorb is in the [carry-forward ledger](#carry-forward-into-m3) below. **A spec that does not name its ledger rows is not finished.**

**Specced by M3-00a:** the tree as data, M3-01…M3-05. **Two splits, both before starting.** M3-01 splits at the format seam — XP and levels are one review, the save bump is another, and ledger row 2's whole point is that the bump gets its own; M3-02 splits at the core/authoring seam, because the three specs, their four ScriptableObjects and the boot lists are eleven files together. **Build order is not id order:** 01a → 01b → 05 → 02a → 02b → 03 → 04, because a node carries an `IEffect` before it can be a spec, and a tree restores its nodes before its hit points. Two rulings the group hangs on, both the owner's at M3-00a: **the tree is layered** — two nodes a tier, four tiers and a keystone, nine a branch, twenty-seven a class (M3-02a rule 7) — and **CH §5.2 ships as authored** with its depth divergence flagged (M3-01a rule 9).

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M3-01a](tasks/M3-01a-xp-and-levels.md) | `XpCurve`, `LevelTracker`, and the kill that pays for a level | M | — | ☐ |
| [M3-01b](tasks/M3-01b-save-format-v2.md) | Save format v2: what a levelled run writes down, and the first real migration | S | 01a | ☐ |
| [M3-05](tasks/M3-05-effect-registry.md) | Effect primitives: the registry, and `ModifyStat` as the first of them | M | 01a | ☐ |
| [M3-02a](tasks/M3-02a-skill-specs.md) | `SkillSpec`, `TriggerSpec`, `SkillTreeSpec`: the tree as data | M | 05 | ☐ |
| [M3-02b](tasks/M3-02b-skill-authoring.md) | Skill authoring: the four definitions and the boot lists | M | 02a | ☐ |
| [M3-03](tasks/M3-03-tree-rules.md) | `TreeRules` and `SkillTree`: gating, availability, keystones, and the nodes a save carries | M | 02a 05 01b | ☐ |
| [M3-04](tasks/M3-04-offer-generator.md) | `OfferGenerator`: three from the available, weighted for variety, from the `Offers` stream | S | 03 | ☐ |

**Specced by M3-00b:** casting and choosing, M3-06…M3-08. **Two splits, both before starting, and both at a seam a previous task already argued.** M3-07 splits at the **format seam** — the toggle is one review and the save bump is another, which is ledger row 2's rule and M3-01a/M3-01b's shape — because the owner ruled at M3-00b that **Auto/Manual and slot order survive a kill from recents**, and M3-01b reserved nothing for them on purpose. M3-08 splits at the **core/screen seam**: the port, the lazy draw, Overflow and the pause gate are one argument, and three cards are another. Three owner rulings the group hangs on, all at M3-00b: **the toggles are saved (v3)**; **pause means the tick is gated, not clocked at zero** — GD §11.4's *"idle the simulation"*, read literally; and **two pending picks are one screen that re-draws**, not two screens. A fourth question answered itself: AR §6 has listed `CastSkill(slot)` and `SetAutoCast` on `IPlayerCommands` since M0-09, so a manual cast is a player command and never a progression one.

**Build order is ID order here**, and the group as a whole waits on M3-04: the runner needs a tree to be handed a skill by, and the offer needs a generator to draw from.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M3-06](tasks/M3-06-skill-runner.md) | `SkillRunner`: cooldowns with the 40 % floor, auto-cast over the blackboard | M | 02a 03 05 | ☐ |
| [M3-07a](tasks/M3-07a-auto-manual-and-slots.md) | Auto/Manual, the four manual slots, and the two commands | S | 06 | ☐ |
| [M3-07b](tasks/M3-07b-save-format-v3.md) | Save format v3: the loadout on disk, and the first chain with two steps | S | 07a 01b | ☐ |
| [M3-08a](tasks/M3-08a-level-up-flow-and-pause.md) | `IProgressionCommands`, the lazy offer, Overflow, and the gate that idles the simulation | M | 04 03 06 | ☐ |
| [M3-08b](tasks/M3-08b-level-up-screen.md) | The level-up screen: three cards, a tap, and the frame that starts again | M | 08a | ☐ |

**The seven below are titles until 00c–d write them** — no sizes, no dependencies, no boxes, because a size is a claim about a Files table that does not exist yet. What they inherit from 00a is listed in [PROGRESS → Next task](PROGRESS.md#current-state). **Two of them grew at M3-00b**: M3-09 inherits CC §5.1's View Tree toggle (it builds the tree view for pause anyway, so building it twice was the alternative), CC §6.2's full-slots prompt and CC §6.2's drag-to-reorder; M3-13 inherits a fourth palette reader.

| ID | Task |
|---|---|
| M3-09 | Pause → Skills screen |
| M3-10 | Skill buttons S1–S4 with radial fills |
| M3-11 | First Oathbound actives: Consecrate, Bulwark (+ views) |
| M3-12 | Oathbound tree v1: ~12 nodes with `LocKey`s |
| M3-13 | Health-bar treatment: tint, elite bars, boss bar stub (GD §16.2) |
| M3-14 | Content validation tests: unique IDs, LocKeys present, tree well-formed |
| M3-15 | M3 acceptance, tag `m3` |

### Carry-forward into M3

**What M3's specs must absorb.** Opened at M2-15 with eight rows, on [M2's terms](#carry-forward-into-m2): every row names the task that must deal with it, and a row leaves only when its owner's *As built* says it is answered. **No row was carried from M2 — all fourteen closed** — so these are new: five are misses or measurements from M2-15's own checklist, two are parking-lot entries that have acquired an owner, and one is the open decision M2 could not make for itself. **Row 9 was opened by M3-00b**, which is what a spec group is for: writing a screen found a thing the milestone cannot judge itself on. Ranked by what it costs to fix later rather than now. **Two are already closed:** row 3 by M2-15a before any spec was written, row 7 by M3-00a with a hook; rows 1 and 2 have their arithmetic and their fields from M3-00a and wait on the tasks that build them.

| # | Finding | Owner | Cost of leaving it |
|---|---|---|---|
| 1 | **The TTK invariant breaks at stage 15, not at stage 30 where M2-15 looked.** GD §12.4 wants a basic enemy dead in 3–5 hits *at every depth*. Measured against the shipped assets — Husk 36 HP, `h(n) = min(1 + 0.06(n−1), 4)`, Oathbound weapon 13 — it is **3 hits at stage 1, 5 at stage 14, 6 at stage 15, and 8 at stage 30**. With the Focus ramp held at its maximum (×1.3 → 16.9) the band survives to stage 23 and is still 6 hits at 30. **Nothing is wrong with §12.3's curve; the gap is that no player power exists yet**, which is precisely what M3 adds. GD §12.5 wants +8–12 % effective DPS per stage from the tree, and **that target is now a number this row can be checked against** rather than a sentence. The danger is shipping a tree tuned by feel that happens not to close it, and discovering at M4 that stage 20 is unkillable for reasons nobody connects to the tree. **M3-00a did the arithmetic** ([M3-01a](tasks/M3-01a-xp-and-levels.md) rule 9): under the shipped curve the player gets **≈ 1.3 picks a stage from 1 to 30**, so GD §12.5's +8–12 % a stage means **the average pick is worth ≈ +6–9 % effective DPS**, or the damage-touching half of the tree ≈ +12–18 % each; a Husk needs +2 % / +70 % at stage 15 and +52 % / +153 % at stage 30 to die in five / three hits. M3-12 authors against those numbers and owes a TTK test at 1 / 15 / 30. | **M3-01a** (the curve), **M3-12** (the nodes), gated by **M3-15** | **High.** It is the whole point of the milestone, and it is the one row with a formula on both sides — the fix is checkable before it is playable. |
| 2 | **M3's tree is the first thing that ever has to migrate a save.** `RunSnapshot` is v1 with ten fields and an empty migration chain; the moment a run carries a level, spent points or chosen nodes, `CurrentVersion` goes to 2 and [M2-13b](tasks/M2-13b-local-json-save-store.md)'s chain test is what refuses to let the step be skipped. **The harness exists and has never been exercised for real** — `Chain_IsUnbrokenFromOldestToCurrent` currently loops over a range of one. The trap is adding the field and the migration in different PRs: the first one merges green, because a v1 file still decodes. **M3-00a named the fields: `Level`, `Xp`, `PendingLevelUps`, `TakenNodeIds` — and ruled one bump, v2, carrying all four** ([M3-01b](tasks/M3-01b-save-format-v2.md) rule 1). The fourth is written empty for two tasks until M3-03 fills it, which trades against M2-13a rule 4 and says so; the alternative was a second step in the chain, for ever, for a format nobody has shipped. Auto/Manual toggles are deliberately *not* reserved — if M3-07 wants them to survive a kill, that is a v3 by the same rule. | ~~M3-00a~~ · **M3-01b** (the bump, with the step and both fixtures) · **M3-03** (fills the fourth field, bumps nothing) | **High.** A format bump done wrong is silent until a player's save is already broken, and this is the only milestone where the chain is still short enough to get right cheaply. |
| ~~3~~ | ~~**The physics-sync decision is the owner's and it is still open.**~~ **Ruled by the owner and closed by [M2-15a](tasks/M2-15a-physics-sync.md), before any M3 spec was written** — which is what the row asked for. `Physics.SyncTransforms()` now sits between the bodies step and the fact phase, so AR §18.1's ordering is true of the code and not only of the call order; a cone or dash sweep resolves against where the bodies stand *this* frame. **Three consecutive PlayMode runs at 11/11**, against 9/11 immediately before — repetition rather than a single green run, because the failure was intermittent and that is what made it read as a flaky fixture for four tasks. The project setting was **not** flipped: one flush at the one seam costs less than `autoSyncTransforms` on every transform write in the game. **What is not closed is the price** — the flush has never been measured on a phone, and that sits with the other device rows. | ~~The owner~~ · ~~M2-15a~~ | ~~High, and rising.~~ Closed. |
| 4 | **The device rows have never run, and the debt now spans three milestones.** Multi-touch, haptics, touch latency, real frame rate, thermal, kill-from-recents, and **the Profiler's no-per-frame-`GC.Alloc` check carried unmet since M1-21**. `m0`, `m1` and `m2` are all tagged on Editor evidence. **M2 added the first row that is a *correctness* question rather than a feel one** — a save surviving a process death the Editor has never performed, against an atomic write built for exactly that case and never tested against it. | **M3-15** to re-state the grain; the first hardware session to clear it | **High and compounding.** Each milestone adds rows and none are retired, so the first device session gets larger and its failures get harder to attribute to the milestone that caused them. |
| 5 | **`PlayerAnimatorView` has no tests, and M3-11 is what promotes it.** It landed at M2-art without a spec and so without the behaviour-rules ↔ tests pairing the protocol asks for. The parking lot said it is promoted "the moment anything else drives an Animator", and Consecrate and Bulwark are views that will. The derived `AttackSpeed` and the "no flinch on a blocked hit" rule are both testable in isolation. | **M3-11** | Medium. Two untested Animator drivers is where the pattern sets. |
| 6 | **GD §16.4's palette still has no file.** [M2-12b](tasks/M2-12b-telegraph-rings.md) reads `#FF4A1F` from `ThreatArrows.Danger` rather than copying it — one place for the number, at the price of making `Views` and `Presentation` mutually dependent, which is legal inside one assembly and untidy. **M3-13 is the next thing to need the palette** (health-bar tint, elite bars, the boss-bar stub), and it is a third reader rather than a second. One shared `Palette` file fixes it. **M3-00b added a fourth, and it lands first:** [M3-08b](tasks/M3-08b-level-up-screen.md) rule 8 tints an offer card by CH §4's four node kinds from four serialized `Color` fields, named as placeholders rather than as a second palette, because inventing `Palette` two tasks early and unspecified is the worse of the two mistakes. One shared file still fixes it; the row's scope is now four readers and is known before it is worked. | **M3-13** | Medium. Cheap now, and the alternative is the number being copied on the third use, which is where palettes stop being one. |
| ~~7~~ | ~~**A branch that is not a numbered task bypasses the `ProjectSettings` guard.** M2-15 found `URPProjectSettings.asset` carrying `m_ProjectSettingFolderPath: URPDefaultResources` — Unity backfilling a default, exactly the [Traps §5](../Traps.md) behaviour the pre-commit check exists to catch. It came in on `m2-art-character-import`, the one M2 branch with no spec and no session protocol behind it. Harmless in itself; **the finding is the gap, not the line.** The protocol says "before every commit, check `git diff ProjectSettings/`" and that instruction only ever reaches a session that is following a spec.~~ **Closed by M3-00a, with a hook rather than a habit:** `.githooks/pre-commit` now refuses a staged `ProjectSettings/` change unless `ALLOW_PROJECT_SETTINGS=1` is set, listing the files and pointing at Traps §5, and warns about an *unstaged* one on every commit — so the check reaches every branch, spec or none, and a backfilled line has to be committed on purpose. M2-11a's `Cover` layer is what the override is for. The backfilled line itself is left where it is: harmless, and reverting it is a `ProjectSettings/` change of its own. | ~~M3-00a~~ | ~~Low to fix, medium to leave.~~ Closed. |
| 8 | **GD §7.3's 40–75 s stage band has never been measured.** M2-15's checklist asked for stages 1, 5 and 10 *timed, not estimated*; the playtest returned a verdict on the loop rather than three stopwatch readings, so the band is carried as accepted-by-feel rather than verified. **M3 changes the thing it measures** — a level-up screen interrupts a stage, so the number after M3 is not the number before it. **M3-00b split the number in two and M3-15 must say which it is quoting** ([M3-08a](tasks/M3-08a-level-up-flow-and-pause.md) rule 11): the pause **gates the tick**, so a paused frame contributes no `Dt` and `RunState.Time` counts *play* seconds only, while a stopwatch counts play **plus** every screen. The two diverge by however long a player deliberates, which at GD §13.1's one level per 25–90 s is not a rounding error. | **M3-15** | Low now, medium later. Measuring it for the first time *after* the interruption lands means never knowing which of the two the number describes. |
| 9 | **A node's name and description are `LocKey`s and nothing in the build resolves one.** `ILocalizer` is an AR §6 port whose adapter (`TableLocalizer`) is M6-10's, so [M3-08b](tasks/M3-08b-level-up-screen.md) rule 7 draws `spec.NameKey.Value` — the card reads `skill.oathbound.consecrate`, not "Consecrate". That is ADR-0012 working as designed and it is **not** a third place raw English is typed into a prefab. **What it costs is a verdict:** GD §13.1's *"every node description must be readable in under 2 seconds on a phone"* and CH §5.1's whole case for random-from-available rest on the player reading three cards fast, and neither can be judged against keys. M3-15 has to decide whether to accept the milestone on keys, pull a minimal localizer and one English table forward, or hand the question to M6-10 and accept that M3's acceptance never tested it. | **M3-15** to rule; **M6-10** if the answer is to wait | Medium. The screen is built either way; what is at risk is signing off a readability claim nobody has been able to test. |

## M4 — First boss and run end *(titles only)*

| ID | Task |
|---|---|
| M4-01 | Boss agent framework: phases at 66/33 %, telegraph system, add-spawn hook |
| M4-02 | Warden of Ash behaviours (GD §9.2) |
| M4-03 | Boss arena hazard + boss view |
| M4-04 | Segmented boss HUD bar |
| M4-05 | Death → `RunEnded` → Shard payout; `PlayerProfile` persisted |
| M4-06 | Run-end screen |
| M4-07 | M4 acceptance, tag `m4` |

## M5 — Second class *(titles only)*

| ID | Task |
|---|---|
| M5-01 | Projectile weapon type + leading seam (CC §3.7) |
| M5-02 | Gravecaller spec + Bone Bolt |
| M5-03 | Shroudstep + corpse decoy |
| M5-04 | Wights: minion agents, friendly registry, Rise passive |
| M5-05 | Wight views + concurrency policy (CH §8.1) |
| M5-06 | Gravecaller tree v1 + Exhume, Tether, Rot Nova |
| M5-07 | Class select screen |
| M5-08 | M5 acceptance, tag `m5` |

## M6 — Systems complete *(titles only)*

| ID | Task |
|---|---|
| M6-01 | Essence wallet + drops from events |
| M6-02 | Sanctum shop core: reroll, banish, heal, cleanse |
| M6-03 | Sanctum screen |
| M6-04 | Veilrot meter, thresholds, the Claiming (GD §10) |
| M6-05 | Pact node variants in `OfferGenerator` (GD §13.2) |
| M6-06 | Ordeals (GD §13.4) |
| M6-07 | Emberwright spec, Cinder Orb, Blink, Kindling |
| M6-08 | Emberwright tree v1 + actives |
| M6-09 | Class unlocks: Shards + achievements |
| M6-10 | `ILocalizer`, `TableLocalizer`, English tables |
| M6-11 | M6 acceptance, tag `m6` |

## M7 — Content pass *(titles only)*

| ID | Task |
|---|---|
| M7-01 | Lunger, Weaver, Warden (core + views) |
| M7-02 | Elites + affixes |
| M7-03 | Choirmother |
| M7-04 | All 81 nodes |
| M7-05 | Ashen Reach: 8–12 arenas + art |
| M7-06 | Drowned Choir: 8–12 arenas + art |
| M7-07 | Audio layers, telegraph cues, boss music |
| M7-08 | M7 acceptance, tag `m7` |

## M8 — Feel, perf, ship *(titles only)*

| ID | Task |
|---|---|
| M8-01 | Game-feel checklist: hit-stop, shake slider, low-HP vignette (GD §16.3) |
| M8-02 | Options + accessibility: control modes, mirror, sliders, colourblind palettes (GD §18) |
| M8-03 | Device tiering + device-independence rule (GD §11) |
| M8-04 | Frame-rate setting, thermal validation |
| M8-05 | Balance pass per class against the death horizon (GD §12.5) |
| M8-06 | Store build, signing, V1 tag |

---

## Parking lot

Unscheduled. **One item, one line: what it is and what promotes it.** History lives in the archive; an item that acquires an owning task becomes a ledger row.

- ~~**`PlayerAnimatorView` has no tests.**~~ **Promoted at M2-15** — M3-11's Consecrate and Bulwark
  views are the "anything else drives an Animator" this was waiting for. Now [M3 ledger row 5](#carry-forward-into-m3).
- **CH §5's tree table says 8 nodes a branch and 27 a class, and 3 × 8 is 24.** The layered shape
  ruled at M3-00a — two a tier × four tiers + a keystone = 9 a branch — is what makes 27 true, so the
  row wants a one-line correction to *9 (8 + 1 Keystone)* and *all 8 preceding*. Flagged, not made,
  the GD §12.1 precedent. Promoted by **M3-12**, which authors the shape.
- **CH §5.2's curve cannot hit its own table past stage 10** against GD §12.1's quadratic budget
  with flat XP per kill: the level at the end of stages 10 / 20 / 35 is 14 / 27 / ≈ 50 against the
  table's 13 / 22 / 30, and the tree fills by stage 20 rather than 30. Ruled at M3-00a: ship 1.4 and
  measure ([M3-01a](tasks/M3-01a-xp-and-levels.md) rule 9 pins the divergence). The fix is one
  exponent in `Descent.asset` (≈ 1.6 fits 5 / 10 / 20) and the CH §5.2 line. Promoted by **M3-15**'s
  stopwatch or **M8-05**.
- **A tree editor for `SkillTreeDefinition`.** Three nested arrays in the default Inspector is enough
  for twelve nodes. Promoted when **M7-04** authors eighty-one by hand and it hurts.
- **A counting `IRandom` fake in `Tests/Core/Fakes/`.** `SpawnDirectorTests` has a private one and
  M3-04 will have a second. Promoted by the third.
- **`handslot.l` / `handslot.r` are empty**, so the Knight swings a fist. The 31 props in
  `ThirdParty/KayKit/Adventurers/Props` are built to parent there. Promoted when the weapon-ownership
  question (class property vs swappable) is settled, because the answer decides who owns the socket.
- **The KayKit skeletons are the enemy roster, and M2-06 decided not to import them yet:**
  `Skeleton_Minion` → Husk, `Skeleton_Warrior` → Elite, `Skeleton_Mage` → Spitter, `Skeleton_Rogue`
  → Lunger, all on `Rig_Medium` so all 139 clips already play on them. **The pack has no Bloater**,
  and three bodies means a prefab, a controller and a `ViewPool` each — an M2-art-sized task.
  [M2-06](tasks/M2-06-enemy-authoring.md) rule 9 buys legibility with a per-archetype tint and
  scale on the one shared body instead, which is what GD §11.3 asks for anyway. Promoted when the
  owner brings enemy art in, the way M2-art was brought in. `Rig_Medium_Special`'s
  `Skeletons_Awaken_Floor` is a diegetic spawn telegraph that would replace
  [M2-12b](tasks/M2-12b-telegraph-rings.md)'s ring decal.
- **Arenas 3 through 12.** [M2-11a](tasks/M2-11a-arena-contract-and-pool.md) rule 11 ships two —
  the contract's proof, not its content — against GD §7.2's target of 8–12 per biome. Promoted by
  M7-05/06's art pass, or the day a playtest says two rooms is where a run starts feeling repetitive.
- **`EnemyRegistry` could prefer a free agent whose behaviour already matches the requested kind**,
  which would make [M2-07b](tasks/M2-07b-spitter-ai.md) rule 4's one-object-per-changed-rental churn
  rare in a mixed arena. Promoted by a profile that says so, not by a hunch — it is a spawn-path
  allocation, and AR §14's ban is about the frame path.
- **The class speed band contradicts the shipped Oathbound.** The owner retuned it 5.4 → 3 m/s at M2-03 (with the Husk 3.5 → 2), but [GD §6.1](../GameDesign.md)'s *"speed range across classes 5.4–6.2 m/s"* and [Characters.md §3](../Characters.md)'s `140 / 5.4` row still carry the old band — so the starter class now sits below its own stated floor. **Not changed with the assets, deliberately:** moving the band is a statement about the Gravecaller (5.6) and the Emberwright (6.2), neither of which is built, and whether the whole band scales by ~0.55 or the Oathbound simply becomes the slow class is a design call. Promoted by the owner's ruling, or by **M5-02**, which is the first task that has to author a second class's speed.
- **The asset-pinning rows assert number pairs where the invariant is a *ratio*.** `EnemyDefinitionTests` says "a Husk must be outrunnable" in a comment and then pins 2 and 3 separately; the M2-03 retune moved both and the row went red for a change that preserved the margin exactly (1.543× → 1.5×). A row asserting `oathbound.Speed / husk.MoveSpeed > 1` — the shape `Oathbound_ToSpec_HasWeapon`'s DPS-floor assertion already uses — would have stayed green and would go red for the change that actually matters. Blocker: it reads two assets across two fixtures, so it needs a home.
- **CI: EditMode tests on every PR** (GitHub Actions + `game-ci/unity-test-runner`). Worth it since M1-21 — 455 tests, and M2 adds migration tests, which rot silently. Blocker: a Unity licence activation secret, not the value.
- **PR template** mirroring a spec's Acceptance section. Not adopted yet.
- **A real Android device.** Every **[device]** row is deferred until one exists and the first hardware session runs them all. BlueStacks cannot run the APK (M0-20a), so there is no fallback outside the Editor.
- **Company name is a placeholder** — `Soulvail`, set in M0-19 with the same status as the application identifier. Both are permanent once uploaded to a store, so M8-06 changes them together before the first upload.
- **Machine-local Gradle configuration is not in this repo** — `~/.gradle/gradle.properties` (HTTP proxy) and `~/.gradle/init.gradle` (Aliyun mirrors) are what make an Android build resolve on this connection; a second machine needs its own. The why, including the SOCKS-vs-HTTP trap, is [Traps.md §10](../Traps.md).
- **Four raw UI strings to localise in M6-10** — `"Soulvail"` and `"Descend"` in `Menu.unity` (M0-17), and `"You died"` and `"Tap to return"` on `Hud.prefab`'s `DeathOverlay` (M1-17). Between them they are the whole of the raw user-facing English in the project; they become `LocKey`s when `ILocalizer` and the English tables land. The HP readout is deliberately *not* on this list — `"{0:0}/{1:0}"` is a number format rather than a sentence, and it survives localisation unchanged.
- **Application identifier** — placeholder `com.soulvail.dev`; permanent once uploaded, so it changes in M8-06 before the first store build.
- ~~**Out-of-range focus tap**~~ → **closed at M2-15** as [ledger row 12](#carry-forward-into-m2); built by M2-12a, ruled at M2-00d, and the CC §8 row it was blocking now ticks. History in the [M1 archive](archive/PROGRESS-M1.md) (M1-09, M1-21).
- ~~**Already-merged specs still point at pre-split task ids.**~~ **Fixed at M2-15, in one pass, exactly as this entry predicted** — the fourteen references in [M2-01](tasks/M2-01-clock.md), [M2-02](tasks/M2-02-mode-spec.md), [M2-05](tasks/M2-05-spawn-director.md), [M2-07a](tasks/M2-07a-projectile-system.md) and [M2-10](tasks/M2-10-stage-flow.md) now name the task that actually shipped the work. **The class outlived the instances and is the part worth keeping:** it recurs on every split, nothing in the protocol checks it, and the reason it was cheap this time is that one task was already reading every spec's *As built* to close the ledger. The next milestone that splits a task owes the same pass to its own acceptance.
- **A spatial hash for `AlliesNearby`** — its `n² − n` comparisons a frame cost 756 at M2-04's cap of 28 and 4,032 at the registry's 64. Promoted the day a device cap above 40 ships (M8-03), and not before.
- ~~**GD §12.1's stage-40 budget row says 1,772 where its own formula gives 1,876.9.**~~ **Corrected at M2-15:** the row now reads **1,877** and the ratio beneath it **47×**. The code was never wrong and was pinned against being "fixed" in three places — `ThreatBudgetTests.Budget_Stage40_FollowsFormulaNotTable`, `ModeDefinitionTests.Descent_CarriesDesignScaling` and [AR §18.3](../Architecture.md#183-numbers-and-identity) — which is the only reason a two-year-old arithmetic slip in a design doc never reached an asset. **The doc was the last copy still disagreeing**, and M2-15's behaviour rule 1 is that doc and asset never do.
- Business model decision (GD §21.1) — needed before M6.
- Google Play Games save sync (GD §21.6) — after M2's local save exists.
- `dotnet` SDK on the dev machine → activates the pre-commit format check.
- GPU Resident Drawer evaluation — when a stage first drops below 60 fps on mid-tier.
- Separate `.NET` class library for Core (better tooling) — if Unity-hosted tests ever feel slow.

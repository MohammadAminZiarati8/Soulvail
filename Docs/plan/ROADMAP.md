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
| **M3** | **Levelling and the tree** | XP, tree rules, offers, level-up screen, SkillRunner + auto-cast, effect primitives, first Oathbound nodes, health-bar treatment | 15 |
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
| [M2-06](tasks/M2-06-enemy-authoring.md) | The Spitter and the Bloater as authored data, and three archetypes you can tell apart | M | 04 | ☐ |
| [M2-07a](tasks/M2-07a-projectile-system.md) | `ProjectileSystem`: shots already in the air, and who decides they landed | S | 06 | ☐ |
| [M2-07b](tasks/M2-07b-spitter-ai.md) | `IEnemyBehaviour`, and the Spitter that keeps its distance | S | 07a | ☐ |
| [M2-08](tasks/M2-08-bloater-ai.md) | `BloaterBehaviour`: a fuse you have to walk away from, and a blast the corpse owns | S | 07b | ☐ |
| [M2-09](tasks/M2-09-projectile-views-and-pooling.md) | Projectile views, and the first test that proves a pooled body forgets its last life | M | 07b | ☐ |

**Specced by M2-00d:** stage flow, arenas and telegraphs, M2-10…M2-12. **Both M2-11 and M2-12 are split before they start.** M2-11 is the arena as a *place* and the arena as something core can *ask about*, which is seven files together and two different reviews; the sense also has to land after the pillars it raycasts against exist. M2-12 is split by machinery, not by ledger row — rows 12 and 14 stay together in 12a because [the ledger](#carry-forward-into-m2) says answering them apart gets two indicators that disagree, while the ground decals share M2-09's pool and none of that question.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M2-10](tasks/M2-10-stage-flow.md) | `StageFlow`: arrival, seal, clear, a door that opens once — and `RespawnPolicy` retired | M | 05 04 02 | ☐ |
| [M2-11a](tasks/M2-11a-arena-contract-and-pool.md) | The arena as a contract: a pool of them, a barrier, and a door | M | 10 | ☐ |
| [M2-11b](tasks/M2-11b-line-of-sight-sense.md) | `LineOfSightSense`: a pillar you can hide behind, and the frame order under test | S | 11a 07b | ☐ |
| [M2-12a](tasks/M2-12a-threat-arrows-and-held-focus.md) | Screen-edge threat arrows, and a focus that reads as held | M | 07b | ☐ |
| [M2-12b](tasks/M2-12b-telegraph-rings.md) | The ring that says *something is about to happen here* | S | 05 08 09 | ☐ |

**Specced by M2-00e:** persistence, resume and acceptance, M2-13…M2-15. **Both M2-13 and M2-14 are split before they start.** M2-13 was four production files and four test files in one row — the port and the DTOs are a review about *what a run is*, and the JSON, the atomic write and the migration harness are a review about *how it is spelled on disk*; the second cannot be judged without the first being settled. M2-14 splits at the seam between writing and reading: 13a…14a can all be verified by looking at a file, while 14b is the menu, the container and the six-field `RunConfig` sweep. [Ledger row 1](#carry-forward-into-m2) is answered across three of them — captured in **14a**, restored in **14b**, and made sayable at all by **13a**'s format v1, which is the last free moment for it.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M2-13a](tasks/M2-13a-save-port-and-dtos.md) | `ISaveStore`, the DTOs, and the stream position that makes a resume honest | M | 01 02 | ☐ |
| [M2-13b](tasks/M2-13b-local-json-save-store.md) | `LocalJsonSaveStore`: format v1 on disk, an atomic write, and the migration harness | M | 13a | ☐ |
| [M2-14a](tasks/M2-14a-snapshot-at-the-boundary.md) | The snapshot at the boundary: what a run writes, when, and what it forgets | M | 13a 10 | ☐ |
| [M2-14b](tasks/M2-14b-resume-and-continue.md) | Resume: a `Continue` that means it, and `PendingRun.Clear`'s first caller | M | 14a 13b | ☐ |
| [M2-15](tasks/M2-15-acceptance-and-tag.md) | M2 acceptance, the ledger closed, tag `m2` | S | everything | ☐ |

### Carry-forward into M2

**What M2's specs must absorb.** Fourteen rows to begin with, **nine open** — rows 2, 3, 4, 5 and 9 were answered by M2-03, M2-02, M2-04 and M2-05 and struck. Their numbers are not reused, so a PROGRESS entry that cites "row 2" still points at the right thing. Four are measurements M1 took, seven are findings from the M0+M1 audit (2026-09-10), which checked spec-against-code across all 42 tasks and found the two milestones sound — these are the exceptions. Row 12 is the parking lot's one behavioural open item, moved here once it had an owner; rows 13 and 14 were added at M2-00c as the named price of ledger row 7's ruling. **Every row names the task that must deal with it, and a spec is not finished until it says how.** A row leaves this table when its owner's *As built* says it is answered. Ranked by what it costs to fix later rather than now.

| # | Finding | Owner | Cost of leaving it |
|---|---|---|---|
| 1 | **Random streams expose no state.** `IRandomStream` has only draw methods and `Pcg32._state` has no accessor, so a resumed run restores the seed but restarts **every stream at draw 0** — a resumed stage re-draws spawn positions it already used, and once M3-04's offers ride the Offers stream, **killing the app becomes a free reroll**. Recorded in neither ADR-0011 nor ADR-0007. **Ruled by the owner at M2-00e: `IRandom.Capture()` / `Restore(in RandomState)`**, a pair on the port rather than the row's own `ulong State { get; set; }` per stream — a settable position on `IRandomStream` is reachable from every core system that draws ([M2-13a](tasks/M2-13a-save-port-and-dtos.md) rule 7). No `IRandom.Reseed`, which settles M2-02 rule 5's deferred question by making it unnecessary. **The capture must precede the composition of the stage it describes** ([M2-14a](tasks/M2-14a-snapshot-at-the-boundary.md) rule 5) or the payoff is lost. | **M2-13a** (the port and the DTO), **M2-14a** (capture), **M2-14b** (restore) | **Highest.** Two members on one port and one struct in the DTO cost nothing at format v1; after v1 ships it is a migration plus a live exploit. **The row leaves when 14b's *As built* says so.** |
| ~~2~~ | ~~**`Stat` removes modifiers by source reference only** — no "drop everything" — and `EnemyAgent` is recycled with its `Health` intact.~~ **Answered by [M2-03](tasks/M2-03-threat-budget.md):** `Stat.RemoveAll()` with no argument, called by `EnemyAgent.Initialise` on all three of an agent's stats before it re-bases them. The agent-owned source token was rejected because a token covers only its own source, so M7-02's affixes and M3's debuffs would each have to be listed at the recycle point — and that list gets one entry short. **A recycled agent forgets everything**, foreign sources included, which is a rule that cannot be half-applied (AR §18.1). | ~~M2-03~~ | **Closed.** |
| ~~3~~ | ~~**`EnemySystem.SpawnAll` runs *after* `RunStarted`**, so an unauthored archetype mid-plan throws with the run announced and enemies standing.~~ **Answered by [M2-02](tasks/M2-02-mode-spec.md):** `Start` resolves the mode, the class, and every archetype the plan, the respawn policy **and the mode's roster** name — all before `RunStarted`. The roster walk is the half with no other line of defence, since nothing spawns from a roster until M2-05 and the failure would otherwise arrive forty seconds into a run. | ~~M2-02~~ | **Closed.** |
| ~~4~~ | ~~**`AlliesNearby` is `n² − n` comparisons a frame** over the *registered* count — free at 12, **3,540 a frame at 60**.~~ **Answered by [M2-04](tasks/M2-04-wave-composer.md):** the cap was chosen against the number rather than inferred, and the arithmetic is written down where the cap lives (`BootInstaller.DeviceEnemyCap`). At 28 it is **756 comparisons a frame**, 1,560 at GD's high tier of 40 and 4,032 at the snapshot's capacity of 64 — so the O(n²) stays and **a cap above 40 needs a spatial hash first** (parking lot, M8-03's if a high tier ever ships). | ~~M2-04~~ | **Closed.** |
| ~~5~~ | ~~**Path refresh is hard-capped at 4 a frame against a 10 Hz cadence, so routes go stale silently above 24 concurrent enemies.** Nothing reports it at runtime.~~ **Measured by [M2-04](tasks/M2-04-wave-composer.md)** — 24 sustained at 60 fps, **12 at 30** — and **fixed by [M2-05](tasks/M2-05-spawn-director.md):** `PathRefreshBudget.ForFrame(activeCount, dt)` = `ceil(n · refreshHz · dt)`, floored at 1 and capped at 16, which is **5 a frame at 60 fps and 10 at 30** for 28 enemies — the same 280 recomputes a second however the frames are sliced. The ceiling of 16 is what stops a hitch feeding itself, and when it binds the shortfall is *reported* rather than lost: `NavPathSense.StalePathCount` counts the enemies overdue by more than a period, and `DebugOverlay` shows it. **The row's real complaint was never the number 4 — it was that nothing said the ceiling had been reached**, which is why the fix is a budget *and* a counter. | ~~M2-04~~ · ~~M2-05~~ | ~~Medium.~~ Closed. |
| 6 | ~~**`RunConfig` is two fields**~~ — **five as of [M2-02](tasks/M2-02-mode-spec.md)**: mode, class, seed, stage, plan, swept across nineteen call sites in one PR. The seed now arrives *in*, and `RunSession.Start` throws when it disagrees with the injected `IRandom` — one truth, checked rather than inherited. **What is left is the sixth field**, `RunSnapshot Restore`, whose DTO does not exist until M2-13a. | ~~M2-02~~ · **M2-14b** (the sixth, `Restore`) | Low, and now bounded. The reshape that would have cost four sweeps is spent; adding one field to a five-field class is a field. |
| 7 | **The fact-versus-direct-call rule is unsettled.** `IRunSession`'s comment still promises a `ReportContact` fact for chasers; M1-18 instead had `ChaserBehaviour` call `PlayerCombat.ApplyDamage` from core-perceived distance. Both are defensible — **but three enemies must not answer it three ways.** **Ruled at M2-00c** ([M2-07a](tasks/M2-07a-projectile-system.md) rule 1): core decides every enemy outcome and calls `ApplyDamage` directly; Unity owes a *fact* only when the answer depends on colliders core cannot see, and a *sense* when the geometric question is a standing one. `IRunSession` gains no member in M2 and the stale comment goes with it. | **M2-07a** (the ruling and the comment), applied by **M2-07b** and **M2-08** | Medium. Divergence here is the kind that never gets unpicked. The row leaves when M2-07a's *As built* says the comment is fixed. |
| 8 | **Views and `RunTicker`'s frame order have no automated coverage at all.** The adapter layer is well covered; `Views/`, most of `Presentation/` and the frame order have nothing. `EnemyView.OnDespawn` — which the code itself calls "the most dangerous method here" — is exercised only through a fake `IPoolable`. Its reset chain was **verified complete by hand** during the audit, so this is a coverage gap, not a live bug. **Split at M2-00d:** the pooled-view half is [M2-09](tasks/M2-09-projectile-views-and-pooling.md) rule 10, the frame-order half is [M2-11b](tasks/M2-11b-line-of-sight-sense.md) rules 10–12, which is the task that adds a step to the order and so makes asserting it worth a file. | **M2-09**, **M2-11b** | Medium, rising. One PlayMode rent → damage → kill → despawn → re-rent test covers the whole family, and M2 triples the number of pooled things. |
| ~~9~~ | ~~**Spawn-position occupancy is unmodelled** — nothing stops two spawns landing on the same point.~~ **Answered by [M2-05](tasks/M2-05-spawn-director.md):** a point is **claimed** from the moment it is telegraphed until `TelegraphTime` after its body appears, and a candidate must be `MinSpawnSeparation` (2 m) from every claimed point — which subsumes the same-point case, since a point is nought metres from itself. **The claim expires on a clock rather than being held for the enemy's life**, because a Husk walks away from where it arrived: holding it any longer would make a small arena run out of places to put things. | ~~M2-05~~ | ~~Low.~~ Closed. |
| 10 | **`PendingRun.Clear()` still has no caller**, and needs a "the run has read everything it needs" point that does not exist yet. **Answered at M2-00e:** the last reader is `RunTicker.Start`, immediately after `_session.Start` returns — `RunInstaller.CreateRandom` resolves the seed earlier, during `RunSession`'s construction, so clearing in the installer would pull the value out from under the ticker ([M2-14b](tasks/M2-14b-resume-and-continue.md) rule 9). | **M2-14b** | Low. Resume is where that point finally exists. |
| 11 | **`HapticsSettings` persists through `PlayerPrefs`** as an explicit stopgap. **Answered at M2-00e:** `FromStore(ISaveStore)` replaces `FromPlayerPrefs()`, the class keeps the shape its own comment promised, and the `PlayerPrefs` key is abandoned rather than migrated — nothing has shipped, so the population to carry over is the owner's dev machine ([M2-13b](tasks/M2-13b-local-json-save-store.md) rule 8). | **M2-13b** | Low. Move it onto `ISaveStore` as the first consumer. |
| 12 | **A focus tap beyond `acquireRange` is silent.** `FocusResolver` has no range limit and `Targeter` honours the override only inside `acquireRange` (CC §3.4 as built in M1-04 — rightly), but `TargetChanged` carries nothing that lets the reticle show *held but inactive*, so the tap is indistinguishable from a miss, which CC §3.5 exists to prevent. Owner-playtested in M1-09 and again in M1-21 with chasers closing the distance; **the only CC §8 row failing on behaviour rather than on missing hardware.** Three ways out, cheapest first: a fourth reticle state (one field on `TargetChanged`, one branch in `ReticleView`, one publish site in `PlayerCombat`); hold the focus regardless of range (reintroduces the turn-away M1-04 rejected); raise `acquireRange` (moves auto-targeting everywhere). **Ruled by the owner at M2-00d: the fourth reticle state**, spelled as `TargetChanged.HeldFocusId` and a second, dimmer, non-pulsing marker parked on the enemy the player actually tapped ([M2-12a](tasks/M2-12a-threat-arrows-and-held-focus.md) rules 1–3). | **M2-12a** | Medium. Threat arrows are the next thing that answers "where is the thing you cannot see"; decide both at once rather than retrofit one to the other. |
| 13 | **Cover does not block enemy projectiles.** GD §7.2 makes it an arena design rule — *"cover blocks enemy projectiles but not pathing"* — and row 7's ruling means core holds no walls, so a Spitter shoots through a pillar. The cheapest honest fix is a **sense, not a fact**: `EnemySense.HasLineOfSight` has been carried unfilled since M0-05, and a `LineOfSightSense` adapter on `NavPathSense`'s pattern (a population-scaled per-frame budget, ~5 raycasts a frame at 28 enemies) lets a Spitter simply not fire through one. Rejected: the projectile view raycasting and reporting a block, which is the fact route row 7 declined; and core holding a wall list, which is a second world model. **Ruled by the owner at M2-00d: the sense.** [M2-11a](tasks/M2-11a-arena-contract-and-pool.md) rule 9 puts the pillars on a `Cover` layer so there is something to be true about; [M2-11b](tasks/M2-11b-line-of-sight-sense.md) fills the sense and a Spitter will not begin a wind-up it cannot see through. **The row leaves when 11b's *As built* says so**, not 11a's. | **M2-11a** (the layer), **M2-11b** (the sense) | Medium. It is where pillars stop being scene dressing and become an arena contract, so the raycast has something to be true about. Cheap now, and the field it fills already exists. |
| 14 | **A Spitter can damage the player from off-screen.** It fires from 14 m, which is past the play camera's comfortable frame behind the player, and GD §12.4's on-screen rule forbids damage originating outside the frustum without a visible edge indicator — *"tighter than the PC version of this rule, a phone screen shows less."* The first archetype that can break it; the Husk had to walk into view to hurt anybody. **Answered with row 12 in one spec at M2-00d** ([M2-12a](tasks/M2-12a-threat-arrows-and-held-focus.md) rules 5–9): one screen-edge arrow renderer, one border rect that dodges GD §12.4's thumb corners, and GD §16.4's palette carrying the distinction — red-orange for a threat, cyan for the held focus. An enemy that is both gets both. | **M2-12a** | Medium. Same task and the same session as row 12: both are "where is the thing you cannot see", and answering them separately gets two indicators that do not agree. |

## M3 — Levelling and the tree *(titles only)*

| ID | Task |
|---|---|
| M3-01 | `XpCurve` + level-up events (CH §5.2) |
| M3-02 | `SkillSpec`, `SkillKind`, `SkillTreeSpec` + authoring SOs |
| M3-03 | `TreeRules`: gating, availability, keystone requirements (CH §5) |
| M3-04 | `OfferGenerator`: 3 from available, variety weighting, `Offers` stream |
| M3-05 | Effect primitives + `EffectRegistry` (ADR-0009) |
| M3-06 | `SkillRunner`: cooldowns (40 % floor), auto-cast triggers over `CombatBlackboard` |
| M3-07 | Auto/Manual toggle, manual slots (max 4), commands |
| M3-08 | Level-up screen: pause, 3 offers, tap, View Tree toggle |
| M3-09 | Pause → Skills screen |
| M3-10 | Skill buttons S1–S4 with radial fills |
| M3-11 | First Oathbound actives: Consecrate, Bulwark (+ views) |
| M3-12 | Oathbound tree v1: ~12 nodes with `LocKey`s |
| M3-13 | Health-bar treatment: tint, elite bars, boss bar stub (GD §16.2) |
| M3-14 | Content validation tests: unique IDs, LocKeys present, tree well-formed |
| M3-15 | M3 acceptance, tag `m3` |

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

- **`PlayerAnimatorView` has no tests.** It landed without a spec and so without the behaviour-rules
  ↔ tests pairing the protocol asks for. Promoted the moment anything else drives an Animator — the
  derived `AttackSpeed` and the "no flinch on a blocked hit" rule are both testable in isolation.
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
- **Out-of-range focus tap** → [ledger row 12](#carry-forward-into-m2), owner M2-12a, ruled at M2-00d. History in the [M1 archive](archive/PROGRESS-M1.md) (M1-09, M1-21).
- **Already-merged specs still point at pre-split task ids.** Splitting before starting is working, but nothing back-fills the forward references in specs that shipped earlier: [M2-05](tasks/M2-05-spawn-director.md) says *"arena spawn-point authoring — M2-11"*, and [M2-01](tasks/M2-01-clock.md), [M2-02](tasks/M2-02-mode-spec.md), [M2-07a](tasks/M2-07a-projectile-system.md) and [M2-10](tasks/M2-10-stage-flow.md) each point at `M2-13` or `M2-14`, neither of which is a task any more. None is *wrong* — the id names the family and this map resolves it — and M2-00d left the M2-11/M2-12 ones for the same reason: four one-line edits in a PR whose Files table does not cover them is worse than one line here. **Fixed in one pass, by the first task that has a reason to open those files anyway** — most likely M2-15, which is already reading every spec's *As built* to close the ledger. The class matters more than the instances: it is the staleness M2-00f's audit was about, and it recurs on every split.
- **A spatial hash for `AlliesNearby`** — its `n² − n` comparisons a frame cost 756 at M2-04's cap of 28 and 4,032 at the registry's 64. Promoted the day a device cap above 40 ships (M8-03), and not before.
- **GD §12.1's stage-40 budget row says 1,772 where its own formula gives 1,876.9** (stages 5, 10 and 20 all agree). **M2-03 built the formula and pinned it in three places** — `ThreatBudgetTests.Budget_Stage40_FollowsFormulaNotTable`, `ModeDefinitionTests.Descent_CarriesDesignScaling` and [AR §18.3](../Architecture.md#183-numbers-and-identity), which says not to "fix" the code to match the table. The table row and the "44×" beneath it (the real ratio is ~46.9×) still want a one-line correction from the owner.
- Business model decision (GD §21.1) — needed before M6.
- Google Play Games save sync (GD §21.6) — after M2's local save exists.
- `dotnet` SDK on the dev machine → activates the pre-commit format check.
- GPU Resident Drawer evaluation — when a stage first drops below 60 fps on mid-tier.
- Separate `.NET` class library for Core (better tooling) — if Unity-hosted tests ever feel slow.

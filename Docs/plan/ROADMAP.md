# Soulvail — Roadmap

**The map.** Milestones → tasks, one line each. Where we are *right now* lives in [PROGRESS.md](PROGRESS.md). What each task is exactly lives in [tasks/](tasks/).

## How to read this

- One milestone at a time. Every milestone ends with something **playable on a phone** and a tag on `main`.
- A task is **one branch, one PR, 1–5 files.** Size S = 1–3 files, M = 4–5, L = must be split before starting. Size counts **new code files and substantial rewrites**; small additive edits to existing files (a new field on a spec, a registration, an event struct) are listed in the spec but don't count — they're trivial to review.
- Task IDs are `M<milestone>-<nn>`. Branch = lowercase ID + slug: `m0-03-domain-events`. PR title = the spec's H1.
- **Full specs exist for the current and next milestone only.** Later milestones are titles + one-line goals; they get specced when the current milestone is ~75% merged, using what we learned.
- Tick a box when the PR is **merged into `dev`**, not when the code is written.
- Splits keep the parent ID: `M0-07a`, `M0-07b`.

**Spec pointers:** GD = [GameDesign.md](../GameDesign.md) · CH = [Characters.md](../Characters.md) · CC = [CoreCombat.md](../CoreCombat.md) · AR = [Architecture.md](../Architecture.md) · ADR = [adr/](../adr/)

## Milestones

| | Milestone | Ends when | Tasks |
|---|---|---|---|
| **M0** | **Walking skeleton** | Floating stick → core motor → intent → capsule moves in a grey box **on the phone**, through VContainer scopes, with events/snapshot/intent plumbing real and tested | 20 |
| **M1** | **Combat feel** | Stat, Health/Aegis, targeting + reticle, tap-to-focus, Censer, Charge, Focus, chaser dummies. [CC §8](../CoreCombat.md) checklist passes on device | 21 |
| **M2** | **Stage loop** | Mode as data, threat budget, director, Husk/Spitter/Bloater, arenas, seal/gate, run persistence across app kill | 15 |
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
| [M0-19](tasks/M0-19-android-build.md) | Player settings, `AndroidBuild` script, APK on device | S | 13 16 17 18 | ☐ |
| [M0-20](tasks/M0-20-acceptance-and-tag.md) | M0 device acceptance, tuning, tag `m0` | S | all | ☐ |

---

## M1 — Combat feel

**Goal:** the moment-to-moment loop — target, swing, dodge — feels good on device with untextured capsules.
**Done when:** [CC §8](../CoreCombat.md) checklist passes on device and `m1` is tagged.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M1-01](tasks/M1-01-stat-and-modifier.md) | `Stat` + `Modifier` stack: Flat → PercentAdd → PercentMult, by source, cached, describable | S | M0 | ☐ |
| [M1-02](tasks/M1-02-health-and-aegis.md) | `Health`, `ShieldSpec`, `DamageResult`: HP, Aegis recharge, hit i-frames | M | 01 | ☐ |
| [M1-03](tasks/M1-03-target-scorer.md) | `TargetCandidate`, `TargetingSpec`, `TargetScorer` | M | 01 | ☐ |
| [M1-04](tasks/M1-04-targeter.md) | `Targeter`: cadence, immediate retarget, focus override, all-blocked state | S | 03 | ☐ |
| [M1-05](tasks/M1-05-enemy-entities.md) | `EnemySpec`, `EnemyAgent`, `EnemyBlackboard`, `EnemyRegistry` | M | 02 | ☐ |
| [M1-06](tasks/M1-06-enemy-system.md) | `EnemySystem`: spawn plan, snapshot ingestion, perception, enemy events | M | 05 | ☐ |
| [M1-07](tasks/M1-07-enemy-authoring-and-views.md) | `EnemyDefinition`, `Husk.asset`, `EnemyView`, `EnemyViews`, dummy prefab | M | 06 | ☐ |
| [M1-08](tasks/M1-08-player-combat.md) | `PlayerCombat`: health, targeting, `CombatBlackboard`, combat events; `RunSession` composes | M | 02 04 06 | ☐ |
| [M1-09](tasks/M1-09-tap-to-focus-and-reticle.md) | `IPlayerCommands.FocusTarget/ClearFocus`, 3 m resolver, `TapToFocusAdapter`, `ReticleView` | M | 07 08 | ☐ |
| [M1-10](tasks/M1-10-weapon-and-cone-intent.md) | `WeaponSpec`, `Weapon` cadence + damage frame, `ConeHitIntent` | M | 08 | ☐ |
| [M1-11](tasks/M1-11-cone-hits-to-damage.md) | `ReportConeHits` fact → damage → `EnemyDamaged` / `EnemyDied`, despawn | S | 10 | ☐ |
| [M1-12](tasks/M1-12-cone-overlap-and-hit-feedback.md) | `ConeOverlapQuery`, `RunTicker` fact loop, hit flash, dissolve death | M | 07 11 | ☐ |
| [M1-13](tasks/M1-13-focus-ramp.md) | `FocusSpec`, `FocusTracker` → fire-rate modifier; ground glow | M | 10 | ☐ |
| [M1-14](tasks/M1-14-charge-skill.md) | `MovementSkillSpec`, `ChargeSkill` (pure): cooldown, buffer, i-frame window | S | 02 | ☐ |
| [M1-15](tasks/M1-15-charge-integration.md) | `ChargeIntent`, `IPlayerCommands.MovementSkill`, `ReportChargeHits`, knockback intent | M | 11 14 | ☐ |
| [M1-16](tasks/M1-16-charge-view-and-skill-button.md) | `PlayerView` charge motion + sweep, `SkillButton`, movement-skill input | M | 15 | ☐ |
| [M1-17](tasks/M1-17-hud.md) | `HudPresenter`, HP bar with ghost trail, shield ring, cooldown, death → Menu | M | 08 16 | ☐ |
| [M1-18](tasks/M1-18-chaser-ai.md) | `ChaserBehaviour` FSM, `EnemyMoveIntent`, strike damage, telegraph event | M | 06 08 | ☐ |
| [M1-19](tasks/M1-19-pooling-respawn-navmesh.md) | `ViewPool`, `RespawnPolicy` (keep 12 alive), NavMesh bake + path sense | M | 12 18 | ☐ |
| [M1-20](tasks/M1-20-haptics.md) | `HapticsListener`, Android vibrator, toggle | S | 11 15 | ☐ |
| [M1-21](tasks/M1-21-acceptance-and-tag.md) | M1 acceptance: CC §8 on device, tuning, tag `m1` | S | all | ☐ |

---

## M2 — Stage loop *(titles only)*

| ID | Task |
|---|---|
| M2-01 | `IClock` + `UnityClock` (wall-clock for persistence) |
| M2-02 | `ModeSpec` + `ModeDefinition`; Descent as the first instance (GD §4.5) |
| M2-03 | `ThreatBudget` + scaling curves B(n), h(n), d(n), s(n) (GD §12) |
| M2-04 | `WaveComposer`: budget → composition, threat costs, concurrency cap, quality-over-quantity rule |
| M2-05 | `SpawnDirector`: waves, 25 % overlap, 6 m spawn safety, telegraph timing |
| M2-06 | `EnemySpec` + `EnemyDefinition` authoring: Husk, Spitter, Bloater |
| M2-07 | Spitter AI + projectile intent/fact |
| M2-08 | Bloater AI: explode on death/contact |
| M2-09 | Projectile views + pooling |
| M2-10 | `StageFlow` FSM: Arrival → Waves → Clear → Gate → next |
| M2-11 | Arena prefab contract, arena pool, barrier + gate views |
| M2-12 | Off-screen threat arrows, spawn telegraph rings |
| M2-13 | Persistence: DTOs, `ISaveStore`, `LocalJsonSaveStore`, versioning + migration test harness |
| M2-14 | Run snapshot at stage boundary, resume flow, app-kill handling |
| M2-15 | M2 acceptance, tag `m2` |

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

Unscheduled. Promote into a milestone when it earns it.

- **CI: EditMode tests on every PR** (GitHub Actions + `game-ci/unity-test-runner`; needs a Unity licence activation secret). Not adopted yet; revisit once the test suite is worth guarding — likely during M1.
- **PR template** mirroring a spec's Acceptance section. Not adopted yet.
- **A real Android device.** Every **[device]** checklist item is deferred until one exists; the first hardware session runs all of them.
- **Two raw UI strings to localise in M6-10** — `"Soulvail"` and `"Descend"` in `Menu.unity`, authored by M0-17 as the one place in the project where a raw user-facing string is allowed. They become `LocKey`s when `ILocalizer` and the English tables land.
- **Application identifier** — placeholder `com.soulvail.dev`; permanent once uploaded, so it changes in M8-06 before the first store build.
- Business model decision (GD §21.1) — needed before M6.
- Google Play Games save sync (GD §21.6) — after M2's local save exists.
- `dotnet` SDK on the dev machine → activates the pre-commit format check.
- GPU Resident Drawer evaluation — when a stage first drops below 60 fps on mid-tier.
- Separate `.NET` class library for Core (better tooling) — if Unity-hosted tests ever feel slow.

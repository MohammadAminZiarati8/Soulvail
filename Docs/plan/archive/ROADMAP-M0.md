# Soulvail — Roadmap archive: M0, Walking skeleton

_Moved verbatim from [ROADMAP.md](../ROADMAP.md) on 2026-09-20, when the closed milestones' tables and ledgers were archived to keep the live file to the milestone in progress. Links are re-based to this folder. A stub heading stays in the ROADMAP for every heading here, so links into them still resolve. The milestone's log is [PROGRESS-M0.md](PROGRESS-M0.md)._

---

## M0 — Walking skeleton

**Goal:** prove the architecture end to end on a real device with the thinnest possible slice: one input, one core system, one intent, one view.
**Done when:** every item in the [M0-20](../tasks/M0-20-acceptance-and-tag.md) device checklist passes and `m0` is tagged on `main`.

**Status: complete, tagged on Editor evidence.** Every architectural claim M0 set out to prove is verified — 153 EditMode + 3 PlayMode tests green, all three scenes playable, zero analyzer warnings — and the feel question is answered. Two things are carried as explicit debt rather than met: the APK does not yet launch outside the Editor ([M0-20a](../tasks/M0-20a-apk-runs-on-bluestacks.md), diagnosed to BlueStacks' Vulkan-through-houdini bridge, not to game code), and the four `[device]` rows have no hardware to run on.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M0-01](../tasks/M0-01-project-skeleton.md) | Project skeleton: folders, five assembly definitions, VContainer 1.19.0 | M | — | ☑ |
| [M0-02](../tasks/M0-02-state-machine.md) | `StateMachine<T>` + core-purity guard test | S | 01 | ☑ |
| [M0-03](../tasks/M0-03-domain-events.md) | Domain events: `IDomainEvents`, `DomainEventHub`, `RecordingEvents` | M | 01 | ☑ |
| [M0-04](../tasks/M0-04-random-streams.md) | `IRandom` with named streams, `SeededRandom`, `FixedRandom` fake | M | 01 | ☑ |
| [M0-05](../tasks/M0-05-world-snapshot.md) | `WorldSnapshot`, `EnemySense`, `Num` vector conversions | M | 01 | ☑ |
| [M0-06](../tasks/M0-06-intents.md) | `PlayerMoveIntent`, `IIntentSink`, `IntentBuffer` | M | 05 | ☑ |
| [M0-07](../tasks/M0-07-player-motor.md) | `MovementSpec`, `PlayerMotor`: accel/decel, no inertia, facing | S | 01 | ☑ |
| [M0-08](../tasks/M0-08-content-catalog.md) | `ContentId`, `LocKey`, `CharacterSpec`, `ContentCatalog` | M | 07 | ☑ |
| [M0-09](../tasks/M0-09-run-contracts.md) | `IRunSession`, `RunConfig`, `RunState`, run events | M | 05 06 07 08 | ☑ |
| [M0-10](../tasks/M0-10-run-session.md) | `RunSession` + `RecordingIntents` fake + tests | S | 03 04 09 | ☑ |
| [M0-11](../tasks/M0-11-character-authoring.md) | `CharacterDefinition` SO, `Oathbound.asset`, conversion + validation tests | S | 08 | ☑ |
| [M0-12](../tasks/M0-12-composition-installers.md) | `BootInstaller`, `RunInstaller`, `PendingRun`, container tests | M | 10 11 | ☑ |
| [M0-13](../tasks/M0-13-scopes-and-scenes.md) | `BootScope`, `RunScope`, `SceneLoader`, `BootFlow`; Boot/Menu/Run scenes | M | 12 | ☑ |
| [M0-14](../tasks/M0-14-input-actions.md) | `Soulvail.inputactions` + generated class + `InputAdapter` | S | 01 | ☑ |
| [M0-15](../tasks/M0-15-floating-stick.md) | `StickShaper` (pure) + `FloatingStick` control + HUD prefab | M | 14 | ☑ |
| [M0-16](../tasks/M0-16-snapshot-builder-player-view.md) | `SnapshotBuilder`, `PlayerView`, `RunTicker`, player + grey-box prefabs | M | 06 12 14 | ☑ |
| [M0-17](../tasks/M0-17-menu-stub.md) | `MenuScope`, `MenuPresenter`, Descend button | S | 13 | ☑ |
| [M0-18](../tasks/M0-18-camera-and-debug-overlay.md) | `FollowCamera`, `DebugOverlay` | S | 16 | ☑ |
| [M0-19](../tasks/M0-19-android-build.md) | Player settings, `AndroidBuild` script, APK on device | S | 13 16 17 18 | ☑ |
| [M0-20](../tasks/M0-20-acceptance-and-tag.md) | M0 device acceptance, tuning, tag `m0` | S | all | ☑ |
| [M0-20a](../tasks/M0-20a-apk-runs-on-bluestacks.md) | Make the APK actually run on BlueStacks (release build / Vulkan deny filter) | S | 19 20 | ☐ |

---


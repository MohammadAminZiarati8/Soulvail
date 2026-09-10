# M1-20 — `HapticsListener`, Android vibrator, toggle

**Size:** S · **Depends on:** M1-11, M1-15 · **Branch:** `m1-20-haptics`
**Design refs:** CC §5.6 (touch requirements), GD §16.3 (feel checklist: haptics with an off switch)

## Goal

The cheapest game-feel win on mobile: a light pulse when you hit, a heavy one when you're hit, a medium one on Charge — as a pure event subscriber that gameplay never knows about, with an off switch.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/AndroidVibrator.cs` | Game | `IVibrator`; Android `VibrationEffect` via `AndroidJavaObject`; no-op elsewhere |
| `Game/Presentation/HapticsListener.cs` | Game | Events → pulses, rate-limited |
| `Tests/Game/Presentation/HapticsListenerTests.cs` | Tests.Game | Mapping + rate limit with a fake vibrator |
| *small edits* | | `BootInstaller` registers `IVibrator` (Android → `AndroidVibrator`, else `NullVibrator`) and `HapticsSettings` (PlayerPrefs-backed `Enabled`, default true — temporary until `ISaveStore` M2-13); `RunScope` registers the listener as an entry point |

## Public API

```csharp
namespace Soulvail.Game.Adapters;

public interface IVibrator { void Pulse(int milliseconds, float amplitude01); }
public sealed class AndroidVibrator : IVibrator { }
public sealed class NullVibrator : IVibrator { }

public sealed class HapticsSettings { public bool Enabled { get; set; } }   // persists via PlayerPrefs for now
```

```csharp
namespace Soulvail.Game.Presentation;

public sealed class HapticsListener : IStartable, IDisposable
{
    public HapticsListener(DomainEventHub hub, IVibrator vibrator, HapticsSettings settings);
}
```

## Behaviour

1. Mapping: `PlayerDamaged` (not blocked) → 60 ms @ 1.0 · `PlayerDamaged{Blocked}` → nothing · `EnemyDamaged` → 15 ms @ 0.4 · `EnemyDied` → 30 ms @ 0.7 · `ChargeStarted` → 30 ms @ 0.6.
2. Rate limit: at most **one pulse per 100 ms** overall; when several events land in the same window, the strongest (highest amplitude) wins and the rest are dropped. Without this, a swing through six enemies buzzes like a phone on a table.
3. `settings.Enabled == false` → no calls to the vibrator at all.
4. `AndroidVibrator`: `getSystemService("vibrator")`; API ≥ 26 → `VibrationEffect.createOneShot(ms, amplitude × 255)`; older → `vibrate(ms)`. Wrapped in try/catch — a device without a vibrator must never throw. `NullVibrator` everywhere except Android player builds.
5. Subscriptions are taken in `Start` and released in `Dispose` (scope teardown).

## Tests

| Test | Given / When / Then |
|---|---|
| `PlayerDamaged_HeavyPulse` | fake vibrator / publish PlayerDamaged / one Pulse(60, 1.0) |
| `Blocked_NoPulse` | — / PlayerDamaged{Blocked} / none |
| `EnemyDamaged_LightPulse` | — / publish / Pulse(15, 0.4) |
| `RateLimit_StrongestWins` | EnemyDamaged then PlayerDamaged within 50 ms / — / exactly one Pulse, amplitude 1.0 |
| `RateLimit_ResetsAfterWindow` | pulse at 0; event at 0.11 / — / second Pulse |
| `Disabled_NoPulses` | Enabled false / any events / none |
| `Dispose_Unsubscribes` | disposed / publish / none |

The listener takes a `Func<float> clock` (defaulting to `Time.realtimeSinceStartup`) so tests control time.

## Manual verification (device)

1. Swing through a crowd — a soft tick per swing, not a buzz-storm.
2. Get hit — a firm thump. Blocked hit during Charge i-frames — nothing.
3. Charge — a medium pulse at the start.
4. Turn haptics off in a temporary Editor toggle (or `PlayerPrefs` key) → silence.

## Acceptance

- [x] All tests green — 452 EditMode (11 new), 3 PlayMode
- [x] Zero errors, zero new analyzer warnings — clean recompile of every assembly, empty Console
- [ ] Manual steps verified on device — **all four are outstanding and cannot be run here**: the Editor resolves `NullVibrator` by construction, and there is no phone (M0-20a)
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- An options screen — M8-02. Audio — M7-07.
- iOS haptics.

## As built

**Files.** Five rather than four: `Game/Adapters/AndroidVibrator.cs` (`IVibrator`, `AndroidVibrator`, `NullVibrator`), `Game/Adapters/HapticsSettings.cs`, `Game/Presentation/HapticsListener.cs`, `Tests/Game/Presentation/HapticsListenerTests.cs`, plus small edits to `BootInstaller`, `RunScope` and `InstallerTests`.

**Deviations.**

1. **`HapticsSettings` has its own file** rather than sitting in `AndroidVibrator.cs` where this spec's `Public API` block implies it. One class per file is the convention, and a settings toggle buried in a vibrator driver is unfindable. `IVibrator` and `NullVibrator` do share the vibrator's file, as the Files table describes.
2. **The listener is `IStartable, ITickable, IDisposable`.** Forced by this spec's own tests: `RateLimit_StrongestWins` cannot be satisfied by an immediate pulse, since nothing knows which event in a window is strongest until the window closes. Pulses are therefore deferred to the end of their window and a heartbeat is what flushes them — which means **every pulse is up to 100 ms late, isolated ones included.** Deliberate, and the top row of the device debt below.
3. **`HapticsSettings` has no public constructor** — `FromPlayerPrefs()` persists, `InMemory(bool)` does not. A test must not be able to turn the developer's own haptics off by accident.
4. **Eleven tests, not seven.** All seven rows are present; added `EnemyDied_MediumPulse`, `ChargeStarted_MediumPulse` (rule 1's remaining clauses), `RateLimit_WeakerInWindow_Dropped` and `DisabledMidWindow_NoPulse`.
5. **One row added to `InstallerTests`** — `Boot_ResolvesHaptics_NullVibratorOffAndroid`, outside the Files table. A missing registration would be indistinguishable from a phone that does not buzz much.
6. **The clock is passed by `RunScope`** despite having a C# default: VContainer never falls back to one (M1-19).

**Verified here.** 452 EditMode + 3 PlayMode tests green; clean recompile of all assemblies with an empty Console. The PlayMode row `Descend_StartsARun_AndRefusesASecondTap` is what proves the `RunScope` entry-point registration composes — no EditMode test can reach a `LifetimeScope`.

**Not verified, and not verifiable here.** Everything in *Manual verification (device)*. The Editor resolves `NullVibrator` by construction, so the mapping, the window and the off switch are proven only against a fake vibrator. Whether 100 ms of coalescing reads as *late* is the first question for the first phone.

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

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] Manual steps verified on device
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- An options screen — M8-02. Audio — M7-07.
- iOS haptics.

## As built

_Filled at merge._

# PL-02c — Howl drawn: the howl, the glow, the pulse

**Size:** M · **Depends on:** PL-02b · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Howl); GD §16.5 (attack speed up is a buff); readable at first look

## Goal

On a howl every standing hound throws its head back, glows faintly cyan while its haste lasts, and
a cyan pulse rolls out from the lead.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Events/PetEvents.cs` | Core | `PackHowled` gains `Seconds` (the haste's length); views have no other way to see the private buff |
| `Game/Views/PetView.cs` | Game | `Howl(seconds, now)`: reuses BM-07e's `Roar` trigger (the `Howl` state); a faint cyan glow until `now + seconds`, cut on fall |
| `Game/Views/PetViews.cs` | Game | Subscribes `PackHowled`; every standing pet's view howls |
| `Game/Views/HowlPulseView.cs` | Game | **New.** A cyan ring from the lead's position to 6 m in 0.5 s, fading; pooled |
| `Prefabs/Vfx/HowlPulse.prefab` | — | **New.** Unlit, `Palette.Player` |
| `Game/Composition/RunScope.cs` | Game | `[SerializeField]` pulse prefab beside `_roarRingPrefab`; registers the view; null throws naming it |
| `Scenes/Run.unity` | — | Assigns the pulse prefab |
| `Tests/Game/Views/HowlDrawnTests.cs` | Tests.Game | **New** |
| *small edit* | | `PetSystem.Howl` publishes `seconds` in `PackHowled` |

## Behaviour

1. `PackHowled` carries the haste's `Seconds`; a refreshing howl publishes again with the new length.
2. On `PackHowled` every standing hound's view plays the howl; a fallen one does not.
3. Each howling hound glows faintly cyan until `Seconds` pass; a later howl extends it; a fall
   ends it.
4. A pulse rolls from the lead's position out to 6 m over 0.5 s, fading to zero, then pools.
5. Colours `Palette.Player` only. The heal shows as each bar climbing; no number pops.
6. A missing pulse prefab on `RunScope` fails loudly at scope build, naming the field.

## Tests

| Test | Given / When / Then |
|---|---|
| `PackHowled_CarriesSeconds` (1) | Core: a howl with 4 s → event `Seconds` 4 |
| `Howled_EveryStandingHoundHowls_FallenDoesNot` (2) |
| `Glow_EndsWithBuff_ExtendedByRehowl_CutOnFall` (3) |
| `Pulse_SixMetres_HalfSecond_ThenPooled` (4) |
| `Pulse_And_Glow_PlayerCyan` (5) |
| `RunScope_MissingPulsePrefab_Throws` (6) |

## Manual verification (Editor / device)

1. **[Editor]** Let a hound drop below half: every hound howls, the pulse rolls out, the hounds
   glow for 4 s, bars climb.
2. **[device]** The pulse reads at a glance and does not hide enemy tells.

## Out of scope

- Howl sound. Pack Tactics, Thick Pelts (PL-03).

## As built

_Filled at merge._

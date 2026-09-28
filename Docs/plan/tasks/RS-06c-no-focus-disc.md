# RS-06c — No Focus disc

**Size:** S · **Depends on:** RS-06b (stacked on it) · **Branch:** `rs-06c-no-focus-disc`
**Design refs:** CC §4.3, GD §16.4 · **Ledger rows:** none

The owner, 2026-09-28, playing the Ranger: *"if i stay still and wont move, an circle shows around me.
i want that to be disabled as well."* This reverses RS-06a's *"leave FocusGlow … alone"*.

## Goal

Standing still draws no cyan disc under the character. The Focus ramp it showed still runs, and
one tick brings the disc back.

## Why a switch on the view, off on the prefab

- **The circle is `FocusGlowView`**, a cyan disc on `Player.prefab` that grows over CC §4.3's
  Focus ramp. The ramp is core's (`FocusTracker`) and pays out as fire rate whether anything draws
  it or not, so this is a picture to hide, not a mechanic to remove.
- **Switched off, never removed** (the 2026-09-28 ruling that made Veilrot and the Sanctum
  switches, RS-05a–d). A `Show` tick on the view is one Inspector click to undo. Unhooking the disc
  would work too, but it hides the choice in a missing reference that looks like a wiring mistake.
- **For every class.** The disc sits on the shared body, and only the Ranger is playable. If a
  returning class wants its disc back and the Ranger does not, that is a per-class flag then.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/FocusGlowView.cs` | Game | `_show`, on unless a prefab says otherwise (rules 1, 2) |
| `Tests/Game/Views/FocusGlowViewTests.cs` | Tests.Game | **New.** Rules 1–3 |
| *small edits* | | `Prefabs/Player/Player.prefab` (`_show: 0`, rule 3) |

## Public API

```csharp
// FocusGlowView
[SerializeField] private bool _show = true;   // Inspector: "Show"
```

## Behaviour

1. **Switched off, the disc never shows.** With `_show` clear, no `FocusRampChanged` level shows it.
2. **Switched on, it is exactly as before.** Hidden at zero Focus, and at full Focus shown at
   1.5 m and 0.35 alpha. A view that never mentions the switch has it on.
3. **`Player.prefab` ships it off, with the disc still dressed**, so a tick brings it back. The Run
   scene's Player overrides neither field.

## Tests

| Test | Given / When / Then |
|---|---|
| `FocusGlowViewTests.Glow_SwitchedOffNeverShows` | `_show` clear / Focus 0.5, then 1 / disc hidden (rule 1) |
| `FocusGlowViewTests.Glow_SwitchedOnGrowsWithTheRamp` | the initialisers / Focus 1, then 0 / shown at 1.5 m; then hidden (rule 2) |
| `FocusGlowViewTests.Player_ShipsWithTheDiscOffAndStillDressed` | `Player.prefab` / read / `_show` false, `_glow` wired (rule 3) |

## Manual verification (Editor / device)

1. **[Editor]** Boot → Menu → the Ranger → descend, and stand still for two seconds. *Expected:* no
   cyan circle under the Ranger. It still shoots faster the longer it stands, as before.
2. **[Editor]** Shoot at a Rootling. *Expected:* no cyan wedge in front of the Ranger (RS-06a), and
   the white impact on a hit (RS-06b).
3. **[Editor]** To bring the disc back: `Prefabs/Player/Player.prefab` → Focus Glow View → tick
   *Show*.

## Out of scope

- The Focus ramp itself, and the Oathbound's wedge.
- A per-class disc.

## As built

**As specified.** The switch is the view's first field, so it is the first line in its Inspector.
Its initialiser is on and the prefab ships off, so `Player_ShipsWithTheDiscOffAndStillDressed` is
not the vacuous row Traps §7 warns of. `Player.prefab`'s diff is the one line `_show: 0`.

**The wedge the owner still saw was RS-06a's, not a second one.** They were playing
`rs-06b-an-arrow-hit-you-can-see`, which was built from `dev` beside RS-06a and so still flashed the
Censer's wedge. No other cyan arc exists in `Game/`. RS-06b was rebased onto RS-06a, and this task
stacks on both, so the checked-out branch now carries all three.

**Red-checked.** With `_show` dropped from `Apply`, `FocusGlowViewTests` went 3 / 2: exactly
`Glow_SwitchedOffNeverShows`.

**Verified, over RS-06a to RS-06c together.** EditMode 3 464 — 3 463 passed, 0 failed, 1 inconclusive
(the animator clock row) — which is RS-05d's 3 439 plus 5, 17 and 3. PlayMode 66 / 67: only
`Loop_ShootsOnlyStandingStill` (Traps §8), which went 29 / 29 with its fixture alone. Console: no
compile errors. Format check clean on the two C# files. `ProjectSettings/TimeManager.asset` reverted.

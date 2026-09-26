# RS-03f — The Descend row taps Descend

**Size:** S · **Depends on:** — · **Branch:** `rs-03f-the-descend-row`
**Design refs:** AR §15; M0-17; M5-07 rule 1; [RS-03e's *As built*](RS-03e-the-rangers-acceptance.md#as-built) ·
**Ledger rows:** none — and the [parking-lot](../ROADMAP.md#parking-lot) line on `run.json`, whose trigger this task is

## Goal

`BootSmokeTests.Descend_StartsARun_AndRefusesASecondTap` taps Descend and then a class card, and it
passes the same with a `run.json` on disk as without one.

## The diagnosis (RS-03e)

The row finds its button as `presenter.GetComponentInChildren<Button>()`, the first active `Button`
under `MenuPresenter`. In `Menu.unity` Continue comes before Descend, and `MenuPresenter.OnEnable`
switches Continue on only when `SavedRun.IsPresent`. So:

- **With a save it taps Continue**, whose handler disables both buttons and loads the run on disk.
  It is green on Continue's guard, and it plays whatever run the machine holds: at
  [M6-02a](../archive/PROGRESS-M6.md) it resumed the owner's run into a trigger `AC_Player` lacked.
- **Without one it taps Descend**, which has opened class select since M5-07 and has no guard by
  design (`MenuPresenter.Descend`'s remarks). It is red: *"Descend stayed interactable"*.

The game is right; the row predates M5-07's second screen. Reproduced on this branch before any
edit: `BootSmokeTests` ran 2 / 1 with no `run.json`.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/PlayMode/BootSmokeTests.cs` | Tests.PlayMode | The row taps the presenter's `_descend`, then an owned card, and asserts a fresh run |
| *small edits* | | `Tests/Game/Composition/ResumeFlowTests.cs` gains one row for Continue's guard (rule 4) |
| *docs* | | this spec; `PROGRESS.md`; `ROADMAP.md`, where RS-03f is ticked and the `run.json` parking-lot line becomes RS-03g (rule 6); `Traps.md` §8, added during the build (*As built*) |

No production code.

## Public API

None.

## Behaviour

1. **The row taps the button it names.** Descend is the `Button` in `MenuPresenter._descend`, read
   through reflection, not the first `Button` under the presenter. Every control the row touches is
   read from the field that wires it: the screen from `_classSelect`, the cards from `_cards`, a
   card's button from `_button`. Descend is interactable before the tap.
2. **Descend opens class select and loads nothing.** After the tap `ClassSelectPresenter.IsOpen` is
   true and the Menu is still the active scene (M5-07 rule 1).
3. **A card starts a fresh run and refuses a second tap.** The row taps the first `Owned` card,
   which is the Oathbound on any profile because the starter is never priced. Straight after it,
   `PendingRun` holds that card's class and no snapshot, so this is a fresh run and not a resume, and
   every shown card is non-interactable. The Run scene is active within 5 s, and nothing unexpected
   is logged. The second-tap claim stays on `interactable`, for the reason the row's remarks give.
4. **Continue's guard keeps a row.** With a save on disk, the smoke row was the only assertion that a
   tap on Continue disables both buttons. `ResumeFlowTests.Menu_ContinueRefusesASecondTap` asserts it
   in EditMode over a recording loader, where no disk decides the answer.
5. **The suite is green with and without a `run.json`.** One EditMode and one PlayMode pass with no
   `run.json`, and one of each with the owner's on disk. Both of the owner's files are backed up first
   and restored byte-identical.
6. **The parking-lot line on `run.json` is promoted, not discharged.** Its trigger is *"the next task
   that touches `BootSmokeTests`"*, which is this one. The row still starts a run through boot's
   `LocalJsonSaveStore`, and so does each of `RunBodyTests`' three. Sheltering the saves is a
   suite-wide change, so it becomes RS-03g.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `BootSmokeTests.Descend_StartsARun_AndRefusesASecondTap` | Boot → Menu, with or without a `run.json` / Descend, then the first owned card / the screen opened and nothing loaded; a fresh run of that class is pending; every card is dead; Run is active; nothing unexpected is logged (rules 1–3) |
| `ResumeFlowTests.Menu_ContinueRefusesASecondTap` | a save, the Menu enabled over a recording loader / Continue tapped / Continue and Descend both non-interactable, one load asked for (rule 4) |
| *the suite, twice* | no `run.json`, then the owner's / EditMode and PlayMode / green each time (rule 5) |

**Red checks.** Rule 1 is red on `dev` with no save (2 / 1, above). Rules 3 and 4 are each red-checked
by removing the guard they pin, in `ClassSelectPresenter.OnCardChosen` and `MenuPresenter.Continue`,
and restoring it from git.

## Out of scope

- **Sheltering the owner's saves from the PlayMode suite.** RS-03g (rule 6).
- **A PlayMode row for Continue end to end.** It needs a legal `run.json` written by the row, which is
  RS-03g's business. `ResumeFlowTests` covers the tap's two reads, and now its guard.
- **Renaming the row.** Its name is true again, and the plan cites it by name.
- **`MenuPresenter` exposing its buttons.** A read added for a test is a claim about the type; the
  reflection route is `ResumeFlowTests.Menu`'s.

## As built

**Rules 1–6 as written, and no production code.** The row reads `_descend`, `_classSelect`,
`_cards` and a card's `_button` through one `Field<T>` helper, which fails by name if a field is
renamed. It taps Descend and asserts the screen is up and the Menu still active. It then taps the
first `Owned` card and asserts a pending run of that class with no snapshot, and every shown card
dead, before the Run scene arrives. `ResumeFlowTests.Menu_ContinueRefusesASecondTap` taps Continue
over the recording loader and asserts both buttons dead and one load asked for.

**Rule 5: green both ways, on the rows this task owns.**

| `run.json` | EditMode | `BootSmokeTests` alone | PlayMode, full |
|---|---|---|---|
| none | 3 270: 3 269 / 0 / 1 inconclusive | 3 / 3 | 56 / 1, the sandbox row below |
| the owner's, a stage-1 Ranger run | 3 270: 3 269 / 0 / 1 inconclusive | 3 / 3 | 56 / 1, the same row |

After each pass with a save, `run.json` held a fresh Oathbound run in place of the owner's Ranger,
so the row went through Descend and a card and not through Continue. The inconclusive row is the
animator clock row, by construction ([Traps §3](../../Traps.md)).

**Red checks, each restored from git and recompiled.** On `dev`'s row with no save, before any edit:
`BootSmokeTests` 2 / 1, *"Descend stayed interactable"*. With `SetCardsInteractable(false)` taken out
of `ClassSelectPresenter.OnCardChosen`, the new row went 2 / 1 on *"A card stayed interactable while
the run was loading"*. With `_descend.interactable = false` taken out of `MenuPresenter.Continue`,
`Menu_ContinueRefusesASecondTap` failed on its Descend assertion.

**Finding: the sandbox row fails full passes while Unity is not the active application.** It is
Traps §8's row and not this task's. `RangerSandboxTests.Loop_ShootsOnlyStandingStill` failed, *"No
arrow on the run"*, in six full passes on this tree. It failed in one on `dev`'s code, with this
change stashed and recompiled, which went 55 / 2 with the old Descend row. It passed alone, in its
fixture alone (29 / 29) and after `BootSmokeTests` (32 / 32), and failed after `FrameOrderTests`
(46 / 1). The one full pass in which the Editor was the active application went 57 / 57. That pass
carried a temporary log line in the row, since reverted, which read `isFocused` true and the stick at
(1, 0). → [Traps §8](../../Traps.md).

**Deviations: 2.** *1.* `Traps.md` joined the Files table during the build, for the finding.
*2.* `RunBodyTests`' remarks still send the `run.json` write to the parking lot. The file is not in
this table, and RS-03g is the task that edits it.

**Rules ↔ rows.** 1–3: `Descend_StartsARun_AndRefusesASecondTap`. 4:
`Menu_ContinueRefusesASecondTap`. 5: the passes above. 6: the ROADMAP's RS-03g and its struck
parking-lot line.

**How it was checked.** The Console straight after each EditMode pass held 46 entries with no save.
With one, it held 47: the same rows plus the direct-Play seed warning left over from the PlayMode pass
before it. Each entry came from a passing negative-path row. Unity's compiler with the analyzers
reported zero warnings over the two recompiled test assemblies. `dotnet format` is clean over the two
files, and `TimeManager.asset` was reverted. The owner's `run.json` and `profile.json` were backed up
first and are byte-identical after.

**Not done here:** nothing is visible in the game, so there is no manual step.

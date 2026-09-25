# RS-03e — The Ranger's acceptance: hits to kill at stages 1–16, and the owner's play

**Size:** S · **Depends on:** RS-03d · **Branch:** `rs-03e-the-rangers-acceptance`
**Design refs:** GD §6.2, §12.3, §12.4, §12.5; CC §4.3; CH §5.2, §5.4; M6-11's instrument ·
**Ledger rows:** [2](../ROADMAP.md#carry-forward-into-m7) — a fourth curve for M8-05, not a discharge

## Goal

The Ranger's hits to kill a Husk at every stage from 1 to 16, measured in a played run on M6-11's
instrument against GD §12.4's band; and the list the owner plays to rule on the roll and the numbers.

## The instrument: M6-11's, rebuilt, and driven rather than thumbed

M6-11's probe logged a played run's hits per Husk to `Logs/` and was reverted, so the ROADMAP's
*"on M6-11's instrument"* means building it again. It is rebuilt with one change: **a temporary
PlayMode driver plays the run**, because a count is an engineering number and the owner's play is
for what a count cannot say (rule 6). The driver:

- starts a Descent run as the Ranger from the Menu through `PendingRun`, as `RunBodyTests` does;
- **never touches the stick.** The Ranger shoots standing still (RS-03a), and a Husk walks to it;
- answers every offer, splash and Sanctum through `IProgressionCommands` by rule 3's path, which
  `RunTicker` and the presenters follow as they follow a tap;
- **moves the body, not the stick**: to the door once the Sanctum is left, and to 7 m of the nearest
  enemy after 1.5 s with nothing targeted. A Spitter holds at 14 m against the bow's 10 m and 12 m
  acquire, so a Ranger that never moves never clears a stage. Hold-fire reads core's motor, not the
  body (`WorldSnapshot.PlayerVelocity`), so a moved body still shoots;
- steps a fixed 1/60 s a frame through `Time.captureDeltaTime`, uncapped, restored on exit;
- is sturdy through a temporary `_maxHp` on `Ranger.asset` (M6-11's *sturdy*). A player who cannot
  die stops dodging (M6-11), and a count of hits on a Husk does not read the player's health;
- stops when stage 17 arrives, or after 60 real minutes.

The probe keeps, per Husk: the arrows that landed on it, how many were volley arrows, and whether
anything else damaged it. It logs each kill with the ordinary arrow's damage, the level, the Overflow
levels and the nodes taken, and closes each stage with a summary line.

## Files

| Path | Purpose |
|---|---|
| `Docs/plan/tasks/RS-03e-the-rangers-acceptance.md` | This spec, and the instrument's table in *As built* |
| `Docs/plan/PROGRESS.md` | The entry and Current State |
| `Docs/plan/ROADMAP.md` | RS-03e ticked and linked; [row 2](../ROADMAP.md#carry-forward-into-m7) gains the Ranger's curve |
| *temporary, reverted before handover* | `Assets/_Project/Tests/PlayMode/RangerAcceptanceProbe.cs`, the driver and the probe, writing `Logs/rs-03e-probe.log`; `Assets/_Project/Data/Characters/Ranger.asset`'s `_maxHp`, 90 → 100 000 while it runs |
| *only if a number moves* | `Data/Characters/Ranger.asset`, `Data/Effects/Ranger/*.asset`, and every row that mirrors the number moved (rule 5, rule 6) |

No production code. **A bug found here becomes `RS-03f` with its own PR** (M6-11 rule 8).

## Public API

None.

## Behaviour

1. **The number is arrows landed per Husk killed by arrows alone** — M6-11's *"killed by the weapon
   alone"*. A Husk that took damage from anything else is counted apart and left out. An arrow's
   damage is the `EnemyDamaged` published inside its landing, before its `ProjectileImpacted`
   (`ProjectileImpacted`'s remarks), so the attribution reads core's own order. A volley arrow is one
   of a group of shots fired on one frame.
2. **One natural run from stage 1 to 16, not starting stages.** The tree fills, the splash is taken
   and Overflow accrues where a player's would. A boss stage (5, 10, 15) reports its adds if it has
   any, and otherwise the stages either side stand for it, as in M6-11's table.
3. **The path is stated, and it is the path that lowers the count:** Volley's three ranks first, then
   Quick Draw's, then Fleet Foot's; then the borrowed branch; never a Pact card. The splash takes the
   borrowable branch with the fewest nodes that raise `WeaponDamage` or cast an Active, so the curve is
   the Ranger's kit and not a lender's; the choice is logged. Nothing is bought in the Sanctum, the
   owner's own practice at M6-11, and the roll is never pressed. **The arrow-only curve beside it is
   arithmetic** — `ceil(36 × h(n) / damage)` at the logged damage — because no Ranger that skips
   Volley differs from it.
4. **The verdict is against GD §12.4's 3–5, up to the death horizon (§12.5).** Each stage gets its
   median and range, and the stage the curve first leaves the band, if it does.
5. **A number moves on this evidence only for a break the other classes do not have.** GD §6.2's
   *"~3 hits at stage 1"* and the Oathbound's 4 flat from stage 4 are what the classes were authored
   to. So a Ranger out of the band at stages 1–8 is an authoring defect, and its own `_weaponDamage`
   moves here. A drift from stage 9 on is ledger row 2's, as the Gravecaller's is, because a curve is
   M8-05's (M6-11's *Out of scope*).
6. **The owner's play rules on the rest, on this branch.** The roll is kept or switched off, and any
   number the owner names moves: the asset, every row that mirrors it, and a line in *As built*. A
   mechanism, the running shot as a node for one, is a new task and not an edit here.
7. **Nothing of the instrument ships.** The driver and its `.meta` are deleted, `Ranger.asset`'s
   `_maxHp` is back at 90, the real `run.json` is restored (the driver's run writes it, [parking
   lot](../ROADMAP.md#parking-lot)), and `TimeManager.asset` is reverted. `git status` shows only this
   table's documents and any number rules 5–6 moved.

**When the spec disagrees with itself, the Checklist wins, then Behaviour, then Files.** When it
disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in
*As built*.

## Checklist

- [x] The run reaches stage 17 without a death, a stall or an exception, and the log says so (rules 2, 3)
- [x] Hits to kill at every stage 1–16: median, range, the Husks counted and left out, the volley share, the ordinary arrow's damage, the level, Overflow and the nodes taken (rules 1–3)
- [x] The arrow-only curve beside it, from the logged damage (rule 3)
- [x] The verdict against GD §12.4, and the stage the band is first left, if it is (rule 4)
- [x] Rule 5 applied: a number moved, or the reason none did
- [x] [Row 2](../ROADMAP.md#carry-forward-into-m7) carries the Ranger's curve for M8-05
- [x] Rule 7: the instrument reverted, `git status` clean of it, `run.json` restored
- [x] EditMode and PlayMode green through `TestRunnerApi` on the final tree, the Console swept after EditMode, zero new analyzer warnings, `dotnet format` clean on any touched C#

## Manual verification — what the owner plays

1. **[Editor] A natural Ranger run from stage 1, as deep as it goes.** Nothing is granted and no
   stage is jumped. *Watch: whether stopping to shoot and moving to dodge reads as the class,
   whether the 3-to-5-hit Husk the instrument measured is the one felt, and what the bow does
   while a Spitter stands 10–12 m off and Husks are at your feet (*As built*'s finding).*
2. **[Editor] The roll, kept or switched off.** Play two stages rolling out of every telegraph you
   can. The Ranger holds fire through the roll and until the body stops. *Kept:* `Charge`, 5 m in
   0.3 s, every 2.5 s, dealing nothing. *Switched off:* `Ranger.asset`'s Movement Skill Kind →
   `None`, which hides the button (RS-03a). Say which.
3. **[Editor] The numbers.** Name any that should move and where it should go. Where each lives:

   | Number | Now | Where |
   |---|---|---|
   | HP · speed | 90 · 3.2 m/s | `Ranger.asset` |
   | Bow: damage · fire rate · range | 13 · 2.2 /s · 10 m | `Ranger.asset` |
   | Acquire range (*As built*'s finding) | 12 m | `Ranger.asset` |
   | Bow: damage frame · arrow speed | 0.72 · 30 m/s | `Ranger.asset` |
   | Volley: arrows · fan · damage | 3 · 30° · ×1.5 | `Ranger.asset` |
   | Volley's ranks | every 5, 4, 3 shots | `Data/Effects/Ranger/Volley*.asset` |
   | Quick Draw · Fleet Foot | +12 % fire rate · +8 % speed a rank | `Data/Effects/Ranger/` |
   | Roll: distance · time · cooldown | 5 m · 0.3 s · 2.5 s | `Ranger.asset` |

4. **[device]** Everything above on a phone. Deferred with every device row ([row 1](../ROADMAP.md#carry-forward-into-m7)).

## Out of scope

- **Balancing the curve.** Row 2 and M8-05, as for the other three classes; rule 5 is the one exception.
- **The running shot, a Ranger Pact, the Ranger's relationship with the Veil, its price and V1.**
  Each is a design question with its own home: a node, M7-04, and the [parking lot](../ROADMAP.md#parking-lot).
- **Keeping the driver.** It is M6-11's instrument rebuilt, and it is reverted like M6-11's. *As built*
  records enough to build it a third time.

## As built

**No production code: one driven run, a reverted instrument.** Seed 7, stage 1 to stage 17's
arrival: 949 game seconds in 291 real (3.3×, a fixed 1/60 s step, unfocused), no death, no error,
3 759 arrows. 135 Husks died to arrows alone and none was left out. Volley's ranks were taken at
stages 1–2, Quick Draw's by 5, Fleet Foot's by 7. The splash took the Oathbound's first branch
(Steady Breath, Tempered Vow, Unbowed, Bulwark), and none of it moved an arrow.

| Stage | Husks | Median | Range | Arrow | Arrow-only | Level · Overflow |
|---|---|---|---|---|---|---|
| 1 · 2 | 10 · 4 | 3 · 3 | 3 · 3 | 13 | 3 · 3 | 2 · 4, 0 |
| 3 · 4 · 5† | 8 · 4 · 6 | 4 · 3 · 3 | 3–4 | 13 | 4 | 5 · 6 · 8, 0 |
| 6 · 7 · 8 | 7 · 7 · 8 | 4 | 4 · 4 · 3–4 | 13 | 4 | 9 · 10 · 12, 0 |
| 9 · 10† | 10 · 7 | 4 · 4 | 4–5 · 3–4 | 13 | 5 | 13 · 14, 0 |
| 11 · 12 | 10 · 16 | 4 · 5 | 3–4 · 4–5 | 13.26 · 13.52 | 5 | 15 · 16, 1 · 2 |
| 13 · 14 · 15† | 11 · 7 · 7 | 4 · 5 · 4 | 3–5 · 4–5 · 4–5 | 14.04 · 14.3 · 14.3 | 5 | 18 · 19 · 19, 4 · 5 · 5 |
| 16 | 13 | 5 | 5 | 14.82 | 5 | 21, 7 |

† a Warden stage; its Husks count like any other. *Arrow-only* is `ceil(36 × h(n) / arrow)`.

**Rule 4's verdict: inside GD §12.4's 3–5 at every stage from 1 to 16**, as the Oathbound is; the
Gravecaller leaves it at 9 and the Emberwright is under it to 9. Without the volley the curve reaches 5 at stage 9; the volley
holds the median a hit under that to stage 11, then 5 at 12, 14 and 16, where all thirteen took 5.
Overflow is the Ranger's only damage scaling, and the splash's four nodes held it back to stage 11,
because a thirteen-node tree fills at level 14. **Not measured, by arithmetic:** at one Overflow
level a stage, arrows alone need 6 near stage 23. That is inside GD §12.5's horizon, so it is
[row 2](../ROADMAP.md#carry-forward-into-m7)'s, beside the Gravecaller's 9. **Rule 5: no number
moved**, because nothing broke the band at all.

**Finding: the bow goes silent while a Spitter out-ranks the Husk at its feet.** The targeter
scores to the 12 m acquire, and `PlayerCombat.IsTargetInWeaponRange` looses inside 10 m. A Spitter
at 10–12 m scores 3 + 3 × (1 − d/12), and 1.5 more once current; a Husk at a metre scores 3.75. So
the Spitter keeps the target, and a planted Ranger shoots nothing until it steps in. That is CC §3.2
as written (*"a Choir healing the pack at 11m must outrank a Husk chewing on you at 3m"*), and the
Oathbound's 8 m against 12 has the same shape. It costs the Ranger more, because a step holds fire.
The driver found it by standing still through stage 3 for 380 s. It is a feel question, so it is
on the play list as the acquire range. **My read: 10 m, if you see it**, as the Gravecaller and the
Emberwright have range equal to acquire.

**Deviation 1: the driver moves more than the instrument section says.** It also steps to 8 m of
its own target when that target stays past the bow's 10 m for a second (89 times). And after 12 s
without a kill it moves to 3 m of the nearest enemy, turning 90° each time (34 times, most of them
during the Warden fights). Without the first, stage 3 stalled on the finding. Without the second, a
move had parked the body on raised ground 0.65 m up, where nothing could reach or see it. Neither
changes what a Husk takes, only whether a stage ends.

**Deviation 2: the splash's scoring tied.** Every lender branch carries an Active or a damage node,
so all five scored 1 and rule 3's *fewest* took the first. The table shows it cost nothing: the arrow
was 13 until Overflow.

**Deviation 3: six runs for one.** Two were stopped from the Editor's toolbar, and a
`playModeStateChanged` hook named `PlayModeButtons` as the caller. One ended on the driver's own
tier index, which CH §5 numbers from 1. Two stalled on deviation 1's gaps. Every run that reached a
stage read it the same.

**Rule 7's bug, `RS-03f`.** The first full PlayMode pass on the reverted tree failed
`BootSmokeTests.Descend_StartsARun_AndRefusesASecondTap`, *"Descend stayed interactable"*. The row
taps `GetComponentInChildren<Button>()`, and in `Menu.unity` Continue comes before Descend. With a
run on disk it taps Continue, which disables both and loads the run. With none, it taps Descend, which
has opened class select since M5-07 and has no guard. So the row is green only on a machine with a
saved run. The game is right and the row is not.

**How it was checked.** EditMode 3 269 (3 268 / 0 / 1 inconclusive) on the reverted tree, and the
Console straight after: 46 entries, each from a passing negative-path row. PlayMode failed 56 / 1
on the row above with no `run.json`, then 3 / 3 alone, then 57 / 0 / 0 with one. No C# is in the
change. `TimeManager.asset` was reverted, the owner's `profile.json` is byte-identical, and both
`run.json`s the runs wrote were removed, as the owner had none.

**For a third build of the instrument:** a PlayMode test that sets `PendingRun` and loads Run.
It resolves `IRunSession`, `IProgressionCommands`, `DomainEventHub`, `WorldSnapshot` and
`PlayerView` from `RunScope`'s container. A body is moved with its `CharacterController` disabled.
It needs `[Timeout]` past 180 s and `LogAssert.ignoreFailingMessages`. Sturdy is `_maxHp`, read
at boot, so it is an asset edit. The log is `Logs/rs-03e-probe-final.log`.

**Not done here:** manual steps 1–4, the owner's.

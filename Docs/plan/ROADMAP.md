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
| **M3** | **Levelling and the tree** ✅ | XP, tree rules, offers, level-up screen, SkillRunner + auto-cast, effect primitives, first Oathbound nodes, health-bar treatment — **complete, accepted on Editor evidence, tagged `m3`** | 35 |
| **M4** | **First boss and run end** | Boss phases, Warden of Ash, death → Shard payout, profile persisted. **The UI work M3-15's acceptance surfaced is *not* here — the owner ruled it out after the tag, and it stays [ledger row 1](#carry-forward-into-m4)** | 11 |
| **M5** | **Second class** | Gravecaller: projectile weapon + leading, Shroudstep, Wights, its tree, class select | 8 |
| **M6** | **Systems complete** | Sanctum shop, Veilrot + Pacts + Claiming, Ordeals, Emberwright, unlocks, localisation tables | 11 |
| **M7** | **Content pass** | Full V1 roster, Elites/affixes, Choirmother, all 81 nodes, both biomes' art, audio | 8 |
| **M8** | **Feel, perf, ship** | Game-feel checklist, options/accessibility, device tiering, thermal, per-class balance, store build | 6 |

---

## M0 — Walking skeleton

**Complete, tagged `m0` on Editor evidence** — 21 tasks, 153 EditMode + 3 PlayMode green. The goal, the task table and its status notes are in [archive/ROADMAP-M0.md](archive/ROADMAP-M0.md); the log is [archive/PROGRESS-M0.md](archive/PROGRESS-M0.md).

## M1 — Combat feel

**Complete, tagged `m1` on Editor evidence** — 21 tasks, 452 EditMode + 3 PlayMode green, the fight playtested and called good in the Editor. Archived in [archive/ROADMAP-M1.md](archive/ROADMAP-M1.md); the log is [archive/PROGRESS-M1.md](archive/PROGRESS-M1.md).

## M2 — Stage loop

**Complete, tagged `m2` on Editor evidence** — 27 tasks, 1 077 EditMode green, the descent playtested and called good in the Editor. The task tables, the spec-group notes and the ledger are in [archive/ROADMAP-M2.md](archive/ROADMAP-M2.md); the log is [archive/PROGRESS-M2.md](archive/PROGRESS-M2.md).

### Carry-forward into M2

**Closed at M2-15: all fourteen rows struck, none carried into M3.** The rows and the closing notes are in [the archive](archive/ROADMAP-M2.md#carry-forward-into-m2). This heading stays so links into it keep resolving.

## M3 — Levelling and the tree

**Complete, tagged `m3` on Editor evidence** — 35 tasks, 1 981 EditMode green, accepted on a verdict rather than a number: the mechanisms work and the UI cannot be read, which is why [M4's ledger](#carry-forward-into-m4) opens with a readout. The task tables, the spec-group notes and the ledger are in [archive/ROADMAP-M3.md](archive/ROADMAP-M3.md); the log is [archive/PROGRESS-M3.md](archive/PROGRESS-M3.md).

### Carry-forward into M3

**Closed at M3-15: seven of nine struck, three carried, none dropped.** What was carried is [M4 rows 2 and 3](#carry-forward-into-m4). The rows, M3-15's closing note and the exponent ruling M4 row 2 cites are in [the archive](archive/ROADMAP-M3.md#carry-forward-into-m3). This heading stays so links into it keep resolving.

## M4 — First boss and run end

**Goal:** a run *ends* — it has a wall to hit, and something the player keeps afterwards.
**Done when:** M4-07's checklist passes and `m4` is tagged.

**The UI redesign M3-15's acceptance asked for is *not* in M4 — the owner ruled it out immediately after the
tag**, and it stays [ledger row 1](#carry-forward-into-m4) rather than becoming tasks. That is a decision about
*when*, not about whether: the row keeps its full account of what was found and what it depends on, so the
milestone that eventually takes it does not have to rediscover that a 44 dp cell is the problem and that icons
are the blocker. **What it costs M4 is stated here rather than left to be found:** M4-04's segmented boss bar
and M4-06's run-end screen are both *new readouts landing on a HUD already known to be unreadable*, so they
inherit the row rather than escaping it, and M4-07's acceptance will be asked the same readability questions
M3-15 could not answer.

**M4 opens with a spec group, and the ruling was made at M3-15 with its reasons.** M4-01's title names *three* subsystems and every comparable framework task in this project has split; M4-05 is a **`PlayerProfile` format bump**, which [ledger row 5](#carry-forward-into-m4) says gets its own review; and **the UI redesign the owner asked for at M3-15 has to be scoped before anything is built**, because the readout it wants depends on icons that currently live in M7. Titles below stay titles until M4-00a replaces them.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| M4-00a | Specs for M4-01…M4-04 — the boss | S | — | ☑ |
| M4-00b | Specs for M4-05…M4-07 — run end, the payout, and acceptance | S | 00a | ☑ |

**Specced by M4-00a:** the boss, M4-01…M4-04. **One split, before starting.** **M4-01 splits at the
effects/AI seam**, because the owner's requirement — *"the boss can get buff and debuff, skills, and everything
that player can get"* — turned out to be a question about `Core/Effects` rather than about bosses, and it is a
different review from a phase machine.

**The finding that produced the split, measured against the shipped code rather than assumed: the combat
machinery is already almost entirely target-agnostic.** `Health` is the same class for both
(`EnemyAgent.cs:55`, `PlayerCombat.cs:287`); `EnemyAgent` **already carries two `Stat`s**; `GrantedShieldPool`
is keyed by `object`; `EffectRegistry.Apply` takes an `object source`; **`SkillRunner`'s constructor names no
player at all**; and `EnemyAgent.IsVulnerable` — the invulnerable beat GD §9.1 rule 3 needs — **already
exists**. **Exactly two things are player-bound**, and breaking them is [M4-01a](tasks/M4-01a-combatant-stats-and-triggers.md):
`ModifyStat` names a `PlayerStat` and its handler holds *the* `PlayerStats`, so an effect can address the player
and nothing else; and `EnemyBlackboard` is a *perception* blackboard with no health fraction, while
`TriggerSpec` reads the player's `CombatBlackboard`.

**Three owner rulings the group hangs on.** **A boss is an `EnemyAgent` with a `BossBehaviour`, not a parallel
system** — one spawn path, one damage path, one pool, and the alternative would make every later system
(Elites, Ordeals, affixes) know about both. **Which stages hold a boss is authored on the mode**, the owner's
words being *"we should be able to select which stages what enemies should have"* — so `Descent.asset` says
*every 5th* in a field and no `stage % 5` exists in code; a stage editor is the eventual shape and is a
[parking-lot](#parking-lot) line. And **boss health is authored on the `EnemySpec` like every other body**
(ADR-0006), with a test asserting GD §9.1 rule 5's 75–120 s against expected player DPS computed from the
shipped assets — M3-12c's TTK shape, and the owner delegated the call.

**One ruling M4-00a made itself rather than asking**, because the code answered it: **`EnemySpawned` gains no
`IsBoss` flag.** M3-13b's defaulted `IsElite` is the nearby precedent and it does not apply — that was a
*look*, and a boss is not. What a view needs is the phase count, which rides on `BossPhaseChanged`, so M4-04's
bar learns its segments from an event and no existing signature moves.

**And one thing the group is required to keep saying out loud:** the owner ruled the UI redesign out of M4
immediately after `m3` was tagged, so [ledger row 1](#carry-forward-into-m4) is **inherited, not escaped**.
M4-04 is the first task since M3-15's verdict to put a new readout on the HUD, and its rule 8 forbids it from
ticking a readability row.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M4-01a](tasks/M4-01a-combatant-stats-and-triggers.md) | A stat an effect can aim at, and a trigger a boss can read about itself | M | 3-05 3-06 3-12a | ☑ |
| [M4-01b](tasks/M4-01b-boss-agent-framework.md) | A boss is a combatant with phases: 66/33, an invulnerable beat, and adds | M | 01a 2-05 2-06 | ☑ |
| [M4-02](tasks/M4-02-warden-behaviours.md) | The Warden of Ash: a shockwave, a fissure, and Husks | M | 01b 2-06 2-12b | ☑ |
| [M4-03](tasks/M4-03-boss-arena-and-views.md) | What the Warden looks like, and the arena that helps it | M | 02 2-11a 3-11c 3-13a | ☑ |
| [M4-04](tasks/M4-04-segmented-boss-bar.md) | The segmented boss bar: a phase you can see coming | S | 01b 3-13a 3-13b | ☑ |

**Specced by M4-00b:** run end and acceptance, M4-05…M4-07. **One split, before starting.** **M4-05 splits at
the arithmetic/persistence seam**, at the same place M3-01 and M3-07 split: a feature, then the format bump
that makes it survive an app kill. [Ledger row 5](#carry-forward-into-m4) asks the bump for *its own review*,
and a task that also contains a formula is not that.

**The ruling the group had to make first, because the ROADMAP's own title assumed the answer:
`RunEnded` gains nothing.** The title read *"Death → `RunEnded` → Shard payout"*, and `RunEnded` is the wrong
event for a payout by the argument three shipped classes have already made about it — `RunSession.End` is a
deliberate no-op-if-not-running so scope disposal can call it blind, so `SaveWriter.cs:78`,
`HudPresenter.cs:50` and `PausePresenter.cs:64` each refused it. A payout riding it would pay a player for
quitting to the menu and pay them again on every teardown. M4-04's *"a readout's facts ride the event"*
precedent still applies — but to a **new** event, `ShardsAwarded`, published only inside `RunSession.Tick`'s
`IsDead` branch, between `PlayerDied` and `RunEnded`.

**And the finding that made M4-05a a small task instead of a second format bump: bosses killed is *derivable*
and needs no counter.** GD §14.1 wants `10·(deepest stage) + 50·(bosses killed) + 25·(new archetype first
encountered)`. Nothing in this game counts bosses — M4-01b ruled deliberately that there is no `BossDied` —
and a counter would have to live on `RunState`, hence in `RunSnapshot`, hence in a **v4**. It is not needed: a
stage is only left once `SpawnDirector.IsStageComplete` is true, and on a boss stage that property *is*
`_bossCleared` (`SpawnDirector.cs:243`), so **every boss stage below the current depth has had its boss
killed**, and `ModeSpec.TryGetBossFor` — `Descent.asset`'s authored *every 5th* — says which those were. The
payout becomes a pure function of the depth and the mode, and it survives a resume for free.

**Two of GD §14.1's three terms ship, and the spec says which.** The third is *first* encountered — a lifetime
fact, so a `PlayerProfile` field holding a **set** of `ContentId`s rather than a run field. It waits, because
**deferring it over-pays a returning player rather than under-paying them** (an empty set pays 25 on the next
Husk it ever sees), which is the exact opposite of the Shard *total*, where not writing the number destroys
it. GD §14.1 is annotated at M4-07 so the doc stops claiming three.

**Shards are persisted with nothing to buy, and that was ruled rather than defaulted.** GD §14.2's unlocks are
M6-09's and the Sanctum is M6-02's, so M4-05b banks a number no player can spend — `Palette.Veilrot`'s shape,
*"ship the member, no reader"*. The difference is that this one is a **save format**, and the difference cuts
the other way from the obvious reading: an unread colour costs nothing to add later, while a total not written
is data destroyed, and GD §14.1's *"dying must always pay something"* is a claim about a number that persists.

**M4-06 has to say what becomes of the death overlay, and it says: replaced, not stacked.** `HudPresenter` has
owned `ui.death.title` / `ui.death.hint` and the tap-to-menu since M1-17, and two screens for one death is the
worst outcome available. The overlay leaves that class entirely — and with it `SceneLoader`, `InputAdapter`
and `ILocalizer`, **because the death path was the only reader of all three**, so `Construct` drops from five
parameters to two and the HUD stops being a localisation reader at all. The two keys move rather than retire.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M4-05a](tasks/M4-05a-shard-payout.md) | Death pays: the Shard payout, computed where the run ends | S | 01b 2-10 | ☑ |
| [M4-05b](tasks/M4-05b-profile-v3-and-shard-writer.md) | `PlayerProfile` v3: the first thing a dead run leaves behind | S | 05a 3-09c 2-13b | ☑ |
| [M4-06](tasks/M4-06-run-end-screen.md) | The run-end screen: what the descent was worth, and the overlay it replaces | S | 05a 05b 3-13a 3-14a | ☑ |
| [M4-07](tasks/M4-07-acceptance-and-tag.md) | M4 acceptance, tag `m4` | S | everything in M4 | ☐ |

### Carry-forward into M4

**What M4's specs must absorb.** Opened at M3-15 on [M2's terms](#carry-forward-into-m2), unchanged through three milestones: every row names the task that must deal with it, and **a row leaves only when its owner's *As built* says it is answered.** Three rows are carried from [M3's table](#carry-forward-into-m3), one is a parking-lot lead that has outlived its trigger, one is the format rule arriving on schedule, and two are M3-15's own checklist misses. Ranked by what it costs to fix later rather than now.

**Row 1 is new in kind, and that is worth saying once.** Every ledger this project has kept has carried rows about *unbuilt mechanisms*. This is the first to carry a row about a **readout**: the mechanisms are built, proved and reachable, and the milestone still could not be judged, because nobody could read the screen it happens on.

| # | Finding | Owner | Cost of leaving it |
|---|---|---|---|
| 1 | **The UI cannot be read, and the readout the owner wants depends on art that is three milestones away.** M3-15's acceptance playtest returned *too cramped to read* and *too unfinished to feel*. **Cramped is measurable and is a layout fact**: a tree-view cell is **44 dp** and holds a name *and* a description, four cells to a column — about **226 dp of a ~432 dp landscape safe area**, so roughly 200 dp sits unused, and [M3-09d](tasks/M3-09d-tree-view.md) rule 9's no-scroll, no-zoom bet was lost on cell *height* rather than on the thing it worried about. `_cellHeightDp` and `_cellGapDp` are serialized for exactly this, so the legibility half is a tuning session rather than a rebuild. **Unfinished is M7's art pass and was never in M3's scope.** **What is not tuning is the change the owner asked for: every taken node shown, Active or Passive, with an Auto skill marked by something orbiting it.** Today the auto-cast row admits **Actives only** — [M3-10b](tasks/M3-10b-progression-hud.md) rule 5 ruled that deliberately, a Passive being a stat modifier with nothing to tick — so a player with eight Passives and two Actives sees **two cells**, and everything else they chose is invisible outside the tree view. That is a membership rule plus a new visual language, so it is a **mechanism**, not a number. **And it has a hard dependency nobody can design around:** M3-10b rule 8's cells are **24 dp and hold neither an icon nor a word**, so twelve nodes there would be **twelve identical dots**. **Sequencing is icons → readout → rotation**, and icons are M7's — which is the same shape as M3-00d's ruling that pulled the English table forward, and should be decided the same way and for the same reason: *a readout nobody can read cannot be judged*. **M4-04 landed a new readout on this HUD without ticking any of it, which is what its rule 8 required:** a full-width boss band now sits **5–15 dp** down, between the 4 dp XP strip on the very top edge and the HP row 16 dp down — two 1 dp gaps, asserted to the edge by `Hud_StripAndBarDoNotOverlap` and **judged by nobody**. So the row is now one milestone older and one element more crowded, and **M4-07's acceptance checklist is the first place anyone says whether any of it can be read.** **Re-owned at M4-00b, because M4-00a merged without discharging it** — that task was asked to rule on scope and on pulling M7's icons forward, and the owner's ruling of the UI redesign out of M4, made immediately after the `m3` tag, left it nothing to rule. **M4-06 adds a third element — a full-screen run-end screen that is nothing but words** — and unlike M4-04's band it collides with nothing: what it risks is being unreadable on its own terms. | **M4-07** to *judge* it, with a measurement rather than a shrug, and to re-own it into M5's ledger. **The fix has no owner in M4, and that is the owner's standing ruling rather than an oversight** | **High, and it compounds with every feature.** Every milestone from here adds things the player must read — a boss's phases, a run-end payout, a second class's tree — onto a HUD already known to be unreadable. Fixing it before M4's content is one job; after M7 it is a retrofit across every screen. |
| 2 | **Three M3 rows wanted a number a playtest produces, and the playtest could not produce one.** Carried from M3 rows 1, 8 and 9 **together rather than separately**, because they share a cause and will be answered in one session: **row 1's played hit counts** at stages 1 / 15 / 30 — the arithmetic is closed and was re-measured at M3-15 (3 / 4 / 5), but *whether stage 15 feels like four hits with three Spitters shooting* is not a thing a test can say; **row 8's two labelled timings** at stages 1, 5 and 10, stopwatch **and** `RunState.Time`, where the gap between them **is** GD §13.1's interruption budget and is the one number no simulation can ever produce, because it is a human deliberating over three cards; and **row 9's two-second read** on twelve real English descriptions. **The exponent half of row 8 is closed and does not carry** — see [M3's closing note](#carry-forward-into-m3). **The cheap part of this row is that it is one playtest**, and it should be run against the *fixed* UI rather than the shipped one, which is why it sits behind row 1. **Re-owned at M4-00b, and the honest reading is stated rather than left to be discovered:** M4-00a was asked only to *sequence* this behind row 1 and it merged without saying so, and since the owner ruled the UI redesign out of M4 there is no fixed UI for M4-07's playtest to run against. **So M4-07 may well carry this row a second time — and if it does, the reason is written down rather than implied.** Carrying a row twice is a decision; carrying it silently is the failure this table exists to prevent. | **M4-07** to produce the three numbers **or to carry the row with a written reason** — M3-15's precedent is binding either way | **Medium, and it decays.** These are the numbers that say whether the tree is tuned at all. Every milestone that adds content before they are taken makes a bad answer more expensive to act on. |
| 3 | **The device debt, carried whole from M3 row 4, with multi-touch still named apart.** `m0`, `m1`, `m2` and now `m3` are all tagged on Editor evidence. **[M3-10a](tasks/M3-10a-skill-slot-buttons.md)'s multi-touch is the one row that risks a *feature* rather than a verdict**: if Android does not deliver a thumb on the stick and a thumb on S3 at once, **manual casting does not work at all**, and no Editor probe can say so. The rest, re-stated rather than re-listed: haptics and M1-20's 100 ms coalescing window, touch latency, real frame rate, thermal, landscape flip, sustained fps at the concurrency cap, **the Profiler's no-per-frame-`GC.Alloc` check unmet since M1-21**, `Physics.SyncTransforms()`' per-frame price (M2-15a), **kill-from-recents mid-stage** — the one *correctness* question on the list, against an atomic write built for exactly that case and never tested against it — and whether the M0–M3 feel verdicts survive leaving the Editor. **M3's own additions all survive**: the 4 dp XP strip beside a notch, the 24 dp radial fill, twelve tree cells in a landscape safe area, the 3 dp enemy bar, whether GD §16.2's damage tint reads in peripheral vision (its entire claim for existing), and whether `timeScale` 0 at 30 fps recovers thermal headroom. **M4-03 added the four the spec promised, and they are the first telegraphs in the project the phone-size question has ever been asked of.** (1) GD §9.1 rule 7 — whether a 14 m expanding ring and a 5 m crack *read* on a six-inch screen, which is the rule this task cannot tick and says so. (2) GD §11.3's fill-rate ceiling, now reachable for the first time: a slam ring, up to eight cracks, a Consecrate zone and a spawn telegraph can all overlap in one boss fight, and the ring is the widest transparent surface the game has ever drawn. (3) Whether the arm-and-fire shape change reads *as a shape change* at thumb distance, or whether two orange rectangles a third of a second apart read as one flicker. (4) Whether a shell over the body says *not taking damage* or just hides the boss — the alpha was already moved once on Editor evidence and every number of it is serialized so the answer is a session rather than a build. **And one that is a *finding* rather than a question, because the Editor answered it:** `M_TelegraphRing` blends premultiplied without URP's `_ALPHAPREMULTIPLY_ON` keyword, so `ZoneView`, `TelegraphRingView` and `BulwarkView` all draw at full brightness whatever their alpha says — M4-03's three views premultiply and those three do not, which is one keyword on one material away from being consistent and is the owner's call rather than this task's. **M4-04 added two, and they are the first questions this ledger has ever had about the *top* edge.** (5) **Whether a 10 dp full-width band, a 4 dp XP strip and a landscape cutout coexist in the safe area** — the two elements are 1 dp apart and the HP row is 1 dp below the band, which is asserted in the Editor and says nothing about a notch eating the corner on one of the two rotations. (6) **Whether the invulnerable beat's dim reads as *“you cannot hurt it”* rather than as the HUD glitching** — the band drops to `_beatAlpha` 0.4 for 1.5 s rather than changing colour, on the palette's own rule that a view multiplies its own alpha, and both the alternative (an eleventh palette member) and the fix (one serialized field) are cheap once somebody has looked at it. **M4-06 adds two more, and they are the first this ledger has ever had about a screen made of words.** (7) **Whether five labels and three numbers read at thumb distance** on a six-inch landscape screen — the run-end screen is full-screen over a stopped run, so unlike M4-04's band it collides with nothing and the only risk it carries is its own legibility. (8) **Whether its one button clears a landscape cutout on *both* rotations**, which is question 5 asked at the opposite edge. **Eight questions, four tags, and nothing has ever run outside the Editor.** | The first hardware session; **M8-xx** if no phone arrives sooner | **High and compounding, unchanged for three milestones.** Each milestone adds rows and none retire, so the first device session gets larger and its failures get harder to attribute to the milestone that caused them. |
| 4 | **`FrameOrderTests` fails about one isolated run in ten, and the cheapest experiment anyone has proposed has gone unrun for nine tasks.** [M3-08a](tasks/M3-08a-level-up-flow-and-pause.md) measured the baseline properly — **5 failures in 50 isolated runs on untouched `dev`**, always `Ticker_RunsTheStepsInOrder` with M2-15a's exact message — and that baseline immediately paid for itself by catching a *new* 30 % rate that turned out to be M3-08a's own new row hanging off the same intermittent sweep. Since then every task has added a tally and none has added evidence. **M3-15 ran it eight times green and said so with the arithmetic**: at 10 %, a clean sweep of eight has probability ≈ **0.43**, so it is a coin flip and not a fix. **[M3-08b](tasks/M3-08b-level-up-screen.md)'s experiment is still the next step and is still unrun** — run `BootSmokeTests` and `FrameOrderTests` together, against `FrameOrderTests` alone, because `BootSmokeTests` loads `Run.unity` in the same PlayMode process first and *“something inside a single run that moves between rows”* is the only hypothesis left standing. **It is a ledger row now rather than a *Known issues* paragraph because it has outlived nine tasks as a lead nobody owns**, which is precisely the condition this mechanism exists for. **Nothing in the game is known to be wrong.** **Given a task at M4-00b, after M4-00a merged without giving it one and the lead outlived five more tasks.** The row's own text says the condition it exists for is *"a lead nobody owns"*, and it is now eleven tasks old. **It goes to [M4-07](tasks/M4-07-acceptance-and-tag.md) as a numbered checklist row with the two filters written out** — `Soulvail.Tests.PlayMode.FrameOrderTests` alone against `BootSmokeTests,FrameOrderTests` together, at **n ≥ 20 each side**, both rates quoted against M3-08a's 10 % baseline. **A standalone diagnosis task was considered and refused**: there is no bug to fix, so such a task could only report, and M4-07 already runs PlayMode repeatedly and is where the arithmetic belongs. **If the experiment diagnoses something, the fix is `M4-07a` with its own PR** (M2-15a's shape); if it retires the hypothesis, the row carries into M5 **with a new next step rather than the same one**. `Run.unity` was changed again this milestone, which is a fifth data point for the lead. | **M4-07**, as a named checklist row with the experiment spelled out; **`M4-07a`** if it finds a bug | **Medium, and corrosive rather than expensive.** A suite with a row that fails one run in ten trains everyone to re-run rather than to read, which is how a real regression gets waved through. |
| 5 | **M4-05 is a `PlayerProfile` format bump, and the rule honoured six times applies again.** Death → `RunEnded` → Shard payout persists something new about the player, so `PlayerProfile.CurrentVersion` goes to **3**. The rule, unchanged since M2-13b and honoured at M3-01b, M3-03, M3-07b and M3-09c: **the field, its migration step and its fixture ship in the same PR**, because the trap is adding the field and the migration separately — the first merges green, since an old file still decodes. **The harness is now genuinely exercised on both formats** (`RunSnapshot` v1→v2→v3, `PlayerProfile` v1→v2), so `Chain_IsUnbrokenFromOldestToCurrent` and `ProfileChain` both loop over more than one version and neither needs its text changed. **And M3-09c already found what a second field costs and fixed it**: `ProfileStore` is the single writer, every feature moves one field through a `With` helper, and a reflection row sits at each door that could reopen it. So this row is cheap **provided M4-05 gets its own review**, which is the whole of what it asks. **M4-00b gave it exactly that, by splitting M4-05.** The arithmetic is [M4-05a](tasks/M4-05a-shard-payout.md) and persists nothing at all; the bump is [M4-05b](tasks/M4-05b-profile-v3-and-shard-writer.md) and holds the field, the `if (version < 3)` step, the `ProfileMirror` key and the fixtures in one PR, with M3-01a/M3-01b and M3-07a/M3-07b as the precedent for splitting a feature from the format that keeps it. **v3 is one `int` and deliberately not two fields** — GD §14.1's archetype term needs a *set*, and it was ruled out of the milestone rather than smuggled into the bump. **~~Closed at M4-05b, honoured rather than waived.~~** The field, the `if (version < 3)` step, the `ProfileMirror` key and three fixture rows shipped in one PR; `PlayerProfile.CurrentVersion` is 3 and `RunSnapshot.CurrentVersion` stayed 3, which is the independence M2-13b built two gate methods for and the first time the two numbers have been *equal* while meaning different things. **The claim this row made about the harness held exactly**: `Profile_ChainAndGateMirrorTheRun` now loops over three versions and its assertions did not change — the one edit the bump forced on it is the constructor argument every call site in the project gained. **And the claim about M3-09c held too**: `ShardWriter` moves one field through `WithShards` and never calls the constructor, and `Profile_HasNoConstructorThatOmitsAField` went from three parameters to four rather than gaining a convenience overload. | ~~**[M4-05b](tasks/M4-05b-profile-v3-and-shard-writer.md)**, in its own PR~~ — **discharged** | **High if skipped, near zero if honoured.** A format bump done wrong is silent until a player's save is already broken. |
| 6 | **Three places where content discipline leaks, found by M3-15's checklist rather than by any test.** (i) **Overflow's 2 % is a `public const` in `LevelUpFlow`, not an asset** — so ADR-0006's *“every number is in an asset”* is true of three of the acceptance checklist's four, and retuning the one number that supplies **more than half a deep run's power** is a rebuild rather than an Inspector edit. (ii) **`TimeToKillTests` feeds the *spec's* table rather than the curve**: `overflowLevels: 7` at stage 15 where the shipped curve produces **8**. Both give 4 hits, so no row is red — which is exactly why it survived, and it is the same class as M3 row 1's own 66/99-versus-66.24/98.64 correction, one level up. A test whose inputs describe a curve should derive them from it. (iii) **`Boot.unity` still carries a raw `Soulvail` splash that nothing resolves** — documented in `TableLocalizerTests`' remarks as deliberately out of scope, sharing `ui.app.title`'s existing row, and **owned by nobody**, so AR §11.5's *“no raw user-facing string anywhere”* is one string short of true. **A fourth reader was added at M4-00b, knowingly rather than by accident:** [M4-05a](tasks/M4-05a-shard-payout.md) rule 7 puts GD §14.1's `10` and `50` in `const`s, because a payout is not a mode, a character or an enemy and there is no asset it belongs on — inventing one would be M6's Sanctum arriving early and unspecified. **It is recorded here rather than quietly done**, which is what this row is for. | **M4-07** to distribute — **re-owned at M4-00b, because M4-00a merged without distributing anything**; (iii) is still one field and one line for whichever task next touches boot | **Low each, medium together.** None of the three is wrong today. All three are the kind of thing discovered by the *next* person to trust the rule. |
| ~~7~~ | **Closed on 2026-09-20 — `.githooks/pre-commit` check 8 refuses a staged PROGRESS.md whose *Last merged task* row does not name the newest Log entry's task.** Closed by the docs PR that added the check rather than by an *As built*, because M4-00a merged without ruling; recorded here so the exception is visible. The original: **Nothing in the protocol checks that Current State was rotated, and M3-14c proved it.** That task updated the stamp, the Milestone row, *Known issues* and the Log, and **did not rotate the task chain or write a *Verified* row** — so for a whole task the file read *Last merged: M3-14b* and *“M3-14b is next”* while carrying M3-14c's date, and M3-15 is what found it. **The class is the part worth keeping, not the instance**: the session protocol says *“append the PROGRESS entry and update Current State in the same change”*, and appending an entry is the visible half while rotating the chain is the half nobody notices missing. It is the same shape as M2-15's *“already-merged specs still point at pre-split task ids”* — a hygiene rule that only holds because someone happens to be reading the file for another reason. **A cheap fix exists**: a check that the *Last merged task* row names the task in the newest Log entry. | ~~**M4-00a** to decide whether it is worth a check or a habit~~ — **discharged**, a check | **Low to fix, medium to leave.** This file is the thing every session reads first, and a stale one sends the next session to the wrong task. |

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
| M5-07a | The second class (CH §5.4): the half-tree trigger, the splash screen, one branch into the offer pool, no Keystone |
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
| M6-10 | The **rest** of localisation: locale selection, the other languages' tables, and every screen M3 did not build — `ILocalizer`, `TableLocalizer` and one English table land early at [M3-14a](tasks/M3-14a-localizer-and-english-table.md), by the owner's M3-00d ruling |
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

- ~~**`PlayerAnimatorView` has no tests.**~~ Promoted at M2-15 to M3 ledger row 5, closed by M3-11c with fourteen rows and no behaviour change. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- **There are two player cyans in the build, and only one of them is GD §16.4's.** `HpBarView`,
  `ThreatArrows`, `VFX_ConsecrateZone` and `VFX_Bulwark` are `#22D3EE` — now `Palette.Player` — while
  `ReticleView._colour`, `FocusGlowView._colour` and `PlayerView._swingColour` are `#4CE6FF`, dressed
  that way on `Player.prefab` and `Reticle.prefab` since M0/M1. Found at **M3-13a** and deliberately
  left: unifying them recolours the reticle, the focus glow and the swing arc on every frame, which is
  a **visible** change rather than the relocation that task was, and which cyan the player's own
  world-space views should be is a look decision rather than an implementation one.
  `Palette_IsNotTheOnlyCyanInTheBuild` pins all three so neither value can drift further in silence.
  Promoted by the owner's ruling, or by **M7**'s art pass, which replaces all three surfaces anyway.
- **A hand-edited save can hang a frame, and no guard in the project is placed to stop it. — *Its named promoter fired and nobody caught it*, found at [M3-15](tasks/M3-15-acceptance-and-tag.md):** this line says *“promoted by M3-14b, which is already the task that refuses content the game cannot survive”*, **M3-14b shipped and did not take it**, and nothing in the protocol noticed. Deliberately **not** made an M4 ledger row — it still has no owner and capping the value is still a ruling about what such a save *becomes*, which is the exact condition for staying here. What changed is that the trigger is now known to have been missed rather than still pending. 
  `LevelTracker`'s `while (Xp >= XpToNext)` terminates for any finite XP, but `"xp":1e38` in
  `run.json` is a legal `RunSnapshot` and settles in ~10^15 iterations; `Grant` has the identical
  exposure through a content-authored `EnemySpec.XpValue`. Found at M3-01b and deliberately not
  fixed there: a cap means ruling what a save above it *becomes*, which is a decision rather than an
  implementation detail. Promoted by the first ruling on it, or by **M3-14b**, which is already the
  task that refuses content the game cannot survive.
- **`Core/Content` stopped being a leaf at M3-02a, and `Content ↔ Combat` is now a namespace cycle.**
  Before this task `Core/Content/` held zero `using Soulvail.*`; `TriggerSpec` now names
  `Core.Combat` (`CombatBlackboard`) and `SkillSpec` names `Core.Effects` (`IEffect`), while
  `Core/Combat` has depended on `Core/Content` since M0-07. It compiles and nothing is wrong —
  AR §5's table has no dependency column and §12 promises no acyclicity, and both types are content
  by AR §10.1 — but it would block AR §5's own escape hatch, *"split into separate assemblies only
  if compile times demand it."* Nothing owns it; promoted by the first task that wants that split,
  which would have to move `CombatBlackboard` or invert the `IsMet` call.
- ~~**CH §5's tree table says 8 nodes a branch and 27 a class, and 3 × 8 is 24.**~~ Corrected at M3-15 — two errors, the table row and the diagram, both now M3-02a rule 7's nine a branch. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- ~~**CH §5.2's curve cannot hit its own table past stage 10.**~~ Settled at M3-15 the other way round: the doc's stage-20 row moved and `Descent.asset`'s 1.4 stayed, because 1.5 or 1.6 would push stage-15 TTK to five hits. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- **A stage editor — *“maybe later we should have a stage creator”*.** The owner's words at M4-00a, alongside the ruling that **which stages hold which enemies is authored on the mode**. `ModeSpec` already carries an enemy roster with `_introducedAtStage`, and M4-01b adds a boss roster beside it, so the *data* is already per-stage-ish; what does not exist is a way to author a **specific** stage rather than a rule that generates them. Promoted when a designer wants stage 7 to be different from stage 6 for a reason no curve expresses — most likely **M7**, when the roster is full and the biomes differ, or M6-04 if Ordeals need per-stage authoring. **Not M4's**: one boss on a rule is what M4 needs, and building an editor for one entry is the kind of tooling that outlives its content.
- **A tree editor for `SkillTreeDefinition`.** Three nested arrays in the default Inspector is enough
  for twelve nodes. Promoted when **M7-04** authors eighty-one by hand and it hurts.
- **CC §6.2's drag-to-reorder for the four manual slots.** Ruled out of V1 at M3-00c, on M3-07a's own
  terms — *"the mechanic lands in M3-09 or not at all in V1"*. It costs one port member
  (`SetSkillSlot(skillId, slot)`) and a drag handler, and **no format bump**, because
  [M3-07b](tasks/M3-07b-save-format-v3.md) rule 1 saves slot *positions* rather than a set. Promoted
  by a playtest that says the lowest-free-slot rule puts the wrong skill under the thumb.
- **Icons for the twelve nodes — promoted at [M3-15](tasks/M3-15-acceptance-and-tag.md), by the playtest this line named as one of its two triggers, and it arrived carrying more than icons.** Now [M4 ledger row 1](#carry-forward-into-m4). The owner's acceptance verdict was *too cramped to read*, and the redesign asked for on the back of it — **every taken node shown, Active or Passive, with an Auto skill marked by something orbiting it** — turns this from an art nicety into a **blocking dependency**: the auto-cast row admits Actives only today, and putting twelve nodes into 24 dp cells that hold neither an icon nor a word would draw **twelve identical dots**. Sequencing is **icons → readout → rotation**. The original line:  [M3-10b](tasks/M3-10b-progression-hud.md) rule 8's 24 dp auto-cast
  cells can hold neither a `LocKey` nor a word, so which skill a cell is showing is not communicated
  at all — the one part of [ledger row 9](#carry-forward-into-m3) that M6-10's localiser cannot fix.
  [M3-10a](tasks/M3-10a-skill-slot-buttons.md) rule 9 has the same gap on the slot buttons, where
  position at least distinguishes them. Promoted by **M7**'s art pass, or by M3-15 if a playtest
  says the row is unreadable without them. **Narrowed at M3-00d: the slot buttons are no longer part
  of it** — [M3-14a](tasks/M3-14a-localizer-and-english-table.md) puts a *word* on a 60 dp circle,
  which is what M3-10a rule 9 wanted and could not have. What is left is the 24 dp cells alone, and
  they are the one reader localisation cannot reach. **M3-13 is no longer named here**: it was, before
  it split into a palette and a health-bar task, neither of which can hold an icon. **The cells exist
  as of M3-10b**, so this is no longer a prediction: twelve of them are authored on `Hud.prefab`, each
  a kind-tinted body with a radial sweep over it, and in every run this build can play they are all
  deactivated because nothing owns an Active until M3-12. The first build in which anyone sees the gap
  is M3-12's.
- **A counting `IRandom` fake in `Tests/Core/Fakes/`.** `SpawnDirectorTests` has a private one and
  **`OfferGeneratorTests` is now the second, written at M3-04** — same shape, renamed reads
  (`OffersDraws` / `OtherDraws` rather than `SpawnDraws` / `OtherDraws`), which is the only thing
  the two copies differ in and the thing a shared fake would have to generalise. **Promoted by the
  third.** `OfferGeneratorTests` also holds a private **LCG** `IRandomStream`, for the two rows that
  need ten thousand different answers rather than a script: `SeededRandom` lives in
  `Soulvail.Game` and `Soulvail.Tests.Core` does not reference it. That is a **first** copy, not a
  second — it is promoted on its own count.
- **`EffectRegistry.CanApply` answers only *"is there a handler for this type"*, and the general
  version — `IEffectHandler<T>` gaining a `CanApply(TEffect)` member — was weighed at **M3-03** and
  deliberately not built.** The owner's ruling, with the trace behind it: for `ModifyStat`, the one
  primitive that exists, every other door is already doubled (`ModifyStat`'s constructor and
  `Modifier`'s both refuse the kind, non-finite values and a null source), the only residual is
  `PlayerStats.Resolve`'s address throw, and **`ModifyStatDefinition.ToEffect` is the only non-test
  constructor of a `ModifyStat` in the project** — which is exactly where M3-02b put
  `Enum.IsDefined`. So nothing reachable is open. Widening the interface now would duplicate that
  check with *worse* diagnostics (a node id, where the authoring door names the asset file and the
  field), and most implementations would be `=> true` — a rubber-stamp member that makes
  `SkillTree`'s rule-4 promise look structural while still resting on each author. **Promoted by the
  first primitive whose applicability is a *run-scoped* question** — one no authoring check could
  answer, because the answer depends on the run rather than on the asset (`SpawnZone`, or anything
  gated on the class) — which is also the first task with a second implementer to write the member
  against. Until then a new primitive owes its own door, and `SkillTree`'s remarks are where that
  rule is written down.
- **`TreeRules` and `SkillTree` address a branch by index into one tree, and CH §5.4 needs a second
  tree's branch beside it.** [M3-03](tasks/M3-03-tree-rules.md)'s constructor takes a single
  `SkillTreeSpec`, and every branch-shaped member — `TryLocate(… out int branch …)`,
  `NodeCountOf(int)`, `TakenInBranch(int)` — is an index into that one tree. Correct for a milestone
  with one class, and it has nowhere to put a branch borrowed from another. **The content side is
  already fine**: a class still has exactly one tree, so M3-02a rule 11's refusal stands and
  `TryGetTreeFor` resolves the second one unchanged — what widens is the *run's* view, not the
  catalog. Promoted by **M5-07a**, whose spec **M5-00a** writes.
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
- ~~**`PROGRESS.md` is 228 KB with an empty Log, and the Log is no longer what grows it.**~~ Done 2026-09-20: Current State is a capped brief, M3's *Verified* rows are in [PROGRESS-M3.md](archive/PROGRESS-M3.md#verification-chain), and the two rules are in [PROGRESS › How to write an entry](PROGRESS.md#how-to-write-an-entry). Neither promoter fired; the owner asked.
- **CI: EditMode tests on every PR** (GitHub Actions + `game-ci/unity-test-runner`). Worth it since M1-21 — 455 tests, and M2 adds migration tests, which rot silently. Blocker: a Unity licence activation secret, not the value.
- **PR template** mirroring a spec's Acceptance section. Not adopted yet.
- **A real Android device.** Every **[device]** row is deferred until one exists and the first hardware session runs them all. BlueStacks cannot run the APK (M0-20a), so there is no fallback outside the Editor.
- **Company name is a placeholder** — `Soulvail`, set in M0-19 with the same status as the application identifier. Both are permanent once uploaded to a store, so M8-06 changes them together before the first upload.
- **Machine-local Gradle configuration is not in this repo** — `~/.gradle/gradle.properties` (HTTP proxy) and `~/.gradle/init.gradle` (Aliyun mirrors) are what make an Android build resolve on this connection; a second machine needs its own. The why, including the SOCKS-vs-HTTP trap, is [Traps.md §10](../Traps.md).
- ~~**Four raw UI strings to localise in M6-10.**~~ Owned by M3-14a rule 8 from M3-00d, because the owner's ruling landed `ILocalizer` three milestones early; shipped there. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- ~~**GD §16.2 and GD §16.4 contradict each other about a dying enemy's colour.**~~ Resolved at M3-15: an ambiguity, not a contradiction — M3-13b's maroon is *toward red* and 0.361 from `#FF4A1F`; §16.2 gained a half-line. [History](archive/ROADMAP-M3.md#parking-lot-items-closed-in-m3).
- **Application identifier** — placeholder `com.soulvail.dev`; permanent once uploaded, so it changes in M8-06 before the first store build.
- ~~**Out-of-range focus tap.**~~ Closed at M2-15 as M2 ledger row 12; built by M2-12a. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- ~~**Already-merged specs still point at pre-split task ids.**~~ Fixed at M2-15 in one pass; the class recurs on every split, and the next milestone that splits owes the same pass to its acceptance. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- **A spatial hash for `AlliesNearby`** — its `n² − n` comparisons a frame cost 756 at M2-04's cap of 28 and 4,032 at the registry's 64. Promoted the day a device cap above 40 ships (M8-03), and not before.
- ~~**GD §12.1's stage-40 budget row says 1,772 where its own formula gives 1,876.9.**~~ Corrected at M2-15 to 1,877; the code was pinned against being "fixed" in three places. [History](archive/ROADMAP-M2.md#parking-lot-items-closed-in-m2).
- Business model decision (GD §21.1) — needed before M6.
- Google Play Games save sync (GD §21.6) — after M2's local save exists.
- `dotnet` SDK on the dev machine → activates the pre-commit format check.
- GPU Resident Drawer evaluation — when a stage first drops below 60 fps on mid-tier.
- Separate `.NET` class library for Core (better tooling) — if Unity-hosted tests ever feel slow.

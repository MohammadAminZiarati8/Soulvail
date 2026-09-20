# M4-02 — The Warden of Ash: a shockwave you walk out of, a fissure you stand off, and Husks

**Size:** M · **Depends on:** M4-01b, M2-06, M2-12b · **Branch:** `m4-02-warden-behaviours`
**Design refs:** GD §9.1 (rules 1, 2, 4, 7), §9.2; AR §18.4 · **Ledger rows:** none — the device half is [M4 row 3](../ROADMAP.md#carry-forward-into-m4)

## Goal

The first boss in the game does three things a player can learn: a **shield-slam shockwave** that expands from
where it stood, **ground fissures** that open under the player's feet and are a place not to be, and it
**summons Husks**. GD §9.2's hook is *"teaches: attack from behind, keep moving"*, and every attack has a safe
answer that costs position rather than health.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/WardenBehaviour.cs` | Core | The attack selection and the three attacks' timing |
| `Core/Combat/Shockwave.cs` | Core | An expanding ring that damages once per body as it passes |
| `Core/Combat/Fissure.cs` | Core | A placed area that arms, fires, and closes |
| `Tests/Core/Ai/WardenBehaviourTests.cs` | Tests.Core | Selection, telegraph lengths, the safe answers |
| `Tests/Core/Combat/ShockwaveAndFissureTests.cs` | Tests.Core | Geometry, once-per-body, and the windows |
| *small edits* | Core | `RunSession.Tick` gains the two steps, beside the zone step M3-11b added; `EnemySpec` gains nothing |
| *data* | — | `Warden.asset` (an `EnemySpec`), `WardenBoss.asset` (a `BossSpec`), and the mode's boss roster entry |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Combat/Shockwave.cs — the same shape as ZoneSystem (M3-11b), on purpose
public sealed class ShockwaveSystem
{
    public const int Capacity = 4;
    public void Emit(Vector3 origin, float speed, float maxRadius, float damage, float thickness);
    public void Tick(float dt, ...);
    public int ActiveCount { get; }
}

// Core/Combat/Fissure.cs
public sealed class FissureSystem
{
    public const int Capacity = 8;
    /// <param name="armSeconds">Telegraph before it bites — GD §9.1 rule 1's ≥ 0.6 s.</param>
    public void Open(Vector3 at, float radius, float armSeconds, float openSeconds, float damage);
    public void Tick(float dt, ...);
    public int ActiveCount { get; }
}

// Core/Events/BossEvents.cs — additions
public readonly struct ShockwaveEmitted { public ShockwaveEmitted(int id, Vector3 origin, float speed, float maxRadius); }
public readonly struct ShockwavePassed  { public ShockwavePassed(int id); }
public readonly struct FissureArmed     { public FissureArmed(int id, Vector3 at, float radius, float armSeconds); }
public readonly struct FissureFired     { public FissureFired(int id); }
public readonly struct FissureClosed    { public FissureClosed(int id); }
```

## Behaviour

1. **Every attack telegraphs for at least 0.6 s, and the number is authored rather than constant** (GD §9.1
   rule 1). A test asserts the *shipped* values clear the floor, so retuning an attack faster than a player can
   react is a red row rather than a playtest discovery.
2. **The shockwave expands from where the Warden stood when it slammed, not from where it is now.** A ring
   that follows the boss is unescapable by walking; one anchored to a place is exactly GD §9.1 rule 2's *"safe
   answer that costs positioning"*. **It damages a body at most once** — the ring passing over someone standing
   still is one hit, not one per frame — which is the same once-per-body discipline `ConeOverlapQuery` has.
3. **A fissure opens under the player's position at the moment it is placed, and does not track.** The safe
   answer is to move; a tracking fissure has none. It **arms visibly**, fires, then closes.
4. **Containment is flat, XZ only** ([AR §18.4](../../Architecture.md#184-combat-and-perception)) — the same rule
   `ZoneSystem` follows, because height is a camera's business and an arena floor is flat.
5. **Both systems tick where the zones do, on the snapshot's clamped `Dt`**, so a 30 fps phone and a 120 fps
   one see the same ring at the same radius. Absolute times, not accumulated deltas — M3-11b's ruling, which is
   why its twelve pulses land identically at both rates.
6. **Summoning is M4-01b's `AddWave` and is not re-implemented here.** The Warden's phases author *"3 Husks"*
   and *"4 Husks"*; this task authors the numbers, not the mechanism (GD §9.1 rule 4).
7. **Attack selection is by cooldown and range, and it is deterministic given a seed.** No `UnityEngine.Random`
   ever, and the stream is the run's — so a boss fight replays identically from a saved run, which is the
   guarantee M3-08a's lazy draw established for offers and which a boss must not break.
8. **A capacity overflow drops rather than throws.** M3-11b's ninth zone *throws*, deliberately, because it was
   unreachable at Consecrate's cooldown — **a boss is different**: an add-heavy phase plus a player-placed zone
   is exactly where a ceiling gets hit, and a run-ending refusal during the first boss fight is the worst
   outcome available. Named here rather than inherited, because the two tasks reach opposite answers on purpose.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Warden_EveryAttackTelegraphsLongEnough` | the shipped `Warden.asset` / every attack's arm time / all ≥ 0.6 s (rule 1) |
| `Shock_ExpandsFromWhereItSlammed` | boss slams then walks 5 m / ticked / the ring's centre is the slam point |
| `Shock_HitsABodyOnce` | a body standing in the path / the ring passing fully over it / exactly one damage event |
| `Shock_MissesSomeoneWhoWalkedOut` | a body outside `maxRadius` when the ring arrives / ticked / no damage — rule 2's safe answer, asserted |
| `Shock_IsFlat` | a body 3 m above the ring's plane / ticked / hit, because containment is XZ (rule 4) |
| `Fissure_OpensWhereThePlayerWas` | player at A, fissure placed, player walks to B / it fires / the damage is at A |
| `Fissure_ArmsBeforeItBites` | arm 0.8 s / a body standing in it / no damage for 0.8 s, then damage |
| `Fissure_MissesSomeoneWhoMoved` | a body leaving during the arm / it fires / no damage (rule 3) |
| `Systems_TickIdenticallyAtBothRates` | the same fight at 30 and 120 fps / the same elapsed time / identical hits, identical radii (rule 5) |
| `Selection_IsDeterministic` | the same seed and the same state / two runs / the identical attack order |
| `Selection_UsesNoEngineRandom` | the assembly / reflected / `Soulvail.Core` names no `UnityEngine.Random` — the standing purity row, extended |
| `Capacity_DropsRatherThanThrows` | `Capacity` shockwaves live / a fifth / dropped, no throw, and the run continues (rule 8) |
| `Warden_SummonsWhatItsPhasesAuthor` | phase 2 / entered / the Husks its `BossSpec` names, through M4-01b's path (rule 6) |
| `Tick_AllocatesNothing` | 10 000 ticks with rings and fissures live / `AllocationAssert.None` / zero |

**Guard rows are implied, not listed:** non-finite radii, speeds and durations; a negative count; a fissure
opened at a non-finite position.

## Manual verification (Editor / device)

**Steps 0a–0c are [M4-01b](M4-01b-boss-agent-framework.md)'s, carried here because they need a stage 5 that has
a boss on it and no build before this one had.**

0a. **[Editor]** Play to stage 5. *Expected: no wave — one large body, and the debug overlay's enemy count is 1.*
0b. **[Editor]** Damage it past 66 %. *Expected: it stops moving and stops taking damage for 1.5 s, any adds
   vanish, then it resumes.*
0c. **[Editor]** Past 33 %. *Expected: the same again, and the phase readout on the overlay reads 2 of 3.*
**Steps 1 and 2 as this spec wrote them are NOT runnable in this build, and the reason is this task's own
*Out of scope*: nothing draws a ring or a crack.** No view in `Soulvail.Game` subscribes to
`ShockwaveEmitted` or `FissureArmed` — `ZoneView` (M3-11c) is the only hazard view that exists — so both
hazards are real in core and invisible on screen until **M4-03** draws them. **This is M4-01b's gap repeated
one task later**, and it was found by the owner playtesting against a handover that claimed otherwise. The
rewritten steps below test the same two rules by their *effect*, which is observable now; the original
wording is kept struck through so M4-03 inherits it verbatim.

1. ~~**[Editor]** Reach stage 5 and let the Warden slam. *Expected: a ring leaves the point it slammed and
   passes through you if you stand still; walking out of it costs you nothing.*~~ **→ M4-03.**
   **Runnable now, by effect:** reach stage 5, stand within about 2 m and wait for the body to swell
   (`EnemyHitFeedback` draws `EnemyTelegraph`, so the *wind-up* is visible even though the ring is not).
   *Expected: standing still costs ~22 hit points roughly a second after the swell; doing it again and
   walking past about 7 m before the hit lands costs nothing at all.* **What is being read is the health
   bar, not the floor.**
2. ~~**[Editor]** Let a fissure arm under you and step off it. *Expected: it fires where you were and
   misses.*~~ **→ M4-03.**
   **Runnable now, by effect:** stand past 5 m so the fissure is the only attack it can make, and wait for
   the swell. *Expected: standing still costs ~22 hit points 0.9 s later; taking one step of about 3 m
   during that 0.9 s costs nothing.*
3. **[Editor]** Fight it to phase 2. *Expected: Husks appear; killing them still pays XP, because rule 6 makes
   them ordinary agents.* **Runnable now** — a Husk is an ordinary body and `EnemyView` already draws one.
4. **[device]** Whether a ring and a fissure **read on a 6-inch screen** — GD §9.1 rule 7, which says to test
   every telegraph at phone size *before calling it done*, and which no Editor can answer.
   [M4 ledger row 3](../ROADMAP.md#carry-forward-into-m4).
5. **[device]** Whether 75–120 s of one enemy holds attention at all, or reads as a health sponge.

## Out of scope

- **The boss's body, animations and the hazard's art** — M4-03.
- **The segmented bar** — M4-04.
- **Bosses 2, 3 and 4** (Choirmother, Gravemaw, Archon) — M7. This task authors one roster entry.
- **The boss buffing itself.** M4-01a's seam exists; the Warden does not use it, and authoring a buff it does
  not need to prove a seam is the wrong reason to author content.
- **Audio.** GD §9.1 rule 1 wants *"visual **and** audio cues"* and there is no audio system in this project
  at all — M7's pass owns it, and **this task ships half of rule 1 and says so** rather than pretending.

## As built

**Five counted files — the Files table exactly, no split.** `WardenBehaviour.cs`, `Shockwave.cs`,
`Fissure.cs` and the two test fixtures. **`Game/Authoring/BossDefinition.cs` is a sixth new file and
is deliberately not counted**, on the grounds the Files table's *data* row states: it is the
authoring shell `WardenBoss.asset` cannot exist without, M4-01b did not ship it precisely because
*"an authoring type with no asset is the guess AR §6 bans"*, and it is 220 lines of which none is
logic — two `ToSpec` conversions over `BossSpec`'s own constructors, which do every check. It is a
deviation from the Files table all the same, and a reviewer should read it as if it were counted.
`Core/Events/BossEvents.cs` gained five event structs, which the Public API lists and the Files table
does not — the precedence rule settling in the Public API's favour, exactly as M4-01b's three did.

**Eight additive edits, none of which changed an existing line's behaviour**: `EnemySystem` (an
optional trailing constructor parameter, one field, one `??`), `RunSession` (two fields, the two
hazards built, two tick steps, two `Clear` lines, a boss-roster resolution loop), `BootInstaller` (an
optional trailing parameter and one `Convert`), `BootScope` (a serialized field and one argument),
`BootScope.prefab` (two list entries), `Descent.asset` (the boss roster row), `English.asset`
(`enemy.warden.name`), and two test fixtures this task does not own — `ContentValidationTests` (one
row, two constants, one helper) and `OathboundTreeTests` (two asset paths and one catalog argument),
both explained below.

### The one thing the spec asked for that the code could not do

**`inner` cannot be built by the caller of `SpawnBoss`, and the reason is structural rather than
awkward.** The instruction was that the Warden's attacks *"go in as the delegated behaviour, through
`EnemySystem.SpawnBoss(bossId, position, inner)`, without changing that signature"*. Every
`IEnemyBehaviour` in the project holds the `EnemyAgent` it drives — it has to, because
`IEnemyBehaviour.Tick` is handed an `EnemyTickContext` with no *self* on it — and the agent does not
exist until `SpawnBoss` has spawned one. The only caller, `SpawnDirector.TickBoss`, therefore cannot
construct the thing it would pass.

**So the signature is untouched and what moved is where the `inner` comes from.** `EnemySystem` gained
a trailing optional `Func<EnemyAgent, BossSpec, IEnemyBehaviour>`; `SpawnBoss` reads
`inner ?? _bossInner?.Invoke(agent, boss)`, so an explicit `inner` still wins and every existing
caller and fixture compiles unchanged. `RunSession.Start` supplies the factory, which is the one
place that holds the hazards, the run's generator and the census at the same moment.

**Every boss gets a `WardenBehaviour`, and that is stated rather than hidden.** V1 has exactly one
boss. The alternatives were a `switch` on a `ContentId` (content identity in code) or an attack-set
enum on `BossSpec` invented for a single implementer (the guess AR §6 bans, and the refusal M4-01b
made twice). **The line becomes a dispatch when GD §9.2's second boss is authored — M7's
Choirmother, whose mechanic is rotating shields and a sonic cone** — and the comment in
`RunSession.Start` says so at the point it would be edited. Nothing can reach it wrongly today:
`RunSession.Start` refuses a run whose roster names a boss the catalog does not hold, and the catalog
holds one.

### Rulings this task made

**The shockwave's front edge bites, rather than a band the body must be inside of — and that is what
makes rule 5 exact.** A band test (`|distance − radius| ≤ half`) can step clean over a body between
two frames whenever `speed × dt` exceeds the thickness: at the shipped 8 m/s that is 0.27 m a frame
at 30 fps against a 1.2 m band, which is safe — and stops being safe the first time either number is
retuned, silently, in a way `Systems_TickIdenticallyAtBothRates` would only catch by luck. The edge
is monotonic, so a body inside the ceiling is hit on the first tick the edge has reached it, at any
frame rate, and the once-per-body flag does the rest. **`thickness` therefore became a drawing width
rather than a hit tolerance**, and says so where it is declared.

**Beyond `MaxRadius` is safe ground *exactly*, checked before the edge rather than after it.** The
hit test refuses a body outside the ceiling even where the band's front edge has swept past the
ground it stands on. That is what makes GD §9.1 rule 2's *"walk out of it"* a rule a player can
learn rather than a tolerance they have to feel out, and it is the one line
`Shock_MissesSomeoneWhoWalkedOut` is actually about.

**GD §9.1 rule 1's 0.6 s is a floor in code, and it is a clamp rather than a refusal.**
`WardenBehaviour.TelegraphSeconds` is the body's authored `EnemySpec.WindupTime` raised to
`MinTelegraphSeconds`, so a Warden retuned to 0.3 s is still *played* at the window the design
promises. The rule is marked non-negotiable, so it should not be breakable by a tuning mistake; and
a hazard that threw instead would end a live run over one, which is worse than either. **What a
clamp cannot do is say the asset and the fight have parted company**, so
`ContentValidationTests.Boss_EveryAttackTelegraphsLongEnough` walks every `BossDefinition` on disk to
the `EnemyDefinition` it names and asks the question of the shipped number. That row is in a third
fixture because `Soulvail.Core` references no Unity assembly and `Soulvail.Tests.Core` cannot load an
asset — the spec's row says *"the shipped `Warden.asset`"*, and this assembly is the only one that
can read one.

**Both attacks telegraph for the same authored number, because `EnemySpec` gains nothing.** The Files
table says so outright. One field, one window, and the fissure's *arm* is that window — the crack is
placed on the way into the state and arms under the player's feet, rather than being placed at the
end of a separate wind-up. A boss that wound up and *then* placed a crack would telegraph twice for
one attack, and the second telegraph — the crack itself, visible on the floor — is the only one that
says *where* as well as *when*.

**A fissure bites once, at the instant its arm ends; the open window that follows is how long the
crack is drawn.** One bite is what makes rule 3's safe answer legible — you were standing on it or
you were not — where a crack that hurt continuously would turn a positioning decision into a
damage-over-time whose edges the player cannot read. **It fires on the tick it closes if one frame
swallows both deadlines**, which is `ZoneSystem.Tick`'s pulse-before-expiry ruling reached again and
worth a whole attack here rather than one pulse.

**The two hazards hang off `RunSession` as private fields, not off `RunState`.** Unlike a zone,
neither needs a read: `ShockwaveEmitted` carries the origin, the speed and the ceiling, and
`FissureArmed` carries the position, the radius and the arm — so whatever draws either integrates its
own animation off the event and never asks core where a hazard is this frame (M4-03 is the first
caller). A zone is the opposite and keeps its two reads, because it stands still and a decal has to
be told where.

**No stage-boundary sweep, and it is arithmetic rather than an oversight.** A ring lives at most
`MaxRadius / Speed` — 0.875 s at the shipped numbers — and a crack at most its arm plus its open
window, both of which are over well inside the two seconds of gate and arrival a boundary already
costs (M2-10). `RunSession.End` clears both; `StageFlow` does not need to, and the comment says what
would change that.

**The boss's attack damage is the agent's `ContactDamage` *stat*, for both hazards**, which is
`SpitterBehaviour`'s rule for a thrown shot applied to ground: GD §12.3's `d(n)` and M7-02's affixes
reach a ring and a crack with no second mechanism. `Slam_CostsTheBodysOwnDamage` drives it through a
real `Modifier`.

**Selection draws from `IRandom.Misc`, and the draw happens only where the choice is real.** Cooldown
and range decide every other tick; the coin decides the tick on which both attacks would have been
legal. `Misc` and not `Spawn`: a boss drawing from the director's stream would make every later stage
of a seeded run depend on how long this fight took (ADR-0011). `Misc` is the first stream in the
project with a caller, and it is already in `RandomState`, so a resumed run continues the fight's
sequence rather than restarting it.

**Rule 8 is the deliberate disagreement with M3-11b, and it is the same word on both hazards.** A
fifth ring and a ninth crack are *dropped* — `Emit` and `Open` answer `0`, publish nothing, and the
run carries on — where a ninth zone throws. The behaviour does not look at the answer and spends the
cooldown either way: the Warden believes it slammed, which is `ProjectileSystem.Fire`'s bargain and
the only reading that does not require a boss to know a hazard system's capacity.

### The numbers, and where each came from

| | Value | Why |
|---|---|---|
| `Warden.asset` HP | **4 200** | Inside M4-01b's window, under its 4 739.63 beats-priced ceiling: 4 200 × h(5) 1.24 ÷ 50.232 DPS = **103.7 s**, plus two 1.5 s beats = **106.7 s**, mid-band of GD §9.1 rule 5's 75–120 s |
| Contact damage | **22** | Between the Bloater's 15 and a boss's weight; 22 × d(5) 1.14 = 25.1 against 140 + 30, so ~15 % of an effective bar a hit |
| Move speed | **1.8** | Slower than the retuned Husk's 2 — it is a wall, and the fight is the player's to position in |
| XP value | **300** | *Not* the 3 × threat convention's 120. A boss stage has no wave budget, so the Warden is priced at what the stage it replaces would have paid: 3 × B(5) = 3 × 102.4 ≈ **307** |
| Windup / recover | **0.9 / 1.2** | The telegraph clears rule 1's 0.6 with room; the recovery is the window GD §9.2's *"attack from behind"* needs |
| Body scale / priority | **2.2 / 8** | The size of a building; priority 8 is GD §8.1's top, so auto-aim never drifts onto an add |
| `ShockwaveSpeed` **8** / `MaxRadius` **7** | — | Chosen against the Oathbound's 3 m/s: from 2.5 m, after the 0.9 s telegraph, a fleeing player is caught at about 8 m — a metre past the ceiling. At 12 m/s the attack is unavoidable from melee; at 5 it can be ignored |
| `SlamRange` **5** | — | Inside the ceiling on purpose: a slam thrown at a player standing *on* the ceiling is one they escape by not moving |
| `SlamCooldown` **6** / `FissureCooldown` **3.5** | — | The slam is an event, not a rhythm; the crack is what stops the fight being won from 10 m, so it comes round nearly twice as often |
| `Capacity` **4** / **8** | — | One Warden holds at most one ring and two cracks of its own; the rest is headroom for M7 and for GD §9.1 rule 6's arena hazard. A ring is a moment and a crack is a place, which is why the two differ |

### Four deviations, each stated

**1. `BossDefinition.cs` is a sixth new file**, argued at the top of this section.

**2. Two test fixtures this task does not own were edited, and both were red first.**
`OathboundTreeTests` builds a catalog from the *shipped* `Descent.asset` and starts a real
`RunSession`; the moment the mode named `boss.warden`, three of its rows failed with the new
resolution guard's own message. **The fixture's own comment had predicted exactly this** — it already
explains that it ships all three archetypes *"because `RunSession.Start` resolves the whole roster
before it announces anything"* — so the fix is the one it describes: `Warden.asset` joins the enemy
paths and `WardenBoss.asset` is passed as the catalog's bosses. `ContentValidationTests` gained the
telegraph row and its anti-vacuity floor, for the reason given above.

**3. `WardenBoss.asset` breaks the file-name-matches-the-last-id-segment convention**, and the spec's
Files table chose the name. `boss.warden`'s last segment is `warden`, and `Warden.asset` is the body.
Two assets cannot share a file name in one folder, so the convention and the spec disagree and the
spec wins. **`BossDefinition` is therefore deliberately *not* added to
`ContentValidationTests`' id-namespace, file-name and cross-kind-uniqueness sweeps** — adding it
would redden the file over a name this task was told to use. The day bosses get a `Data/Bosses/`
folder, the asset can be `Warden.asset` and join all three sweeps at once.

**4. `RunSession.RequireAuthored` gained a boss-roster loop the Files table does not list.** Without
it, a mode naming an unauthored boss plays perfectly for four stages and throws out of
`ContentCatalog.Boss` on the fifth — a hundred seconds in, at a moment that looks like a director
bug. Three places in the codebase already *claimed* this check existed (`ModeDefinition`,
`ContentCatalog` and `BootScope`'s own remarks all say a run is refused before it is announced); it
did not, and now it does. It is six lines and it is what turned the `OathboundTreeTests` failure into
a message naming the mode and the id.

**Verified:** **2 093 / 0 / 0 EditMode, three runs** (24.0 s, 35.7 s, 26.1 s) against M4-01b's
**2 058** — **+35, and the reconciliation is exact**: **18** in `WardenBehaviourTests`, **16** in
`ShockwaveAndFissureTests`, **1** in `ContentValidationTests`; no existing row was removed. **The
first run was red at 2 088 / 5** and all five are recorded above — three `OathboundTreeTests` rows the
new guard caught, and two of this task's own that had mis-counted the state machine's deferred
transition. Console swept after a **clean rebuild of every assembly**
(`RequestScriptCompilation(CleanBuildCache)`): **zero errors and zero warnings of any kind**, so no
compiler and no analyzer warning. `dotnet format whitespace --folder --verify-no-changes` over all
thirteen touched C# files: **exit 0**. `ProjectSettings/TimeManager.asset` dirtied and reverted for
the **twenty-first** time, same rational form (2 822 399 / 141 120 000 = 0.02, `m_TimeScale` 1).

### What a player of this build can see: stage 5

**This is the task that makes a boss reachable, and it is also the task that proves you cannot see what
it does.** Descend, clear four stages, and the fifth arrives with no waves and one body two and a bit
times a Husk's size, at 4 200 hit points, that slams, cracks the ground and calls in Husks at 66 % and
33 %. **What is on screen: the body** (`EnemyView`, dark and 2.2× a Husk), **the wind-up before every
attack** (`EnemyHitFeedback` draws `EnemyTelegraph`, and both attacks publish one), **the Husks**, and
**the player's health moving.** What is *not* on screen is the ring and the crack themselves —
`ShockwaveEmitted` and `FissureArmed` go out every fight and **nothing in `Soulvail.Game` subscribes to
either**. That is this spec's own *Out of scope* line working as written, and it makes the fight read,
correctly, as *"something invisible just hit me."*

**The owner found this by playtesting, against a handover that had claimed the ring was visible.** The
manual list above is rewritten as a result, and this is the second consecutive task whose manual steps
could not be run as written for the same structural reason — M4-01b's steps needed a boss that did not
exist, and M4-02's need a view that does not. **[M4-03](M4-03-boss-arena-and-views.md) inherits both the
original wording and the obligation**, and its own acceptance should re-run steps 1 and 2 as this spec
first wrote them. **Steps 4 and 5 are device rows and remain deferred** to
[M4 ledger row 3](../ROADMAP.md#carry-forward-into-m4) — whether a ring and a crack read on a 6-inch
screen is GD §9.1 rule 7's question, no Editor can answer it, and nothing draws them to answer it with
yet either.

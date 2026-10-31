# Soulvail — Characters and Skills

**Version:** 0.3 — *in-run skill trees, and the second class*
**Date:** 2026-09-13
**Companion to:** [GameDesign.md](GameDesign.md) — read §5 (controls), §10 (Veilrot), and §12 (difficulty) first.

---

## 0. What changed from v0.1, and why it's better

v0.1 put the skill tree in **meta-progression** — permanent, levelled across runs, with a separate in-run card system beside it. That was wrong for this game.

**The tree is in-run.** You pick a class, enter stage 1, kill things, gain XP, level up mid-run, and each level lets you take a node. At death the tree is gone and the next run builds a new one.

This is strictly better here, and it collapses three problems at once:

| v0.1 problem | Resolved by |
|---|---|
| Two progression systems (tree + Sanctum cards) competing for the same design space | **They merge.** The tree *is* the in-run progression. Cards are deleted as a separate system; every former card is now a tree node. |
| Replay fatigue — good players re-grinding solved early stages | **Dissolved.** Replaying stages 1–10 isn't repeating solved content, it's *rebuilding your build*, which is the core fun. |
| **Anchors** (§14.3) existed only to paper over that fatigue | **Cut entirely.** A whole system removed from scope. |

Meta-progression shrinks to one job: **unlocking characters**. That's a significant scope reduction, and it makes the game more of a roguelite rather than less.

**v0.3 changed one thing on top of that:** halfway through the tree, a run picks up **one branch of a second unlocked class** (§5.4). It adds no nodes — it multiplies the ones v0.2 already scoped — and it gives the unlocks above something to be worth.

---

## 1. Mode context

This document describes character behaviour in **Descent** — the first and, for V1, only mode.

> **Descent:** choose a class → stage 1 → endless ordered stages of rising difficulty → level up and build a tree as you go → play until you die. No win condition. The score is how deep you got.

Further modes come later. The only thing that matters now is that **mode is a data object, not an assumption** — see [GameDesign.md §4.5](GameDesign.md). A class must not hard-code anything that only makes sense in Descent.

---

## 2. Class design axes

With one-thumb controls ([GameDesign.md §5](GameDesign.md)) there is one stick and a small number of buttons. Class identity **cannot** live in complex input — there's no room for it.

That constraint is worth embracing. Identity lives in five places:

| Axis | What varies |
|---|---|
| **Weapon behaviour** | The auto-fire pattern itself — projectile, range, cadence, AoE |
| **Movement skill** | Replaces the dash. Charge, Shroudstep, Blink — each reshapes positioning |
| **Signature passive** | An always-on rule that needs no input |
| **Veilrot relationship** | How the signature system treats you. The strongest differentiator available. |
| **Tree** | Three branches of skills unique to that class |

**The insight worth stating plainly:** on a one-thumb scheme, the best class mechanics are **autonomous** — summons, auras, passives, on-kill triggers, auto-cast actives. Things that act without being told to. This is also exactly why auto-cast (§4.2) is the right default rather than a concession.

---

## 3. The roster

Vailkeepers were an order, not a job. Different hands for different work.

| | **Oathbound** | **Gravecaller** | **Emberwright** |
|---|---|---|---|
| Archetype | Paladin | Necromancer | Wizard |
| Unlock | **Free — the starter** | 2,000 Shards *or* kill Choirmother | 3,500 Shards *or* reach stage 20 |
| HP / Speed | 140 / **3.0** | 80 / **3.1** | 70 / **3.4** |
| Difficulty | Easy | Medium | Hard |
| Veilrot | **Resists** it | **Thrives** on it | **Spends** it |
| Fantasy | Walk into the swarm. Make it regret touching you. | You don't fight. The dead do. | Enormous, slow, unforgiving. |

The Oathbound starts unlocked because tanky and forgiving is the right teaching class — a new player learning a one-thumb game should not also be learning to survive at 70 HP.

**The speed column was 5.4 / 5.6 / 6.2 until M5-02.** The owner retuned the Oathbound to 3 m/s after playtesting at M2-03 and dropped the Husk 3.5 → 2 in the same pass; the documents kept the old band for two milestones because moving it is a statement about two classes that did not exist. M5-02 is the first task that has to author a second class's speed, so it scaled the whole band by the factor the retune already applied — **3 / 5.4 = 0.5556** — and rounded to a decimal. The *ordering* is what the design asked for and it is preserved exactly: the tank is the slowest, the wizard the fastest. See [GD §6.1](GameDesign.md) and [CoreCombat.md](CoreCombat.md) §2.5.

### 3.1 Oathbound — *paladin*

| | |
|---|---|
| **Weapon** | **Censer** — 8m forward cone, 18 dmg, 3.0/s, hits everything in the arc |
| **Movement** | **Charge** — 10m, damages and knocks back everything hit, full i-frames |
| **Signature** | **Aegis** — a 30-point shield regenerating after 4s without damage. The only regeneration in the game. |
| **Veilrot** | Gains **−40%** from Pact nodes, Cleanses at half price — but his Pacts are weaker: **his own Pact forms are authored to about ¾ of GD §13.2's 1.8× budget** *(ruled at M6-11 — a Pact is authored, so "25 % weaker" is a budget and not a multiplier, which would have turned a Pact's downside into a discount; the two shipped, Keen Censer and Zealotry, predate the rule and M7-04 re-reads them against it)* |
| **Branches** | **Oath** (shield, endurance) · **Censure** (the cone) · **Judgment** (auras, holy actives) |

| Keystone | Effect |
|---|---|
| ★ **Unbroken** *(Oath)* | Aegis recharges 2× faster, and breaking it emits a 6m knockback |
| ★ **Wide Censure** *(Censure)* | The cone becomes a full 360° ring at 60% damage |
| ★ **Martyr** *(Judgment)* | When Aegis breaks, deal damage equal to everything it absorbed to all enemies within 10m |

Martyr is the class's best node: it turns the defensive stat into a damage resource and rewards *letting* the shield break at the right moment.

### 3.2 Gravecaller — *necromancer*

| | |
|---|---|
| **Weapon** | **Bone Bolt** — 7 dmg, 4.0/s. Deliberately weak; you are not the damage. **The asset ships 9** — see below. |
| **Movement** | **Shroudstep** — 6m blink leaving a corpse-decoy that taunts enemies for 3s |
| **Signature** | **Rise** — 25% of enemies killed rise as **Wights**: autonomous minions, 20s lifespan, base cap 3 |
| **Veilrot** | **Starts at 15.** Gains +50% faster. Gains **+1% damage per Veilrot point.** |
| **Branches** | **Legion** (minions) · **Grave-Work** (weapon, curses) · **Rot** (Veilrot synergy) |

| Keystone | Effect |
|---|---|
| ★ **The Host** *(Legion)* | Minion cap **+4**, but all Wights have 50% HP |
| ★ **Second Death** *(Grave-Work)* | Enemies killed **by minions** explode for 25 damage in 3m |
| ★ **Rot Bloom** *(Rot)* | Veilrot thresholds grant **buffs instead of penalties**, and the Claiming's HP drain is **halved** |

**Rot Bloom is the best node in the game.** It's the point where a class fantasy and the game's signature system become the same sentence — the necromancer is the one for whom corruption was never a cost. It creates the game's most interesting build: a Gravecaller who *rushes* to 100 Veilrot on purpose and plays the back half of a run inside what is normally a death spiral.

Minions are also pure class identity at zero input cost, which is why this is the right second class to build.

**7 damage breaks GD §6.2 and `Gravecaller.asset` ships 9.** A Husk has 36 HP, so `ceil(36 / 7)` is six hits at stage 1 with no tree and no Overflow, against the *"3–5 hits from any class, at any depth"* GD §6.2 calls the primary balance invariant of the whole game — and the Wights cannot rescue it, because Rise needs a kill, so the first enemy of every run is fought with the weapon alone. At 9 it is four hits, and the class still trades down: 36 DPS against the Censer's 39, on 80 HP against 140. The 7 stays in the table above because it is what the design published and M5-02's control test measures it; the number the game plays is the asset's, and the owner overrides it by typing a different one.

**Watch item:** Wights must be unmistakable from enemies at phone scale. Cyan-tinted, per the colour language. If players can't tell their army from the swarm, the class fails.

### 3.3 Emberwright — *wizard*

| | |
|---|---|
| **Weapon** | **Cinder Orb** — 30 dmg, 1.5/s, slow (25 m/s), 3m AoE detonation. **The asset ships 17** — see below. |
| **Movement** | **Blink** — instant 10m teleport, 2.0s cooldown, leaves a fire pool |
| **Signature** | **Kindling** — consecutive hits without taking damage stack +2% damage to +60%. Resets on any hit. |
| **Veilrot** | **Spends** it — any ability may be cast instantly off-cooldown for **5 Veilrot** |
| **Branches** | **Ember** (fire, weapon) · **Arcana** (actives, cooldowns) · **Ash** (zones, AoE) |

| Keystone | Effect |
|---|---|
| ★ **Wildfire** *(Ember)* | Kindling no longer fully resets — you lose half your stacks instead |
| ★ **Overflow** *(Arcana)* | Every 5th ability cast is free and instant |
| ★ **Scorched Vail** *(Ash)* | Your fire pools spread to any enemy that burns in them |

Pleasing synergy worth noting: slow projectiles are normally the hardest thing to aim, and our auto-targeter leads shots for you. A weapon that would be miserable to thumb-aim becomes the class's whole appeal.

**30 damage breaks GD §6.2 and `Emberwright.asset` ships 17.** A Husk has 36 HP, so `ceil(36 / 30)` is two hits at stage 1 with no tree, no Overflow and no Kindling — under the *"3–5 hits from any class"* GD §6.2 calls the primary balance invariant, and it stays at two until stage 13 on the shipped curve. Lowering the fire rate instead would move DPS without moving hits, which is the argument M5-02 already refused for the Bone Bolt. At 17 it is three hits and 25.5 DPS against one body — the slowest single-target killer in the game and the widest blast in it, which is *"enormous, slow, unforgiving"* as two numbers. Against four bodies inside the 3 m it is 102. The 30 stays in the table because it is what the design published and M6-07a's control test measures it; the number the game plays is the asset's, and the owner overrides it by typing a different one.

**Kindling at full ramp goes under the band on purpose.** +60 % is ×1.6 and GD §6.2's band is 5 / 3 wide, so any weapon at three hits cold is at two hits hot: `ceil(36 / 27.2) = 2`. Thirty consecutive hits without taking a point of damage is the price, and `KindlingTests.Kindling_AtFullRampGoesUnderTheBand` makes a retune of either number a decision rather than a drift.

### 3.4 Ranger — *archer* (the class that ships today)

| | |
|---|---|
| **Body** | Own model on `Rig_Medium`. 90 HP, no Aegis, 3.2 m/s |
| **Weapon** | **Longbow** — 13 dmg arrows, 2.2/s, 10 m range, 30 m/s. **Holds fire while moving**, bow down; stops to shoot |
| **Movement** | **Roll** on Space — 5 m in 0.3 s, 2.5 s cooldown, its own animation |
| **Active** | **Volley**, earned from the tree; the cell glows cyan when ready |
| **Tree** | 3 branches × 3 nodes, free-pick (§5.1). No Veilrot relationship yet |

### 3.5 Ranger paths — levels 11 to 30

*Owner's ruling, 2026-10-31. For the Ranger this replaces §5.4: the second discipline is a specialisation of the Ranger, not a branch of another class.*

The level cap is **30**. Points from levels 2–10 fill the Ranger tree (9). From level **11** the player chooses **one** of three paths, locked for the run; levels 11–30 (20 points) are spent in it. **The Ranger's skills stay** — the path is added above them.

Each path is **8 skills: 5 passives, 2 actives, 1 ultimate**. Seven have 3 ranks, the ultimate 2 — **23 ranks for 20 points**, so even a level-30 build leaves three unbought. Rows are level-gated:

| Unlocks at | Opens |
|---|---|
| Lv 11 | 1 passive + the signature active |
| Lv 15 | 2 passives |
| Lv 20 | 1 active + 1 passive |
| Lv 25 | 1 passive |
| Lv 28 | Ultimate (rank 2 needs Lv 30) |

Built one at a time: **Marksman first**, playtested, then the next. Skirmisher and Echo Archer below are recorded, not specced.

**Marksman — stand still.** Steady Aim (P 11: arrows while standing stack damage ×5) · **Ballista Bolt** (A 11: huge line-piercing bolt) · Piercing Arrows (P 15: through 1/2/3) · Deadeye (P 15: first arrow after stopping crits) · **Mark Target** (A 20: toughest enemy takes more; its kill resets Ballista) · Eagle Eye (P 20: range, more damage far) · Hold the Line (P 25: less damage taken standing) · *Killshot* (U 28/30: every 3rd standing arrow is a Ballista Bolt).

**Skirmisher — never stop.** Running Shot (P 11: fire while moving at 40/55/70% rate) · **Caltrop Roll** (A 11: roll leaves slowing spikes) · Hit and Run (P 15: hits give a speed burst) · Momentum (P 15: damage from distance moved in 2 s) · **Whirlwind** (A 20: spin, arrows all round) · Second Wind (P 20: roll cooldown; rank 3 two charges) · Untouchable (P 25: first hit within 1 s of a roll misses) · *Storm Run* (U 28/30: every roll fires a Volley).

**Echo Archer — copies shoot for you.** Echo Arrow (P 11: every 4th/3rd/2nd arrow repeated by a shadow) · **Mirror Image** (A 11: 1–3 clones shoot for 5 s) · Decoy Roll (P 15: roll leaves a clone enemies target) · Shared Volley (P 15: clones Volley with you) · **Shadow Swap** (A 20: swap with farthest clone; auto at low HP) · Fading Burst (P 20: clones explode on expiry) · Umbral Link (P 25: clones take part of your damage) · *Twin Shadow* (U 28/30: one permanent clone).

**The tree screen** shows one icon per skill with rank pips (●●○), not a node per rank: Ranger row at the bottom, the chosen path's level rows above, locked rows marked with their level. Before level 11 the path area shows the three cards (tap = greyed preview); after the choice, the other two disappear. Actives auto-cast and get a cyan edge; no active buttons on the battle HUD.

---

## 4. Skills — passive, active, auto-cast

Every tree node is one of four kinds.

| Kind | Share | Behaviour |
|---|---|---|
| **Passive** | ~45% | Always on. Stat changes and rule changes. |
| **Active** | ~25% | Grants a skill with a cooldown. **Auto-casts by default.** |
| **Upgrade** | ~25% | Improves a skill you already own — lower cooldown, more damage, an added effect. Only offered if you own the parent. |
| **Keystone** | 3 per class | Build-defining, end of a branch, occasionally with a real drawback. |

Upgrade nodes are what make this feel like a *tree* rather than a list: taking **Consecrate** early makes three later nodes meaningful, so an early pick reshapes the whole rest of the run.

**The shares are over a full 27-node class tree, and what ships today is under all of them — deliberately, and flagged rather than quietly true.** Both v1 trees hold **twelve** nodes of the eventual twenty-seven, and across the 24 shipped nodes there are **3 Actives** (Consecrate, Bulwark, Exhume) — **12.5 %** against this table's ~25 % — and **no Keystones at all** against *3 per class*. Neither number is a tuning miss: a v1 tree is the half of a branch a run can actually walk, and the missing Actives are missing for a stated reason rather than for want of authoring. **M5-06b rule 3 ruled on the Gravecaller's own three**: Exhume ships because M5-06a built the verb it needs; **Tether and Rot Nova do not**, because each wants an effect primitive nobody has built, and Rot Nova's trigger reads a `TriggerField.Veilrot` nothing writes until M6-04. **The share becomes measurable — and this row becomes checkable — at M7-04**, which authors all 81 nodes and the three Keystones a class. Until then the table describes the target, not the build.

### 4.1 Cooldowns

Every active has a base cooldown, reducible by Upgrade nodes and passives. Cooldown reduction is **multiplicative and floored at 40%** of base — otherwise late-run stacking produces permanently-active everything, which flattens the moment-to-moment loop into a single held note.

### 4.2 Auto-cast is the default

**Every active skill auto-casts when off cooldown and its trigger condition is met.** The player does nothing.

This is not a concession to touch controls — it's the correct design for a game whose entire skill expression is positioning (P2). Ten actives on manual buttons would be unplayable on a phone *and* would drown the thing we actually want players thinking about.

Each skill ships with a designer-authored trigger condition so auto-cast feels smart rather than random:

| Skill | Auto-cast trigger |
|---|---|
| Sever (radial burst) | ≥3 enemies within 6m |
| Consecrate (heal zone) | Player HP < 60% |
| Exhume (raise 3 Wights) | Wight count < half of cap |
| Bulwark (shield wall) | An enemy projectile is inbound |
| Rot Nova | Veilrot ≥ 50 **and** ≥4 enemies within 8m |

Conditions are authored per skill, not configured by the player — exposing a condition editor on a phone would be a rules-engine UI, and nobody wants that.

### 4.3 Auto-cast is a per-skill switch

**Every active skill has an Auto/Manual toggle, defaulting to Auto.** Set it to Manual and it stops auto-casting and takes a button; you cast it yourself. Up to **4 manual slots**, plus the always-present movement skill button.

Full spec — slot layout, the management screen, trigger conditions, button sizes — is in [CoreCombat.md §6](CoreCombat.md).

The reason this matters: auto-cast is condition-driven, but a skilled player is *intent*-driven. Casting Wane the instant before a Lunger commits is worth far more than casting it because four enemies happened to wander within 8m. Manual should be a real advantage in skilled hands and never a requirement — if playtests show it's mandatory to survive depth, the auto conditions are too dumb and we fix those, not the balance.

Toggles are changeable **any time, including mid-run**, because the tree is in-run: you acquire skills as you go, so managing them has to work during play.

### 4.4 Pact nodes — Veilrot inside the tree

Veilrot survives the merge, and gets better for it. When a level-up offers its 3 nodes, **one may be a Pact variant** — visually distinct (violet, corrupted), roughly **1.8× stronger** than the equivalent clean node, and granting **+10 to +20 Veilrot**.

Pacts are not a branch. Any node in any branch can appear in its corrupted form, so the temptation is continuous rather than a decision you make once at the start. That keeps [GameDesign.md §10](GameDesign.md) load-bearing every single level-up.

---

## 5. Tree structure

Identical skeleton for every class, so the UI is built once and content varies.

```
        BRANCH A            BRANCH B            BRANCH C
  T1  ┌─◇ ◇─┐             ┌─◇ ◇─┐             ┌─◇ ◇─┐
  T2  ├─◇ ◇─┤             ├─◇ ◇─┤             ├─◇ ◇─┤      ◇ = node
  T3  ├─◇ ◇─┤             ├─◇ ◇─┤             ├─◇ ◇─┤      ★ = keystone
  T4  ├─◇ ◇─┤             ├─◇ ◇─┤             ├─◇ ◇─┤
  T5  └──★──┘             └──★──┘             └──★──┘
```

**Two nodes a tier, four tiers, then the Keystone alone: 9 a branch, 27 a class.** The earlier drawing here
showed eight tiers of one node, which is 24 — it disagreed with the *Total nodes* row directly beneath it
for two milestones. Corrected at M3-15 against the shape ruled at
M3-00a and shipped by M3-02a rule 7, which is what `SkillTreeSpec` has
enforced since it was written. **M3's v1 tree is a subset of this, not a different shape**: three branches ×
two tiers × two nodes = twelve, no Keystones (M3-12c).

| Rule | Value |
|---|---|
| Branches | 3 per class |
| Nodes per branch | **9 (8 + 1 Keystone)** — four tiers of two, then the Keystone alone |
| **Total nodes** | **27** |
| Cost | 1 level = 1 node. No point economy, no partial saving. |
| Branch gating | Tier *N* requires *N−1* nodes already taken in that branch |
| Keystone | Requires all **8** preceding nodes in its branch |

**A deep run reaches roughly level 42 by stage 30 and level 50 by stage 35** — measured at
M3-15, not estimated. So a deep run **completes** the tree
(27 picks, filled around stage 21) and spends everything after that on Overflow; a stage-15 run gets
twenty picks, which is about three-quarters of it. Reaching a Keystone requires committing **9** of your
picks to one branch — expensive enough to be a real decision, and reachable in any run that gets past
stage 8.

**This paragraph used to say "roughly level 30" and "8 of your ~30 picks", and both were wrong** — the
first by twelve levels, the second by the same off-by-one as the table above. The design consequence is
worth stating rather than quietly fixing: **the tree is not the scarce thing a deep run is choosing
between.** Overflow is where more than half a stage-30 run's power comes from, so *"a great run nearly
completes the tree"* stopped being true some time before anyone noticed. Whether that is the right shape is
[§8](#8-open-questions)'s fourth open question, and M7-04 is the task that can move it.

### 5.1 Levelling: banked points, and the tree screen

**In the Jungle (a mode with `HasFreePick`), a level banks a point and play goes on.** A green count on the level star shows the points unspent, and a "LEVEL UP!" toast marks the moment without stopping anything. Tapping the star — or View Tree in the pause menu — **pauses and opens the tree as a grid** (a column per branch, tier 1 at the bottom). A banked point takes **any available node**; the screen stays open until the player closes it, and closing returns to play (from the star) or to the pause menu (from the menu). **Overflow comes only once nothing is open to spend on**: a full tree's leftover points settle into Overflow as before.

Free-pick is a tree of 3 × 3, so it ships for the Ranger alone. **Pacts, Reroll, Banish and the half-tree splash are off while free** — switched off, not removed — because each of them acts on the random-three offer, which a free run does not make.

#### Descent, until its classes are reshaped

Descent keeps the older flow, because its classes have 12-node trees not yet reshaped into 3 × 3, and class select offers the Ranger alone. Levelling there pauses the game and offers **3 nodes drawn at random from everything currently available** (prerequisites met, not yet taken). Tap to take.

Random-from-available rather than free-pick-anything, because:

- It's fast — the pacing budget for a mid-combat interruption on a phone is a couple of seconds, not a planning session
- It preserves run-to-run variety, which is the entire point of a roguelite
- It makes Upgrade nodes exciting rather than automatic

But **a "View Tree" toggle on the level-up screen** shows the full tree with your path highlighted, for players who want to plan. Opt-in, so it never slows down anyone who doesn't care. The tree is also viewable any time from pause (read-only in a mode without free-pick).

| Sanctum service | Cost | Effect |
|---|---|---|
| **Reroll** | 25 Essence | Reroll your next level-up offer (doubles per use) |
| **Banish** | 40 Essence | Permanently remove a node from this run's offers |
| **Heal** 30 HP | 40 Essence | |
| **Cleanse** 15 Veilrot | 60 Essence | |

The Sanctum ([GameDesign.md §13](GameDesign.md)) is now **a shop, not a second upgrade screen.** One progression moment (level-up), one economy moment (Sanctum). Clean separation, no overlap.

### 5.2 Levelling curve

XP comes from kills, auto-collected. Levels should arrive fast early and slow deep, so the build takes shape quickly and then matures.

```
XP to reach level N  ≈  20 + 12·N^1.4
```

| Around stage | Expected level | Level roughly every |
|---|---|---|
| 1–5 | 1–8 | 25–35 s |
| 10 | ~14 | 45 s |
| 20 | **~27** | 65 s |
| 35 | **~50** | 90 s |

**These are measured, not intended** — corrected at M3-15 by
running the shipped `XpCurve` against `Descent.asset`'s threat budget at the real `ToReach(Level + 1)`
threshold. The exponent **stays at 1.4** and the table moved to meet it, which is the reverse of what the
ROADMAP's parking lot expected: at stages 5 and 10 — the two depths M3-00d's checklist actually tests —
1.4 was already right (**L8** and **L14** against *1–8* and *~13*). It is stages 20 and 35 where the
original table was wishful. **Retuning to ≈1.6 was examined and rejected**, because it pushes a stage-15
Husk from **4 hits to 5** — still inside [GD §12.4](GameDesign.md)'s band, but sitting on its ceiling
with no headroom until M7-04 authors all twenty-seven nodes.

**The tree fills at stage 9** on a twelve-node v1 tree (level 13), and at **stage 21** on a full 27-node one.
Everything past that is Overflow, which by stage 30 is **29 levels** and more than half the damage a run has
gained — the reason M3's ledger row 1 closes at 5 hits, and the
thing M7-04 shifts back.

Past the point where the tree is full, further levels grant **Overflow**: +2% damage and +2% max HP each, forever. Small, uncapped, and just enough that levelling never stops meaning something in an endless mode.

### 5.3 The balance rule that keeps this honest

> **The tree must never outrun the threat budget.**

Enemy scaling is quadratic ([GameDesign.md §12.1](GameDesign.md)); tree power must stay roughly linear so the curves cross and the run ends. If a full tree makes a player unkillable, the mode has no failure state and the entire difficulty design is decorative.

Tune so that a **well-built level-30 character dies somewhere in stages 35–50**, matching the death horizon in §12.5.

### 5.4 The second class

Halfway through the tree, the run picks up a second discipline.

> **When half your tree is taken, choose one branch of a second unlocked class.** You keep everything you already have; what changes is the pool the level-up draws from.

| Rule | Value |
|---|---|
| **When** | The level-up at which **half the tree's nodes** are taken — level 7 against v1's twelve, level ~15 against the full twenty-seven |
| **What you choose** | A second **unlocked** class, then **one of its three branches** |
| **What you gain** | That branch's nodes enter your offer pool, gated by the same tier rule (§5) |
| **What you do not gain** | Its weapon, its movement skill, its signature passive, its Veilrot relationship — **and its Keystone** |
| **Reversible** | No. Locked for the run. |
| **If you have unlocked nothing** | The moment does not happen. A player holding only the Oathbound never sees this screen. |

**A fraction, not a fixed level, and the reason is arithmetic.** A twelve-node tree fills at level 13 and a twenty-seven-node one at level 28 (§5.2), so any constant is wrong at one of the two scales — "level 10" would land three picks from the end of the v1 tree and barely a third of the way into the full one. Half the tree is the same beat at every content size, which means **the moment can be playtested at v1 and still be correct at eighty-one nodes.**

**One branch, not the whole tree, because of the pick budget.** A deep run is about 30 levels against 27 nodes, which is what makes §5's *"a great run nearly completes the tree"* true. A second *full* tree would put 54 nodes in front of 30 picks and turn near-completion into a third of two trees. One branch minus its Keystone is 7, so a deep run chooses among **34** — still nearly completable, now with a real question about where the last picks go.

**No Keystone from the second class.** A Keystone is the most build-defining node a class has, and a splashed one would speak louder than the primary it is bolted to. It is also frequently nonsense on its face: **Rot Bloom** (§3.2) rewrites a Veilrot relationship the Oathbound does not have.

**The choice is mandatory.** An offer you may decline is an offer most players decline, and the point of the beat is that the run visibly becomes something. There is no "stay pure" option and no compensating bonus for refusing one.

**It costs no new content, and that is the whole argument for it.** Three classes give **18 pairings** — primary × second class × branch — out of nodes that had to exist anyway. That is the cheapest variety in the game, and it is why this is worth building before a fourth class rather than after one.

---

## 6. Meta-progression

Everything permanent now does exactly one job: **unlock classes.**

| Class | Shard cost | — or — Achievement |
|---|---|---|
| **Oathbound** | Free | Default |
| **Gravecaller** | 2,000 | Kill the Choirmother (stage 10) |
| **Emberwright** | 3,500 | Reach stage 20 |

```
Shards = 10·(deepest stage) + 50·(bosses killed) + 25·(new archetype first encountered)
```

Two paths to every class — **pay, or prove.** Grinders and skilled players both get there and neither route is the "wrong" one.

**An unlock is worth more than a class.** §5.4's second-class pick draws only from what you own, so the Gravecaller arrives as three new mid-run branches for the Oathbound *and* three Oathbound branches for itself — **six new pairings for one purchase**, on top of the class. The second unlock is worth more than the first for the same reason, which is the right shape for a store and the right shape for an achievement.

**There is no permanent power progression.** No meta stats, no permanent upgrades, no account level. Every run starts from the same baseline, and the only thing that carries across runs is *which classes you can pick* and what you personally have learned. This is a deliberate stance: it keeps leaderboards meaningful, keeps difficulty tuning tractable against a fixed baseline, and means a returning player is never behind.

If retention data later demands a permanent layer, add it as **cosmetics and classes**, not power.

---

## 7. Scope

Merging cards into the tree and cutting Anchors makes this **smaller** than v0.1, not larger.

| | v0.1 (meta trees + cards) | v0.2 (in-run trees) |
|---|---|---|
| Classes in V1 | 2 | **3** |
| Per class | 33 tree nodes + ~10 cards | **27 tree nodes** |
| Shared card pool | 24 | — *(deleted)* |
| Anchors | Yes | **Cut** |
| Meta systems | XP, levels, skill points, respec, meta tree UI | **Class unlocks only** |
| Total content units | ~110 | **~81** |

**v0.3 adds no content units.** §5.4's second class is a rule over nodes that already exist: three classes make **18 pairings** where there were three, and the count above stays at ~81. The cost is one screen (**M5-07a-ii**) and a wider draw (**M5-07a-i**).

### 7.1 What was built

The Oathbound first and completely, then the Gravecaller, the Emberwright and the Ranger. The current build ships **the Ranger alone** in class select; the other three are switched off, not removed.

---

## 8. Open questions

1. ~~**Do Wights count against the enemy concurrency cap?**~~ **Answered: no.** Wights live in `MinionSystem`, a separate pool capped at 8, outside `EnemyRegistry`. A device-tiered compensating buff waits until device tiers exist.
2. ~~**Does levelling pause the game?**~~ **Answered (2026-09-29):** a level banks a point and play goes on; the tree screen pauses (§5.1).
3. **Should the level-up offer guarantee variety?** e.g. never 3 nodes from the same branch, or always ≥1 Active if you own fewer than 2. Probably yes, with soft weighting — pure random produces genuinely dead offers, and a dead offer at level 12 costs a run.
4. **Is Overflow (§5.2) enough for very deep runs?** A player at level 45 with a full tree is gaining +2%/level against quadratic enemy scaling. That's intentionally a losing race, but it needs playtesting to confirm it feels like a heroic last stand rather than a slow suffocation.
5. **Does the Archon mirror the class?** ([GameDesign.md §9.2](GameDesign.md)) The stage-20 boss reflects your Pacts. Against a Gravecaller it should raise *your own Wights* against you. Excellent fight, three times the boss scripting.

---

# Soulvail — Characters and Skills

**Version:** 0.2 — *in-run skill trees*
**Date:** 2026-09-06
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
| HP / Speed | 140 / 5.4 | 80 / 5.6 | 70 / 6.2 |
| Difficulty | Easy | Medium | Hard |
| Veilrot | **Resists** it | **Thrives** on it | **Spends** it |
| Fantasy | Walk into the swarm. Make it regret touching you. | You don't fight. The dead do. | Enormous, slow, unforgiving. |

The Oathbound starts unlocked because tanky and forgiving is the right teaching class — a new player learning a one-thumb game should not also be learning to survive at 70 HP.

### 3.1 Oathbound — *paladin*

| | |
|---|---|
| **Weapon** | **Censer** — 8m forward cone, 18 dmg, 3.0/s, hits everything in the arc |
| **Movement** | **Charge** — 10m, damages and knocks back everything hit, full i-frames |
| **Signature** | **Aegis** — a 30-point shield regenerating after 4s without damage. The only regeneration in the game. |
| **Veilrot** | Gains **−40%** from Pact nodes, Cleanses at half price — but Pact *effects* are 25% weaker for him |
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
| **Weapon** | **Bone Bolt** — 7 dmg, 4.0/s. Deliberately weak; you are not the damage. |
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

**Watch item:** Wights must be unmistakable from enemies at phone scale. Cyan-tinted, per the colour language. If players can't tell their army from the swarm, the class fails.

### 3.3 Emberwright — *wizard*

| | |
|---|---|
| **Weapon** | **Cinder Orb** — 30 dmg, 1.5/s, slow (25 m/s), 3m AoE detonation |
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
  T1  ┌─◇─┐               ┌─◇─┐               ┌─◇─┐
  T2  ├─◇─┤               ├─◇─┤               ├─◇─┤
  T3  ├─◇─┤               ├─◇─┤               ├─◇─┤
  T4  ├─◇─┤               ├─◇─┤               ├─◇─┤       ◇ = node
  T5  ├─◇─┤               ├─◇─┤               ├─◇─┤       ★ = keystone
  T6  ├─◇─┤               ├─◇─┤               ├─◇─┤
  T7  ├─◇─┤               ├─◇─┤               ├─◇─┤
  T8  └─★─┘               └─★─┘               └─★─┘
```

| Rule | Value |
|---|---|
| Branches | 3 per class |
| Nodes per branch | 8 (7 + 1 Keystone) |
| **Total nodes** | **27** |
| Cost | 1 level = 1 node. No point economy, no partial saving. |
| Branch gating | Tier *N* requires *N−1* nodes already taken in that branch |
| Keystone | Requires all 7 preceding nodes in its branch |

**A deep run reaches roughly level 30**, so a great run *nearly* completes the tree and a typical run gets maybe half. Reaching a Keystone requires committing 8 of your ~30 picks to one branch — expensive enough to be a real decision, reachable enough that it happens most runs.

### 5.1 The level-up screen

Levelling pauses the game and offers **3 nodes drawn at random from everything currently available** (prerequisites met, not yet taken). Tap to take.

Random-from-available rather than free-pick-anything, because:

- It's fast — the pacing budget for a mid-combat interruption on a phone is a couple of seconds, not a planning session
- It preserves run-to-run variety, which is the entire point of a roguelite
- It makes Upgrade nodes exciting rather than automatic

But **a "View Tree" toggle on the level-up screen** shows the full tree with your path highlighted, for players who want to plan. Opt-in, so it never slows down anyone who doesn't care. The tree is also viewable any time from pause.

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
| 10 | ~13 | 45 s |
| 20 | ~22 | 65 s |
| 35 | ~30 | 90 s |

Past the point where the tree is full, further levels grant **Overflow**: +2% damage and +2% max HP each, forever. Small, uncapped, and just enough that levelling never stops meaning something in an endless mode.

### 5.3 The balance rule that keeps this honest

> **The tree must never outrun the threat budget.**

Enemy scaling is quadratic ([GameDesign.md §12.1](GameDesign.md)); tree power must stay roughly linear so the curves cross and the run ends. If a full tree makes a player unkillable, the mode has no failure state and the entire difficulty design is decorative.

Tune so that a **well-built level-30 character dies somewhere in stages 35–50**, matching the death horizon in §12.5.

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

### 7.1 Build order — one class, all the way through

**We build the Oathbound first and completely** — movement, targeting, attack, Charge, Aegis, its full 27-node tree — before starting a second class.

Why the Oathbound:

- It's the **free starter**, so it ships no matter what the schedule does
- **140 HP and a regenerating shield** make it forgiving to playtest with; you can spend an hour feeling out movement without dying every 20 seconds
- A cone attack is the **cheapest thing to get on screen** for M0 — no projectile pooling required on day one

The tradeoff worth naming: a short-range cone is the **least representative weapon in the roster**, so there's a real risk of under-building the systems the other classes need. Two mitigations, both in [CoreCombat.md](CoreCombat.md):

1. The cone still drives the **full targeting system** for facing, so scoring, hysteresis, tap-to-focus, and vulnerability awareness all get properly exercised.
2. The **projectile-leading seam** gets built at M2 even though nothing uses it, because the Emberwright's 25 m/s orb is unusable without it and discovering that at M6 is a rewrite.

### 7.2 Then three

**V1 still targets three classes.** Three is what makes "choose your character" a real proposition — with two, the select screen is a coin flip.

**Designated cut line: the Emberwright.** If the schedule slips, ship two and add the wizard post-launch. Don't ship one; a class-based game with one class is a different game, and the class system is the retention hook.

The expensive part remains the **balance pass per class**, which does not parallelise. Budget for it explicitly.

---

## 8. Open questions

1. **Do Wights count against the enemy concurrency cap?** ([GameDesign.md §11.1](GameDesign.md)) Rendering says yes, fairness says no. Leaning: separate pool with a tighter cap, and low-tier devices get fewer Wights *plus* a compensating minion damage buff, so difficulty stays device-independent.
2. **Does levelling pause the game?** Assumed yes. Vampire Survivors pauses; Survivor.io pauses. The alternative — choosing while enemies close in — sounds tense and mostly produces mis-taps on a phone.
3. **Should the level-up offer guarantee variety?** e.g. never 3 nodes from the same branch, or always ≥1 Active if you own fewer than 2. Probably yes, with soft weighting — pure random produces genuinely dead offers, and a dead offer at level 12 costs a run.
4. **Is Overflow (§5.2) enough for very deep runs?** A player at level 45 with a full tree is gaining +2%/level against quadratic enemy scaling. That's intentionally a losing race, but it needs playtesting to confirm it feels like a heroic last stand rather than a slow suffocation.
5. **Does the Archon mirror the class?** ([GameDesign.md §9.2](GameDesign.md)) The stage-20 boss reflects your Pacts. Against a Gravecaller it should raise *your own Wights* against you. Excellent fight, three times the boss scripting.

---

## 9. Milestone impact

| # | Change |
|---|---|
| **M0** | Unchanged. One capsule, one stick, one weapon. **Do not let classes delay the feel test.** |
| **M1** | Weapon, movement skill, and passive must be **data-driven from the start** — `CharacterDefinition` assets, not a switch on an enum. Cheap now, brutal at M6. |
| **M2** | *(changed)* **Levelling and the tree**, not cards. XP, level-up screen, ~12 nodes for one class, auto-cast, cooldowns. This is now the core progression milestone and it comes early. |
| **M3** | First boss. |
| **M4** | **Second class playable** — Gravecaller with Bone Bolt, Shroudstep, Wights, and its own tree. Proves the class abstraction survives a real second case. |
| **M5** | Sanctum shop, Pact nodes, manual pinning, View Tree UI, third class. |
| **M6** | Content pass across all three trees (81 nodes), Elites, 2nd boss, both biomes with real art. |
| **M7** | Balance pass **per class** against the death horizon. Budget for three. |

**M4 is the load-bearing milestone.** Building the second class *before* the systems around it are finished means the class abstraction gets tested by a real second case while it's still cheap to change — rather than discovering at M6 that everything quietly assumed one class.

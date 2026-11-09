# Soulvail — Characters and Skills

**Version:** 0.3 — *in-run skill trees, and the second class*
**Date:** 2026-09-13
**Companion to:** [GameDesign.md](GameDesign.md) — read §5 (controls), §10 (Veilrot), and §12 (difficulty) first.

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
| **Weapon** | **Censer** — 3 m forward sweep (120°), 18 dmg, 3.0/s, hits everything in the arc *(was an 8 m, 60° cone until the 2026-11-07 melee test; CC §4.1)* |
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

A path is **a handful of skills, most of them actives, each with 3–5 ranks**, and no "ultimate": its last skill is simply the strongest, unlocking at Lv 20. Skills are level-gated. A path holds **exactly 20 ranks for its 20 points**, so level 30 buys everything (LV-02d). Choosing a path costs no point; the choice opens at level 11 whether or not the Ranger tree is full.

Built one at a time: the **Marksman** and the **Skirmisher** are built; the **Echo Archer** is next (EA specs) and last.

**Marksman — stand still.** (Owner, 2026-10-31.)

| Lv | Skill | Kind | Ranks | Effect |
|---|---|---|---|---|
| 11 | **Steady Aim** | Passive | 4 | Each arrow loosed standing still adds a damage stack (max 5); moving or rolling clears them. Rank 2+: the first arrow after stopping crits ×2. Rank 4: −20% damage taken at 5 stacks. |
| 11 | **Rain of Arrows** | Active | 4 | Arrows fall on a 3 m circle for 2 s at the densest group. Ranks: damage, cooldown; rank 3: wider and slows. |
| 14 | **Binding Shot** | Active | 3 | Chains the target to an enemy, stone, tree or wall within 4 m behind it: both stunned 2–3 s. Nothing behind: 0.75 s. Bosses: half. |
| 14 | **Eagle Eye** | Passive | 3 | +1/2/3 m range; +10/20/30% damage to targets farther than 6 m. |
| 17 | **Hunter's Snare** | Active | 3 | A trap at your feet when an enemy closes in; the first to step on it is rooted and hurt. |
| 20 | **Killshot** | Active | 3 | Kneel and draw 0.7 s, then one arrow across the arena down the most crowded line, losing damage per enemy; non-bosses under 15/20/25% HP die outright. The line is 3 m wide and pierces frontal guards, not a boss's phase shield; the Ranger is immune while drawing. |

Every active auto-casts on its own trigger (densest group · nearest threat · an enemy closing in · the longest line), and no two auto-casts fire within 0.5 s of each other. Each skill has its own effect and animation; a chosen path changes the hero's look.

**Skirmisher — never stop.** (Owner, 2026-11-05: Caltrop Roll cut, Ricochet Shot in its place; Momentum folded into Hit and Run, Untouchable into Second Wind, Storm Run made the Lv-20 active. 2026-11-06: Running Shot out-fires standing at max rank; Whirlwind and Storm Run cast on one enemy.)

| Lv | Skill | Kind | Ranks | Effect |
|---|---|---|---|---|
| 11 | **Running Shot** | Passive | 4 | The Ranger fires while moving, at 50/75/100/125% of the standing rate: at rank 4 running out-fires standing. Standing still fires at the full rate. |
| 11 | **Ricochet Shot** | Active | 4 | One arrow that bounces to the nearest enemy it has not hit within 5 m: 3 hits, 1× weapon each. Ranks: 4 hits; +25% damage; 5 hits and cooldown 8 → 6 s. |
| 14 | **Hit and Run** | Passive | 3 | Every arrow that hits gives +15/20/25% move speed for 1.5 s (refreshes). Rank 2+: **Momentum** — +2/3% damage per metre moved in the last 2 s, counting up to 8 m. |
| 14 | **Second Wind** | Passive | 3 | Roll cooldown −0.5/−1.0 s. Rank 2+: the first hit within 1 s after a roll ends misses. Rank 3: two roll charges. |
| 17 | **Whirlwind** | Active | 3 | A spin on the run: a ring of 12 arrows outward, twice 0.3 s apart, 0.6× weapon each. Ranks: 16 arrows a ring; a third ring and cooldown 10 → 8 s. |
| 20 | **Storm Run** | Active | 3 | For 6 s every roll ends in a Volley (one arrow at each enemy in range) and the roll cooldown is halved. Ranks: 8 s; Volley arrows +30% damage. |

Triggers: Ricochet — at least 3 enemies in range · Whirlwind — at least 1 within 6 m · Storm Run — at least 1 in range, cooldown 30 s. The Skirmisher's tint is a pale silver-cyan, apart from the Marksman's trim and from every enemy's lime.

**Echo Archer — be in many places.** (Owner, 2026-11-06: the 8-skill draft reshaped to 20 ranks — Umbral Link, Shared Volley and Twin Shadow cut, Fading Burst a skill of its own; Shadow Swap cut for Crossfire, because a teleport on a phone loses the hero and an auto-cast must never move the player.)

A **shade** is a see-through copy of the Ranger in smoky deep cyan. It stands where it appears, nothing can hit or target it, and it fades after its seconds. Up to 8 stand at once; a ninth replaces the oldest, which leaves without bursting. A shade that shoots looses the Ranger's arrow at the nearest enemy in weapon range of where it stands, at the Ranger's standing rate, for its own share of weapon damage; its kills pay as the Ranger's.

| Lv | Skill | Kind | Ranks | Effect |
|---|---|---|---|---|
| 11 | **Echo Arrow** | Passive | 4 | Every 4th basic arrow is loosed again 0.15 s later at the same target, for 60% damage. Ranks: every 3rd; 80%; every 2nd. |
| 11 | **Mirror Image** | Active | 4 | A shade beside you for 6 s, shooting at 50% damage. Ranks: 2 shades; 8 s; 3 shades and cooldown 14 → 11 s. |
| 14 | **Decoy Roll** | Passive | 3 | A roll leaves a shade where it began, for 2 s, at most once every 8 s. It does not shoot; every enemy but a boss walks at it. Ranks: 3 s and every 6 s; every 5 s. |
| 14 | **Fading Burst** | Passive | 3 | A shade that fades bursts: every enemy within 3 m takes 1.5/2/2.5× weapon damage. Rank 3: the burst slows by 40% for 2 s. |
| 17 | **Crossfire** | Active | 3 | Two shades appear at your sides, and every standing shade looses one arrow at your target, 1.5× weapon each; its own two fade after 1 s. Ranks: 3 shades; 2× and cooldown 12 → 9 s. |
| 20 | **Hall of Mirrors** | Active | 3 | A ring of 4 shades 3 m around you for 5 s, each shooting at 70% damage. Ranks: 6 shades; 7 s and 100% damage. |

Triggers: Mirror Image and Crossfire — at least 1 enemy in range · Hall of Mirrors — at least 1 in range, cooldown 30 s. Crossfire's arrows are ordinary arrows: the first enemy in the way takes one, and nothing pierces. Shades, shade arrows and echo arrows are smoky deep cyan — the player's colour, darker than the Marksman's trim and the Skirmisher's silver-cyan; violet stays Veilrot's (GD §16.4).

**The tree screen** shows one icon per skill with rank pips (●●○), not a node per rank: Ranger row at the bottom, the chosen path's level rows above, locked rows marked with their level. Before level 11 the path area shows the cards (tap = greyed preview); after the choice the others disappear.

**The battle HUD** (owner, 2026-10-31; GD §5.2): the roll button sits in from the bottom-right corner; every owned active is a circle in an arc above and left of it — cooldown sweep, cyan ready glow, an "A" badge while auto-cast is on, a padlock and level while locked. Tap casts now; long-press toggles auto-cast. Passives are small untappable icons in the corner below and right of the roll. **Buffs and debuffs** — on the player beside the HP bar, with a timer or stack count; stun, root and slow also as an icon over an enemy's bar.

### 3.6 Beastmaster — *axe-thrower and hound* (the second character; designed, not built)

*Owner, 2026-11-07. Melee is on hold: the Oathbound playtest showed melee needs a stagger/poise system, and he stays in class select as a test with his 3 m, 120° Censer. The second character is ranged. **Each campaign place rewards a character themed on it**; the Beastmaster is the Jungle's. Unlock: beat the Apex (Jungle stage 30), or buy him in the store. The price is set when the store is built, and no power is ever for sale.*

| | |
|---|---|
| **Body** | KayKit **Barbarian** on `Rig_Medium`; the axes are separate props. 100 HP, no Aegis, 3.1 m/s |
| **Weapon** | **Returning axes** *(owner, 2026-11-08)*. He carries **two axes, never more**, and throws **both at once** at the nearest enemy within 8 m, fanned so each passes 0.25 m beside it (at most 8° either side), so both reach it *(BT-09a)*. Each flies out to the full 8 m whatever the target's distance, turns, and spins back to his hand, homing on wherever he is now. A rock stops the out pass: the axe turns where it hit; the way back passes cover. 6 dmg per pass; each pass hits an enemy once, and at most 3 enemies. **A throw every 1.2 s, and only with both axes in hand.** Attack speed shortens the interval and speeds the flight by the same factor, so the axes are always back in time. **He stands to throw**, as the Ranger does; axes in flight still come home while he runs |
| **Signature** | **Mark.** Every axe hit marks the enemy for 4 s (refreshed by each hit). Every animal goes for the enemy his last throw aimed at while it is marked, then the nearest marked, then the enemy nearest him, anywhere in the arena |
| **Pet** | **The Hound** (§3.6.2), a real animal with real stats. It can fall, and it can be rescued |
| **Movement** | **Wild Swap** on Space: he and the hound leap past each other and trade places (§3.6.3), 6 s cooldown, its own animation |
| **Active** | **Pounce**, earned from the tree; the hound's leap, cast from his arc like any active |
| **Tree** | 3 branches × 3 nodes, free-pick (§5.1), like the Ranger's; three paths from Lv 11 (§3.7) |

**Sized against the Ranger.** Against one enemy standing on the axe's line both passes land, so a throw does 2 axes × 2 passes × 6 = 24 every 1.2 s ≈ **20 dps**. The hound adds **7 dps**: **27** in all, against the Ranger's 13 × 2.2 = **28.6** standing. A Frog (36 HP) takes 2 throws. The axes hit up to three enemies a pass and the bites hit one, so a crowd adds only on the axe's line. The Oathbound's lesson is that area damage meeting the Jungle's crowds is what breaks the curve, and nothing in the base kit is an area. **The hound is about a quarter of his damage**: it helps and never carries, and losing it costs a quarter, not half.

#### 3.6.1 Pet survival — the problem, and the ruling

A pet that fights in melee next to a crowd, with no thumb of its own, dies fast. **The fix is not to make it unkillable. It is to make every death visible, slow, caused by something the player saw, and undoable.**

| Option | Verdict | Why |
|---|---|---|
| **Enemies prefer the player** | **Yes, the core rule** | An enemy hunts you. It turns on the hound only if the hound bit it in the last 2 s **and** the hound is nearer than you. **At most 2 enemies hunt the hound at once**; a third stays on you. **Bosses never turn on it**, and boss adds follow the rule. Only melee kinds turn on it (chasers and lungers: Frog, Rootling, Bullfrog, the panthers); ranged and bursting enemies never do |
| **What can hurt it** | **Only what turned on it** | An attack aimed at the hound can hit only the hound, and one aimed at you only you. Projectiles, hazard zones and red-orange telegraphs aimed at you pass the hound by. A death always has a culprit standing next to the body |
| **HP scales with level** | **Yes** | The player's own curve: +3% HP and +2% damage a level, healed like the player's. **Thick Hide** counts for both |
| **Retreat to heel** | **Yes** | Below 35% HP it breaks off and runs to your side. It is faster than anything in the Jungle (4.0 m/s against the panther's 2.4), and enemies hunting it lose it once it stops biting. At heel it regenerates 6% max HP a second and goes back in at 80%. Between waves it is always at heel |
| **Downed → revive timer** | **No** (kept as the tuning lever) | A clock that brings it back alone hides the cost, and the owner's ruling is that pets can die |
| **Falls, waits for a rescue** | **Yes** | At 0 HP it falls where it stood, and the body stays with a cyan ring. Stand within 2.5 m for 2 s (progress is kept if you step away) and it gets up at 50%. Enemies ignore the body. **Unrescued, it stays down for the rest of the stage**; every stage starts with it at full HP |
| **Rescue through the swap** | **Yes** | Swapping with a hunted hound pulls it out of the fight and drops every enemy's hold on it. Swapping to a fallen hound leaps you onto the body with the rescue half done |
| **A heal / mend active** | **Not in the base kit** | Another button for a job heel already does. Two paths carry a heal (Howl, Shared Blood) |
| **Tree nodes toughen it** | **Yes** | Thick Hide (Lv 2–10); Thick Pelts (Packleader); Shared Blood and Last Stand (Apex Bond) |
| **It blocks enemies** | **No** | Enemies walk through it. It cannot be used as a wall, and it never gets pinned |

**Why this is fair.** The hound gives three warnings before it is lost: its cyan bar, the run to heel, and a fall that can be undone. Only what it was fighting can kill it, never more than two at once, never a boss. And the only way to lose it for the stage is to leave the body. With a Frog's 8 dmg it takes about six bites to send it to heel, and with a Panther's 14 about four lunges. **Tuning lever** if the fall plays too harsh: an unrescued hound gets up on its own after 20 s at 25%.

#### 3.6.2 The animals

**The Hound** is the base pet. It is a big jungle hound, chosen against the Jungle's panthers on every axis GD §16.4 and P1 care about:

| | Enemy panther | The Hound |
|---|---|---|
| Silhouette | Long and low, head down, an arrow along its dash | **Upright and tall-legged**: head up, pricked ears, a raised curled tail |
| Coat | Charcoal | **Sandy bone with darker brindle** (desaturated, off the green floor) |
| Accents | Lime | **Cyan** `#22D3EE` leather collar with a lit tag, and cyan-lacquered bone plates on shoulders and back |
| Moves | Prowl, crouch, lunge | Trot, snap-and-spring-back, pounce, heel, fall |

| Stat | Value |
|---|---|
| HP | 70 (+3%/level) |
| Speed | 4.0 m/s |
| Bite | 7 dmg every 1.0 s, 1.4 m reach (+2%/level) |
| Reach | Any enemy in the arena *(owner, 2026-11-08; the 8 m leash is gone)* |
| Target order | Your last throw's target while marked → the nearest marked enemy → the enemy nearest you; rechosen every 0.5 s, and at once when your throw target changes |
| Following | Out of a fight each animal holds a slot: the Hound at your right, pack hounds at your left and behind, the boar behind-left. Animals keep 1.1 m apart; they ease into the slot, ignore a nudge under 0.5 m, and turn at a capped rate, so they never read as steered by the stick |

There is no red-orange, violet, gold or lime anywhere on it. Its bar is a small cyan one over its body, always shown: its health is the player's to manage (GD §16.2). The Barbarian's own atlas gets the same check: any cell reading gold or red-orange is repainted bone or cyan. **What each path adds:** Packleader brings pack hounds (smaller copies of the same body), a **hawk** and a spitting **boar**: two new animals. Axe Master adds no animal. Apex Bond grows the one hound: armour and size, on the same body. The art cost is one hound and, later, a hawk and a boar.

#### 3.6.3 Wild Swap

*The draft's "Spirit Swap", renamed: nothing in this class is a spirit.*

Space: he and the hound **leap past each other** in 0.25 s and land in each other's places. Both stay visible, and a cyan streak joins the two points for 0.3 s. It is a leap, not a cut, because the Echo Archer ruled that a teleport on a phone loses the hero. Both are immune during the leap and for 0.2 s after. The leap arcs, so nothing stops it — no cover, no enemy, no animal — and each lands exactly on the other's spot. Every enemy hunting the hound loses it. It hurts and shoves no one it passes *(BT-09a)*. Cooldown **6 s**. With the hound fallen, he leaps onto the body and the rescue starts half done. Clips: `Jump_Full_Short` for him, a lunge for the hound.

**Bond Barrier** *(BT-08)*: every swap shields him for **15% max HP**, standing until 3 s after landing — a pale bubble on his body and a granted-shield segment on the HP bar. A new swap replaces what is left rather than stacking. It absorbs like any granted shield, bosses included; a hit spent past it lands the difference. A hit that empties it pops the bubble; running out of time does not.

#### 3.6.4 Tree — levels 2 to 10

Free-pick, tier *N* needs *N−1* in its branch (§5). The Lv-10 tree costs 9 points, exactly as the Ranger's does.

| Tier | **Axe** | **Beast** | **Wild** |
|---|---|---|---|
| 1 | **Heavy Edge** — +12% axe damage | **Sharp Claws** — +15% hound damage | **Thick Hide** — +10% max HP, for you and the hound |
| 2 | **Swift Hands** — +20% attack speed: a throw every 1.0 s, axes 20% faster *(replaced Third Axe, owner 2026-11-08)* | **Feral Bite** — every 3rd bite stuns a light enemy 0.5 s | **Lick Wounds** — the hound heals twice as fast at heel (12%/s) and goes back in at 60% instead of 80% |
| 3 | **Cleaving Return** — the return pass deals +50% and slows 25% for 1.5 s | **Pounce** — *active.* The hound leaps up to 6 m at a marked enemy: 2× bite, and knocks a light enemy down 0.8 s. Cooldown 8 s | **Quick Swap** — swap cooldown −1 s; the hound lands with a roar that slows enemies within 3 m by 30% for 2 s; the Bond Barrier rises to 25%, and when a hit breaks it every non-boss within 3 m is shoved 2 m away |

**Light** is a flag each enemy's content carries: Frog, Rootling, Puffball, Spitter, Panther and their kind. It excludes Bullfrog, Mossguard, Shaman, every elite and every boss. Pounce's trigger is a marked enemy 3–6 m from the hound.

### 3.7 Beastmaster paths — levels 11 to 30

The Ranger's rules (§3.5) word for word: one path from Lv 11, locked for the run, six skills and **exactly 20 ranks**, the strongest unlocking at Lv 20. The level-2–10 tree stays. Every active auto-casts on its own trigger, and no two auto-casts fire within 0.5 s of each other; the hound's skills count as his. **Weapon** below means one axe pass (6 dmg, before levels and nodes). Every animal follows §3.6.1: it heels, falls, waits to be rescued, and turns enemies only by biting them.

**Packleader — run with the pack.** More animals.

| Lv | Skill | Kind | Ranks | Effect |
|---|---|---|---|---|
| 11 | **Pack Hounds** | Passive | 4 | A pack hound runs with you, at 50% of the Hound's HP and damage. Ranks: 65%; a second pack hound; 80%. Swap trades with the Hound alone |
| 11 | **Howl** | Active | 4 | Every animal heals 20% max HP. A heal, not a buff: the attack-speed part moved to Frenzy. Ranks: 30%; 40%; cooldown 14 → 11 s |
| 14 | **Boar** | Passive | 3 | *(Owner, 2026-11-08; replaced Pack Tactics.)* A jungle boar joins the pack, a **ranged** animal: 60 HP (+3%/level), 3.6 m/s, holds 5 m from its target and spits a toxic-green glob (`#16A34A`, never the enemies' lime, with a thin cyan trail) every 1.5 s at 12 m/s, 7 m range, 6 dmg (+2%/level). Cover stops a glob, so it spits only with a clear line. A melee enemy its glob hits may turn on it by the hunt rule. Ranks: +30% spit damage; the glob slows 25% for 1.5 s |
| 14 | **Thick Pelts** | Passive | 3 | Animals take −10/−20/−30% damage. Rank 3: a rescue takes 1 s |
| 17 | **Hawk** | Active | 3 | A jungle hawk circles above you for 8 s and dives at marked enemies every 1.5 s: 2× weapon, and a 0.4 s stun on light ones. It flies, so nothing hits it. After each strike it climbs back to its circle over 0.4 s. Ranks: 10 s; a dive every 1.0 s and cooldown 16 → 12 s |
| 20 | **Frenzy** | Active | 3 | *(Owner, 2026-11-08; replaced Stampede.)* Every animal gets +30% attack speed, +30% move speed and +30% damage for 8 s, the buff icon over its bar (GD §16.5). Ranks: 10 s; +40% and cooldown 30 → 24 s |

Triggers: Howl — an animal under 50% HP · Hawk — at least 1 marked enemy · Frenzy — at least 1 in range, cooldown 30 s. Bites stay single-target, so even three hounds add nothing to a crowd that one hound would not: the pack grows the hound's share, not the area. Look: cyan war-paint stripes on him and on every hound.

**Axe Master — the axe.** No new animal; the Hound stays as base.

| Lv | Skill | Kind | Ranks | Effect |
|---|---|---|---|---|
| 11 | **Long Arc** | Passive | 4 | +1 m axe range. Rank 2: the axe hovers 0.3 s at its turn, spinning, and hits everything within 1.5 m once more. Rank 3: +2 m. Rank 4: a 0.5 s hover that hits twice |
| 11 | **Hurl** | Active | 4 | A great axe at the densest group: it flies 10 m, spins 1.5 s on a 2 m circle hitting 4 times for 1× weapon, then returns, hitting on the way. Ranks: +25% damage; 2.5 m circle; cooldown 10 → 8 s |
| 14 | **Splitter** | Passive | 3 | Every 5th throw is a heavy axe: 2× damage, and it knocks light enemies back 1 m. Ranks: every 4th; 2.5×, every 3rd |
| 14 | **Battle Rhythm** | Passive | 3 | Each axe kill gives +5% throw rate for 3 s (refreshes), stacking to 3/4/5 |
| 17 | **Whirling Axes** | Active | 3 | He spins for 1.5 s at full speed, axes in both hands: 3 hits of 0.8× weapon on every enemy within 2 m, and light ones are pushed out to 2.5 m. Throws pause while he spins. Ranks: 4 hits; cooldown 12 → 9 s |
| 20 | **Axe Storm** | Active | 3 | For 6 s, +100% attack speed: throws twice as often and the axes fly twice as fast (still two axes, §3.6). Ranks: 8 s; axes +30% damage |

Triggers: Hurl — at least 3 enemies in range · Whirling Axes — at least 2 within 2.5 m · Axe Storm — at least 1 in range, cooldown 30 s. Look: bright cyan axe edges and a cyan spin trail.

**Apex Bond — one great beast.** The callback to the fight that earned him.

| Lv | Skill | Kind | Ranks | Effect |
|---|---|---|---|---|
| 11 | **Deep Bond** | Passive | 4 | +15% Hound HP and +10% Hound damage a rank. Rank 4: it grows a size (×1.2) and its bite reaches 1.8 m |
| 11 | **Maul** | Active | 4 | The Hound rears on its target: 3 bites in 0.6 s, 1.5× bite each. Ranks: 4 bites; the last stuns 1 s (bosses half); cooldown 10 → 8 s |
| 14 | **Shared Blood** | Passive | 3 | The Hound heals 10/15/20% of the damage it deals. Rank 3: you heal a quarter as much |
| 14 | **Challenge** | Active | 3 | The Hound roars: every non-boss enemy within 4 m of it turns on it for 3 s, past the two-hunter cap, and it takes −40% damage for those 3 s. Ranks: 5 m; 4 s and cooldown 14 → 11 s |
| 17 | **Last Stand** | Passive | 3 | Once a stage, a blow that would fell the Hound leaves it at 1 HP, immune for 1.5 s, and it runs to heel. Ranks: 2.5 s; twice a stage |
| 20 | **Apex Form** | Active | 3 | For 8 s the Hound grows to the Apex's size (×1.8): +50% max HP (the gain is healed), its bites hit every enemy in a 2 m cone, and it cannot be stunned or knocked down. Ranks: 10 s; +30% damage and an opening pounce that knocks light enemies within 3 m down |

Triggers: Maul — the Hound is biting a marked enemy · Challenge — at least 2 enemies within 3 m of you · Apex Form — at least 3 enemies within 8 m of the Hound, or a boss in range, cooldown 30 s. Look: the Hound's bone plates grow and glow cyan, and the Apex's broken fang hangs on its collar. It is a hound grown huge, never a panther and never lime.

#### 3.7.1 What the code already has (checked 2026-11-07)

- **Minions.** `MinionSystem` (Core/Ai) holds Gravecaller's Wights: a fixed 8-slot array (`MaxConcurrent = 8`) and a live `Cap` stat. Each Wight walks at the nearest enemy and hits it, retargets every 0.5 s and expires on a clock. Its kills go through `EnemySystem.ApplyDamage(..., player)`, so **they pay XP as the player's**. The Hound's do the same. Boss adds still pay 0 (they are `SpawnSummon`s), whoever kills them. The player's targeter and the stage-complete check cannot see a Wight. `MinionSystem.ApplyDamage` exists but **nothing calls it**, by ruling ("enemies do not fight back"). The Hound needs a hunt/heel/fall/rescue state machine, no clock, a leash and a mark priority. That is a `PetSystem` of its own built on the same patterns, not a stretched `MinionSystem`, whose remarks rule against one class serving two shapes. `MinionStats` addresses MaxHp/MoveSpeed/ContactDamage, and its remarks name the missing piece: no `StatTarget` reaches "my pet", which Sharp Claws and Thick Hide need.
- **Enemies have no target id.** `LureSystem` only redirects where an enemy *walks*; `EnemyBlackboard` carries no `TargetId`, and lured enemies still face the real player (the Mossguard fix). **An enemy attack landing on the Hound is the largest new piece of this design**, and it gets its own task.
- **Panther.** `Art/Enemies/Panther.fbx` is a generated body on its own `Rig_Panther` skeleton (Generic), with clips Crouch, Death, Hit, Idle, Lunge, Prowl. The Hound is a new generated quadruped. Skinning it to `Rig_Panther` to inherit the clips is worth trying, but whether the clips key bone translations, which would break on longer legs, is to be checked in the art task.
- **Barbarian and axes.** `ThirdParty/KayKit/Adventurers/Characters/Barbarian.fbx` (Generic) and `Prefabs/Characters/Barbarian.prefab` exist, already driven by Rig_Medium clips (`AC_Barbarian_Showcase`). Props include `axe_1handed.fbx` and `axe_2handed.fbx`. Rig_Medium has `Throw`, `Jump_Full_Short` (swap) and `Interact` / `PickUp` (rescue kneel).
- **Blink.** `MovementSkillKind.Blink` is a 0.05 s, 10 m dash along the stick on `ChargeSkill`'s clock, buffer, i-frames and HUD button, with a fire-pool payload. Wild Swap reuses all of that. Its **direction and distance** come from the Hound's position, not the stick, and it moves a second body, so it is a new kind appended to the enum (`None` is last but ordinals are stored, so append after it). Whether the dash is stopped by cover is checked in its task; the swap must not be.

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

Meta-progression is **class unlocks only**: no respec, no Anchors, no separate card pool (all cut with v0.1). V1 scopes three classes at 27 tree nodes each, ~81 content units.

The Oathbound first and completely, then the Gravecaller, the Emberwright and the Ranger. The current build ships **the Ranger alone** in class select; the other three are switched off, not removed.

---

## 8. Open questions

1. ~~**Do Wights count against the enemy concurrency cap?**~~ **Answered: no.** Wights live in `MinionSystem`, a separate pool capped at 8, outside `EnemyRegistry`. A device-tiered compensating buff waits until device tiers exist.
2. ~~**Does levelling pause the game?**~~ **Answered (2026-09-29):** a level banks a point and play goes on; the tree screen pauses (§5.1).
3. **Should the level-up offer guarantee variety?** e.g. never 3 nodes from the same branch, or always ≥1 Active if you own fewer than 2. Probably yes, with soft weighting — pure random produces genuinely dead offers, and a dead offer at level 12 costs a run.
4. **Is Overflow (§5.2) enough for very deep runs?** A player at level 45 with a full tree is gaining +2%/level against quadratic enemy scaling. That's intentionally a losing race, but it needs playtesting to confirm it feels like a heroic last stand rather than a slow suffocation.
5. **Does the Archon mirror the class?** ([GameDesign.md §9.2](GameDesign.md)) The stage-20 boss reflects your Pacts. Against a Gravecaller it should raise *your own Wights* against you. Excellent fight, three times the boss scripting.

---

# Soulvail — Game Design Document

**Version:** 0.2 (founding draft, mobile-first)
**Date:** 2026-09-06
**Engine:** Unity 6.3 (`6000.3.23f1`), Universal Render Pipeline 17.3, Input System 1.20
**Target platform:** **Android** (phone-first, landscape). PC is a possible later port, not a design constraint.
**Perspective:** Top-down twin-stick — *decided*
**Companion docs:** [Characters.md](Characters.md) — classes, skills, in-run trees · [CoreCombat.md](CoreCombat.md) — movement, targeting, attack, skill casting (build spec)
**Mode:** V1 ships one mode, **Descent** (endless). More modes later — see §4.5.
**Status:** Vision agreed. Numbers below are *starting values to be tuned*, not laws.

---

## 1. One-paragraph pitch

**Soulvail** is a fast, arena-based 3D action roguelite for Android. You pick a Vailkeeper — a paladin, a necromancer, a burning zealot — and descend through an endless, ordered sequence of stages inside a collapsing afterlife. Each stage is a sealed arena: kill everything, a gate opens, you descend. Kills level you up, and every level lets you take a skill from your class's tree — passives, auras, and actives that cast themselves while you concentrate on staying alive. Every fifth stage is a boss. Every stage is harder than the last — forever, until you die.

Your weapon fires itself; **your thumb is for surviving**. The twist is **Veilrot**: the strongest version of every skill is its corrupted one, and every corrupted pick brings the Veil closer. Push too far and it claims you mid-run — which is either how you die, or the most powerful thing that can happen to you.

Runs are short (8–20 minutes), deaths are frequent and total, and the question the game keeps asking is: *how much of yourself will you spend to see the next floor?*

---

## 2. Design pillars

Every decision below should be traceable to one of these five. If a feature serves none of them, cut it.

| # | Pillar | What it means in practice |
|---|--------|---------------------------|
| **P1** | **Readable at a glance** | On a 6-inch screen with a thumb covering the corner, the player must always know what is about to hurt them. One reserved color for danger, clear silhouettes, telegraphs before every hit. |
| **P2** | **Movement is the answer** | Aiming is automatic. Positioning, dashing, and timing are the *entire* skill expression. Enemies exist to constrain space. |
| **P3** | **Escalation you can feel** | Difficulty rises through *new behaviour and new pressure*, not bigger health bars. The player should be able to name why stage 20 is harder than stage 10. |
| **P4** | **Meaningful, risky choices** | Every Sanctum offer should be a real decision with a downside. Power that costs nothing is not interesting. |
| **P5** | **Respect the phone** | One thumb should be enough. A run survives an incoming call. A 20-minute session doesn't cook the device or drain 20% of the battery. |

### Anti-pillars (things we are explicitly not doing)

- **No bullet sponges.** HP scaling is the last lever we pull, never the first. (See §12.)
- **No inventory management.** Upgrades are automatic and permanent for the run. No equipment screens, no stat comparison UI.
- **No cheap deaths.** Every death should be traceable to a mistake the player can name. No off-screen damage, no unavoidable hits.
- **No filler length.** We do not lengthen runs to lengthen playtime.
- **No precision-dependent input.** Nothing may require an accurate tap on a moving target smaller than a thumb.

---

## 3. Fantasy and setting

### The name

The **Vail** is the veil between the living and the dead — and also the *vale*, the valley the dead fall into. Souls are supposed to pass through it. They stopped passing. Now the valley is full, the layers are compacting under their own weight, and the Vail is tearing.

### Who you are

A **Vailkeeper** — a warden whose job was to shepherd souls downward and keep the layers separate. You failed. Now you descend through the collapse, floor by floor, burning souls as ammunition to reach the bottom and find out what plugged the exit.

You are not a hero. You are a maintenance worker with a gun made of grief.

### Tone

Melancholy, not grimdark. Bone-white and ash, lit by the cyan of your own soul-fire. The enemies are not evil — they are stuck. The bosses were Vailkeepers before you, further gone. Text is sparse: item descriptions, a line from a boss when it dies, nothing more. **No cutscenes, no dialogue trees, no lore dumps** — nobody reads paragraphs on a phone.

### Biomes (the layers)

| Stages | Layer | Visual identity | Mechanical identity |
|--------|-------|-----------------|---------------------|
| 1–10 | **The Ashen Reach** | Grey dunes, broken pillars, falling ash | Open arenas, few hazards. The teaching floor. |
| 11–20 | **The Drowned Choir** | Flooded cathedral, shin-deep water, distant singing | Water slows movement; sound-based telegraphs |
| 21–30 | **The Bone Orchard** | White tree-forms, dense cover, red sky | Sightline-breaking cover; ambush enemies |
| 31–40 | **The Static Halls** | Fractured geometry, glitching light | Moving/vanishing floor, teleport enemies |
| 41+ | **The Abyss** | Void with fragments of prior layers | Biomes recombine; escalating global modifiers |

Biome changes every 10 stages. Beyond 40 the Abyss loops previous layers with stacking **Ordeals** (§13.4).

---

## 4. Core loop

Four nested loops. Each must be satisfying on its own.

### 4.1 Moment-to-moment (2–5 seconds)
> Read the threat → reposition → plant and burn → dash out of the counter-attack → repeat.

### 4.2 Stage (40–75 seconds)
> Enter sealed arena → clear 2–5 waves → arena unseals → Essence auto-collects → step through the Gate.

### 4.3 Level-up (every 25–90 seconds)
> Kills grant XP → level up → the game pauses → **take 1 of 3 skills** from your class tree → back to it.

This is the progression loop, and it fires *during* stages rather than between them. Full design in [Characters.md §5](Characters.md).

### 4.4 Run (8–20 minutes, or forever)
> Choose a class → Stage 1 → clear → **Sanctum** (shop) → Stage 2 → … → Boss every 5th stage → death.

Death is total. The tree is gone, the build is gone. Only which classes you've unlocked persists.

### 4.5 Modes

**V1 ships exactly one mode: Descent** — the endless run described above. No win condition; the score is depth reached.

More modes come later, and the only thing that matters now is that **a mode is a data object, not an assumption baked into the code.** A mode configures: the stage sequence, the difficulty curve, the win/lose conditions, which classes are legal, and any starting modifiers. Nothing in the run loop, spawn director, or class definitions may hard-code "endless" or "starts at stage 1."

Getting this boundary right at M1 costs almost nothing. Retrofitting it after three classes and a boss roster assume Descent is a rewrite.

Plausible later modes, listed only to check the abstraction holds — **not designed, not committed**: a fixed-seed Daily with a leaderboard, a Boss Rush, a finite curated campaign with an ending, a Trial that hands you a pre-built tree and tests execution rather than luck.

---

## 5. Controls — the most important section in this document

On a phone, the control scheme *is* the game design. Everything downstream depends on it.

### 5.1 The core decision: one thumb moves, the gun aims itself

**Recommended: single floating joystick + automatic firing + auto-target, with dash and ability as right-thumb buttons.**

This is the model Archero and Survivor.io use, and it dominates the genre for a reason: it eliminates the second stick entirely. Virtual twin-stick is a well-documented ergonomic problem — no tactile feedback, thumbs occlude a third of the screen, and precision aiming with a thumb on glass is genuinely bad.

Critically, **this makes our pillars stronger, not weaker.** P2 already says "movement is the answer." Removing manual aim doesn't remove skill — it concentrates *all* skill into positioning, which is exactly where we wanted it.

### 5.2 Layout (landscape)

```
┌──────────────────────────────────────────────────────────┐
│ ♥♥♥♥♥♥♥░░  100/100          STAGE 12  ▮▮▮▯▯      ✦ 340  │  ← top strip: safe from thumbs
│                                                     ▓    │
│                        ▲                            ▓    │  ← Veilrot bar (right edge)
│              ▲    ◈────────→   ▲                    ▓    │
│                  (you)                              ▓    │
│         ▲              ▲              ▲                  │
│                                                          │
│    ╭───╮                                        ╭──╮╭──╮ │
│    │ ◉ │  ← floating stick                      │⚡││✦ │ │  ← dash / ability
│    ╰───╯     (appears where you touch)          ╰──╯╰──╯ │
└──────────────────────────────────────────────────────────┘
     LEFT ZONE (45%)                          RIGHT ZONE
     movement only                     buttons + tap-to-focus
```

| Input | Gesture | Notes |
|---|---|---|
| **Move** | Touch anywhere in the left 45% and drag | *Floating* stick — origin spawns under your thumb, never fixed. Full deflection at 55px drag. |
| **Fire** | Automatic | Continuous, at the current target. No fire button. |
| **Movement skill** | Tap the fixed right-side button | 48dp minimum, corner-anchored in natural thumb arc. Radial cooldown fill on the button. |
| **Manual skills** | Tap — up to 4 optional buttons | Only appear for skills the player has switched to Manual. Everything else auto-casts. |
| **Focus target** | Tap any enemy | Locks auto-aim to it until it dies. See §5.4. |
| **Pause** | Tap top-right icon | Small target, but non-critical and out of the way |

### 5.3 Alternate control modes (all ship in V1)

| Mode | Description | For |
|---|---|---|
| **Keeper** *(default)* | 1 stick + auto-aim + 2 buttons | Everyone |
| **Precision** | Right thumb becomes an aim stick; firing follows it. Dash/ability move to double-tap and two-finger tap. | Players who want manual aim (the Soul Knight audience) |
| **Drifter** | Drag-to-move anywhere on screen, no stick region, auto-aim | One-handed / accessibility |

**Left-handed mirror** is a toggle that flips the entire layout. This is cheap to build if the HUD is anchored properly from day one and expensive to retrofit.

### 5.4 Auto-targeting — where the depth lives

Naive "shoot the nearest enemy" would destroy this design. Our two best enemies exist to make the player *choose a target*: the **Warden** (immune from the front) and the **Choir** (heals everything nearby, never attacks). Nearest-target auto-aim would blindly chew on a Warden's shield forever.

So targeting has three layers:

1. **Threat scoring, not proximity.** The auto-targeter scores candidates on distance, damage-dealing potential, whether they're currently vulnerable, and a designer-set `targetPriority` per archetype. A Choir at 15m outranks a Husk at 5m.
2. **Tap-to-focus.** Tap any enemy to lock onto it; a reticle marks it until it dies or you tap elsewhere. This restores full target-priority agency with a single thumb-tap — no precision required, since the tap resolves to the nearest enemy within a generous radius of the touch point.
3. **Vulnerability awareness.** The targeter never picks an enemy it cannot currently damage. Against a Warden it holds fire and marks the shield — telling the player, in the game's own language, *"go around."* The answer is movement, which is the whole point.

A visible reticle on the current target is **mandatory**, not polish. Auto-aim that the player doesn't trust is worse than no auto-aim.

### 5.5 Focus — the plant-and-burn rhythm

Standing still for **0.4s** begins ramping fire rate to **130%** over the following second; moving drops it instantly.

This gives the loop a rhythm — *dodge, plant, burn, dodge* — and creates a real risk/reward tension on a control scheme that otherwise has only one input. It's a softer version of Archero's stop-to-shoot: we never take the gun away, we just reward commitment.

**Flagged for M0 validation.** If it doesn't feel good with capsules, cut it. It's two lines of code either way.

### 5.6 Touch-specific requirements

| Requirement | Why |
|---|---|
| **Input buffering, 0.15s** | Touch has meaningfully higher latency than a gamepad. A dash tapped just before the cooldown ends must still fire. |
| **Generous dash i-frames** | Already in the design (§6.1) — doubly important when input is imprecise. |
| **48dp minimum touch targets** | Standard Android accessibility floor. |
| **Haptics on hit, damage taken, and dash** | Cheapest game-feel win available on mobile. Must have an off switch. |
| **Safe-area aware HUD** | `Screen.safeArea` — notches and gesture bars vary wildly across Android. |
| **No gesture conflicts** | Keep interactive elements clear of the bottom-edge system gesture strip. |

---

## 6. The player character

The player picks a **class** at the start of every run. Each has its own weapon behaviour, movement skill, signature passive, Veilrot relationship, and skill tree. The V1 roster is **Oathbound** (paladin), **Gravecaller** (necromancer), and **Emberwright** (wizard) — full detail in [Characters.md](Characters.md).

This section defines what is **true for every class**. Anything not listed here varies per class.

### 6.1 Shared constants

Per-class values (HP, speed, weapon) live in [Characters.md §3](Characters.md). These do not vary:

| Constant | Value | Notes |
|---|---|---|
| Passive regeneration | **None** | Healing is scarce and deliberate. The Oathbound's Aegis shield is the sole exception, and it's a shield, not health. |
| Movement-skill i-frames | Full duration + 0.05s | Generous on purpose — dodging must feel reliable through touch latency |
| Hit i-frames | 0.5 s | Prevents multi-hit chip death in a swarm |
| Collision | `CharacterController` | Not rigidbody. Predictable, cheap, no physics jank. |
| HP range across classes | 70–140 | Anything outside this band breaks the one-shot rule's arithmetic (§12.4) |
| Speed range across classes | 5.4–6.2 m/s | Every class must feel faster than almost every enemy |

### 6.2 Weapon rules

Weapons differ per class, but all obey the same laws:

- **Projectiles only, never hitscan.** The player must see their own DPS in flight, and travel time is what makes enemy positioning matter.
- **Automatic fire.** No fire button exists (§5.1).
- **No ammo, no reload.** Resource management is not this game's texture.
- **Pooled and hard-capped**, single shared material. On mobile, an uncapped bullet-hell is a frame-rate incident waiting to happen.

**The primary balance invariant of the whole game:**

> A basic enemy dies in **3–5 hits from any class, at any depth.**

At stage 1 an unlevelled class kills a Husk in ~3 hits. At stage 30 a well-built one should still kill a Husk in ~3–5 hits, because tree power and enemy HP scale together by design. If basic enemies start taking 10 hits, the curve has failed and no amount of content fixes it.

### 6.3 Skills

Skills come from the in-run tree, not from a pre-run loadout. Every class has:

- **One movement skill**, fixed, on a permanent button. It is class identity, not a choice — Charge is what the Oathbound *is*, as Blink is what the Emberwright is.
- **Passive skills** — always on, no input.
- **Active skills** — on cooldown, and **auto-cast by default** when their trigger condition is met.
- **A per-skill Auto/Manual toggle.** Default Auto. Set a skill to Manual and it stops auto-casting and takes one of **4 manual buttons**, cast by the player. Changeable any time, including mid-run.

Design and rationale in [Characters.md §4](Characters.md); full implementation spec in [CoreCombat.md §6](CoreCombat.md). The short version: auto-cast is the *default* because the entire skill expression of this game is positioning, and a player who never opens the menu gets a complete game with one button. Manual is opt-in depth for players who want to time things themselves.

---

## 7. Stage structure

### 7.1 Anatomy of a stage

1. **Arrival** (2s) — arena boundary seals with a visible soul-barrier, stage number appears and fades.
2. **Waves** (2–5) — enemies spawn from ground portals with a 0.8s telegraph ring. Never within 6m of the player.
3. **Clear** — barrier drops, remaining Essence auto-collects, **Gate** materializes.
4. **Sanctum** (§13) — appears after every stage. Untimed. This is the breathing room, and where the run's identity is decided.
5. **Gate** — walk through, next stage loads.

### 7.2 Arena design rules

- Roughly **36m × 36m**, bounded, flat or gently tiered. Square, so it frames well in landscape.
- **3–6 cover pillars** placed off-center. Cover blocks enemy projectiles but not pathing.
- No dead ends. The player must always have a circle-strafe route.
- Camera never collides with geometry. No ceilings, no corridors.
- **V1 uses hand-built arena prefabs from a pool** (target 8–12 per biome), chosen at random per stage. Procedural generation is a V3 conversation at the earliest.

### 7.3 Pacing and session design

Waves overlap: wave *n+1* begins spawning when wave *n* is at 25% remaining. This kills the "chase the last enemy" dead time that ruins wave shooters. If fewer than 3 enemies remain and 8 seconds pass, survivors get a waypoint arrow.

**Mobile session rules (P5):**

- **A stage is a commute unit.** 40–75s means a player can always finish the stage they're in.
- **Pause anywhere, instantly**, including mid-boss.
- **Run state persists to disk at every stage boundary.** Android kills backgrounded apps without warning; losing a 15-minute run to an incoming call is unacceptable and would be the single most rage-inducing bug we could ship.
- **Sanctums are untimed** — they're the natural "put the phone down" point, and the natural resume point.

---

## 8. Enemies

### 8.1 Archetypes

Each archetype constrains the player's space in a *different* way. If two enemies pressure the player identically, one is redundant and should be cut.

| Enemy | Role | Behaviour | Base HP | Threat Cost | Target priority |
|---|---|---|---|---|---|
| **Husk** | Swarm pressure | Beelines at player, melee. Slow, numerous. | 36 | 4 | 1 |
| **Spitter** | Punishes standing still | Stops at 14m, fires slow arcing projectile. | 28 | 7 | 3 |
| **Bloater** | Punishes clustering | Waddles at player, explodes on death or contact (3m). | 24 | 8 | 2 |
| **Lunger** | Punishes bad positioning | Telegraphs 0.9s, dashes 15m in a line. | 44 | 9 | 4 |
| **Weaver** | Attrition | On death splits into 2 smaller Weavers (2 generations). | 50 | 12 | 2 |
| **Warden** | Forces flanking | Front shield blocks all damage. Vulnerable from behind. | 90 | 14 | 3 |
| **Choir** | Priority target | Never attacks. Heals + speed-buffs allies in a 12m aura. | 60 | 16 | **8** |
| **Revenant** | Denies safe zones | Teleports behind the player every 4s, short-range burst. | 70 | 18 | 6 |

The `targetPriority` column feeds the auto-targeter (§5.4) — it's what makes the gun smart enough to be trusted, and it's a designer-tunable number per archetype.

**Design note:** the Choir and Warden are the two most important enemies in the roster. They are what force target selection and repositioning rather than "hold stick, watch numbers." They are also the two most likely to be broken by a lazy auto-aim implementation — guard them.

### 8.2 Introduction schedule

New enemies arrive one at a time, in a wave where they're the *only* new thing.

| Stage | Introduced |
|---|---|
| 1 | Husk |
| 2 | Spitter |
| 4 | Bloater |
| 6 | Lunger |
| 8 | Weaver + **first Elites** |
| 11 | Warden |
| 12 | **Affixes begin** (1 per Elite) |
| 14 | Choir |
| 17 | Revenant |
| 21+ | No new archetypes — new *combinations* and Ordeals (§13.4) |

By stage 21 the player knows the full vocabulary. Everything after is composition, density, and modifiers. **This is deliberate**: an endless game can't ship endless content, so depth must come from interaction between known parts.

### 8.3 Elites and affixes

From stage 8, part of each wave's budget may buy **Elites** — a normal enemy at **2.5× Threat Cost**, **2.2× HP**, a distinct emissive outline, and (from stage 12) one **affix**:

| Affix | Effect |
|---|---|
| **Volatile** | Leaves a damaging pool on death (4s) |
| **Warded** | Immune while any other enemy is within 8m |
| **Hasted** | +45% move speed, +30% attack rate |
| **Siphoning** | Drains 2 HP/s from the player within 10m, heals itself the same |
| **Splintered** | Fires a 6-way projectile burst on death |

From stage 30, Elites may roll **two** affixes, checked against a blacklist (Warded + Siphoning together is banned — unkillable-feeling without being interesting).

Elite HP caps at **2.2×** and never scales past it. Elites are *priority targets*, not walls.

---

## 9. Bosses

Every 5th stage. Every 20th is an **Archon** — longer, multi-phase, with a biome transition after.

### 9.1 Boss design rules (non-negotiable)

1. **Every attack telegraphs ≥0.6s** with distinct visual *and* audio cues.
2. **Every attack has a safe answer** that costs positioning, not health.
3. **Phase transitions at 66% and 33% HP**, with a brief invulnerable beat that clears adds and resets pressure.
4. **Bosses spawn adds** — a lone boss is a DPS check, and DPS checks are boring.
5. **Boss HP is a function of expected player DPS at that depth.** Target fight length **75–120s** (shorter than a PC game — this is a phone). A 4-minute boss is a bug.
6. **The arena participates.** Each boss arena has one hazard the boss can weaponize.
7. **Boss attacks must read on a 6-inch screen.** Test every telegraph at phone size before calling it done.

### 9.2 V1 boss roster

| Stage | Boss | Hook |
|---|---|---|
| 5 | **The Warden of Ash** | A Warden that grew. Shield-slam shockwaves, ground fissures, summons Husks. *Teaches: attack from behind, keep moving.* |
| 10 | **Choirmother** | Immobile, ringed by rotating Weaver shields. Sonic cone broken by line-of-sight. *Teaches: target priority.* |
| 15 | **Gravemaw** | Burrows, surfaces under the player's *predicted* position. *Teaches: break your movement pattern.* |
| 20 | **Archon of the Vail** (major) | Mirrors the player's own Pacts back at them — the more corrupt your build, the deadlier this fight. *Teaches: your choices have weight.* |

Past stage 20, bosses recycle with stacking Ordeals and one previously-unseen attack pattern per repeat.

**The Archon is the design centerpiece.** It's where Veilrot becomes mechanical instead of numerical, and where the game's theme and its systems finally say the same thing.

---

## 10. Veilrot — the signature system

This is what separates Soulvail from every other mobile wave shooter. **If we cut one thing from this document, it should not be this.**

### 10.1 How it works

- **Veilrot** is a 0–100 meter, always visible, starts at 0.
- **Pact nodes** (§13.2) are corrupted versions of ordinary tree nodes — ~1.8× stronger, and they add **+10 to +20 Veilrot**.
- Veilrot never decays. It's reduced only by spending Essence at a Sanctum, or at rare Cleansing shrines.

### 10.2 Thresholds

| Veilrot | Effect |
|---|---|
| **25** | All enemies +5% move speed. The Veil notices you. |
| **50** | A Revenant stalks you each stage, regardless of depth. Ambient audio shifts. |
| **75** | Max HP −20%. Screen edges begin to fray. |
| **100** | **The Claiming.** +100% damage, +30% move speed, dash cooldown halved — and you lose **1% max HP per second**, permanently, until you die. |

### 10.3 Why this is good

The Claiming turns the fail state into a **strategy**. A player at stage 34 with a dying run can *choose* to hit 100 Veilrot and buy 90 seconds of godhood to push two more stages. It converts "I'm about to lose" into "I'm about to gamble" — the most valuable emotional beat a roguelite can produce, and a great one to end a commute on.

It's also cheap: one float, four threshold checks, one state transition.

---

## 11. Platform and performance budget

This section is a design document section, not an engineering appendix, because **on mobile the perf budget dictates how many enemies exist, which dictates the difficulty curve.**

### 11.1 Device tiers

| Tier | Reference hardware | FPS target | Max concurrent enemies |
|---|---|---|---|
| **Low** | ~3-year-old budget Android (Snapdragon 6-series) | 30 | 18 |
| **Mid** | Mainstream current mid-range | 60 | 28 |
| **High** | Recent flagship | 60 | 40 |

Tier is auto-detected on first launch and overridable in settings.

### 11.2 The device-independence rule

**Difficulty must not depend on the player's phone.** When the concurrency cap bites, the spawn director spends the surplus threat budget on *quality* — Elites, expensive archetypes, extra affixes — rather than quantity. A stage-30 run is equally hard on a cheap phone and a flagship; it just looks different.

This is not optional. Without it, low-tier devices become easy mode and leaderboards are meaningless.

### 11.3 Rendering budget

| Budget | Low tier | Mid/High |
|---|---|---|
| Draw calls / frame | < 50 | < 100 |
| Triangles / frame | < 100k | < 300k |
| Realtime lights | 0 | 1 directional |
| Realtime shadows | None (blob decals only) | None (blob decals only) |
| Post-processing | None | Bloom (half-res, low quality) + vignette |

Techniques, in priority order:

1. **SRP Batcher** — the foundation in URP, on by default, needs compatible shaders.
2. **GPU Instancing** for the enemy swarm — one shared material and texture atlas across *all* enemy variants, with color/variation driven by per-instance properties. Instancing benefits are larger on mobile than desktop. Note the caveat: in URP, custom shaders need SRP Batcher compatibility disabled to instance.
3. **GPU Resident Drawer** (Unity 6) — worth evaluating for the dense stages; requires Forward+ and `Batch Renderer Group Variants: Keep All`. Claimed CPU rendering savings up to ~50% in object-dense scenes.
4. **Object pooling everywhere** — enemies, projectiles, VFX, damage numbers. Zero runtime instantiation during a wave.
5. **Watch overdraw, not compute.** Mobile GPUs are bandwidth-bound. Overlapping transparent VFX are the real killer in a horde game — cap simultaneous particle systems and keep alpha layers few.

The project already has `Mobile_Renderer.asset` and `Mobile_RPAsset.asset` in [Assets/Settings/](../Assets/Settings/) — those are the pipeline assets to configure, and the PC ones should be left alone.

### 11.4 Thermal and battery

A 20-minute run must not thermally throttle, or late stages get slower and *unfairly harder* on exactly the runs the player cares most about.

- 60 FPS is a *setting*, not an assumption. Default to 60 on mid/high with a 30 FPS battery-saver option.
- Cap frame rate explicitly (`Application.targetFrameRate`); never let the GPU render frames nobody sees.
- Sanctums and the pause menu drop to 30 FPS and idle the simulation — free thermal recovery at exactly the moment the player isn't looking at motion.
- **Profile on a real low-tier device.** Editor playmode bypasses mobile GPU drivers entirely, so Editor numbers are fiction.

---

## 12. Difficulty scaling — the math

The heart of an endless game. The reference implementations — Risk of Rain 2's director/credit system, and the broad consensus that HP inflation is the *worst* available lever — point the same direction: **spend a budget on composition, and treat stats as nearly fixed.**

### 12.1 Threat Budget

Each stage *n* has a budget the spawn director spends on enemies:

```
B(n) = 40 + 12·(n−1) + 0.9·(n−1)²
```

| Stage | Budget | Rough composition |
|---|---|---|
| 1 | 40 | 10 Husks |
| 5 | 102 | Husks + Spitters + Bloaters |
| 10 | 221 | + Lungers, Weavers, first Elites |
| 20 | 593 | Full roster, several Elites |
| 40 | 1,772 | Dense mixed packs, multi-affix Elites |

Quadratic growth. Early stages ramp gently; deep stages get genuinely oppressive. Bosses are **not** paid from this budget — they're additive.

### 12.2 Wave count and concurrency

```
W(n) = clamp(2 + floor(n / 5), 2, 5)                    // waves per stage
C(n) = min(10 + floor(n / 2), deviceCap)                // deviceCap = 18 / 28 / 40
```

The concurrency cap does two jobs: it protects frame rate *and* readability (P1). On a phone, above ~28 enemies the screen stops communicating regardless of what the GPU can do. Surplus budget converts to quality per §11.2 — which is also the mechanism by which stage 60 differs from stage 30.

### 12.3 Stat scaling (deliberately shallow)

```
HP multiplier:      h(n) = min(1 + 0.06·(n−1), 4.0)     // soft-caps ≈ stage 51
Damage multiplier:  d(n) = min(1 + 0.035·(n−1), 3.0)    // hard cap
Speed multiplier:   s(n) = min(1 + 0.02·floor(n/5), 1.3)
```

Compare the shapes: at stage 40 the budget is **44×** stage 1, but enemy HP is only **3.3×**. That ratio is the entire design thesis. Difficulty comes from *more things doing more different things at once*, not from enemies absorbing more bullets.

### 12.4 Hard guardrails

Invariants. Violating them is a bug, not a tuning choice.

| Guardrail | Rule |
|---|---|
| **One-shot rule** | No non-boss attack may exceed **35% of max HP** at any depth. Boss attacks cap at **50%**. |
| **Spawn safety** | Nothing spawns within **6m** of the player. Revenant teleports are exempt but telegraph 0.5s. |
| **TTK invariant** | A basic enemy dies in **3–5 hits** at every depth. Drift above 5 means HP scaling is too steep or player scaling too weak. |
| **On-screen rule** | No damage originates from outside the camera frustum without a visible edge indicator. Tighter than the PC version of this rule — a phone screen shows less. |
| **Thumb rule** | Nothing critical may resolve in the bottom-left or bottom-right ~15% of the screen. Thumbs live there. |
| **Cover guarantee** | Every arena has a valid circle-strafe path at all times. |

### 12.5 The intended death horizon

Player power grows roughly **+8–12% effective DPS per stage** from levelling the tree. The threat budget grows quadratically. These curves cross — intentionally. That's what makes the game endless but losable.

**The rule this implies:** tree power must stay roughly *linear* against quadratic enemy scaling. If a completed tree makes a player unkillable, Descent has no failure state and everything in §12 is decorative.

| Player | Expected death depth |
|---|---|
| First-time player | Stage 4–8 |
| Competent, ~10 runs in | Stage 15–25 |
| Skilled, well-built tree | Stage 35–50 |
| Best-in-world | 60+ |

If everyone dies at the same depth, scaling is too steep. If good players never die, it's too shallow.

---

## 13. Progression within a run

Two separate moments, doing two separate jobs. **Level-up is progression. The Sanctum is economy.** They never overlap.

### 13.1 Level-up — the progression moment

Fires mid-stage, whenever XP crosses the threshold. **The game pauses**, three skills are offered from the current class's tree, the player taps one, play resumes. Every 25–90 seconds depending on depth.

Full design — tree shape, node kinds, the levelling curve, auto-cast, pinning — is in [Characters.md §4–5](Characters.md).

Rules that belong here because they're global:

- **Offers are drawn at random from currently-available nodes**, not free-picked from the whole tree. Fast enough for a mid-combat interruption on a phone, and it preserves run variety.
- A **View Tree** toggle on the level-up screen shows the full tree with your path highlighted, for players who want to plan. Opt-in, so it never slows anyone else down.
- **Every node must change how you play, not just a number.** A pure "+5% damage" node is filler. Ship 81 nodes where half are stat lines and we've built a spreadsheet with a shooter attached.
- **Every node description must be readable in under 2 seconds on a phone.** If it needs two lines, redesign the effect.
- Nodes **stack additively within a family, multiplicatively across families** — builds compound without exploding.

### 13.2 Pact nodes — Veilrot inside the tree

One of the three offered nodes may appear as a **Pact** — visually corrupted, roughly **1.8× stronger** than the clean equivalent, granting **+10 to +20 Veilrot**.

Pacts are not a branch or a class. *Any* node can appear in its corrupted form, so the temptation is continuous rather than a decision made once at the start. This is what keeps §10 load-bearing at every single level-up rather than a system you interact with twice a run.

| | Clean node | Pact node |
|---|---|---|
| Power | Baseline | ~1.8× |
| Veilrot | 0 | +10 to +20 |
| Flavour | Something you earned | Something you borrowed |

**Example clean:** +15% fire rate · +20 max HP · movement skill leaves a damaging trail · projectiles pierce 1 enemy
**Example Pacts:** +45% damage, −25 max HP *(+15 Rot)* · kills heal 4 HP, enemies spawn 20% faster *(+12 Rot)* · projectiles home, enemy health bars vanish *(+10 Rot)*

### 13.3 The Sanctum — the economy moment

After every stage. A small safe room, untimed. **No skills are offered here** — this is a shop, priced in Essence:

| Service | Cost | Effect |
|---|---|---|
| **Reroll** | 25 Essence (doubles per use) | Reroll your next level-up offer |
| **Banish** | 40 Essence | Permanently remove a node from this run's offer pool |
| **Heal** | 40 Essence | +30 HP |
| **Cleanse** | 60 Essence | −15 Veilrot |

Banish is the most interesting purchase: it's how a player *sculpts* their offers rather than just re-rolling luck, and it makes Essence a build tool rather than a vending-machine token.

### 13.4 Ordeals (deep-run modifiers)

From stage 25, each new biome loop applies a permanent stacking **Ordeal**, drawn randomly:

| Ordeal | Effect |
|---|---|
| **Famine** | Essence drops −40% |
| **Vigil** | Level-ups offer 2 nodes instead of 3 |
| **Swarm** | Concurrency cap +8 (device permitting), Husk cost halved |
| **Fracture** | Arena has 3 fewer cover pillars |
| **Hunger** | Veilrot gains +50% from all sources |
| **Echo** | Every 4th wave repeats the previous wave's composition at full strength |

Ordeals answer "how do you keep an infinite game interesting without infinite content" — a handful of global multipliers that recombine into distinct-feeling runs.

---

## 14. Meta-progression

Meta-progression does exactly one job: **unlock classes.** That's the whole system.

### 14.1 Soul Shards

Earned on death regardless of outcome. Dying must always pay something, or the loop breaks.

```
Shards = 10·(deepest stage) + 50·(bosses killed) + 25·(new archetype first encountered)
```

### 14.2 What Shards buy

| Unlock | Cost | Also unlockable by |
|---|---|---|
| **Gravecaller** | 2,000 | Killing the Choirmother (stage 10) |
| **Emberwright** | 3,500 | Reaching stage 20 |
| Cosmetics | 100–300 | — (pure vanity, no power) |

Two paths to every class — **pay, or prove.** Grinders and skilled players both get there, and neither route is the wrong one.

### 14.3 There is no permanent power progression

No meta stats. No account level. No permanent upgrades. Every run starts every class from the same baseline; the only things that carry over are *which classes you can pick* and what you personally have learned.

This is a deliberate stance, and it buys three things:

- **Difficulty tuning stays tractable.** §12 is balanced against one fixed player baseline instead of a moving target that depends on how long someone has played.
- **Leaderboards mean something.** Depth reached measures skill, not hours.
- **A returning player is never behind.** Nothing decayed, nothing to catch up on. On mobile, where re-engagement after weeks away is the norm, this matters more than it would on PC.

If retention data later demands a permanent layer, it should be **classes and cosmetics — never power.**

> **Cut from v0.1: Anchors.** They existed to solve replay fatigue from re-running solved early stages. With the skill tree now in-run, replaying stages 1–10 isn't repeating solved content — it's rebuilding your build, which is the fun. The problem dissolved, so the system went with it.

---

## 15. Economy summary

| Currency | Scope | Source | Sink |
|---|---|---|---|
| **XP** | Single run | Every kill | Levels → one skill-tree node each |
| **Essence** | Single run | Stage clear, Elites, bosses | Sanctum shop: reroll, banish, heal, cleanse |
| **Soul Shards** | Permanent | Awarded on death | Class unlocks and cosmetics. **Nothing that grants power.** |
| **Veilrot** | Single run | Pact nodes, some Ordeals | Cleansing (Essence), Cleansing shrines |

Essence per stage clear at depth *n*: `20 + 4·n`, plus 15 per Elite and 60 per boss, so income tracks the threat budget and shop services stay meaningfully priced at depth.

**Two deliberate choices worth defending:**

- **XP is granted on kill, not dropped as a pickup.** Manual pickup means chasing motes with a thumb stick, which is neither fun nor readable and directly contradicts P2. It also removes an entire category of on-screen clutter from a 6-inch display — with 28 enemies alive, a floor covered in collectibles is exactly the wrong use of pixels.
- **Essence drops from events, not bodies.** Keeping the two currencies on different rhythms — XP continuous, Essence lumpy — means the Sanctum shop feels like spending a reward rather than skimming a stream.

---

## 16. UI and feedback

### 16.1 HUD layout (landscape, safe-area aware)

The bottom corners belong to thumbs. Nothing important goes there.

| Element | Position | Notes |
|---|---|---|
| **HP bar** | Top-left | Large, with a delayed "ghost" bar showing damage just taken. Numeric `current/max` alongside. |
| **XP bar** | Thin strip along the very top edge | Fills toward the next level. Full width, 4px, ignorable but always there. |
| **Level** | Top-left, beside HP | Just a number |
| **Skill cooldowns** | Top-left, under HP | A row of small icons with radial fills. **Auto-cast skills need visible cooldowns** even though the player doesn't trigger them — otherwise the build is invisible. |
| **Movement skill** | On its own button | Radial fill on the button itself, never a separate readout |
| **Stage / wave** | Top-center | Fades 3s after wave start |
| **Essence** | Top-right | Small counter |
| **Veilrot meter** | Right screen edge, vertical | Grows visually more organic and invasive as it fills |
| **Enemies remaining** | Top-center | Only when ≤5 remain |
| **Off-screen threats** | Screen-edge arrows | Required by the on-screen rule (§12.4) |

HUD opacity is user-adjustable down to 40%. Screen real estate is the scarcest resource on a phone.

### 16.2 Health bars — yes, but not on everything

Health readability matters, and it matters most on the things you're deciding about. But 28 floating bars on a 6-inch screen is noise, not information, and it directly attacks P1.

So: **health is always readable, but expressed three different ways depending on what's carrying it.**

| Who | Treatment |
|---|---|
| **Player** | Always-on bar, top-left, large, with ghost-damage trail and a numeric readout. Never ambiguous. |
| **Bosses** | Big segmented bar across the top of the screen, one segment per phase, so the player can see a phase transition coming. |
| **Elites** | Persistent bar above the unit. They're priority targets — you're making decisions about them, so you need the number. |
| **Basic enemies** | **Damage tint, not a bar.** The body darkens and shifts toward red as it loses HP. A thin bar appears above the unit only while it's being damaged, then fades after 2s. |

The damage-tint approach is the important one: it scales to 28 enemies where 28 bars would not, it costs zero UI pixels, and it reads instantly in peripheral vision — which is where most enemies are while you're watching your own position.

**Settings toggle: "Always show enemy health bars"** for players who want the full readout. Off by default, and we should watch playtests to see whether the default is right — if most people turn it on immediately, the tint isn't doing its job.

### 16.3 Game feel checklist

Non-negotiable — this is what separates a prototype from a game:

- Hit-stop on player hits (40–60ms), scaled to damage
- **Haptics**: light on hit dealt, heavy on damage taken, medium on dash. With an off switch.
- Screen shake on damage taken, ability use, boss slam — **with a 0–100% slider**
- Enemy hit flash (white, 80ms) + directional knockback
- Death: enemies dissolve upward into soul particles. No ragdolls (physics cost, and they read badly from above)
- Damage numbers: **off by default**, toggleable — they're clutter on a small screen
- Low HP: desaturation vignette + heartbeat audio below 25%
- Every projectile has a visible tracer and a faction-distinct silhouette

### 16.4 Color language (P1)

One rule, enforced globally:

| Color | Reserved meaning |
|---|---|
| **Cyan** `#22D3EE` | The player. Projectiles, dash trail, safe things. |
| **Saturated red-orange** `#FF4A1F` | **Danger.** Enemy projectiles, telegraphs, hazard zones. Used for *nothing else, ever.* |
| **Violet** `#A855F7` | Veilrot, Pacts, corruption |
| **Warm gold** `#FBBF24` | Essence, rewards, Gates |
| Desaturated bone / ash | Everything else — environment, neutral props |

Environment art stays desaturated so gameplay-critical colors pop. This is why low-poly works here: it's cheap *and* it enforces the readability pillar. It also survives outdoor screen glare, which a muddy, detailed art style would not.

---

## 17. Art and audio direction

### 17.1 Art

**Low-poly, flat-shaded, strong silhouettes, emissive accents.** Chosen deliberately: producible by a small team, cheap enough for 28 enemies on a mid-range phone, and it forces the silhouette clarity P1 demands.

- Characters: **400–1,200 tris** (half the PC budget), **one shared material and atlas across all enemies** for instancing
- No normal maps, no per-enemy Animator where a simple skinned or vertex-animated loop will do
- Environment: modular kit per biome, 6–10 pieces, all sharing one atlas
- **Enemy silhouettes must be distinguishable as pure black shapes at phone scale.** Confusable in silhouette means one gets redesigned.
- Lighting: fully baked + light probes. One directional light on mid/high, zero realtime shadows anywhere.
- Post: bloom (half-res, low) + vignette on mid/high; nothing on low. No motion blur, no DOF, no SSAO.

### 17.2 Audio

- **Combat music layers** with wave intensity: base → percussion → lead, cross-fading at wave boundaries.
- **Every telegraph has an audio cue** — but design so the game is fully playable **muted**, because most mobile play is silent. Audio reinforces; it never carries information alone.
- Boss music is unique per boss and drops out for a half-second at phase transitions.
- Veilrot ≥50 adds a dissonant drone that never leaves.
- Compressed formats, aggressive streaming — APK size matters for install conversion.

---

## 18. Accessibility and options

Ships in V1. Retrofitting is expensive.

- **Control mode**: Keeper / Precision / Drifter (§5.3)
- **Left-handed mirror** layout
- **Joystick size, opacity, and deadzone** sliders
- Screen shake: **0–100%**
- Haptics: on / off
- Damage numbers: on / off
- HUD opacity: 40–100%
- Colorblind palettes: protanopia, deuteranopia, tritanopia variants of §16.3
- Frame rate: 30 / 60 (battery saver)
- Full audio sliders, and a guarantee the game is playable muted
- **Difficulty modifiers**, separate from endless scaling:

| Modifier | Effect | Shard payout |
|---|---|---|
| Wanderer | Threat budget ×0.7, player HP ×1.3 | ×0.7 |
| Keeper (default) | Baseline | ×1.0 |
| Severed | Threat budget ×1.3, Veilrot gain ×1.5 | ×1.4 |

---

## 19. Scope

### V1 — the shippable core

- **Descent mode**, built on a mode abstraction that could carry others
- Touch controller: floating stick, auto-fire, auto-target with tap-to-focus, all 3 control modes
- **3 classes — Oathbound, Gravecaller, Emberwright** — with class select, **27-node in-run trees each (81 total)**
- Levelling, level-up screen, View Tree, auto-cast, cooldowns, manual pinning
- Stages 1–20, 2 biomes (Ashen Reach, Drowned Choir), 8–12 arenas each
- 6 enemy archetypes (through Warden), Elites with affixes
- 2 bosses (Warden of Ash, Choirmother)
- Full Veilrot system including Pact nodes and the Claiming
- Sanctum shop with all 4 services
- Soul Shards → class unlocks and cosmetics only
- Health bar treatment per §16.2
- Full options/accessibility set
- **Run persistence across app kill**
- Device tiering with the device-independence rule
- Past stage 20, content recycles with Ordeals

### V2

- Remaining 2 biomes + Choir and Revenant
- A 4th class
- Archon of the Vail
- Additional modes (§4.5)
- Full Ordeal set

### Explicitly not doing

Multiplayer · procedural mesh/level generation · inventory or equipment screens · dialogue or cutscenes · crafting · open world · destructible environments · portrait mode (V2 at earliest) · iOS (post-launch)

---

## 20. Milestones

Each ends with something playable. No milestone is "build the architecture" — architecture emerges from making M0 work and then not breaking it.

| # | Milestone | Done when |
|---|---|---|
| **M0** | **Grey-box feel test, on a real phone** | Capsule player moves via floating stick, auto-fires at capsule enemies, dashes, in an untextured box arena — **running on an actual Android device.** Validate Focus (§5.5) here. |
| **M1** | **Stage loop** | Waves spawn from a threat budget, arena seals/unseals, Gate loads next stage, difficulty visibly rises. 3 enemy types. Object pooling from the start. Weapon/movement/passive are **data-driven** (`CharacterDefinition` assets, not an enum switch), and the **mode abstraction (§4.5) exists** even though only Descent uses it. |
| **M2** | **Levelling and the tree** | XP, level-up pause screen, ~12 nodes for one class, auto-cast with trigger conditions, cooldowns, health-bar treatment. **This is the core progression milestone and it comes early** — everything after it is content poured into a working loop. Run persistence. |
| **M3** | **First boss** | Warden of Ash, 3 phases, adds, arena hazard. Death → run-end → payout. |
| **M4** | **Second class** | Gravecaller playable — Bone Bolt, Shroudstep, autonomous Wights, and its own tree. Proves the class abstraction survives a real second case. |
| **M5** | **Systems complete** | Sanctum shop, Pact nodes, manual pinning, View Tree UI, class select, third class, Soul Shards and unlocks. |
| **M6** | **Content pass** | Full V1 enemy roster, Elites + affixes, all 81 nodes, 2nd boss, both biomes with real art. |
| **M7** | **Feel, perf & ship pass** | Game feel checklist, haptics, audio layers, full options, device tiering, thermal validation, tuning against §12.5 **per class**. |

**M0 is a gate, not a formality — and it must run on hardware.** A touch twin-stick that doesn't feel good with untextured capsules on a real phone will not feel good with art. Editor playtesting with a mouse will lie to you about exactly the thing M0 exists to test. Budget real time for it and be willing to change §5–6 based on what it teaches us.

---

## 21. Open questions

Ranked by how much downstream work they affect.

1. **Business model — premium, or free-to-play?** This document currently assumes **premium** (paid once, no ads, no IAP). F2P would change meta-progression pacing substantially: Shard costs in §14 are tuned for a player who owns the game, not one being nudged toward a purchase. It would also add systems (ad-revive, energy, battle pass) that touch the run loop directly. **Decide before M2**, because meta-progression is where it lands.
2. **Orientation** — landscape (recommended, assumed throughout) or portrait? Portrait enables true one-handed play and is what Archero/Survivor.io use, but it costs arena readability and the two-button right thumb. Landscape is assumed; changing it later means redoing every UI screen.
3. **Minimum device spec** — how old a phone do we support? This sets the low tier in §11.1 and therefore the floor for everything visual.
4. **Art sourcing** — asset store kits, AI-generated meshes (Unity's AI packages are already installed in this project), or hand-modelled? Dramatically affects the M4 timeline.
5. **Should the level-up offer guarantee variety?** Pure random produces dead offers, and a dead offer at level 12 costs a run. Probably soft weighting — never 3 nodes from one branch, always ≥1 Active if you own fewer than 2.
6. **Save + cloud** — local JSON is fine for V1, but on Android reinstalls and device changes are common. Decide before M5 whether meta-progression needs Google Play Games save sync. Character levels and trees make this materially more painful to lose than a Shard total.
7. **How many classes in V1?** This doc assumes **3** (Oathbound, Gravecaller, Emberwright), with the Emberwright as the designated cut if the schedule slips. Do not cut to one — a class-based game with one class is a different game.
8. **Is the no-permanent-power stance (§14.3) commercially survivable?** It's the right call for design purity and it's what keeps §12 tunable, but "nothing permanent to grind" is a real retention risk on mobile. Revisit with data, and if it must change, add classes and cosmetics — never power. Class-specific open questions are in [Characters.md §8](Characters.md).

---

## 22. Glossary

| Term | Meaning |
|---|---|
| **Vail** | The veil/vale between living and dead. The setting. |
| **Vailkeeper** | The player character. A warden of the boundary. |
| **Stage** | One sealed arena. Numbered from 1, endless. |
| **Depth** | Current stage number; the universal difficulty input. |
| **Biome / Layer** | A visual + mechanical theme spanning 10 stages. |
| **Threat Budget** | Points the spawn director spends on enemies per stage. |
| **Threat Cost** | What one enemy costs from the budget. |
| **Focus** | Fire-rate ramp for standing still. |
| **Sanctum** | Safe room between stages where upgrades are chosen. |
| **Pact node** | The corrupted version of a tree node — stronger, grants Veilrot. |
| **Pact** | A stronger upgrade that adds Veilrot. |
| **Veilrot** | 0–100 corruption meter. Signature system. |
| **The Claiming** | Veilrot 100 state: enormous power, terminal HP drain. |
| **Elite** | A buffed enemy with an affix. Priority target. |
| **Ordeal** | Global run modifier applied at deep biome loops. |
| **Essence** | In-run currency for the Sanctum shop. |
| **Soul Shard** | Permanent currency. Buys class unlocks and cosmetics — never power. |
| **Descent** | The V1 mode: endless ordered stages until you die. |
| **Device tier** | Low / Mid / High perf bracket driving concurrency caps. |
| **Oathbound** | Paladin class. Free starter. Resists Veilrot. |
| **Gravecaller** | Necromancer class. Raises Wights, thrives on Veilrot. |
| **Emberwright** | Wizard class. Slow AoE, spends Veilrot. |
| **Wight** | Autonomous minion raised by the Gravecaller. |
| **Aegis** | The Oathbound's regenerating shield. |
| **Auto-cast** | Default behaviour of every active skill — fires on cooldown when its trigger condition is met. |
| **Manual slot** | A button holding an active whose auto-cast is switched off. Max 4. |
| **Keystone** | Tier-8 build-defining tree node. One per branch, three per class. |
| **Overflow** | Post-full-tree levels: +2% damage and max HP each, uncapped. |

---

## Appendix A — References consulted

**Difficulty and roguelite structure**
- [Risk of Rain 2 — Directors](https://riskofrain2.wiki.gg/wiki/Directors) — the credit/budget spawn director model §12 adapts
- [Risk of Rain 2 — Difficulty](https://riskofrain2.wiki.gg/wiki/Difficulty) — stage-driven difficulty coefficient
- [Risk of Rain 2 — Level](https://riskofrain2.fandom.com/wiki/Level) — enemy level derived from the coefficient
- [Bullet Sponges — Game Design Snacks](https://game-design-snacks.fandom.com/wiki/Bullet_Sponges) — why HP inflation fails as a difficulty lever
- [There Are Too Many Bullet Sponges In Action Games — Den of Geek](https://www.denofgeek.com/games/bullet-sponges-v/) — the boss-as-sponge failure case
- [5 Essential Tips to Make Your Roguelite Game Work — Entalto Studios](https://entaltostudios.com/5-essential-tips-to-make-your-roguelite-game-work/) — run length, clarity of death, gradual complexity
- [RogueWave (Unity roguelite wave shooter)](https://github.com/TheWizardsCode/RogueWave) — spawner placement and elite design as difficulty axes

**Mobile controls**
- [A Guide To iOS Twin Stick Shooter Usability — Game Developer](https://www.gamedeveloper.com/design/a-guide-to-ios-twin-stick-shooter-usability) — the virtual joystick region and its four core design decisions
- [Everything I Learned About Dual-Stick Shooter Controls — Game Developer](https://www.gamedeveloper.com/design/everything-i-learned-about-dual-stick-shooter-controls) — sticks beat buttons; auto-fire pairing
- [Survivor.io: Will It Follow in Archero's Footsteps? — Naavik](https://naavik.co/deep-dives/survivorio-archeros-footsteps/) — the one-finger + auto-aim genre standard
- [Archero 2 vs. the Competition — Mobile Game Report](https://www.mobilegamereport.com/articles/archero2-vs-competition) — accessibility vs. precision tradeoff against Soul Knight
- [Control Mode: Auto-Aim — Survivorslikes](https://survivorslikes.com/control-modes/auto-aim/) — auto-aim as a spectrum and as accessibility
- [Tilt-Touch Synergy (York University)](https://www.yorku.ca/mack/ec2017.html) — empirical: touch beats tilt; never put aiming on tilt

**Mobile performance**
- [Unity — Introduction to GPU instancing](https://docs.unity3d.com/Manual/GPUInstancing.html) — instancing benefits are larger on mobile; URP SRP Batcher caveat
- [Reduce Draw Calls Unity 6 Mobile](https://game-developers.org/reduce-draw-calls-unity-6-mobile) — draw call targets, GPU Resident Drawer setup
- [Unity Draw Call Batching: The Ultimate Guide — TheGamedev.Guru](https://thegamedev.guru/unity-performance/draw-call-optimization/) — batching stack priority, Frame Debugger verification
- [Art optimization tips for mobile game developers — Unity](https://unity.com/how-to/mobile-game-optimization-tips-part-2) — atlasing, overdraw, baked lighting

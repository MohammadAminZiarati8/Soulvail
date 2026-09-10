# Soulvail — Core Combat Spec

**Version:** 0.1
**Date:** 2026-09-06
**Companion to:** [GameDesign.md](GameDesign.md) · [Characters.md](Characters.md)
**Covers:** movement, aiming/targeting, basic attack, movement skill, and skill casting.

These are **shared systems** — every class uses them, only the numbers change. Values here are the **Oathbound's**, the first and for now only class we build.

This is the M0–M2 implementation spec. If something here isn't precise enough to build from, that's a bug in this document.

---

## 1. What M0 has to prove

> Capsule moves. Capsule faces the right enemy. Capsule swings. It feels good **on a real phone**.

Nothing else. No stages, no waves, no tree, no art. If this isn't fun with untextured capsules, no amount of content saves it — and Editor playtesting with a mouse will lie to you about exactly the thing M0 exists to test.

---

## 2. Movement

### 2.1 The floating joystick

| Property | Value | Notes |
|---|---|---|
| **Virtual joystick region (VJR)** | Left **45%** of screen width, full height below the top HUD strip | Touches outside it do not move you |
| **Origin** | Spawns wherever the thumb lands | *Floating*, never fixed. A fixed stick forces the player to look down to find it. |
| **Deadzone** | 8 dp | Below this, no movement |
| **Full-speed threshold** | 40 dp | See §2.2 |
| **Max radius** | 60 dp | Beyond this, recentering kicks in (§2.3) |
| **Visual base / knob** | 120 dp / 52 dp | Base fades in on touch at 60% opacity |
| **Direction** | Full 360°, analog | Not 8-way. It's a 3D arena. |
| **Multi-touch** | Required | Movement and buttons must work simultaneously, always |

### 2.2 The analog band

Speed is analog in a small window, then flat:

```
  drag distance:   0 ─── 8dp ────────── 40dp ──────── 60dp+
  speed output:    0      0 ──ramp──► 100%          100%
                      deadzone   analog band      full speed
```

Between 8dp and 40dp you get proportional speed for fine positioning; past 40dp you're at max and stay there.

**Why not pure analog to the edge:** in practice players hold near-full deflection ~95% of the time, and demanding thumb precision for normal movement is exactly the ergonomic failure that makes virtual sticks feel bad. **Why not pure binary:** threading between two projectiles wants sub-maximal speed, and losing that costs the game's best moments.

### 2.3 Dynamic recentering — do not skip this

When the thumb travels beyond `maxRadius` from the origin, **move the origin so it stays exactly `maxRadius` behind the thumb.**

Without this, a player who drags 200dp right must drag 200dp back before they start moving left. It feels like the controls stick, players describe it as "laggy" without knowing why, and it is the single most common reason virtual sticks are judged bad.

### 2.4 Acceleration and facing

| Property | Value |
|---|---|
| Time to full speed | 0.06 s |
| Time to full stop | 0.08 s |
| Inertia / sliding | **None** |
| Turn speed | 720 °/s |
| Collision | `CharacterController`, radius 0.45m, capsule |

**Facing is decoupled from movement.** The character strafes: the body faces the current target while moving independently — this is what makes it read as a shooter rather than a runner, and it shows the player who they're about to hit.

| Situation | Facing |
|---|---|
| Has a target | Rotate toward the target at 720°/s |
| No target, moving | Rotate toward movement direction |
| No target, idle | Hold last facing |

An 8-direction blend tree over `localVelocity` handles the animation. No aim IK, no upper-body layer — that budget doesn't exist on this platform.

### 2.5 Oathbound movement values

| Stat | Value |
|---|---|
| Move speed | **3 m/s** |
| Max HP | **140** |
| Aegis shield | **30**, recharges after 4s without damage, refills in 2s (15/s) |

**Move speed was 5.4 m/s until the owner retuned it to 3 after playtesting**, in the same pass that dropped the Husk from 3.5 to 2 m/s. The whole game got slower; the speed *ratio* barely moved — 5.4 / 3.5 was 1.543×, 3 / 2 is 1.5× — which is what keeps [GD §6.1](GameDesign.md)'s rule that every class must feel faster than almost every enemy true. **[Characters.md](Characters.md) §3's class table and GD §6.1's "5.4–6.2 m/s across classes" row still carry the old band**, because moving it is a statement about the Gravecaller and the Emberwright, neither of which is built — flagged for the owner rather than guessed at.

---

## 3. Aiming and targeting

In the default control mode there is **no manual aim.** The gun aims itself. All the design effort goes into making that choice feel *smart*, because auto-aim the player doesn't trust is worse than no auto-aim.

### 3.1 The targeting loop

Runs at **10 Hz**, not per-frame. With 28 enemies this is meaningful CPU on a mid-range phone, and 100ms of latency on target selection is imperceptible.

```
1. GATHER    enemies within acquireRange
2. FILTER    alive, damageable-right-now, (optional) line of sight
3. SCORE     each candidate
4. SELECT    highest score, subject to hysteresis
```

| Property | Oathbound value |
|---|---|
| `acquireRange` | **12 m** (weapon range × 1.5) |
| Loop rate | 10 Hz |
| Line-of-sight check | Skip for V1. Revisit when Bone Orchard cover arrives. |

### 3.2 Scoring

```
score =  archetypePriority                    // 1–8, from the enemy table
       + 3.0 × (1 − distance / acquireRange)  // closer is better
       + 2.0 × isElite
       + 1.0 × canKillWithinOneSecond         // prefer finishing wounded targets
       + 1.5 × isCurrentTarget                // hysteresis, see below
```

`archetypePriority` is the designer-tuned column already in [GameDesign.md §8.1](GameDesign.md) — Choir is 8, Husk is 1. **This is the knob that makes auto-aim feel intelligent**, and it should be exposed per-archetype in the inspector, not buried in code. A Choir healing the pack at 11m must outrank a Husk chewing on you at 3m, because that's what a good player would do.

### 3.3 Hysteresis — the anti-jitter rule

The current target gets a **+1.5 bonus**. A challenger must beat it by that margin to steal focus.

Without this, two similarly-scored enemies make the character twitch between them every 100ms, which looks broken and wastes the swing that was mid-windup. Retarget immediately (skipping hysteresis) only when the current target dies, leaves range, or becomes undamageable.

### 3.4 Tap-to-focus

The player's override, and the reason target priority stays *their* decision rather than the computer's.

| Step | Behaviour |
|---|---|
| **Input** | Tap anywhere in the right zone that isn't a button |
| **Resolution** | Screen ray → ground plane → find nearest enemy within **3 m** of that world point |
| **Generosity** | 3m is deliberately huge. Thumbs are imprecise and this must never feel like a precision test. |
| **Effect** | That enemy becomes the forced target, ignoring scoring entirely |
| **Persists until** | It dies · it leaves `acquireRange` for >2s · the player taps empty ground |
| **Feedback** | Bright animated ring + chevron above the unit |

### 3.5 Reticles are mandatory

| State | Visual |
|---|---|
| Auto-selected target | Subtle cyan ring at the unit's feet |
| Player-focused target | Brighter ring, pulsing, plus a chevron overhead |
| Undamageable target (Warden's shield) | Ring turns hollow, small "blocked" glyph |

This is not polish. A player who can't see what they're shooting at will conclude the auto-aim is broken even when it's making optimal choices.

### 3.6 Vulnerability awareness

The targeter **never selects an enemy it cannot currently damage.**

Against a Warden (immune from the front) it scores it out of contention and picks the next-best target, showing the blocked glyph. If *every* candidate is blocked, hold facing on the nearest one and keep the glyph up — that's the game saying *"go around"* in its own language, and the answer is movement, which is the entire point of the design.

### 3.7 Projectile leading — build the seam now

The Oathbound's cone doesn't need it. The Emberwright's 25 m/s orb will be unusable without it. Build the interface at M2 even though nothing uses it yet:

```
t = distance / projectileSpeed
aimPoint = targetPos + targetVelocity × t     // iterate twice for accuracy
```

Pleasing consequence worth remembering: slow projectiles are the hardest thing to thumb-aim, and auto-lead removes that entirely — a weapon that would be miserable manually becomes a class's whole appeal.

### 3.8 Manual aim (Precision mode)

For the minority who want it ([GameDesign.md §5.3](GameDesign.md)): the right thumb becomes an aim stick, firing follows its direction, and auto-targeting is disabled — but **reticles and vulnerability glyphs stay on**, since the information is still useful.

Ship it in V1, expect ~10% adoption, and don't balance around it.

---

## 4. Basic attack

### 4.1 The Censer

| Property | Value |
|---|---|
| Shape | **60° cone, 8 m** along facing |
| Damage | **13** per swing, to **every** enemy in the arc |
| Rate | **3.0 /s** (0.333s interval) |
| Ammo / reload | None. Ever. |
| Cost | None |

**TTK check:** a Husk has 36 HP → **3 swings** (1.0s). That satisfies the game's primary balance invariant ([GameDesign.md §6.2](GameDesign.md)) and must keep satisfying it at every depth.

Single-target DPS is only 39 — deliberately low. The Oathbound's damage comes from arc coverage (2–4 enemies is normal), and the low single-target number is what he pays for 140 HP and a regenerating shield.

### 4.2 Firing rules

| Rule | Behaviour |
|---|---|
| **Trigger** | Automatic, whenever any enemy is within 8m. Arc is not required to *start* the swing — we're rotating to face anyway. |
| **Idle** | No enemy in range → no swing. No wasted VFX, no audio noise. |
| **Movement** | **Attacking never slows, roots, or interrupts movement.** Non-negotiable. |
| **Damage frame** | 40% through the swing animation — readable, but not a commitment |
| **Hit registration** | Sphere overlap + dot-product angle test. Not per-swing physics colliders. |
| **Multi-hit** | One damage event per enemy per swing |

The movement rule is the one to defend under pressure. Pillar P2 says movement is the answer; the instant attacking costs mobility, the answer becomes "stand still and hold," and the game is over.

### 4.3 Focus

Standing still ramps damage output — the *dodge, plant, burn, dodge* rhythm.

| Property | Value |
|---|---|
| Activation | 0.4 s stationary |
| Ramp | To **130%** fire rate over 1.0 s |
| Cancellation | Instant on any movement input |
| Feedback | Subtle cyan ground glow that intensifies |

Fits the Oathbound especially well: planting in the middle of a swarm, tanking on Aegis, and swinging faster *is* the class fantasy. Tree nodes later modify the cap.

**Flagged for M0 validation.** If it doesn't feel good with capsules, cut it — it's a timer and a multiplier either way.

---

## 5. Movement skill — Charge

Every class has exactly one, on a permanent button, and **it never auto-casts.** Auto-dashing would fight the player for control of position, which is the one thing they are actually doing.

| Property | Value |
|---|---|
| Distance | **8 m** |
| Duration | 0.5 s (16 m/s) |
| i-frames | Full duration **+ 0.05 s** → 0.55 s |
| Cooldown | **2.5 s** |
| Damage | 20 to everything passed through |
| Knockback | 4 m |
| Direction | Current stick direction; facing direction if the stick is neutral |
| Input buffer | **0.15 s** — a tap just before cooldown ends still fires |

The trailing 0.05s of invulnerability and the input buffer are both there to absorb touch latency. Without them the dodge feels unreliable, and an unreliable dodge in a game built on dodging is fatal.

**Retuned by the owner after playtesting: 10 m / 0.22 s / 5 m knockback → 8 m / 0.5 s / 4 m.** Two consequences worth knowing, because both are derived numbers rather than authored ones. The dash is **much less explosive** — 45 m/s down to 16 m/s, and against the retuned 3 m/s walk it is 5.3× move speed where it used to be 8.4×. And **the invulnerable window doubled**, 0.27 s → 0.55 s, which is now longer than a Husk's entire 0.4 s windup: a dodge entered at any point during a telegraph covers the strike outright. Neither number is validated outside the Editor, and the dodge is on the [device-only deferred list](plan/PROGRESS.md) — touch latency is exactly what the input buffer and the i-frame trail exist to absorb, and nothing in the Editor can measure it.

---

## 6. Skill casting — auto-cast per skill

**This replaces the "pin up to 2" model in v0.2 of [Characters.md](Characters.md).** Per-skill toggling is more flexible and it puts the decision where it belongs — with the player, per skill, changeable any time.

### 6.1 Two states per active skill

| State | Button? | Behaviour |
|---|---|---|
| **Auto** *(default)* | No | Fires on cooldown whenever its trigger condition is met |
| **Manual** | Yes — takes a slot | Never fires itself. Taps only. |

Passive skills have no toggle and no button. The movement skill is always manual and never counts against slots.

**Every skill defaults to Auto.** A player who never opens the menu has a complete, playable game with one button — which is the whole promise of the control scheme. Manual is opt-in depth.

### 6.2 Slot limit

**Maximum 4 manual slots**, plus the always-present movement button.

Attempting to set a 5th skill to Manual prompts: *"Manual slots full — which skill goes back to auto?"* with the current four shown. Never silently refuse, and never silently swap.

Four is the ergonomic ceiling for a right thumb in landscape, and it's the same budget Diablo Immortal settled on for the same reason.

```
                          ╭────╮
                    ╭────╮│ S3 │╭────╮
                    │ S2 │╰────╯│ S4 │
                    ╰────╯      ╰────╯
              ╭────╮       ╭──────╮
              │ S1 │       │  ⚡  │      ⚡ = Charge, always present
              ╰────╯       ╰──────╯
```

Slots fill S1→S4 and are draggable to reorder. Unused slots are not drawn — a player with zero manual skills sees exactly one button, and one with two sees three.

| Property | Value |
|---|---|
| Movement skill button | 72 dp |
| Skill slot buttons | 60 dp |
| Minimum spacing | 12 dp |
| Cooldown display | Radial fill on the button itself |
| Unavailable | 40% opacity, no tap response |

### 6.3 Managing toggles

**Pause → Skills.** A list of every active skill you own, each with an Auto/Manual switch, its cooldown, and its auto-cast trigger condition written out in plain language.

Changeable **any time**, including mid-run. This matters because the tree is in-run: you acquire skills as you go, so the management screen has to be usable during play, not just on a loadout screen that doesn't exist.

When a level-up grants your first-ever active skill, show a one-time hint pointing at the Skills screen. Once. Never again.

### 6.4 Auto-cast trigger conditions

Every active ships with a designer-authored condition so Auto feels deliberate rather than random:

| Skill | Fires when |
|---|---|
| **Consecrate** (healing zone) | Player HP < 60% |
| **Bulwark** (shield wall) | An enemy projectile is inbound |
| **Judgment** (damage all in LOS) | ≥4 enemies visible |
| **Sever** (radial burst) | ≥3 enemies within 6m |

Conditions are authored per skill, **not configured by the player.** Exposing a condition editor on a phone means shipping a rules-engine UI, and nobody wants that. The player's controls are the Auto/Manual switch and their own thumb.

### 6.5 Why manual is worth choosing

Auto-cast is condition-driven; a skilled player is *intent*-driven. Casting Wane the instant before a Lunger commits is worth far more than casting it when four enemies happen to be nearby.

Manual should be a real advantage in skilled hands and never a requirement. If playtests show manual is mandatory to survive depth, the auto conditions are too dumb and need fixing — not the balance.

---

## 7. Tuning table

Every number in one place. Expose all of these in a ScriptableObject; do not hard-code any of them.

### Movement
| | |
|---|---|
| Move speed | 3 m/s |
| Accel / decel | 0.06 s / 0.08 s |
| Turn speed | 720 °/s |
| Controller radius | 0.45 m |
| Stick deadzone / analog cap / max radius | 8 / 40 / 60 dp |
| VJR width | 45% of screen |

### Survivability
| | |
|---|---|
| Max HP | 140 |
| Aegis shield | 30 |
| Aegis recharge delay / rate | 4 s / 15 per s |
| Hit i-frames | 0.5 s |

### Attack
| | |
|---|---|
| Cone angle / range | 60° / 8 m |
| Damage | 13 |
| Rate | 3.0 /s |
| Damage frame | 40% of animation |
| Focus delay / cap / ramp | 0.4 s / 130% / 1.0 s |

### Charge
| | |
|---|---|
| Distance / duration | 8 m / 0.5 s |
| i-frames | duration + 0.05 s |
| Cooldown | 2.5 s |
| Damage / knockback | 20 / 4 m |
| Input buffer | 0.15 s |

### Targeting
| | |
|---|---|
| Acquire range | 12 m |
| Loop rate | 10 Hz |
| Hysteresis bonus | 1.5 |
| Distance weight | 3.0 |
| Elite bonus | 2.0 |
| Finisher bonus | 1.0 |
| Tap-to-focus radius | 3 m |
| Focus drop delay | 2 s out of range |

---

## 8. M1 acceptance checklist

Build to this, run it on a phone, and be honest about the answers.

> **Renamed in M2-00a.** This was headed "M0 acceptance checklist" from the first draft, but every
> row it lists — auto-target, reticle, tap-to-focus, Charge, haptics — is M1 work, and both
> [ROADMAP](plan/ROADMAP.md) and [M1-21](plan/tasks/M1-21-acceptance-and-tag.md) have always cited
> it as M1's gate. The heading was the only thing that disagreed. The closing question below is
> M1's question, and it is the one M1-21 answered.

- [ ] Stick spawns under the thumb anywhere in the left 45%
- [ ] Dynamic recentering works — drag far right, then left, and movement reverses immediately
- [ ] Movement and buttons work simultaneously (multi-touch)
- [ ] Character strafes: faces target while moving independently
- [ ] Auto-target picks the sensible enemy and **doesn't jitter** between two of them
- [ ] Reticle is always visible under the current target
- [ ] Tap-to-focus locks on, and the lock is obvious
- [ ] Attacking never slows movement
- [ ] Charge feels reliable — i-frames land, buffered taps register
- [ ] Sustained 60 fps on a mid-tier device with 20 capsules
- [ ] Haptics on hit, damage taken, and Charge

**The one question M1 answers:** *is moving and swinging, with no content around it, fun for two minutes straight?*

If yes, build M2. If no, change these numbers before building anything else — they are far cheaper to change now than after three enemy types and a boss are balanced against them.

**Answered in [M1-21](plan/tasks/M1-21-acceptance-and-tag.md): yes, on Editor evidence, with no number changed** — every value above already matched the asset shipping it. Three rows are device-only and unanswerable until a phone exists, and one is deliberately unresolved rather than passed: *"tap-to-focus locks on, and the lock is obvious"* fails for a tap beyond `acquireRange`, which is silent. That sits in the [parking lot](plan/ROADMAP.md#parking-lot) with its three ways out.

# JG-21e — The cousins' new moves are drawn

**Size:** M · **Depends on:** JG-21a, JG-21d · **Branch:** `dev` (direct)
**Design refs:** GD §8.2; AR §18.2; memory "readable at first look"

## Goal

Every new cousin move reads on a phone at first sight: the Bullfrog's landing ring and its arc
through the air, and a chain bomber's lit fuse with the link it came down.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/ChainLinkViews.cs` | Game | **New.** Pooled links from `EnemyChainLit` |
| `Materials/Enemies/M_ChainLink.mat` | — | **New.** Unlit additive, the danger red-orange |
| `Tests/Game/Views/CousinViewsTests.cs` | Tests.Game | **New.** Rules 1–7 |
| *small edits* | | `TelegraphRings` (`EnemyLeapAimed`); `EnemyView` (cosmetic hop arc); `EnemyAnimatorView` (`Leap` on move 1 of a leaper); `EnemyHitFeedback` (fuse flash); `Animation/Controllers/AC_Frog.controller` (`Leap` state if absent); `RunScope`, `RunTicker` (register and tick `ChainLinkViews`) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class ChainLinkViews : IDisposable
    {
        public ChainLinkViews(IEventHub hub, LineRenderer prefab, Transform root, int capacity);
        public void Tick(float dt);
        public int ActiveCount { get; }
    }
}
```

## Behaviour

1. **Landing ring**: `EnemyLeapAimed` rents a filling telegraph ring at `Target` with `Radius`,
   lasting `CrouchSeconds + AirSeconds` — the same ring a ringed lob draws.
2. **Crouch and flight**: move 1 of `EnemyTelegraph` on an agent whose look is a leaper plays the
   Frog rig's `Leap` clip once.
3. **Arc**: for `AirSeconds` after the crouch the body's model child (never the
   `CharacterController`) rises on a parabola peaking at 1.2 m and is back at 0 on landing. Cosmetic
   time; core position is untouched.
4. **Link**: `EnemyChainLit` draws a line from `From` to `To`, 0.15 m wide, 0.3 m above the floor,
   fading over `Seconds`; then returns to the pool.
5. **Fuse flash**: the lit body flashes red-orange at 8 Hz for `Seconds`; a death or despawn ends it.
6. **Pool**: `capacity` links (16); a 17th reuses the oldest. Nothing instantiates after `Begin`.
7. **Reserved colours**: red-orange only on rings, links and fuse flashes (danger); no cyan, violet
   or gold anywhere in this task.

## Tests

| Test | Given / When / Then |
|---|---|
| `LeapAimed_RentsRingForCrouchAndAir` | event 0.6 + 0.5 s / ring at target, 1.1 s (1) |
| `LeaperTelegraph_PlaysLeap` | move 1 on a leaper; on a Frog / `Leap`; nothing new (2) |
| `HopArc_PeaksAndReturns` | 0.5 s air / model y 1.2 at 0.25 s, 0 at 0.5 s (3) |
| `ChainLit_DrawsLinkThatFades` | event / line From→To, gone after 0.3 s (4) |
| `ChainLit_FlashesUntilDeath` | lit, then died / flash stops (5) |
| `Links_ReuseOldestPastCapacity` | 17 events / 16 active (6) |
| `CousinViews_UseNoReservedColour` | material colours / none cyan, violet or gold (7) |

## Manual verification (Editor / device)

1. **[Editor]** A Bullfrog from DT-01 at stage 21 crouches, a ring fills, it arcs in and lands on it.
2. **[Editor]** Shoot one of four Chain bombers bunched: links run, each flashes, then pops in turn.

## Out of scope

- Tints, scales and assets (JG-21f). Shadow panther and Thorn pitcher need no new view: lanes and
  lob rings already draw them.

## As built

_Filled at merge._

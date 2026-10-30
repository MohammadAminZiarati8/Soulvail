# JG-13e — Pods are lobbed and hatch

**Size:** S · **Depends on:** JG-13c (the picker; also edits `MotherPitcherBehaviour.cs`) · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 1, 2, 4; JG-12c rules 1, 4, 6

## Goal

Mother Pitcher's pod is no longer a Frog appearing 3 m away: it is a lob onto open ground, it sits
there for a second, and then its Frog hatches.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/MotherPitcherBehaviour.cs` | Core | the pod's flight, planting and hatching |
| `Tests/Core/Ai/MotherPitcherPodTests.cs` | Tests.Core | rules 1–7 |
| *small edits* | | `Core/Events/BossEvents.cs` (`PodPlanted`, `PodHatched`) |
| *ripple* | | `MotherPitcherAttackTests`: `Mother_Pod_DropsAFrogAway` removed (rule 1 replaces it); `Mother_Pod_SkippedAt3Frogs` and the allocation row step through a hatch |

## Public API

```csharp
public const float PodHatchSeconds = 1f;                 // MotherPitcherBehaviour; PodDistance is removed
public readonly struct PodPlanted { public readonly int Id, BossId; public readonly Vector3 At;
    public readonly float Seconds; public PodPlanted(int id, int bossId, Vector3 at, float seconds); }
public readonly struct PodHatched { public readonly int Id; public PodHatched(int id); }
```

## Behaviour

1. **The pod is a lob.** At the pod's release she picks a spot with her `OpenGroundPicker` (pods not
   yet hatched reserved) and fires one `Projectile` of the Frog's spec id from her position to it, at
   `LobSpeed`, `LobRadius`, her `ContactDamage`. With no spot, the target is `MinDistance` out on the
   side away from the player.
2. **It lands and sits.** When that shot's flight ends (the XZ distance over `LobSpeed`, as
   `ProjectileSystem` counts it) she publishes `PodPlanted(shot id, her id, spot, PodHatchSeconds)`, once.
3. **It hatches.** `PodHatchSeconds` after planting, one Frog spawns at the spot and `PodHatched(id)`
   is published. With the registry full, no Frog, and `PodHatched` still.
4. **Pods count against the cap.** Live Frogs plus pods not yet hatched at `MaxPodFrogs` turns a pod
   into a barrage, as live Frogs alone did.
5. **A crossing leaves pods alone; `Reset` drops them.** `OnPhaseEntered` keeps pods in flight and
   planted (lobs in flight land, JG-12c rule 6); `Reset` forgets them without events.
6. **A pod lands like a lob.** A player inside the ring when it lands takes her `ContactDamage`,
   through `ProjectileSystem` as a barrage lob does.
7. **`Tick` allocates nothing**, a release, planting and hatch included (warmed Frog pool).

## Tests

| Test | Given / When / Then |
|---|---|
| `Mother_Pod_LobsAFrogShotOntoOpenGround` | lattice report / pod release / one `ProjectileFired`, Frog spec id, from her, target a picked spot; no Frog yet |
| `Mother_Pod_PlantsWhenTheFlightEnds` | release, 7 m spot / step to 1.0 s, then past / no `PodPlanted`, then one at the spot, 1 s |
| `Mother_Pod_HatchesAFrogASecondLater` | planted / step 0.99 s, then 0.02 s / no Frog; then one Frog at the spot and `PodHatched` |
| `Mother_Pod_FullRegistry_HatchesNothing` | registry full at hatch / step / no Frog, `PodHatched` published |
| `Mother_Pod_PendingPodsCountAgainstTheCap` | two Frogs live, one pod pending / next pod slot / a barrage |
| `Mother_Pod_SurvivesACrossing_ResetDropsIt` | pod in flight / `OnPhaseEntered`, then `Reset` / still hatches; after `Reset`, no events |
| `Mother_Pod_LandingOnThePlayerHurts` | player at the spot / projectiles tick past landing / player takes her `ContactDamage` |
| `Mother_Pod_AllocatesNothing` | warmed / release to hatch under `AllocationAssert.None` / passes |

## Manual verification (Editor / device)

Owed after JG-13f, which draws it.

## Out of scope

- Drawing the pod (JG-13f); hatched Frogs clearing on a phase beat (PROGRESS known issue 11).

## As built

_Filled at merge._

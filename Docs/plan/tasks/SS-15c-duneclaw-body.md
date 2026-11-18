# SS-15c — Show and sound the Duneclaw's attacks

**Size:** M · **Depends on:** SS-15a, SS-15b · **Branch:** `dev` (direct)
**Design refs:** [GD §9.3](../../GameDesign.md#93-the-sunscar--stage-5-the-duneclaw), GD §16.4, `EnemyView`

## Goal

The pooled boss body shows each attack's exact shape and plays a distinct cue at the start of each tell.

## Files

| Path under `Assets/_Project/` | Purpose |
|---|---|
| `Game/Views/DuneclawView.cs` | Match events to animator, tells and two audio cues |
| `Prefabs/Enemies/Duneclaw.prefab` | Pooled body, rig, view and serialized references |
| `Animation/Controllers/AC_Duneclaw.controller` | Clip states driven by view events |
| `Audio/Enemies/DuneclawSweep.wav` | Short dry claw scrape |
| `Audio/Enemies/DuneclawSlam.wav` | Short low tail rattle |
| `Tests/Game/Views/DuneclawViewTests.cs` | Visual and audio event routing and pooling |

## Public API

```csharp
// DuneclawView : MonoBehaviour, IPoolable
```

## Behaviour

1. Filter combat and phase events by this body's enemy id. On arm, show a filled Danger
   sector for sweep or a Danger circle for slam, using the announced geometry and duration.
   Light only the matching claw or stinger. Play the corresponding short local audio cue
   once at arm time. Use simple locally authored placeholder sounds for the two cues.
2. At impact, clear the attack tell, play the attack and hold the planted animation through
   the impact's recovery on slams. Aim the posed tail toward the announced slam centre.
   Phase entry plays the shake and clears the active tell. Use ordinary movement, hit and
   death states outside committed moves. No animation event decides damage.
3. Use `EnemyView`'s body and pooling patterns. Clear animator, tell, glow and audio on
   despawn and reuse. A sweep followed by a slam shows only the pending attack.
   Reuse tell geometry buffers and materials during ticks.

## Tests

| Test | Given / When / Then |
|---|---|
| `Arm_MatchingBody_ShowsExactSectorOrCircleAndPlaysDistinctCue` | Two bodies / each attack / shape, timer, glow and cue on owner only (1) |
| `ImpactAndPhase_ClearTell_AndPinUsesRecovery` | Armed boss / impact, next arm or phase / correct clips and state (2, 3) |
| `DespawnAndReuse_ClearsPresentation` | Visible tell and playing sound / pool cycle / clean state (3) |

## Manual verification (Editor / device)

1. **[Editor]** Fire both tells on the body. Check the claw, stinger, aim and sound start against the marked areas.
2. **[device]** Check both geometry and cue distinctions in the stage-5 fight once SS-15e lands.

## Out of scope

Slab rendering, roster and selection of a production audio source.

## As built

_Written at merge in the commit body._

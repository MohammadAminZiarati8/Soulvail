# M7-05k — An enemy showcase

**Size:** M · **Depends on:** M7-05j (the Frog's clips), M7-05h (the Rootling's) · **Branch:** `m7-05k-an-enemy-showcase`
**Design refs:** none; a review scene · **Ledger rows:** none

The owner's request of 2026-09-29: *"create a showcase scene for the enemies. Add the frogs to them,
and they should show their animation there in loops."*

## Goal

`EnemyShowcase.unity`: every enemy body with clips, one instance per clip, each looping, side by side
on the Jungle's ground.

## Why this shape

- **One body per clip, not one body touring every clip.** `CharacterShowcase` tours each body
  through its clips, one at a time. A new enemy is reviewed by comparing its clips, so here each clip
  plays on its own body at once.
- **A component, because the entry state is the controller's.** Six frogs sharing one controller all
  start in its default state. `ShowcaseClip` names the state each instance starts in, which is one
  line of code. The alternative was eleven override-controller assets.
- **Every state loops.** A looping clip loops itself. A one-shot transitions back to itself once it
  has held its last frame a while, so a death lies on the ground before it gets up.
- **Raw models, not the run's prefabs.** A run's body needs injection and throws without it; the
  showcase needs only the model, its Animator and its clips.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Sandbox/ShowcaseClip.cs` | Game | **New.** Starts a body in a named state (rule 2) |
| `Animation/Controllers/AC_Frog_Showcase.controller`, `AC_Rootling_Showcase.controller` | — | **New.** One looping state per clip (rule 3) |
| `Scenes/EnemyShowcase.unity` | — | **New.** The Frog's row and the Rootling's (rule 1) |
| `Tests/Game/Art/EnemyShowcaseTests.cs` | Tests.Game | **New.** Rules 1–3 |

## Public API

```csharp
namespace Soulvail.Game.Sandbox
{
    [RequireComponent(typeof(Animator))]
    public sealed class ShowcaseClip : MonoBehaviour
    {
        // [SerializeField] string _state — the state it plays from Start
        public string State { get; }
    }
}
```

## Behaviour

1. **Each Frog clip is shown once.** The Frog's row names each of `Frog.fbx`'s six clips, and each
   state of `AC_Frog_Showcase` plays the Frog clip of its own name.
2. **A body starts in its state.** `ShowcaseClip.Start` plays its state on the Animator's first
   layer, and every body's state exists in its controller.
3. **Nothing plays once and freezes.** Every state of every showcase controller loops its clip or
   returns to itself after its exit time, so a one-shot plays again.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Showcase_ShowsEveryFrogClip` | the scene and `Frog.fbx` / the Frog row's states / the six clips, each once, each state its own clip (rule 1) |
| `Showcase_EveryBodyStartsInAStateOfItsController` | every `ShowcaseClip` / its state / in its controller (rule 2) |
| `Showcase_EveryStateLoops` | every showcase state / read / a looping clip or an exit-time transition to itself (rule 3) |
| `Showcase_AOneShotPlaysAgain` | a Frog on `Hit` / `Start`, then a second of updates / past its end, then back in `Hit` from its start (rules 2, 3) |

## Manual verification (Editor / device)

1. **[Editor]** Open `Scenes/EnemyShowcase.unity` and press Play. *Expected:* six frogs in front:
   Idle, Hop, Attack, Leap, Hit, Death. Five Rootlings behind them: Idle, Walk, Attack, Hit, Death.
   Each loops, and a one-shot pauses at its end before playing again.

## Out of scope

- The capsule archetypes, which have no clips.
- The wind-up glow and the hit flash, which a run's `EnemyHitFeedback` drives.

## As built

**As specified**, with two notes. *(1)* **The Rootling's row plays its run clips**
(M7-05h *As built*): `Skeletons_Idle`; `Crouching` at 2.5×, where a run plays it at up to 4×;
`Melee_2H_Attack_Chop` at 1.3×, `Hit_A` at 1.4× and `Death_A` at 1.5×, a run's own rates. *(2)* The lights and the camera's
look are `CharacterShowcase`'s, copied; the ground is `M_JungleGround` at 80 m square, so the camera's
32° view meets no horizon.

**Holds.** Attack 40 % of its length, Leap 30 %, Hit 80 %, Death 120 %, each blending back into
itself over 0.15 s.

**Findings.** *(1)* **`AssetDatabase.SaveAssets` rewrote `ProjectSettings/TimeManager.asset`** into
6.3's rational form at the same 0.02 s (Traps §5); reverted. *(2)* **The MCP refused a command that
called `AssetDatabase.DeleteAsset`** before running any of it, as it refused `File.Delete` at M3-01b
(Traps §3); nothing needed deleting.

**Verified.** `EnemyShowcaseTests` 4 / 4. Render: `Temp/Renders/enemy_showcase.png`, each body
sampled mid-clip, since Animators do not tick in Edit mode. Suite counts are M7-05m's.

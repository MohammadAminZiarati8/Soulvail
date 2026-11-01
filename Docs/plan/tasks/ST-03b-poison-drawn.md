# ST-03b — Poison drawn: bubbles on the body, a drop on the bar

**Size:** S · **Depends on:** ST-02, ST-03a · **Branch:** `st-03b-poison-drawn`
**Design refs:** GD §16.4, GD §16.5

## Goal

A poisoned enemy reads at a glance: green bubbles rise off its body while poisoned, and its bar shows a poison-drop icon with the stack count.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Art/UI/Hud/T_Status_Poisoned.png` | — | bile-green drop (`#7BC043`) on a dark rim, 64 px |
| `Prefabs/Vfx/FX_Status_Poison.prefab` | — | slow rising bubbles, the frost effect's shared unlit material, tinted by property block |
| `Game/Views/EnemyStatusView.cs` | Game | effects keyed by id; a fourth, poison |
| *small edits* | | `Data/StatusIconBook.asset` (+ poisoned), `Enemy.prefab` and `Shaman.prefab` (`_poisonBubbles`), `EnemyStatusViewTests` |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class EnemyStatusView : MonoBehaviour
    {
        [SerializeField] private GameObject _poisonBubbles;
        public GameObject Effect(ContentId status);   // null for a status without one
    }
}
```

## Behaviour

1. A poisoned event with `Seconds > 0` turns the bubbles on; 0 s, death or unbind turns them off and clears the particles.
2. The bubbles sit at the body's mid-height and scale with the capsule radius, as the frost does.
3. The bar's poisoned icon is the book's drop. With ST-02's ordering it outranks a slow and loses to a stun or root.
4. Green is not a reserved colour (GD §16.4). The drop's shape, not its hue, separates it from the Shaman's lime haste chevrons.
5. Bubbles are built under the parked root at construct time and never instantiated mid-run.

## Tests

| Test | Given / When / Then |
|---|---|
| `Poisoned_TurnsBubblesOn_AndOff` | bound view / poisoned 4 s, then 0 s / active, then inactive |
| `Poisoned_Death_ClearsBubbles` | poisoned / `EnemyDied` / inactive, no particles alive |
| `Effect_ById_NullWhenNone` | `Effect(hasted)` / / null |
| `Prefabs_HaveBubbles` | `Enemy`, `Shaman` prefabs / load / `_poisonBubbles` assigned |
| `IconBook_Poisoned_HasSprite` | the book / `For(poisoned)` / not the fallback |

## Manual verification (Editor / device)

1. **[Editor]** Debug-poison a Mossguard (three stacks): green bubbles rise; the bar shows the drop with "3", draining over 4 s, and the HP drops in steps.
2. **[Editor]** Poison then stun the same enemy: the stun icon shows; the bubbles keep rising.

## Out of scope

- A poison source in play; green damage numbers (numbers are off by default, GD §16.3).

## As built

_Commit body at merge, ≤ 1 500 bytes._

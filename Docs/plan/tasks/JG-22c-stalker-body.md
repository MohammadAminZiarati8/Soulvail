# JG-22c — The Chameleon stalker's body: shimmer, reveal, strike

**Size:** M · **Depends on:** JG-22a, JG-22b · **Branch:** `dev` (direct)
**Design refs:** GD §8.2; AR §18.2; memory "readable at first look"

## Goal

A hidden stalker is a faint shimmer a player can spot if they look — never fully gone — and its
reveal before a strike is unmissable on a phone.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/StalkerView.cs` | Game | **New.** Cloak blend, reveal flash, bar hiding |
| `Prefabs/Enemies/Stalker.prefab` | — | **New.** `Stalker.fbx`, `StalkerView`, the Shaman prefab's parts |
| `Animation/Controllers/AC_Stalker.controller` | — | **New.** Idle/Sneak blend, Crouch, Strike, Hit, Death |
| `Shaders/S_Cloak.shadergraph` | — | **New.** URP transparent: rim shimmer over a near-clear body |
| `Materials/Enemies/M_StalkerCloak.mat` | — | **New.** From `S_Cloak`, atlas-sampled, pale leaf rim |
| `Tests/Game/Views/StalkerViewTests.cs` | Tests.Game | **New.** Rules 1–6 |
| *small edits* | | `EnemyAnimatorView` (`Crouch` while lurking: speed 0 and hidden-capable); `EnemyHealthBar` (`SetHidden`) |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class StalkerView : MonoBehaviour
    {
        [SerializeField] private Renderer _body;
        [SerializeField] private Material _cloak;
        public void Bind(int enemyId, IEventHub hub, EnemyHealthBar bar);
        public float CloakAmount { get; } // 0 shown … 1 hidden
    }
}
```

## Behaviour

1. **`EnemyHiddenChanged(true)`** blends the body to `M_StalkerCloak` over 0.3 s (`CloakAmount`
   0 → 1); `false` blends back over 0.15 s. Other ids are ignored.
2. **Never invisible**: at full cloak the rim stays at ≥ 25 % alpha and the body ≥ 8 %; the shimmer
   scrolls so a still stalker still moves on screen.
3. **The bar hides with it**: hidden = health bar and status row off; shown = back at the current
   fraction.
4. **Reveal flash**: `EnemyTelegraph` for this id snaps `CloakAmount` to 0 and flashes the body
   white for 0.1 s before the windup clip — the lane already warns in red-orange.
5. **Damage shows it**: `EnemyDamaged` for a hidden stalker drops the cloak at once (core reveals
   it; the view does not wait for the event order).
6. **Reset**: despawn returns the material, `CloakAmount` 0, bar on.

## Tests

| Test | Given / When / Then |
|---|---|
| `Hidden_BlendsToCloak` | event / 1 after 0.3 s, 0 after 0.15 s back (1) |
| `Cloak_NeverFullyClear` | full cloak / rim ≥ 0.25, body ≥ 0.08 alpha (2) |
| `Hidden_HidesTheBar` | hidden; shown / bar off; on at fraction (3) |
| `Telegraph_RevealsAndFlashes` | hidden, telegraph / 0 at once, flash 0.1 s (4) |
| `Damaged_DropsCloak` | hidden, damaged / 0 (5) |
| `Despawn_ResetsEverything` | despawn mid-cloak / material, 0, bar on (6) |

## Manual verification (Editor / device)

1. **[Editor]** A stalker sneaks behind the Ranger, crouches and fades to a shimmer that is still
   findable on a phone-sized Game view; the auto-aim ignores it.
2. **[Editor]** Stop beside it: it flashes, the lane arms, it strikes. **The owner rules whether the
   shimmer is too easy or too hard to spot.**

## Out of scope

- Asset and stage (JG-22d). Sound cues (no audio yet).

## As built

_Filled at merge._

# SF-05 — A STAGE N card at the start of every stage

**Size:** S · **Depends on:** SF-04 · **Branch:** `sf-05-stage-card`
**Design refs:** GD §7.1

## Goal

Owner, 2026-11-10: when a stage begins, the HUD shows its number big for a few seconds. A placeholder that works; the UI rework restyles it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/StageCardView.cs` | Game | new; rules 1–4 |
| `Prefabs/UI/Hud.prefab` | — | a `StageCard` child: one centred TMP text, not a raycast target |
| `Tests/Game/Presentation/StageCardViewTests.cs` | Tests.Game | new |

Allows `.meta` files and test count/coverage updates. Injected the way `StageReadout` is (`Construct(DomainEventHub, ILocalizer)`); text from the existing `ui.hud.stage` key (`STAGE {0:0}`), no new strings.

## Public API

```csharp
namespace Soulvail.Game.Presentation
{
    public sealed class StageCardView : MonoBehaviour
    {
        [Inject] public void Construct(DomainEventHub hub, ILocalizer localizer);
        public void Advance(float unscaledDt); // Update passes Time.unscaledDeltaTime
        public bool Showing { get; }
        public float Alpha { get; }
    }
}
```

## Behaviour

1. Every `StageArrived`, stage 1 included, shows the card with `ui.hud.stage` formatted with `evt.Stage`.
2. Timeline from arrival, unscaled: alpha 0 until 0.45 s (under the gate flash), up to 1 by 0.65 s, holds to 2.15 s, down to 0 by 2.5 s, then hidden. A new `StageArrived` restarts it.
3. `RunEnded` hides it at once.
4. Look: white, 96 dp, `Palette.Player` outline, centred one third from the top of the safe area; never blocks touches.

## Tests

| Test | Given / When / Then |
|---|---|
| `Card_Arrived_ShowsStageNumber` | arrival at stage 7 / — / `Showing`, text `STAGE 7` (rule 1) |
| `Card_FirstStage_Shows` | arrival at stage 1 / — / `Showing` (rule 1) |
| `Card_Timeline` | arrival / advance to 0.3, 1.0, 2.3, 2.6 s / alpha 0, 1, between 0 and 1, hidden (rule 2) |
| `Card_Rearrival_Restarts` | arrival, advance 2 s / arrival at stage 8 / alpha 0 at +0.3 s, text `STAGE 8` (rule 2) |
| `Card_RunEnded_Hides` | showing / `RunEnded` / hidden (rule 3) |
| `Card_NotRaycastTarget` | Hud prefab / load / the card's text and any graphic have `raycastTarget` false (rule 4) |

## Manual verification (Editor / device)

1. **[Editor]** Start a run: as the flash clears, STAGE 1 rises, holds ~1.5 s, fades.
2. **[Editor]** Walk through a gate: STAGE 2 shows the same way; the joystick still works under it.
3. **[device]** Readable on the phone in landscape without covering the hero.

## Out of scope

- A BOSS label, the run progress strip, the STAGE CLEAR banner, the edge arrow (UI rework).
- Retiring `HudPresenter`'s black fade.

## As built

_Written at merge as the commit body, ≤ 1 500 bytes._

# ST-02 — The enemy bar's status row: by id, debuffs first, one icon on basic enemies

**Size:** M · **Depends on:** ST-01 · **Branch:** `st-02-status-row`
**Design refs:** GD §16.2, GD §16.5

## Goal

The bar draws statuses by id, debuffs first: one icon on a basic enemy, four on an Elite, a count on stacks above 1.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EnemyStatusRow.cs` | Game | plain C#: the active entries, their order and countdown |
| `Game/Views/EnemyHealthBar.cs` | Game | status part rewritten over `EnemyStatusRow`; four pooled icon slots |
| `Art/UI/Hud/T_Status_Hasted.png` | — | lime double chevron, 64 px, replaces the code-drawn one |
| `Tests/Game/Views/EnemyStatusRowTests.cs` | Tests.Game | |
| *small edits* | | `Data/StatusIconBook.asset` (+ hasted), `EnemyHealthBarTests` |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class EnemyStatusRow
    {
        public const int Tracked = 8;
        public const int EliteShown = 4;
        public const int BasicShown = 1;
        public int Count { get; }
        public void Apply(in EnemyStatusChanged evt);  // add, update or remove by id
        public void Step(float dt);                    // cosmetic countdown
        public void Clear();
        public StatusRule RuleAt(int rank);            // rank 0 = drawn first
        public float FillAt(int rank);                 // remaining / duration, 0..1
        public int StacksAt(int rank);
        public int Shown(bool isElite);                // min(Count, Elite/BasicShown)
    }
}
```

## Behaviour

1. Order: every debuff before every buff; within a side, higher priority first; a tie keeps arrival order.
2. `Seconds > 0` adds an id or updates its fill duration and stacks; `Seconds 0` removes it.
3. A ninth id is dropped if it ranks below all eight; otherwise it evicts the lowest-ranked.
4. A basic enemy draws rank 0 only; an Elite draws ranks 0–3. Bosses still show no enemy bar.
5. Each icon's sprite is `StatusIconBook.For(rule.Id)`; an id without a sprite gets the fallback, never nothing.
6. A stack count above 1 shows as a number on the icon's lower right; 1 shows no number.
7. The fill sweep drains from the event's seconds to 0. A new event for the same id restarts it.
8. Unbind, death and `ClearStatuses` empty the row. Drawing allocates nothing after the first bind.

## Tests

| Test | Given / When / Then |
|---|---|
| `Order_DebuffsBeforeBuffs` | hasted then slowed / rank / slowed 0, hasted 1 |
| `Order_PriorityWithinSide` | slowed, stunned, rooted / rank / stunned, rooted, slowed; equal priorities by arrival |
| `Apply_ZeroSeconds_Removes` | stunned / stunned 0 s / `Count` 0 |
| `Apply_Ninth_EvictsLowest` | eight entries, a ninth outranking one / apply / lowest gone, `Count` 8 |
| `Apply_Ninth_BelowAll_Dropped` | eight entries, a ninth ranking last / apply / unchanged |
| `Shown_BasicOne_EliteFour` | five entries / `Shown(false)`, `Shown(true)` / 1, 4 |
| `Fill_DrainsAndRestarts` | slowed 2 s / step 1 s, then slowed 2 s again / 0.5, then 1 |
| `Stacks_CountOnlyAboveOne` | stacks 1, then 3 / bar draws / no label, then "3" |
| `Bar_BasicEnemy_DrawsOneIcon` | stunned + slowed on a basic enemy / draw / only the stun icon active |
| `Bar_Hasted_UsesBookSprite` | hasted / draw / sprite is the book's hasted entry |
| `Bar_UnknownId_Fallback` | a rule with an unmapped id / draw / fallback sprite |
| `Bar_Draw_NoAllocation` | bound bar, statuses churning / 100 draws / `AllocationAssert.None` |

## Manual verification (Editor / device)

1. **[Editor]** Stage 17, debug-stun a hasted Mossguard: one stun icon over the bar; when the stun ends, the haste chevrons return.
2. **[Editor]** Debug-slow a Frog: frost on the body, one slow icon draining over 3 s.

## Out of scope

- A boss-bar row; haste on the body; restyling (the UI is placeholder).

## As built

_Commit body at merge, ≤ 1 500 bytes._

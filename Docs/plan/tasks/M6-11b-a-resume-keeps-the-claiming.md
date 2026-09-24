# M6-11b — A resume keeps the Claiming

**Size:** S · **Depends on:** M6-11 · **Branch:** `m6-11b-a-resume-keeps-the-claiming`
**Design refs:** GD §10.3; AR §18.1 · **Ledger rows:** [M7 row 5](../ROADMAP.md#carry-forward-into-m7)

## Goal

A Claimed run comes back Claimed from a `Continue` whatever its meter reads, so quitting is never a
way out of GD §10.3's hundred seconds.

## Why this is a bug and how it was found

Found by [M6-11](M6-11-acceptance-and-tag.md)'s instrument B. A Claimed Emberwright at stage 36 kept
casting through its cooldowns for 5 Rot each (M6-07c), so the meter read **75** when the stage's last
body fell. That boundary snapshot was restored on a Continue as **Veilrot 75, not Claimed** — no drain,
no death, and the owner played on to stage 39 unable to die.

The save carries `RunEconomy.Veilrot` and **not the latch**. M6-04 rule 9 stated the one resume case
it considered — *"a run restored at exactly 100 comes back Claimed with `ClaimedFor` at zero"* — and
M6-04's own `Claiming_SurvivesCleansing` and M6-07c's paid cast are the two ways a Claimed meter
legitimately falls below 100. Either one, a boundary, and a quit, and the Claiming is gone. The Sanctum's
Cleanse is the same escape for every class, one stage later than the paid cast.

## Why v4 is re-cut rather than bumped

[M6-01b](M6-01b-save-format-v4.md) licensed it in writing: *"this format is re-cut before `m6` is
tagged — which costs nothing, because v4 has never left the machine it was written on."* One optional
field on `RunEconomy`, defaulted `false`, and a restore that latches on **`claimed || value >= Max`** —
so a v4 file written before this task, which has no field, still restores exactly as M6-04 rule 9 says.
**If this merges after `m6` is tagged the licence has expired**, and the same change is a v5 with a
migration step deriving `claimed` from `veilrot >= 100`; the task says which it did.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Save/SaveDtos.cs` | Core | `RunEconomy` gains `bool Claimed`, optional and last |
| `Core/Run/Veilrot.cs` | Core | `Restore(float value, bool claimed)` — latches silently, `ClaimedFor` zero |
| `Tests/Core/Run/RunSessionResumeTests.cs` | Tests.Core | Rule 3, end to end |
| *small edits* | Core, Game | `Core/Save/RunRecorder.cs` writes `state.IsClaimed`; `Core/Run/RunSession.cs` passes it to `Restore`; `Game/Adapters/LocalJsonSaveStore.cs` gains `claimed` on the mirror; `VeilrotTests`, `SaveDtoTests`, `LocalJsonSaveStoreTests` gain their rows |

## Public API

```csharp
public readonly struct RunEconomy
{
    public RunEconomy(int essence, float veilrot, int rerollsBought, int rerollsSpent, bool claimed = false);
    public bool Claimed { get; }
}

public sealed class Veilrot
{
    internal void Restore(float value, bool claimed);   // the one-argument form stays and calls this with false
}
```

## Behaviour

1. **The latch is saved.** `RunRecorder.Take` writes `RunState.IsClaimed` into `RunEconomy.Claimed`.
2. **`Restore(value, claimed)` latches when `claimed || value >= Max`**, silently, with `ClaimedFor`
   at zero — M6-04 rule 9's stated cost (a Continue restarts the clock) is unchanged and still stated.
3. **A Claimed run whose meter fell below 100 resumes Claimed**, and its drain runs from the first
   tick.
4. **An unclaimed run is unaffected**, and a v4 file with no `claimed` field reads as `false`.
5. **`RunSnapshot.CurrentVersion` stays 4** if merged before the tag (the re-cut); otherwise 5 with the
   migration step above, and the M6-11 checklist row *"each was bumped exactly once"* is annotated.

## Tests

| Test | Given / When / Then |
|---|---|
| `Resume_AClaimedRunSpentBelowAHundredStaysClaimed` | an Emberwright Claimed at 100, two paid casts to 90, a boundary / recorder → fresh session / `IsClaimed`, meter 90, max HP falling after one second |
| `Resume_AClaimedRunCleansedStaysClaimed` | Claimed, Cleanse to 85, a boundary / resume / `IsClaimed` |
| `Resume_AnUnclaimedRunAtNinetyIsNotClaimed` | 90, never Claimed / resume / not Claimed |
| `Restore_LatchesOnTheFlagOrOnAHundred` | `(40, true)`, `(100, false)`, `(99, false)` / `Restore` / Claimed, Claimed, not |
| `Economy_ClaimedDefaultsFalse` | the four-argument constructor / — / `Claimed` false |
| `Store_RoundTripsClaimed` | a snapshot with `Claimed` true / save, load / true |
| `Store_AV4FileWithoutTheFieldReadsFalse` | the fixture v4 JSON / load / `Claimed` false, veilrot as written |

## Out of scope

- **Pinning the meter at 100 once Claimed** — the other fix, refused: it reverses M6-04's
  `Claiming_SurvivesCleansing` and M6-07c's paid cast, both decided rather than accidental.
- **Saving `ClaimedFor`** — M6-04 rule 9's reset clock is a stated cost, not this defect.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._

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

**Rules 1–5 as written, as a v4 re-cut.** `m6` was untagged, so `RunSnapshot.CurrentVersion` stays
**4** with no migration step (rule 5), and M6-11's *"each was bumped exactly once"* row stays true
unannotated. `RunEconomy` gained `bool Claimed`, optional and last; `RunRecorder.Take` writes
`state.IsClaimed`; `RunSession.Start` passes it on. `Veilrot.Restore(value, claimed)` settles the
thresholds first, then calls `BeginClaiming` for a flag below 100 — at 100 `ApplyStates` has already
closed the latch, and `!_isClaimed` stops a second set of three modifiers. Silent, `ClaimedFor` zero.

**Five deviations.**

*1. No one-argument `Restore` beside the new one.* The Public API kept it, forwarding `false`. It
would have had no production caller, and it is exactly the call that brings the defect back — a
restore that forgets the latch. Either way, four reflective helpers had to change:
`GetMethod("Restore")` over two overloads throws `AmbiguousMatchException`. So one method with
`claimed` required, and the helpers pass `false` — `VeilrotTests`, and outside the table
`PaidCastTests`, `OrdealEffectsTests` and `ClassVeilrotTests`, one token each.

*2. The key sits after `rerollsSpent`, not after the lists.* It is `RunEconomy`'s fifth field, a
bug report wants it beside `veilrot`, and JsonUtility reads by key, so position costs no older file
anything. `V4Run` kept every character and is now the pre-re-cut document
`Store_AV4FileWithoutTheFieldReadsFalse` reads. A new literal, `V4RunClaimed` — `"claimed":true`
beside a meter of 42.5 — is what the build writes; `Fixture_V4Run_IsWhatThisBuildWrites` and
`…_DecodesToTheExpectedSnapshot` were re-pointed to it.

*3. Two statements of the bug as design were corrected outside the table.* `RunState.IsClaimed`'s
remarks said a resumed run *"comes back Claimed because its saved meter reads 100, which is why v4
needed no field for it"*; AR §18.1's restore row named only *"a meter restored at exactly 100"*.
One comment and one doc clause; no code outside the table moved.

*4. The paid-cast row takes four frames, not two.* The spec's two paid casts to 90 hold. The first
draft ticked twice and saw one, because `SkillRunner` casts at most one skill a frame, bought or
free (M3-06 rule 6). The class is the fixture's own id wearing CH §3.3's `VeilrotSpec` — the
relationship is all a paid cast needs — through a `veilrot` parameter on `Oathbound` and
`BuildWithActiveTree`.

*5. The Cleanse row's boundary is the run's own.* A Sanctum opens after the clear-edge write, so a
Cleanse reaches disk at the *next* clear: the row clears stage 4 Claimed, buys the Cleanse, crosses,
clears stage 5, and resumes what stage 5's edge wrote — stage 6, 85, Claimed — two clears inside
the hundred-second drain. The spent row calls `RunRecorder.Take` directly, `Resume_APactSurvivesAKill`'s
route, so both write paths are covered.

**Rules ↔ rows.** 1: both `Resume_AClaimed…` rows assert `written.Economy.Claimed`, and
`Resume_AnUnclaimedRunAtNinetyIsNotClaimed` the false case. 2: `Restore_LatchesOnTheFlagOrOnAHundred`
— (40, true), (100, false), (99, false), and (100, true) for the stacking the guard prevents.
3: `Resume_AClaimedRunSpentBelowAHundredStaysClaimed`, the maximum 112 → 110.88 after sixty frames.
4: `Economy_ClaimedDefaultsFalse`, `Store_AV4FileWithoutTheFieldReadsFalse`, and
`Store_RoundTripsClaimed` both ways — a dropped key reads false, a constant one true.
5: `Resume_DerivesOverflow` already pins 4.

**Red checks, on the Editor.** With `Restore` ignoring the flag: **3 149 / 3**, exactly the three
latch rows, the spent one failing with *"A quit is not a way out of GD §10.3's hundred seconds."*
With the recorder writing `false` and the mirror neither writing nor reading the key: **3 147 / 5** —
both session rows at `written.Economy.Claimed`, and the three store rows, the byte fixture reading
`"claimed":false`.

**Verified:** **3 152 EditMode / 0 / 0** (+7 on 3 145), then **PlayMode 26 / 0 / 0** on one pass. A
second EditMode pass *after* PlayMode ran **3 151 / 1** on
`Animator_AttackSpeedIsUnreachableFromAnEditorClock`, which reads the Editor clock PlayMode had
advanced — [M7 row 8](../ROADMAP.md#carry-forward-into-m7)'s, M6-11e's; nothing here touches it.
Console clean after the compile; after that pass, 16 errors and 30 warnings, M6-11a's count, each
from a passing negative-path row. Zero new analyzer warnings; `dotnet format` *Formatted 0 of 13*;
`TimeManager.asset` re-serialised and was reverted.

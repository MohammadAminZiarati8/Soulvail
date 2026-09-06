# M0-00 — Title in sentence case

**Size:** S | M · **Depends on:** — · **Branch:** `m0-00-slug`
**Design refs:** AR §n, ADR-nnnn, CC §n

## Goal

One sentence. What exists after this task that didn't before.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/…/X.cs` | Core | |
| `Tests/Core/…/XTests.cs` | Tests.Core | |

Only these files change. Anything else is a deviation and goes in the PR description.

## Public API

```csharp
// Signatures only. Names are binding; later specs reference them.
```

## Behaviour

1. Numbered rules. Each maps to at least one test below.
2. …

## Tests

| Test | Given / When / Then |
|---|---|
| `Method_Condition_Expectation` | … / … / … |

## Manual verification (device)

_Only if a Game-side change is visible._ Numbered steps, each with the expected observation.

## Acceptance

- [ ] All tests above green
- [ ] Zero errors, zero new analyzer warnings
- [ ] Manual steps verified by the owner (if any)
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Tempting things NOT to do here. This section keeps the branch at 1–5 files.

## As built

_Filled at merge. Deviations from the above, or "as specified"._

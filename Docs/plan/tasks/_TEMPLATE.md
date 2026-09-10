# M0-00 — Title in sentence case

**Size:** S | M · **Depends on:** — · **Branch:** `m0-00-slug`
**Design refs:** AR §n, ADR-nnnn, CC §n · **Ledger rows:** n, m — or "none"

## Goal

One sentence. What exists after this task that didn't before.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/…/X.cs` | Core | |
| `Tests/Core/…/XTests.cs` | Tests.Core | |
| *small edits* | | existing files that gain a member, a field or a registration — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *ripple* | | what a widened port or constructor forces: the fakes that implement it, the fixtures that construct it, the `RunScope` field that registers it |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Signatures only. Names are binding; later specs reference them.
// A sketch, not a compile: MonoBehaviours use block namespaces (Traps §5), and anything
// `internal` on RunState is reached through a narrow read, never the handle (AR §18.2).
```

## Behaviour

1. Numbered rules. Each maps to at least one test below.
2. …

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Method_Condition_Expectation` | … / … / … |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row. They belong in the test file and are not deviations.

## Manual verification (Editor / device)

_Only if a Game-side change is visible._ Numbered steps, each with the expected observation, each tagged **[Editor]** (a probe, or the owner's Play) or **[device]** (deferred until a phone exists — PROGRESS lists them).

## Out of scope

- Tempting things NOT to do here. This section keeps the branch at 1–5 files.

## As built

_Filled at merge. Deviations from the above with their reasons, or "as specified". This footer owns the deviations; the PROGRESS entry only counts them and links here._

---

_Spec self-check, before a spec is called finished — delete this block from the copy:_
- [ ] Every § reference resolves to a heading that exists.
- [ ] Every member Behaviour names is in the Public API block, with the same signature.
- [ ] Every Behaviour rule has at least one Tests row, and no row tests a rule that isn't written.
- [ ] Every [ledger](../ROADMAP.md#carry-forward-into-m2) row naming this task is answered in Behaviour or Out of scope.

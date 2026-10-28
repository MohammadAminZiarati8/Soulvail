# Soulvail — project notes for Claude

Unity 6.3 (`6000.3.23f1`) · URP · Input System · **Android, portrait** · top-down auto-aim action roguelite.
Solo project. The owner directs the game and playtests; Claude is the engineer. **A task is built only when the owner names it and says go** — answering a clarifying question is not a go-ahead.

## Read budget

Every byte read stays in the session and is paid again on every turn. At session start read **only**:
[PROGRESS](Docs/plan/PROGRESS.md), the named task's spec, and [Invariants](Docs/Invariants.md) if the task touches code.
Everything else is opened **by section, when needed** (`grep -n '^#'` the file, then read the range):

- [Docs/Workflow.md](Docs/Workflow.md) — git and commit rules, Editor verification, lint, asset naming. Read §1 before committing, §2 before verifying.
- [Docs/Architecture.md](Docs/Architecture.md) — the full architecture; only when a task changes a system's shape.
- [Docs/Traps.md](Docs/Traps.md) — toolchain lies (Unity, MCP, VContainer, Input System, shell probes). Read the matching section before debugging a probe that says "no", or believing one that says "yes".
- [Docs/GameDesign.md](Docs/GameDesign.md) · [Docs/Characters.md](Docs/Characters.md) · [Docs/CoreCombat.md](Docs/CoreCombat.md) · [Docs/adr/](Docs/adr/) — design and decisions; only the section a spec cites.
- [Docs/plan/ROADMAP.md](Docs/plan/ROADMAP.md) — milestones → tasks; [Docs/plan/tasks/](Docs/plan/tasks/) — one spec per task, `_TEMPLATE.md` is the shape.

Report test results as counts plus failing names; never paste whole logs. Prefer `read_page`/text over screenshots.

## Session protocol

1. Build exactly the spec's Files table. Behaviour rules ↔ tests, one to one. Nothing outside the table without saying so.
2. Before handing over: tests green, zero errors, zero new analyzer warnings, `dotnet format` clean, manual steps listed.
3. In the same change: fill the spec's *As built* footer (≤ 1 500 bytes), overwrite PROGRESS's rows (≤ 6 000 bytes, never append history), tick the ROADMAP box.
4. Commit and push to `dev` per [Workflow §1](Docs/Workflow.md#1-git) (`ALLOW_DIRECT_COMMIT=1`, subject ≤ 72 chars counted, **no `Co-Authored-By`**).
5. Report what changed. **Anything the owner is meant to copy goes in a fenced code block** — commands, tag messages (labelled as such), and the prompt for the next task.
6. A task is up to 8 files ([ROADMAP › How to read this](Docs/plan/ROADMAP.md#how-to-read-this)); split before starting, never after.

## Architecture in five lines

1. Hexagonal. `Soulvail.Core` is pure C# (`noEngineReferences`) and owns **all** game logic. `Soulvail.Game` is Unity: adapters, views, VContainer scopes. `Game → Core`, never the reverse.
2. **Core is the brain; Unity is the body and the senses.** Unity reports facts, core decides, Unity executes intents and renders events.
3. Commands and facts in; a per-frame `Tick(snapshot)` (`dt` on the snapshot); events and intents out.
4. **Nothing is static.** VContainer `BootScope` → `RunScope`. No singletons, no service locator, no global bus.
5. Every gameplay number is a `Stat`; every content reference a `ContentId`; every user string a `LocKey`; `IRandom` has named streams; save DTOs are versioned with migration tests.

## Code conventions

- Namespaces mirror folders. Private fields `_camelCase`. `[SerializeField] private`, never public fields. One class per file. `.editorconfig` enforces.
- **File-scoped namespaces, except every `MonoBehaviour`/`ScriptableObject`, which uses a block namespace** — otherwise assets referencing it load as null silently ([Traps §5](Docs/Traps.md)).
- **Every asmdef needs a `csc.rsp` with `-langversion:10` beside it.**
- Core: constructor injection, `System.Numerics`, no `UnityEngine`, no allocations in `Tick` paths (`AllocationAssert.None`, never raw `GC` probes).
- Game: `[Inject]`; views are dumb — read intents, render events.
- Banned: statics/singletons/service locator, static event bus, `FindObjectOfType`, `GetComponent` in `Update`, `Resources.Load`, `UnityEngine.Random`/`Time` in core, string-keyed blackboards, `switch (effect.Type)`, enum ordinals as content identity, raw UI strings, LINQ in hot paths.

## Ask before

- Creating files beyond what a request explicitly covers.
- Deleting or moving assets, changing `ProjectSettings/`, adding or removing packages (VContainer 1.19.0 and `com.unity.pipeline` 0.7.0-exp.1 are in; nothing else is approved).
- Anything touching the remote other than pushing `dev`.

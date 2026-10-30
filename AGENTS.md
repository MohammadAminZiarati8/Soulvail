# Soulvail — notes for coding agents (Codex and others)

**[CLAUDE.md](CLAUDE.md) is your instruction file. Read it first and follow it exactly.** Wherever it
says "Claude", it means you. It holds the read budget, session protocol, architecture, code
conventions and the ask-before list; none of it is repeated here. This file adds only what an agent
outside Claude Code cannot otherwise see.

## Working beside other agents

- Claude and Codex both build tasks on this repo, one task at a time. Run `git status` and
  `git log --oneline -5` before starting; if PROGRESS's *Next task* is already ticked in ROADMAP,
  stop and say so.
- Commit **only your task's files** (`git add <paths>`, never `git add -A`). Leave changes you didn't
  make alone and mention them.
- If `git push` is rejected, `git pull --rebase origin dev`, re-run your tests if anything you touched
  moved, then push. Never force-push.

## Reaching the Unity Editor

The owner keeps the Editor open on this project. **You drive it yourself, through MCP and the CLI.
Never click in its window or use Computer Use.** Never ask the owner to run tests for you.

**MCP: server `unity_mcp`** (Unity's relay, `~/.unity/relay/relay_win.exe --mcp`, already in
`~/.codex/config.toml`). Its tools:

| Tool | Use |
|---|---|
| `mcp__unity_mcp__Unity_RunCommand` | Compile and run a C# snippet in the Editor: import assets, edit importers/prefabs/scenes, submit tests |
| `mcp__unity_mcp__Unity_GetConsoleLogs` | Compiler errors and warnings after a change (`logTypes: "Error,Warning"`) |
| `mcp__unity_mcp__Unity_Camera_Capture`, `..._SceneView_*` | Pictures, only when a task needs one |

A `RunCommand` snippet is `internal class CommandScript : IRunCommand { public void Execute(ExecutionResult result) { ... } }`
with fully qualified names and output only through `result.Log`. Read
[Traps §4](Docs/Traps.md#4-the-unity-mcp) before your first one: it lists what gets refused and the
namespaces that shadow names.

**The test loop, every task:**

1. `RunCommand`: `UnityEditor.AssetDatabase.Refresh();` (picks up your files and compiles).
2. `RunCommand`: probe a new type, e.g. `System.Type.GetType("Soulvail.Tests.Core.Ai.FooTests, Soulvail.Tests.Core")`.
   If it is null, the Editor has not compiled your code yet. `GetConsoleLogs` shows why.
3. `RunCommand` (a separate call, never in the same one as a refresh): create a `TestRunnerApi`, set
   `hideFlags = HideAndDontSave`, keep it in a `static` field, and `Execute` with
   `new Filter { testMode = TestMode.EditMode }`.
4. Wait about 90 s, then read `%USERPROFILE%/AppData/LocalLow/Soulvail/Soulvail/TestResults.xml`
   from the shell. Check that its `start-time` (UTC) is after your submit, then report
   `total/passed/failed/inconclusive` and the names of any failed `test-case`.

**CLI: `%LOCALAPPDATA%/Unity/bin/unity.exe`.** `unity status` must show the project as `ready`.
Editor calls go through `unity command <name>` (`unity command` alone lists them). Useful ones:

- `unity command editor_focus` raises the window. An unfocused Editor stops ticking
  ([Traps §3](Docs/Traps.md#3-the-unfocused-editor)), so try this first when MCP calls hang.
- `unity command recompile` forces a compile.
- `unity command console_status` reports the compile-failure flag and the Console counts.

**Never `unity command run_tests`**: it wedges the Editor's pipeline server. Never `package_add` or
`package_remove`, because packages are the owner's call (CLAUDE.md › Ask before).

If neither route answers, stop and tell the owner that the Editor is unreachable. Report "EditMode
suite not run" in the report and the *As built* footer. **Never claim a pass you did not see.**

## Tooling

- `dotnet format whitespace --folder --verify-no-changes --include <files>` is the format gate.
- Blender 4.1: `C:/Program Files/Blender Foundation/Blender 4.1/blender.exe`, headless, run with
  `PYTHONHASHSEED=0` (see each script's docstring under `Tools/Blender/`).
- Shell is Git Bash on Windows; `python` is not installed (Blender has its own). `gh` is not
  installed.
- Hooks in `.githooks/` reject: commits to `dev` without `ALLOW_DIRECT_COMMIT=1`, a subject over 72
  characters, any `Co-Authored-By` trailer, and staged `ProjectSettings/` changes. Revert
  `ProjectSettings/TimeManager.asset` before every commit.

## Standing rulings from the owner

These were given in conversation and live nowhere else in the repo.

- **Roles.** The owner decides what the game *is* (feel, fantasy, content). How it is built, tuned,
  tested and sequenced is yours: decide, give the reason in a sentence, carry on. Give firm
  recommendations, not option menus.
- **The design docs are agent drafts.** `GameDesign.md`, `Characters.md` and `CoreCombat.md` were
  written by Claude, not the owner. Resolve their ambiguities yourself, record the reading in the
  spec; never ask the owner what a doc meant.
- **Focus build: switches, never removals.** Veilrot, the Sanctum, the Focus disc and every class
  but the Ranger are switched off (`ModeDefinition._hasVeilrot` / `_hasSanctum`, `BootScope._classSelect`,
  `FocusGlowView._show`). Never delete them as unused.
- **Campaign = choose a place.** Each place (the Jungle first) is a `ModeDefinition` with its own
  stages and enemies; this replaces GD §3's layered descent.
- **Levelling:** a level banks a skill point; the tree screen pauses and the player picks freely.
  This replaces CH §5.1's random three-card offer.
- **Art:** KayKit packs plus generated models on KayKit rigs; weapons are separate props. Audio
  source is undecided. Player cyan; no red-orange, violet or gold on player characters.
- **UI:** Clash of Clans / Clash Royale style, within the colour law: health stays cyan, no red or
  gold buttons, frames are stone or bone.
- **Each class gets its own Space move** (Ranger roll, a future Knight's shield, a future
  Necrophos's ethereal form). The slot is dash-only today.
- **Open, don't decide:** whether a weapon belongs to the class or is swappable. Camera: the endgame
  is a fixed whole-arena view; leave `FollowCamera` alone until asked.
- **Pull requests** only when asked; the title is the branch name with hyphens as spaces and the
  first letter capitalised.

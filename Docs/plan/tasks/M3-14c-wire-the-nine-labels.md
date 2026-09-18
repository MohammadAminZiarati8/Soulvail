# M3-14c — Wire the nine labels: the keys M3-14a authored, M3-14b found, and nothing resolves

**Size:** S (one new test file; every other change is additive — [sizing rule](../ROADMAP.md#how-to-read-this)) · **Depends on:** M3-14a (the port, the adapter and the nine rows), M3-14b (the finding), M3-09a (`Pause.prefab`), M3-09b (`Skills.prefab`), M3-09d (`TreeView.prefab`), M3-08b (`LevelUp.prefab`) · **Branch:** `m3-14c-wire-the-nine-labels`
**Design refs:** GD §5.2, §7.3, §13.1; CC §6.2, §6.3; CH §5.1; AR §6, §7, §11.5; ADR-0012 · **Ledger rows:** **none.** Row 9 closed at M3-14b and does not reopen — its seventh reader was ruled art, and these nine were never on it: they are prefab text nobody had counted as a reader at all. Said here so a reader of the ledger does not go looking for the row that owns this task.

## Goal

The four screens a paused player actually touches stop drawing their own keys: `Resume`, `Skills`, `View Tree`, `Quit`, two `Close`s, `Cancel`, `All four slots are full. Which goes back to Auto?` and a second `View Tree` — nine labels, nine keys that already have rows, and a test that can tell if a tenth is ever authored and forgotten.

## The finding, and why it is this task rather than M3-14b's

M3-14b's sweeps are green on all nine **and correctly so**: rule 4 asks whether a key an asset carries has a row in `English.asset`, and every one of them does. The gap is that nothing ever reads the row. The eleven raw keys on the four prefabs are, exactly:

| Prefab | Keys authored as `m_text` | Wired? |
|---|---|---|
| `Pause.prefab` | `ui.pause.resume` · `ui.pause.skills` · `ui.pause.tree` · `ui.pause.quit` | no — four |
| `Skills.prefab` | `ui.skills.close` · `ui.skills.cancel` · `ui.skills.slotsFull` | no — three |
| `Skills.prefab` | `ui.skills.empty` | **yes**, since M3-09b |
| `TreeView.prefab` | `ui.tree.close` | no — one |
| `TreeView.prefab` | `ui.tree.none` | **yes**, since M3-09d |
| `LevelUp.prefab` | `ui.levelup.tree` | no — one |

**The two wired ones are the shape to copy, not a bug to fix.** `SkillsPresenter.EmptyKey` (`:89`, field `:98`, resolve `:466`) and `TreeViewPresenter.NoTreeKey` (`:111`, field `:132`, resolve `:630`) both keep the key as the prefab's placeholder text and overwrite it at runtime — so an undressed field shows up in the Editor as a key rather than as a blank label, which is the whole point of authoring it. **Nothing in this task rewrites a `m_text` value.** It is also why *"no prefab draws a raw key"* is not an assertion this project can make, and rule 6 is the assertion it can make instead.

The visible consequence, which PROGRESS names and a playtest should not be where it is found: **the tree view draws twelve English nodes under a Close button reading `ui.tree.close`.**

## Sizing: one task — the guess was two, and the owner ruled one

The owner's guess when commissioning this spec was **two** — Pause (a presenter gains a dependency) and the other three (a presenter that already has one gains a label) — with the split declared before starting, and the spec was asked to disagree if it disagreed. It did, on the arithmetic below; **the owner then ruled one at spec review.** The tripwire stands anyway, because it is about what implementation discovers rather than about what sizing predicted.

- **Counted files: one.** The sizing rule counts *new code files and substantial rewrites*; *"small additive edits to existing files (a new field on a spec, a registration, an event struct) are listed in the spec but don't count."* This task adds **no new production file** and rewrites none. Every presenter change is a static key, a serialized field, and a line — including `PausePresenter`'s, whose `Construct` gains a fifth parameter next to four it already null-guards. The one counted file is the sweep, `StaticLabelWiringTests.cs`.
- **The milestone's own precedent is larger, in one task, twice.** [M3-13a](M3-13a-palette.md) was **S** while touching nine readers, six prefabs and four test files. [M3-14a](M3-14a-localizer-and-english-table.md) was **M** while touching four new files, *six* readers plus their `Construct` signatures and their fixtures, two screens, `Menu.unity`, `Hud.prefab` and `BootScope.prefab` — roughly twenty files, one PR. This task touches thirteen and adds one.
- **The strongest test in the task cannot be written in either half.** Rule 6 sweeps all four prefabs at once and asks whether every authored key is claimed by a field. Split, it either lives in the first PR and is red until the second lands, or lives in the second and the first ships unguarded.
- **It is one review, because it is one question**: does every static label on the four screens resolve? A reviewer answering that across two PRs a day apart is doing the same work twice.

**The tripwire, pre-declared rather than discovered.** The split is `M3-14c-i` (**Pause** — the presenter that gains a dependency: `PausePresenter.cs`, `Pause.prefab`, `PausePresenterTests.cs`) and `M3-14c-ii` (**the other five** plus the sweep), and it is taken **before** continuing, never after, if any one of these turns out to be true:

1. `PausePresenter` gaining `ILocalizer` forces an edit to `Game/Composition/RunScope.cs` or to `Scenes/Run.unity` — i.e. the registration is not the no-op rule 2 claims it is.
2. Dressing a prefab reference forces a re-serialize of `Run.unity` (it should not: all nine references are prefab-internal).
3. Any presenter's write cannot live in `Start` and needs a change to its draw path — rule 3 and rule 4 are where that would show up first.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/Game/Authoring/StaticLabelWiringTests.cs` | Tests.Game | the sweep: every `ui.*` key authored on the four prefabs is claimed by a serialized `TMP_Text` field on that prefab's presenter, and the sweep says so about all four (rules 6, 7). Beside M3-14b's two sweeps because it is the same family — an `AssetDatabase` walk at author time |
| *small edits* | | `Game/Presentation/PausePresenter.cs` — four keys, four `TMP_Text` fields, `ILocalizer` as `Construct`'s fifth parameter, a `Write` helper, four calls in `Start` (rules 1–3, 5); `Game/Presentation/SkillsPresenter.cs` — three keys, three fields, a `Write`, three calls (rules 1, 3–5); `Game/Presentation/TreeViewPresenter.cs` — one key, one field, one call through its existing `Resolve` (rules 1, 3); `Game/Presentation/LevelUpPresenter.cs` — one key, one field, a `Write`, one call (rules 1, 3, 4) |
| *ripple* | | the four prefabs gain nine serialized references and **change nothing else** — `Prefabs/UI/Pause.prefab`, `Skills.prefab`, `TreeView.prefab`, `LevelUp.prefab`; `Tests/Game/Presentation/PausePresenterTests.cs`'s **five** `Construct` call sites (`:791`, `:795`, `:799`, `:803`, `:957`) gain the localizer, and its null-argument row gains a fifth case; `SkillsPresenterTests`, `TreeViewPresenterTests` and `LevelUpPresenterTests` gain their pinning rows |

Only these files change. Anything else is a deviation: say so in *As built*.

**No new `LocKey`, no new row in `English.asset`, and no new asset.** All nine rows exist (`English.asset:124–140`) and carry the words rule 8 pins. A task that had to author a word would be M3-14a's, not this one.

## Public API

```csharp
// Signatures only. All four are MonoBehaviours and keep their block namespaces (Traps §5).

// ── PausePresenter — the only one of the four that does not have the port ──────────────
private static readonly LocKey ResumeKey = new LocKey("ui.pause.resume");
private static readonly LocKey SkillsKey = new LocKey("ui.pause.skills");
private static readonly LocKey TreeKey   = new LocKey("ui.pause.tree");
private static readonly LocKey QuitKey   = new LocKey("ui.pause.quit");

[SerializeField] private TMP_Text _resumeLabel;
[SerializeField] private TMP_Text _skillsLabel;
[SerializeField] private TMP_Text _viewTreeLabel;
[SerializeField] private TMP_Text _quitLabel;

[Inject]
public void Construct(
    IRunSession session,
    RunPause pause,
    DomainEventHub hub,
    SceneLoader loader,
    ILocalizer localizer);            // fifth and last, null-guarded like the other four

private void Write(TMP_Text label, LocKey key);   // MenuPresenter.Write's semantics, copied

// ── SkillsPresenter — has the port since M3-14a ────────────────────────────────────────
private static readonly LocKey CloseKey     = new LocKey("ui.skills.close");
private static readonly LocKey CancelKey    = new LocKey("ui.skills.cancel");
private static readonly LocKey SlotsFullKey = new LocKey("ui.skills.slotsFull");

[SerializeField] private TMP_Text _closeLabel;
[SerializeField] private TMP_Text _promptCancelLabel;   // under _prompt, inactive at Start
[SerializeField] private TMP_Text _slotsFullLabel;      // under _prompt, inactive at Start

private void Write(TMP_Text label, LocKey key);

// ── TreeViewPresenter — has the port, and already has the helper ───────────────────────
private static readonly LocKey CloseKey = new LocKey("ui.tree.close");

[SerializeField] private TMP_Text _closeLabel;
// No Write: TreeViewPresenter.Resolve(LocKey) (:630) is the same method with the other
// half inlined, and a second one beside it would be the drift rule 5 is about.

// ── LevelUpPresenter — has the port ────────────────────────────────────────────────────
private static readonly LocKey ViewTreeKey = new LocKey("ui.levelup.tree");

[SerializeField] private TMP_Text _viewTreeLabel;

private void Write(TMP_Text label, LocKey key);
```

**Nine `private static readonly LocKey`s, and no `Docs/Architecture.md` edit is owed for them.** [AR §7](../../Architecture.md)'s sanctioned-static row bans the shared *surface* and explicitly permits *"a handful of types \[holding] a `private static readonly` constant of their own"*, naming `FirstActiveHint.HintKey` and `OverflowToast.ToastKey` among them. **That list is already non-exhaustive as of M3-14a** — `MenuPresenter`'s three, `SkillsPresenter.EmptyKey` and `TreeViewPresenter.NoTreeKey` are all of this kind and none is listed — so the row sanctions the kind rather than enumerating the members, and adding nine changes nothing it says. Checked rather than assumed, because a spec that adds nine statics and says nothing about §7 is the one a reviewer has to stop on.

## Behaviour

1. **Each of the nine labels gains a named serialized field, a key beside it, and a write — and the prefab's `m_text` stays exactly as authored.** Named `TMP_Text` fields rather than one array per screen, `MenuPresenter`'s shape: an array makes "which of the four is undressed" an index rather than a name, on the screen whose `Start` already throws a `MissingReferenceException` naming its parts. The key stays on the prefab because that is what `ui.skills.empty` and `ui.tree.none` have done since M3-09b, and it is what makes an undressed field visible in the Editor instead of blank.
2. **`PausePresenter` gains `ILocalizer` and nothing else changes to get it there.** Fifth parameter on `Construct`, after `SceneLoader`, null-guarded exactly like `session`, `pause`, `hub` and `loader`. **`RunScope` is not edited and must not need to be**: it already does `builder.RegisterComponent(_pausePresenter)` (`RunScope.cs:361`), and `ILocalizer` is a `BootScope` singleton (`BootInstaller.cs:269`) that `SkillsPresenter`, `TreeViewPresenter` and `LevelUpPresenter` — all three registered in the same `RunScope` — already resolve through the parent container. If that turns out to be false it is tripwire 1 and the task splits.
3. **All nine are written once, in each presenter's existing `Start`, and never again.** These are *static* labels: unlike `ui.skills.empty` and `ui.tree.none`, whose **visibility** is a function of run state and which therefore belong in `Draw`, none of these nine ever changes while the app is running. `Start` rather than `Construct`, because that is the earliest moment every `Awake` in the scene is guaranteed to have run — `PausePresenter.Start`'s own stated reason for putting its "was never injected" check there. `Start` rather than `OnEnable` (`MenuPresenter`'s place), because all four of these screens hide by `CanvasGroup` alpha rather than by deactivation, so `OnEnable` fires exactly once anyway and `Start` is the one ordered after injection. The consequence is the honest one and is named in *Out of scope*: a language changed at runtime would not reach these nine, and that is M6-10's problem.
4. **Four of the nine sit under objects that are inactive when `Start` runs, and the claim that a write still lands is a row rather than an assumption.** `ui.skills.cancel` and `ui.skills.slotsFull` live under `SkillsPresenter._prompt`, which is `SetActive(false)` until CC §6.2's question is asked; `ui.pause.tree` and `ui.levelup.tree` sit on buttons `RefreshPanel` / `RefreshTreeButton` switch off for a class with no tree. Writing `TMP_Text.text` on a component whose `GameObject` is inactive is expected to be honoured on activation — **but this is exactly the family Traps §1 is about** (an API that accepts a value and echoes it back has not agreed to honour it), so it is asserted after the object is switched on, which is the only state the player can see. **If it does not hold, the fallback is to write those four where the object is shown** (`ShowPrompt`, `RefreshPanel`, `RefreshTreeButton`) and to say so in *As built* — a deviation from rule 3, decided by a red row rather than by taste.
5. **A missing label is silent; a missing localizer falls back to the key.** `MenuPresenter.Write`'s exact semantics: `label == null` returns, and `_localizer is null ? key.ToString() : _localizer.Get(key)` — `ToString()` and not `.Key`, because a `default(LocKey)`'s `Key` is null. A button with no `Button` is a screen the player cannot use and throws; a button with no **label** is a screen the player can still use, and taking it down over a cosmetic gap is the worse failure. **The helper is copied into each file rather than hoisted**, which is M3-14a's choice and not this task's to revisit: a shared `LabelWriter` would be the first shared UI static in the project and would have to argue with AR §7's sanctioned-static row, over four lines.
6. **The assertion this project can make, and could not before today: every `ui.*` key authored as `m_text` on one of the four prefabs is claimed by a serialized `TMP_Text` field on that prefab's presenter.** *"No prefab draws a raw key"* is false by design — the two wired placeholders prove it — so the checkable form is that every key-carrying label is **pointed at**, which is what wiring means. It is a `SerializedObject` walk at author time, in `Tests.Game` beside M3-14b's sweeps, and it does two jobs: it fails the day someone authors a tenth key and forgets its field, and **it reads the nine references back after they are dressed**, which is the guard Traps §1's `SerializedProperty.objectReferenceValue` row (M1-07) says nine hand-dressed references need — an assignment that reports success and stores `None` leaves a label silently drawing its key, which is precisely the bug this task exists to remove.
7. **The sweep is anti-vacuous by construction.** Every assertion in rule 6 has the form *"nothing I found is unclaimed"*, which is trivially true of an empty set: a walk that stopped finding labels would turn the whole task green and silent. So the sweep also asserts it found at least one key-carrying label on **each** of the four prefabs, M3-14b's `EverySweep_FindsTheAssetsItIsMeantTo` and the same reason.
8. **Each of the nine is pinned to the English it draws today, against the shipped `English.asset` rather than a fixture table.** *"Resume"*, *"Skills"*, *"View Tree"*, *"Quit"*, *"Close"*, *"Cancel"*, *"All four slots are full. Which goes back to Auto?"*, *"View Tree"*, *"Close"*. M3-14a's `Card_DrawsEnglish` is the precedent and the owner's instruction is the standard: a row that pins the current string so that inverting it is a red row somebody has to argue with. **`ui.pause.tree` and `ui.levelup.tree` are two keys carrying one word and stay two keys** — one screen's button may grow a count or a hint the other's does not, and a merge M6-10 has to undo is more expensive than a duplicated row.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `EveryAuthoredKey_IsClaimedByAField` | the four prefabs / every `TMP_Text` whose `m_text` is a key `English.asset` answers / each is the target of a serialized `TMP_Text` field on that prefab's presenter — eleven labels, eleven fields, the two placeholders included (rule 6) |
| `TheSweep_FindsAKeyOnEveryScreen` | the same walk / — / at least one key-carrying label found on each of `Pause`, `Skills`, `TreeView` and `LevelUp`; a walk that found none is red rather than green (rule 7) |
| `Pause_Start_WritesItsFourLabels` | `Pause.prefab` + the shipped table / `Start` / *"Resume"*, *"Skills"*, *"View Tree"*, *"Quit"* — none of them a key (rules 1, 3, 8) |
| `Pause_TreeLabel_ReadsEnglishOnceTheButtonIsOffered` | a class with a tree, the View Tree button switched on by `RefreshPanel` / — / *"View Tree"*, written while the object was inactive (rule 4) |
| `Pause_Construct_NullLocalizer_Throws` | the other four dependencies / `Construct(…, null)` / `ArgumentNullException` naming `localizer` — the fifth case on the existing row (rule 2) |
| `Pause_ResolvesTheLocalizerFromTheRunScope` | `RunScope` installed with the pause presenter / resolve and `Start` / the four labels read English; **`RunScope.cs` and `Run.unity` are unchanged** — tripwire 1, as a row (rule 2) |
| `Pause_NoLocalizer_LeavesTheKeys` | the prefab, never injected / `Start` / the four labels still read their keys, no throw, nothing logged (rule 5) |
| `Pause_Update_DoesNotRewriteTheLabels` | after `Start`, each label overwritten with a sentinel / a frame of `Update` / the sentinel survives — the write is in `Start` and in no per-frame path (rule 3) |
| `Skills_Start_WritesTheCloseLabel` | `Skills.prefab` + the shipped table / `Start` / *"Close"* (rules 1, 3, 8) |
| `Skills_Prompt_ReadsEnglishWhenItIsAsked` | four manual slots full, a fifth flipped / the prompt shown / *"All four slots are full. Which goes back to Auto?"* and *"Cancel"*, both written while `_prompt` was inactive (rules 4, 8) |
| `Skills_NoLabelDressed_IsSilent` | the three fields cleared / `Start` and `Open` / no throw, the screen still draws its list (rule 5) |
| `Tree_Start_WritesTheCloseLabel` | `TreeView.prefab` + the shipped table / `Start` / *"Close"* — the button under M3-14a's twelve English nodes (rules 1, 3, 8) |
| `LevelUp_ViewTreeLabel_ReadsEnglishOnceTheButtonIsOffered` | a class with a tree, an offer shown / `RefreshTreeButton` / *"View Tree"* (rules 1, 3, 4, 8) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row. **This task adds no spec type, no public constructor and no `float` door** — `Pause_Construct_NullLocalizer_Throws` is listed rather than left implied only because it is the one signature change in the task, and a reviewer should see it named.

## Manual verification (Editor / device)

1. **[Editor]** Play a run, tap the pause icon: the panel reads **Resume · Skills · View Tree · Quit**. Before this task all four read their keys (rule 1).
2. **[Editor]** Pause → **Skills**: the button back reads **Close**. Set four skills to Manual and flip a fifth: the prompt reads **All four slots are full. Which goes back to Auto?** over a **Cancel** button (rule 4 — this is the step that proves the inactive-object write landed; if either still reads its key, take rule 4's fallback and say so in *As built*).
3. **[Editor]** Pause → **View Tree**: the button under the twelve English nodes reads **Close**. This is the screen PROGRESS names as the visible consequence (rule 1).
4. **[Editor]** Level up: the **View Tree** button on the offer screen reads English, not `ui.levelup.tree` (rule 4).
5. **[Editor]** `git diff --stat` over the four prefabs: **only the presenter component blocks change** — no `RectTransform`, no `Canvas`, no font, and the nine `m_text` values byte-identical. **`Scenes/Run.unity` must be untouched**; if it is not, that is tripwire 2 and the split is taken before continuing. M3-08b's still-unrun lead gets no sixth scene data point from this task.
6. **[Editor]** Full EditMode suite green against the **1 967 / 0 / 0** baseline (M3-14b), zero compile errors, zero new analyzer warnings, and `dotnet format whitespace --folder --verify-no-changes --include <the touched .cs files>` clean.
7. **[Editor]** Check `ProjectSettings/TimeManager.asset` before handover and revert it if Unity rewrote `Fixed Timestep` into its rational form. It is not this task's, the pre-commit hook refuses it, and it has been found on six of the last eight tasks.

**Toolchain, both now in [Traps §3](../../Traps.md#3-the-unfocused-editor) and both worth re-reading before starting:** the Editor must be **focused** or `RequestScriptCompilation` never drains while `AssetDatabase.Refresh` reports 0.008 s having done nothing — check `InternalEditorUtility.isApplicationActive` and `Library/ScriptAssemblies/*.dll` timestamps against source mtimes, because `isCompiling` reads `False` both while pending and when done. And the MCP refuses `TestRunnerApi.Execute` from a submitted `RunCommand` while `EditorApplication.delayCall` never fires unfocused, so the suite runs from a throwaway launcher under `Assets/Editor/` that is **deleted before handover** and named in *As built*.

## Out of scope

- **M3-10b rule 8's 24 dp auto-cast cells.** They hold neither a key nor a word — the seventh reader ledger row 9 ruled *art* rather than *text*, and no table can help. They stay on the [parking lot](../ROADMAP.md#parking-lot) until M7's art pass. **Ledger row 9 closed at M3-14b and does not reopen**, here or anywhere.
- **Changing any of the eleven authored `m_text` values.** Keeping the key as placeholder text is the pattern (rule 1), not the bug.
- **A shared `LabelWriter`.** Four copies of a four-line private method, deliberately (rule 5).
- **Re-writing a label when the table changes at runtime.** Rule 3 writes once in `Start`; a language switch is M6-10's, along with the rest of localisation.
- **`Boot.unity`'s splash string**, which `MenuPresenter`'s own remarks record as shared with `ui.app.title` and resolved by nothing. It is a scene this task does not open and a screen with no presenter; M6-10 or M8's polish.
- **Any new key, any new row in `English.asset`, any change to `TableLocalizer`, `ILocalizer` or `LocalizationTable`.** All nine rows exist. A task that authored a word would be M3-14a's.
- **The seven readers M3-14a already wired** — the menu's three, the death overlay's two, `ui.hint.firstActive`, `ui.overflow.granted` — and the two placeholders this task leaves exactly as they are.
- **A "no `TMP_Text` in the build carries authored English" sweep.** M3-14a's `Menu_AndDeathOverlayDrawFromTheTable` makes that claim about two scenes; making it about the whole project would have to rule on `0.0 s`, `skill.name`, `trigger.key`, `branch` and `II`, which are placeholders on pooled templates and on an icon glyph, and that is a ruling rather than a test.
- **The `FrameOrderTests` intermittency.** This task runs no PlayMode and adds no tally to it.

## As built

**One new test file, nine references dressed, fourteen new rows, 1 981 / 0 / 0 — and the arithmetic is exact.** Against M3-14b's 1 967, +14. The Tests table names 13 rows; **11 of them became new `[Test]` methods**, one was **folded into a row that already existed** (`Pause_Construct_NullLocalizer_Throws` is a fifth case on `Construct_RefusesNullDependencies` rather than a row of its own — a second method asserting one more null on a four-null row would be the drift M3-14b rule 11 is about), one was **dropped** (deviation 3), and **3 rows the table did not name were added** (deviation 4). 11 − 0 + 3 = 14. The two sweep rows are in `StaticLabelWiringTests`; the other twelve are spread across the four presenter fixtures.

**Three consecutive green full runs on the final code**, plus a deliberately-broken run in between (see *Manual verification* below). **Console on a passing run: 1 error and 24 warnings, every one of them a `LogAssert`-ed message from a pre-existing fixture** — `LocalJsonSaveStore`'s discard path, the `OnValidate` id warnings, `SkillAuthoringTests`' authoring warnings, `InstallerTests`' fallback-seed warning — and **zero traceable to any of the five fixtures this task touched**, which was the point of looking.

### Four deviations, and one finding about the sweep

1. **`LevelUp.prefab` and `TreeView.prefab` lost 17 lines of dead YAML, which manual step 5 forbids.** `PrefabUtility.SaveAsPrefabAsset` forced a re-serialize that finally flushed `OfferCard`'s four `_passive/_active/_upgrade/_keystone` lines (×3 cards) and `TreeNodeView`'s seven — **fields M3-13a deleted from the code and whose YAML Unity 6.3 had kept**, exactly as [ledger row 6](../ROADMAP.md#carry-forward-into-m3) records (*"Unity 6.3 keeps a removed field's YAML line… the stale lines are dead text the Inspector does not show"*). **Checked rather than assumed: neither `OfferCard.cs` nor `TreeNodeView.cs` declares any of those fields**, so nothing live was lost and no test moved. Kept rather than hand-restored — re-adding YAML for fields that do not exist would be worse than the deviation. **`Pause.prefab` and `Skills.prefab` are additions only**, and **`Scenes/Run.unity` is untouched**, so tripwire 2 did not fire and M3-08b's still-unrun scene lead gains no data point.
2. **Seven `Construct` call sites, not the five the spec counted.** `SkillsPresenterTests:1187` and `TreeViewPresenterTests:1218` each build a pause screen of their own to test the panel underneath. Both files were already in the *ripple* row for their own pinning rows, so this widened no Files table — but the spec's number was wrong and is corrected here rather than quietly.
3. **`Pause_ResolvesTheLocalizerFromTheRunScope` was dropped, because it already exists.** `TableLocalizerTests` (M3-14a's `Boot_LocalizerOutlivesARun`) resolves `ILocalizer` from a root and from a `RunScope` child and asserts they are the same instance. A copy in `PausePresenterTests` would be two tests of one rule that can disagree, which is rule 11's whole argument. **What the row was also going to assert — that `RunScope.cs` and `Run.unity` are unchanged — is a git fact, not a test**, and it is checked in manual step 5. Rule 2's claim held exactly: **`RunScope` was not edited.**
4. **Three `…IsSilent` rows the Tests table did not name.** The table gave rule 5 one row, on the Skills screen. Each of the other three presenters has its own `Write` with its own throw-or-shrug line one method up, and *"a missing label is silent"* is a claim about each of them — so `PausePresenterTests.NoLabelDressed_IsSilent`, `TreeViewPresenterTests.Start_NoCloseLabelDressed_IsSilent` and `LevelUpPresenterTests.Start_NoViewTreeLabelDressed_IsSilent` were added. Rule 5 now has four rows and four files, one to one.

**`Pause_NoLocalizer_LeavesTheKeys` is reached by clearing the field, not by skipping `Construct` — and the spec's framing for it was unreachable.** As written the row said *"the prefab, never injected"*, but `Construct` null-guards the localizer and `Start` throws outright when `_pause` is null, so an uninjected presenter never reaches a `Write` at all. The fallback path is still real — it is what a fixture that hand-builds the component gets — and `SetPrivate(_presenter, "_localizer", null)` is the only route to it. The row is named `NullLocalizer_LeavesTheKeys` and says so in a comment.

**The finding worth keeping is that the sweep's first run was red, and it was right to be.** `EveryAuthoredKey_IsClaimedByAField` threw `ArgumentException: '0.0 s' is not a valid localisation key` — **`LocKey`'s constructor refuses whitespace and empty strings**, and the four prefabs are full of authored text that trips it (`0.0 s` on every cooldown readout; the pause icon's `II` glyph is legal only by luck). The fix is `IsKeyTheTableAnswers`, which **catches rather than re-implementing the grammar**: text that cannot be a `LocKey` is not one, and asking the real constructor is the only way to find out without a second copy of a rule `ContentId` and `LocKey` already own. Worth naming because the next sweep over authored text will meet it on its first run too.

**One tidy-up inside an edited file, named because it is a change rather than an addition:** `SkillsPresenter.Draw` had `_localizer is null ? EmptyKey.ToString() : _localizer.Get(EmptyKey)` inline since M3-14a; it now goes through the new `Write`. Two resolve paths in one file is the drift the helper exists to prevent. Behaviour is identical and no existing row moved.

**`TreeViewPresenter` gained no `Write`**, as the Public API block specified — its existing `Resolve(LocKey)` is the same method with the other half inlined, and `Start` calls it behind a null check.

### Manual verification

**Steps 1–4 are the owner's** — they need Play, and this task was built against an Editor that was unfocused for its whole life (see below). What was checked here instead is the thing those steps would be checking *through*: the nine references are dressed in the YAML, and each screen's rows assert the English the table holds.

5. **[Editor] Prefab diffs:** `Pause.prefab` +4 lines, `Skills.prefab` +3, both additions to the presenter block only. `TreeView.prefab` and `LevelUp.prefab` carry deviation 1's deletions as well. **`Scenes/Run.unity` untouched; `Game/Composition/RunScope.cs` untouched.**
6. **[Editor] The suite: 1 981 / 0 / 0, three consecutive runs on the final code**, zero compile errors and zero analyzer warnings across all six assemblies, `dotnet format whitespace --folder --verify-no-changes` clean on all nine touched `.cs` files.
7. **[Editor] `ProjectSettings/TimeManager.asset` was rewritten once and reverted.** Unity 6.3 rewriting `Fixed Timestep` into its rational form, for the seventh time in nine tasks. `ProjectSettings/` is clean at handover.

**And the check the spec did not list but rule 7 is meaningless without: the sweep was made to fail.** `_quitLabel` was cleared on `Pause.prefab` and the suite re-run: **three rows red**, with `EveryAuthoredKey_IsClaimedByAField` naming the exact label — *"Assets/\_Project/Prefabs/UI/Pause.prefab: the label 'Pause/Panel/Quit/Label' draws 'ui.pause.quit', which is a key …English.asset answers, and no PausePresenter field points at it — so nothing resolves it and the player reads the key."* The other two were `Start_WritesTheFourPanelLabels` and `NullLocalizer_LeavesTheKeys`, both `NullReferenceException` on a field that is no longer dressed, which is the fixture reading a broken asset rather than a message worth having. **The reference was then restored and read back from a fresh load** — Traps §1's rule for `SerializedProperty.objectReferenceValue` (M1-07) — and `Pause.prefab` hashes identically to its pre-break state.

### Toolchain

**The Editor was unfocused for the entire task and never once needed to be focused**, which is a correction worth recording against Traps §3's table rather than a lucky run:

- **It drained every compile.** The new serialized fields were live in the Editor's own DLLs within seconds of each edit, confirmed by the strong probe (`SerializedObject.FindProperty` answering on the new names) rather than by a timestamp — M3-09c's finding, now seen a second time and in the other direction: §3's *"`RequestScriptCompilation` never drains"* row is about that API, not about compilation as such.
- **`AssetDatabase` imported a brand-new file unprompted** — `StaticLabelWiringTests.cs` had its `.meta` and was listed in `Library/Bee/artifacts/*.dag/Soulvail.Tests.Game.rsp` before anything asked it to be.
- **The offline Roslyn route caught nothing the suite would not have**, but cost seconds rather than two minutes: Unity's own `.rsp` files copied to `Temp/`, `-out`/`-refout` repointed, `dotnet <Editor>/Data/DotNetSdkRoslyn/csc.dll @scratch.rsp`. Four assemblies, real analyzers, zero errors.
- **`System.IO.File.Delete` is on the MCP's `k_UnsafeMethods` list**, which is not in Traps §4's enumeration and cost one refused command. `File.WriteAllText` to truncate is the way round it, as §4 already says for the others.

**One piece of scaffolding, created and deleted inside the branch:** `Assets/Editor/__M3_14c_Harness/RunTests.cs`, because the MCP refuses `TestRunnerApi.Execute` from a submitted `RunCommand` (Traps §4) and `EditorApplication.delayCall` never fires unfocused (§3). It held its `TestRunnerApi` in a **static** field with `hideFlags = HideAndDontSave`, which is Traps §7's rule — a runner left in a local is collected mid-run and `RunFinished` never fires. It lived in `Assembly-CSharp-Editor`, which no test assembly references and which contributed no tests; **it and `Assets/Editor/` are deleted, `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` is gone, and the project recompiles clean without it** — all three confirmed by probe. **All four suite runs were taken with it present**, said plainly rather than glossed.

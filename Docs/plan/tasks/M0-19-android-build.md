# M0-19 — Player settings, `AndroidBuild` script, APK on device

**Size:** S · **Depends on:** M0-13, M0-16, M0-17 · **Branch:** `m0-19-android-build`
**Design refs:** GD §11, §5.2; `CLAUDE.md` Unity section

## Goal

One menu item produces an installable development APK, and the walking skeleton runs on the owner's phone in landscape — the pipeline is proven before there's anything hard to debug.

## Files

| Path | Assembly | Purpose |
|---|---|---|
| `Assets/_Project/Editor/Build/AndroidBuild.cs` | Editor | Menu item + `BuildPipeline` wrapper |
| `ProjectSettings/ProjectSettings.asset` | — | *modified via the Editor (MCP or Inspector)*, values below |
| `ProjectSettings/EditorBuildSettings.asset` | — | *modified:* Boot, Menu, Run (already from M0-13; verify) |

Build output goes to `Builds/Android/` — already git-ignored.

## Public API

```csharp
namespace Soulvail.Editor.Build;

public static class AndroidBuild
{
    [MenuItem("Soulvail/Build/Android APK (Development)")]
    public static void BuildDevelopmentApk();      // → Builds/Android/Soulvail-dev.apk

    public static BuildReport Build(bool development, string outputPath);
}
```

## Player settings (applied once, values recorded here)

| Setting | Value |
|---|---|
| Company name | owner's (confirm) |
| Product name | `Soulvail` |
| Application identifier | `com.<owner>.soulvail` — **owner to confirm the exact id before this task runs**; it's permanent once published |
| Version / bundle version code | `0.1.0` / `1` |
| Default orientation | Landscape Left; auto-rotate **landscape left + right only** (portrait off) |
| Scripting backend | IL2CPP (already) |
| Target architectures | ARM64 only |
| Minimum API level | 25 (already) |
| Target API level | Automatic (highest installed) |
| Graphics APIs | Vulkan, OpenGLES3 (auto) |
| Active build target | Android (switch; triggers a reimport) |
| Quality | URP `Mobile_RPAsset` for the Android quality levels (verify the template's mapping) |

## Behaviour

1. `Build(development, path)`: scenes = enabled scenes from `EditorBuildSettings`; target `Android`; options `Development | AllowDebugging` when `development`; writes to `path`; logs the summary (result, size, time); throws if `result != Succeeded` so a failed build can't be mistaken for a success.
2. `BuildDevelopmentApk` calls `Build(true, "Builds/Android/Soulvail-dev.apk")`, creating the folder if needed.
3. The script does not change player settings — those are project state, not build-time toggles.

## Tests

None. The test is the phone.

## Manual verification (device)

1. Soulvail → Build → Android APK (Development) completes; Console shows `Succeeded`.
2. `adb install -r Builds/Android/Soulvail-dev.apk` (or copy and tap); launch.
3. App opens in **landscape**; rotating the phone upside-down flips to the other landscape; portrait never engages.
4. Boot label flashes → Menu → tap **Descend** → grey box with a cyan capsule.
5. Floating stick moves the capsule; recentering works; multi-touch works (hold stick, tap elsewhere).
6. Debug overlay visible (development build); `|v|` reads 5.40 at full stick.
7. Background the app, wait 10 s, foreground — still running, stick still works.

## Acceptance

- [ ] Steps 1–7 verified by the owner
- [ ] Zero errors, zero new analyzer warnings
- [ ] `ProjectSettings` diff reviewed in the PR (only the rows above changed)
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Release/signed builds, keystore, AAB — M8-06.
- Device tiering, frame-rate setting — M8-03/04. `targetFrameRate = 60` from `BootFlow` is enough for now.
- CI.

## As built

_Filled at merge._

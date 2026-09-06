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
| Application identifier | **Placeholder `com.soulvail.dev`** — decided later; permanent once uploaded to a store, so M8-06 changes it *before* the first upload |
| Version / bundle version code | `0.1.0` / `1` |
| Default orientation | Landscape Left; auto-rotate **landscape left + right only** (portrait off) |
| Scripting backend | IL2CPP (already) |
| Target architectures | **Development builds: ARM64 + x86-64** (BlueStacks runs x86-64 natively; its ARM translation is unreliable for IL2CPP). **Release builds: ARM64 only.** The build script sets this per build kind (rule 4). |
| Minimum API level | 25 (already) |
| Target API level | Automatic (highest installed) |
| Graphics APIs | Vulkan, OpenGLES3 (auto) |
| Active build target | Android (switch; triggers a reimport) |
| Quality | URP `Mobile_RPAsset` for the Android quality levels (verify the template's mapping) |

## Behaviour

1. `Build(development, path)`: scenes = enabled scenes from `EditorBuildSettings`; target `Android`; options `Development | AllowDebugging` when `development`; writes to `path`; logs the summary (result, size, time); throws if `result != Succeeded` so a failed build can't be mistaken for a success.
2. `BuildDevelopmentApk` calls `Build(true, "Builds/Android/Soulvail-dev.apk")`, creating the folder if needed.
3. The script does not change player settings — those are project state, not build-time toggles — **except** target architectures, which are a build-kind concern:
4. `Build(development: true, …)` sets `PlayerSettings.Android.targetArchitectures = ARM64 | X86_64` for the duration of the build and restores the previous value in a `finally`; `development: false` uses `ARM64` only. Release APKs never ship x86-64.

## Emulator setup (until a phone is available)

BlueStacks 5, one-time:
1. Instance: **Android 11 (or "Pie 64-bit")**, performance profile with ≥ 4 CPU / 4 GB.
2. Display: resolution **1920 × 1080**, orientation landscape, **DPI 240** — realistic dp math for the stick (`StickShaper.PixelsPerDp` reads `Screen.dpi`).
3. Settings → Advanced → **Android Debug Bridge: on**. Then `adb connect 127.0.0.1:5555` and `adb devices` shows the instance.
4. Install with `adb install -r Builds/Android/Soulvail-dev.apk`.

What the emulator can and cannot tell us: **can** — install, boot flow, scene loading, stick shaping at a known DPI, the debug overlay, background/foreground survival. **Cannot** — multi-touch (mouse is one finger), rotation, haptics, touch latency, real frame rate, thermal. Those stay tagged *device-only*.

## Tests

None. The test is the phone — or, for now, the emulator for what it can show.

## Manual verification

Emulator-verifiable now:
1. Soulvail → Build → Android APK (Development) completes; Console shows `Succeeded`; the APK contains `lib/arm64-v8a` and `lib/x86_64`.
2. `adb install -r Builds/Android/Soulvail-dev.apk`; launch from the BlueStacks home screen.
3. App opens in **landscape**; portrait never engages.
4. Boot label flashes → Menu → tap **Descend** → grey box with a cyan capsule.
5. Floating stick moves the capsule; recentering works.
6. Debug overlay visible (development build); `|v|` reads 5.40 at full stick.
7. Send the app to the background (BlueStacks home), wait 10 s, return — still running, stick still works.

Device-only (deferred, tracked in PROGRESS):
8. Rotating the phone upside-down flips to the other landscape.
9. Multi-touch: hold the stick, tap elsewhere.

## Acceptance

- [ ] Steps 1–7 verified by the owner on BlueStacks; 8–9 recorded as deferred
- [ ] Zero errors, zero new analyzer warnings
- [ ] `ProjectSettings` diff reviewed in the PR (only the rows above changed)
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Release/signed builds, keystore, AAB — M8-06.
- Device tiering, frame-rate setting — M8-03/04. `targetFrameRate = 60` from `BootFlow` is enough for now.
- CI.

## As built

_Filled at merge._

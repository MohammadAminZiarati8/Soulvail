# M0-20a — Make the APK actually run on BlueStacks

**Size:** S · **Depends on:** M0-19, M0-20 · **Branch:** `m0-20a-apk-runs`
**Design refs:** AR §16

## Goal

The walking skeleton runs outside the Editor for the first time: an APK launches on BlueStacks, reaches the Menu, and a tap on Descend moves the capsule around the grey box — so every claim M0 makes about feel stops being Editor-only.

## Why this exists

M0-20 installed `Builds/Android/Soulvail-dev.apk` on BlueStacks 5 and it closed instantly with no dialog. `adb logcat` localised it exactly, and **the cause is not what the reference-device plan assumed**:

```
F libc  : Fatal signal 11 (SIGSEGV), code 1 (SEGV_MAPERR), fault addr 0x40
          in tid 4190 (UnityGfxDeviceW), pid 4129 (om.soulvail.dev)
F DEBUG : ABI: 'x86_64'   Cause: null pointer dereference
F DEBUG : #00 /system/vendor/lib64/hw/vulkan.default.so (vk_common_SetDebugUtilsObjectNameEXT+259)
F DEBUG : #01 /system/lib64/libhoudini.so
F DEBUG : #02 /system/lib64/libhoudini.so
```

ARM64 translation **works**: `IL2CPP: JNI_OnLoad` and `Unity: Context Type: GameActivity` both appear before the crash, so houdini executed our arm64 code fine. What dies is Unity's graphics worker thread calling `vkSetDebugUtilsObjectNameEXT`, which crosses the ARM→x86 boundary through `libhoudini.so` into BlueStacks' own Vulkan driver, which null-derefs.

Two consequences shape this task:

1. **The crash cannot occur on real ARM hardware** — there is no houdini and no `vulkan.default.so` in the path. Nothing in `Soulvail.Core`, the scopes or the views is implicated, so this is a build-configuration task, not a bug fix.
2. **`vkSetDebugUtilsObjectNameEXT` is a development-build path.** Unity enables `VK_EXT_debug_utils` for object naming in development builds, which is why the very first APK the project ever produced is the one that trips it.

Android graphics APIs are currently `[Vulkan, OpenGLES3]` — `m_APIs: 150000000b000000`, `m_Automatic: 0`, Vulkan first.

## Approach — cheapest probe first

Do these **in order** and stop at the first one that gives a running app. Each is independently reversible.

1. **Release APK.** `AndroidBuild.Build(development: false, …)` already exists and is `public`; it has no menu item. Add one, build, install, launch. If the debug-utils naming is the whole story this is the entire fix and no project setting changes at all.
2. **`androidVulkanDenyFilterList`.** Present and empty in `ProjectSettings.asset`. Adding an entry matching the emulator makes Unity skip Vulkan **on that device only**, so real phones keep it. This is the option to take if a development APK is wanted on BlueStacks.
3. **GLES3 first in the graphics API list.** Works, and is the last resort: it downgrades *every* device, real phones included, to satisfy an emulator. Do not take this without saying so out loud in the PR.

## Files

| Path | Purpose |
|---|---|
| `Assets/_Project/Editor/Build/AndroidBuild.cs` | A `Soulvail/Build/Android APK (Release)` menu item over the existing `Build(development: false, …)` |
| `ProjectSettings/ProjectSettings.asset` | *only if* step 1 is insufficient — `androidVulkanDenyFilterList` entry, or graphics API order |
| `Docs/plan/PROGRESS.md` | Result, the logcat evidence, what it means for the reference-device plan |
| `Docs/plan/ROADMAP.md` | Box ticked |

**`ProjectSettings/` is on CLAUDE.md's *ask before* list.** Step 1 needs no permission; steps 2 and 3 need the owner's explicit go-ahead before the file is touched.

## Behaviour

1. The release menu item mirrors the development one exactly — scenes from `EditorBuildSettings`, architectures pinned and restored in a `finally`, a throw rather than a log on a non-`Succeeded` result — differing only in `development: false` and the output path.
2. The release APK goes to `Builds/Android/Soulvail.apk`, beside the dev one, never overwriting it. Both paths stay git-ignored under `/Builds/`.
3. No change to `Soulvail.Core` or `Soulvail.Game`. If this task ever needs one, it is the wrong task and the finding belongs in a new spec.

## Manual verification (owner, emulator)

1. Build via the new menu item; the Console reports `Release APK · Succeeded · … · Android ARM64`.
2. `adb install -r Builds/Android/Soulvail.apk` → `Success`.
3. Launch. **Expected:** black splash → Menu with the title and the Descend button. No instant close.
4. Tap **Descend** → the grey box, the capsule, the floating stick.
5. Drag in the left 45 % → the capsule moves; the debug overlay is absent (release build strips it).
6. Kill from recents, relaunch → clean start.
7. `adb logcat -d "*:E"` shows no `libc` fatal signal and no tombstone for `com.soulvail.dev`.

If step 3 still closes instantly, capture the new backtrace and go to approach 2 — **do not** start guessing at game code.

## Acceptance

- [ ] An APK launches on BlueStacks and reaches the Menu
- [ ] Descend starts a run and the capsule moves under the stick
- [ ] No fatal signal in `logcat` across a launch, a run, and a relaunch
- [ ] Zero errors, zero new analyzer warnings; EditMode and PlayMode suites still green
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- **Any change to game code.** The diagnosis exonerates it; a "fix" there would be cargo cult.
- Chasing the `OnBackInvokedCallback` `NoClassDefFoundError` in the log. It is logged at `I` level, Unity catches it, and execution continues well past it into graphics init. It is not the crash.
- Making the *development* APK work on BlueStacks, if the release one does. The dev build's value is the profiler and the debug overlay, and both are worth more on real hardware than on an emulator.
- Buying or configuring a real device. That is the parking-lot item this task exists to work around.

## As built

_Filled at merge._

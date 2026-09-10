using System;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>
/// One pulse of the phone's vibrator. The whole of what the game may ask a device to feel like.
/// </summary>
/// <remarks>
/// <para>
/// A port on the Unity side rather than in core, deliberately. Core decides what happened — a hit
/// landed, a Charge began — and haptics is one of the several things that may render that fact
/// (AR §3). Nothing in <c>Soulvail.Core</c> knows this interface exists, and nothing behind it can
/// be asked a question: a vibrator has no state anyone may read, so there is nothing here for a
/// gameplay decision to accidentally depend on.
/// </para>
/// <para>
/// <see cref="Pulse"/> is fire-and-forget and must never throw. A device with no vibrator, a
/// permission that was refused, an OEM ROM whose service is missing — all of them are a game that
/// plays exactly the same and simply does not buzz.
/// </para>
/// </remarks>
public interface IVibrator
{
    /// <param name="milliseconds">How long to buzz. Zero or less does nothing.</param>
    /// <param name="amplitude01">
    /// How hard, from 0 to 1. Honoured on API 26 and up; below that the platform has no amplitude
    /// at all and only the duration carries the weight of the pulse.
    /// </param>
    void Pulse(int milliseconds, float amplitude01);
}

/// <summary>
/// The Android vibrator, reached through JNI. A no-op everywhere else, including in the Editor.
/// </summary>
/// <remarks>
/// <para>
/// The whole JNI body sits behind <c>UNITY_ANDROID &amp;&amp; !UNITY_EDITOR</c>. The second half of
/// that condition is the load-bearing one: <c>UNITY_ANDROID</c> is defined in the Editor whenever
/// the active build target is Android, and <c>UnityPlayer.currentActivity</c> does not exist there
/// — so without it, switching platforms would turn every Editor playtest into a stream of caught
/// JNI failures.
/// </para>
/// <para>
/// The service is resolved once and cached. The alternative — walking
/// <c>UnityPlayer → currentActivity → getSystemService</c> on every pulse — would build four JNI
/// objects up to ten times a second for a call whose entire point is that it is cheap. A failure to
/// resolve is remembered too, so a device without a vibrator pays for one failed lookup per run and
/// nothing after it.
/// </para>
/// <para>
/// <b>Nothing here has ever run.</b> There is no phone yet and the APK does not run on BlueStacks
/// (M0-20a), so this class is written against the Android documentation and verified only by the
/// fact that it compiles and that the Editor takes <see cref="NullVibrator"/> instead. The first
/// hardware session is what confirms it.
/// </para>
/// </remarks>
public sealed class AndroidVibrator : IVibrator, IDisposable
{
#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>Oreo. The first API level with <c>VibrationEffect</c>, and so with amplitude.</summary>
    private const int AmplitudeApiLevel = 26;

    /// <summary>The device's <c>Vibrator</c> service, held for the app's life.</summary>
    private AndroidJavaObject _vibrator;

    /// <summary><c>android.os.VibrationEffect</c>, for the static factory. Null below API 26.</summary>
    private AndroidJavaClass _effects;

    private int _sdkInt;

    /// <summary>The lookup has been attempted, successfully or not.</summary>
    private bool _resolved;

    /// <summary>The lookup failed, or a pulse threw. Every later pulse is silent.</summary>
    private bool _unavailable;
#endif

    /// <inheritdoc />
    public void Pulse(int milliseconds, float amplitude01)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (milliseconds <= 0 || _unavailable)
        {
            return;
        }

        try
        {
            if (!Resolve())
            {
                return;
            }

            if (_sdkInt >= AmplitudeApiLevel)
            {
                // Clamped to 1 rather than 0: zero is "no vibration" to createOneShot, so an
                // amplitude that rounded down to nothing would silently drop the pulse instead of
                // playing the weakest one the device can.
                int amplitude = Mathf.Clamp(Mathf.RoundToInt(amplitude01 * 255f), 1, 255);

                using var effect = _effects.CallStatic<AndroidJavaObject>(
                    "createOneShot", (long)milliseconds, amplitude);

                _vibrator.Call("vibrate", effect);
            }
            else
            {
                // The deprecated overload, and the only one that exists here. Amplitude is simply
                // not a thing the platform has below Oreo — the duration carries the weight alone.
                _vibrator.Call("vibrate", (long)milliseconds);
            }
        }
        catch (Exception exception)
        {
            // A hit that buzzes is a nicety; a hit that throws is a bug report. Whatever went
            // wrong — no vibrator, a refused permission, an OEM service that is not there — this
            // device does not do haptics, said once and then never again.
            _unavailable = true;

            Debug.LogWarning(
                "Haptics are off for this session: the Android vibrator could not be used. " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
#endif
    }

    /// <summary>Releases the cached JNI objects. Called by the root scope at app teardown.</summary>
    public void Dispose()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        _vibrator?.Dispose();
        _effects?.Dispose();

        _vibrator = null;
        _effects = null;

        // Not a reset: a disposed vibrator must stay silent rather than resolve itself again on
        // the next stray pulse from something that outlived the scope.
        _resolved = true;
        _unavailable = true;
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Finds the vibrator service and the API level, once. Returns whether this device can buzz.
    /// </summary>
    /// <remarks>
    /// The activity and the player class are disposed immediately — they are borrowed handles to
    /// objects Unity owns — while the service and the effect class are kept, because they are what
    /// every later pulse is made of.
    /// </remarks>
    private bool Resolve()
    {
        if (_resolved)
        {
            return !_unavailable;
        }

        _resolved = true;

        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            _sdkInt = version.GetStatic<int>("SDK_INT");
        }

        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        {
            _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
        }

        if (_vibrator == null)
        {
            _unavailable = true;
            return false;
        }

        if (_sdkInt >= AmplitudeApiLevel)
        {
            _effects = new AndroidJavaClass("android.os.VibrationEffect");
        }

        return true;
    }
#endif
}

/// <summary>
/// A vibrator that does nothing. What every platform except an Android player build gets.
/// </summary>
/// <remarks>
/// A real registration rather than a null the listener has to check for: the listener's job is to
/// decide <em>what</em> to play, and a branch on "is there a device" in the middle of that job is a
/// second thing it could get wrong. In the Editor this is the one that runs, which is why haptics
/// cannot be playtested here at all.
/// </remarks>
public sealed class NullVibrator : IVibrator
{
    /// <inheritdoc />
    public void Pulse(int milliseconds, float amplitude01)
    {
    }
}

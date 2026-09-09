using System;
using Soulvail.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The one place in <c>Soulvail.Game</c> that reads the Input System. Everything downstream —
/// <c>SnapshotBuilder</c> (M0-16), the HUD, the presenters — asks this adapter, never the
/// generated <see cref="SoulvailActions"/> and never <c>InputSystem</c> directly, so a control
/// mode (GD §5.3) or an on-screen control can be swapped without touching a single reader.
/// </summary>
/// <remarks>
/// <para>
/// The floating stick of M0-15 sits on the *other* side of this class: it <em>feeds</em> the
/// Input System through <c>OnScreenControl</c>, surfacing as a virtual gamepad, and the adapter
/// reads it back through the same <c>Move</c> binding a real gamepad uses. Neither knows about
/// the other, which is the whole point of routing through the asset.
/// </para>
/// <para>
/// <c>Move</c>, <c>Focus</c> and <c>MovementSkill</c> are read. The other five actions are bound in
/// the asset already so that later tasks add a reader here rather than reopening the actions asset
/// — M3-10 takes <c>Skill1</c>–<c>Skill4</c>, M3-09 <c>Pause</c>.
/// </para>
/// </remarks>
public sealed class InputAdapter : IDisposable
{
    private readonly SoulvailActions _actions;
    private bool _disposed;

    /// <summary>
    /// Creates the actions, disabled. Nothing is read until <see cref="Enable"/> — a run owns
    /// the enabled window, so the menu never steers a player that is not on screen.
    /// </summary>
    public InputAdapter()
    {
        _actions = new SoulvailActions();
    }

    /// <summary>Whether the <c>Player</c> map is currently listening.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// The move stick, clamped to the unit disc, and <see cref="Vector2.zero"/> while disabled
    /// or after <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// The clamp is a guarantee to core, not a correction of any binding that exists today:
    /// both current sources are already unit-bounded — a stick control runs through
    /// <c>StickDeadzone</c>, which caps magnitude at 1, and the WASD composite is normalized.
    /// It is here so that the promise survives the first binding that is not, rather than being
    /// discovered by a player who moves faster diagonally.
    /// </remarks>
    public Vector2 Move
    {
        get
        {
            if (_disposed || !IsEnabled)
            {
                return Vector2.zero;
            }

            return Vector2.ClampMagnitude(_actions.Player.Move.ReadValue<Vector2>(), 1f);
        }
    }

    /// <summary>
    /// A tap or a click began this frame, and <see langword="false"/> while disabled or after
    /// <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The press edge, not the held state: <c>Focus</c> is bound to <c>&lt;Pointer&gt;/press</c>,
    /// so a thumb resting on the screen while the stick is being dragged would otherwise read as a
    /// tap on every frame of the drag. One tap, one command.
    /// </para>
    /// <para>
    /// Read once per frame by <c>TapToFocusAdapter</c>, from <c>RunTicker</c>'s command phase.
    /// <c>WasPressedThisFrame</c> is frame-scoped in the Input System's own sense — it compares the
    /// action's last change against the current update — so reading it twice in one frame is safe
    /// and reading it in a later phase of the same frame still answers the same thing.
    /// </para>
    /// </remarks>
    public bool FocusPressedThisFrame
    {
        get
        {
            if (_disposed || !IsEnabled)
            {
                return false;
            }

            return _actions.Player.Focus.WasPressedThisFrame();
        }
    }

    /// <summary>
    /// The movement-skill button went down this frame, and <see langword="false"/> while disabled
    /// or after <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The press edge, exactly like <see cref="FocusPressedThisFrame"/>: a thumb held on the button
    /// is one dash asked for, not a dash asked for on every frame of the hold. Core would refuse
    /// the repeats anyway — <c>ChargeSkill</c> keeps only the latest press and drops it once it
    /// fires — but sending them would mean every frame of a hold overwrote the buffered press with
    /// a fresher one, which is precisely how a tap made <em>before</em> the cooldown ended would
    /// stop being the tap that fires when it does.
    /// </para>
    /// <para>
    /// The action is bound to <c>&lt;Keyboard&gt;/space</c> and <c>&lt;Gamepad&gt;/buttonSouth</c>
    /// (M0-14). On a phone the second of those is the on-screen <c>SkillButton</c>, which feeds the
    /// virtual gamepad the same way the floating stick feeds the left stick — so this one property
    /// is read identically in the Editor and under a thumb.
    /// </para>
    /// </remarks>
    public bool MovementSkillPressedThisFrame
    {
        get
        {
            if (_disposed || !IsEnabled)
            {
                return false;
            }

            return _actions.Player.MovementSkill.WasPressedThisFrame();
        }
    }

    /// <summary>
    /// Where the pointer is in screen pixels, and <see cref="Vector2.zero"/> while disabled, after
    /// <see cref="Dispose"/>, or when the device has no pointer at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from <see cref="Pointer.current"/> rather than through a bound action, because the
    /// position is not an intent — nothing is being asked for, it is the coordinate the
    /// <c>Focus</c> press happened at. Binding it as a <c>Value</c> action would add a second
    /// action to the asset that is polled every frame and means nothing on its own. It still comes
    /// through this class rather than from <c>Pointer.current</c> at the call site, so the rule
    /// that this is the only reader of the Input System survives intact.
    /// </para>
    /// <para>
    /// <see cref="Pointer"/>, not <c>Mouse</c> or <c>Touchscreen</c>: a touchscreen's primary touch
    /// and a mouse are both pointers, so the Editor and the phone answer the same question through
    /// the same property. Zero is honest for a device with neither — the caller is about to raycast
    /// through it, and the bottom-left corner of the screen is over the stick region, which is
    /// exactly where a tap gets ignored.
    /// </para>
    /// </remarks>
    public Vector2 PointerPosition
    {
        get
        {
            if (_disposed || !IsEnabled)
            {
                return Vector2.zero;
            }

            Pointer pointer = Pointer.current;

            // `is null`, not Unity's ==: an InputDevice is a plain C# object, not a
            // UnityEngine.Object, so there is no overloaded operator here to reach for.
            return pointer is null ? Vector2.zero : pointer.position.ReadValue();
        }
    }

    /// <summary>Starts listening. Idempotent; a no-op once disposed.</summary>
    public void Enable()
    {
        if (_disposed || IsEnabled)
        {
            return;
        }

        _actions.Player.Enable();
        IsEnabled = true;
    }

    /// <summary>Stops listening. Idempotent; a no-op once disposed.</summary>
    public void Disable()
    {
        if (_disposed || !IsEnabled)
        {
            return;
        }

        _actions.Player.Disable();
        IsEnabled = false;
    }

    /// <summary>
    /// Disables the map and destroys the underlying asset. Idempotent, and safe to call from a
    /// scope teardown; every member is inert afterwards rather than throwing.
    /// </summary>
    /// <remarks>
    /// Disabling first is not politeness: <c>SoulvailActions</c>' finalizer asserts the map is
    /// disabled, and a still-enabled map leaks its state monitors into the next run.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Disable();
        DestroyAsset(_actions.asset);
        _disposed = true;
    }

    /// <summary>
    /// Destroys the actions asset with the call the current context accepts.
    /// </summary>
    /// <remarks>
    /// The generated <c>SoulvailActions.Dispose()</c> calls <c>Object.Destroy</c> unconditionally,
    /// which is correct in a player and wrong in the Editor outside play mode: there it destroys
    /// nothing and logs <c>"Destroy may not be called from edit mode!"</c> as an *error*, which
    /// both leaks the asset and reddens any EditMode test that disposes an adapter. Hence the
    /// split, and hence this class not delegating to the generated method.
    /// </remarks>
    private static void DestroyAsset(UnityEngine.Object asset)
    {
        if (asset == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEngine.Object.DestroyImmediate(asset);
            return;
        }
#endif

        UnityEngine.Object.Destroy(asset);
    }
}

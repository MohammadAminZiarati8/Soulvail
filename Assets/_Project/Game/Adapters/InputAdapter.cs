using System;
using Soulvail.Game.Input;
using UnityEngine;

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
/// Only <c>Move</c> is read in M0. The other seven actions are bound in the asset already so
/// that later tasks add a reader here rather than reopening the actions asset — M1-09 takes
/// <c>Focus</c>, M1-16 <c>MovementSkill</c>, M3-10 <c>Skill1</c>–<c>Skill4</c>.
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

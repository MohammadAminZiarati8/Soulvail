using NUnit.Framework;
using Soulvail.Game.Adapters;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// Runs on <see cref="InputTestFixture"/>, which swaps the whole Input System for an isolated
/// one per test and restores it afterwards — devices, settings and all — so nothing here can
/// reach the Editor's real input state.
/// </summary>
/// <remarks>
/// <para>
/// NUnit runs base-class <c>[SetUp]</c> before derived, and derived <c>[TearDown]</c> before
/// base, which is exactly the nesting the adapter needs: it is constructed after the test input
/// state exists and destroyed before it is torn down.
/// </para>
/// <para>
/// Deadzones are neutralised in setup. A gamepad stick carries <c>StickDeadzone</c>, which by
/// default drops everything under 0.125 and rescales the rest — so an unmodified fixture reads
/// a stick set to (0.3, 0.6) back as roughly (0.305, 0.610) and the value assertions would be
/// measuring the processor's curve, not the adapter. What the deadzone cannot be talked out of
/// is its upper clamp, which is why <see cref="Move_IsClampedToUnit"/> says what it says below.
/// </para>
/// </remarks>
[TestFixture]
public sealed class InputAdapterTests : InputTestFixture
{
    private Gamepad _gamepad;
    private InputAdapter _adapter;

    [SetUp]
    public void CreateAdapter()
    {
        InputSystem.settings.defaultDeadzoneMin = 0f;
        InputSystem.settings.defaultDeadzoneMax = 1f;

        _gamepad = InputSystem.AddDevice<Gamepad>();
        _adapter = new InputAdapter();
    }

    [TearDown]
    public void DisposeAdapter()
    {
        _adapter?.Dispose();
        _adapter = null;
    }

    [Test]
    public void Move_ReflectsGamepadLeftStick()
    {
        _adapter.Enable();

        Set(_gamepad.leftStick, new Vector2(0.3f, 0.6f));
        InputSystem.Update();

        Assert.That(_adapter.Move.x, Is.EqualTo(0.3f).Within(1e-3f));
        Assert.That(_adapter.Move.y, Is.EqualTo(0.6f).Within(1e-3f));
    }

    [Test]
    public void Move_IsClampedToUnit()
    {
        _adapter.Enable();

        Set(_gamepad.leftStick, new Vector2(1f, 1f));
        InputSystem.Update();

        Assert.That(_adapter.Move.magnitude, Is.EqualTo(1f).Within(1e-4f));

        // Direction survives the clamp — a corner deflection still points at the corner.
        Assert.That(_adapter.Move.x, Is.EqualTo(_adapter.Move.y).Within(1e-4f));

        // Honesty note, deliberately asserted rather than left as a comment: the stick control
        // has already capped its own magnitude at 1 before the adapter ever sees it, so this
        // fixture cannot make the adapter's ClampMagnitude do observable work. The test pins the
        // *contract* — Move never leaves the unit disc — and this line records why it would keep
        // passing if the clamp were deleted. The first binding that can exceed 1 is the one that
        // makes it bite, and it is that binding's test that must prove it.
        Assert.That(
            _gamepad.leftStick.ReadValue().magnitude,
            Is.EqualTo(1f).Within(1e-4f),
            "StickDeadzone is expected to pre-clamp; if this ever fails the note above is stale.");
    }

    /// <remarks>
    /// The <c>InputSystem.Update()</c> after each <c>Enable</c> is load-bearing, not ceremony.
    /// A Value action picks up an already-deflected control through an *initial state check*,
    /// which the system runs on the update following the enable — so an action enabled while the
    /// stick is held reads zero until then. Without those updates this test fails on the sanity
    /// assertion, and the same lag will be visible in the game: M0-16 enables the adapter as a
    /// run starts, and a thumb already on the stick moves the player one frame later.
    /// </remarks>
    [Test]
    public void Move_ZeroWhileDisabled()
    {
        Set(_gamepad.leftStick, new Vector2(0.5f, 0f));
        InputSystem.Update();

        _adapter.Enable();
        InputSystem.Update();
        Assert.That(
            _adapter.Move.x,
            Is.EqualTo(0.5f).Within(1e-3f),
            "Sanity: the stick really is deflected, so the zero below means something.");

        _adapter.Disable();
        Assert.That(_adapter.Move, Is.EqualTo(Vector2.zero));

        _adapter.Enable();
        InputSystem.Update();
        Assert.That(
            _adapter.Move.x,
            Is.EqualTo(0.5f).Within(1e-3f),
            "Disabling drops the reading; it must not destroy the ability to read.");
    }

    [Test]
    public void EnableDisable_Idempotent()
    {
        Assert.That(_adapter.IsEnabled, Is.False, "A fresh adapter listens to nothing.");

        _adapter.Enable();
        _adapter.Enable();
        Assert.That(_adapter.IsEnabled, Is.True);

        _adapter.Disable();
        _adapter.Disable();
        Assert.That(_adapter.IsEnabled, Is.False);
    }

    [Test]
    public void Dispose_ThenMove_ReturnsZero()
    {
        _adapter.Enable();
        Set(_gamepad.leftStick, new Vector2(0.5f, 0f));
        InputSystem.Update();

        Assert.That(
            _adapter.Move.x,
            Is.EqualTo(0.5f).Within(1e-3f),
            "Sanity: the adapter was reading something, so the zero after disposal is disposal's doing.");

        _adapter.Dispose();

        Assert.That(_adapter.Move, Is.EqualTo(Vector2.zero));
        Assert.That(_adapter.IsEnabled, Is.False);

        // Teardown disposes again; a scope that disposes twice must not be punished for it.
        Assert.DoesNotThrow(() => _adapter.Dispose());
    }
}

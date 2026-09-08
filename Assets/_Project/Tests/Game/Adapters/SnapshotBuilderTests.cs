using NUnit.Framework;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The senses, measured. Every row is a fact core cannot check for itself: if the builder copies
/// the wrong number, or the same number twice, core is simply wrong about the world and nothing
/// downstream can tell.
/// </summary>
/// <remarks>
/// <para>
/// Runs on <see cref="InputTestFixture"/>, which swaps the whole Input System for an isolated one
/// per test — the same arrangement <see cref="InputAdapterTests"/> uses, and for the same reason:
/// the stick has to be settable without touching the Editor's real input state. Deadzones are
/// neutralised in setup so a stick set to (0.3, 0.6) reads back as (0.3, 0.6) rather than as
/// <c>StickDeadzone</c>'s rescaled curve.
/// </para>
/// <para>
/// Every vector is fully qualified, as in <see cref="NumTests"/>: with a <c>using</c> for either
/// vector namespace, a boundary crossing and an identity look the same on the page.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SnapshotBuilderTests : InputTestFixture
{
    private GameObject _playerObject;
    private PlayerView _player;
    private Gamepad _gamepad;
    private InputAdapter _input;
    private SnapshotBuilder _builder;
    private WorldSnapshot _snapshot;

    [SetUp]
    public void CreateBuilder()
    {
        InputSystem.settings.defaultDeadzoneMin = 0f;
        InputSystem.settings.defaultDeadzoneMax = 1f;
        _gamepad = InputSystem.AddDevice<Gamepad>();

        // AddComponent brings the CharacterController with it, per PlayerView's [RequireComponent].
        // Nothing here calls Apply: outside play mode Awake never runs, so the controller the body
        // would move is not wired — which is exactly why the spec leaves Apply to the manual steps.
        _playerObject = new GameObject("Player");
        _player = _playerObject.AddComponent<PlayerView>();

        _input = new InputAdapter();
        _builder = new SnapshotBuilder(_player, _input);
        _snapshot = new WorldSnapshot(8);
    }

    [TearDown]
    public void DestroyBuilder()
    {
        _input?.Dispose();
        _input = null;

        if (_playerObject != null)
        {
            // DestroyImmediate, not Destroy: in edit mode the latter destroys nothing and logs an
            // error, which would leak the object and redden the test (M0-14).
            Object.DestroyImmediate(_playerObject);
        }

        _playerObject = null;
    }

    [Test]
    public void Build_CopiesPlayerPositionAndVelocity()
    {
        _playerObject.transform.position = new UnityEngine.Vector3(3f, 0f, -2f);

        _builder.Build(_snapshot, 0.02f);

        Assert.That(_snapshot.PlayerPosition.X, Is.EqualTo(3f));
        Assert.That(_snapshot.PlayerPosition.Y, Is.EqualTo(0f));
        Assert.That(_snapshot.PlayerPosition.Z, Is.EqualTo(-2f),
            "Z must come from Z — a component swapped at the boundary is a game that walks sideways.");

        Assert.That(_snapshot.Dt, Is.EqualTo(0.02f));
        Assert.That(_snapshot.EnemyCount, Is.Zero, "M0 has no enemies to report.");

        // Honesty note, asserted rather than left as a comment: a view that has never applied an
        // intent really does report zero velocity, so this line cannot distinguish a copied zero
        // from a hardcoded one. Making it non-zero needs Apply, which needs Awake, which needs play
        // mode. The value is verified on the device instead (manual step 4, |v| = 5.4); what this
        // row pins is that the field is written at all and that nothing else leaks into it.
        Assert.That(_snapshot.PlayerVelocity, Is.EqualTo(System.Numerics.Vector3.Zero));

        // The adapter was never enabled, so the stick reads zero — which makes the non-zero
        // reading in Build_CopiesMoveInput the thing that proves the wire.
        Assert.That(_snapshot.MoveInput, Is.EqualTo(System.Numerics.Vector2.Zero));
    }

    [Test]
    public void Build_CopiesMoveInput()
    {
        _input.Enable();

        Set(_gamepad.leftStick, new UnityEngine.Vector2(0.3f, 0.6f));

        // Load-bearing: a Value action picks up an already-deflected control through an initial
        // state check that runs on the update *after* the enable (M0-14).
        InputSystem.Update();

        _builder.Build(_snapshot, 0.016f);

        // Stick X → world X, stick Y → world Z. The snapshot carries the pair unrotated; the
        // mapping into world axes is the motor's, and turning it into a camera-relative one is
        // this builder's job the day the camera can yaw.
        Assert.That(_snapshot.MoveInput.X, Is.EqualTo(0.3f).Within(1e-3f));
        Assert.That(_snapshot.MoveInput.Y, Is.EqualTo(0.6f).Within(1e-3f));
    }

    [Test]
    public void Build_ClearsPreviousEnemies()
    {
        // `ref` on both sides, deliberately: bound to a copy, the fill would write to a temporary
        // and the count below would be the only thing this row was really testing (M0-05).
        ref EnemySense first = ref _snapshot.AddEnemy();
        first.Id = 1;
        ref EnemySense second = ref _snapshot.AddEnemy();
        second.Id = 2;
        ref EnemySense third = ref _snapshot.AddEnemy();
        third.Id = 3;

        Assert.That(_snapshot.EnemyCount, Is.EqualTo(3), "Sanity: there is something to clear.");

        _builder.Build(_snapshot, 0.016f);

        // Last frame's enemies are still sitting in the array — Clear does not zero it — and the
        // count is the only thing that keeps core from reading them. If the builder ever stops
        // clearing, every dead enemy stays alive to core forever.
        Assert.That(_snapshot.EnemyCount, Is.Zero);
        Assert.That(_snapshot.Enemies[0].Id, Is.EqualTo(1),
            "Clear leaves the slots alone by design; only the count moves.");
    }

    [Test]
    public void Build_ClampsDt()
    {
        // A hitch, a scene load or a phone waking up. Unclamped, core would advance the player
        // 1.6 m in one Move and through whatever was in the way.
        _builder.Build(_snapshot, 0.3f);

        Assert.That(_snapshot.Dt, Is.EqualTo(SnapshotBuilder.MaxDt));
        Assert.That(_snapshot.Dt, Is.EqualTo(0.05f), "50 ms, the spec's floor of 20 fps.");

        // A normal frame passes through untouched — the clamp is a ceiling, not a quantiser.
        _builder.Build(_snapshot, 0.016f);

        Assert.That(_snapshot.Dt, Is.EqualTo(0.016f));
    }

    [Test]
    public void Build_AllocatesNothing()
    {
        // Enabled and deflected on purpose: with the adapter disabled, Move returns early and the
        // measurement would never touch the Input System read that is the only plausible allocator
        // in this path.
        _input.Enable();
        Set(_gamepad.leftStick, new UnityEngine.Vector2(0.3f, 0.6f));
        InputSystem.Update();

        AllocationAssert.None(() => _builder.Build(_snapshot, 0.016f));
    }
}

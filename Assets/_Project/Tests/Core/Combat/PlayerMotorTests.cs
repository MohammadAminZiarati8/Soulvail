using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// The Oathbound's numbers throughout (CC §7), so a failure reads as "the character we ship
/// stopped behaving" rather than as an arithmetic puzzle.
/// </summary>
/// <remarks>
/// Tick sizes vary by test. Most use 1/120 s, but two of the spec's durations are not whole
/// numbers of those ticks — 0.03 s is 3.6 and 0.0625 s is 7.5 — so those tests pick a size that
/// sums to the specced duration exactly. Rounding the tick count instead would miss the stated
/// tolerance and turn a correct motor red.
/// </remarks>
[TestFixture]
public sealed class PlayerMotorTests
{
    private const float Speed = 5.4f;
    private const float AccelTime = 0.06f;
    private const float DecelTime = 0.08f;
    private const float TurnSpeedDeg = 720f;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    private static MovementSpec Oathbound() => new(Speed, AccelTime, DecelTime, TurnSpeedDeg);

    private static void Run(PlayerMotor motor, float dt, int ticks, Vector2 input, Vector3? face = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            motor.Tick(dt, input, face);
        }
    }

    /// <summary>Unsigned angle between two ground-plane unit vectors, in degrees.</summary>
    private static float AngleDegrees(Vector3 a, Vector3 b)
    {
        float cos = (a.X * b.X) + (a.Z * b.Z);

        // Acos of 1.0000001 is NaN, and normalised vectors land there routinely.
        if (cos > 1f)
        {
            cos = 1f;
        }
        else if (cos < -1f)
        {
            cos = -1f;
        }

        return MathF.Acos(cos) * (180f / MathF.PI);
    }

    [Test]
    public void FromRest_FullInput_ReachesSpeedWithinAccelTime()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);

        // 8 × 1/120 = 0.0667 s, just past the 0.06 s ramp, so this pins that the ramp finishes
        // and clamps rather than continuing to accelerate.
        Run(motor, Frame, 8, new Vector2(0f, 1f));

        Assert.That(motor.Velocity.Length(), Is.EqualTo(Speed).Within(1e-3f));
    }

    [Test]
    public void FromRest_FullInput_HalfwayAtHalfAccelTime()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);

        // 0.03 s exactly — half the accel time, so half the top speed. The ramp is linear, which
        // is the half of rule 2 that "reaches Speed in AccelTime" alone would not pin: an
        // exponential approach passes that test and fails this one.
        Run(motor, 0.01f, 3, new Vector2(0f, 1f));

        Assert.That(motor.Velocity.Length(), Is.EqualTo(2.7f).Within(0.1f));
    }

    [Test]
    public void FullSpeed_ZeroInput_StopsWithinDecelTime()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);
        Run(motor, Frame, 8, new Vector2(0f, 1f));

        // 10 × 1/120 = 0.0833 s, past the 0.08 s decel. No inertia: the stick is released and
        // the character is stopped, not sliding.
        Run(motor, Frame, 10, Vector2.Zero);

        Assert.That(motor.Velocity.Length(), Is.LessThan(1e-3f));
    }

    [Test]
    public void HalfInput_SettlesAtHalfSpeed()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);

        // A full second, far longer than the ramp: the point is that it settles there and stays,
        // rather than creeping up to full speed given enough time.
        Run(motor, Frame, 120, new Vector2(0f, 0.5f));

        Assert.That(motor.Velocity.Length(), Is.EqualTo(2.7f).Within(1e-3f));
    }

    [Test]
    public void OversizedInput_IsClamped()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);

        Run(motor, Frame, 120, new Vector2(0f, 3f));

        // Rule 3's row: an input the shaping layer over-delivered buys no extra speed.
        Assert.That(motor.Velocity.Length(), Is.EqualTo(Speed).Within(1e-3f));
    }

    [Test]
    public void InputMapsXToX_YToZ()
    {
        var alongX = new PlayerMotor(Oathbound(), Vector3.UnitZ);
        Run(alongX, Frame, 120, new Vector2(1f, 0f));

        Assert.That(alongX.Velocity.X, Is.EqualTo(Speed).Within(1e-3f));
        Assert.That(alongX.Velocity.Z, Is.EqualTo(0f).Within(1e-6f));

        var alongZ = new PlayerMotor(Oathbound(), Vector3.UnitZ);
        Run(alongZ, Frame, 120, new Vector2(0f, 1f));

        // The stick's Y is the ground plane's Z, not the world's up. Swapped, every test above
        // still passes — they only measure magnitudes — and the character strafes when it should
        // walk forward.
        Assert.That(alongZ.Velocity.Z, Is.EqualTo(Speed).Within(1e-3f));
        Assert.That(alongZ.Velocity.X, Is.EqualTo(0f).Within(1e-6f));
    }

    [Test]
    public void Velocity_YIsAlwaysZero()
    {
        // Y offered on both inputs that could carry one: the initial facing and the aim.
        var motor = new PlayerMotor(Oathbound(), new Vector3(0f, 5f, 1f));

        for (int i = 0; i < 240; i++)
        {
            // A stick that sweeps the whole circle, so this covers deceleration and reversal too
            // rather than one steady heading.
            var input = new Vector2(MathF.Sin(i * 0.1f), MathF.Cos(i * 0.1f));
            motor.Tick(Frame, input, new Vector3(0f, 3f, 1f));

            Assert.That(motor.Velocity.Y, Is.EqualTo(0f), $"Velocity left the ground plane on tick {i}.");
            Assert.That(motor.Facing.Y, Is.EqualTo(0f), $"Facing left the ground plane on tick {i}.");
        }
    }

    [Test]
    public void Facing_FollowsVelocity_WhenNoFaceDirection()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);

        Run(motor, Frame, 120, new Vector2(1f, 0f));

        Assert.That(motor.Facing.X, Is.EqualTo(1f).Within(1e-3f));
        Assert.That(motor.Facing.Z, Is.EqualTo(0f).Within(1e-3f));
    }

    [Test]
    public void Facing_RotatesAtTurnSpeed()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);

        // Reach full speed along +X while pinning the aim to the facing it already has. Started
        // from rest with a free facing this would also be measuring how much of the first tick's
        // rotation the accel ramp allows, which is a different rule.
        Run(motor, Frame, 8, new Vector2(1f, 0f), Vector3.UnitZ);
        Assert.That(motor.Facing.Z, Is.EqualTo(1f).Within(1e-6f), "Sanity: the aim held the facing at +Z.");

        // 5 × 0.0125 = 0.0625 s, and 720°/s × 0.0625 s = 45°.
        Run(motor, 0.0125f, 5, new Vector2(1f, 0f));

        Assert.That(AngleDegrees(motor.Facing, Vector3.UnitZ), Is.EqualTo(45f).Within(1f));
    }

    [Test]
    public void Facing_SnapsWhenWithinOneStep()
    {
        // 1° off +Z, against a step of 720°/s × 1/120 s = 6°.
        float radians = 1f * (MathF.PI / 180f);
        var motor = new PlayerMotor(Oathbound(), new Vector3(MathF.Sin(radians), 0f, MathF.Cos(radians)));

        motor.Tick(Frame, Vector2.Zero, Vector3.UnitZ);

        // Exactly, not nearly: the snap is an assignment of the target, so there is no residual
        // to accumulate. Rotating by the remaining angle instead would leave a rounding error
        // every frame and let a held aim drift around its target.
        Assert.That(motor.Facing, Is.EqualTo(Vector3.UnitZ));
    }

    [Test]
    public void Facing_UsesFaceDirection_OverVelocity()
    {
        // Starting at +Z with an aim of −Z is the 180° case, where "the shorter arc" has no
        // answer. It must still resolve and finish rather than stalling on the singularity.
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);

        Run(motor, Frame, 120, new Vector2(1f, 0f), -Vector3.UnitZ);

        Assert.That(motor.Facing.Z, Is.EqualTo(-1f).Within(1e-3f));
        Assert.That(motor.Facing.X, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(motor.Velocity.X, Is.EqualTo(Speed).Within(1e-3f), "Sanity: it really was moving +X.");
    }

    [Test]
    public void Facing_HoldsLast_WhenIdle()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitX);

        Run(motor, Frame, 120, Vector2.Zero);

        // Untouched, not merely unchanged to within a tolerance — a standing character has no
        // reason to accumulate rotation at all.
        Assert.That(motor.Facing, Is.EqualTo(Vector3.UnitX));
    }

    [Test]
    public void Facing_TakesShorterArc()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);

        // +Z to −X is 90° counter-clockwise or 270° clockwise.
        motor.Tick(Frame, Vector2.Zero, -Vector3.UnitX);

        // One 6° step the short way puts X slightly negative. The long way round would take it
        // through +X, so the sign is what distinguishes them after a single tick.
        Assert.That(motor.Facing.X, Is.LessThan(0f));
        Assert.That(motor.Facing.Z, Is.GreaterThan(0f));
        Assert.That(AngleDegrees(motor.Facing, Vector3.UnitZ), Is.EqualTo(6f).Within(0.1f));
    }

    [Test]
    public void Facing_ZeroFaceDirection_FallsBackToVelocity()
    {
        // Beyond the spec's Tests table, and covering a decision it does not make. `Vector3?`
        // lets a caller provide a vector with no direction in it; normalising one is NaN, and a
        // NaN facing never recovers. "Provided" therefore means "provided a direction", and
        // anything else falls through to the velocity rule.
        var zeroAim = new PlayerMotor(Oathbound(), Vector3.UnitZ);
        Run(zeroAim, Frame, 120, new Vector2(1f, 0f), Vector3.Zero);

        Assert.That(zeroAim.Facing.X, Is.EqualTo(1f).Within(1e-3f));

        // Purely vertical flattens to the same nothing.
        var verticalAim = new PlayerMotor(Oathbound(), Vector3.UnitZ);
        Run(verticalAim, Frame, 120, new Vector2(1f, 0f), Vector3.UnitY);

        Assert.That(verticalAim.Facing.X, Is.EqualTo(1f).Within(1e-3f));

        // With no velocity to fall back to either, it holds — the one path where both rules
        // decline, and the one where a NaN would otherwise be permanent.
        var idle = new PlayerMotor(Oathbound(), Vector3.UnitX);
        Run(idle, Frame, 60, Vector2.Zero, Vector3.Zero);

        Assert.That(idle.Facing, Is.EqualTo(Vector3.UnitX));
    }

    [Test]
    public void Ctor_ZeroFacing_DefaultsToPlusZ()
    {
        Assert.That(new PlayerMotor(Oathbound(), Vector3.Zero).Facing, Is.EqualTo(Vector3.UnitZ));

        // "Y forced to 0" has no row of its own, and a purely vertical facing is where it
        // decides the answer: flattened, there is no direction left, so it defaults the same way.
        Assert.That(new PlayerMotor(Oathbound(), Vector3.UnitY).Facing, Is.EqualTo(Vector3.UnitZ));

        // A tilted facing keeps its ground direction and is renormalised, rather than being
        // rejected or kept at length 2.
        Assert.That(new PlayerMotor(Oathbound(), new Vector3(0f, 10f, 2f)).Facing, Is.EqualTo(Vector3.UnitZ));
    }

    [Test]
    public void Tick_ZeroDt_NoChange()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);

        // Stopped mid-ramp and mid-turn: a state a no-op tick could visibly disturb, unlike rest.
        Run(motor, Frame, 4, new Vector2(1f, 0f));

        Vector3 velocityBefore = motor.Velocity;
        Vector3 facingBefore = motor.Facing;

        // A different stick and an aim, so a tick that did anything at all would move something.
        motor.Tick(0f, new Vector2(0f, 1f), -Vector3.UnitX);

        Assert.That(motor.Velocity, Is.EqualTo(velocityBefore));
        Assert.That(motor.Facing, Is.EqualTo(facingBefore));

        // A negative dt is the same claim — no time has passed. Integrated, it would accelerate
        // backwards and turn the wrong way.
        motor.Tick(-Frame, new Vector2(0f, 1f), -Vector3.UnitX);

        Assert.That(motor.Velocity, Is.EqualTo(velocityBefore));
        Assert.That(motor.Facing, Is.EqualTo(facingBefore));
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        var motor = new PlayerMotor(Oathbound(), Vector3.UnitZ);
        var input = new Vector2(0.7f, 0.7f);

        AllocationAssert.None(() => motor.Tick(Frame, input, null));

        // The aimed path is a different branch, and the `Vector3?` argument is where a boxing
        // conversion would hide if the signature were ever widened to an interface or object.
        AllocationAssert.None(() => motor.Tick(Frame, input, Vector3.UnitX));
    }
}

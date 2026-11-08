using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;

namespace Soulvail.Tests.Core.Ai;

/// <summary>BT-04: the pack's slots, lagged facing, easing and spacing as pure math.</summary>
[TestFixture]
public sealed class PackFormationTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void SlotOffset_LeadRight_PackLeftAndBehind()
    {
        Assert.That(PackFormation.SlotOffset(0), Is.EqualTo(new Vector2(1.6f, 0f)), "Lead on his right.");
        Assert.That(PackFormation.SlotOffset(1), Is.EqualTo(new Vector2(-1.6f, 0f)), "Second on his left.");
        Assert.That(PackFormation.SlotOffset(2), Is.EqualTo(new Vector2(0f, -1.8f)), "Third behind.");
        Assert.That(PackFormation.SlotOffset(3), Is.EqualTo(new Vector2(-1.2f, -1.6f)), "Fourth behind-left.");
        Assert.Throws<ArgumentOutOfRangeException>(() => PackFormation.SlotOffset(PackFormation.SlotCount));

        var formation = new PackFormation();
        formation.Tick(10f, Vector2.UnitX);
        Assert.That(Vector2.Distance(formation.WorldOffset(0), new Vector2(0f, -1.6f)), Is.LessThan(Tolerance),
            "Facing +X, his right is −Y.");
    }

    [Test]
    public void Facing_Flick_TurnsAtCappedRate()
    {
        var formation = new PackFormation();
        Assert.That(formation.Facing, Is.EqualTo(Vector2.UnitY));

        formation.Tick(0.1f, -Vector2.UnitX);
        float turned = MathF.Acos(Math.Clamp(Vector2.Dot(formation.Facing, Vector2.UnitY), -1f, 1f)) * 180f / MathF.PI;
        Assert.That(turned, Is.EqualTo(PackFormation.TurnDegreesPerSecond * 0.1f).Within(0.01f));
        Assert.That(formation.Facing.X, Is.LessThan(0f), "It turns toward the stick.");

        formation.Tick(0.1f, Vector2.UnitY);
        Assert.That(formation.Facing, Is.EqualTo(Vector2.UnitY), "A flick back inside the step snaps home.");

        formation.Tick(0.1f, Vector2.Zero);
        Assert.That(formation.Facing, Is.EqualTo(Vector2.UnitY), "No stick, no turn.");
    }

    [Test]
    public void ArrivalSpeed_SlowsNearSlot_CappedByMoveSpeed()
    {
        Assert.That(PackFormation.ArrivalSpeed(0.8f, 4f), Is.EqualTo(2f).Within(Tolerance));
        Assert.That(PackFormation.ArrivalSpeed(0.4f, 4f), Is.LessThan(PackFormation.ArrivalSpeed(0.8f, 4f)));
        Assert.That(PackFormation.ArrivalSpeed(10f, 4f), Is.EqualTo(4f));
        Assert.That(PackFormation.ArrivalSpeed(float.NaN, 4f), Is.Zero);
        Assert.That(PackFormation.ArrivalSpeed(1f, -1f), Is.Zero);
    }

    [Test]
    public void Separation_TwoOverlapping_PushedApart()
    {
        Span<Vector2> positions = stackalloc Vector2[] { Vector2.Zero, new Vector2(0.5f, 0f), new Vector2(5f, 0f) };

        Vector2 first = PackFormation.Separation(positions, 0);
        Vector2 second = PackFormation.Separation(positions, 1);
        Vector2 far = PackFormation.Separation(positions, 2);

        Assert.That(first.X, Is.LessThan(0f));
        Assert.That(second.X, Is.GreaterThan(0f));
        Assert.That(far, Is.EqualTo(Vector2.Zero), "Spaced animals are left alone.");

        Span<Vector2> stacked = stackalloc Vector2[] { Vector2.Zero, Vector2.Zero, Vector2.Zero };
        Assert.That(PackFormation.Separation(stacked, 1).Length(), Is.LessThanOrEqualTo(1.5f + Tolerance));
        Assert.That(PackFormation.Separation(stacked, 0).X, Is.LessThan(0f), "Stacked: split by index.");
        Assert.That(PackFormation.Separation(stacked, 2).X, Is.GreaterThan(0f));
    }

    [Test]
    public void Heel_SlotWithinRegenRadius()
    {
        for (int slot = 0; slot < PackFormation.SlotCount; slot++)
        {
            Assert.That(PackFormation.SlotOffset(slot).LengthSquared(),
                Is.LessThanOrEqualTo(PetSystem.AtHeelRadius * PetSystem.AtHeelRadius + Tolerance), $"Slot {slot}.");
        }
    }
}

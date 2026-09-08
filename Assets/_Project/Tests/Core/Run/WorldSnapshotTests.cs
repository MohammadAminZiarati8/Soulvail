using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

[TestFixture]
public sealed class WorldSnapshotTests
{
    [Test]
    public void Ctor_AllocatesArrayOfCapacity()
    {
        var snapshot = new WorldSnapshot(32);

        Assert.That(snapshot.Enemies, Is.Not.Null);
        Assert.That(snapshot.Enemies.Length, Is.EqualTo(32));
        Assert.That(snapshot.EnemyCapacity, Is.EqualTo(32));
        Assert.That(snapshot.EnemyCount, Is.EqualTo(0), "A fresh snapshot holds no enemies yet.");
    }

    [Test]
    public void Ctor_ZeroCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldSnapshot(0));

        // The rule is "not positive", not "not zero" — a negative capacity must fail the same way
        // rather than reaching new EnemySense[-1].
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldSnapshot(-1));
    }

    [Test]
    public void AddEnemy_ReturnsSlotAndIncrements()
    {
        var snapshot = new WorldSnapshot(4);

        // `ref` on both sides, deliberately: written as `var first = snapshot.AddEnemy()` this
        // would bind a copy, every assertion below would still pass on it, and the test would
        // prove nothing about the snapshot.
        ref EnemySense first = ref snapshot.AddEnemy();
        first.Id = 7;

        Assert.That(snapshot.EnemyCount, Is.EqualTo(1));
        Assert.That(snapshot.Enemies[0].Id, Is.EqualTo(7), "The ref must write through to the array.");

        ref EnemySense second = ref snapshot.AddEnemy();
        second.Id = 9;

        Assert.That(snapshot.EnemyCount, Is.EqualTo(2));
        Assert.That(snapshot.Enemies[1].Id, Is.EqualTo(9), "The second call must hand back the next slot.");
        Assert.That(snapshot.Enemies[0].Id, Is.EqualTo(7), "…and must not disturb the first.");
    }

    [Test]
    public void AddEnemy_WhenFull_Throws()
    {
        var snapshot = new WorldSnapshot(1);
        snapshot.AddEnemy();

        // Loud rather than silent: dropping the enemy would make core blind to it, and growing
        // the array would allocate in the middle of a frame.
        Assert.Throws<InvalidOperationException>(() => { snapshot.AddEnemy(); });
    }

    [Test]
    public void Clear_ResetsScalarsAndCount_KeepsArrayInstance()
    {
        var snapshot = new WorldSnapshot(4)
        {
            Dt = 0.016f,
            MoveInput = new Vector2(0.5f, -0.5f),
            PlayerPosition = new Vector3(1f, 2f, 3f),
            PlayerVelocity = new Vector3(4f, 5f, 6f),
        };

        EnemySense[] enemiesBefore = snapshot.Enemies;
        ref EnemySense enemy = ref snapshot.AddEnemy();
        enemy.Id = 11;

        snapshot.Clear();

        Assert.That(snapshot.Dt, Is.EqualTo(0f));
        Assert.That(snapshot.MoveInput, Is.EqualTo(Vector2.Zero));
        Assert.That(snapshot.PlayerPosition, Is.EqualTo(Vector3.Zero));
        Assert.That(snapshot.PlayerVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(snapshot.EnemyCount, Is.EqualTo(0));
        Assert.That(
            snapshot.Enemies,
            Is.SameAs(enemiesBefore),
            "The array is allocated once and never replaced — that is what makes refilling free.");

        // Stale slots survive by design. Zeroing them would be a capacity-sized write every
        // frame to erase data no reader is allowed to look at.
        Assert.That(snapshot.Enemies[0].Id, Is.EqualTo(11));
    }

    [Test]
    public void ClearAndAdd_AllocateNothing()
    {
        var snapshot = new WorldSnapshot(16);

        // A frame's worth of refill, not a fill to capacity: the claim is that the per-frame
        // cycle costs nothing, which is the one that runs 60 times a second.
        AllocationAssert.None(() =>
        {
            snapshot.Clear();

            for (int i = 0; i < 8; i++)
            {
                ref EnemySense enemy = ref snapshot.AddEnemy();
                enemy.Id = i;
                enemy.Position = new Vector3(i, 0f, i);
                enemy.Velocity = Vector3.UnitX;
                enemy.PathDirectionToPlayer = Vector2.UnitY;
                enemy.HasLineOfSight = true;
            }
        });

        Assert.That(snapshot.EnemyCount, Is.EqualTo(8), "Sanity: the measured body really did fill the snapshot.");
    }
}

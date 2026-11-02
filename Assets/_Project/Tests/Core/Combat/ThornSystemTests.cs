using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>A line of thorns that arms, bites once as it rises, stands, and withers. JG-20a.</summary>
/// <remarks>Against a real <see cref="PlayerCombat"/>, for <see cref="LaneSystemTests"/>' reason.</remarks>
[TestFixture]
public sealed class ThornSystemTests
{
    private const string OathboundId = "character.oathbound";

    private const int EnemyCapacity = 8;

    private const float Damage = 20f;

    private const float Arm = 0.8f;

    private const float Stand = 4f;

    private const float Length = 8f;

    private const float HalfWidth = 0.5f;

    private const float Tolerance = 1e-3f;

    private static readonly Vector2 North = new Vector2(0f, 1f);

    private static readonly Vector3 Inside = new Vector3(0.2f, 0f, 4f);

    private RecordingEvents _events;
    private PlayerCombat _player;
    private ThornSystem _thorns;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _player = new PlayerCombat(Oathbound(), _events, new RecordingIntents(), EnemyCapacity);
        _thorns = new ThornSystem(_events);
    }

    // ---- Rule 1 -----------------------------------------------------------------------------------

    [Test]
    public void Open_Valid_PublishesArmed()
    {
        var origin = new Vector3(1f, 0f, 2f);

        // Not unit length: the wall runs along it, normalised.
        int id = _thorns.Open(origin, new Vector2(0f, -3f), Length, HalfWidth, Arm, Stand, Damage, 0f);

        Assert.That(id, Is.EqualTo(1), "Ids from 1, so 0 can mean refused.");

        ThornArmed armed = _events.Single<ThornArmed>();

        Assert.That(armed.Id, Is.EqualTo(id));
        Assert.That(armed.Origin, Is.EqualTo(origin));
        Assert.That(armed.Direction.X, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(armed.Direction.Y, Is.EqualTo(-1f).Within(Tolerance));
        Assert.That(armed.Length, Is.EqualTo(Length).Within(Tolerance));
        Assert.That(armed.HalfWidth, Is.EqualTo(HalfWidth).Within(Tolerance));
        Assert.That(armed.ArmSeconds, Is.EqualTo(Arm).Within(Tolerance));

        Assert.That(_thorns.ActiveCount, Is.EqualTo(1));
        Assert.That(_thorns.IsStandingAt(0), Is.False, "Arming, not standing.");
        Assert.That(_thorns.DirectionAt(0).Y, Is.EqualTo(-1f).Within(Tolerance));

        // Zero damage is a wall with no bite, and is allowed.
        Assert.That(
            _thorns.Open(origin, North, Length, HalfWidth, ThornSystem.MinArmSeconds, Stand, 0f, 0f),
            Is.EqualTo(2),
            "The minimum arm is allowed; ids are never reused.");
    }

    [Test]
    public void Open_ArmUnderPointSix_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _thorns.Open(Vector3.Zero, North, Length, HalfWidth, 0.59f, Stand, Damage, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _thorns.Open(Vector3.Zero, North, Length, HalfWidth, float.NaN, Stand, Damage, 0f));

        // The other doors, NaN first among them (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _thorns.Open(Vector3.Zero, Vector2.Zero, Length, HalfWidth, Arm, Stand, Damage, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _thorns.Open(Vector3.Zero, North, float.NaN, HalfWidth, Arm, Stand, Damage, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _thorns.Open(Vector3.Zero, North, Length, 0f, Arm, Stand, Damage, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _thorns.Open(Vector3.Zero, North, Length, HalfWidth, Arm, float.PositiveInfinity, Damage, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _thorns.Open(Vector3.Zero, North, Length, HalfWidth, Arm, Stand, -1f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _thorns.Open(new Vector3(float.NaN, 0f, 0f), North, Length, HalfWidth, Arm, Stand, Damage, 0f));
        Assert.Throws<ArgumentNullException>(() => new ThornSystem(null));
        Assert.Throws<ArgumentNullException>(() => _thorns.Tick(0f, Vector3.Zero, null));

        Assert.That(_events.Count<ThornArmed>(), Is.Zero, "Nothing published for a refused wall.");
    }

    // ---- Rule 2 -----------------------------------------------------------------------------------

    [Test]
    public void Rise_PlayerInside_DamagedOnce()
    {
        Open();

        float before = Hp();

        _thorns.Tick(Arm - 0.01f, Inside, _player);

        Assert.That(Hp(), Is.EqualTo(before), "Nothing while arming.");
        Assert.That(_events.Count<ThornRose>(), Is.Zero);

        _thorns.Tick(Arm, Inside, _player);

        Assert.That(before - Hp(), Is.EqualTo(Damage).Within(Tolerance));
        Assert.That(_events.Single<ThornRose>().Hit, Is.True);
        Assert.That(_thorns.IsStandingAt(0), Is.True);
    }

    [Test]
    public void Rise_PlayerOutside_NotHit()
    {
        Open();

        float before = Hp();

        // One sidestep out, halfway along; with a metre of height inside that does not count.
        _thorns.Tick(Arm, new Vector3(HalfWidth + 0.1f, 1f, 4f), _player);

        Assert.That(Hp(), Is.EqualTo(before));
        Assert.That(_events.Single<ThornRose>().Hit, Is.False, "It rises either way.");
    }

    [Test]
    public void Rise_RollIFrames_NoDamage()
    {
        Open();

        // The flag a roll raises for its i-frames (PlayerCombat's movement-skill gate).
        _player.Health.SetExternalInvulnerable(true);

        float before = Hp();

        _thorns.Tick(Arm, Inside, _player);

        Assert.That(Hp(), Is.EqualTo(before), "The roll answers the bite.");
        Assert.That(_events.Single<ThornRose>().Hit, Is.False);
    }

    // ---- Rule 3 -----------------------------------------------------------------------------------

    [Test]
    public void Stand_NoSecondBite_ThenWithers()
    {
        int id = Open();

        _thorns.Tick(Arm, new Vector3(5f, 0f, 0f), _player);

        float before = Hp();

        // Walking into the standing wall is the collider's business, not a second bite.
        _thorns.Tick(Arm + 1f, Inside, _player);
        _thorns.Tick(Arm + Stand - 0.01f, Inside, _player);

        Assert.That(Hp(), Is.EqualTo(before), "One bite, at the rise.");
        Assert.That(_thorns.ActiveCount, Is.EqualTo(1), "Still standing.");
        Assert.That(_events.Count<ThornWithered>(), Is.Zero);

        _thorns.Tick(Arm + Stand, Inside, _player);

        Assert.That(_thorns.ActiveCount, Is.Zero);
        Assert.That(_events.Single<ThornWithered>().Id, Is.EqualTo(id));
        Assert.That(_events.Count<ThornRose>(), Is.EqualTo(1));
    }

    // ---- Rule 4 -----------------------------------------------------------------------------------

    [Test]
    public void Full_OldestStandingWithers()
    {
        // Two arming at 0, the rest a second later; the first two rise while the rest still arm.
        Open(0f);
        Open(0f);

        for (int i = 2; i < ThornSystem.Capacity; i++)
        {
            Open(1f);
        }

        _thorns.Tick(Arm, new Vector3(50f, 0f, 50f), _player);

        Assert.That(_thorns.IsStandingAt(0), Is.True);
        Assert.That(_thorns.IsStandingAt(2), Is.False);

        int id = Open(Arm);

        Assert.That(id, Is.EqualTo(ThornSystem.Capacity + 1));
        Assert.That(_events.Single<ThornWithered>().Id, Is.EqualTo(1), "The oldest standing wall.");
        Assert.That(_thorns.ActiveCount, Is.EqualTo(ThornSystem.Capacity));
        Assert.That(_thorns.IsStandingAt(0), Is.True, "Wall 2 compacted into slot 0.");
        Assert.That(_thorns.IsStandingAt(ThornSystem.Capacity - 1), Is.False, "The new one arms last.");
    }

    [Test]
    public void Full_AllArming_Refuses()
    {
        for (int i = 0; i < ThornSystem.Capacity; i++)
        {
            Assert.That(Open(), Is.EqualTo(i + 1));
        }

        Assert.That(Open(), Is.Zero, "A promise is never dropped for a newcomer.");
        Assert.That(_thorns.ActiveCount, Is.EqualTo(ThornSystem.Capacity));
        Assert.That(_events.Count<ThornArmed>(), Is.EqualTo(ThornSystem.Capacity));
        Assert.That(_events.Count<ThornWithered>(), Is.Zero);
    }

    // ---- Rule 5 -----------------------------------------------------------------------------------

    [Test]
    public void Blocks_NearSegment_True_FarFalse()
    {
        Assert.That(_thorns.Blocks(Inside, 0f), Is.False, "Nothing live, nothing blocks.");

        // Diagonal, so the test is of the segment and not of an axis.
        var origin = new Vector3(2f, 0f, -1f);
        Vector2 along = Vector2.Normalize(new Vector2(1f, 1f));
        var across = new Vector3(along.Y, 0f, -along.X);
        var forward = new Vector3(along.X, 0f, along.Y);

        _thorns.Open(origin, along, Length, HalfWidth, Arm, Stand, Damage, 0f);

        Vector3 mid = origin + (forward * (Length * 0.5f));

        Assert.That(_thorns.Blocks(mid + new Vector3(0f, 3f, 0f), 0f), Is.True, "Arming counts; height does not.");
        Assert.That(_thorns.Blocks(mid + (across * (HalfWidth + 0.3f)), 0f), Is.False);
        Assert.That(_thorns.Blocks(mid + (across * (HalfWidth + 0.3f)), 0.4f), Is.True, "Clearance widens it.");
        Assert.That(_thorns.Blocks(origin - (forward * 0.4f), 0f), Is.True, "The ends are rounded.");
        Assert.That(_thorns.Blocks(origin + (forward * (Length + 0.6f)), 0f), Is.False, "Past the tip.");

        _thorns.Tick(Arm, new Vector3(50f, 0f, 50f), _player);

        Assert.That(_thorns.Blocks(mid, 0f), Is.True, "Standing counts.");

        _thorns.Tick(Arm + Stand, new Vector3(50f, 0f, 50f), _player);

        Assert.That(_thorns.Blocks(mid, 0f), Is.False, "Withered does not.");
        Assert.Throws<ArgumentOutOfRangeException>(() => _thorns.Blocks(mid, float.NaN));
    }

    // ---- Rule 6 -----------------------------------------------------------------------------------

    [Test]
    public void WitherAll_DropsArmingAndStanding()
    {
        Open(0f);
        _thorns.Tick(Arm, new Vector3(50f, 0f, 50f), _player);
        Open(Arm);

        Assert.That(_thorns.IsStandingAt(0), Is.True);
        Assert.That(_thorns.IsStandingAt(1), Is.False);

        float before = Hp();

        _thorns.WitherAll();

        Assert.That(_thorns.ActiveCount, Is.Zero);
        Assert.That(_events.Count<ThornWithered>(), Is.EqualTo(2));

        _thorns.Tick(Arm * 2f, Inside, _player);

        Assert.That(Hp(), Is.EqualTo(before), "A withered promise never bites.");
        Assert.That(_events.Count<ThornRose>(), Is.EqualTo(1), "Only the first one rose.");
    }

    [Test]
    public void Clear_PublishesWitheredInOrder()
    {
        Open();
        Open();
        Open();

        _thorns.Clear();

        IReadOnlyList<ThornWithered> withered = _events.Of<ThornWithered>();

        Assert.That(withered, Has.Count.EqualTo(3));
        Assert.That(withered[0].Id, Is.EqualTo(1));
        Assert.That(withered[1].Id, Is.EqualTo(2));
        Assert.That(withered[2].Id, Is.EqualTo(3));
        Assert.That(_thorns.ActiveCount, Is.Zero);
        Assert.That(Open(), Is.EqualTo(4), "Ids carry on past a boundary.");
    }

    // ---- Rule 7 -----------------------------------------------------------------------------------

    [Test]
    public void Tick_SwallowsBoth_RiseThenWither()
    {
        int id = Open();

        float before = Hp();

        _thorns.Tick(Arm + Stand + 1f, Inside, _player);

        Assert.That(before - Hp(), Is.EqualTo(Damage).Within(Tolerance), "The bite still lands.");
        Assert.That(_thorns.ActiveCount, Is.Zero);

        int rose = IndexOf<ThornRose>();
        int withered = IndexOf<ThornWithered>();

        Assert.That(rose, Is.GreaterThanOrEqualTo(0));
        Assert.That(withered, Is.GreaterThan(rose), "Rise first.");
        Assert.That(_events.Single<ThornWithered>().Id, Is.EqualTo(id));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var silent = new SilentEvents();
        var player = new PlayerCombat(Oathbound(), silent, new RecordingIntents(), EnemyCapacity);
        var thorns = new ThornSystem(silent);

        float now = 0f;

        // The player stands clear, so the walk, the rises and the withers are measured, not the bite.
        var clear = new Vector3(500f, 0f, 500f);

        AllocationAssert.None(() =>
        {
            now += 1f / 60f;

            thorns.Tick(now, clear, player);

            if (thorns.ActiveCount < ThornSystem.Capacity)
            {
                thorns.Open(Vector3.Zero, North, Length, HalfWidth, Arm, 0.5f, Damage, now);
            }

            thorns.Blocks(Inside, 0.3f);
        });

        Assert.That(now, Is.GreaterThan(Arm + 0.5f), "The probe ran long enough to cycle walls.");
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    private int Open(float now = 0f) =>
        _thorns.Open(Vector3.Zero, North, Length, HalfWidth, Arm, Stand, Damage, now);

    private int IndexOf<T>()
        where T : struct
    {
        IReadOnlyList<object> all = _events.All;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is T)
            {
                return i;
            }
        }

        return -1;
    }

    private float Hp() => _player.Health.Current + _player.Health.Shield;

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}

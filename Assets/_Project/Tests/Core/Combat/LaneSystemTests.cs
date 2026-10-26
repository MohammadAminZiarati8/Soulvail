using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Tests.Core.Fakes;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// A strip of ground that arms, bites once and closes — the line counterpart of a fissure.
/// JG-04e rules 1–3.
/// </summary>
/// <remarks>
/// Against a real <see cref="PlayerCombat"/>, for <see cref="ShockwaveAndFissureTests"/>' reason,
/// and on Old Croaker's shipped lash numbers, off <see cref="CroakerBehaviour"/>'s own constants.
/// </remarks>
[TestFixture]
public sealed class LaneSystemTests
{
    private const string OathboundId = "character.oathbound";

    private const int EnemyCapacity = 8;

    private const float Damage = 20f;

    private const float Arm = CroakerBehaviour.LashTelegraphSeconds;

    private const float Length = CroakerBehaviour.LashLength;

    private const float HalfWidth = CroakerBehaviour.LashHalfWidth;

    private const float Tolerance = 1e-3f;

    private RecordingEvents _events;
    private PlayerCombat _player;
    private LaneSystem _lanes;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _player = new PlayerCombat(Oathbound(), _events, new RecordingIntents(), EnemyCapacity);
        _lanes = new LaneSystem(_events);
    }

    // ---- Rule 1 -----------------------------------------------------------------------------------

    [Test]
    public void Fire_HitsInsideTheLane_MissesBeside()
    {
        // Diagonal on purpose, so the test is of the rectangle and not of an axis.
        Vector3 direction = Vector3.Normalize(new Vector3(1f, 0f, 1f));
        var origin = new Vector3(2f, 0f, -1f);

        var across = new Vector3(direction.Z, 0f, -direction.X);

        // Just beside the strip, halfway along: one sidestep's worth out.
        Vector3 beside = origin + (direction * (Length * 0.5f)) + (across * (HalfWidth + 0.1f));

        Open(origin, direction);

        Assert.That(TakenByFiring(beside), Is.Zero, "Beside the lane, the tongue misses.");
        Assert.That(_events.Count<LaneFired>(), Is.EqualTo(1), "It fires either way.");

        // Inside, near the far end and near the edge — with a metre of height that does not count.
        Vector3 inside = origin
            + (direction * (Length - 0.2f))
            + (across * (HalfWidth - 0.1f))
            + new Vector3(0f, 1f, 0f);

        Open(origin, direction);

        Assert.That(TakenByFiring(inside), Is.EqualTo(Damage).Within(Tolerance), "Inside, it hits.");

        // Behind the mouth and past the tip are outside the lane too.
        Open(origin, direction);
        Assert.That(TakenByFiring(origin - direction), Is.Zero, "Behind the mouth.");

        Open(origin, direction);
        Assert.That(TakenByFiring(origin + (direction * (Length + 0.2f))), Is.Zero, "Past the tip.");
    }

    [Test]
    public void Fire_DamagesOnce_ThenClosesAfterItsWindow()
    {
        var origin = Vector3.Zero;
        var direction = new Vector3(0f, 0f, 1f);
        var inside = new Vector3(0f, 0f, 4f);

        Open(origin, direction);

        float before = Hp();

        _lanes.Tick(Arm - 0.01f, inside, _player);

        Assert.That(Hp(), Is.EqualTo(before), "Nothing during the arm.");

        _lanes.Tick(Arm, inside, _player);

        Assert.That(before - Hp(), Is.EqualTo(Damage).Within(Tolerance));

        // Still standing in it: open, but not biting again.
        float afterBite = Hp();

        _lanes.Tick(Arm + LaneSystem.OpenSeconds - 0.01f, inside, _player);

        Assert.That(Hp(), Is.EqualTo(afterBite), "One bite.");
        Assert.That(_lanes.ActiveCount, Is.EqualTo(1), "Still open inside its 0.2 s.");
        Assert.That(LaneSystem.OpenSeconds, Is.EqualTo(0.2f).Within(Tolerance));

        _lanes.Tick(Arm + LaneSystem.OpenSeconds, inside, _player);

        Assert.That(_lanes.ActiveCount, Is.Zero);
        Assert.That(_events.Count<LaneClosed>(), Is.EqualTo(1));
    }

    // ---- Rule 2 -----------------------------------------------------------------------------------

    [Test]
    public void Open_PublishesArmed_FirePublishesFired()
    {
        var origin = new Vector3(1f, 0f, 2f);

        // Not unit length and with height: the lane runs along its XZ part, normalised.
        int id = _lanes.Open(origin, new Vector3(0f, 5f, -3f), Length, HalfWidth, Arm, Damage, 0f);

        Assert.That(id, Is.EqualTo(1), "Ids are issued from 1, so 0 can mean nobody.");

        LaneArmed armed = _events.Single<LaneArmed>();

        Assert.That(armed.Id, Is.EqualTo(id));
        Assert.That(armed.Origin, Is.EqualTo(origin));
        Assert.That(armed.Direction.X, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(armed.Direction.Y, Is.EqualTo(-1f).Within(Tolerance));
        Assert.That(armed.Length, Is.EqualTo(Length).Within(Tolerance));
        Assert.That(armed.HalfWidth, Is.EqualTo(HalfWidth).Within(Tolerance));
        Assert.That(armed.ArmSeconds, Is.EqualTo(Arm).Within(Tolerance));
        Assert.That(armed.ArmSeconds, Is.GreaterThanOrEqualTo(WardenBehaviour.MinTelegraphSeconds));

        Assert.That(_events.Count<LaneFired>(), Is.Zero);

        _lanes.Tick(Arm, new Vector3(50f, 0f, 50f), _player);

        Assert.That(_events.Single<LaneFired>().Id, Is.EqualTo(id));
        Assert.That(_lanes.HasFiredAt(0), Is.True);
    }

    // ---- Rule 3 -----------------------------------------------------------------------------------

    [Test]
    public void Clear_ClosesAll()
    {
        var inside = new Vector3(0f, 0f, 4f);

        Open(Vector3.Zero, new Vector3(0f, 0f, 1f));
        Open(Vector3.Zero, new Vector3(0f, 0f, 1f));

        float before = Hp();

        _lanes.Clear();

        Assert.That(_lanes.ActiveCount, Is.Zero);

        _lanes.Tick(Arm, inside, _player);

        Assert.That(Hp(), Is.EqualTo(before), "A cleared lane never bites.");
        Assert.That(_events.Count<LaneFired>(), Is.Zero);

        Assert.That(Open(Vector3.Zero, new Vector3(0f, 0f, 1f)), Is.EqualTo(1), "The ids restart with the run.");
    }

    [Test]
    public void Full_DropsTheNewLane()
    {
        for (int i = 0; i < LaneSystem.Capacity; i++)
        {
            Assert.That(Open(Vector3.Zero, new Vector3(1f, 0f, 0f)), Is.EqualTo(i + 1));
        }

        Assert.That(Open(Vector3.Zero, new Vector3(1f, 0f, 0f)), Is.Zero, "Dropped, not thrown.");
        Assert.That(_lanes.ActiveCount, Is.EqualTo(LaneSystem.Capacity));
        Assert.That(
            _events.Count<LaneArmed>(),
            Is.EqualTo(LaneSystem.Capacity),
            "Nothing published for the dropped lane.");

        _lanes.Tick(Arm + LaneSystem.OpenSeconds, new Vector3(50f, 0f, 50f), _player);

        Assert.That(_lanes.ActiveCount, Is.Zero, "The full table still drains.");
        Assert.That(Open(Vector3.Zero, new Vector3(1f, 0f, 0f)), Is.EqualTo(LaneSystem.Capacity + 1));
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    private int Open(Vector3 origin, Vector3 direction) =>
        _lanes.Open(origin, direction, Length, HalfWidth, Arm, Damage, 0f);

    /// <summary>
    /// Fires every armed lane against <paramref name="player"/>, closes them, and answers what the
    /// player lost. A fresh player each time, so i-frames from one bite never hide the next.
    /// </summary>
    private float TakenByFiring(Vector3 player)
    {
        _player = new PlayerCombat(Oathbound(), _events, new RecordingIntents(), EnemyCapacity);

        float before = Hp();

        _lanes.Tick(Arm, player, _player);
        _lanes.Tick(Arm + LaneSystem.OpenSeconds, player, _player);

        Assert.That(_lanes.ActiveCount, Is.Zero);

        _lanes.Clear();

        return before - Hp();
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

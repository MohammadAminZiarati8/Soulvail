using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

[TestFixture]
public sealed class BuriedKingBehaviourTests
{
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _king;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _world;
    private Vector3[] _ground;
    private Vector3 _hero;
    private Vector3 _position;
    private float _now;
    private float _lungeClearance;
    private bool _bodyBlocked;
    private bool _hasSight;
    private Vector2 _path;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        var character = new CharacterSpec(new ContentId("character.test"), new LocKey("character.test.name"),
            new LocKey("character.test.description"), 100f, new MovementSpec(3f, 0.06f, 0.08f, 720f),
            new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
        _player = new PlayerCombat(character, _events, _intents, 8);
        _projectiles = new ProjectileSystem(_events, 8);
        var catalog = new ContentCatalog(new[] { character }, Array.Empty<EnemySpec>());
        _enemies = new EnemySystem(catalog, _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), 8);
        _world = new WorldSnapshot(8);
        _position = Vector3.Zero;
        _hero = 8f * Vector3.UnitZ;
        _ground = new[] { _hero };
        _now = 0f;
        _lungeClearance = 0f;
        _bodyBlocked = false;
        _hasSight = true;
        _path = Vector2.Zero;
        _king = _enemies.SpawnBuriedKingPrototype(_position);
    }

    [TestCase(0f, 3f, 76f)]
    [TestCase(-3.4641016f, 2f, 76f)]
    [TestCase(3.4641016f, 2f, 76f)]
    [TestCase(3f, 0f, 100f)]
    [TestCase(0f, 4.1f, 100f)]
    public void Sweep_WedgeResolvesAtFarEdgeOnce(float x, float z, float expectedHp)
    {
        _hero = 3f * Vector3.UnitZ;
        Tick(0f);
        BuriedKingStateChanged tell = LastPose();
        Assert.That(tell.Pose, Is.EqualTo(BuriedKingPose.SweepTell));
        Assert.That(tell.End, Is.EqualTo(4f * Vector3.UnitZ));
        Assert.That(tell.SweepDegrees, Is.EqualTo(120f));
        Assert.That(tell.WarningSeconds, Is.EqualTo(0.85f));
        Assert.That(tell.CutSeconds, Is.EqualTo(0.16f));

        _hero = new Vector3(-x, 0f, z);
        Tick(0.84f);
        Assert.That(_intents.LastEnemyMove.FacingXZ, Is.EqualTo(Vector2.UnitY));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        _hero = new Vector3(x, 0f, z);
        Tick(0.02f);
        AssertPath(BuriedKingPose.SweepImpact, tell);
        Assert.That(LastPose().SweepDegrees, Is.EqualTo(tell.SweepDegrees));
        Assert.That(LastPose().CutSeconds, Is.EqualTo(tell.CutSeconds));
        Assert.That(_player.Health.Current, Is.EqualTo(expectedHp));

        Tick(1.39f);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SweepImpact));
        Assert.That(_events.Of<BuriedKingStateChanged>().Count(change => change.Pose == BuriedKingPose.SweepImpact), Is.EqualTo(1));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(expectedHp < 100f ? 1 : 0));
        _hero = 3f * Vector3.UnitZ;
        Tick(0.02f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SweepTell));
    }

    [Test]
    public void TwoSweeps_BackOffBeforeThird()
    {
        _hero = 3f * Vector3.UnitZ;
        _ground = new[] { _hero };
        Tick(0f);
        Tick(0.85f);
        Tick(1.4f);
        Tick(0.85f);
        Tick(1.4f);
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.Negative);
        Tick(0f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.CastTell));
    }

    [TestCase(8f, 0f)]
    [TestCase(4.2f, 3f)]
    public void Cast_RequiresNearbyOpenGround(float heroDistance, float groundDistance)
    {
        _hero = heroDistance * Vector3.UnitZ;
        _ground = new[] { groundDistance * Vector3.UnitZ };
        Tick(0f);
        Assert.That(_events.Of<BuriedKingStateChanged>(), Is.Empty);
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.Positive);
    }

    [Test]
    public void CastAndRecall_LockBothEndpoints()
    {
        Tick(0f);
        BuriedKingStateChanged cast = LastPose();
        Assert.That(cast.Pose, Is.EqualTo(BuriedKingPose.CastTell));
        Assert.That(cast.Start, Is.EqualTo(Vector3.Zero));
        Assert.That(cast.End, Is.EqualTo(_hero));
        Assert.That(cast.Width, Is.EqualTo(1.6f));
        Assert.That(cast.LandingRadius, Is.EqualTo(2f));

        _hero = new Vector3(5f, 0f, 6f);
        _ground = new[] { _hero };
        Tick(0.99f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.CastTell));
        Tick(0.02f);
        AssertPath(BuriedKingPose.Outbound, cast);
        Tick(0.5f);
        AssertPath(BuriedKingPose.Embedded, cast);
        Assert.That(LastPose().WeaponPosition, Is.EqualTo(cast.End));
        Assert.That(LastPose().LandingRadius, Is.EqualTo(cast.LandingRadius));

        Vector3 planted = _position;
        Tick(1.59f);
        AssertPlantedAndTargetable(planted);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Embedded));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        Tick(0.02f);
        AssertPlantedAndTargetable(planted);
        BuriedKingStateChanged recall = LastPose();
        Assert.That(recall.Pose, Is.EqualTo(BuriedKingPose.RecallTell));
        Assert.That(recall.Start, Is.EqualTo(cast.End));
        Assert.That(recall.End, Is.EqualTo(planted));
        Assert.That(recall.End, Is.EqualTo(cast.Start));

        _hero = -8f * Vector3.UnitX;
        Tick(0.99f);
        Assert.That(_position, Is.EqualTo(planted));
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.RecallTell));
        Tick(0.02f);
        AssertPath(BuriedKingPose.Returning, recall);
        Tick(0.1f);
        AssertPath(BuriedKingPose.Returning, recall);
        Vector3 expected = recall.Start + Vector3.Normalize(recall.End - recall.Start) * 1.8f;
        Assert.That(Vector3.Distance(LastPose().WeaponPosition, expected), Is.LessThan(0.001f));
        AssertPlantedAndTargetable(planted);
        Tick(0.5f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        AssertPlantedAndTargetable(planted);
    }

    [TestCase(1.99f, 84f)]
    [TestCase(2f, 84f)]
    [TestCase(2.01f, 100f)]
    public void Landing_HitsTheLockedCircleAtImpact(float offset, float expectedHp)
    {
        Tick(0f);
        Vector3 landing = LastPose().End;
        _hero = landing + new Vector3(offset, 1f, 0f);
        Tick(1f);
        Tick(0.4f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Outbound));
        Assert.That(_events.Of<PlayerDamaged>(), Is.Empty);
        Tick(0.06f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Embedded));
        Tick(0.4f);
        Assert.That(_player.Health.Current, Is.EqualTo(expectedHp));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(expectedHp < 100f ? 1 : 0));
    }

    [TestCase(false, 64f)]
    [TestCase(true, 80f)]
    public void Flights_AttemptOnlyOneHitPerPass(bool blockFirst, float expectedHp)
    {
        Tick(0f);
        Tick(1f);
        _hero = new Vector3(0.79f, 0f, 1f);
        _player.Health.SetExternalInvulnerable(blockFirst);
        Tick(0.12f);
        _player.Health.SetExternalInvulnerable(false);
        _hero = 3f * Vector3.UnitZ;
        Tick(0.12f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));

        _hero = new Vector3(1.5f, 0f, 8f);
        Tick(0.3f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Embedded));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
        BuriedKingStateChanged recall = AdvanceAndArmRecall();
        Assert.That(Vector3.Distance(recall.Start, recall.End), Is.GreaterThanOrEqualTo(2f));
        Tick(1f);
        _hero = Vector3.Lerp(recall.Start, recall.End, 0.15f);
        Tick(0.08f);
        _hero = Vector3.Lerp(recall.Start, recall.End, 0.6f);
        Tick(0.1f);
        Tick(0.5f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(2));
        Assert.That(_player.Health.Current, Is.EqualTo(expectedHp));
    }

    [Test]
    public void Dodge_LeavesStationaryRecovery()
    {
        Tick(0f);
        _hero = new Vector3(5f, 0f, 8f);
        Tick(1f);
        Tick(0.5f);
        BuriedKingStateChanged recall = AdvanceAndArmRecall();
        _hero = new Vector3(-8f, 0f, -8f);
        Tick(1f);
        Tick(0.7f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        Vector3 planted = _position;
        Tick(2.09f);
        Assert.That(_position, Is.EqualTo(planted));
        Assert.That(planted, Is.EqualTo(recall.End));
        Assert.That(_king.IsVulnerable, Is.True);
        Assert.That(_events.Of<PlayerDamaged>(), Is.Empty);
        Tick(0.02f);
        Assert.That(_intents.LastEnemyMove.Velocity.Length(), Is.Positive);
    }

    [Test]
    public void ThresholdDuringSequence_LatchesForNextCast()
    {
        _enemies.ApplyDamage(_king.Id, 220f, _now, _player);
        Assert.That(_king.Health.Fraction, Is.GreaterThan(0.66f));
        Tick(0f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.CastTell));

        CrossReclaimThreshold();
        Tick(1f);
        _king.Health.Heal(_king.Health.MaxHp.Value);
        Assert.That(_king.Health.Fraction, Is.EqualTo(1f));
        FinishCatch();
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));

        _hero = 8f * Vector3.UnitZ;
        _ground = new[] { _hero };
        Tick(2.09f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Tick(0.02f);
        FinishSpinDebut();
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.CastTell));
        Tick(1f);
        FinishCatch();
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
    }

    [TestCase(3.5f, 1)]
    [TestCase(-3.5f, 1)]
    [TestCase(-3.6f, 0)]
    public void Leap_StartsOneFullCircleAtTheObservedLanding(float offset, int expectedHits)
    {
        BuriedKingStateChanged leap = StartLunge();
        _hero = leap.End + 5f * Vector3.UnitX;
        Tick(1f);
        Tick(0.55f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.LungeTravel));
        // Model a small controller offset within the confirmed landing tolerance.
        _position += 0.02f * Vector3.UnitX;
        Tick(0f);

        BuriedKingStateChanged tell = LastPose();
        Assert.That(tell.Pose, Is.EqualTo(BuriedKingPose.SlamTell));
        Assert.That(tell.Start, Is.EqualTo(_position));
        Assert.That(tell.End, Is.EqualTo(tell.Start));
        Assert.That(tell.SlamRadius, Is.EqualTo(3.5f));
        Assert.That(tell.WarningSeconds, Is.EqualTo(0.90f));
        Assert.That(tell.CutSeconds, Is.EqualTo(0.22f));

        _hero = tell.Start + new Vector3(offset, 1f, 0f);
        Tick(0.89f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SlamTell));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        Tick(0.02f);
        AssertPath(BuriedKingPose.SlamImpact, tell);
        Assert.That(LastPose().SlamRadius, Is.EqualTo(tell.SlamRadius));
        Assert.That(LastPose().CutSeconds, Is.EqualTo(tell.CutSeconds));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(expectedHits));
        Assert.That(_player.Health.Current, Is.EqualTo(100f - 28f * expectedHits));
        Tick(2f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(expectedHits));
    }

    [Test]
    public void Slam_LeavesFullStationaryTargetableRecovery()
    {
        StartSlam();
        Vector3 planted = _position;
        _hero = planted + 3f * Vector3.UnitX;
        Tick(0.89f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SlamTell));
        AssertPlantedAndTargetable(planted);

        Tick(0.02f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SlamImpact));
        AssertPlantedAndTargetable(planted);
        Tick(2.39f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SlamImpact));
        AssertPlantedAndTargetable(planted);
        Tick(0.02f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SweepTell));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShatteredThreshold_DuringCastPreservesTheSelectedPhase(bool spinUnlocked)
    {
        if (spinUnlocked)
        {
            CrossReclaimThreshold();
        }

        Tick(0f);
        if (spinUnlocked)
        {
            FinishSpinDebut();
        }

        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.CastTell));
        SetKingHealthFraction(0.33f);
        Tick(1f);
        _king.Health.Heal(_king.Health.MaxHp.Value);
        FinishCatch();
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        _hero = _position - 8f * Vector3.UnitZ;
        _ground = new[] { _hero };
        Tick(2.09f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        Tick(0.02f);
        if (!spinUnlocked)
        {
            FinishSpinDebut();
        }

        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.CastTell));
        Tick(1f);
        FinishCatch();
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.FractureTell));
    }

    [TestCase(0.331f, BuriedKingPose.Held)]
    [TestCase(0.33f, BuriedKingPose.FractureTell)]
    [TestCase(0.32f, BuriedKingPose.FractureTell)]
    public void ShatteredThreshold_AppliesAtCastSelection(float fraction, BuriedKingPose catchPose)
    {
        SetKingHealthFraction(fraction);
        Tick(0f);
        FinishSpinDebut();
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.CastTell));
        Tick(1f);
        FinishCatch();
        Assert.That(LastPose().Pose, Is.EqualTo(catchPose));
    }

    [TestCase(0.5f, 0.79f, 1)]
    [TestCase(0.5f, -0.81f, 0)]
    [TestCase(-0.05f, 0f, 0)]
    [TestCase(1.05f, 0f, 0)]
    public void Fracture_ReusesLockedRecallRectangleAndHitsOnce(float along, float across, int expectedHits)
    {
        BuriedKingStateChanged recall = StartFracture();
        AssertPath(BuriedKingPose.FractureTell, recall);
        Assert.That(LastPose().FractureProgress, Is.Zero);

        Vector3 side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, recall.End - recall.Start));
        _position -= 5f * Vector3.UnitX;
        _hero = Vector3.Lerp(recall.Start, recall.End, along) + side * across + Vector3.UnitY;
        Tick(0.99f);
        AssertPath(BuriedKingPose.FractureTell, recall);
        Assert.That(LastPose().FractureProgress, Is.EqualTo(0.99f).Within(0.0001f));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        Tick(0.02f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        Assert.That(_player.Health.Current, Is.EqualTo(100f - 20f * expectedHits));
        Tick(0.8f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(expectedHits));
    }

    [Test]
    public void ShatteredReturn_LeavesFullFractureAndRecoveryWindows()
    {
        BuriedKingStateChanged recall = StartFracture();
        Vector3 planted = recall.End;
        _hero = Vector3.Lerp(recall.Start, planted, 0.9f);
        Tick(0.99f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.FractureTell));
        AssertPlantedAndTargetable(planted);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));

        Tick(0.02f);
        Assert.That(_player.Health.Current, Is.EqualTo(80f));
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        AssertPlantedAndTargetable(planted);
        Tick(2.697f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        AssertPlantedAndTargetable(planted);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
        Tick(0.01f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SweepTell));
    }

    [Test]
    public void Prototype_OnlyKingIsRegisteredAndVulnerable()
    {
        Tick(0f);
        _hero = new Vector3(3f, 0f, 8f);
        Tick(1f);
        Tick(0.5f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Embedded));
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1));
        Assert.That(_events.Single<EnemySpawned>().Id, Is.EqualTo(_king.Id));

        _player.Tick(0.2f, _now, _world, _enemies.Registry.Alive, Vector3.UnitZ);
        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(_king.Id));
        float hp = _king.Health.Current;
        _enemies.ApplyDamage(_king.Id, 10f, _now, _player);
        Assert.That(_king.Health.Current, Is.EqualTo(hp - 10f));
    }

    [TestCase(BuriedKingPose.SweepTell)]
    [TestCase(BuriedKingPose.CastTell)]
    [TestCase(BuriedKingPose.Outbound)]
    [TestCase(BuriedKingPose.Embedded)]
    [TestCase(BuriedKingPose.RecallTell)]
    [TestCase(BuriedKingPose.Returning)]
    [TestCase(BuriedKingPose.Held)]
    public void Death_ClearsEncounter(BuriedKingPose pose)
    {
        if (pose == BuriedKingPose.SweepTell)
        {
            _hero = 3f * Vector3.UnitZ;
        }

        Tick(0f);
        if (pose is BuriedKingPose.Outbound or BuriedKingPose.Embedded or BuriedKingPose.RecallTell
            or BuriedKingPose.Returning or BuriedKingPose.Held)
        {
            Tick(1f);
        }
        if (pose is BuriedKingPose.Embedded or BuriedKingPose.RecallTell or BuriedKingPose.Returning or BuriedKingPose.Held)
        {
            _hero = new Vector3(4f, 0f, 8f);
            Tick(0.5f);
        }
        if (pose is BuriedKingPose.RecallTell or BuriedKingPose.Returning or BuriedKingPose.Held)
        {
            AdvanceAndArmRecall();
        }
        if (pose is BuriedKingPose.Returning or BuriedKingPose.Held)
        {
            Tick(1f);
        }
        if (pose == BuriedKingPose.Held)
        {
            Tick(1f);
        }

        Assert.That(LastPose().Pose, Is.EqualTo(pose));
        AssertDeathClearsEncounter();
    }

    [Test]
    public void DeathDuringSlam_ClearsWarningAndPendingHit()
    {
        StartSlam();
        _hero = _position;
        AssertDeathClearsEncounter();
    }

    [Test]
    public void DeathDuringShatteredReturn_ClearsWarningAndPendingHits()
    {
        BuriedKingStateChanged recall = StartFracture();
        _hero = Vector3.Lerp(recall.Start, recall.End, 0.9f);
        AssertDeathClearsEncounter();
    }

    private void AssertDeathClearsEncounter()
    {
        int hits = _events.Count<PlayerDamaged>();
        _enemies.ApplyDamage(_king.Id, _king.Health.MaxHp.Value, _now, _player);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Cleared));
        Tick(8f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Cleared));
        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(hits));
    }

    [Test]
    public void Despawn_ClearsEncounter()
    {
        Tick(0f);
        Assert.That(_enemies.Despawn(_king.Id), Is.True);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Cleared));
        Assert.That(_enemies.Registry.AliveCount, Is.Zero);
    }

    [Test]
    public void Spawn_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new BuriedKingBehaviour(null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _enemies.SpawnBuriedKingPrototype(new Vector3(float.NaN, 0f, 0f)));
    }

    [TestCase(1f, 6f, 12f, BuriedKingPose.LungeTell)]
    [TestCase(0.661f, 3f, 12f, BuriedKingPose.SweepTell)]
    [TestCase(0.661f, 6f, 12f, BuriedKingPose.LungeTell)]
    [TestCase(0.661f, 4.1f, 8f, BuriedKingPose.LungeTell)]
    [TestCase(0.661f, 6f, 7.99f, BuriedKingPose.CastTell)]
    [TestCase(1f, 6f, float.PositiveInfinity, BuriedKingPose.CastTell)]
    [TestCase(1f, 6f, float.NaN, BuriedKingPose.CastTell)]
    public void LungeSelection_RequiresRangeAndValidClearance(float hp, float distance, float clearance,
        BuriedKingPose expected)
    {
        SetKingHealthFraction(hp);
        _hero = Vector3.UnitZ * distance;
        _ground[0] = _hero;
        _lungeClearance = clearance;
        Tick(0.01f);
        Assert.That(LastPose().Pose, Is.EqualTo(expected));
        if (expected == BuriedKingPose.LungeTell)
        {
            Assert.That(LastPose().End.Z, Is.EqualTo(8f));
        }
    }

    [TestCase(0.5f)]
    [TestCase(3f)]
    public void Lunge_ApproachRunsAndNormalPursuitWalks(float step)
    {
        _hero = 12f * Vector3.UnitZ;
        Tick(0f);
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(2.5f));
        _lungeClearance = 8f;
        Tick(step);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.LungeApproach));
        Assert.That(LastPose().WarningSeconds, Is.Zero);
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(MathF.Min(4f, 4f / step)));
        if (step < 1f)
        {
            Tick(step);
        }

        Vector3 planted = _position;
        Assert.That(_hero.Z - planted.Z, Is.EqualTo(8f).Within(0.00001f));

        Tick(0.01f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.LungeTell));
        Assert.That(LastPose().Start, Is.EqualTo(planted));
        Assert.That(_position, Is.EqualTo(planted));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void AfterLunge_WalksIntoSweepRangeBeforeRearmingTheLunge()
    {
        StartLunge();
        Tick(1f);
        Tick(0.55f);
        _hero = 13.5f * Vector3.UnitZ;
        Tick(0.01f);
        Tick(0.9f);
        Tick(1.5f);
        Tick(0.91f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Held));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(_king.MoveSpeed.Value * Vector3.UnitZ));

        Tick(0.01f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SweepTell));
        _hero = 16.25f * Vector3.UnitZ;
        Tick(0.85f);
        Tick(1.4f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.LungeTell));
    }

    [Test]
    public void Lunge_LockedWarningMatchesSingleHit_AfterNormalSpeedSideStep()
    {
        BuriedKingStateChanged warning = StartLunge();
        Assert.That(warning.WarningSeconds, Is.EqualTo(1f));
        Assert.That(warning.End, Is.EqualTo(8f * Vector3.UnitZ));
        Assert.That(warning.Width, Is.EqualTo(4f));

        for (int i = 0; i < 4; i++)
        {
            _hero += Vector3.UnitX * (3.2f * 0.25f);
            Tick(0.25f);
            Assert.That(_position, Is.EqualTo(Vector3.Zero));
        }

        BuriedKingStateChanged travel = LastPose();
        Assert.That(travel.Pose, Is.EqualTo(BuriedKingPose.LungeTravel));
        Assert.That(travel.WarningSeconds, Is.EqualTo(0.55f));
        Assert.That(travel.CutSeconds, Is.EqualTo(0.15f));
        Assert.That(travel.Start, Is.EqualTo(warning.Start));
        Assert.That(travel.End, Is.EqualTo(warning.End));
        Assert.That(travel.Width, Is.EqualTo(warning.Width));
        Tick(0.40f);
        Assert.That(_position.X, Is.Zero);
        Tick(0.15f);
        Assert.That(_position, Is.EqualTo(warning.End));
        Tick(0.01f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SlamTell));
    }

    [TestCase(2f, 4f, 74f)]
    [TestCase(-2f, 4f, 74f)]
    [TestCase(2.001f, 4f, 100f)]
    [TestCase(0f, 0f, 74f)]
    [TestCase(0f, -0.001f, 100f)]
    [TestCase(0f, 8f, 74f)]
    [TestCase(0f, 8.001f, 100f)]
    public void Lunge_LockedWarningMatchesSingleHit(float x, float z, float expectedHp)
    {
        StartLunge();
        Tick(1f);
        _hero = 2f * Vector3.UnitZ;
        Tick(0.25f);
        Assert.That(_position.Z, Is.GreaterThan(_hero.Z));
        Assert.That(_player.Health.Current, Is.EqualTo(100f), "Travel across the player deals no contact damage.");
        _hero = new Vector3(x, 0f, z);
        Tick(0.30f);
        Assert.That(_player.Health.Current, Is.EqualTo(100f), "The final movement must be confirmed before the hit.");
        Tick(0.01f);
        Assert.That(_player.Health.Current, Is.EqualTo(expectedHp));
        _hero = 6f * Vector3.UnitZ;
        Tick(0.89f);
        Assert.That(_player.Health.Current, Is.EqualTo(expectedHp));
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SlamTell));
        AssertPlantedAndTargetable(8f * Vector3.UnitZ);
    }

    [TestCase(0.40f)]
    [TestCase(0.55f)]
    public void Lunge_ConfirmationOrClearEndsAttack(float blockedStep)
    {
        StartLunge();
        Tick(1f);
        _bodyBlocked = true;
        Tick(blockedStep);
        Tick(0.01f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.LungeRecovery));
        Assert.That(LastPose().CutSeconds, Is.Zero);
        Assert.That(LastPose().WarningSeconds, Is.EqualTo(0.9f));
        Assert.That(_player.Health.Current, Is.EqualTo(100f));
        AssertPlantedAndTargetable(Vector3.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Lunge_DeathOrDespawnClearsPendingHit(bool despawn)
    {
        StartLunge();
        Tick(1f);
        Tick(0.55f);
        if (!despawn)
        {
            AssertDeathClearsEncounter();
            return;
        }

        Assert.That(_enemies.Despawn(_king.Id), Is.True);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Cleared));
        Tick(0.01f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Spin_ThresholdWaitsForFreshSelection(bool loseLeap)
    {
        _hero = 10f * Vector3.UnitZ;
        _lungeClearance = 8f;
        Tick(0f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.LungeApproach));
        SetKingHealthFraction(loseLeap ? 0.33f : 0.66f);
        _hero = 6f * Vector3.UnitZ;
        _lungeClearance = loseLeap ? 0f : 8f;
        Tick(0f);
        if (!loseLeap)
        {
            Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.LungeTell));
            _hero += 5f * Vector3.UnitX;
            Tick(1f);
            Tick(0.55f);
            Tick(0f);
            Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SlamTell));
            Tick(0.9f);
            Tick(2.39f);
            Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SlamImpact));
            Tick(0.02f);
        }

        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SpinTell));
        _hero = _position + 5f * Vector3.UnitZ;
        _lungeClearance = 8f;
        _king.Behaviour.Reset();
        _king.Health.Heal(_king.Health.MaxHp.Value);
        _events.Clear();
        Tick(0f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.LungeTell));
    }

    [Test]
    public void Spin_PursuitAndFootprintFollowBody()
    {
        CrossReclaimThreshold();
        _hasSight = false;
        Tick(0f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SpinTell));
        Assert.That(LastPose().WarningSeconds, Is.EqualTo(1.25f));
        Tick(1f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SpinTell));
        Assert.That(_position, Is.EqualTo(Vector3.Zero));
        Tick(0.25f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SpinActive));

        _bodyBlocked = true;
        _path = Vector2.UnitX;
        Tick(0.25f);
        Vector3 motion = _intents.LastEnemyMove.Velocity;
        Assert.That(motion.Length(), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(motion.X, Is.EqualTo(motion.Z).Within(0.0001f));
        Assert.That(_position, Is.EqualTo(Vector3.Zero));
        _bodyBlocked = false;
        Tick(0.25f);
        Assert.That(_position.X, Is.EqualTo(0.5f).Within(0.0001f));
        _path = Vector2.Zero;
        _hero = _position;
        Tick(0.25f);
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(LastPose().Start, Is.EqualTo(_position));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Spin_PulsesUseCurrentCenters(bool hitch)
    {
        CrossReclaimThreshold();
        Tick(0f);
        Tick(1.25f);
        _bodyBlocked = true;
        _position = 6f * Vector3.UnitX;
        _hero = _position + 3.5f * Vector3.UnitZ + 2f * Vector3.UnitY;
        Tick(0.5f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        Tick(0.25f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SpinPulse));
        Assert.That(LastPose().Start, Is.EqualTo(_position));
        Assert.That(LastPose().WarningSeconds, Is.Zero);
        Assert.That(LastPose().SlamRadius, Is.EqualTo(3.5f));

        _hero = Vector3.Zero;
        Tick(1f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
        _hero = _position;
        _player.Health.SetExternalInvulnerable(true);
        Tick(1f);
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(2));
        Assert.That(_events.Of<PlayerDamaged>()[^1].Blocked, Is.True);
        Assert.That(_player.Health.Current, Is.EqualTo(90f));
        _player.Health.SetExternalInvulnerable(false);
        Tick(hitch ? 2f : 1f);
        if (!hitch)
        {
            Tick(1f);
        }

        Assert.That(_events.Of<BuriedKingStateChanged>().Count(change => change.Pose == BuriedKingPose.SpinPulse),
            Is.EqualTo(hitch ? 4 : 5));
        float expectedHp = hitch ? 80f : 70f;
        Assert.That(_player.Health.Current, Is.EqualTo(expectedHp));
        Tick(0.25f);
        Assert.That(_player.Health.Current, Is.EqualTo(expectedHp));
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SpinRecovery));
    }

    [TestCase("complete")]
    [TestCase("death")]
    [TestCase("despawn")]
    public void Spin_EndClearsEffectsAndRecovers(string ending)
    {
        CrossReclaimThreshold();
        Tick(0f);
        Tick(1.25f);
        if (ending == "death")
        {
            AssertDeathClearsEncounter();
        }
        else if (ending == "despawn")
        {
            Assert.That(_enemies.Despawn(_king.Id), Is.True);
            Tick(5f);
            Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.Cleared));
            Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        }
        else
        {
            Tick(5f);
            Assert.That(LastPose().WarningSeconds, Is.EqualTo(1.6f));
            Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SpinRecovery));
            Vector3 planted = _position;
            _hero = planted + 3f * Vector3.UnitZ;
            Tick(1.59f);
            AssertPlantedAndTargetable(planted);
            Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
            Tick(0.02f);
            Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SweepTell));
        }
    }

    private BuriedKingStateChanged StartLunge()
    {
        _hero = 6f * Vector3.UnitZ;
        _lungeClearance = 12f;
        Tick(0.01f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.LungeTell));
        return LastPose();
    }

    private void Tick(float dt)
    {
        _world.Clear();
        _world.PlayerPosition = _hero;
        _world.AddEnemy() = new EnemySense
        {
            Id = _king.Id,
            Position = _position,
            HasLineOfSight = _hasSight,
            PathDirectionToPlayer = _path,
            KingLungeClearance = _lungeClearance
        };
        _enemies.Ingest(_world);
        _player.Blackboard.PlayerPosition = _hero;
        _intents.Clear();
        _now += dt;
        _enemies.Tick(new EnemyTickContext(dt, _now, _player, _intents, _events, _projectiles, _enemies,
            openGround: _ground));
        if (_intents.EnemyMoves.Count > 0 && !_bodyBlocked)
        {
            _position += _intents.LastEnemyMove.Velocity * dt;
        }
    }

    private BuriedKingStateChanged AdvanceAndArmRecall()
    {
        Tick(1.6f);
        return LastPose();
    }

    private void CrossReclaimThreshold()
    {
        _enemies.ApplyDamage(_king.Id, _king.Health.Current - 429f, _now, _player);
        Assert.That(_king.Health.Fraction, Is.EqualTo(0.66f));
    }

    private void StartSlam()
    {
        BuriedKingStateChanged leap = StartLunge();
        _hero = leap.End + 5f * Vector3.UnitX;
        Tick(1f);
        Tick(0.55f);
        Tick(0f);
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SlamTell));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    private BuriedKingStateChanged StartFracture()
    {
        SetKingHealthFraction(0.33f);
        Tick(0f);
        FinishSpinDebut();
        Tick(1f);
        BuriedKingStateChanged recall = FinishCatch();
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.FractureTell));
        AssertPlantedAndTargetable(recall.End);
        return recall;
    }

    private void FinishSpinDebut()
    {
        Assert.That(LastPose().Pose, Is.EqualTo(BuriedKingPose.SpinTell));
        Vector3 bait = _hero;
        _hero = _position + Vector3.UnitX * 20f;
        Tick(1.25f);
        Tick(5f);
        _hero = bait;
        Tick(1.6f);
    }

    private void SetKingHealthFraction(float fraction)
    {
        float target = fraction * _king.Health.MaxHp.Value;
        _enemies.ApplyDamage(_king.Id, _king.Health.Current - target, _now, _player);
        Assert.That(_king.Health.Fraction, Is.EqualTo(fraction).Within(0.000001f));
    }

    private BuriedKingStateChanged FinishCatch()
    {
        _hero = new Vector3(4f, 0f, 8f);
        Tick(1f);
        BuriedKingStateChanged recall = AdvanceAndArmRecall();
        Vector3 side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, recall.End - recall.Start));
        _hero = recall.End + 5f * side;
        Tick(1f);
        Tick(1f);
        return recall;
    }

    private void AssertPlantedAndTargetable(Vector3 planted)
    {
        Assert.That(_position, Is.EqualTo(planted));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_king.IsVulnerable, Is.True);
        _player.Tick(0.2f, _now, _world, _enemies.Registry.Alive, Vector3.UnitZ);
        Assert.That(_player.Targeter.CurrentTargetId, Is.EqualTo(_king.Id));
    }

    private BuriedKingStateChanged LastPose() => _events.Of<BuriedKingStateChanged>()[^1];

    private void AssertPath(BuriedKingPose pose, BuriedKingStateChanged locked)
    {
        BuriedKingStateChanged current = LastPose();
        Assert.That(current.Pose, Is.EqualTo(pose));
        Assert.That(current.Start, Is.EqualTo(locked.Start));
        Assert.That(current.End, Is.EqualTo(locked.End));
        Assert.That(current.Width, Is.EqualTo(locked.Width));
    }
}

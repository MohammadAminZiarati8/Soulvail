using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>BT-03 rule 5: after a strike the hawk climbs back to its circle.</summary>
[TestFixture]
public sealed class HawkClimbTests
{
    private const int Capacity = 8;
    private const float DiveAt = 1.5f;
    private const float StrikeAt = DiveAt + HawkSystem.DiveSeconds;
    private static readonly ContentId Light = new("enemy.climb_light");

    private RecordingEvents _events;
    private PlayerCombat _combat;
    private PlayerStats _stats;
    private EnemySystem _enemies;
    private HawkSystem _hawk;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        var intents = new RecordingIntents();
        var character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 10f, 2f, 8f, 360f, 0.4f),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.None, 8f, 0.25f, 6f, 0.15f, 0f, 0f, 0.2f));
        var enemy = new EnemySpec(Light, new LocKey("enemy.test.name"), 1000f, 2f, 1, 4, 5f,
            false, 8f, 1.2f, 0.2f, 0.2f, 30f, EnemyBehaviourKind.Static, isLight: true);
        _combat = new PlayerCombat(character, _events, intents, Capacity);
        _stats = new PlayerStats(_combat, new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));
        _enemies = new EnemySystem(new ContentCatalog(new[] { character }, new[] { enemy }), _events,
            new FixedRandom(), new DepthScaling(Scalings.Design()), Capacity);
        _hawk = new HawkSystem(_events, _stats);
    }

    [Test]
    public void Hawk_AfterStrike_NotOnCircle_ReachesItIn0_4s()
    {
        EnemyAgent target = Marked(2f);
        _hawk.Call(0f, 2f, 0.4f);
        Tick(DiveAt);
        Tick(StrikeAt);
        Assert.That(_events.Of<HawkStruck>(), Has.Count.EqualTo(1));
        Assert.That(_hawk.Position, Is.EqualTo(target.Position), "Starts at the strike point.");

        Tick(StrikeAt + HawkSystem.ClimbSeconds / 2f);
        Vector3 mid = _hawk.Position;
        Assert.That(mid.Y, Is.GreaterThan(HawkSystem.Height / 2f).And.LessThan(HawkSystem.Height), "Height eases up.");
        Assert.That(new Vector2(mid.X, mid.Z).Length(), Is.Not.EqualTo(HawkSystem.CircleRadius).Within(0.05f));

        Tick(StrikeAt + HawkSystem.ClimbSeconds + 0.001f);
        Assert.That(_hawk.Position.Y, Is.EqualTo(HawkSystem.Height));
        Assert.That(new Vector2(_hawk.Position.X, _hawk.Position.Z).Length(),
            Is.EqualTo(HawkSystem.CircleRadius).Within(0.0001f));
    }

    [Test]
    public void Hawk_MidClimb_NoDive()
    {
        // A 0.3 s interval puts the next dive due 0.2 s into the 0.4 s climb.
        _stats.Resolve(PlayerStat.HawkDiveInterval).Add(new Modifier(ModifierKind.Flat, -1.2f, this));
        Marked(2f);
        _hawk.Call(0f, 2f, 0.4f);
        const float dive = 0.3f;
        const float strike = dive + HawkSystem.DiveSeconds;
        Tick(dive);
        Tick(strike);
        Assert.That(_events.Of<HawkStruck>(), Has.Count.EqualTo(1));

        Tick(strike + 0.1f);
        Tick(strike + HawkSystem.ClimbSeconds - 0.01f);
        Assert.That(_events.Of<HawkDove>(), Has.Count.EqualTo(1));

        Tick(strike + HawkSystem.ClimbSeconds + 0.001f);
        Assert.That(_events.Of<HawkDove>(), Has.Count.EqualTo(2));
    }

    private void Tick(float now) => _hawk.Tick(0.01f, now, Vector3.Zero, _enemies, _combat);

    private EnemyAgent Marked(float x)
    {
        EnemyAgent enemy = _enemies.Spawn(Light, x * Vector3.UnitX);
        _enemies.Mark(enemy.Id, 10f);
        return enemy;
    }
}

using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>JG-13c rules 2–5 and 8, one row per rule.</summary>
[TestFixture]
public sealed class OpenGroundPickerTests
{
    private const string OathboundId = "character.oathbound";
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private RecordingEvents _events;
    private RecordingIntents _intents;
    private ProjectileSystem _projectiles;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, 32);
        _projectiles = new ProjectileSystem(_events, 8);
        var body = new EnemySpec(new ContentId("enemy.test"), new LocKey("enemy.test.name"),
            10f, 0f, 1, 4, 12f, false, 0f, 1f, 0.6f, 0.6f, 40f, EnemyBehaviourKind.Static);
        _enemies = new EnemySystem(new ContentCatalog(new[] { Oathbound() }, new[] { body }),
            _events, new FixedRandom(), new DepthScaling(Scalings.Design()), 32);
    }

    [Test]
    public void Pick_RefusesEveryUnclearSpot()
    {
        // Height never enters an XZ query; exact boundaries qualify, corpses do not block.
        _enemies.Spawn(new ContentId("enemy.test"), new Vector3(5.4f, -200f, 0f));
        EnemyAgent dead = _enemies.Spawn(new ContentId("enemy.test"), new Vector3(-4f, 0f, 0f));
        _enemies.ApplyDamage(dead.Id, 100f, 0f, _player);
        var reserved = new[] { new Vector3(0f, -200f, 5.4f), new Vector3(-5.5f, 0f, 0f) };
        var good = new[] { new Vector3(-4f, 100f, 0f), new Vector3(0f, 100f, -8f) };
        var ground = new[]
        {
            new Vector3(3.9f, 0f, 0f), new Vector3(8.1f, 0f, 0f),
            new Vector3(4f, 100f, 0f), new Vector3(0f, 100f, 4f),
            new Vector3(0f, 100f, -4f), good[0], good[1],
        };
        var ctx = Context(ground);
        var player = new Vector3(0f, -200f, -1.1f);
        for (int i = 0; i < 100; i++)
        {
            var picker = new OpenGroundPicker(new FixedRandom(i / 100f).Spawn);
            Assert.That(picker.TryPick(ctx, Vector3.Zero, player, reserved, out Vector3 spot), Is.True);
            Assert.That(spot, Is.EqualTo(good[i < 50 ? 0 : 1]));
        }

        // Player clearance is inclusive at exactly 3 m.
        ctx = Context(new[] { new Vector3(0f, 100f, -4.1f), good[1] });
        Assert.That(new OpenGroundPicker(new FixedRandom(0f).Spawn)
            .TryPick(ctx, Vector3.Zero, player, reserved, out Vector3 boundary), Is.True);
        Assert.That(boundary, Is.EqualTo(ctx.OpenGround[0]));
    }

    [Test]
    public void Pick_SameSeedSameSpot()
    {
        var ctx = Context(new[] { new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f),
            new Vector3(6f, 0f, 0f), new Vector3(8f, 0f, 0f) });
        var generator = new Random(713);
        float draw = (float)generator.NextDouble();
        var first = new FixedRandom(713, draw);
        var second = new FixedRandom(713, draw);
        Assert.That(new OpenGroundPicker(first.Spawn).TryPick(ctx, Vector3.Zero, Vector3.Zero,
            ReadOnlySpan<Vector3>.Empty, out Vector3 a), Is.True);
        Assert.That(new OpenGroundPicker(second.Spawn).TryPick(ctx, Vector3.Zero, Vector3.Zero,
            ReadOnlySpan<Vector3>.Empty, out Vector3 b), Is.True);
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Is.EqualTo(ctx.OpenGround[1 + (int)(draw * 3)]));
        Assert.That(first.Capture().Spawn, Is.EqualTo(1ul));
        Assert.That(second.Capture().Spawn, Is.EqualTo(1ul));
    }

    [Test]
    public void Pick_Crowded_FallsBackToTheBand()
    {
        var ctx = Context(new[] { new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f),
            new Vector3(5f, 0f, 0f), new Vector3(8.1f, 0f, 0f) });
        foreach (float draw in new[] { 0f, 0.5f, 0.999f })
        {
            var random = new FixedRandom(draw);
            Assert.That(new OpenGroundPicker(random.Spawn).TryPick(ctx, Vector3.Zero,
                new Vector3(4.5f, 0f, 0f), ReadOnlySpan<Vector3>.Empty, out Vector3 spot), Is.True);
            Assert.That(spot, Is.EqualTo(ctx.OpenGround[1 + (int)(draw * 2)]));
            Assert.That(random.Capture().Spawn, Is.EqualTo(1ul));
        }
    }

    [Test]
    public void Pick_NoGround_FalseWithoutADraw()
    {
        var random = new FixedRandom(0.25f);
        var picker = new OpenGroundPicker(random.Spawn);
        foreach (Vector3[] ground in new[] { Array.Empty<Vector3>(), new[] { new Vector3(2f, 0f, 0f) } })
        {
            var ctx = Context(ground);
            Assert.That(picker.TryPick(ctx, Vector3.Zero, Vector3.Zero,
                ReadOnlySpan<Vector3>.Empty, out _), Is.False);
            Assert.That(random.Capture().Spawn, Is.Zero);
        }
    }

    [Test]
    public void Pick_AllocatesNothing()
    {
        var ground = new Vector3[300];
        for (int i = 0; i < ground.Length; i++)
        {
            float angle = i * (2f * MathF.PI / ground.Length);
            ground[i] = new Vector3(6f * MathF.Cos(angle), 0f, 6f * MathF.Sin(angle));
        }

        var ctx = Context(ground);
        var picker = new OpenGroundPicker(new FixedRandom().Spawn);
        var reserved = new[] { ground[0] };
        _enemies.Spawn(new ContentId("enemy.test"), ground[150]);
        Assert.That(picker.TryPick(ctx, Vector3.Zero, Vector3.Zero, reserved, out _), Is.True);
        AllocationAssert.None(() => picker.TryPick(ctx, Vector3.Zero, Vector3.Zero, reserved, out _));
    }

    private EnemyTickContext Context(Vector3[] ground) => new EnemyTickContext(
        0.1f, 0f, _player, _intents, _events, _projectiles, _enemies, openGround: ground);

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

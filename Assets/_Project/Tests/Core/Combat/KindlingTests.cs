using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// CH §3.3's Kindling: the ramp, the cap, the reset, and the hits that do not count. M6-07a rules 3–7
/// and 11.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two halves, and the seam between them is the class that owns the doors.</b> The first half
/// drives a bare <see cref="Kindling"/> on a bare <see cref="Stat"/>, because the arithmetic is the
/// passive's alone. The second half builds a <see cref="PlayerCombat"/> the way a run does — the
/// Emberwright's orb, or a Censer carrying the same block — and asks which of the ways damage leaves
/// the player's hands reaches the ramp: a swing and an orb do, a dash and a zone do not.
/// </para>
/// <para>
/// <b>The orb's numbers are written out</b>, 17 damage and a 3 m blast, the way
/// <c>TimeToKillTests</c> writes the Bone Bolt's: <c>Soulvail.Tests.Core</c> cannot open
/// <c>Emberwright.asset</c> (M0-10), and <c>EmberwrightTests.Orb_IsTheRuledWeapon</c> is the row in
/// the assembly that can. The two meet at the number.
/// </para>
/// </remarks>
[TestFixture]
public sealed class KindlingTests
{
    private const string EmberwrightId = "character.emberwright";
    private const string HuskId = "enemy.husk";
    private const string DescentId = "mode.descent";
    private const int Seed = 7;

    /// <summary>CH §3.3's +2 % a stack.</summary>
    private const float PerStack = 0.02f;

    /// <summary>CH §3.3's +60 %, as a count of <see cref="PerStack"/>.</summary>
    private const int MaxStacks = 30;

    /// <summary>M6-07a rule 2's ruled orb — CH §3.3 says 30.</summary>
    private const float OrbDamage = 17f;

    private const float OrbsPerSecond = 1.5f;
    private const float OrbRange = 12f;
    private const float OrbSpeed = 25f;
    private const float OrbRadius = 3f;
    private const float OrbDamageFrame = 0.15f;

    private const float CenserDamage = 13f;

    /// <summary>The shipped Husk's 36 — GD §8.1, and what GD §6.2's band is written against.</summary>
    private const float HuskMaxHp = 36f;

    private const float PlayerMaxHp = 70f;
    private const float HitIFrames = 0.5f;

    private const int BandLow = 3;

    private const float Frame = 1f / 120f;
    private const int MaxFramesPerSwing = 400;
    private const int EnemyCapacity = 8;
    private const int ProjectileCapacity = 8;

    private const float Tolerance = 1e-5f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private WorldSnapshot _snapshot;
    private List<EnemySense> _senses;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();

        _enemies = new EnemySystem(
            new ContentCatalog(new[] { Emberwright() }, new[] { Husk() }, new[] { Descent() }),
            _events,
            new FixedRandom(Seed),
            new DepthScaling(Scalings.Design()),
            EnemyCapacity);

        _snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };
        _senses = new List<EnemySense>();
        _now = 0f;
    }

    // ---- The spec ------------------------------------------------------------------------------------

    [Test]
    public void Spec_CarriesItsTwo()
    {
        var spec = new KindlingSpec(PerStack, MaxStacks);

        Assert.That(spec.PerStack, Is.EqualTo(PerStack));
        Assert.That(spec.MaxStacks, Is.EqualTo(MaxStacks));
        Assert.That(spec.MaxBonus, Is.EqualTo(0.60f).Within(Tolerance), "CH §3.3's +60 %.");
    }

    [Test]
    public void Spec_RefusesAnImpossibleBlock()
    {
        foreach (float perStack in new[] { 0f, -0.02f, float.NaN, float.PositiveInfinity })
        {
            var thrown = Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new KindlingSpec(perStack, MaxStacks), $"perStack {perStack}");

            Assert.That(thrown.ParamName, Is.EqualTo("perStack"));
        }

        foreach (int maxStacks in new[] { 0, -1 })
        {
            var thrown = Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new KindlingSpec(PerStack, maxStacks), $"maxStacks {maxStacks}");

            Assert.That(thrown.ParamName, Is.EqualTo("maxStacks"));
        }
    }

    // ---- Rules 3 and 4: the ramp ---------------------------------------------------------------------

    [Test]
    public void Kindling_StartsCold()
    {
        var damage = new Stat(OrbDamage);
        Kindling kindling = Passive(damage);

        Assert.That(kindling.Stacks, Is.Zero);
        Assert.That(kindling.Bonus, Is.Zero);
        Assert.That(damage.ModifierCount, Is.Zero, "a cold ramp puts nothing on the stat.");
        Assert.That(_events.Count<KindlingChanged>(), Is.Zero);
    }

    [Test]
    public void Kindling_OneHitIsOneStack()
    {
        var damage = new Stat(OrbDamage);
        Kindling kindling = Passive(damage);

        kindling.OnWeaponHitLanded();

        Assert.That(kindling.Stacks, Is.EqualTo(1));
        Assert.That(damage.Value, Is.EqualTo(OrbDamage * 1.02f).Within(Tolerance));

        KindlingChanged changed = _events.Single<KindlingChanged>();

        Assert.That(changed.Stacks, Is.EqualTo(1));
        Assert.That(changed.MaxStacks, Is.EqualTo(MaxStacks));
        Assert.That(changed.Bonus, Is.EqualTo(0.02f).Within(Tolerance));
    }

    [Test]
    public void Kindling_PoolsAdditively()
    {
        var damage = new Stat(OrbDamage);
        Kindling kindling = Passive(damage);

        Hits(kindling, MaxStacks);

        Assert.That(damage.Value / OrbDamage, Is.EqualTo(1.60f).Within(Tolerance),
            "ADR-0008's order: one PercentAdd pooled under one source.");

        Assert.That(damage.Value / OrbDamage, Is.Not.EqualTo(MathF.Pow(1.02f, MaxStacks)).Within(0.01f),
            "1.02³⁰ is ×1.81 — thirty compounding modifiers, which is not what CH §3.3 wrote.");

        Assert.That(damage.ModifierCount, Is.EqualTo(1), "one modifier, rewritten, never thirty.");
    }

    [Test]
    public void Kindling_SaturatesSilently()
    {
        var damage = new Stat(OrbDamage);
        Kindling kindling = Passive(damage);

        Hits(kindling, MaxStacks);

        int published = _events.Count<KindlingChanged>();

        Hits(kindling, 5);

        Assert.That(kindling.Stacks, Is.EqualTo(MaxStacks));
        Assert.That(damage.Value / OrbDamage, Is.EqualTo(1.60f).Within(Tolerance));
        Assert.That(_events.Count<KindlingChanged>(), Is.EqualTo(published),
            "Rule 4: a thirty-first hit is the ordinary state of a good stretch, not news.");
    }

    [Test]
    public void Kindling_BothNumbersAreStats()
    {
        Kindling kindling = Passive(new Stat(OrbDamage));

        Assert.That(typeof(Kindling).GetProperty(nameof(Kindling.PerStack)).PropertyType, Is.EqualTo(typeof(Stat)));
        Assert.That(typeof(Kindling).GetProperty(nameof(Kindling.MaxStacks)).PropertyType, Is.EqualTo(typeof(Stat)));

        Assert.That(kindling.PerStack.Value, Is.EqualTo(PerStack), "seeded from the spec.");
        Assert.That(kindling.MaxStacks.Value, Is.EqualTo(MaxStacks), "seeded from the spec.");

        // Rule 4 and M3-12a's sequence: the stat exists because the number is authored, and the
        // address arrives with the node that names it (M6-08). Asked by name, so the day M6-08 adds
        // one this row is where it says so.
        foreach (string name in Enum.GetNames(typeof(PlayerStat)))
        {
            Assert.That(name, Does.Not.Contain("Kindling"),
                $"PlayerStat.{name} addresses Kindling before any node does — M6-08's to add.");
        }
    }

    [Test]
    public void Kindling_ANegativeStatIsFlooredWhereItIsRead()
    {
        var damage = new Stat(OrbDamage);
        Kindling kindling = Passive(damage);
        var node = new object();

        kindling.PerStack.Add(new Modifier(ModifierKind.Flat, -0.52f, node));
        kindling.MaxStacks.Add(new Modifier(ModifierKind.Flat, -34f, node));

        Assert.That(kindling.PerStack.Value, Is.EqualTo(-0.5f).Within(Tolerance), "the premise.");
        Assert.That(kindling.MaxStacks.Value, Is.EqualTo(-4f).Within(Tolerance), "the premise.");

        Assert.DoesNotThrow(() => Hits(kindling, 10));

        Assert.That(kindling.Stacks, Is.Zero, "a cap below zero is a ramp that never starts.");
        Assert.That(kindling.Bonus, Is.Zero, "and a negative per-stack never punishes the stretch.");
        Assert.That(damage.Value, Is.EqualTo(OrbDamage));
    }

    [Test]
    public void Kindling_RemoveAllTakesTheWholeRamp()
    {
        var damage = new Stat(OrbDamage);
        Kindling kindling = Passive(damage);
        var node = new object();

        Hits(kindling, 12);
        damage.Add(new Modifier(ModifierKind.PercentAdd, 0.15f, node));

        Assert.That(damage.RemoveAll(kindling), Is.EqualTo(1), "the ramp is one modifier.");

        Assert.That(damage.Value, Is.EqualTo(OrbDamage * 1.15f).Within(Tolerance),
            "the node's +15 % survives: the ramp is taken back by its source and nothing else.");
    }

    [Test]
    public void Kindling_ResetIsSilent()
    {
        var damage = new Stat(OrbDamage);
        Kindling kindling = Passive(damage);

        Hits(kindling, 20);
        _events.Clear();

        kindling.Reset();

        Assert.That(kindling.Stacks, Is.Zero);
        Assert.That(kindling.Bonus, Is.Zero);
        Assert.That(damage.Value, Is.EqualTo(OrbDamage));
        Assert.That(_events.Count<KindlingChanged>(), Is.Zero, "the end of a run is not news.");

        // And through the owner, which is how a run reaches it.
        PlayerCombat player = Player(Orb());

        Hits(player.Kindling, 20);
        _events.Clear();

        player.Reset();

        Assert.That(player.Kindling.Stacks, Is.Zero);
        Assert.That(player.Weapon.Damage.Value, Is.EqualTo(OrbDamage));
        Assert.That(_events.Count<KindlingChanged>(), Is.Zero);
    }

    // ---- Rule 7: what breaks it ----------------------------------------------------------------------

    [Test]
    public void Kindling_ADamagedPlayerLosesEverything()
    {
        PlayerCombat player = Player(Orb());

        Hits(player.Kindling, 20);
        _events.Clear();

        DamageResult result = player.ApplyDamage(5f, 1f);

        Assert.That(result.Applied, Is.GreaterThan(0f), "the premise: the hit landed.");
        Assert.That(player.Kindling.Stacks, Is.Zero);
        Assert.That(player.Weapon.Damage.ModifierCount, Is.Zero, "the modifier is gone.");
        Assert.That(player.Weapon.Damage.Value, Is.EqualTo(OrbDamage));

        KindlingChanged changed = _events.Single<KindlingChanged>();

        Assert.That(changed.Stacks, Is.Zero);
        Assert.That(changed.MaxStacks, Is.EqualTo(MaxStacks));
        Assert.That(changed.Bonus, Is.Zero);
    }

    [Test]
    public void Kindling_ABlockedHitLeavesItStanding()
    {
        PlayerCombat player = Player(Orb());

        // A first hit at no stacks opens CC §7's half-second, and costs nothing: there was nothing
        // to lose. The stretch is built inside the window.
        player.ApplyDamage(5f, 1f);
        Hits(player.Kindling, 20);
        _events.Clear();

        DamageResult refused = player.ApplyDamage(5f, 1f + (HitIFrames / 2f));

        Assert.That(refused.Blocked, Is.True, "the premise: the i-frames ate it.");
        Assert.That(player.Kindling.Stacks, Is.EqualTo(20), "a dodge is not a hit.");
        Assert.That(_events.Count<KindlingChanged>(), Is.Zero);
    }

    // ---- Rule 5: one swing, one orb, one stack --------------------------------------------------------

    [Test]
    public void Kindling_AConeSwingIsOneStackHoweverManyItCaught()
    {
        PlayerCombat player = Player(Censer());
        int[] ids = FourHusks(z: 3f);

        TickToCone(player);

        player.ResolveConeHits(ids, _now, _enemies);

        Assert.That(DamagedCount(), Is.EqualTo(4), "the premise: the swing reached all four.");
        Assert.That(player.Kindling.Stacks, Is.EqualTo(1), "CH §3.3 counts hits, not bodies.");
    }

    [Test]
    public void Kindling_AnOrbIsOneStackHoweverManyItCaught()
    {
        PlayerCombat player = Player(Orb());
        var projectiles = new ProjectileSystem(_events, ProjectileCapacity);

        FourHusks(z: 10f);

        Land(projectiles, player, At(10f));

        Assert.That(DamagedCount(), Is.EqualTo(4), "the premise: the blast caught all four.");
        Assert.That(player.Kindling.Stacks, Is.EqualTo(1));
    }

    [Test]
    public void Kindling_AMissCostsNothing()
    {
        PlayerCombat player = Player(Orb());
        var projectiles = new ProjectileSystem(_events, ProjectileCapacity);

        FourHusks(z: 10f);
        Hits(player.Kindling, 10);
        _events.Clear();

        // Thirty metres off to the side of the four: the orb reaches nobody.
        Land(projectiles, player, new Vector3(30f, 0f, 10f));

        Assert.That(_events.Single<ProjectileImpacted>().Hit, Is.False, "the premise: a miss.");
        Assert.That(player.Kindling.Stacks, Is.EqualTo(10), "a miss neither adds nor resets.");
        Assert.That(_events.Count<KindlingChanged>(), Is.Zero);
    }

    // ---- Rule 6: what never counts --------------------------------------------------------------------

    [Test]
    public void Kindling_ADashHitIsNotAStack()
    {
        // A Charge that deals CC §5's 20, on a class carrying the block — the half of rule 6 the
        // Emberwright cannot reach, because its Blink deals nothing (rule 8).
        PlayerCombat player = Player(Censer(), new MovementSkillSpec(
            MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

        int[] ids = FourHusks(z: 3f);

        Hits(player.Kindling, 10);

        player.Charge.Request(_now);
        TickCombat(player);

        Assert.That(player.Charge.IsActive, Is.True, "the premise: a dash in flight.");

        player.ResolveChargeHits(ids.AsSpan(0, 2), _now, _enemies);

        Assert.That(DamagedCount(), Is.EqualTo(2), "the premise: the dash hit two.");
        Assert.That(player.Kindling.Stacks, Is.EqualTo(10));
    }

    [Test]
    public void Kindling_AZonePulseIsNotAStack()
    {
        PlayerCombat player = Player(Orb());
        var zones = new ZoneSystem(player.Health, player.Blackboard, _events);

        Hits(player.Kindling, 10);

        zones.Spawn(3f, 3f, 5f, 0.5f, 0f, new object());

        for (float t = 0f; t <= 3f; t += 0.1f)
        {
            zones.Tick(t);
        }

        // Pinned before M6-07b can make a burning zone reachable: nothing a zone does has a door
        // into the ramp, and six pulses a blink would otherwise fill it off the movement button.
        Assert.That(player.Kindling.Stacks, Is.EqualTo(10));
    }

    // ---- The ruling's cost, written down --------------------------------------------------------------

    [Test]
    public void Kindling_AtFullRampGoesUnderTheBand()
    {
        var damage = new Stat(OrbDamage);
        Kindling kindling = Passive(damage);

        int cold = HitsToKill(HuskMaxHp, damage.Value);

        Hits(kindling, MaxStacks);

        int hot = HitsToKill(HuskMaxHp, damage.Value);

        Assert.That(cold, Is.EqualTo(BandLow), "ceil(36 / 17): the bottom of GD §6.2's band.");

        Assert.That(hot, Is.EqualTo(2),
            "ceil(36 / 27.2). A full ramp is the one thing in the build allowed under GD §6.2's "
                + "floor, and this row is what makes a retune of either number a decision.");
    }

    // ---- Rule 11 --------------------------------------------------------------------------------------

    [Test]
    public void Kindling_AllocatesNothing()
    {
        var kindling = new Kindling(new KindlingSpec(PerStack, MaxStacks), new Stat(OrbDamage), new SilentEvents());

        AllocationAssert.None(
            () =>
            {
                for (int i = 0; i < MaxStacks + 5; i++)
                {
                    kindling.OnWeaponHitLanded();
                }

                kindling.OnPlayerDamaged();
                kindling.OnWeaponHitLanded();
                kindling.Reset();
            },
            iterations: 100_000 / (MaxStacks + 7));
    }

    // ---- Guard rows -----------------------------------------------------------------------------------

    [Test]
    public void Kindling_RefusesANullArgument()
    {
        var spec = new KindlingSpec(PerStack, MaxStacks);
        var damage = new Stat(OrbDamage);

        Assert.Throws<ArgumentNullException>(() => _ = new Kindling(null, damage, _events));
        Assert.Throws<ArgumentNullException>(() => _ = new Kindling(spec, null, _events));
        Assert.Throws<ArgumentNullException>(() => _ = new Kindling(spec, damage, null));
    }

    [Test]
    public void Kindling_IsNullOnAClassWithoutTheBlock()
    {
        CharacterSpec plain = Character(Censer(), kindling: false);

        Assert.That(plain.Kindling, Is.Null);
        Assert.That(new PlayerCombat(plain, _events, _intents, EnemyCapacity).Kindling, Is.Null,
            "rule 1: no block, no passive — the two shipped classes are byte for byte as they were.");
    }

    // ---- Fixtures ------------------------------------------------------------------------------------

    private Kindling Passive(Stat damage) =>
        new Kindling(new KindlingSpec(PerStack, MaxStacks), damage, _events);

    private static void Hits(Kindling kindling, int count)
    {
        for (int i = 0; i < count; i++)
        {
            kindling.OnWeaponHitLanded();
        }
    }

    private static int HitsToKill(float hp, float damagePerHit) =>
        (int)Math.Ceiling(hp / damagePerHit);

    private PlayerCombat Player(WeaponSpec weapon, MovementSkillSpec movementSkill = null) =>
        new PlayerCombat(Character(weapon, movementSkill: movementSkill), _events, _intents, EnemyCapacity);

    /// <summary>Four Husks side by side at <paramref name="z"/>, sensed, inside a 3 m blast.</summary>
    private int[] FourHusks(float z)
    {
        var ids = new int[4];

        for (int i = 0; i < ids.Length; i++)
        {
            EnemyAgent agent = _enemies.Spawn(new ContentId(HuskId), new Vector3((i - 1.5f) * 0.8f, 0f, z));

            ids[i] = agent.Id;
            _senses.Add(new EnemySense
            {
                Id = agent.Id,
                Position = agent.Position,
                Velocity = Vector3.Zero,
                PathDirectionToPlayer = Vector2.Zero,
                HasLineOfSight = true,
            });
        }

        return ids;
    }

    private int DamagedCount() => _events.Count<EnemyDamaged>();

    private static Vector3 At(float metres) => new(0f, 0f, metres);

    /// <summary>Fires an orb from the origin at <paramref name="target"/> and ticks past its arrival.</summary>
    private void Land(ProjectileSystem projectiles, PlayerCombat player, Vector3 target)
    {
        projectiles.Fire(
            new Projectile(new ContentId(EmberwrightId), 0, Vector3.Zero, target, OrbSpeed, OrbRadius, OrbDamage,
                ShotSide.AtEnemies),
            _now);

        projectiles.Tick(_now + 5f, Vector3.Zero, player, _enemies);
    }

    /// <summary>One frame in <c>RunSession.Tick</c>'s order: ingest, then combat.</summary>
    private void TickCombat(PlayerCombat player)
    {
        _now += Frame;

        _snapshot.EnemyCount = 0;

        for (int i = 0; i < _senses.Count; i++)
        {
            _snapshot.AddEnemy() = _senses[i];
        }

        _enemies.Ingest(_snapshot);

        player.Tick(Frame, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);
    }

    private void TickToCone(PlayerCombat player)
    {
        for (int i = 0; i < MaxFramesPerSwing; i++)
        {
            TickCombat(player);

            if (player.PendingConeRequestId >= 0)
            {
                return;
            }
        }

        throw new InvalidOperationException($"No damage frame within {MaxFramesPerSwing} ticks.");
    }

    /// <summary>M6-07a rule 2's orb, in code.</summary>
    private static WeaponSpec Orb() => new(
        WeaponKind.Projectile, OrbDamage, OrbsPerSecond, OrbRange, 360f, OrbDamageFrame, OrbSpeed, OrbRadius);

    /// <summary>CC §7's Censer — a cone, for the one door a swing has.</summary>
    private static WeaponSpec Censer() => new(WeaponKind.Cone, CenserDamage, 3f, 8f, 60f, 0.4f);

    private static CharacterSpec Emberwright() => Character(Orb());

    /// <summary>
    /// The Emberwright as rule 2 authors it, carrying <paramref name="weapon"/>. The Focus ramp is
    /// switched off with a multiplier of 1, so no fire-rate modifier shares a row with the damage one.
    /// </summary>
    private static CharacterSpec Character(
        WeaponSpec weapon,
        MovementSkillSpec movementSkill = null,
        bool kindling = true) => new(
        new ContentId(EmberwrightId),
        new LocKey("character.emberwright.name"),
        new LocKey("character.emberwright.description"),
        PlayerMaxHp,
        new MovementSpec(3.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        weapon,
        new FocusSpec(0.4f, 1f, 1f),
        movementSkill ?? new MovementSkillSpec(MovementSkillKind.Blink, 10f, 0.05f, 2f, 0.15f, 0f, 0f, 0.05f),
        null,
        HitIFrames,
        null,
        kindling ? new KindlingSpec(PerStack, MaxStacks) : null);

    private static EnemySpec Husk() => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        HuskMaxHp,
        2f,
        1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    private static ModeSpec Descent() => new(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());
}

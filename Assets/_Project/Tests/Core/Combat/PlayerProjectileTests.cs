using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// M5-01's ten rules from the weapon's end: what a <see cref="WeaponKind.Projectile"/> spec may
/// carry, that its cadence is the cone's exactly, that a damage frame offers a shot aimed by
/// <see cref="ProjectileLead"/>, that the shot reaches enemies and not the player, and that the run
/// puts it in the air on the tick it was decided.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Gravecaller's Bone Bolt throughout</b> — <see href="../../../../Docs/Characters.md">CH
/// §4</see>'s 9 damage at 4.0 a second, with M5-02's ruled 40 m/s and 0.8 m — so a failure reads as
/// "the weapon we are about to author stopped working" rather than as an arithmetic puzzle. <b>No
/// asset here is content:</b> M5-02 authors the class, and every spec in this fixture is built in
/// code, which is what keeps this task's review about the machinery.
/// </para>
/// <para>
/// <b>Mostly assembled by hand rather than driven through a run, and the reason is
/// <c>internal</c>.</b> <c>RunState.Combat</c>, <c>RunState.Enemies</c> and
/// <c>RunState.Projectiles</c> are all internal with no <c>InternalsVisibleTo</c> anywhere (AR
/// §18.2), and every observable this fixture needs — <c>PendingShot</c>, an enemy's hit points, the
/// blackboard's census — sits behind one of them. So the objects are built directly and ticked in
/// <c>RunSession.Tick</c>'s own order, and the one row that is about that order uses a real session
/// and asserts what a session exposes.
/// </para>
/// <para>
/// <b>Nothing here is visible in play, deliberately.</b> The Oathbound is the only authored class and
/// it is a cone, so every run this build plays behaves identically — see the spec's <i>Manual
/// verification</i>. These rows are the whole of the evidence until M5-02.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PlayerProjectileTests
{
    private const string GravecallerId = "character.gravecaller";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string DescentId = "mode.descent";
    private const int Seed = 99;

    // CH §4's Bone Bolt and M5-02's rule 3, which is the table this fixture is written against.
    private const float BoltDamage = 9f;
    private const float BoltsPerSecond = 4f;
    private const float BoltRange = 12f;
    private const float BoltSpeed = 40f;
    private const float BoltRadius = 0.8f;

    /// <summary>
    /// 0.15 of the interval — 37.5 ms into a 250 ms swing. A short windup, because a bolt that
    /// telegraphed as long as the Censer's arc would be a fourth of a second of standing still.
    /// </summary>
    private const float BoltDamageFrame = 0.15f;

    // CC §7's Censer, for the two rows that compare the kinds against each other.
    private const float SwingDamage = 13f;
    private const float SwingsPerSecond = 3f;
    private const float ConeRange = 8f;
    private const float ConeAngle = 60f;
    private const float ConeDamageFrame = 0.4f;

    /// <summary>A full circle — the only angle a <see cref="WeaponKind.Projectile"/> may carry.</summary>
    private const float FullCircle = 360f;

    private const float HuskMaxHp = 36f;
    private const float HuskXp = 12f;
    private const float PlayerMaxHp = 140f;

    /// <summary>Well inside the bolt's 12 m reach, and a 0.25 s flight at 40 m/s.</summary>
    private const float TargetRange = 10f;

    /// <summary>The flight of a bolt fired at <see cref="TargetRange"/>. 10 / 40.</summary>
    private const float FlightTime = TargetRange / BoltSpeed;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    /// <summary>
    /// How many frames a row waits for a damage frame before giving up. One bolt interval is 30
    /// frames at this rate and the targeter decides at 10 Hz, so this is several of both: long enough
    /// that a slow acquisition cannot fail a row, short enough that a weapon which has stopped firing
    /// fails it immediately.
    /// </summary>
    private const int MaxFramesPerSwing = 200;

    private const int EnemyCapacity = 8;
    private const int DeviceCap = 8;
    private const int ProjectileCapacity = 8;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private ContentCatalog _catalog;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _snapshot;

    /// <summary>The fixture's simulated clock — <c>RunState.Time</c>'s stand-in.</summary>
    private float _now;

    /// <summary>What the body reports about each enemy this frame, refilled into the snapshot.</summary>
    private List<EnemySense> _senses;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _catalog = new ContentCatalog(
            new[] { Gravecaller(), Oathbound() }, new[] { Husk() }, new[] { Descent() });

        _enemies = new EnemySystem(
            _catalog, _events, new FixedRandom(Seed), new DepthScaling(Scalings.Design()), EnemyCapacity);

        _player = new PlayerCombat(Gravecaller(), _events, _intents, EnemyCapacity);
        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);

        // The player stands at the origin all fixture long and never touches the stick, so every
        // enemy's spawn position is also its distance.
        _snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };
        _senses = new List<EnemySense>();
        _now = 0f;
    }

    // ---- Rule 2: the three shot numbers, and which kind may carry them --------------------------

    [Test]
    public void Spec_AProjectileWeaponRequiresItsShotNumbers()
    {
        // No speed: a bolt that never arrives anywhere.
        ArgumentOutOfRangeException noSpeed = Assert.Throws<ArgumentOutOfRangeException>(
            () => Bolt(shotSpeed: 0f));

        Assert.That(noSpeed.ParamName, Is.EqualTo("shotSpeed"));
        Assert.That(noSpeed.Message, Does.Contain("shotSpeed").And.Contain("Projectile"),
            "The message names the field and the kind — a designer looking at an asset needs both.");

        // No radius: a bolt that can only catch a mathematical point.
        ArgumentOutOfRangeException noRadius = Assert.Throws<ArgumentOutOfRangeException>(
            () => Bolt(shotRadius: 0f));

        Assert.That(noRadius.ParamName, Is.EqualTo("shotRadius"));
        Assert.That(noRadius.Message, Does.Contain("shotRadius").And.Contain("Projectile"));

        // And non-finite either way. Infinity is asked about separately from NaN because it passes a
        // `> 0` test: an infinite speed is a bolt that needs no leading, which is a lie about the
        // weapon, and an infinite radius is a bolt that hits the whole arena.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Bolt(shotSpeed: bad), $"speed {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(() => Bolt(shotRadius: bad), $"radius {bad}");
        }
    }

    [Test]
    public void Spec_AConeRefusesShotNumbers()
    {
        // Rule 2's forgotten-field guard, and the direction that matters: a cone carrying a shot
        // speed is a weapon somebody authored as a projectile and left as a cone. Ignoring the
        // number would ship a weapon that is not the one in the asset.
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => Censer(shotSpeed: 12f));

        Assert.That(thrown.ParamName, Is.EqualTo("shotSpeed"));
        Assert.That(thrown.Message, Does.Contain("Cone"));

        Assert.Throws<ArgumentOutOfRangeException>(() => Censer(shotRadius: 1.6f));

        // Exactly zero, not "at most zero": a negative number on a cone is as much a forgotten field
        // as a positive one, and NaN fails the equality with them.
        Assert.Throws<ArgumentOutOfRangeException>(() => Censer(shotSpeed: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Censer(shotRadius: float.NaN));
    }

    [Test]
    public void Spec_AProjectileRequiresAFullCircle()
    {
        // Rule 2's unread-number guard. PlayerCombat never reads an angle on a projectile weapon, so
        // 60 here is a designer's intention going nowhere — and 360 is the one value that says "the
        // angle does not gate this weapon", which is also what PlayerCombat.ConeAngle clamps to.
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => Bolt(coneAngleDeg: ConeAngle));

        Assert.That(thrown.ParamName, Is.EqualTo("coneAngleDeg"));
        Assert.That(thrown.Message, Does.Contain("360").And.Contain("Projectile"));

        Assert.DoesNotThrow(() => Bolt(coneAngleDeg: FullCircle));
    }

    [Test]
    public void Spec_TheShippedConeIsUnchanged()
    {
        // The six-argument constructor, exactly as forty-five fixtures and one asset call it. The
        // three new numbers are defaulted parameters, which is the whole of why the ripple of this
        // task is nothing — M4-01a rule 4's trade for the same reason.
        var spec = new WeaponSpec(
            WeaponKind.Cone, SwingDamage, SwingsPerSecond, ConeRange, ConeAngle, ConeDamageFrame);

        Assert.That(spec.ShotSpeed, Is.EqualTo(0f));
        Assert.That(spec.ShotRadius, Is.EqualTo(0f));
        Assert.That(spec.ShotSpread, Is.EqualTo(0f));

        // And nothing else moved.
        Assert.That(spec.Kind, Is.EqualTo(WeaponKind.Cone));
        Assert.That(spec.Damage, Is.EqualTo(SwingDamage));
        Assert.That(spec.SwingsPerSecond, Is.EqualTo(SwingsPerSecond));
        Assert.That(spec.Range, Is.EqualTo(ConeRange));
        Assert.That(spec.ConeAngleDeg, Is.EqualTo(ConeAngle));
        Assert.That(spec.DamageFrame, Is.EqualTo(ConeDamageFrame));
    }

    // ---- Rule 9: the spread is reserved ----------------------------------------------------------

    [Test]
    public void Spec_SpreadMustBeZero()
    {
        // Refused on both kinds, and refused before the kind is looked at: the field exists so that
        // the day a spread is wanted it is a validation change here rather than a widening of this
        // spec, and it is refused today so that it cannot first become a number nobody draws for.
        // A spread is a change to what a seed means (ADR-0011), which is why it is not simply
        // implemented.
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => Bolt(shotSpread: 0.1f));

        Assert.That(thrown.ParamName, Is.EqualTo("shotSpread"));

        Assert.Throws<ArgumentOutOfRangeException>(() => Censer(shotSpread: 0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bolt(shotSpread: -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bolt(shotSpread: float.NaN));
    }

    [Test]
    public void Spec_RefusesAnUndefinedKind()
    {
        // An undefined kind satisfies neither set of rule 2's shot-number rules, so nothing would
        // have checked them: it would construct with three unvalidated numbers on it and then land in
        // neither branch of PlayerCombat's damage frame — a class whose basic attack silently does
        // nothing. Refused at the door instead, which is a reversal of what WeaponKind's own remark
        // used to say and is written down there.
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => new WeaponSpec((WeaponKind)7, SwingDamage, SwingsPerSecond, ConeRange, ConeAngle, ConeDamageFrame));

        Assert.That(thrown.ParamName, Is.EqualTo("kind"));
    }

    // ---- Rule 1: the cadence does not change -----------------------------------------------------

    [Test]
    public void Weapon_AProjectileWeaponKeepsTheConesCadence()
    {
        // The same rate and the same damage frame on both kinds, so the only difference is the kind
        // itself. Rule 1 says Weapon.cs is not in the Files table and must not be edited — "a damage
        // frame is a damage frame; what changes is what PlayerCombat does with one" — and this is
        // that asserted rather than assumed.
        var cone = new Weapon(new WeaponSpec(
            WeaponKind.Cone, SwingDamage, BoltsPerSecond, ConeRange, ConeAngle, BoltDamageFrame));

        var projectile = new Weapon(new WeaponSpec(
            WeaponKind.Projectile, BoltDamage, BoltsPerSecond, BoltRange, FullCircle, BoltDamageFrame,
            BoltSpeed, BoltRadius));

        var coneFrames = new List<float>();
        var projectileFrames = new List<float>();

        // Ten seconds is forty swings at 4.0 a second — long enough that a drifting accumulator
        // would separate the two timelines by a whole frame, which is what this row would catch.
        for (int i = 1; i <= (int)(10f / Frame); i++)
        {
            float now = i * Frame;

            if (cone.Tick(Frame, now, targetInRange: true).DamageFrame)
            {
                coneFrames.Add(now);
            }

            if (projectile.Tick(Frame, now, targetInRange: true).DamageFrame)
            {
                projectileFrames.Add(now);
            }
        }

        Assert.That(projectileFrames.Count, Is.EqualTo(coneFrames.Count));
        Assert.That(projectileFrames.Count, Is.GreaterThan(30), "Sanity: both weapons actually fired.");
        Assert.That(projectileFrames, Is.EqualTo(coneFrames), "The same moments, not merely the same count.");
    }

    // ---- Rule 6: a damage frame offers a shot ----------------------------------------------------

    [Test]
    public void Combat_ADamageFrameOffersAShot()
    {
        EnemyAgent husk = Spawn(TargetRange);

        TickToShot();

        Assert.That(_player.PendingShot, Is.Not.Null);

        Projectile offered = _player.PendingShot.Value;

        Assert.That(offered.SpecId, Is.EqualTo(new ContentId(GravecallerId)),
            "The class's own id, so a view can pick this class's bolt.");
        Assert.That(offered.SourceId, Is.Zero, "Nobody: that field is an enemy id, and the player is not one.");
        Assert.That(offered.Side, Is.EqualTo(ShotSide.AtEnemies));
        Assert.That(offered.Origin, Is.EqualTo(_snapshot.PlayerPosition));
        Assert.That(offered.Speed, Is.EqualTo(BoltSpeed));
        Assert.That(offered.Radius, Is.EqualTo(BoltRadius));
        Assert.That(offered.Damage, Is.EqualTo(BoltDamage));

        // A stationary Husk, so the lead solves to the target itself — ProjectileLeadTests owns the
        // arithmetic, and this is the wiring: the aim point is the agent's position, not the
        // player's and not the origin.
        Assert.That(offered.Target, Is.EqualTo(husk.Position));

        // Taken once, and there is no way to look without consuming.
        Assert.That(_player.TryTakeShot(out Projectile taken), Is.True);
        Assert.That(taken.Target, Is.EqualTo(offered.Target));
        Assert.That(_player.PendingShot, Is.Null);
        Assert.That(_player.TryTakeShot(out _), Is.False);

        // And the body was asked nothing. An arrival is a point and a moment core computes itself,
        // so there is no wedge to resolve and no request outstanding — which is also what stops a
        // stale ReportConeHits from being believed by a class that never swings.
        Assert.That(_intents.ConeHits, Is.Empty);
        Assert.That(_player.PendingConeRequestId, Is.EqualTo(-1));
    }

    [Test]
    public void Combat_AConeWeaponOffersNoShot()
    {
        // The shipped Oathbound, whose Censer is the only authored weapon in the game.
        var oathbound = new PlayerCombat(Oathbound(), _events, _intents, EnemyCapacity);

        Spawn(5f);

        // Several swings at 3.0 a second: two whole seconds is six.
        for (int i = 0; i < (int)(2f / Frame); i++)
        {
            TickCombat(oathbound);

            Assert.That(oathbound.PendingShot, Is.Null);
        }

        Assert.That(_intents.ConeHits.Count, Is.GreaterThan(3), "The cone intents are unchanged.");
    }

    [Test]
    public void Combat_TheShotIsLedAtTheTargetsVelocity()
    {
        // A Husk walking across the line of fire at 3 m/s. The velocity reaches core only through
        // the snapshot (M1-06), which is why this row reports one rather than spawning and hoping.
        EnemyAgent husk = Spawn(TargetRange);
        var velocity = new Vector3(3f, 0f, 0f);

        Sense(husk.Id, husk.Position, velocity);

        TickToShot();

        Projectile shot = _player.PendingShot.Value;

        // Against the agent's *live* position and velocity, which is what the fixture read back out
        // of the agent rather than what it put in — so a row that stopped ingesting would fail here
        // rather than pass by comparing two copies of the same constant.
        Vector3 expected = ProjectileLead.Solve(
            _snapshot.PlayerPosition, husk.Position, husk.Velocity, BoltSpeed);

        Assert.That(husk.Velocity, Is.EqualTo(velocity), "Sanity: the walk was ingested.");
        Assert.That(shot.Target, Is.EqualTo(expected));
        Assert.That(shot.Target.X, Is.GreaterThan(husk.Position.X), "Led ahead, not at.");
    }

    // ---- Rule 8: no target, no shot ---------------------------------------------------------------

    [Test]
    public void Combat_NoTargetProducesNoShot()
    {
        // An empty arena. Weapon.Tick is handed targetInRange and refuses to start a swing without
        // it, so there is no path where the lead is solved against a target that does not exist.
        for (int i = 0; i < (int)(5f / Frame); i++)
        {
            TickCombat(_player);

            Assert.That(_player.PendingShot, Is.Null);
        }

        Assert.That(_events.Count<PlayerAttacked>(), Is.Zero, "And no damage frame, because no swing.");
    }

    [Test]
    public void Combat_ATargetThatDiedProducesNoShot()
    {
        EnemyAgent husk = Spawn(TargetRange);

        TickToSwingStart();

        Assert.That(_player.PendingShot, Is.Null, "Sanity: the swing has started and not yet landed.");

        // Killed mid-windup. A cone's "not a commitment" cuts both ways — the wedge would still
        // resolve against whoever is standing there — but a bolt aimed at where a corpse used to be
        // would land on nobody and look like a miss the player did not make.
        _enemies.ApplyDamage(husk.Id, HuskMaxHp, _now, _player);

        Assert.That(husk.IsAlive, Is.False, "Sanity: the target is a corpse.");

        // Well past the damage frame and past the end of the swing. The weapon fires its damage
        // frame regardless of what happened to the target — that is Weapon's rule, pinned by
        // Weapon_AProjectileWeaponKeepsTheConesCadence above — so what is being asserted here is
        // that OfferShot refused it, and that it refused without throwing on a corpse.
        for (int i = 0; i < (int)(0.3f / Frame); i++)
        {
            Assert.DoesNotThrow(() => TickCombat(_player));

            Assert.That(_player.PendingShot, Is.Null);
        }

        // The corpse is still registered, which is what makes the row about rule 8 rather than about
        // an empty arena: the span the damage frame walked did contain the target's id.
        Assert.That(_enemies.Registry.TryGet(husk.Id, out _), Is.True);
    }

    // ---- Rule 4: what an AtEnemies shot lands on -------------------------------------------------

    [Test]
    public void Shot_LandsOnEveryEnemyInTheRadius()
    {
        // Two inside the 0.8 m blast and one two metres out. A bolt is a small blast rather than a
        // single-target hit — the same bargain the Censer's arc makes, and what keeps one authored
        // damage number honest.
        EnemyAgent onIt = Spawn(TargetRange);
        EnemyAgent beside = Spawn(TargetRange, x: 0.7f);
        EnemyAgent clear = Spawn(TargetRange, x: 2f);

        Land(FireAtEnemies(At(TargetRange)));

        Assert.That(onIt.Health.Current, Is.EqualTo(HuskMaxHp - BoltDamage).Within(1e-4f));
        Assert.That(beside.Health.Current, Is.EqualTo(HuskMaxHp - BoltDamage).Within(1e-4f));
        Assert.That(clear.Health.Current, Is.EqualTo(HuskMaxHp).Within(1e-4f));

        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(2));

        ProjectileImpacted impacted = _events.Single<ProjectileImpacted>();

        Assert.That(impacted.Hit, Is.True);
        Assert.That(impacted.Position, Is.EqualTo(At(TargetRange)));
    }

    [Test]
    public void Shot_AtEnemiesNeverTouchesThePlayer()
    {
        Spawn(TargetRange);

        // The player standing exactly on the impact point. The AtEnemies branch never reads where
        // the player is, which is what the two asymmetrical branches buy.
        Land(FireAtEnemies(At(TargetRange)), playerPosition: At(TargetRange));

        Assert.That(_player.Health.Current, Is.EqualTo(PlayerMaxHp).Within(1e-4f));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(1), "Sanity: the bolt did land on something.");
    }

    [Test]
    public void Shot_AtPlayerIsUnchanged()
    {
        // The Spitter's shot, as ProjectileSystemTests already builds it: 12 m/s, a 1.6 m blast, and
        // an enemy id in SourceId. The widening asserted to be a widening.
        var spitterShot = new Projectile(
            new ContentId(HuskId), 1, Vector3.Zero, At(TargetRange), 12f, 1.6f, BoltDamage);

        Assert.That(spitterShot.Side, Is.EqualTo(ShotSide.AtPlayer), "The default, unwritten.");

        // An enemy standing on the impact point, to prove the other branch was not taken.
        EnemyAgent bystander = Spawn(TargetRange);

        _projectiles.Fire(spitterShot, 0f);

        Assert.That(_player.Blackboard.IncomingProjectiles, Is.Zero, "Not written until the step runs.");

        _projectiles.Tick(0f, At(TargetRange), _player, _enemies);

        Assert.That(_player.Blackboard.IncomingProjectiles, Is.EqualTo(1), "In the air: CC §6.4's Bulwark trigger.");

        _projectiles.Tick(TargetRange / 12f, At(TargetRange), _player, _enemies);

        Assert.That(_player.Health.Current, Is.EqualTo(PlayerMaxHp - BoltDamage).Within(1e-4f));
        Assert.That(_events.Single<ProjectileImpacted>().Hit, Is.True);
        Assert.That(_player.Blackboard.IncomingProjectiles, Is.Zero);
        Assert.That(bystander.Health.Current, Is.EqualTo(HuskMaxHp).Within(1e-4f), "An enemy shot hurts nobody else.");
    }

    [Test]
    public void Shot_AMissPublishesAnImpact()
    {
        // Two metres clear of a 0.8 m blast.
        Spawn(TargetRange, x: 2f);

        Land(FireAtEnemies(At(TargetRange)));

        ProjectileImpacted impacted = _events.Single<ProjectileImpacted>();

        // Published either way, because the view has to stop existing whether or not the shot hit
        // anything — and the impact VFX is what tells the player the bolt missed rather than
        // vanished.
        Assert.That(impacted.Hit, Is.False);
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
    }

    [Test]
    public void Shot_AKillFromABoltPaysXpOnce()
    {
        EnemyAgent husk = Spawn(TargetRange);

        // Down to 1 HP, so the bolt's 9 is a kill with no ambiguity about which hit did it.
        _enemies.ApplyDamage(husk.Id, HuskMaxHp - 1f, _now, _player);

        Assert.That(_enemies.DrainXp(), Is.Zero, "Sanity: nothing has died yet.");

        _events.Clear();

        Land(FireAtEnemies(At(TargetRange)));

        // Every death pays, whoever caused it (M3-01a rule 3): a bolt reaches the same one door a
        // cone, a Charge and a Bloater's own fuse do, so nobody has to ask who fired.
        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1));
        Assert.That(_enemies.DrainXp(), Is.EqualTo(HuskXp).Within(1e-4f));
        Assert.That(_enemies.DrainXp(), Is.Zero, "Drained once, not once per read.");
        Assert.That(_events.Single<ProjectileImpacted>().Hit, Is.True);
    }

    [Test]
    public void Shot_DoesNotDamageACorpse()
    {
        EnemyAgent husk = Spawn(TargetRange);

        _enemies.ApplyDamage(husk.Id, HuskMaxHp, _now, _player);

        Assert.That(husk.IsAlive, Is.False, "Sanity: dead, and still registered for CorpseTime.");

        _events.Clear();

        Land(FireAtEnemies(At(TargetRange)));

        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
        Assert.That(_events.Count<EnemyDied>(), Is.Zero, "And death does not arrive twice.");

        // Hit is false, because the corpse is skipped before the distance test rather than left to
        // ApplyDamage's own refusal: the damage would be right either way, but the impact would flash
        // a hit for a bolt that landed on a body already dissolving.
        Assert.That(_events.Single<ProjectileImpacted>().Hit, Is.False);
    }

    // ---- Rule 7: the run puts it in the air on the tick it was decided ---------------------------

    [Test]
    public void Run_TheShotIsInTheAirTheTickItWasDecided()
    {
        RunSession session = Session();

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(GravecallerId),
            Seed,
            1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(HuskId), At(TargetRange)) }),
            restore: null));

        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };

        for (int i = 0; i < MaxFramesPerSwing; i++)
        {
            session.Tick(snapshot);

            if (_events.Count<ProjectileFired>() == 0)
            {
                continue;
            }

            // The shot was decided by the combat step and fired immediately after it, and the
            // projectile step runs after the enemy behaviours — so at the end of the very tick that
            // decided it, the bolt is in the air and has not arrived. That one-frame grace is what
            // M2-07a rule 10 gives every Spitter's bolt, now given to the player's.
            Assert.That(session.State.InFlightProjectiles, Is.EqualTo(1));
            Assert.That(_events.Count<ProjectileImpacted>(), Is.Zero, "Not on the tick it left.");

            ProjectileFired fired = _events.Single<ProjectileFired>();

            Assert.That(fired.SpecId, Is.EqualTo(new ContentId(GravecallerId)));
            Assert.That(fired.FlightTime, Is.EqualTo(FlightTime).Within(1e-4f), "10 m at 40 m/s.");

            // And it arrives on a later tick, which is the other half of "in the air".
            for (int j = 0; j < MaxFramesPerSwing && _events.Count<ProjectileImpacted>() == 0; j++)
            {
                session.Tick(snapshot);
            }

            Assert.That(_events.Count<ProjectileImpacted>(), Is.GreaterThan(0), "It does land, later.");
            Assert.That(_events.Count<EnemyDamaged>(), Is.GreaterThan(0), "On the Husk it was aimed at.");

            return;
        }

        Assert.Fail($"No shot within {MaxFramesPerSwing} ticks. The projectile weapon never fired.");
    }

    // ---- Rules 5 and 10: the side, and the budget ------------------------------------------------

    [Test]
    public void Shot_RefusesAnUndefinedSide()
    {
        // An undefined side lands in neither branch of Land: a shot that flies, publishes its impact
        // and hurts nobody, which is the silence Projectile's other guards exist to refuse.
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => new Projectile(
                new ContentId(GravecallerId), 0, Vector3.Zero, At(TargetRange),
                BoltSpeed, BoltRadius, BoltDamage, (ShotSide)7));

        Assert.That(thrown.ParamName, Is.EqualTo("side"));
    }

    [Test]
    public void Shot_TickAllocatesNothing()
    {
        // Silent ports, because the fixture's recorders are what would allocate rather than the code
        // under test: RecordingEvents boxes every payload it is handed. Traps §7 — measured with
        // AllocationAssert, never a hand-rolled GC probe.
        var events = new QuietEvents();
        var intents = new SilentIntents();

        var enemies = new EnemySystem(
            new ContentCatalog(new[] { Gravecaller() }, new[] { Husk(maxHp: 1e9f) }, new[] { Descent() }),
            events,
            new FixedRandom(Seed),
            new DepthScaling(Scalings.Design()),
            EnemyCapacity);

        var player = new PlayerCombat(Gravecaller(), events, intents, EnemyCapacity);
        var projectiles = new ProjectileSystem(events, ProjectileCapacity);

        // Three dummies with a billion hit points each, so 10 000 landings never kill one and every
        // iteration measures the same path — the one where the walk finds somebody and damage lands.
        for (int i = 0; i < 3; i++)
        {
            enemies.Spawn(new ContentId(HuskId), new Vector3(i * 0.3f, 0f, TargetRange));
        }

        // Both sides in the air on every iteration, because the two branches are different code: the
        // player's bolt walks the registry, the Spitter's tests one position. Fired at 0 and landed
        // at 1, so each iteration resolves both and leaves the store empty for the next.
        AllocationAssert.None(() =>
        {
            projectiles.Fire(EnemyShot(), 0f);
            projectiles.Fire(PlayerShot(), 0f);

            projectiles.Tick(1f, At(TargetRange), player, enemies);
        });
    }

    // ---- Beyond the spec's Tests table -----------------------------------------------------------

    [Test]
    public void Shot_OnlyInboundShotsCountForBulwark()
    {
        // Beyond the Tests table, and it guards a rule the spec does not state: rule 4 widens what a
        // shot may hurt, and CombatBlackboard.IncomingProjectiles is CC §6.4's Bulwark trigger — "an
        // enemy projectile is inbound". Counted raw, a Gravecaller firing four bolts a second would
        // hold that predicate true for the whole run and auto-cast Bulwark off the player's own fire.
        // The field's name always meant one side; until a side existed there was no way for it to be
        // wrong.
        _projectiles.Fire(FireShot(ShotSide.AtEnemies), 0f);
        _projectiles.Fire(FireShot(ShotSide.AtEnemies), 0f);

        _projectiles.Tick(0f, Vector3.Zero, _player, _enemies);

        Assert.That(_projectiles.InFlightCount, Is.EqualTo(2), "Both are genuinely in the air.");
        Assert.That(_player.Blackboard.IncomingProjectiles, Is.Zero, "And none of it is incoming.");

        _projectiles.Fire(FireShot(ShotSide.AtPlayer), 0f);

        _projectiles.Tick(0f, Vector3.Zero, _player, _enemies);

        Assert.That(_projectiles.InFlightCount, Is.EqualTo(3));
        Assert.That(_player.Blackboard.IncomingProjectiles, Is.EqualTo(1), "Exactly the one fired at them.");
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>A point <paramref name="metres"/> up the +Z axis, on the ground.</summary>
    private static Vector3 At(float metres) => new(0f, 0f, metres);

    /// <summary>The Bone Bolt, with one number a row overrides to watch it be refused.</summary>
    private static WeaponSpec Bolt(
        float coneAngleDeg = FullCircle,
        float shotSpeed = BoltSpeed,
        float shotRadius = BoltRadius,
        float shotSpread = 0f) =>
        new(WeaponKind.Projectile, BoltDamage, BoltsPerSecond, BoltRange, coneAngleDeg, BoltDamageFrame,
            shotSpeed, shotRadius, shotSpread);

    /// <summary>CC §7's Censer, with one shot number a row sets to watch it be refused.</summary>
    private static WeaponSpec Censer(
        float shotSpeed = 0f,
        float shotRadius = 0f,
        float shotSpread = 0f) =>
        new(WeaponKind.Cone, SwingDamage, SwingsPerSecond, ConeRange, ConeAngle, ConeDamageFrame,
            shotSpeed, shotRadius, shotSpread);

    /// <summary>One shot of <paramref name="side"/>, aimed at <see cref="TargetRange"/>.</summary>
    private static Projectile FireShot(ShotSide side) => new(
        new ContentId(GravecallerId), 0, Vector3.Zero, At(TargetRange),
        BoltSpeed, BoltRadius, BoltDamage, side);

    /// <summary>The player's bolt, as the allocation row fires it.</summary>
    private static Projectile PlayerShot() => FireShot(ShotSide.AtEnemies);

    /// <summary>A Spitter's bolt, as the allocation row fires it — the other branch of Land.</summary>
    private static Projectile EnemyShot() => new(
        new ContentId(HuskId), 1, Vector3.Zero, At(TargetRange), 12f, 1.6f, BoltDamage);

    /// <summary>Spawns a Husk and reports its sense, so the agent is ingested from the first tick.</summary>
    private EnemyAgent Spawn(float z, float x = 0f)
    {
        EnemyAgent agent = _enemies.Spawn(new ContentId(HuskId), new Vector3(x, 0f, z));

        Sense(agent.Id, agent.Position, Vector3.Zero);

        return agent;
    }

    /// <summary>
    /// What the body will report about <paramref name="id"/> from now on. Replaces any earlier report
    /// for the same agent.
    /// </summary>
    private void Sense(int id, Vector3 position, Vector3 velocity)
    {
        var sense = new EnemySense
        {
            Id = id,
            Position = position,
            Velocity = velocity,
            PathDirectionToPlayer = Vector2.Zero,
            HasLineOfSight = true,
        };

        for (int i = 0; i < _senses.Count; i++)
        {
            if (_senses[i].Id == id)
            {
                _senses[i] = sense;
                return;
            }
        }

        _senses.Add(sense);
    }

    /// <summary>
    /// One frame of the player's fight, in <c>RunSession.Tick</c>'s order: ingest, then combat.
    /// </summary>
    /// <remarks>
    /// The shot is deliberately <em>not</em> taken here. Every row that cares reads
    /// <c>PendingShot</c> itself, which is the observable rule 6 is about;
    /// <see cref="Run_TheShotIsInTheAirTheTickItWasDecided"/> is the row that drives a real session
    /// and therefore the only one where a shot actually leaves.
    /// </remarks>
    private void TickCombat(PlayerCombat player)
    {
        _now += Frame;

        _snapshot.EnemyCount = 0;

        for (int i = 0; i < _senses.Count; i++)
        {
            _snapshot.AddEnemy() = _senses[i];
        }

        _enemies.Ingest(_snapshot);

        // Facing +Z, held: the body never turns in this fixture, and a projectile weapon's aim comes
        // from the lead rather than from the facing — which is one of the things that makes the two
        // kinds different at the damage frame and identical everywhere else.
        player.Tick(Frame, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);
    }

    /// <summary>Ticks until the weapon offers a shot.</summary>
    private void TickToShot()
    {
        for (int i = 0; i < MaxFramesPerSwing; i++)
        {
            TickCombat(_player);

            if (_player.PendingShot is not null)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"No shot within {MaxFramesPerSwing} ticks. The weapon has stopped firing — check that a "
                + "living Husk is inside the bolt's range.");
    }

    /// <summary>Ticks until a swing starts, which is <see cref="PlayerAttacked"/> and nothing else.</summary>
    private void TickToSwingStart()
    {
        int before = _events.Count<PlayerAttacked>();

        for (int i = 0; i < MaxFramesPerSwing; i++)
        {
            TickCombat(_player);

            if (_events.Count<PlayerAttacked>() > before)
            {
                return;
            }
        }

        throw new InvalidOperationException($"No swing within {MaxFramesPerSwing} ticks.");
    }

    /// <summary>Puts an <see cref="ShotSide.AtEnemies"/> bolt in the air, aimed at <paramref name="target"/>.</summary>
    private Projectile FireAtEnemies(Vector3 target)
    {
        var shot = new Projectile(
            new ContentId(GravecallerId), 0, Vector3.Zero, target,
            BoltSpeed, BoltRadius, BoltDamage, ShotSide.AtEnemies);

        _projectiles.Fire(shot, 0f);

        return shot;
    }

    /// <summary>Ticks the projectile step past <paramref name="shot"/>'s arrival.</summary>
    private void Land(in Projectile shot, Vector3 playerPosition = default)
    {
        float dx = shot.Target.X - shot.Origin.X;
        float dz = shot.Target.Z - shot.Origin.Z;

        float flightTime = MathF.Sqrt((dx * dx) + (dz * dz)) / shot.Speed;

        _projectiles.Tick(_now + flightTime, playerPosition, _player, _enemies);
    }

    private RunSession Session() => new(
        _catalog,
        new FixedRandom(Seed),
        _events,
        _intents,
        new RunRecorder(new FixedRandom(Seed), new FixedClock(default), _events),
        EnemyCapacity,
        DeviceCap,
        ProjectileCapacity);

    /// <summary>
    /// The Gravecaller as M5-02 will author it, built in code: CH §4's Bone Bolt and rule 3's ruled
    /// speed and radius.
    /// </summary>
    /// <remarks>
    /// <b>No Aegis</b>, because every row reading <c>Health.Current</c> would otherwise be a test of
    /// the shield instead — and CH §4 gives the class none anyway. The Focus ramp is switched off with
    /// a multiplier of 1, so the four bolts a second the rows are written against stay four.
    /// </remarks>
    private static CharacterSpec Gravecaller() => new(
        new ContentId(GravecallerId),
        new LocKey("character.gravecaller.name"),
        new LocKey("character.gravecaller.description"),
        PlayerMaxHp,
        new MovementSpec(3.2f, 0.06f, 0.08f, 720f),
        new TargetingSpec(BoltRange, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(
            WeaponKind.Projectile, BoltDamage, BoltsPerSecond, BoltRange, FullCircle, BoltDamageFrame,
            BoltSpeed, BoltRadius),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.22f, 2.5f, 0.15f, 0f, 0f, 0.05f),
        null,
        0.5f);

    /// <summary>The Oathbound of CC §7 — the one authored class, and the cone row's subject.</summary>
    private static CharacterSpec Oathbound() => new(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        PlayerMaxHp,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, SwingDamage, SwingsPerSecond, ConeRange, ConeAngle, ConeDamageFrame),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        null,
        0.5f);

    /// <summary>GD §8.1's Husk, with the one number the allocation row overrides.</summary>
    private static EnemySpec Husk(float maxHp = HuskMaxHp) => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp,
        3.5f,
        1,
        threatCost: 4,
        xpValue: HuskXp,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    /// <summary>
    /// Descent with an <b>empty roster</b>, for the reason every fixture that starts a run gives:
    /// <c>RunSession.Start</c> resolves every roster id before it announces a run, and what these rows
    /// spawn comes from a <c>SpawnPlan</c>.
    /// </summary>
    private static ModeSpec Descent() => new(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());

    /// <summary>An <see cref="IDomainEvents"/> that discards everything, so nothing boxes.</summary>
    /// <remarks>
    /// No type-handle branch at all, unlike <c>ConeHitsToDamageTests</c>' version: the allocation row
    /// gets every id it needs off <c>EnemySystem.Spawn</c>'s return value, so there is nothing to
    /// learn from a publish here.
    /// </remarks>
    private sealed class QuietEvents : IDomainEvents
    {
        public void Publish<T>(in T evt)
            where T : struct
        {
            // Deliberately nothing.
        }
    }

    /// <summary>An <see cref="IIntentSink"/> that keeps nothing, so no list can grow mid-measurement.</summary>
    /// <remarks>
    /// Nothing the allocation row runs writes an intent — a landing bolt asks the body no questions,
    /// which is the whole of rule 4 — so this is here because <c>PlayerCombat</c> requires one rather
    /// than because anything reaches it.
    /// </remarks>
    private sealed class SilentIntents : IIntentSink
    {
        public void PlayerMove(in PlayerMoveIntent intent)
        {
            // Deliberately nothing.
        }

        public void ConeHit(in ConeHitIntent intent)
        {
            // Deliberately nothing.
        }

        public void Charge(in ChargeIntent intent)
        {
            // Deliberately nothing.
        }

        public void EnemyMove(in EnemyMoveIntent intent)
        {
            // Deliberately nothing.
        }

        public void MinionMove(in EnemyMoveIntent intent)
        {
            // Deliberately nothing. No run in this fixture raises a Wight.
        }

        public void EnemyKnockback(in EnemyKnockbackIntent intent)
        {
            // Deliberately nothing.
        }
    }
}

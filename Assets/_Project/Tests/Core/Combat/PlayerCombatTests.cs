using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// The M1-08 spec's seven rules: candidate gathering, the target event, the facing, the damage
/// events, the run's new tick order, the motor's speed stat, and the allocation budget.
/// </summary>
/// <remarks>
/// <para>
/// The Oathbound's numbers throughout (CC §7) — 140 HP, a 30-point Aegis at 15/s after 4 s, 0.5 s
/// of i-frames, a 12 m acquire range at 10 Hz — so a failure reads as "the character we ship
/// stopped fighting" rather than as an arithmetic puzzle. Three rows override one number each,
/// and say which and why.
/// </para>
/// <para>
/// Enemies are reached only through <see cref="EnemyRegistry"/>, because that is the only route
/// there is: <c>EnemyAgent</c>'s constructor and its <c>Position</c> and <c>IsVulnerable</c>
/// setters are all <c>internal</c> and <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c>
/// (M0-10). Positions therefore come from <c>Spawn</c>, and "cannot be damaged right now" is
/// reached by killing the agent — the <c>IsAlive</c> half of the same rule, and the only half a
/// test can spell until M7-01's Warden lowers the other one.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PlayerCombatTests
{
    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";
    private const string HuskId = "enemy.husk";
    private const int Seed = 99;

    /// <summary>Room for every row's enemies, and the buffer <c>PlayerCombat</c> preallocates.</summary>
    private const int EnemyCapacity = 8;

    /// <summary>
    /// The device cap a run composes its stages under (M2-05). This fixture's mode has an empty
    /// roster, so nothing is composed and the director is inert — it is here because a run needs
    /// one, not because any row is about it.
    /// </summary>
    private const int DeviceCap = 8;

    /// <summary>
    /// Room for every shot a row here puts in the air, which is none: nothing fires one until
    /// M2-07b. Required by <c>RunSession</c> since M2-07a, and guarded positive, so it is a
    /// number rather than a zero.
    /// </summary>
    private const int ProjectileCapacity = 8;

    // CC §7, Survivability and Targeting.
    private const float MaxHp = 140f;
    private const float ShieldMax = 30f;
    private const float ShieldDelay = 4f;
    private const float ShieldRefill = 15f;
    private const float HitIFrames = 0.5f;
    private const float AcquireRange = 12f;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    /// <summary>
    /// The body's facing for rows that do not care about it: +Z, where a run starts. It only
    /// reaches <c>ConeHitIntent.FacingXZ</c>, which M1-10's own fixture is what tests.
    /// </summary>
    private static readonly Vector3 Facing = Vector3.UnitZ;

    private RecordingEvents _events;
    private RecordingIntents _intents;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
    }

    // ---- Rule 1 and 2: gathering and the targeting pass ----------------------------------------

    [Test]
    public void Tick_BuildsCandidates_FromEnemies()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        // Three candidates chosen so that each of the three fields gathering has to copy decides
        // the answer on its own. Distance alone would pick the Husk at 2 m; priority alone would
        // pick either of the eights; the range filter is what rules out the one at 20 m.
        EnemyAgent near = registry.Spawn(Enemy(priority: 1), new Vector3(0f, 0f, 2f));
        EnemyAgent important = registry.Spawn(Enemy(priority: 8), new Vector3(0f, 0f, 10f));
        EnemyAgent distant = registry.Spawn(Enemy(priority: 8), new Vector3(0f, 0f, 20f));

        combat.Tick(Frame, 0f, Snapshot(), registry.Alive, Facing);

        // 1 + 3 × (1 − 2/12) = 3.5 against 8 + 3 × (1 − 10/12) = 8.5, and the third scores 8.25
        // but never reaches the comparison — CC §3.1 step 1 gathers within acquireRange.
        Assert.That(combat.Targeter.CurrentTargetId, Is.EqualTo(important.Id));
        Assert.That(combat.Targeter.CurrentTargetId, Is.Not.EqualTo(near.Id));
        Assert.That(combat.Targeter.CurrentTargetId, Is.Not.EqualTo(distant.Id));
        Assert.That(combat.Targeter.IsCurrentBlocked, Is.False);
    }

    [Test]
    public void Tick_PublishesTargetChanged_OnlyOnChange()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();
        EnemyAgent husk = registry.Spawn(Enemy(), new Vector3(0f, 0f, 4f));

        for (int i = 0; i < 5; i++)
        {
            combat.Tick(Frame, i * Frame, Snapshot(), registry.Alive, Facing);
        }

        // One event for the acquisition, and silence for the four ticks that re-confirmed it. A
        // reticle rebuilt 120 times a second would be the visible symptom; the invisible one is
        // that "changed" would stop meaning anything.
        TargetChanged changed = _events.Single<TargetChanged>();

        Assert.That(changed.Id, Is.EqualTo(husk.Id));
        Assert.That(changed.IsFocused, Is.False, "Nothing has tapped anything — focus arrives in M1-09.");
        Assert.That(changed.IsBlocked, Is.False);
    }

    // ---- Rule 3: the facing --------------------------------------------------------------------

    [Test]
    public void FaceDirection_PointsAtTarget()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();
        registry.Spawn(Enemy(), new Vector3(3f, 0f, 4f));

        combat.Tick(Frame, 0f, Snapshot(), registry.Alive, Facing);

        Assert.That(combat.FaceDirection, Is.Not.Null);

        Vector3 face = combat.FaceDirection.Value;

        // The 3-4-5 triangle, so the normalisation is checkable by eye rather than by tolerance.
        Assert.That(face.X, Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(face.Y, Is.EqualTo(0f), "The facing never leaves the ground plane.");
        Assert.That(face.Z, Is.EqualTo(0.8f).Within(1e-5f));
    }

    [Test]
    public void FaceDirection_NullWithoutTarget()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        combat.Tick(Frame, 0f, Snapshot(), registry.Alive, Facing);

        // Null rather than a stale or invented direction: the motor reads it as "face the way you
        // are moving", which is the whole of M0's behaviour and still correct for an empty arena.
        Assert.That(combat.FaceDirection, Is.Null);
        Assert.That(combat.Targeter.CurrentTargetId, Is.EqualTo(-1));
        Assert.That(_events.Count<TargetChanged>(), Is.Zero, "Nothing changed — there was never a target.");
    }

    [Test]
    public void FaceDirection_HeldOnBlockedTarget()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();
        EnemyAgent corpse = registry.Spawn(Enemy(), new Vector3(0f, 0f, 5f));

        // A corpse still in the registry: CC §3.6's "cannot be damaged right now", reached through
        // the one half of it a test can spell. It sits in Alive until M1-11 despawns it.
        corpse.Health.ApplyDamage(1000f, 0f);

        combat.Tick(Frame, 0f, Snapshot(), registry.Alive, Facing);

        // Held, not dropped. The character looks at the thing it cannot hurt and the reticle says
        // so — that is the game saying "go around" in its own language, and swinging the facing
        // away would delete the sentence.
        Assert.That(combat.FaceDirection, Is.Not.Null);
        Assert.That(combat.FaceDirection.Value.Z, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(combat.Targeter.CurrentTargetId, Is.EqualTo(corpse.Id));
        Assert.That(combat.Targeter.IsCurrentBlocked, Is.True);
        Assert.That(combat.Blackboard.IsTargetBlocked, Is.True);
        Assert.That(_events.Single<TargetChanged>().IsBlocked, Is.True);
    }

    // ---- M2-12a, ledger row 12: the focus the gun has not taken --------------------------------

    [Test]
    public void Focus_OutOfRange_CarriesHeldId()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        EnemyAgent near = registry.Spawn(Enemy(), new Vector3(0f, 0f, 5f));
        EnemyAgent far = registry.Spawn(Enemy(), new Vector3(0f, 0f, 14f));

        // A second enemy inside the range is what makes this the state the ledger complained
        // about. With the far one alone, scoring finds nothing and SelectNearest hands the current
        // target back to it blocked — so the bright ring is already on it and nothing was ever
        // invisible. The silent case is precisely this one: the gun has somewhere else to go.
        combat.Targeter.Focus(far.Id);

        combat.Tick(Frame, 0f, Snapshot(), registry.Alive, Facing);

        TargetChanged changed = _events.Of<TargetChanged>()[^1];

        Assert.That(changed.Id, Is.EqualTo(near.Id), "The gun shoots what scoring picked.");
        Assert.That(changed.IsFocused, Is.False, "The target is not the focused one.");
        Assert.That(
            changed.HeldFocusId,
            Is.EqualTo(far.Id),
            "The tap at 14 m against a 12 m acquire range registered, and this field is the only "
                + "thing in the game that says so (CC §3.4, §3.5).");
    }

    [Test]
    public void Focus_InRange_HeldIdIsMinusOne()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        EnemyAgent husk = registry.Spawn(Enemy(), new Vector3(0f, 0f, 5f));

        combat.Targeter.Focus(husk.Id);

        combat.Tick(Frame, 0f, Snapshot(), registry.Alive, Facing);

        TargetChanged changed = _events.Of<TargetChanged>()[^1];

        Assert.That(changed.Id, Is.EqualTo(husk.Id));
        Assert.That(changed.IsFocused, Is.True);
        Assert.That(
            changed.HeldFocusId,
            Is.EqualTo(-1),
            "A focus the gun has taken is IsFocused, and saying it twice would have the reticle "
                + "draw two markers on one enemy.");
    }

    [Test]
    public void Focus_None_HeldIdIsMinusOne()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        registry.Spawn(Enemy(), new Vector3(0f, 0f, 5f));

        combat.Tick(Frame, 0f, Snapshot(), registry.Alive, Facing);

        Assert.That(_events.Single<TargetChanged>().HeldFocusId, Is.EqualTo(-1));
    }

    [Test]
    public void Focus_HeldAndFocusedAreNeverBothSet()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        EnemyAgent near = registry.Spawn(Enemy(), new Vector3(0f, 0f, 5f));
        EnemyAgent far = registry.Spawn(Enemy(), new Vector3(0f, 0f, 14f));

        WorldSnapshot snapshot = Snapshot();

        float now = 0f;

        // Every state the targeter can be walked into from a test: nothing focused, a focus in
        // range, a focus out of range, and a focus that has expired.
        now = Advance(combat, registry, snapshot, now, 0.2f);

        combat.Targeter.Focus(near.Id);
        now = Advance(combat, registry, snapshot, now, 0.2f);

        combat.Targeter.Focus(far.Id);
        now = Advance(combat, registry, snapshot, now, 0.2f);

        combat.Targeter.ClearFocus();
        now = Advance(combat, registry, snapshot, now, 0.2f);

        combat.Targeter.Focus(far.Id);
        Advance(combat, registry, snapshot, now, 3f);

        Assert.That(_events.Count<TargetChanged>(), Is.GreaterThan(1), "The walk has to have moved.");

        foreach (TargetChanged changed in _events.Of<TargetChanged>())
        {
            Assert.That(
                changed.IsFocused && changed.HeldFocusId >= 0,
                Is.False,
                "The two fields are two states of one thing, not two things. Both set at once "
                    + "would have the reticle draw its bright ring and its faint marker on the "
                    + "same enemy.");
        }
    }

    [Test]
    public void Focus_HeldIdPublishesOnEveryTransition()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        EnemyAgent near = registry.Spawn(Enemy(), new Vector3(0f, 0f, 5f));
        EnemyAgent far = registry.Spawn(Enemy(), new Vector3(0f, 0f, 14f));

        WorldSnapshot snapshot = Snapshot();

        float now = Advance(combat, registry, snapshot, 0f, 0.2f);

        Assert.That(_events.Of<TargetChanged>()[^1].HeldFocusId, Is.EqualTo(-1), "Nothing focused yet.");

        // 1. The tap lands out of range.
        combat.Targeter.Focus(far.Id);
        now = Advance(combat, registry, snapshot, now, 0.2f);

        Assert.That(_events.Of<TargetChanged>()[^1].HeldFocusId, Is.EqualTo(far.Id));

        // 2. Walking towards it brings it inside the acquire range, and the gun takes it. The
        // player moves rather than the enemy, because EnemyAgent.Position is internal and this
        // assembly has no InternalsVisibleTo (AR §18.2) — the distance is the difference, so
        // either end of it is the same experiment.
        snapshot.PlayerPosition = new Vector3(0f, 0f, 4f);
        now = Advance(combat, registry, snapshot, now, 0.2f);

        TargetChanged arrived = _events.Of<TargetChanged>()[^1];

        Assert.That(arrived.Id, Is.EqualTo(far.Id));
        Assert.That(arrived.IsFocused, Is.True);
        Assert.That(arrived.HeldFocusId, Is.EqualTo(-1), "It stopped being held the moment it was taken.");

        // 3. Walking away puts it back out of range, and the gun falls back to the near one.
        snapshot.PlayerPosition = Vector3.Zero;
        now = Advance(combat, registry, snapshot, now, 0.2f);

        TargetChanged left = _events.Of<TargetChanged>()[^1];

        Assert.That(left.Id, Is.EqualTo(near.Id));
        Assert.That(left.HeldFocusId, Is.EqualTo(far.Id));

        // 4. CC §3.4's two seconds run out and the focus expires on its own.
        Advance(combat, registry, snapshot, now, 2.5f);

        Assert.That(
            _events.Of<TargetChanged>()[^1].HeldFocusId,
            Is.EqualTo(-1),
            "The expiry moves FocusedTargetId, which is a member of the triple ChangedThisTick "
                + "watches — so there is no way for this field to go stale without a publish.");
    }

    [Test]
    public void Focus_TargeterUnchanged()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        EnemyAgent near = registry.Spawn(Enemy(), new Vector3(0f, 0f, 5f));
        EnemyAgent far = registry.Spawn(Enemy(), new Vector3(0f, 0f, 14f));

        WorldSnapshot snapshot = Snapshot();

        float now = Advance(combat, registry, snapshot, 0f, 0.2f);

        combat.Targeter.Focus(far.Id);
        now = Advance(combat, registry, snapshot, now, 0.2f);

        // Row 12 was never a targeting bug. Every field of the event is a function of the two ids
        // the targeter already published before M2-12a, so core decided nothing new — the game
        // simply gained a way to say what it had already decided. The other half of this claim is
        // the whole M1-04 TargeterTests fixture, which is green and unmodified.
        Assert.That(combat.Targeter.CurrentTargetId, Is.EqualTo(near.Id));
        Assert.That(combat.Targeter.FocusedTargetId, Is.EqualTo(far.Id));

        TargetChanged last = _events.Of<TargetChanged>()[^1];

        Assert.That(last.Id, Is.EqualTo(combat.Targeter.CurrentTargetId));
        Assert.That(
            last.HeldFocusId,
            Is.EqualTo(
                combat.Targeter.FocusedTargetId >= 0
                && combat.Targeter.FocusedTargetId != combat.Targeter.CurrentTargetId
                    ? combat.Targeter.FocusedTargetId
                    : -1),
            "Derived, not decided. The day this stops matching, something started keeping a "
                + "second opinion about what the player tapped.");
    }

    /// <summary>
    /// Ticks <paramref name="seconds"/> of frames and returns the clock it left off at.
    /// </summary>
    /// <remarks>
    /// Several frames rather than one, because <c>Targeter.Tick</c> re-selects on a 10 Hz cadence:
    /// a distance that changed between two ticks is not acted on until the next scheduled
    /// selection, so a single frame after moving the player would assert against the decision
    /// before it.
    /// </remarks>
    private static float Advance(
        PlayerCombat combat,
        EnemyRegistry registry,
        WorldSnapshot snapshot,
        float now,
        float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            combat.Tick(Frame, now, snapshot, registry.Alive, Facing);

            now += Frame;
        }

        return now;
    }

    // ---- Rule 4: damage, and what it is worth saying -------------------------------------------

    [Test]
    public void ApplyDamage_PublishesPlayerDamaged_WithFractions()
    {
        PlayerCombat combat = Combat();

        DamageResult result = combat.ApplyDamage(20f, 0f);

        Assert.That(result.ToShield, Is.EqualTo(20f).Within(1e-4f), "Sanity: the Aegis takes it first.");

        PlayerDamaged damaged = _events.Single<PlayerDamaged>();

        Assert.That(damaged.ToShield, Is.EqualTo(20f).Within(1e-4f));
        Assert.That(damaged.ToHp, Is.EqualTo(0f));
        Assert.That(damaged.Blocked, Is.False);

        // Both the split and the resulting fractions, because they answer different questions: the
        // split is what a damage number floats, the fractions are what the bars are set to.
        Assert.That(damaged.HpFraction, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(damaged.ShieldFraction, Is.EqualTo(1f / 3f).Within(1e-4f));

        Assert.That(_events.Count<PlayerDied>(), Is.Zero);
    }

    [Test]
    public void ApplyDamage_Blocked_StillPublishes()
    {
        PlayerCombat combat = Combat();

        combat.ApplyDamage(10f, 0f);
        _events.Clear();

        // 0.1 s into the 0.5 s of i-frames CC §7 gives the Oathbound.
        DamageResult result = combat.ApplyDamage(10f, 0.1f);

        Assert.That(result.Blocked, Is.True);

        PlayerDamaged damaged = _events.Single<PlayerDamaged>();

        // Published, not swallowed. A hit that was shrugged off is a thing that happened, and half
        // a second of invulnerability is invisible unless the game says so.
        Assert.That(damaged.Blocked, Is.True);
        Assert.That(damaged.ToShield, Is.EqualTo(0f));
        Assert.That(damaged.ToHp, Is.EqualTo(0f));
        Assert.That(damaged.ShieldFraction, Is.EqualTo(2f / 3f).Within(1e-4f), "…and it reports the shield as it stands.");
    }

    [Test]
    public void ApplyDamage_Kill_PublishesPlayerDiedOnce()
    {
        // 5 HP, no Aegis, no i-frames, so the second call is refused for being dead and for no
        // other reason.
        var combat = new PlayerCombat(
            Character(maxHp: 5f, withShield: false, hitIFrames: 0f),
            _events,
            _intents,
            EnemyCapacity);

        DamageResult first = combat.ApplyDamage(50f, 2f);

        Assert.That(first.Killed, Is.True);

        // Overkill reports the damage that landed, not the damage that was swung.
        Assert.That(first.ToHp, Is.EqualTo(5f).Within(1e-4f));
        Assert.That(combat.IsDead, Is.True);
        Assert.That(_events.Single<PlayerDied>().Time, Is.EqualTo(2f).Within(1e-6f));

        DamageResult second = combat.ApplyDamage(50f, 2.5f);

        Assert.That(second.Applied, Is.EqualTo(0f));
        Assert.That(second.Blocked, Is.False, "Not blocked — nothing arrived at anything.");
        Assert.That(second.Killed, Is.False);

        // Exactly once per life, and the corpse is not damaged a second time either.
        Assert.That(_events.Count<PlayerDied>(), Is.EqualTo(1));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
    }

    [Test]
    public void Tick_PublishesShieldChanged_WhileRecharging()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        combat.ApplyDamage(ShieldMax, 0f);

        Assert.That(combat.Health.Shield, Is.EqualTo(0f), "Sanity: the Aegis is spent.");

        _events.Clear();

        // Up to the recharge deadline and not past it: Health credits only the part of a step that
        // is beyond `lastDamageAt + delay`, so this whole step is worth nothing.
        combat.Tick(ShieldDelay, ShieldDelay, Snapshot(), registry.Alive, Facing);

        Assert.That(_events.Count<PlayerShieldChanged>(), Is.Zero, "Nothing has refilled yet.");

        combat.Tick(0.1f, ShieldDelay + 0.1f, Snapshot(), registry.Alive, Facing);

        // 15 per second for 0.1 s is 1.5 points of a 30-point shield.
        Assert.That(_events.Single<PlayerShieldChanged>().Fraction, Is.EqualTo(0.05f).Within(1e-4f));
        Assert.That(combat.Blackboard.ShieldFraction, Is.EqualTo(0.05f).Within(1e-4f));
    }

    // ---- Rule 2's last step: the blackboard ----------------------------------------------------

    [Test]
    public void Blackboard_CountsByDistance()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        registry.Spawn(Enemy(), new Vector3(0f, 0f, 3f));
        registry.Spawn(Enemy(), new Vector3(0f, 0f, 7f));
        registry.Spawn(Enemy(), new Vector3(0f, 0f, 11f));
        registry.Spawn(Enemy(), new Vector3(0f, 0f, 20f));

        combat.Tick(Frame, 0f, Snapshot(), registry.Alive, Facing);

        // Three independent bands rather than nested ones, so a class whose acquire range was
        // narrower than 8 m would still report each honestly.
        Assert.That(combat.Blackboard.EnemiesWithin6m, Is.EqualTo(1));
        Assert.That(combat.Blackboard.EnemiesWithin8m, Is.EqualTo(2));
        Assert.That(combat.Blackboard.EnemiesInAcquireRange, Is.EqualTo(3), "The one at 20 m is outside the 12 m range.");

        Assert.That(combat.Blackboard.HpFraction, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(combat.Blackboard.ShieldFraction, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(combat.Blackboard.CurrentTargetId, Is.EqualTo(combat.Targeter.CurrentTargetId));
        Assert.That(combat.Blackboard.HasFocus, Is.False);

        // Untouched by this task, and deliberately so — M6-04 and M2-07 own them.
        Assert.That(combat.Blackboard.Veilrot, Is.EqualTo(0f));
        Assert.That(combat.Blackboard.IncomingProjectiles, Is.Zero);
    }

    [Test]
    public void Blackboard_StationaryTime_AccumulatesAndResets()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        combat.Tick(0.25f, 0.25f, Snapshot(), registry.Alive, Facing);
        combat.Tick(0.25f, 0.5f, Snapshot(), registry.Alive, Facing);

        Assert.That(combat.Blackboard.StationaryTime, Is.EqualTo(0.5f).Within(1e-6f));

        combat.Tick(0.25f, 0.75f, Snapshot(input: new Vector2(1f, 0f)), registry.Alive, Facing);

        // Cleared outright, not decayed: this is "how long have I been standing still", and a
        // trigger that waits on it must not be nudged over the line by a frame of drift.
        Assert.That(combat.Blackboard.StationaryTime, Is.EqualTo(0f));
    }

    // ---- Rule 5: the run composes it -----------------------------------------------------------

    [Test]
    public void RunSession_MotorFacesTarget()
    {
        var catalog = new ContentCatalog(
            new[] { Character() }, new[] { Enemy() }, new[] { Descent() });
        var session = new RunSession(catalog, new FixedRandom(Seed), _events, new RecordingIntents(), EnemyCapacity, DeviceCap, ProjectileCapacity);

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(OathboundId),
            Seed,
            1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(5f, 0f, 0f)) })));

        Assert.That(session.State.PlayerFacing, Is.EqualTo(Vector3.UnitZ), "Sanity: a run starts looking down +Z.");

        // A full second with the stick centred. At 720 °/s the 90° turn takes 0.125 s, so this is
        // measuring that the facing arrived and stayed rather than how fast it got there.
        var snapshot = new WorldSnapshot(EnemyCapacity);
        snapshot.Dt = Frame;

        for (int i = 0; i < 120; i++)
        {
            session.Tick(snapshot);
        }

        // The whole chain in one assertion: the registry's enemy became a candidate, the candidate
        // became a target, the target became a direction, and the motor turned to it — with no
        // stick input anywhere, which is what makes it the target's doing.
        Assert.That(session.State.PlayerFacing.X, Is.EqualTo(1f).Within(1e-3f));
        Assert.That(session.State.PlayerFacing.Z, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(session.State.PlayerVelocity, Is.EqualTo(Vector3.Zero));

        Assert.That(_events.Count<TargetChanged>(), Is.EqualTo(1), "One acquisition, not one a frame.");
    }

    [Test]
    public void PlayerDied_EndsRun()
    {
        // M1-17 rule 3. Five hit points against a Husk that swings for eight, no Aegis and no
        // i-frames, so the first strike that connects is the last thing that happens in the run —
        // and every step of it goes through the public surface: a chaser walks up and hits, exactly
        // as it does in the game.
        var catalog = new ContentCatalog(
            new[] { Character(maxHp: 5f, withShield: false, hitIFrames: 0f) },
            new[] { Chaser() },
            new[] { Descent() });

        var session = new RunSession(catalog, new FixedRandom(Seed), _events, new RecordingIntents(), EnemyCapacity, DeviceCap, ProjectileCapacity);

        // A metre away: inside the Husk's 1.2 m reach on the tick it starts chasing, so the run is
        // over inside the 0.4 s wind-up plus a handful of frames rather than after a walk across
        // the arena. Nothing moves it — no body reports a position in a headless test — so the
        // distance the behaviour reads stays exactly this.
        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(OathboundId),
            Seed,
            1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(0f, 0f, 1f)) })));

        var snapshot = new WorldSnapshot(EnemyCapacity);
        snapshot.Dt = Frame;

        // A second of simulated time, which is twice what the wind-up needs. The loop stops itself
        // rather than running to the end, because ticking a session that has finished is exactly
        // the mistake this row exists to prove core no longer makes.
        for (int i = 0; i < 120 && session.IsRunning; i++)
        {
            session.Tick(snapshot);
        }

        Assert.That(session.IsRunning, Is.False, "A dead player ends the run.");

        PlayerDied died = _events.Single<PlayerDied>();
        RunEnded ended = _events.Single<RunEnded>();

        int diedAt = -1;
        int endedAt = -1;

        for (int i = 0; i < _events.All.Count; i++)
        {
            if (diedAt < 0 && _events.All[i] is PlayerDied)
            {
                diedAt = i;
            }

            if (endedAt < 0 && _events.All[i] is RunEnded)
            {
                endedAt = i;
            }
        }

        // Published in that order and on the same tick: the strike announces the death, and the run
        // closes behind it. A listener handling PlayerDied can still read a session that is ending
        // rather than one that has already gone.
        Assert.That(
            endedAt,
            Is.GreaterThan(diedAt),
            "PlayerDied comes first — the death is the cause, RunEnded is the consequence.");

        Assert.That(ended.Time, Is.EqualTo(died.Time).Within(1e-6f), "…and neither waited a tick.");

        // The state is still readable after the end, which is what M4-06's payout screen will need,
        // and it says what killed the run.
        Assert.That(session.State.PlayerHp, Is.EqualTo(0f));
        Assert.That(session.State.PlayerHpFraction, Is.EqualTo(0f));
        Assert.That(session.State.PlayerMaxHp, Is.EqualTo(5f).Within(1e-6f));

        // And the run really is over: the frame loop's guard is what normally stops this, and core
        // refuses it either way rather than quietly ticking a finished run.
        Assert.That(() => session.Tick(snapshot), Throws.InvalidOperationException);
    }

    // ---- Reset and the allocation budget -------------------------------------------------------

    [Test]
    public void Reset_ClearsAll()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();
        registry.Spawn(Enemy(), new Vector3(0f, 0f, 4f));

        combat.ApplyDamage(50f, 0f);
        combat.Tick(0.5f, 0.5f, Snapshot(), registry.Alive, Facing);

        Assert.That(combat.Health.Fraction, Is.LessThan(1f), "Sanity: it was hurt…");
        Assert.That(combat.Targeter.CurrentTargetId, Is.Not.EqualTo(-1), "…and it was aiming at something.");

        combat.Reset();

        Assert.That(combat.Health.Fraction, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(combat.Health.Shield, Is.EqualTo(ShieldMax).Within(1e-4f));
        Assert.That(combat.IsDead, Is.False);

        Assert.That(combat.Targeter.CurrentTargetId, Is.EqualTo(-1));
        Assert.That(combat.Targeter.IsCurrentBlocked, Is.False);
        Assert.That(combat.Targeter.HasFocus, Is.False);

        Assert.That(combat.Weapon.IsSwinging, Is.False, "The swing clock goes back to rest with everything else.");
        Assert.That(combat.PendingConeRequestId, Is.EqualTo(-1), "…and no cone is owed an answer.");
        Assert.That(combat.DpsOneSecond, Is.EqualTo(0f));

        Assert.That(combat.FaceDirection, Is.Null);

        // −1 rather than zero, because zero is a shape an id could have taken and "nobody" is
        // spelled −1 everywhere else in this project.
        Assert.That(combat.Blackboard.CurrentTargetId, Is.EqualTo(-1));
        Assert.That(combat.Blackboard.HpFraction, Is.EqualTo(0f));
        Assert.That(combat.Blackboard.ShieldFraction, Is.EqualTo(0f));
        Assert.That(combat.Blackboard.EnemiesInAcquireRange, Is.Zero);
        Assert.That(combat.Blackboard.StationaryTime, Is.EqualTo(0f));
        Assert.That(combat.Blackboard.IsTargetBlocked, Is.False);
        Assert.That(combat.Blackboard.HasFocus, Is.False);
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        var registry = new EnemyRegistry(32);
        var combat = new PlayerCombat(Character(), _events, _intents, 32);

        for (int i = 0; i < 32; i++)
        {
            registry.Spawn(Enemy(), new Vector3(i * 0.5f, 0f, 3f));
        }

        WorldSnapshot snapshot = Snapshot(input: new Vector2(0.7f, 0.7f), capacity: 32);

        // Warmed to a steady state first, so the acquisition event and its boxing land outside the
        // measurement. From here the target is stable, the Aegis is full and the blackboard is
        // rewritten in place, so a run of ticks should publish nothing and touch no heap at all —
        // and a stray publish would show up here as an allocation, which is the point.
        for (int i = 0; i < 200; i++)
        {
            combat.Tick(Frame, i * Frame, snapshot, registry.Alive, Facing);
        }

        // The measured window sits inside a swing rather than across one, and one tick at the
        // measurement's own clock is what puts it there: the jump from the warm-up to t = 10
        // finishes the swing that was running and starts a fresh one, which then has 0.333 s to go
        // and nothing to say for any of it. A window that spanned a damage frame would measure the
        // event and the intent that a swing is *supposed* to allocate — M1-10's PlayerAttacked
        // boxes on publish, three times a second, by design — instead of the per-frame path this
        // row exists to hold to zero.
        combat.Tick(Frame, 10f, snapshot, registry.Alive, Facing);

        _events.Clear();
        _intents.Clear();

        AllocationAssert.None(() => combat.Tick(Frame, 10f, snapshot, registry.Alive, Facing));

        Assert.That(_events.All, Is.Empty, "A tick inside a swing has nothing to announce.");
        Assert.That(_intents.ConeHits, Is.Empty, "…and nothing to ask the body.");
    }

    [Test]
    public void Ctor_NullDependency_Throws()
    {
        // Beyond the spec's Tests table. The constructor is the one place a mis-wired run would
        // surface early rather than a frame later as an NRE inside Tick, and the capacity is
        // checked here rather than at the first tick for the same reason RunSession checks its own.
        Assert.Throws<ArgumentNullException>(() => new PlayerCombat(null, _events, _intents, EnemyCapacity));
        Assert.Throws<ArgumentNullException>(() => new PlayerCombat(Character(), null, _intents, EnemyCapacity));
        Assert.Throws<ArgumentNullException>(() => new PlayerCombat(Character(), _events, null, EnemyCapacity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerCombat(Character(), _events, _intents, 0));
    }

    // ---- Fixture helpers -----------------------------------------------------------------------

    private PlayerCombat Combat() => new(Character(), _events, _intents, EnemyCapacity);


    /// <summary>
    /// Descent as this fixture needs it: endless, from stage 1, and with an <b>empty roster</b>.
    /// </summary>
    /// <remarks>
    /// Empty because <c>RunSession.Start</c> resolves every roster id against the catalog before
    /// it announces a run, and no row here is about a schedule -- what these rows spawn comes from
    /// a <c>SpawnPlan</c>. A roster would couple every one of them to content they do not use.
    /// </remarks>
    private static ModeSpec Descent() => new ModeSpec(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Array.Empty<RosterEntry>());

    /// <summary>The Oathbound of CC §7, with the four numbers a row may need to override.</summary>
    /// <remarks>
    /// <para>
    /// The Aegis is asked for as a <see cref="bool"/> rather than passed in as a
    /// <see cref="ShieldSpec"/>, because a default argument cannot be a constructed object and a
    /// <c>null</c> default would make "no shield" and "you did not say" the same request — which
    /// is exactly the distinction the kill row depends on.
    /// </para>
    /// <para>
    /// <b>Focus is switched off by default</b> — a <c>MaxMultiplier</c> of 1, which
    /// <see cref="FocusSpec"/> documents as "this class does not ramp". Every row in this fixture
    /// ticks a centred stick, so the real ×1.3 would have CC §4.3's ramp quietly speeding the
    /// Censer up under tests that are about targeting, damage and cone requests and were written
    /// against a weapon at 3 swings a second. The ramp's own behaviour is
    /// <c>FocusTrackerTests</c>' subject; here it is background, and background is exactly what a
    /// fixture should be able to hold still. The same isolation <paramref name="withShield"/>
    /// gives the Aegis.
    /// </para>
    /// </remarks>
    private static CharacterSpec Character(
        float maxHp = MaxHp,
        bool withShield = true,
        float hitIFrames = HitIFrames,
        float focusMaxMultiplier = 1f)
    {
        return new CharacterSpec(
            new ContentId(OathboundId),
            new LocKey("character.oathbound.name"),
            maxHp,
            new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
            new TargetingSpec(AcquireRange, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
            new FocusSpec(0.4f, 1f, focusMaxMultiplier),
            // Required as of M1-14, and inert in every row here: PlayerCombat does not compose a
            // ChargeSkill until M1-15, so this is authored data nothing yet reads.
            new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
            withShield ? new ShieldSpec(ShieldMax, ShieldDelay, ShieldRefill) : null,
            hitIFrames);
    }

    /// <summary>GD §8.1's Husk, with the one number the scoring rows vary.</summary>
    /// <remarks>
    /// <c>Static</c>, so that every row about targeting, damage and cone requests is written
    /// against enemies that stand exactly where they were put. The one row that needs a Husk which
    /// fights back asks for <see cref="Chaser"/>.
    /// </remarks>
    private static EnemySpec Enemy(int priority = 1) => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        36f,
        3.5f,
        priority,
        threatCost: 4,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    /// <summary>The same Husk, with M1-18's brain switched on: it walks up, telegraphs, and hits.</summary>
    private static EnemySpec Chaser() => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        36f,
        3.5f,
        1,
        threatCost: 4,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Chaser);

    /// <summary>
    /// A snapshot with the player at the origin, so every enemy's spawn position is also its
    /// distance.
    /// </summary>
    /// <remarks>
    /// It carries no enemies. <c>PlayerCombat</c> reads positions off the agents rather than off
    /// the snapshot — ingestion has already copied one into the other by the time the run calls it
    /// — and an agent the snapshot does not name keeps the position it was spawned at, which is
    /// exactly what these rows want.
    /// </remarks>
    private static WorldSnapshot Snapshot(Vector2 input = default, int capacity = EnemyCapacity)
    {
        var snapshot = new WorldSnapshot(capacity);
        snapshot.MoveInput = input;
        return snapshot;
    }
}

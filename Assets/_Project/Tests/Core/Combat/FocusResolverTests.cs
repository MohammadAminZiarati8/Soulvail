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
using Soulvail.Tests.Core.Fakes;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// M1-09's two halves: the arithmetic that turns a tap into an enemy, and the command rules that
/// turn that enemy into a focus. The first is exercised directly, the second through
/// <see cref="RunSession"/> — which is the only route there is, and also the one the game uses.
/// </summary>
/// <remarks>
/// <para>
/// The command rows go through the session rather than through <c>PlayerCombat</c> because the
/// contract being checked is <see cref="IPlayerCommands"/>, and the session is what implements it:
/// the "not running" rule lives nowhere else, and reaching for the enemy span is the session's job
/// too. Reaching into <c>PlayerCombat.FocusAt</c> directly would test a method while leaving the
/// port that calls it unproven.
/// </para>
/// <para>
/// Enemies are reached only through <see cref="EnemyRegistry"/> and <c>SpawnPlan</c>, for the
/// reason <c>PlayerCombatTests</c> gives: <c>EnemyAgent</c>'s constructor and its <c>Position</c>
/// setter are <c>internal</c> and <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c>
/// (M0-10). Positions therefore come from a spawn, and "dead" is reached by damaging.
/// </para>
/// </remarks>
[TestFixture]
public sealed class FocusResolverTests
{
    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";
    private const string HuskId = "enemy.husk";
    private const int Seed = 4;
    private const int EnemyCapacity = 8;

    /// <summary>CC §7's acquire range, wide enough that no row loses a focus to distance.</summary>
    private const float AcquireRange = 12f;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    private RecordingEvents _events;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
    }

    // ---- The resolver ------------------------------------------------------------------------

    [Test]
    public void Resolve_NearestWithinRadius()
    {
        var registry = new EnemyRegistry(EnemyCapacity);

        EnemyAgent near = registry.Spawn(Enemy(), new Vector3(1f, 0f, 0f));
        registry.Spawn(Enemy(), new Vector3(2.5f, 0f, 0f));
        registry.Spawn(Enemy(), new Vector3(5f, 0f, 0f));

        // Nearest to the *tap*, not to the player — the player is nowhere in this call, and that is
        // the point: a tap means "that one", and letting anything else weigh in would be the game
        // second-guessing a decision it just promised was the player's.
        Assert.That(Resolve(Vector3.Zero, registry), Is.EqualTo(near.Id));
    }

    [Test]
    public void Resolve_NoneWithinRadius_MinusOne()
    {
        var registry = new EnemyRegistry(EnemyCapacity);

        registry.Spawn(Enemy(), new Vector3(3.1f, 0f, 0f));

        // A tenth of a metre outside, and a miss. −1 is what makes tapping bare ground the way to
        // clear a focus (CC §3.4) rather than a request that quietly does nothing.
        Assert.That(Resolve(Vector3.Zero, registry), Is.EqualTo(-1));
    }

    [Test]
    public void Resolve_IgnoresDead()
    {
        var registry = new EnemyRegistry(EnemyCapacity);

        EnemyAgent corpse = registry.Spawn(Enemy(), new Vector3(0.5f, 0f, 0f));
        EnemyAgent alive = registry.Spawn(Enemy(), new Vector3(2f, 0f, 0f));

        corpse.Health.ApplyDamage(1000f, 0f);

        Assert.That(corpse.IsAlive, Is.False, "Sanity: it is a corpse and still in the registry.");

        // Four times closer to the tap and skipped anyway. A corpse sits in Alive until M1-11
        // despawns it, so this is rule 1 doing real work rather than a defensive check.
        Assert.That(Resolve(Vector3.Zero, registry), Is.EqualTo(alive.Id));
    }

    [Test]
    public void Resolve_Ties_LowestId()
    {
        var registry = new EnemyRegistry(EnemyCapacity);

        EnemyAgent first = registry.Spawn(Enemy(), new Vector3(2f, 0f, 0f));
        EnemyAgent second = registry.Spawn(Enemy(), new Vector3(-2f, 0f, 0f));

        Assert.That(second.Id, Is.GreaterThan(first.Id), "Sanity: ids ascend with spawn order.");

        // Exactly equidistant, so the answer has to come from the tie-break rather than from float
        // noise. Lowest id is earliest spawned, which is the same "first in wins" TargetScorer
        // uses — two systems asked about the same pair must not disagree.
        Assert.That(Resolve(Vector3.Zero, registry), Is.EqualTo(first.Id));
    }

    [Test]
    public void Resolve_EmptySpan_MinusOne()
    {
        // Beyond the spec's table, and the cheapest row here: an empty arena is every frame before
        // the first wave, and the loop below it must not read a candidate that is not there.
        Assert.That(
            FocusResolver.Resolve(Vector3.Zero, ReadOnlySpan<EnemyAgent>.Empty, FocusResolver.RadiusMetres),
            Is.EqualTo(-1));
    }

    [Test]
    public void Resolve_MeaninglessRadius_MinusOne()
    {
        // Also beyond the table. The negative case is the one worth having: −1 squared is 1, so
        // without the guard a nonsense radius would quietly become a plausible one-metre one and
        // find this enemy. NaN and zero come along for the same two lines.
        var registry = new EnemyRegistry(EnemyCapacity);
        registry.Spawn(Enemy(), new Vector3(0.5f, 0f, 0f));

        Assert.That(FocusResolver.Resolve(Vector3.Zero, registry.Alive, -1f), Is.EqualTo(-1));
        Assert.That(FocusResolver.Resolve(Vector3.Zero, registry.Alive, float.NaN), Is.EqualTo(-1));
        Assert.That(FocusResolver.Resolve(Vector3.Zero, registry.Alive, 0f), Is.EqualTo(-1));
    }

    [Test]
    public void Resolve_IgnoresHeight()
    {
        // Beyond the table. Every distance in this project is XZ, and a tap arrives on the plane
        // y = 0 while an enemy's position is its capsule centre — so a resolver that measured in
        // three dimensions would quietly shrink the 3 m radius by however tall the enemy is.
        var registry = new EnemyRegistry(EnemyCapacity);
        EnemyAgent husk = registry.Spawn(Enemy(), new Vector3(2.9f, 1.5f, 0f));

        Assert.That(Resolve(Vector3.Zero, registry), Is.EqualTo(husk.Id));
    }

    // ---- The commands, through the run --------------------------------------------------------

    [Test]
    public void FocusTarget_OnEnemy_SetsFocus()
    {
        // Two enemies, and the tap is on the *far* one: scoring prefers the near one, so a focus
        // that landed would be visible as the target moving against the grain rather than as a
        // flag on the enemy it was already aiming at.
        RunSession session = StartedRun(new Vector3(4f, 0f, 0f), new Vector3(11f, 0f, 0f));

        int near = SpawnedId(0);
        int far = SpawnedId(1);

        Assert.That(TargetId(), Is.EqualTo(near), "Sanity: the run opens aiming at the nearer one.");

        _events.Clear();

        // Half a metre off the enemy, which is what a thumb actually manages.
        session.FocusTarget(new Vector3(11.5f, 0f, 0f));

        Assert.That(_events.Count<TargetChanged>(), Is.Zero,
            "A command lands on the tick, not in the call — Targeter.Focus defers deliberately.");

        Tick(session);

        // The event the reticle and the overlay are both built on, and the only thing about a
        // focus that leaves core: RunState.Combat is internal on purpose (M0-10), so this *is* the
        // public surface, not a stand-in for one.
        TargetChanged changed = _events.Single<TargetChanged>();

        Assert.That(changed.Id, Is.EqualTo(far));
        Assert.That(changed.IsFocused, Is.True);
        Assert.That(changed.IsBlocked, Is.False);
    }

    [Test]
    public void FocusTarget_OnEmptyGround_Clears()
    {
        RunSession session = StartedRun(new Vector3(4f, 0f, 0f), new Vector3(11f, 0f, 0f));

        int near = SpawnedId(0);
        int far = SpawnedId(1);

        session.FocusTarget(new Vector3(11f, 0f, 0f));
        Tick(session);

        Assert.That(TargetId(), Is.EqualTo(far), "Sanity: the focus took hold.");

        _events.Clear();

        // Thirty metres away, with nothing standing anywhere near it. CC §3.4: this is the only
        // gesture a thumb has for going back to auto-aim, so it must not be a no-op.
        session.FocusTarget(new Vector3(30f, 0f, 30f));
        Tick(session);

        TargetChanged changed = _events.Single<TargetChanged>();

        Assert.That(changed.IsFocused, Is.False);
        Assert.That(changed.Id, Is.EqualTo(near), "Scoring takes over again, and it prefers the nearer one.");
    }

    [Test]
    public void ClearFocus_Clears()
    {
        RunSession session = StartedRun(new Vector3(4f, 0f, 0f), new Vector3(11f, 0f, 0f));

        int near = SpawnedId(0);
        int far = SpawnedId(1);

        session.FocusTarget(new Vector3(11f, 0f, 0f));
        Tick(session);

        Assert.That(TargetId(), Is.EqualTo(far), "Sanity: the focus took hold.");

        _events.Clear();

        session.ClearFocus();

        Assert.That(_events.Count<TargetChanged>(), Is.Zero,
            "Deferred like every other focus command — nothing changes until the tick.");

        Tick(session);

        TargetChanged changed = _events.Single<TargetChanged>();

        Assert.That(changed.IsFocused, Is.False);
        Assert.That(changed.Id, Is.EqualTo(near));
    }

    [Test]
    public void Commands_WhenNotRunning_Throw()
    {
        var session = new RunSession(Catalog(), new FixedRandom(Seed), _events, new RecordingIntents(), EnemyCapacity);

        // Before any run: State is null, so a no-op here would be a NullReferenceException one line
        // later anyway. Throwing says which of the two problems it is.
        Assert.Throws<InvalidOperationException>(() => session.FocusTarget(Vector3.Zero));
        Assert.Throws<InvalidOperationException>(() => session.ClearFocus());

        session.Start(Config(new Vector3(4f, 0f, 0f)));
        session.End();

        // And after: the finished run stays readable (M0-10), which is exactly why commanding it
        // has to be refused rather than quietly mutating a run that is over.
        Assert.Throws<InvalidOperationException>(() => session.FocusTarget(Vector3.Zero));
        Assert.Throws<InvalidOperationException>(() => session.ClearFocus());
    }

    // ---- Fixture helpers -----------------------------------------------------------------------

    private static int Resolve(Vector3 point, EnemyRegistry registry)
    {
        return FocusResolver.Resolve(point, registry.Alive, FocusResolver.RadiusMetres);
    }

    /// <summary>
    /// A live run with a Husk standing at each position given, and one tick already behind it so
    /// that targeting has settled and the acquisition event is out of the way.
    /// </summary>
    private RunSession StartedRun(params Vector3[] positions)
    {
        var session = new RunSession(Catalog(), new FixedRandom(Seed), _events, new RecordingIntents(), EnemyCapacity);

        session.Start(Config(positions));
        Tick(session);

        return session;
    }

    /// <remarks>
    /// A fresh snapshot per call, carrying no enemies. Core reads positions off the agents, and
    /// <c>Ingest</c> leaves an agent the snapshot does not name exactly where it was spawned —
    /// which is what these rows want, since nothing here is supposed to move.
    /// </remarks>
    private static void Tick(RunSession session)
    {
        var snapshot = new WorldSnapshot(EnemyCapacity);
        snapshot.Dt = Frame;
        session.Tick(snapshot);
    }

    /// <summary>
    /// The id of the <paramref name="index"/>th enemy the run spawned, read off its
    /// <c>EnemySpawned</c>.
    /// </summary>
    /// <remarks>
    /// The registry is not reachable from here — <c>RunState.Enemies</c> is <c>internal</c> and
    /// <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c> (M0-10) — and the census events
    /// are how the rest of the game learns the same thing, so this is the intended route rather
    /// than a workaround.
    /// </remarks>
    private int SpawnedId(int index) => _events.Of<EnemySpawned>()[index].Id;

    /// <summary>The most recently announced target id, or −1 if nothing has been announced yet.</summary>
    private int TargetId()
    {
        IReadOnlyList<TargetChanged> changes = _events.Of<TargetChanged>();

        return changes.Count == 0 ? -1 : changes[changes.Count - 1].Id;
    }

    private static RunConfig Config(params Vector3[] positions)
    {
        var entries = new List<SpawnPlan.Entry>(positions.Length);

        foreach (Vector3 position in positions)
        {
            entries.Add(new SpawnPlan.Entry(new ContentId(HuskId), position));
        }

        return new RunConfig(
            new ContentId(DescentId),
            new ContentId(OathboundId),
            Seed,
            1,
            new SpawnPlan(entries.ToArray()));
    }

    private static ContentCatalog Catalog() =>
        new(new[] { Character() }, new[] { Enemy() }, new[] { Descent() });


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
        Array.Empty<RosterEntry>());

    /// <summary>The Oathbound of CC §7.</summary>
    private static CharacterSpec Character() => new(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(AcquireRange, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        // Required as of M1-13, and switched off here with a MaxMultiplier of 1. Worth saying
        // out loud in this fixture of all of them: the Focus these rows are about is CC §3.4's
        // tapped *target*, and the one being switched off is CC §4.3's stationary fire-rate
        // ramp. Two mechanics, one word, and nothing here exercises the second.
        new FocusSpec(0.4f, 1f, 1f),
        // Required as of M1-14, and inert in every row here: nothing in this fixture dashes.
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);

    /// <summary>GD §8.1's Husk.</summary>
    private static EnemySpec Enemy() => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        36f,
        3.5f,
        1,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        EnemyBehaviourKind.Static);
}

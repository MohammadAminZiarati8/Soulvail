using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// M1-11's seven rules: the fact is refused unless a swing is owed an answer, one report spends one
/// swing, ids are deduplicated, unknown and dead ones are ignored, damage becomes
/// <see cref="EnemyDamaged"/> and <see cref="EnemyDied"/>, a corpse is despawned after
/// <see cref="EnemySystem.CorpseTime"/>, and none of it allocates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything is driven and observed through the ports.</b> <c>RunState.Combat</c> and
/// <c>RunState.Enemies</c> are <c>internal</c> and <c>Soulvail.Tests.Core</c> has no
/// <c>InternalsVisibleTo</c> (M0-10), so there is no reaching in to read a pending request id or an
/// enemy's hit points — which is the right constraint for this fixture in particular. The body
/// learns that a swing has landed from a <c>ConeHitIntent</c> and learns what the damage did from
/// events, so a test that watches those two things is watching exactly what M1-12's
/// <c>RunTicker</c> will.
/// </para>
/// <para>
/// The numbers are CC §7's Censer against GD §8.1's Husk throughout — 13 damage a swing, three
/// swings a second, into 36 hit points — so a failure reads as "the weapon we ship stopped killing
/// the enemy we ship" rather than as an arithmetic puzzle. Three swings is a kill, which is the
/// TTK CC §4.1 asks for and the reason every row here is about a multiple of 13.
/// </para>
/// <para>
/// The Husk is authored <c>Static</c> rather than <c>Chaser</c>, as it is in every M1 fixture: it
/// stands where it was spawned, so its distance is its spawn position and nothing here depends on
/// an AI that M1-18 has not written yet.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ConeHitsToDamageTests
{
    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";
    private const string HuskId = "enemy.husk";
    private const int Seed = 99;

    /// <summary>Room for every row's enemies, and the buffers the run preallocates.</summary>
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

    // CC §7's Censer and GD §8.1's Husk. Three swings is 39 against 36: a kill with 3 to spare.
    private const float SwingDamage = 13f;
    private const float HuskMaxHp = 36f;
    private const float SwingsPerSecond = 3f;
    private const float WeaponRange = 8f;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    /// <summary>
    /// How many frames a row will wait for a damage frame before giving up. One swing is 40 frames
    /// at this rate, so this is four swings' worth: long enough that a slow acquisition cannot fail
    /// a row, short enough that a weapon which has stopped swinging fails it immediately.
    /// </summary>
    private const int MaxFramesPerSwing = 200;

    /// <summary>An id of the right shape that no run has ever handed out.</summary>
    private const int UnknownEnemyId = 99;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private ContentCatalog _catalog;
    private RunSession _session;
    private WorldSnapshot _snapshot;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _catalog = new ContentCatalog(new[] { Oathbound() }, new[] { Husk() }, new[] { Descent() });
        _session = new RunSession(_catalog, new FixedRandom(Seed), _events, _intents, EnemyCapacity, DeviceCap, ProjectileCapacity);

        // The player stands at the origin all fixture long and never touches the stick, so every
        // enemy's spawn position is also its distance and the cone's origin is the origin.
        _snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };
    }

    // ---- Rules 2 and 3: the report becomes damage, death and events -----------------------------

    [Test]
    public void ThreeReports_KillHusk()
    {
        int husk = StartRun(At(5f))[0];
        int[] report = { husk };

        for (int swing = 0; swing < 3; swing++)
        {
            TickToDamageFrame();
            _session.ReportConeHits(report);
        }

        IReadOnlyList<EnemyDamaged> damaged = _events.Of<EnemyDamaged>();

        Assert.That(damaged.Count, Is.EqualTo(3), "One per swing, and never one per tick.");

        // 36 → 23 → 10 → 0, which is 0.64, 0.28 and 0 of a Husk's health bar.
        Assert.That(damaged[0].Amount, Is.EqualTo(SwingDamage).Within(1e-4f));
        Assert.That(damaged[0].HpFraction, Is.EqualTo(23f / HuskMaxHp).Within(1e-4f));
        Assert.That(damaged[0].Killed, Is.False);

        Assert.That(damaged[1].Amount, Is.EqualTo(SwingDamage).Within(1e-4f));
        Assert.That(damaged[1].HpFraction, Is.EqualTo(10f / HuskMaxHp).Within(1e-4f));
        Assert.That(damaged[1].Killed, Is.False);

        // Overkill reports the damage that landed, not the damage that was swung — 10, not 13.
        // What a floating damage number shows, and what anything summing damage dealt has to add.
        Assert.That(damaged[2].Amount, Is.EqualTo(10f).Within(1e-4f));
        Assert.That(damaged[2].HpFraction, Is.EqualTo(0f));
        Assert.That(damaged[2].Killed, Is.True);

        EnemyDied died = _events.Single<EnemyDied>();

        Assert.That(died.Id, Is.EqualTo(husk));
        Assert.That(died.SpecId, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(died.Position.Z, Is.EqualTo(5f).Within(1e-4f), "Where it died, for the drop and the VFX.");

        // A death is not a despawn. The corpse is still registered, which is what gives M1-12's
        // dissolve an id to animate.
        Assert.That(_events.Count<EnemyDespawned>(), Is.Zero);
    }

    // ---- Rule 1: one pending request, consumed once ---------------------------------------------

    [Test]
    public void Report_WithoutPending_Ignored()
    {
        int husk = StartRun(At(5f))[0];
        int[] report = { husk };

        // Not one tick yet, so no swing has been thrown and nothing is owed an answer. A report
        // here is the body talking about a swing that never happened.
        _session.ReportConeHits(report);

        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
        Assert.That(_events.Count<EnemyDied>(), Is.Zero);

        // …and it cost the Husk nothing, which the next real swing proves: a full-health Husk drops
        // to 23 of 36, not to 10.
        TickToDamageFrame();
        _session.ReportConeHits(report);

        Assert.That(_events.Single<EnemyDamaged>().HpFraction, Is.EqualTo(23f / HuskMaxHp).Within(1e-4f));
    }

    [Test]
    public void Report_ConsumesPending()
    {
        int husk = StartRun(At(5f))[0];
        int[] report = { husk };

        TickToDamageFrame();

        _session.ReportConeHits(report);
        _session.ReportConeHits(report);

        // The second is a duplicate of an answer already given — the same swing cannot be spent
        // twice, however many times the body reports it.
        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(1));
        Assert.That(_events.Single<EnemyDamaged>().HpFraction, Is.EqualTo(23f / HuskMaxHp).Within(1e-4f));
    }

    [Test]
    public void Report_DedupesIds()
    {
        int husk = StartRun(At(5f))[0];

        TickToDamageFrame();

        // One enemy named three times — a body whose overlap query returned overlapping colliders,
        // which is exactly what a capsule with two of them does.
        _session.ReportConeHits(new[] { husk, husk, husk });

        Assert.That(_events.Count<EnemyDamaged>(), Is.EqualTo(1));
        Assert.That(_events.Single<EnemyDamaged>().HpFraction, Is.EqualTo(23f / HuskMaxHp).Within(1e-4f), "36 − 13, once.");
    }

    [Test]
    public void Report_IgnoresUnknownAndDead()
    {
        // Two Husks: the far one is the corpse, the near one is only there to keep the weapon
        // swinging afterwards — a lone corpse is nothing to swing at, and the row would then pass
        // for rule 1's reason instead of rule 3's.
        int[] ids = StartRun(At(5f), At(4f));

        Kill(ids[0]);

        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1), "Sanity: the far Husk is a corpse.");

        _events.Clear();

        TickToDamageFrame();
        _session.ReportConeHits(new[] { UnknownEnemyId, ids[0] });

        // A view is allowed to lag a frame behind a death and report a corpse; an id core has
        // never heard of is a stale report. Neither is an error and neither is news.
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
        Assert.That(_events.Count<EnemyDied>(), Is.Zero);
    }

    // ---- Rule 4: the corpse timer ---------------------------------------------------------------

    [Test]
    public void Corpse_DespawnsAfterCorpseTime()
    {
        int husk = StartRun(At(5f))[0];

        Kill(husk);

        // The death landed inside ReportConeHits, which stamps it with the run's time as of the
        // last tick — so this is exactly the moment the corpse clock started from.
        float killedAt = _session.State.Time;

        _events.Clear();

        TickTo(killedAt + (EnemySystem.CorpseTime - 0.01f));

        Assert.That(_events.Count<EnemyDespawned>(), Is.Zero, "0.59 s in, the dissolve is still playing.");

        TickTo(killedAt + (EnemySystem.CorpseTime + 0.01f));

        Assert.That(_events.Single<EnemyDespawned>().Id, Is.EqualTo(husk));
    }

    // ---- Rule 5: a dead target is retargeted on the next tick -----------------------------------

    [Test]
    public void DeadTarget_RetargetsNextTick()
    {
        // 4 m scores 3.0 against 5 m's 2.75 — priority is equal, so the closer one wins and is
        // what the gun is pointed at when the killing starts.
        int[] ids = StartRun(At(4f), At(5f));

        TickToDamageFrame();

        Assert.That(_events.Single<TargetChanged>().Id, Is.EqualTo(ids[0]), "Sanity: the near Husk is the target.");

        Kill(ids[0]);

        _events.Clear();

        _session.Tick(_snapshot);

        // The corpse is still in the registry — it has 0.6 s of dissolve owed — and carries 0 HP,
        // which is what the targeter reads to know the schedule cannot be waited for (M1-04 rule
        // 1). One tick, and the gun has moved on.
        TargetChanged changed = _events.Single<TargetChanged>();

        Assert.That(changed.Id, Is.EqualTo(ids[1]));
        Assert.That(changed.IsBlocked, Is.False, "It moved to something it can actually hurt.");
    }

    // ---- Rule 6: the fact belongs to a live run -------------------------------------------------

    [Test]
    public void Report_WhenNotRunning_Throws()
    {
        int[] report = { 1 };

        // Before a run, because a fact arriving here is a ticker that is reporting when it should
        // not be — a wiring mistake, and one that would otherwise NRE on a null State.
        Assert.Throws<InvalidOperationException>(() => _session.ReportConeHits(report));

        StartRun(At(5f));
        _session.End();

        // And after one, which is the case that actually happens: a physics query resolved on the
        // frame the run ended, answering a swing whose enemies have been cleared out from under it.
        Assert.Throws<InvalidOperationException>(() => _session.ReportConeHits(report));
    }

    // ---- Rule 7: the allocation budget ----------------------------------------------------------

    [Test]
    public void ReportPath_AllocatesNothing()
    {
        // Silent ports, because the fixture's recorders are what would allocate rather than the
        // code under test: RecordingEvents boxes every payload it is handed, and RecordingIntents
        // grows a list per swing. M1-10 learned that a periodic event does not break a
        // zero-allocation claim, it moves where the window has to sit; here the window cannot be
        // moved off the publish at all, because publishing is what the report is for.
        var events = new QuietEvents();
        var intents = new SilentIntents();

        // A dummy with a billion hit points, so 10 000 swings never kill it and every iteration
        // measures the same path — the one where damage lands and an event goes out.
        var catalog = new ContentCatalog(
            new[] { Oathbound() }, new[] { Husk(maxHp: 1e9f) }, new[] { Descent() });
        var session = new RunSession(catalog, new FixedRandom(Seed), events, intents, EnemyCapacity, DeviceCap, ProjectileCapacity);

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(OathboundId),
            Seed,
            1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(HuskId), At(3f)) })));

        Assert.That(events.LastSpawnedId, Is.GreaterThan(0), "Sanity: the dummy is out there.");

        int[] report = { events.LastSpawnedId };

        // One tick per swing interval, exactly. The Censer's interval is 1/3 s and so is this,
        // computed the same way from the same 3.0 swings a second, so each tick lands precisely on
        // the end of a swing: the damage frame fires, the swing ends, the next one starts, and the
        // pattern repeats for ever. Two ticks to reach that steady state — the first only starts a
        // swing — and AllocationAssert's own warm-up call is then already inside it.
        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = 1f / SwingsPerSecond };

        session.Tick(snapshot);
        session.Tick(snapshot);
        session.ReportConeHits(report);

        AllocationAssert.None(() =>
        {
            session.Tick(snapshot);
            session.ReportConeHits(report);
        });
    }

    // ---- Beyond the spec's Tests table ----------------------------------------------------------

    [Test]
    public void ApplyDamage_NothingLanded_PublishesNothing()
    {
        // Beyond the Tests table, and it guards a rule that is not in the spec either: rule 3 says
        // to publish EnemyDamaged for any known, living enemy, and this refuses when nothing
        // actually arrived. Stat clamps nothing by design (ADR-0008), so a Pact driving weapon
        // damage to zero — or to NaN, which Health refuses at the door — is reachable, and the
        // symptom would be a hit flash and a damage number for a swing that did nothing.
        // PlayerCombat.ApplyDamage has refused the same way since M1-08; this is its mirror.
        //
        // Reached through EnemySystem directly because the weapon's Damage stat sits behind
        // RunState.Combat, which is internal — there is no route to a zero-damage swing from
        // outside core.
        var enemies = new EnemySystem(_catalog, _events, new FixedRandom(), Scaling(), EnemyCapacity);
        EnemyAgent husk = enemies.Spawn(new ContentId(HuskId), At(5f));

        _events.Clear();

        Assert.That(enemies.ApplyDamage(husk.Id, 0f, 1f).Applied, Is.EqualTo(0f));
        Assert.That(enemies.ApplyDamage(husk.Id, float.NaN, 1f).Applied, Is.EqualTo(0f));

        Assert.That(_events.All, Is.Empty, "A swing that did nothing has nothing to announce.");
        Assert.That(husk.Health.Current, Is.EqualTo(HuskMaxHp).Within(1e-4f));
    }

    // ---- Fixture helpers -----------------------------------------------------------------------

    /// <summary>A point <paramref name="metres"/> straight ahead of the player, on +Z.</summary>
    /// <remarks>
    /// Straight ahead so that a run's starting facing already points at it and the character never
    /// has to turn: the cone's direction is irrelevant to every row here — nothing resolves a wedge
    /// until M1-12 — but a Husk inside <see cref="WeaponRange"/> is what makes the weapon swing at
    /// all, and that is what every row depends on.
    /// </remarks>
    private static Vector3 At(float metres) => new(0f, 0f, metres);

    /// <summary>
    /// Starts a run with one Husk per position and returns their ids, in plan order.
    /// </summary>
    /// <remarks>
    /// The ids come from the <see cref="EnemySpawned"/> events rather than from
    /// <c>EnemyRegistry</c>'s "ids start at one" rule, because that rule is the registry's fixture
    /// to pin and this one should keep working the day a run spawns something before its plan.
    /// </remarks>
    private int[] StartRun(params Vector3[] positions)
    {
        var entries = new SpawnPlan.Entry[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            entries[i] = new SpawnPlan.Entry(new ContentId(HuskId), positions[i]);
        }

        _session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, new SpawnPlan(entries)));

        IReadOnlyList<EnemySpawned> spawned = _events.Of<EnemySpawned>();
        var ids = new int[spawned.Count];

        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = spawned[i].Id;
        }

        return ids;
    }

    /// <summary>
    /// Ticks until the weapon's next damage frame — the tick that writes a
    /// <see cref="ConeHitIntent"/> and leaves core owed an answer.
    /// </summary>
    /// <remarks>
    /// The intent is the signal because it is the only one there is from outside core: the pending
    /// request id lives behind an internal <c>RunState.Combat</c>, and this is precisely what
    /// M1-12's <c>RunTicker</c> will watch for. <c>PlayerAttacked</c> would be the wrong signal —
    /// it announces the *start* of a swing, four tenths of an interval too early.
    /// </remarks>
    private void TickToDamageFrame()
    {
        int before = _intents.ConeHits.Count;

        for (int i = 0; i < MaxFramesPerSwing; i++)
        {
            _session.Tick(_snapshot);

            if (_intents.ConeHits.Count > before)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"No damage frame within {MaxFramesPerSwing} ticks. The weapon has stopped swinging — "
                + "check that a living enemy is inside the Censer's range.");
    }

    /// <summary>Ticks until the run's simulated clock has reached <paramref name="time"/>.</summary>
    /// <remarks>
    /// One tick with whatever <c>Dt</c> is needed, rather than a stream of frames, because the rows
    /// that use this care about crossing a deadline to the hundredth of a second and a frame-sized
    /// step would land wherever it landed. It is well inside what <c>Targeter</c> and
    /// <c>Health</c> accept, and the snapshot's own 50 ms clamp is the builder's business rather
    /// than core's.
    /// </remarks>
    private void TickTo(float time)
    {
        _snapshot.Dt = time - _session.State.Time;

        _session.Tick(_snapshot);

        _snapshot.Dt = Frame;
    }

    /// <summary>Three swings into <paramref name="enemyId"/>, which is 39 damage against 36 HP.</summary>
    private void Kill(int enemyId)
    {
        int[] report = { enemyId };

        for (int swing = 0; swing < 3; swing++)
        {
            TickToDamageFrame();
            _session.ReportConeHits(report);
        }
    }


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

    /// <summary>The Oathbound of CC §7 — the Censer is the only block any row here reads.</summary>
    private static CharacterSpec Oathbound() => new(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, SwingDamage, SwingsPerSecond, WeaponRange, 60f, 0.4f),
        // Required as of M1-13, and switched off here with a MaxMultiplier of 1: every row in
        // this fixture ticks a centred stick, and CC §4.3's ramp would quietly speed the Censer
        // up under assertions written against 3 swings a second. FocusTrackerTests is the ramp's
        // fixture; here it is background, held still.
        new FocusSpec(0.4f, 1f, 1f),
        // Required as of M1-14, and inert in every row here: the cone is what these rows damage
        // with, and the Charge's own 20 damage does not exist for anything until M1-15.
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);

    /// <summary>GD §8.1's Husk, with the one number the allocation row overrides.</summary>
    private static EnemySpec Husk(float maxHp = HuskMaxHp) => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp,
        3.5f,
        1,
        threatCost: 4,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    /// <summary>
    /// An <see cref="IDomainEvents"/> that discards everything except the id of the last enemy
    /// spawned, which the allocation row needs before it starts measuring.
    /// </summary>
    /// <remarks>
    /// <c>typeof(T) == typeof(EnemySpawned)</c> rather than a pattern match, and the difference is
    /// the whole point of this class: <c>evt is EnemySpawned</c> on a generic value would box on
    /// every publish, including the <see cref="EnemyDamaged"/> the measured window emits ten
    /// thousand times. A type-handle comparison boxes nothing, and the branch that does box is
    /// taken only at <c>Start</c>, long before anything is measured.
    /// </remarks>
    private sealed class QuietEvents : IDomainEvents
    {
        /// <summary>The id of the most recent <see cref="EnemySpawned"/>, or −1.</summary>
        public int LastSpawnedId { get; private set; } = -1;

        public void Publish<T>(in T evt)
            where T : struct
        {
            if (typeof(T) == typeof(EnemySpawned))
            {
                LastSpawnedId = ((EnemySpawned)(object)evt).Id;
            }
        }
    }

    /// <summary>An <see cref="IIntentSink"/> that keeps nothing, so no list can grow mid-measurement.</summary>
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
            // Deliberately nothing. Nothing in this fixture presses the movement-skill button.
        }

        public void EnemyMove(in EnemyMoveIntent intent)
        {
            // Deliberately nothing. Every Husk in this fixture is a Static dummy.
        }

        public void EnemyKnockback(in EnemyKnockbackIntent intent)
        {
            // Deliberately nothing.
        }
    }

    /// <summary>
    /// The depth scaling every <c>EnemySystem</c> in this fixture is built with, required as of
    /// M2-03.
    /// </summary>
    /// <remarks>
    /// Inert in every row here, and that is by construction rather than by luck: the system's
    /// <c>Depth</c> defaults to 1, where GD §12.3's three multipliers are all exactly 1, so an
    /// enemy spawned by this fixture wears its archetype's authored numbers. The rows that are
    /// about depth are <c>DepthScalingTests</c>' and <c>EnemySystemTests.Spawn_AppliesDepth</c>.
    /// </remarks>
    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());
}

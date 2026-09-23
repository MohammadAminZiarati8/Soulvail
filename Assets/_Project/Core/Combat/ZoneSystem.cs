using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// Which side of the fight a zone acts on. Two members, and the closed set <see cref="ShotSide"/>
/// already is: a zone placed by the player either restores the player or damages what is standing
/// in it.
/// </summary>
/// <remarks>
/// <b>Not a dispatch over content and not ADR-0009's ban arriving under a new name.</b> What ADR-0009
/// refuses is <c>switch (effect.Type)</c> — code branching on which authored <em>thing</em> it was
/// handed. This is <see cref="ShotSide"/>'s shape: a two-member fact about a placement, decided once
/// where the placement is made, branched on in exactly one method (<c>ZoneSystem.Pulse</c>), and never
/// reachable from an asset. A third member is a design decision with a task behind it, not a row in
/// a table (M6-07b rule 1).
/// </remarks>
public enum ZoneSide
{
    /// <summary>Heals the player standing in it. Every zone before M6-07b — M3-11b's Consecrate.</summary>
    HealsThePlayer,

    /// <summary>Damages every living enemy standing in it. CH §3.3's fire pool.</summary>
    BurnsEnemies,
}

/// <summary>
/// A patch of ground that does something to whoever is standing in it, and the pulse that decides
/// they were. One system, because a second kind of zone is a field on the entry rather than a second
/// list (rule 10). See CC §6.4, GD §2, §13.2 and AR §18.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Placed, not carried, and that is the whole design.</b> A heal that followed the player would be
/// a regeneration buff with a circle drawn under it; a heal that stays where it was cast makes the
/// player choose between the ground that is healing them and the ground that is safe. GD §2's second
/// pillar is that skill expression <em>is</em> positioning, and this is the first active that asks
/// anything of it.
/// </para>
/// <para>
/// <b>The player's position comes off the <see cref="CombatBlackboard"/>, and that is a decision
/// rather than the only option.</b> <see cref="ProjectileSystem"/> — the class this one is otherwise
/// shaped after — is handed the position as a <c>Tick</c> parameter, and the same would work for
/// <see cref="Tick"/> here. It cannot work for <see cref="Spawn"/>: a zone is placed from inside
/// <c>IEffectHandler&lt;T&gt;.Apply</c>, which is called from the skills step and not from any
/// <c>Tick</c> of this class's, so no parameter of this class's reaches the moment that actually
/// needs a position — and a field cached from the last <see cref="Tick"/> would place the zone at
/// last frame's feet, for the same reason a cached clock would expire a grant a frame early
/// (M3-11a-ii). Given that one of the two moments must read a held table, both read the same one:
/// one fact, one door, rather than a class that answers "where is the player" two ways. The
/// blackboard is the right table for the reason it holds the rest — it is what a skill's condition
/// might need to know, filled once a tick by <c>PlayerCombat</c> before the runner is asked anything
/// (M3-06 rule 7), so at cast time it is <em>this</em> frame's position.
/// </para>
/// <para>
/// <b>It acts in pulses, not continuously.</b> <c>amountPerPulse</c> every <c>pulseInterval</c>,
/// scheduled as absolute times against the simulated clock — <c>Weapon</c>'s shape and
/// <see cref="TimedEffects"/>'. No accumulator to drift, so the same number of pulses land at 30 fps
/// and at 120, and a tick that swallows several intervals lands several pulses rather than losing
/// them. Continuous healing was the alternative and it is worse three ways: it publishes sixty events
/// a second or none, it is untestable without a tolerance, and it reads as a number sliding rather
/// than as the game doing something. A pulse is also what a view can flash (M3-11c).
/// </para>
/// <para>
/// <b>A pulse lands before the zone that is due to retire on the same instant does.</b> The two
/// clocks meet exactly — Consecrate's 6 s duration over its 0.5 s interval puts the twelfth pulse on
/// the expiry second — and the order decides whether the authored skill is worth 36 hit points or 33.
/// The zone is alive up to and including its last instant, so the pulse scheduled for that instant is
/// one it was alive for; retiring first would quietly make every authored zone worth one pulse less
/// than its own arithmetic says, with nothing anywhere recording the deduction. Pinned by
/// <c>Zone_PulsesOnTheTickItExpires</c>, which is a row of its own precisely because
/// <c>Zone_ExpiresAfterItsDuration</c> ticks past the coincidence and never sees it.
/// </para>
/// <para>
/// <b>A zone heals the player or burns enemies, and which is a field on the entry</b> (M6-07b rule
/// 1). Until M6-07b the player was the only thing it touched, and these remarks said that a second
/// subject would be <em>"a faction on the entry"</em>; <see cref="ZoneSide"/> is that field, arriving
/// from a player skill that burns rather than from an affix that heals. The schedule, the catch-up,
/// the capacity, the retirement order and the XZ test are unchanged, because none of them is about
/// who is standing there — the side is one parallel array and one branch in <c>Pulse</c>. A second
/// system was refused: it would duplicate everything here to differ in one call. An enemy-placed
/// zone is still M7-02's and would be a third member, decided then.
/// </para>
/// <para>
/// <b>What a burn reaches is held, not handed to <see cref="Tick"/></b> (M6-07b rule 2), which is
/// the opposite of <see cref="ProjectileSystem"/>'s arrangement and the call-site count is why:
/// <see cref="Tick"/> has several times more callers than the constructor, and every one of them
/// ticks a heal zone. The pair is optional and refused half-set, and <see cref="Spawn"/> refuses a
/// burn on a system built without it rather than placing fire that burns nobody.
/// </para>
/// <para>
/// <b>A zone owns its own life.</b> Nothing holds it in <see cref="TimedEffects"/> and the effect
/// handler's <c>Remove</c> does nothing: a granted shield is state on the player and has to be taken
/// back, while a zone is a thing in the world that ends when it ends. Re-casting therefore places a
/// <em>second</em> zone rather than refreshing the first — deliberately the opposite of
/// <see cref="TimedEffects.Hold"/>, which refreshes a pair rather than queuing a second note because
/// there a second note would come due on the first cast's clock and cut the refreshed shield short.
/// Both rules are right and they are opposite because one is a number on the player and the other is
/// a place on the floor.
/// </para>
/// <para>
/// <b>Fixed capacity, and a ninth throws rather than growing.</b> Consecrate at a 12 s cooldown and a
/// 6 s duration can have at most one of its own; eight is headroom for the damaging and slowing zones
/// GD §13.2 and M7-02 will want. It is deliberately not <c>TimedEffects.Capacity</c>'s sixteen or
/// <c>GrantedShieldPool.Capacity</c>'s eight-by-coincidence: this one limits places on the floor.
/// Nothing here allocates after construction — parallel arrays and a count, <c>SkillRunner</c>'s
/// shape rather than an array of structs.
/// </para>
/// </remarks>
public sealed class ZoneSystem
{
    /// <summary>How many zones may stand at once.</summary>
    public const int Capacity = 8;

    /// <summary>
    /// The most pulses one zone may ever schedule — its duration over its interval.
    /// </summary>
    /// <remarks>
    /// <b>A bound on the catch-up loop, and it is the door rather than the loop that carries it</b>
    /// (AR §18.3). <see cref="Tick"/> lands every pulse a zone was alive for, so a zone authored at a
    /// six-second duration and a one-microsecond interval is not a fast zone — it is six million heals
    /// inside one frame, which is a hang rather than a bug report. Consecrate schedules twelve, and
    /// ten thousand is past anything a designer could mean by a pulse.
    /// </remarks>
    public const int MaxPulses = 10_000;

    /// <summary>The first id of a run. Ids are issued from 1 so that 0 means nobody.</summary>
    private const int FirstId = 1;

    private readonly Health _player;
    private readonly CombatBlackboard _blackboard;
    private readonly IDomainEvents _events;

    /// <summary>What a burn reaches, or null for a system that places only heals — rule 2.</summary>
    private readonly EnemySystem _enemies;

    /// <summary>Who a blast set off by a burn would catch. Null exactly when <see cref="_enemies"/> is.</summary>
    private readonly PlayerCombat _combat;

    private readonly int[] _ids = new int[Capacity];
    private readonly Vector3[] _positions = new Vector3[Capacity];
    private readonly float[] _radii = new float[Capacity];
    private readonly float[] _heals = new float[Capacity];
    private readonly ZoneSide[] _sides = new ZoneSide[Capacity];
    private readonly float[] _intervals = new float[Capacity];
    private readonly float[] _nextPulseAt = new float[Capacity];
    private readonly float[] _expiresAt = new float[Capacity];

    /// <summary>
    /// Who placed each zone — the <c>ActiveSpec</c>, for a cast (M3-06 rule 8).
    /// </summary>
    /// <remarks>
    /// Held for the reason <see cref="TimedEffects"/> holds one: identity is the only thing a zone
    /// could ever be taken back by, and a placement with no owner is one nothing could dismiss.
    /// Nothing reads it today, because rule 9 gives a zone its own life and no caster takes one back
    /// — the first thing that does (a cleanse, a dismissal, M7-02's affixes) finds it here rather than
    /// having to change this signature. It is cleared when a zone retires, so a finished cast's spec
    /// is not held alive by an array the rest of the run reuses.
    /// </remarks>
    private readonly object[] _sources = new object[Capacity];

    private int _count;

    private int _nextId = FirstId;

    /// <param name="player">Whose hit points a pulse restores. The player's, this run.</param>
    /// <param name="blackboard">
    /// Where the player is standing, this tick — see the class remarks for why the position is read
    /// from here rather than handed in.
    /// </param>
    /// <param name="events">
    /// Where <c>ZoneSpawned</c>, <c>ZoneHealed</c>, <c>ZoneBurned</c> and <c>ZoneExpired</c> go.
    /// </param>
    /// <param name="enemies">
    /// What a <see cref="ZoneSide.BurnsEnemies"/> pulse reaches, or <see langword="null"/> for a
    /// system that cannot place one — M6-07b rule 2. Held rather than handed to <see cref="Tick"/>;
    /// see the class remarks.
    /// </param>
    /// <param name="combat">
    /// Who a blast set off by a burn would catch — <c>EnemySystem.ApplyDamage</c>'s fourth argument,
    /// which is not optional there. Null with <paramref name="enemies"/> and never without it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="player"/>, <paramref name="blackboard"/> or <paramref name="events"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// One of <paramref name="enemies"/> and <paramref name="combat"/> is null and the other is not.
    /// </exception>
    public ZoneSystem(
        Health player,
        CombatBlackboard blackboard,
        IDomainEvents events,
        EnemySystem enemies = null,
        PlayerCombat combat = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        // Refused half-set, at the one place both are visible: a system holding enemies and no
        // player could not tell a Bloater's blast whom to catch, and one holding a player and no
        // enemies has nothing to burn. Either is a wiring mistake rather than a configuration.
        if ((enemies is null) != (combat is null))
        {
            throw new ArgumentException(
                "A ZoneSystem that burns needs both the enemies it burns and the PlayerCombat a "
                    + "burn-triggered blast would catch, and one that does not needs neither. "
                    + $"Got enemies {(enemies is null ? "null" : "set")} and combat "
                    + $"{(combat is null ? "null" : "set")}.",
                enemies is null ? nameof(enemies) : nameof(combat));
        }

        _enemies = enemies;
        _combat = combat;
    }

    /// <summary>How many zones are standing right now.</summary>
    public int Count => _count;

    /// <summary>
    /// Where the zone at <paramref name="index"/> was placed, in world metres.
    /// </summary>
    /// <remarks>
    /// An index into the live zones in the order they were placed, and it is <em>not</em> a handle:
    /// a zone retiring shifts the ones after it down, exactly as <c>TimedEffects</c> does, so anything
    /// that has to follow one zone across ticks follows the id the events carry. This is the read an
    /// overlay walks from 0 to <see cref="Count"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">There is no zone at that index.</exception>
    public Vector3 PositionAt(int index)
    {
        Require(index);

        return _positions[index];
    }

    /// <summary>How far the zone at <paramref name="index"/> reaches, in metres.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no zone at that index.</exception>
    public float RadiusAt(int index)
    {
        Require(index);

        return _radii[index];
    }

    /// <summary>What the zone at <paramref name="index"/> does. For an overlay and the tests.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no zone at that index.</exception>
    public ZoneSide SideAt(int index)
    {
        Require(index);

        return _sides[index];
    }

    /// <summary>
    /// Places one where the player is standing, or at <paramref name="at"/>, and publishes
    /// <c>ZoneSpawned</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The position is taken once, here, and never revised</b> (rule 1). What is read is the
    /// blackboard's, which <c>PlayerCombat.Tick</c> filled earlier in this same frame — so a cast on
    /// the tick the player moved lands under their feet rather than under where they were.
    /// </para>
    /// <para>
    /// <b>Unless <paramref name="at"/> says otherwise, and a Blink is why it can</b> (M6-07b rule 3).
    /// A cast is resolved after <c>PlayerCombat.UpdateBlackboard</c>, so the blackboard is this
    /// frame's; a dash's start edge is resolved <em>before</em> it, where the blackboard is last
    /// frame's. That caller passes the position it was handed instead, and every cast passes nothing.
    /// </para>
    /// <para>
    /// <b>The first pulse is one whole interval away.</b> A zone that healed on the instant it was
    /// placed would pay out before the player had chosen to stand in it, which is the decision the
    /// whole skill is made of; and it would make the authored arithmetic — duration over interval —
    /// count one pulse more than it says.
    /// </para>
    /// </remarks>
    /// <param name="radius">How far it reaches, in metres. Finite and greater than zero.</param>
    /// <param name="duration">How long it stands, in simulated seconds. Finite and greater than zero.</param>
    /// <param name="amountPerPulse">
    /// Hit points one pulse restores, or takes off — <paramref name="side"/> says which. Finite and
    /// greater than zero.
    /// </param>
    /// <param name="pulseInterval">Simulated seconds between pulses. Finite and greater than zero.</param>
    /// <param name="now">Simulated run time — <c>RunState.Time</c>, never a wall clock.</param>
    /// <param name="source">
    /// Who placed it — the <c>ActiveSpec</c>, for a cast (M3-06 rule 8). Identity only; see
    /// <see cref="_sources"/>.
    /// </param>
    /// <param name="side">What the zone does to whoever is inside — M6-07b rule 1.</param>
    /// <param name="at">
    /// Where to place it, or <see langword="null"/> for the blackboard's position — M6-07b rule 3.
    /// </param>
    /// <returns>Its run-stable id, issued from 1 and never reused within a run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A number is not finite and greater than zero, <paramref name="now"/> is not finite, or
    /// <paramref name="at"/> has a component that is not.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Capacity"/> zones already stand, or <paramref name="side"/> is
    /// <see cref="ZoneSide.BurnsEnemies"/> on a system built with nothing to burn (M6-07b rule 2).
    /// </exception>
    public int Spawn(
        float radius,
        float duration,
        float amountPerPulse,
        float pulseInterval,
        float now,
        object source,
        ZoneSide side = ZoneSide.HealsThePlayer,
        Vector3? at = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        Positive(radius, nameof(radius));
        Positive(duration, nameof(duration));
        Positive(amountPerPulse, nameof(amountPerPulse));
        Positive(pulseInterval, nameof(pulseInterval));
        RequireFinite(now);

        if (at is Vector3 given && !(Finite(given.X) && Finite(given.Y) && Finite(given.Z)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(at),
                given,
                "A zone placed at a non-finite position fails every containment test and burns or "
                    + "heals nobody, silently.");
        }

        // Refused at the door that can see it, not at the constructor that cannot (M6-07b rule 2,
        // M6-01a rule 5's direction): a system built with no enemies may still place heals, and a
        // burn placed on it would stand, pulse and reach nobody with nothing logged.
        if (side == ZoneSide.BurnsEnemies && _enemies is null)
        {
            throw new InvalidOperationException(
                "This ZoneSystem was built without an EnemySystem and a PlayerCombat, so it cannot "
                    + "place a zone that burns enemies. RunSession wires both; a fixture that wants "
                    + "a burn must pass them to the constructor.");
        }

        if (duration / pulseInterval > MaxPulses)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pulseInterval),
                pulseInterval,
                $"A zone lasting {duration} s with a pulse every {pulseInterval} s schedules more "
                    + $"than {MaxPulses} pulses, which is an authoring mistake rather than a fast "
                    + "zone: every one of them lands inside the tick the zone is over.");
        }

        if (_count == Capacity)
        {
            throw new InvalidOperationException(
                $"No more than {Capacity} zones may stand at once, and a ninth is a bug rather than "
                    + "a build: Consecrate at a 12 s cooldown and a 6 s duration can have at most one "
                    + "of its own. Growing the array instead would put an allocation on a per-frame "
                    + "path.");
        }

        int id = _nextId;
        _nextId++;

        int slot = _count;

        _ids[slot] = id;
        _positions[slot] = at ?? _blackboard.PlayerPosition;
        _radii[slot] = radius;
        _heals[slot] = amountPerPulse;
        _sides[slot] = side;
        _intervals[slot] = pulseInterval;
        _nextPulseAt[slot] = now + pulseInterval;
        _expiresAt[slot] = now + duration;
        _sources[slot] = source;

        _count++;

        // After the entry exists, so a listener reading ActiveZoneCount from inside it counts this
        // one — SkillRunner.Fire's rule 9, one layer down.
        _events.Publish(new ZoneSpawned(id, _positions[slot], radius, duration));

        return id;
    }

    /// <summary>
    /// Pulses the zones that are due and retires the ones that are over. Allocates nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pulses first, then the retirement</b> — see the class remarks: the two clocks meet exactly
    /// on an authored zone's last second, and the order is worth a pulse of every zone in the game.
    /// </para>
    /// <para>
    /// <b>A pulse heals whoever is inside at the instant it lands</b>, by squared distance on the XZ
    /// plane (AR §18.4, and every other range check in the project). In and out between pulses costs
    /// nothing and gains nothing: standing in it when the pulse lands is the whole of the contract,
    /// which is legible in a way a "time inside" accumulator would not be.
    /// </para>
    /// <para>
    /// <b>Catch-up, not skip.</b> A tick that swallows three intervals lands three pulses, because the
    /// schedule is absolute — which is what makes <c>Zone_PulsesAreAbsolute</c> true at any frame
    /// rate, and what stops a frame hitch from silently costing the player hit points. Pulses are
    /// capped at the expiry, so a tick taken long after a zone was over lands only the pulses the zone
    /// was alive for.
    /// </para>
    /// <para>
    /// The walk does not advance across a retirement, because retiring shifts the entry after it into
    /// the slot just vacated. A <see cref="Spawn"/> from inside a published handler appends past the
    /// end of the walk and is visited with a first pulse that is one interval away, so it cannot pulse
    /// on the tick it was placed.
    /// </para>
    /// </remarks>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="now"/> is not finite.</exception>
    public void Tick(float now)
    {
        RequireFinite(now);

        var i = 0;

        while (i < _count)
        {
            // Everything due at or before this instant, but never past the zone's own end.
            float until = now < _expiresAt[i] ? now : _expiresAt[i];

            while (until >= _nextPulseAt[i])
            {
                _nextPulseAt[i] += _intervals[i];

                Pulse(i, now);
            }

            if (now >= _expiresAt[i])
            {
                Retire(i);

                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// Forgets every zone, silently. For the end of a run only.
    /// </summary>
    /// <remarks>
    /// Publishes nothing, for <c>ProjectileSystem.Clear</c>'s reason at the same moment: the scope is
    /// going away and with it every subscriber an event could reach, so a farewell per zone would be
    /// noise. A stage boundary deliberately does <em>not</em> call this — a zone outlives the wave it
    /// was cast during, which is M2-10's rule that a door heals nobody seen from the other side.
    /// </remarks>
    public void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            _sources[i] = null;
        }

        _count = 0;
        _nextId = FirstId;
    }

    /// <remarks>
    /// <c>!(value &gt; 0f)</c> rather than <c>value &lt;= 0f</c>, so NaN is refused too (AR §18.3),
    /// and infinity separately because it passes a <c>&gt; 0</c> test. A NaN radius makes every
    /// containment test answer <em>no</em>; a NaN interval makes every pulse comparison false. Both
    /// are a zone that stands there doing nothing, with nothing logged.
    /// </remarks>
    private static void Positive(float value, string paramName)
    {
        if (!(value > 0f) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be a finite number greater than zero. A zone with no extent, no "
                    + "life, no pulse or no heal is one that is drawn and does nothing.");
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <remarks>
    /// Refused rather than trusted, which is <c>ProjectileSystem</c>'s answer rather than
    /// <c>SkillRunner</c>'s: a non-finite clock here is not merely a step that does nothing, it is a
    /// pulse schedule that cannot be compared against — every zone immortal and silent, or, at the
    /// other end of the same comparison, a catch-up loop with no exit.
    /// </remarks>
    private static void RequireFinite(float now)
    {
        if (float.IsNaN(now) || float.IsInfinity(now))
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                now,
                "now must be finite. It is RunState.Time, which is a sum of clamped frame times, so "
                    + "a non-finite one is a mis-wired clock rather than a long session.");
        }
    }

    /// <summary>
    /// Does what the zone at <paramref name="index"/> does to whoever is standing in it, and says so.
    /// </summary>
    /// <remarks>
    /// <b>The one branch on <see cref="ZoneSide"/> in the class</b> (M6-07b rule 1). A burn is
    /// <see cref="Burn"/>; everything below the branch is the heal, unchanged since M3-11b.
    /// <para>
    /// <c>Health.Heal</c> is already promised to be safe here, in its own remarks: a heal neither
    /// starts i-frames nor holds off the Aegis refill, because <em>"a heal that reset the Aegis delay
    /// would make a Consecrate zone (M3-11) actively counterproductive for the Oathbound"</em> — the
    /// class named this task and kept the promise before the task existed. It also returns what was
    /// actually restored and returns zero when dead or full, which is this event's <c>Amount</c> for
    /// free. Nothing is re-implemented here.
    /// </para>
    /// </remarks>
    private void Pulse(int index, float now)
    {
        if (_sides[index] == ZoneSide.BurnsEnemies)
        {
            Burn(index, now);

            return;
        }

        float dx = _blackboard.PlayerPosition.X - _positions[index].X;
        float dz = _blackboard.PlayerPosition.Z - _positions[index].Z;

        // XZ only (AR §18.4): a metre of height is a camera's business, and counting it would make
        // standing on a step leave the zone the player is visibly inside of.
        if ((dx * dx) + (dz * dz) > _radii[index] * _radii[index])
        {
            return;
        }

        float healed = _player.Heal(_heals[index]);

        // Published after the hit points moved, and published even when it restored nothing: a view
        // that flashed on a wasted pulse would be lying, and one that never heard about it could not
        // tell a zone being stood in at full health from a zone being stood outside of.
        _events.Publish(new ZoneHealed(_ids[index], healed));
    }

    /// <summary>
    /// Damages every living enemy standing in the zone at <paramref name="index"/>, one
    /// <see cref="ZoneBurned"/> each — M6-07b rules 6 and 7.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>ProjectileSystem.LandOnEnemies</c>' walk</b>: the registry's span with the
    /// <c>IsAlive</c> test, because registered is not breathing and a corpse inside a pool takes
    /// nothing; squared XZ distance (AR §18.4), inclusive and spelled as the negated <c>&lt;=</c> so
    /// a distance that is not a number skips the agent; and <c>EnemySystem.ApplyDamage</c> with the
    /// player, which is what makes a pool that kills a Bloater set off the blast that can catch the
    /// Emberwright standing in their own fire. Killing an agent leaves it registered, so the span
    /// stays valid across the walk.
    /// </para>
    /// <para>
    /// <b>Nothing here feeds Kindling</b> (M6-07a rule 6). The ramp's doors are the swing and the
    /// orb, both in <c>PlayerCombat</c>'s weapon path; a pool is the movement button.
    /// </para>
    /// </remarks>
    private void Burn(int index, float now)
    {
        ReadOnlySpan<EnemyAgent> agents = _enemies.Registry.Alive;

        Vector3 centre = _positions[index];
        float radiusSquared = _radii[index] * _radii[index];

        for (int i = 0; i < agents.Length; i++)
        {
            EnemyAgent agent = agents[i];

            if (!agent.IsAlive)
            {
                continue;
            }

            Vector3 position = agent.Position;

            float dx = position.X - centre.X;
            float dz = position.Z - centre.Z;

            if (!((dx * dx) + (dz * dz) <= radiusSquared))
            {
                continue;
            }

            DamageResult result = _enemies.ApplyDamage(agent.Id, _heals[index], now, _combat);

            // After the damage, so a subscriber reading the enemy's health sees the burn applied.
            // "Nothing arrived" is EnemySystem.ApplyDamage's own spelling of silence, so this event
            // goes out exactly when EnemyDamaged did — per enemy damaged, and none for nobody.
            if (result.Blocked || result.Applied > 0f)
            {
                _events.Publish(new ZoneBurned(_ids[index], agent.Id, result.Applied));
            }
        }
    }

    /// <summary>Drops the zone at <paramref name="index"/> and says so.</summary>
    /// <remarks>
    /// Shifted rather than swapped with the last entry, for <see cref="TimedEffects"/>'s reason: the
    /// order the entries sit in is the order they were placed in, and it is what
    /// <see cref="PositionAt"/> hands an overlay.
    /// </remarks>
    private void Retire(int index)
    {
        int id = _ids[index];

        for (int i = index; i < _count - 1; i++)
        {
            _ids[i] = _ids[i + 1];
            _positions[i] = _positions[i + 1];
            _radii[i] = _radii[i + 1];
            _heals[i] = _heals[i + 1];
            _sides[i] = _sides[i + 1];
            _intervals[i] = _intervals[i + 1];
            _nextPulseAt[i] = _nextPulseAt[i + 1];
            _expiresAt[i] = _expiresAt[i + 1];
            _sources[i] = _sources[i + 1];
        }

        _count--;

        // The vacated slot is cleared rather than left: a stale reference would hold an ActiveSpec
        // alive past the run that cast it.
        _sources[_count] = null;

        // After the state moved, like the other two.
        _events.Publish(new ZoneExpired(id));
    }

    /// <exception cref="ArgumentOutOfRangeException">There is no zone at that index.</exception>
    private void Require(int index)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"There is no zone at that index. {_count} are standing.");
        }
    }
}

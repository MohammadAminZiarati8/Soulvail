using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>
/// The Wights standing on the player's side, end to end: the friendly registry, the one frame of
/// senses they get, the one behaviour they have, and the door damage would reach one through. The
/// run owns one; nothing else may raise or retire a minion. See CH §3.2, AR §3, §9, §18.1 and
/// M5-04a.
/// </summary>
/// <remarks>
/// <para>
/// <b>One class where the enemies need three</b>, and the difference is how much a Wight does.
/// <c>EnemyRegistry</c>, <c>EnemySystem</c> and an <c>IEnemyBehaviour</c> per archetype exist
/// because an enemy has a state machine, a blackboard, depth scaling, a corpse clock, a roster and
/// a director. A Wight walks at the nearest living enemy and hits it; splitting that across three
/// files would be a seam invented for a shape nobody has.
/// </para>
/// <para>
/// <b>A Wight is never an <see cref="EnemyAgent"/></b> (rule 1) — the argument is on
/// <see cref="MinionAgent"/>, and what it means here is that nothing in this class touches
/// <c>EnemyRegistry</c> except to read <c>Alive</c>. The player's targeter cannot see a Wight, the
/// director's stage-complete check cannot count one, and a kill a Wight scores pays experience
/// through the one door every death already comes through (rule 11).
/// </para>
/// <para>
/// <b>Enemies do not fight back, and that is a ruling rather than an omission</b> (rule 9). No
/// behaviour retargets onto a Wight, nothing damages one, and <see cref="ApplyDamage"/> ships called
/// by nothing. CH §3.2 is <em>"You don't fight. The dead do"</em> — the class works with Wights as a
/// second source of damage, and the only thing that dies in M5 is a Wight's own clock. Making
/// enemies choose between the player and a Wight is the <c>TargetId</c> question
/// <c>LureSystem</c>'s remarks refuse and M7-01's Choir pays for; doing it here would make a
/// twenty-second body into the arena's whole attention economy with no playtest behind it.
/// </para>
/// <para>
/// <b>Nothing here allocates.</b> One array of <see cref="MaxConcurrent"/> agents built at
/// construction and recycled in place, four linear passes over at most eight, a scan of the enemy
/// span per pass, struct intents and struct events (AR §4.3).
/// </para>
/// </remarks>
public sealed class MinionSystem
{
    /// <summary>
    /// The most Wights that may stand at once, whatever a node says. Eight: CH §3.2's base of three
    /// plus The Host's four, with one spare — rule 3.
    /// </summary>
    /// <remarks>
    /// <b>A constant where <see cref="Cap"/> is a <see cref="Stat"/>, and the pair is the whole of
    /// rule 3.</b> A <c>Stat</c> clamps nothing (ADR-0008), so a modifier stack could drive the live
    /// cap to forty — and a system that resized to meet it would allocate on the path Rise puts on
    /// every kill. So the array is sized here, once, and a spawn above this is refused exactly as
    /// one above the cap is.
    /// </remarks>
    public const int MaxConcurrent = 8;

    /// <summary>How often a Wight re-chooses what it is walking at, in seconds. Rule 6.</summary>
    /// <remarks>
    /// <b>A cadence rather than every tick, for <c>Targeter</c>'s reason at 10 Hz:</b> a minion that
    /// re-picks every frame oscillates between two equidistant Husks and walks nowhere. Half a
    /// second is slow enough to commit and fast enough that a Wight does not cross the arena after
    /// something that died — and a quarry that dies is dropped immediately whatever this says, so
    /// what the cadence delays is a <em>change</em> of mind and never a <em>dead</em> one.
    /// </remarks>
    public const float RetargetInterval = 0.5f;

    /// <summary>The first id of a run. One rather than zero — see <see cref="MinionAgent.QuarryId"/>.</summary>
    private const int FirstId = 1;

    /// <summary>
    /// Below this separation a Wight and its quarry are the same point and there is no direction to
    /// walk in. <c>EnemySystem</c>'s number and its reason: dividing by 1e-9 yields a unit vector
    /// made of noise, which is worse than admitting there is no answer.
    /// </summary>
    private const float MinDirectionDistance = 1e-4f;

    private readonly MinionSpec _spec;
    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;

    /// <summary>
    /// The whole army, built once. <c>[0, _count)</c> is standing, in the order it was raised;
    /// everything at or past the count is a body waiting to be raised again.
    /// </summary>
    /// <remarks>
    /// Retiring shifts the entries after it down and puts the retired instance in the slot that
    /// falls off the end, so the array always holds all <see cref="MaxConcurrent"/> agents and the
    /// next <see cref="Spawn"/> takes the one just freed. <c>LureSystem.Retire</c>'s shift, with the
    /// object kept — which is what makes "raised in order" a property of the storage and recycling
    /// free at the same time.
    /// </remarks>
    private readonly MinionAgent[] _agents = new MinionAgent[MaxConcurrent];

    private int _count;

    private int _nextId = FirstId;

    /// <summary>
    /// Seconds since the whole army last re-chose. One clock rather than one per Wight, because the
    /// cadence is a property of <em>this system's</em> thinking rate and not of any one body — the
    /// same reading <c>Targeter</c> takes of its own — and because a per-agent deadline would be a
    /// fourth field on <see cref="MinionAgent"/> that nothing outside this class could use.
    /// </summary>
    private float _sinceRetarget;

    /// <param name="spec">The class's minions — the run's one <c>minion.wight</c>.</param>
    /// <param name="recipe">
    /// What this run says a Wight is born with (M5-06a rule 3). Built from <paramref name="spec"/>
    /// by <c>RunSession.Start</c> and handed in rather than made here, because the same object is
    /// what <c>ModifyStatHandler</c> aims <see cref="StatTarget.Minions"/> at — one recipe per run,
    /// or a node would move numbers no body reads.
    /// </param>
    /// <param name="events">Where <see cref="MinionSpawned"/> and its three siblings go.</param>
    /// <param name="intents">Where a Wight's walk leaves through — <see cref="IIntentSink.MinionMove"/>.</param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    public MinionSystem(
        MinionSpec spec,
        MinionRecipe recipe,
        IDomainEvents events,
        IIntentSink intents)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));

        // Checked here and then handed on rather than stored: the army holds the recipe through its
        // bodies, which is where Initialise reads it, and a second reference on this class would be
        // a field nothing reads.
        if (recipe is null)
        {
            throw new ArgumentNullException(nameof(recipe));
        }

        _events = events ?? throw new ArgumentNullException(nameof(events));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));

        Cap = new Stat(spec.Cap);

        // Built here and never again, which is rule 12: Rise (M5-04b) raises a Wight from inside
        // EnemySystem's kill path, and a `new` on that path is a GC spike behind every fourth kill.
        // The ids and positions are placeholders — nothing is standing, so nothing can see them, and
        // Initialise sets every one of them on the way out of the pool.
        for (int i = 0; i < _agents.Length; i++)
        {
            _agents[i] = new MinionAgent(0, spec, recipe, Vector3.Zero, float.NegativeInfinity);
        }
    }

    /// <summary>
    /// How many Wights may stand right now, live. Where CH §3.2's The Host — <em>"minion cap
    /// +4"</em> — lands as a <c>Flat</c> modifier (rule 3).
    /// </summary>
    /// <remarks>
    /// Seeded from <see cref="MinionSpec.Cap"/>, and read through a clamp at the point of use
    /// because a <see cref="Stat"/> clamps nothing: a live value at or below zero stands nobody up,
    /// a value at or above <see cref="MaxConcurrent"/> is that ceiling, and a non-finite one refuses
    /// — the direction every comparison in core takes, because a modifier stack nobody can read must
    /// not become a guaranteed army (AR §18.3).
    /// </remarks>
    public Stat Cap { get; }

    /// <summary>How many Wights are standing right now.</summary>
    public int Count => _count;

    /// <summary>
    /// Every standing Wight, in the order it was raised. Borrowed — valid until the next
    /// <see cref="Spawn"/>, <see cref="Tick"/>, <see cref="ApplyDamage"/> or <see cref="Clear"/>,
    /// and never to be retained.
    /// </summary>
    /// <remarks>
    /// A span over a preallocated array rather than a list, for <c>EnemyRegistry.Alive</c>'s reason:
    /// a <c>ReadOnlySpan</c> over a <c>List&lt;T&gt;</c> needs <c>CollectionsMarshal.AsSpan</c>,
    /// which is not in Unity's netstandard 2.1 profile. Unlike that one it holds no corpses — a
    /// Wight leaves the army on the tick it dies (rule 10) — so everything in here is breathing.
    /// </remarks>
    public ReadOnlySpan<MinionAgent> Alive => new ReadOnlySpan<MinionAgent>(_agents, 0, _count);

    /// <summary>Finds a standing Wight by id.</summary>
    /// <remarks>
    /// A linear scan of at most <see cref="MaxConcurrent"/> rather than <c>EnemyRegistry</c>'s
    /// dictionary, which is the right trade at eight: eight int comparisons beat a hash, and there
    /// is no table to keep in step with the shift <see cref="Spawn"/> and the retirements do.
    /// </remarks>
    /// <returns><see langword="false"/> and a null <paramref name="agent"/> for an id that is not
    /// standing — one that expired, one that died, or one that never existed.</returns>
    public bool TryGet(int id, out MinionAgent agent)
    {
        if (IndexOf(id, out int index))
        {
            agent = _agents[index];

            return true;
        }

        agent = null;

        return false;
    }

    /// <summary>
    /// Stands one up at <paramref name="position"/>, expiring at <c>now + lifespan</c>, and
    /// announces it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The Wight is born from this run's <c>MinionRecipe</c></b> (M5-06a rule 3), which
    /// <see cref="MinionAgent.Initialise"/> re-bases all three of its <see cref="Stat"/>s from on
    /// the way out of the pool. So a Legion node taken a moment ago is on this body and a Wight
    /// already standing is unchanged — the lag CH §3.2's twenty-second lifespan bounds.
    /// </para>
    /// <para>
    /// <b>A refused spawn is silent</b> (rule 3). At the live cap — or at
    /// <see cref="MaxConcurrent"/>, whichever is lower — this publishes nothing and returns null,
    /// because <c>ProjectileSystem.Fire</c>'s bargain holds here too: one lost Wight is better than
    /// an exception that ends a run, and M5-04b's Rise draws against a full army often enough that
    /// a throw would be a routine event. The oldest are kept rather than the newest, which is the
    /// honest reading of <em>"cap 3"</em> — a cap is not a queue.
    /// </para>
    /// </remarks>
    /// <param name="position">Where it stands up, in world metres — where the corpse fell.</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <returns>The Wight, or <see langword="null"/> when the raise was refused.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="position"/> has a non-finite component, or <paramref name="now"/> is not
    /// finite.
    /// </exception>
    public MinionAgent Spawn(Vector3 position, float now)
    {
        RequireFinite(position);
        RequireFinite(now, nameof(now));

        // One test rather than two: LiveCap is already bounded by MaxConcurrent, so the ceiling and
        // the authored cap refuse through the same line and there is no order between them to get
        // wrong.
        if (_count >= LiveCap())
        {
            return null;
        }

        int id = _nextId;
        _nextId++;

        MinionAgent agent = _agents[_count];

        agent.Initialise(id, position, now + _spec.Lifespan);

        _count++;

        // After the body is standing, so a listener that reads Count from inside this event counts
        // this one — LureSystem.Drop's rule, and EnemySystem.Spawn's register-then-announce.
        _events.Publish(new MinionSpawned(id, _spec.SpecId, position, _spec.Lifespan));

        return agent;
    }

    /// <summary>
    /// One frame of senses: copies the snapshot's positions onto the Wights it names (AR §3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A body reports where it is, and core never assigns it</b> (ADR-0003).
    /// <c>EnemySystem.Ingest</c>'s first pass exactly, with the same two bargains: an id the system
    /// does not know is ignored rather than refused, because a view is allowed to lag a frame behind
    /// a despawn; and a Wight the snapshot does not name keeps its last position, because that is a
    /// view that has not reported yet and the position it was raised at is the best answer there is.
    /// </para>
    /// <para>
    /// <b><c>EnemySense.PathDirectionToPlayer</c> and <c>EnemySense.HasLineOfSight</c> are
    /// deliberately unread</b> (rule 4). A Wight walks in a straight line at something inside the
    /// arena, and <c>NavPathSense</c> only ever paths to the player — so a path copied here would be
    /// a route to the wrong place. <b>Until M5-05 fills the array, it is empty in play and filled
    /// only by tests</b>, which is stated so an empty array is not read as a fault.
    /// </para>
    /// </remarks>
    public void Ingest(WorldSnapshot snapshot)
    {
        // Stops at MinionCount, never at Minions.Length: Clear() leaves the array's contents alone,
        // so everything past the count is last frame's Wights (AR §4.2).
        for (int i = 0; i < snapshot.MinionCount; i++)
        {
            ref EnemySense sense = ref snapshot.Minions[i];

            if (!IndexOf(sense.Id, out int index))
            {
                continue;
            }

            MinionAgent agent = _agents[index];

            agent.Position = sense.Position;
            agent.Velocity = sense.Velocity;
        }
    }

    /// <summary>
    /// One tick of the army: expire, choose, walk, strike — rule 7's order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Four passes, and the order between them is the contract</b> (rule 7).
    /// <em>Expiring first</em> means a Wight in its last frame does not pick a target it will never
    /// reach, and that nothing walks or swings on the tick its clock ran out.
    /// <em>Striking last</em> means it strikes from the position it was <em>reported</em> at this
    /// frame rather than the one it is walking to — the same discipline <c>ChaserBehaviour</c>
    /// keeps, and the reason a strike is a distance test against <see cref="MinionSpec.Reach"/>
    /// rather than a swept volume.
    /// </para>
    /// <para>
    /// <b>The kill goes through <c>EnemySystem.ApplyDamage</c> and through nothing else</b>
    /// (rule 11), which is why this method needs the player: that door resolves a Bloater's blast,
    /// and a blast catches the player. So a Wight that kills a Bloater can hurt the person who
    /// raised it, which is correct and would have been silently untrue if this class had reached for
    /// a narrower door.
    /// </para>
    /// </remarks>
    /// <param name="dt">Seconds since the last tick — the snapshot's, never a wall clock. It drives the retarget cadence and nothing else.</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>.</param>
    /// <param name="enemies">The census a Wight chooses from and hits through.</param>
    /// <param name="player">
    /// Who a blast a Wight's kill sets off would catch. Required rather than optional, for the
    /// reason <c>EnemySystem.ApplyDamage</c> requires one: the compiler should enumerate every call
    /// site the day something else explodes rather than letting one silently opt out.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="enemies"/> or <paramref name="player"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dt"/> or <paramref name="now"/> is not finite.</exception>
    public void Tick(float dt, float now, EnemySystem enemies, PlayerCombat player)
    {
        RequireFinite(dt, nameof(dt));
        RequireFinite(now, nameof(now));

        if (enemies is null)
        {
            throw new ArgumentNullException(nameof(enemies));
        }

        if (player is null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        Expire(now);

        // Advanced whether or not anything is standing, so the cadence is a property of the run's
        // clock rather than of who happens to be alive — an army raised mid-interval joins the
        // rhythm the arena is already on instead of starting its own.
        _sinceRetarget += dt;

        bool rechoose = _sinceRetarget >= RetargetInterval;

        if (rechoose)
        {
            _sinceRetarget = 0f;
        }

        Choose(enemies, rechoose);
        Walk(enemies);
        Strike(enemies, player, now);
    }

    /// <summary>
    /// Applies <paramref name="amount"/> to the Wight with <paramref name="minionId"/> and, if that
    /// finished it, retires it and says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one door damage reaches a Wight through, and it ships called by nothing</b> (rule 9).
    /// It exists so that when something does hurt one there is a door rather than a new mechanism.
    /// </para>
    /// <para>
    /// <b>A death is not an expiry</b> (rule 10). This publishes <see cref="MinionDied"/> with the
    /// position — M5-06's Second Death keystone wants it — and never
    /// <see cref="MinionDespawned"/>, which belongs to the clock. They are separate so that a
    /// keystone paying for a Wight's death cannot fire on one that simply timed out.
    /// </para>
    /// <para>
    /// <b>A call that did nothing says nothing</b> — <c>EnemySystem.ApplyDamage</c>'s rule and its
    /// spelling. An unknown id, a non-positive amount and a NaN all report
    /// <see cref="DamageResult.None"/> without publishing.
    /// </para>
    /// <para>
    /// <b>Not safe to call from inside <see cref="Tick"/>'s own passes</b>, and nothing does: a
    /// death retires immediately, which shifts the array those passes are walking. The day something
    /// hurts a Wight from inside the frame, it queues the way <c>EnemySystem.DespawnAtEndOfTick</c>
    /// does.
    /// </para>
    /// </remarks>
    /// <param name="minionId">Who to hurt. An id that is not standing is not an error.</param>
    /// <param name="amount">Damage to apply. Zero, negative and NaN all do nothing.</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <returns>What <c>Health</c> did, unchanged, for a caller with its own conclusions to draw.</returns>
    public DamageResult ApplyDamage(int minionId, float amount, float now)
    {
        if (!IndexOf(minionId, out int index))
        {
            return DamageResult.None;
        }

        MinionAgent agent = _agents[index];

        DamageResult result = agent.Health.ApplyDamage(amount, now);

        // Spelled as "nothing arrived" rather than as a comparison against None, so a future
        // DamageResult field cannot quietly change what counts as silence — EnemySystem's spelling.
        if (!result.Blocked && !(result.Applied > 0f))
        {
            return result;
        }

        if (result.Killed)
        {
            // Read before the retirement, because the retirement is what makes the id stop
            // resolving — and the event has to carry somewhere to draw the death.
            Vector3 where = agent.Position;

            Remove(index);

            _events.Publish(new MinionDied(minionId, where));
        }

        return result;
    }

    /// <summary>
    /// Sends the whole army away without announcing anything. For the end of a run.
    /// </summary>
    /// <remarks>
    /// <c>EnemySystem.Clear</c>'s silence and its reason: the scope is going away and with it every
    /// subscriber a farewell could reach. The ids go back to one with the bodies, as
    /// <c>EnemyRegistry.Clear</c> does — safe exactly because nothing outside is still holding one.
    /// <b><see cref="Cap"/> is deliberately left alone</b>: it carries whatever nodes the player has
    /// taken, and those belong to the run rather than to the arena — the same reason
    /// <c>EnemySystem.Clear</c> does not reset the depth.
    /// </remarks>
    public void Clear()
    {
        _count = 0;
        _nextId = FirstId;
        _sinceRetarget = 0f;
    }

    /// <summary>Retires every Wight whose twenty seconds are up, oldest first, and announces each.</summary>
    /// <remarks>
    /// The walk does not advance across a retirement, because retiring shifts the entry after it
    /// into the slot just vacated — <c>LureSystem.Tick</c>'s shape. A <see cref="Spawn"/> from
    /// inside a published handler appends past the end of the walk with a moment that is still in
    /// the future, so it cannot expire on the tick it was raised.
    /// </remarks>
    private void Expire(float now)
    {
        var i = 0;

        while (i < _count)
        {
            if (now >= _agents[i].ExpiresAt)
            {
                int id = _agents[i].Id;

                Remove(i);

                // After the state moved, so a listener that asks how many are standing is told the
                // truth and one that resolves the id correctly finds nothing.
                _events.Publish(new MinionDespawned(id));

                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// Points every Wight at the nearest living enemy — on the cadence, or immediately when it has
    /// nobody (rule 6).
    /// </summary>
    private void Choose(EnemySystem enemies, bool rechoose)
    {
        EnemyRegistry registry = enemies.Registry;

        ReadOnlySpan<EnemyAgent> candidates = registry.Alive;

        for (int i = 0; i < _count; i++)
        {
            MinionAgent agent = _agents[i];

            // A quarry that has died or been despawned is dropped whatever the cadence says, which
            // is what makes rule 6 a delay on changing its mind rather than on noticing a corpse.
            if (agent.QuarryId != 0
                && (!registry.TryGet(agent.QuarryId, out EnemyAgent quarry) || !quarry.IsAlive))
            {
                agent.QuarryId = 0;
            }

            if (agent.QuarryId != 0 && !rechoose)
            {
                continue;
            }

            agent.QuarryId = Nearest(candidates, agent.Position);
        }
    }

    /// <summary>
    /// Writes one <see cref="EnemyMoveIntent"/> per standing Wight, every tick, through
    /// <see cref="IIntentSink.MinionMove"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One per Wight per tick, zero velocity included</b> — <see cref="EnemyMoveIntent"/>'s own
    /// rule: a tick that emitted nothing would leave the body applying whatever it last read, which
    /// is a Wight sliding after a quarry that died a second ago.
    /// </para>
    /// <para>
    /// <b>It stops inside its reach rather than shoving its quarry across the arena</b> —
    /// <c>ChaserBehaviour</c>'s discipline. The facing is kept while it stands there, because a body
    /// reads a zero facing as <em>"keep the one you have"</em> and a Wight mid-swing should be
    /// looking at what it is hitting.
    /// </para>
    /// </remarks>
    private void Walk(EnemySystem enemies)
    {
        EnemyRegistry registry = enemies.Registry;

        for (int i = 0; i < _count; i++)
        {
            MinionAgent agent = _agents[i];

            Vector3 velocity = Vector3.Zero;
            Vector2 facing = Vector2.Zero;

            if (agent.QuarryId != 0 && registry.TryGet(agent.QuarryId, out EnemyAgent quarry))
            {
                Vector3 position = agent.Position;

                float dx = quarry.Position.X - position.X;
                float dz = quarry.Position.Z - position.Z;

                // XZ, not the full 3D separation (AR §18.4): the Y difference between two capsule
                // centres is a rendering detail, and counting it would make a Wight walk into the
                // floor.
                float distance = MathF.Sqrt((dx * dx) + (dz * dz));

                if (distance >= MinDirectionDistance)
                {
                    facing = new Vector2(dx / distance, dz / distance);

                    if (distance > _spec.Reach)
                    {
                        float speed = agent.MoveSpeed.Value;

                        velocity = new Vector3(facing.X * speed, 0f, facing.Y * speed);
                    }
                }
            }

            _intents.MinionMove(new EnemyMoveIntent(agent.Id, velocity, facing));
        }
    }

    /// <summary>
    /// Hits whatever each Wight is walking at, if it is inside <see cref="MinionSpec.Reach"/> and
    /// its own interval has passed.
    /// </summary>
    private void Strike(EnemySystem enemies, PlayerCombat player, float now)
    {
        EnemyRegistry registry = enemies.Registry;

        float reachSquared = _spec.Reach * _spec.Reach;

        for (int i = 0; i < _count; i++)
        {
            MinionAgent agent = _agents[i];

            if (agent.QuarryId == 0 || now < agent.NextAttackAt)
            {
                continue;
            }

            if (!registry.TryGet(agent.QuarryId, out EnemyAgent quarry) || !quarry.IsAlive)
            {
                continue;
            }

            // Compared squared, so a strike never takes a square root. MinionSpec refuses a
            // non-positive or infinite reach at the door, which is what makes squaring it safe
            // (AR §18.3).
            if (SquaredXZ(quarry.Position, agent.Position) > reachSquared)
            {
                continue;
            }

            float amount = agent.ContactDamage.Value;

            // The one door a death comes through (AR §18.2) — so the *enemy's* death pays experience
            // and counts a kill exactly as it would have if the player had swung, and the Wight
            // itself is worth nothing (rule 11).
            enemies.ApplyDamage(agent.QuarryId, amount, now, player);

            agent.NextAttackAt = now + _spec.AttackInterval;

            // After the damage, so the EnemyDamaged — and the EnemyDied behind it — are on the wire
            // first and this says the extra thing that happened.
            _events.Publish(new MinionStruck(agent.Id, agent.QuarryId, amount));
        }
    }

    /// <summary>
    /// The id of the nearest living enemy to <paramref name="from"/>, or 0 when there is none.
    /// </summary>
    /// <remarks>
    /// Nearest by XZ (AR §18.4), compared squared so the choice never takes a square root it would
    /// discard. Corpses are skipped — <c>EnemyRegistry.Alive</c> holds them until
    /// <c>EnemySystem</c>'s sweep retires them. <b>Ties go to the earlier spawn</b>, because the
    /// comparison is a strict <c>&lt;</c> over a span the registry keeps in spawn order — the same
    /// rule, for the same reason, as <c>LureSystem.TryGetLure</c>'s: two identical frames must not
    /// disagree.
    /// </remarks>
    private static int Nearest(ReadOnlySpan<EnemyAgent> candidates, Vector3 from)
    {
        int best = 0;
        float bestSquared = float.PositiveInfinity;

        for (int i = 0; i < candidates.Length; i++)
        {
            EnemyAgent candidate = candidates[i];

            if (!candidate.IsAlive)
            {
                continue;
            }

            float squared = SquaredXZ(candidate.Position, from);

            if (squared < bestSquared)
            {
                best = candidate.Id;
                bestSquared = squared;
            }
        }

        return best;
    }

    /// <summary>Squared XZ separation. No square root, and no allocation (AR §18.4).</summary>
    private static float SquaredXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;

        return (dx * dx) + (dz * dz);
    }

    /// <remarks>
    /// <c>ProjectileSystem</c>'s answer rather than <c>SkillRunner</c>'s, and for its reason: a
    /// non-finite clock here is not a step that does nothing, it is an expiry that can never be
    /// compared against — every Wight immortal and silent.
    /// </remarks>
    private static void RequireFinite(float value, string paramName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be finite. It comes from the snapshot or from RunState.Time, "
                    + "which is a sum of clamped frame times, so a non-finite one is a mis-wired "
                    + "clock rather than a long session.");
        }
    }

    /// <remarks>
    /// A non-finite component spreads into every distance the choice and the strike compare, so a
    /// Wight raised at NaN is one that wins or loses every comparison by accident. Refused at the
    /// door, where the caller that invented the position is still on the stack.
    /// </remarks>
    private static void RequireFinite(Vector3 position)
    {
        if (IsFinite(position.X) && IsFinite(position.Y) && IsFinite(position.Z))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(position),
            position,
            "position must have finite components. A Wight standing at NaN is one every distance "
                + "test answers nonsense about.");
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>
    /// The most Wights that may stand right now: <see cref="Cap"/>, clamped into
    /// <c>[0, MaxConcurrent]</c>.
    /// </summary>
    /// <remarks>
    /// The ceiling is asked first so that an infinite <see cref="Cap"/> lands on it rather than
    /// overflowing the cast, and the floor is spelled <c>!(value &gt;= 1f)</c> so that NaN is
    /// refused with everything below one — a cap nobody can read stands nobody up (AR §18.3).
    /// </remarks>
    private int LiveCap()
    {
        float value = Cap.Value;

        if (value >= MaxConcurrent)
        {
            return MaxConcurrent;
        }

        if (!(value >= 1f))
        {
            return 0;
        }

        return (int)value;
    }

    /// <summary>Where <paramref name="id"/> is standing in the army, or <see langword="false"/>.</summary>
    private bool IndexOf(int id, out int index)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_agents[i].Id == id)
            {
                index = i;

                return true;
            }
        }

        index = -1;

        return false;
    }

    /// <summary>
    /// Takes the Wight at <paramref name="index"/> out of the army and puts its body back on the
    /// pool.
    /// </summary>
    /// <remarks>
    /// Shifted rather than swapped with the last, so the order the entries sit in stays the order
    /// they were raised in — <c>EnemyRegistry</c>'s determinism argument at a scale where the copy
    /// is seven references. The retired instance goes into the slot that falls off the end rather
    /// than being nulled, which is what makes the next <see cref="Spawn"/> free.
    /// </remarks>
    private void Remove(int index)
    {
        MinionAgent agent = _agents[index];

        for (int i = index; i < _count - 1; i++)
        {
            _agents[i] = _agents[i + 1];
        }

        _count--;

        _agents[_count] = agent;
    }
}

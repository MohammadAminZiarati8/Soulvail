using System;
using System.Numerics;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>
/// What makes a boss a boss: it watches its own health, crosses into a new phase at an authored
/// threshold, goes untouchable for a beat while the arena empties, calls in what the new phase
/// summons, and then hands the tick back to whatever actually fights. GD §9.1 rules 3 and 4;
/// M4-01b rules 2, 4 and 5.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is an <see cref="IEnemyBehaviour"/> and the boss is an <see cref="EnemyAgent"/>, which is
/// the whole architecture of a boss in this game</b> (rule 2). The Warden spawns through
/// <c>EnemySystem</c>, takes damage through <c>Health</c>, is chosen by the same <c>Targeter</c>,
/// is pooled by the same <c>EnemyRegistry</c> and pays experience through the same door — so every
/// later system (Elites, Ordeals, affixes) learns about one kind of enemy rather than two.
/// </para>
/// <para>
/// <b>It owns the phase machine and delegates the fighting</b> to <see cref="Inner"/>, which is
/// M4-02's <c>WardenBehaviour</c> and is <see langword="null"/> until then. A boss with no inner
/// behaviour stands where it was spawned, changes phase and summons — which is exactly what this
/// task ships and what M4-02's attacks hang on. The split is what makes
/// <em>"the boss does not act during the beat"</em> a single early return rather than a state every
/// attack has to check.
/// </para>
/// <para>
/// <b>The beat needs no edit to the damage path, and that is rule 2 paying for itself.</b>
/// <c>Health.SetExternalInvulnerable</c> has turned damage away with a
/// <c>DamageResult.Blocked</c> since M1, and <see cref="EnemyAgent.IsVulnerable"/> has kept the
/// target scorer off an enemy it cannot currently hurt since M1-13. A beat raises both and lowers
/// both; nothing in <c>EnemySystem.ApplyDamage</c> or <c>Health.ApplyDamage</c> changed.
/// <b>The two are separate on purpose:</b> one answers <em>"can this be hurt"</em> and the other
/// answers <em>"should auto-aim point at it"</em>, and a boss mid-beat wants both false while a
/// future shielded-from-the-front Warden (GD §8.1) will want them to disagree.
/// </para>
/// <para>
/// <b>Health is read from <c>Health.Fraction</c> and never from the blackboard.</b>
/// <c>EnemySystem.Perceive</c> skips the dead, so a corpse's <c>EnemyBlackboard.HpFraction</c> is
/// frozen at its last living reading (M4-01a's <c>Blackboard_ACorpseKeepsItsLastReading</c>) — a
/// phase machine driven from it would read a dead boss as healthy. It also reads this tick's health
/// rather than this tick's <em>perception</em>, which is what makes rule 9 true: combat runs above
/// the behaviour step, so a hit that crosses 33 % opens the beat on the frame it landed.
/// </para>
/// <para>
/// <b>Nothing on the per-tick path allocates.</b> The add table is sized from the spec once, at
/// construction; a tick with no crossing is a float compare, a subtraction and one intent.
/// </para>
/// </remarks>
public sealed class BossBehaviour : IEnemyBehaviour
{
    /// <summary>Metres from the boss that a summoned add appears.</summary>
    /// <remarks>
    /// <b>A number no design document gives, and flagged as invented rather than derived.</b> GD
    /// §9.1 rule 4 says a boss summons adds and does not say where they stand. Far enough out that
    /// they are not inside the boss's body and near enough that they read as <em>its</em> help
    /// rather than as a wave; the arena's own geometry is M4-03's, and a ring that can overlap a
    /// pillar is a cost this task accepts rather than one it solves with a spatial query core does
    /// not have.
    /// </remarks>
    public const float AddRingRadius = 3f;

    private readonly EnemyAgent _agent;

    private readonly BossPhases _phases;

    /// <summary>
    /// The ids of the adds this boss has standing, so the beat can clear exactly what it called in
    /// and nothing else.
    /// </summary>
    /// <remarks>
    /// Sized once from <see cref="BossSpec.MaxSummonedBodies"/>, which is the most any single phase
    /// asks for — and a phase's summons are cleared before the next phase's are called in, so the
    /// table can never be asked to hold two phases' worth. Tracked by id rather than by reference
    /// because an agent is recycled: a reference held across a despawn is a handle on whatever the
    /// pool handed out next, and an id is stale rather than misleading (<c>EnemyRegistry</c> rule 1).
    /// </remarks>
    private readonly int[] _addIds;

    private int _addCount;

    /// <param name="agent">The body this drives. Its <c>Health</c> is what the phases are read off.</param>
    /// <param name="spec">The boss it is an instance of.</param>
    /// <param name="inner">
    /// What actually fights, or <see langword="null"/> for a boss that only changes phase and
    /// summons. Null until M4-02 authors the Warden's attacks, which is deliberate: a placeholder
    /// that made the boss chase would be a content decision taken by the task that does not own the
    /// content (GD §9.2's hook is M4-02's to build).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> or <paramref name="spec"/> is null.</exception>
    public BossBehaviour(EnemyAgent agent, BossSpec spec, IEnemyBehaviour inner = null)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));

        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        _phases = new BossPhases(spec);
        _addIds = new int[spec.MaxSummonedBodies];

        Inner = inner;
    }

    /// <summary>The boss this is an instance of.</summary>
    public BossSpec Spec => _phases.Spec;

    /// <summary>What actually fights, or null while nothing does.</summary>
    public IEnemyBehaviour Inner { get; }

    /// <summary>Which phase the fight is in, numbered from 0.</summary>
    public int Phase => _phases.Current;

    /// <summary>How many phases the fight has.</summary>
    public int PhaseCount => _phases.PhaseCount;

    /// <summary>The boss is inside a phase change's beat: untouchable, and not acting.</summary>
    public bool IsInBeat => _phases.IsInBeat;

    /// <summary>How many summoned adds this boss still has standing.</summary>
    public int AddCount => _addCount;

    /// <summary>
    /// One tick of the fight: run the phases, and then either sit out the beat or let the inner
    /// behaviour act.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order inside the tick is the contract</b> (rule 9). The phase check is first, so a hit
    /// resolved earlier this frame — <c>PlayerCombat.Tick</c> runs above the behaviour step — opens
    /// its beat on the frame it landed rather than one later. Then the clear, then the summon, then
    /// the announcement; a summon before the clear would despawn the bodies it had just called in.
    /// </para>
    /// <para>
    /// <b>An <c>EnemyMoveIntent</c> goes out on every path through this method</b>, the beat
    /// included, because <c>EnemyView</c> folds gravity into the same <c>Move</c> that carries the
    /// walk — a tick with no intent is a tick this body is not pinned to the floor by
    /// (<see cref="IEnemyBehaviour.Tick"/>). During a beat it is a zero velocity and a zero facing,
    /// which the body reads as <em>"stand still and keep looking where you are looking"</em>.
    /// </para>
    /// </remarks>
    public void Tick(in EnemyTickContext ctx)
    {
        bool wasInBeat = _phases.IsInBeat;

        // Health.Fraction, never Blackboard.HpFraction — see the class remarks, and M4-01a's
        // Blackboard_ACorpseKeepsItsLastReading.
        bool entered = _phases.Tick(_agent.Health.Fraction, ctx.Dt);

        if (entered)
        {
            EnterPhase(ctx);

            Stand(ctx);

            return;
        }

        if (_phases.IsInBeat)
        {
            Stand(ctx);

            return;
        }

        if (wasInBeat)
        {
            SetBeat(false);

            ctx.Events.Publish(new BossBeatEnded(_agent.Id));
        }

        if (Inner is null)
        {
            Stand(ctx);

            return;
        }

        Inner.Tick(ctx);
    }

    /// <summary>
    /// Back to the opening phase, no beat, no adds remembered, and hurtable again.
    /// </summary>
    /// <remarks>
    /// <b>It forgets the adds rather than despawning them</b>, and the asymmetry with the beat's
    /// clear is deliberate: this runs when an agent is recycled into a new life, at which point the
    /// arena those bodies stood in has already been emptied by <c>EnemySystem.Clear</c>, and the
    /// ids would resolve either to nothing or — worse — to whatever the pool has handed out since.
    /// </remarks>
    public void Reset()
    {
        _phases.Reset();

        _addCount = 0;

        SetBeat(false);

        Inner?.Reset();
    }

    /// <summary>
    /// Announces the phase the boss is in right now, without changing anything.
    /// </summary>
    /// <remarks>
    /// <b>Called once by <c>EnemySystem.SpawnBoss</c>, for phase 0.</b> A fight announces the phase
    /// it starts in as well as the ones it crosses into, so a segmented bar (M4-04) is told how many
    /// segments it has at the moment the body appears rather than at 66 % — which is what makes rule
    /// 7's refusal to put an <c>IsBoss</c> flag on <c>EnemySpawned</c> pay for itself.
    /// </remarks>
    public void Announce(IDomainEvents events)
    {
        if (events is null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        events.Publish(new BossPhaseChanged(_agent.Id, _phases.Current, _phases.PhaseCount));
    }

    /// <summary>
    /// The whole of a phase change: clear what the last phase called in, raise the beat, call in
    /// what this one does, and say so.
    /// </summary>
    /// <remarks>
    /// <b>The adds go on the beat's first tick, not at its end</b> (rule 4, GD §9.1 rule 3). The
    /// point of the beat is to reset pressure <em>now</em>: a player who has just been told the
    /// fight is changing should see the arena empty immediately, rather than spend a second and a
    /// half fighting Husks around a boss they cannot hurt.
    /// </remarks>
    private void EnterPhase(in EnemyTickContext ctx)
    {
        ClearAdds(ctx);

        SetBeat(true);

        Summon(ctx);

        // The phase first and the beat second, so a handler that sizes itself from the phase count
        // has done so before it is told how long it has to animate the change.
        ctx.Events.Publish(new BossPhaseChanged(_agent.Id, _phases.Current, _phases.PhaseCount));
        ctx.Events.Publish(new BossBeatStarted(_agent.Id, Spec.BeatSeconds));
    }

    /// <summary>Retires every add this boss still has standing.</summary>
    /// <remarks>
    /// <b>Through <c>EnemySystem.DespawnAtEndOfTick</c> and never through <c>Despawn</c>.</b> This
    /// runs from inside the behaviour pass, which walks a span over the registry's own array, and
    /// <c>EnemyRegistry.Despawn</c> compacts that array in place — so an immediate despawn here
    /// would shift the agents the pass has not reached yet and walk it off the end. That is the
    /// hazard <c>EnemySystem.Tick</c>'s remarks have named since M2-08, and this is the first
    /// behaviour to reach it.
    /// </remarks>
    private void ClearAdds(in EnemyTickContext ctx)
    {
        for (int i = 0; i < _addCount; i++)
        {
            ctx.Enemies.DespawnAtEndOfTick(_addIds[i]);
        }

        _addCount = 0;
    }

    /// <summary>
    /// Calls in the current phase's summons, as far as the arena has room for them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Through <c>EnemySystem.Spawn</c>, so a summoned Husk is a Husk in every way that
    /// matters</b> (rule 5): scaled to the stage's depth by the one place that applies depth,
    /// announced by <c>EnemySpawned</c>, targeted by the same scorer, and worth its authored
    /// experience when the player kills it.
    /// </para>
    /// <para>
    /// <b>A summon that would not fit is dropped, not queued</b> (rule 5). A boss that banked
    /// summons and released twenty at once on a cheap phone is a frame-rate bug wearing a design
    /// hat, and the ceiling is the registry's own <c>Capacity</c> — the device cap
    /// <c>RunSession</c> built it at (GD §11.1), which is the only one a behaviour can see. The
    /// stage's authored concurrency is the director's and does not apply here: a boss stage has no
    /// waves to pace.
    /// </para>
    /// <para>
    /// <b>Positions are derived, never drawn</b> (ADR-0011's spirit): a ring around the boss, evenly
    /// spaced, so summoning consumes no randomness at all and a seeded run replays a boss fight
    /// identically whatever else has drawn from the <c>Spawn</c> stream.
    /// </para>
    /// </remarks>
    private void Summon(in EnemyTickContext ctx)
    {
        BossPhaseSpec phase = _phases.CurrentPhase;

        int wanted = phase.SummonedBodyCount;

        if (wanted == 0)
        {
            return;
        }

        EnemyRegistry registry = ctx.Enemies.Registry;

        int room = registry.Capacity - registry.AliveCount;

        if (room <= 0)
        {
            return;
        }

        Vector3 centre = _agent.Position;

        int placed = 0;

        for (int i = 0; i < phase.Summons.Count && placed < room; i++)
        {
            AddWave wave = phase.Summons[i];

            for (int n = 0; n < wave.Count && placed < room; n++)
            {
                float angle = placed * (2f * MathF.PI / wanted);

                var at = new Vector3(
                    centre.X + (MathF.Cos(angle) * AddRingRadius),
                    centre.Y,
                    centre.Z + (MathF.Sin(angle) * AddRingRadius));

                EnemyAgent add = ctx.Enemies.Spawn(wave.SpecId, at);

                _addIds[placed] = add.Id;

                placed++;
            }
        }

        _addCount = placed;
    }

    /// <summary>Raises or lowers both halves of being untouchable.</summary>
    /// <remarks>
    /// Two flags because they answer two questions — see the class remarks. Neither is an edit to
    /// the damage path: <c>Health.ApplyDamage</c> has turned an invulnerable target away with a
    /// <c>DamageResult.Blocked</c> since M1-10, and <c>TargetScorer</c> has skipped an enemy it
    /// cannot hurt since M1-13.
    /// </remarks>
    private void SetBeat(bool inBeat)
    {
        _agent.IsVulnerable = !inBeat;

        _agent.Health.SetExternalInvulnerable(inBeat);
    }

    /// <summary>Stand still and keep the facing the body has — the intent every tick owes.</summary>
    private void Stand(in EnemyTickContext ctx) =>
        ctx.Intents.EnemyMove(new EnemyMoveIntent(_agent.Id, Vector3.Zero, Vector2.Zero));
}

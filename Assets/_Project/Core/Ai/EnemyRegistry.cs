using System;
using System.Collections.Generic;
using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Ai;

/// <summary>
/// Every enemy currently in the run, and the only thing that creates or retires one. The director
/// spawns through it, targeting gathers candidates from it, and the AI iterates it — so there is
/// one place to look for "what is out there". See AR §9 and ADR-0005.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spawn order, for ever.</b> <see cref="Alive"/> is in the order things were spawned, and a
/// despawn compacts rather than swapping with the last. A swap would be O(1) instead of O(n), and
/// it would also make the iteration order depend on the *history* of deaths — which feeds
/// <c>TargetScorer</c>'s tie-break, which feeds what the gun shoots, which means two runs from the
/// same seed could diverge on the strength of who died first. At the 64-enemy cap of GD §11 the
/// shift is a memory move of at most a few hundred bytes; determinism is worth more.
/// </para>
/// <para>
/// <b>Nothing allocates after the first <see cref="Capacity"/> spawns.</b> Agents are pooled: a
/// despawned one goes on a free list and comes back with a new id. Three preallocated arrays and a
/// pre-sized dictionary, none of which ever grows — see <see cref="Spawn"/>. This is a per-frame
/// path at the scale of a wave, and AR §4.3 says such paths allocate nothing.
/// </para>
/// <para>
/// <b>It publishes nothing and knows no time.</b> Death is <c>EnemySystem</c>'s to notice and
/// announce (M1-11): it reads the <c>DamageResult</c>, publishes <c>EnemyDied</c>, and only then
/// calls <see cref="Despawn"/>. A corpse therefore sits in <see cref="Alive"/> for the rest of its
/// tick, which is deliberate — the view needs one more frame to start its dissolve, and the event
/// must be out before the id it names becomes unresolvable.
/// </para>
/// </remarks>
public sealed class EnemyRegistry
{
    /// <summary>
    /// The first id of a run. One rather than zero, so that zero is never a valid enemy — a
    /// default-initialised id field reads as "nobody" rather than as the first Husk.
    /// </summary>
    private const int FirstId = 1;

    /// <summary>
    /// The registered agents, in spawn order, in <c>[0, _aliveCount)</c>. Anything at or past the
    /// count is a stale reference the registry has already nulled.
    /// </summary>
    private readonly EnemyAgent[] _registered;

    /// <summary>
    /// Despawned agents waiting to be handed out again, in <c>[0, _freeCount)</c>. Used as a stack,
    /// so a burst of spawns reuses the most recently retired agents and leaves the rest cold.
    /// </summary>
    private readonly EnemyAgent[] _free;

    /// <summary>
    /// Id to agent, for <see cref="TryGet"/>. Pre-sized to <see cref="Capacity"/> and never larger
    /// than that many entries, so its internal free list absorbs every insert after a remove and it
    /// never resizes — which is what keeps rule 8 true through a whole run of monotonic ids.
    /// </summary>
    private readonly Dictionary<int, EnemyAgent> _byId;

    private int _aliveCount;
    private int _freeCount;
    private int _nextId = FirstId;

    /// <param name="capacity">
    /// The most enemies that may exist at once. Matches the snapshot's enemy capacity, because an
    /// enemy core knows about but the snapshot cannot carry is one core is blind to the position of.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="capacity"/> is not positive. A registry that can hold no enemies is a
    /// configuration mistake rather than a valid state to run with — the same guard, for the same
    /// reason, as <c>WorldSnapshot</c>'s.
    /// </exception>
    public EnemyRegistry(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "capacity must be greater than zero.");
        }

        _registered = new EnemyAgent[capacity];
        _free = new EnemyAgent[capacity];
        _byId = new Dictionary<int, EnemyAgent>(capacity);
    }

    /// <summary>
    /// How many agents are registered — which is not the same as how many are breathing. A dead
    /// agent counts here until <c>EnemySystem</c> despawns it; ask <see cref="EnemyAgent.IsAlive"/>
    /// for the other question.
    /// </summary>
    public int AliveCount => _aliveCount;

    /// <summary>The most agents that may be registered at once.</summary>
    public int Capacity => _registered.Length;

    /// <summary>
    /// Every registered agent, in spawn order. Borrowed — valid until the next
    /// <see cref="Spawn"/>, <see cref="Despawn"/> or <see cref="Clear"/>, and never to be
    /// retained.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A span over a preallocated array rather than a list, because a <c>ReadOnlySpan</c> over a
    /// <c>List&lt;T&gt;</c> needs <c>CollectionsMarshal.AsSpan</c>, which is not in Unity's
    /// netstandard 2.1 profile — leaving only <c>ToArray()</c>, an allocation every frame. Same
    /// shape as <c>WorldSnapshot.Enemies</c> plus <c>EnemyCount</c>, and it carries the same
    /// hazard in the same way: everything past <see cref="AliveCount"/> is not yours to read, and
    /// the span already stops there so you cannot.
    /// </para>
    /// <para>
    /// It includes the dead. That is rule 4 and it is on purpose — see the class remarks — so a
    /// reader that cares (a spawn policy counting live threats, a behaviour tick) checks
    /// <see cref="EnemyAgent.IsAlive"/>, and a reader that does not (the view sync) does not have
    /// to.
    /// </para>
    /// </remarks>
    public ReadOnlySpan<EnemyAgent> Alive => new ReadOnlySpan<EnemyAgent>(_registered, 0, _aliveCount);

    /// <summary>
    /// Registers a new enemy of <paramref name="spec"/> at <paramref name="position"/> and returns
    /// it, with a fresh id, full health and a blank blackboard.
    /// </summary>
    /// <remarks>
    /// Recycles a despawned agent when one is available, so only the first <see cref="Capacity"/>
    /// spawns of a run construct anything. <b>A recycled agent is re-initialised completely</b> —
    /// it may come back as an entirely different archetype, and as of M2-03 that includes its
    /// three stats: <c>EnemyAgent.Initialise</c> wipes every modifier, whoever added it, before
    /// re-basing them, so a Husk that died at stage 40 does not come back wearing that depth's
    /// scaling (ledger row 2). There is no exception left to name.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The registry is full. Deliberately loud, like <c>WorldSnapshot.AddEnemy</c>: silently
    /// dropping the enemy would make the director believe it spawned a wave it did not, and
    /// silently growing would allocate mid-frame. The caller checks <see cref="AliveCount"/>
    /// against <see cref="Capacity"/> first — the concurrency cap is a design decision (GD §11),
    /// so respecting it is choosing which enemies matter rather than hoping.
    /// </exception>
    public EnemyAgent Spawn(EnemySpec spec, Vector3 position)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        if (_aliveCount >= _registered.Length)
        {
            throw new InvalidOperationException(
                $"EnemyRegistry is full at {_registered.Length} enemies. The caller must check "
                    + "AliveCount against Capacity before spawning.");
        }

        // Claimed before anything can fail, and never handed back: rule 1's ids increase for the
        // life of the run, so an id that was spent on a Husk which has since died is not offered to
        // the next one. An event or a view still holding it resolves to nothing, which is the truth,
        // rather than to a stranger.
        int id = _nextId;
        _nextId++;

        EnemyAgent agent;

        if (_freeCount > 0)
        {
            _freeCount--;
            agent = _free[_freeCount];

            // Cleared so the free list holds no reference past the slot it is using — otherwise a
            // despawned archetype's spec stays reachable for as long as the registry lives.
            _free[_freeCount] = null;

            agent.Initialise(id, spec, position);
        }
        else
        {
            agent = new EnemyAgent(id, spec, position);
        }

        _registered[_aliveCount] = agent;
        _aliveCount++;
        _byId.Add(id, agent);

        return agent;
    }

    /// <summary>
    /// Retires the agent with <paramref name="id"/>: out of <see cref="Alive"/>, out of
    /// <see cref="TryGet"/>, onto the free list.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> for an id that is not registered — already despawned, or never
    /// spawned. Not an error: a caller cleaning up after a wave can despawn unconditionally, and
    /// M1-11's death flow can be called twice for the same corpse without having to remember.
    /// </returns>
    public bool Despawn(int id)
    {
        if (!_byId.TryGetValue(id, out EnemyAgent agent))
        {
            return false;
        }

        _byId.Remove(id);

        int index = IndexOf(agent);

        if (index < 0)
        {
            // Unreachable: the dictionary and the array are written and cleared together in this
            // one type. Loud rather than absent, for the reason Stat.Pool's default branch is —
            // silently returning false here would report "no such enemy" for one that is still in
            // Alive, and the corpse would be iterated for the rest of the run.
            throw new InvalidOperationException(
                $"EnemyRegistry invariant broken: enemy {id} is indexed but not registered.");
        }

        // Order-preserving removal — the shift of rule 3, not a swap with the last. See the class
        // remarks for why determinism buys the O(n).
        int remaining = _aliveCount - index - 1;

        if (remaining > 0)
        {
            Array.Copy(_registered, index + 1, _registered, index, remaining);
        }

        _aliveCount--;
        _registered[_aliveCount] = null;

        _free[_freeCount] = agent;
        _freeCount++;

        return true;
    }

    /// <summary>Finds a registered agent by id.</summary>
    /// <remarks>
    /// O(1), because this is how every event, intent and view resolves an id back to an enemy and
    /// there are a lot of them per frame. Registered, not living — a corpse is still findable until
    /// it is despawned, which is what lets M1-11 publish a death that names it.
    /// </remarks>
    /// <returns><see langword="false"/> and a null <paramref name="agent"/> for an unknown id.</returns>
    public bool TryGet(int id, out EnemyAgent agent) => _byId.TryGetValue(id, out agent);

    /// <summary>
    /// Empties the registry and starts ids again from one.
    /// </summary>
    /// <remarks>
    /// <b>Between runs only.</b> Resetting the id counter is safe exactly when nothing outside is
    /// still holding an id — a new run, a fresh registry's worth of state. Called mid-run it would
    /// make rule 1's promise false: an event already published, or a view mid-dissolve, would find
    /// its id resolving to a different enemy. Every agent goes back on the free list, so a cleared
    /// registry still allocates nothing on its next wave.
    /// </remarks>
    public void Clear()
    {
        // Alive plus free never exceeds Capacity — an agent exists only because a Spawn that passed
        // the capacity check created it — so the free list cannot overflow here.
        for (int i = 0; i < _aliveCount; i++)
        {
            _free[_freeCount] = _registered[i];
            _freeCount++;
            _registered[i] = null;
        }

        _aliveCount = 0;
        _byId.Clear();
        _nextId = FirstId;
    }

    /// <summary>
    /// Position of <paramref name="agent"/> in <see cref="_registered"/>, or −1.
    /// </summary>
    /// <remarks>
    /// By reference rather than by id: the caller already has the object from the dictionary, so
    /// this compares handles instead of re-reading a field, and it cannot be fooled by an agent
    /// whose id was reassigned mid-call.
    /// </remarks>
    private int IndexOf(EnemyAgent agent)
    {
        for (int i = 0; i < _aliveCount; i++)
        {
            if (ReferenceEquals(_registered[i], agent))
            {
                return i;
            }
        }

        return -1;
    }
}

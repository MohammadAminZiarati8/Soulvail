using System;
using System.Collections.Generic;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// The <see cref="IIntentSink"/> core tests write into: it keeps every intent instead of handing
/// the latest one to a view, so a test can assert both what core asked for and how many times it
/// asked.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately unlike <c>IntentBuffer</c>, the real adapter, which keeps only the newest intent
/// per kind because a body can only do one thing at a time. Here the history is the point: "one
/// <see cref="PlayerMoveIntent"/> per tick, never two and never none" is a rule about the
/// sequence, and a buffer that overwrites cannot tell those apart.
/// </para>
/// <para>
/// <see cref="IIntentSink.PlayerMove"/> is implemented explicitly, so writing goes through the
/// port and reading goes through this class — the same one-way shape the real buffer enforces,
/// for the same reason. A test cannot accidentally record an intent core never produced.
/// </para>
/// </remarks>
public sealed class RecordingIntents : IIntentSink
{
    private readonly List<PlayerMoveIntent> _playerMoves = new();
    private readonly List<ConeHitIntent> _coneHits = new();
    private readonly List<ChargeIntent> _charges = new();
    private readonly List<EnemyKnockbackIntent> _knockbacks = new();
    private readonly List<EnemyMoveIntent> _enemyMoves = new();

    /// <summary>Every player-move intent written, in the order core produced them.</summary>
    /// <remarks>
    /// Exposed as a read-only view of the list rather than the list itself: a test that can
    /// <c>Add</c> to the record can fake a tick that never happened, and the assertion that then
    /// passes is worthless. Same instinct as <c>ContentCatalog.Characters</c>.
    /// </remarks>
    public IReadOnlyList<PlayerMoveIntent> PlayerMoves => _playerMoves;

    /// <summary>
    /// The most recent player-move intent — usually what a test means by "what core decided".
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing has been written. Louder than returning a default intent, which would read as a
    /// stationary character and quietly pass a test whose system never emitted anything.
    /// </exception>
    public PlayerMoveIntent LastPlayerMove
    {
        get
        {
            if (_playerMoves.Count == 0)
            {
                throw new InvalidOperationException(
                    "No PlayerMoveIntent has been written to this sink.");
            }

            return _playerMoves[_playerMoves.Count - 1];
        }
    }

    /// <summary>Every cone request written, in the order core produced them.</summary>
    /// <remarks>
    /// The history matters more here than it does for movement. "One cone per damage frame, and
    /// none on the ticks between" is a rule about a sequence three times a second inside a stream
    /// of sixty, and the request ids are only monotonic if you can see them in order.
    /// </remarks>
    public IReadOnlyList<ConeHitIntent> ConeHits => _coneHits;

    /// <summary>The most recent cone request.</summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing has been written. Louder than a default intent, which would read as a zero-range
    /// cone at the origin and quietly pass a test whose weapon never swung.
    /// </exception>
    public ConeHitIntent LastConeHit
    {
        get
        {
            if (_coneHits.Count == 0)
            {
                throw new InvalidOperationException(
                    "No ConeHitIntent has been written to this sink.");
            }

            return _coneHits[_coneHits.Count - 1];
        }
    }

    /// <summary>Every dash written, in the order core started them.</summary>
    /// <remarks>
    /// The count is most of what a test wants from this one. "One Charge intent per press, and none
    /// on any of the ticks the dash is still in flight" is the rule that stops a body dashing every
    /// frame for 0.22 s, and only a history can tell that apart from a dash that fired once.
    /// </remarks>
    public IReadOnlyList<ChargeIntent> Charges => _charges;

    /// <summary>The most recent dash.</summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing has been written. Louder than a default intent, which would read as a dash of no
    /// length in no direction and quietly pass a test whose skill never fired.
    /// </exception>
    public ChargeIntent LastCharge
    {
        get
        {
            if (_charges.Count == 0)
            {
                throw new InvalidOperationException(
                    "No ChargeIntent has been written to this sink.");
            }

            return _charges[_charges.Count - 1];
        }
    }

    /// <summary>Every knockback written, in the order core decided them.</summary>
    /// <remarks>
    /// The one record here where the order carries meaning beyond counting: a dash reports the
    /// enemies it passed through in the order the body found them, and each is knocked back exactly
    /// once per Charge, so this list read end to end is the dedupe rule made visible.
    /// </remarks>
    public IReadOnlyList<EnemyKnockbackIntent> Knockbacks => _knockbacks;

    /// <summary>Every enemy walk written, in the order core decided them.</summary>
    /// <remarks>
    /// The busiest record here by far — one per living chaser per tick — which is what makes
    /// <see cref="Clear"/> the usual first line of a chaser assertion: "what did it decide *this*
    /// tick" is otherwise buried under everything it decided before.
    /// </remarks>
    public IReadOnlyList<EnemyMoveIntent> EnemyMoves => _enemyMoves;

    /// <summary>The most recent enemy walk.</summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing has been written. Louder than a default intent, which would read as enemy 0 standing
    /// still and quietly pass a test whose behaviour never ran.
    /// </exception>
    public EnemyMoveIntent LastEnemyMove
    {
        get
        {
            if (_enemyMoves.Count == 0)
            {
                throw new InvalidOperationException(
                    "No EnemyMoveIntent has been written to this sink.");
            }

            return _enemyMoves[_enemyMoves.Count - 1];
        }
    }

    /// <summary>How many walks were written for <paramref name="enemyId"/>.</summary>
    /// <remarks>
    /// The question "did this enemy act at all" — which is what a dead agent's row asks — cannot be
    /// answered by the count alone once a fixture holds more than one enemy.
    /// </remarks>
    public int CountEnemyMoves(int enemyId)
    {
        int count = 0;

        for (int i = 0; i < _enemyMoves.Count; i++)
        {
            if (_enemyMoves[i].Id == enemyId)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Forgets everything recorded so far, so a test can assert on one phase at a time.</summary>
    /// <remarks>
    /// The lists keep their capacity, which is what makes this usable inside an allocation test:
    /// grow them past the measured window first, clear them, and the adds that follow cannot be the
    /// thing that allocates.
    /// </remarks>
    public void Clear()
    {
        _playerMoves.Clear();
        _coneHits.Clear();
        _charges.Clear();
        _knockbacks.Clear();
        _enemyMoves.Clear();
    }

    void IIntentSink.PlayerMove(in PlayerMoveIntent intent)
    {
        _playerMoves.Add(intent);
    }

    void IIntentSink.ConeHit(in ConeHitIntent intent)
    {
        _coneHits.Add(intent);
    }

    void IIntentSink.Charge(in ChargeIntent intent)
    {
        _charges.Add(intent);
    }

    void IIntentSink.EnemyMove(in EnemyMoveIntent intent)
    {
        _enemyMoves.Add(intent);
    }

    void IIntentSink.EnemyKnockback(in EnemyKnockbackIntent intent)
    {
        _knockbacks.Add(intent);
    }
}

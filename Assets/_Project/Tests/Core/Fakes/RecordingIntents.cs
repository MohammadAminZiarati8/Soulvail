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
    }

    void IIntentSink.PlayerMove(in PlayerMoveIntent intent)
    {
        _playerMoves.Add(intent);
    }

    void IIntentSink.ConeHit(in ConeHitIntent intent)
    {
        _coneHits.Add(intent);
    }
}

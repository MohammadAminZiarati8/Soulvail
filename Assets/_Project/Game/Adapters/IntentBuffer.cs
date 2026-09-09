using System.Collections.Generic;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The Unity side of <see cref="IIntentSink"/>: a per-frame mailbox core fills during
/// <c>Tick</c> and the views empty straight after. One instance per run, registered in
/// <c>RunScope</c> (AR §7).
/// </summary>
/// <remarks>
/// <para>
/// The intent lives in a field, so a frame's write-then-read costs nothing and there is
/// nothing to preallocate beyond the buffer itself. M0-16's <c>RunTicker</c> owns the order:
/// <see cref="Clear"/>, then <c>session.Tick</c>, then the views read.
/// </para>
/// <para>
/// <see cref="Clear"/> lowers <see cref="HasPlayerMove"/> and deliberately leaves the stored
/// intent alone — the same bargain <c>WorldSnapshot.Clear</c> makes with its enemy array. The
/// value is undefined while the flag is down and every reader checks the flag first; zeroing
/// it would trade one wrong answer for another while suggesting the check were optional.
/// </para>
/// <para>
/// The writing side is an explicit implementation so that <see cref="PlayerMove"/> can be the
/// property the views read. The shared name is intentional and legal — an explicit member's
/// name is <c>IIntentSink.PlayerMove</c>, which never enters this class's declaration space —
/// and it makes the type enforce the direction of the boundary: core, holding an
/// <see cref="IIntentSink"/>, can only write; a view, holding an <see cref="IntentBuffer"/>,
/// can only read.
/// </para>
/// </remarks>
public sealed class IntentBuffer : IIntentSink
{
    /// <summary>
    /// Room for more cone requests than a tick can plausibly produce — one basic attack and a
    /// Charge sweep is two (M1-15). Preallocated so the list stops growing after the first frame
    /// and the per-frame write allocates nothing.
    /// </summary>
    private const int ConeHitCapacity = 4;

    /// <summary>
    /// Room for a dash through a crowd without the list growing. A Charge sweeps a 10 m line and
    /// knocks back everything it touches (CC §5), which against GD §8.1's clustering is a handful
    /// rather than an arena; more than this is legal and costs one resize, once, for the life of
    /// the buffer.
    /// </summary>
    private const int KnockbackCapacity = 16;

    private readonly List<ConeHitIntent> _coneHits = new(ConeHitCapacity);
    private readonly List<EnemyKnockbackIntent> _knockbacks = new(KnockbackCapacity);

    private PlayerMoveIntent _playerMove;
    private ChargeIntent _charge;

    /// <summary>Whether core wrote a player move intent during this tick.</summary>
    /// <remarks>
    /// Down for every tick of a dash, which is not a gap: core suspends the motor while a
    /// <see cref="Charge"/> is in flight precisely so that the body has one instruction about where
    /// the player goes rather than two. See <c>RunSession.TickBody</c>.
    /// </remarks>
    public bool HasPlayerMove { get; private set; }

    /// <summary>Whether core started a dash during this tick.</summary>
    public bool HasCharge { get; private set; }

    /// <summary>
    /// The move intent core wrote this tick. Undefined while <see cref="HasPlayerMove"/> is
    /// <c>false</c>: it is the previous tick's leftovers, not a zero. Callers check the flag.
    /// </summary>
    public PlayerMoveIntent PlayerMove => _playerMove;

    /// <summary>
    /// Every cone core asked to have resolved this tick, in the order it asked. Empty on most
    /// ticks — the Censer swings three times a second, not sixty.
    /// </summary>
    /// <remarks>
    /// Read it with a <c>for</c> over <see cref="IReadOnlyCollection{T}.Count"/>. A
    /// <c>foreach</c> over the interface boxes an enumerator, and this is read every frame on a
    /// phone; the same reason nothing in core allocates on a tick path.
    /// </remarks>
    public IReadOnlyList<ConeHitIntent> ConeHits => _coneHits;

    /// <summary>
    /// The dash core started this tick. Undefined while <see cref="HasCharge"/> is <c>false</c>,
    /// for the reason <see cref="PlayerMove"/> is: it is the previous dash's leftovers, not a zero.
    /// Callers check the flag.
    /// </summary>
    /// <remarks>
    /// Set on one tick in every two and a half seconds at the very best, which is what makes
    /// re-reading it a bug rather than a rounding error: a body that applied this without checking
    /// would dash again every frame for the rest of the run.
    /// </remarks>
    public ChargeIntent Charge => _charge;

    /// <summary>
    /// Every enemy core wants shoved, in the order it decided. Empty on almost every frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Filled from a <em>fact</em> rather than from the tick, and that is the one thing about this
    /// list that is unlike the rest of the buffer: <c>ReportChargeHits</c> is answered after
    /// <c>session.Tick</c> has returned and after the player has been moved, so these arrive later
    /// in the frame than every other intent here. A reader has to run after the fact phase —
    /// <c>RunTicker</c> owns that order — or it will spend the frame looking at an empty list and
    /// find the entries cleared before it next looks.
    /// </para>
    /// <para>
    /// Read it with a <c>for</c> over <see cref="IReadOnlyCollection{T}.Count"/>, for the reason
    /// <see cref="ConeHits"/> gives.
    /// </para>
    /// </remarks>
    public IReadOnlyList<EnemyKnockbackIntent> Knockbacks => _knockbacks;

    void IIntentSink.PlayerMove(in PlayerMoveIntent intent)
    {
        _playerMove = intent;
        HasPlayerMove = true;
    }

    void IIntentSink.ConeHit(in ConeHitIntent intent)
    {
        _coneHits.Add(intent);
    }

    void IIntentSink.Charge(in ChargeIntent intent)
    {
        _charge = intent;
        HasCharge = true;
    }

    void IIntentSink.EnemyKnockback(in EnemyKnockbackIntent intent)
    {
        _knockbacks.Add(intent);
    }

    /// <summary>
    /// Drops every intent, ready for the next tick to refill. Runs once per frame
    /// <em>before</em> <c>session.Tick</c>, never after — clearing afterwards would erase the
    /// intents the views have not read yet.
    /// </summary>
    /// <remarks>
    /// The two lists are genuinely emptied, where the two flags are only lowered, and the asymmetry
    /// is the two shapes rather than an inconsistency: a list has no flag to check first, so
    /// leftovers would read as this tick's swings and knockbacks — the body would sweep the same
    /// cone every frame until the next one replaced it, and shove the same enemy for ever. Emptying
    /// keeps the capacity, so it allocates nothing.
    /// </remarks>
    public void Clear()
    {
        HasPlayerMove = false;
        HasCharge = false;

        _coneHits.Clear();
        _knockbacks.Clear();
    }
}

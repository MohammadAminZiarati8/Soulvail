namespace Soulvail.Core.Ports;

/// <summary>
/// The outbound port core publishes through: "this happened." Core never learns who listens.
/// </summary>
/// <remarks>
/// Outbound only. Anything the outside needs to tell core goes the other way, as a method call
/// on an inbound port — a two-way bus makes flow untraceable. Events describe what happened,
/// never what to do: <c>PlayerDamaged</c>, never <c>ShakeCamera</c>. Payloads are
/// <c>readonly struct</c>s taken by <c>in</c>, so reaching the port costs no copy and no box.
/// See <see href="../../../../Docs/adr/0004-scoped-domain-events.md">ADR-0004</see>.
/// </remarks>
public interface IDomainEvents
{
    /// <summary>
    /// Delivers <paramref name="evt"/> to every subscriber of <typeparamref name="T"/>, each of
    /// which receives its own copy. Publishing an event nobody listens for is a no-op.
    /// </summary>
    void Publish<T>(in T evt) where T : struct;
}

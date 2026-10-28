using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// After every <em>n</em> shots the next one fires at every eligible enemy, each dealing more.
/// Owns the live numbers and the counter; PlayerCombat chooses the targets (RS-08b).
/// </summary>
/// <remarks>
/// <para>
/// <b>Read literally: <em>n</em> ordinary shots, then the volley.</b> At <see cref="Every"/> 5, shots
/// 1–5 are ordinary, the 6th is the volley and the 12th is the next. The counter moves only when
/// <c>PlayerCombat</c> says a shot has left (<see cref="Loose"/>), so a draw the hold drops (RS-03a
/// rule 3) and a damage frame whose target died count nothing.
/// </para>
/// <para>
/// <b>Three <see cref="Stat"/>s, <c>Kindling</c>'s arrangement</b>: an authored number a node is going
/// to move is a <see cref="Stat"/> seeded from the spec (ADR-0008). Each is read through a clamp at
/// the point of use, because a <see cref="Stat"/> clamps nothing. <see cref="Every"/> is the one with
/// no authored base: it starts at 0, so a class carrying a volley fires none until a node gives it one.
/// </para>
/// <para>
/// <b>Ready is a reading of the count against <see cref="Every"/>, kept current in both directions.</b>
/// A shot moves the count, and a node moves <see cref="Every"/> through <see cref="Stat.Changed"/>, so
/// a rank taken mid-count readies the volley the moment it is taken (rule 3). Each change of
/// <see cref="IsReady"/> is one <see cref="VolleyReady"/>, and nothing else publishes one.
/// </para>
/// <para>
/// <b>Nothing here allocates</b>: two <see cref="int"/>s and a <see cref="bool"/>. The one delegate is made at construction.
/// </para>
/// </remarks>
public sealed class Volley
{
    private readonly IDomainEvents _events;

    /// <param name="spec">The class's authored volley. Null is refused — build one or do not.</param>
    /// <param name="events">Where <see cref="VolleyReady"/> goes.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public Volley(VolleySpec spec, IDomainEvents events)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        _events = events ?? throw new ArgumentNullException(nameof(events));

        Every = new Stat(0f);
        Arrows = new Stat(spec.Arrows);
        Damage = new Stat(spec.DamageMultiplier);

        // The volley's own stat, so the subscription lives exactly as long as the two of them do.
        Every.Changed += OnEveryChanged;
    }

    /// <summary>
    /// How many ordinary shots come before a volley, live — floored where it is read. Base 0: no
    /// volley until a node gives one. Where the Ranger's Volley puts +5, and Volley II and III −1 each.
    /// </summary>
    /// <remarks>
    /// Below 1, or not finite, there is no volley and the count holds where it stands (rule 2).
    /// </remarks>
    public Stat Every { get; }

    /// <summary>Legacy arrow count for authored specs and saved modifiers; unread by the shot path.</summary>
    public Stat Arrows { get; }

    /// <summary>
    /// Each volley arrow's damage as a multiple of an ordinary shot's, live. Base the spec's. See
    /// <see cref="DamageMultiplier"/>.
    /// </summary>
    public Stat Damage { get; }

    /// <summary>The next shot is a volley.</summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// <see cref="Every"/> as the whole number of shots <see cref="Loose"/> counts to: floored, and 0
    /// when there is no volley (UI-01e).
    /// </summary>
    /// <remarks>
    /// The count the counter is measured against, handed out so a HUD draws the same number the
    /// shots are counted to — a pip per shot — rather than flooring the stat again with its own rule.
    /// </remarks>
    public int EveryCount => Count();

    /// <summary>
    /// Ordinary shots since the last volley, in <c>[0, Every]</c> while <see cref="Every"/> holds still.
    /// </summary>
    public int ShotsTowardNext { get; private set; }

    /// <summary>
    /// Legacy <see cref="Arrows"/> count: floored, and clamped to
    /// <c>2..</c><see cref="VolleySpec.MaxArrows"/> — rule 4.
    /// </summary>
    /// <remarks>
    /// Kept for compatibility; the volley now fires once per eligible enemy. NaN reads as the floor.
    /// </remarks>
    public int ArrowCount
    {
        get
        {
            float value = Arrows.Value;

            if (!(value >= 2f))
            {
                return 2;
            }

            return value >= VolleySpec.MaxArrows ? VolleySpec.MaxArrows : (int)MathF.Floor(value);
        }
    }

    /// <summary>
    /// <see cref="Damage"/> as a multiplier a volley arrow can carry: below 1, or not finite, it is 1
    /// — rule 4. A volley never deals less per arrow than the shot it replaces.
    /// </summary>
    public float DamageMultiplier
    {
        get
        {
            float value = Damage.Value;

            return value >= 1f && !float.IsInfinity(value) ? value : 1f;
        }
    }

    /// <summary>
    /// A shot is leaving. Answers whether it is the volley, and moves the counter — rule 3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by <c>PlayerCombat</c> once per damage frame that looses anything, and before the shot is
    /// built, because the answer decides how many arrows it is. A volley resets the count and
    /// publishes <c>VolleyReady(false)</c>; an ordinary shot adds one, and the shot that reaches
    /// <see cref="Every"/> publishes <c>VolleyReady(true)</c>.
    /// </para>
    /// <para>
    /// With no volley (<see cref="Every"/> under 1) an ordinary shot counts nothing: the count holds.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> when this shot is the volley.</returns>
    public bool Loose()
    {
        if (IsReady)
        {
            ShotsTowardNext = 0;
            SetReady(false);

            return true;
        }

        int every = Count();

        if (every < 1)
        {
            return false;
        }

        ShotsTowardNext++;

        if (ShotsTowardNext >= every)
        {
            SetReady(true);
        }

        return false;
    }

    /// <summary>Back to no shots counted and nothing ready, silently. <c>PlayerCombat.Reset</c>'s.</summary>
    /// <remarks>
    /// Publishes nothing, for <c>Kindling.Reset</c>'s reason: the end of a run is not news. The stats
    /// keep their modifiers, which belong to whoever put them there.
    /// </remarks>
    public void Reset()
    {
        ShotsTowardNext = 0;
        IsReady = false;
    }

    /// <summary>
    /// The live count as a whole number of shots: <c>floor(Every.Value)</c>, or 0 when there is none.
    /// </summary>
    /// <remarks>
    /// <c>Kindling.Cap</c>'s spelling: the negated <c>&gt; 0</c> so NaN floors to zero, and anything
    /// past <see cref="int.MaxValue"/> saturates rather than overflowing a cast.
    /// </remarks>
    private int Count()
    {
        float value = Every.Value;

        if (!(value > 0f))
        {
            return 0;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)MathF.Floor(value);
    }

    /// <summary>
    /// A node moved <see cref="Every"/>: the volley is ready exactly when there is one and the count
    /// has reached it — rule 3's "a rank taken mid-count".
    /// </summary>
    /// <remarks>
    /// Both directions, so ready always means the same thing: a lower <see cref="Every"/> readies it at
    /// once, and one raised past the count, or driven under 1, stands it down. The count is never
    /// touched here; only a shot moves it.
    /// </remarks>
    private void OnEveryChanged(Stat _)
    {
        int every = Count();

        SetReady(every >= 1 && ShotsTowardNext >= every);
    }

    /// <summary>Moves <see cref="IsReady"/> and publishes the change, or does nothing.</summary>
    private void SetReady(bool ready)
    {
        if (IsReady == ready)
        {
            return;
        }

        IsReady = ready;

        _events.Publish(new VolleyReady(ready));
    }
}

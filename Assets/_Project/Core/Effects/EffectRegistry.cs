using System;
using System.Collections.Generic;

namespace Soulvail.Core.Effects;

// The effects module's whole vocabulary: the marker every primitive implements, the run-side half
// that knows what applying one means, and the table that matches the two. Three types in one file
// for `RunEvents.cs`' reason — a module's vocabulary read in one place is worth more than one type
// per file, and none of these is more than a few lines. See AR §11.2 and
// <../../../../Docs/adr/0009-effect-primitives-open-set.md>.

/// <summary>
/// Something a node, a Pact, an affix or an Ordeal does — as data.
/// </summary>
/// <remarks>
/// <para>
/// A marker, deliberately: each primitive is its own sealed class and
/// <see cref="EffectRegistry"/> keys on that type, which is what ADR-0009's "no
/// <c>switch (effect.Type)</c>" means in code. There is no <c>Type</c> property to switch on
/// because the CLR already carries one, and no member here at all — anything common to every
/// effect would be a guess about the tenth primitive made while writing the first.
/// </para>
/// <para>
/// Immutable and shared. An effect is authored data the catalog hands to every run (AR §10.1), so
/// nothing on one may be per-run state; the run's live objects are the <em>handler</em>'s, which
/// is the whole of the split.
/// </para>
/// </remarks>
public interface IEffect
{
}

/// <summary>
/// The run-side half of a primitive: what applying <typeparamref name="TEffect"/>'s data to this
/// run's live objects actually means.
/// </summary>
/// <remarks>
/// Built once per run, holding whatever that run's answer needs — <see cref="ModifyStatHandler"/>
/// holds a <see cref="PlayerStats"/>, and a later <c>SpawnZone</c> will hold the thing that owns
/// zones. Registered with one line in <c>RunSession.Start</c>, which is the only edit adding a
/// primitive costs anywhere outside its own file.
/// </remarks>
/// <typeparam name="TEffect">
/// The primitive this handler answers for. Contravariant, so a handler written against a base
/// effect type may be registered for it; dispatch is still by exact type — see
/// <see cref="EffectRegistry.Apply"/>.
/// </typeparam>
public interface IEffectHandler<in TEffect>
    where TEffect : IEffect
{
    /// <summary>
    /// Puts <paramref name="effect"/> into force, on behalf of <paramref name="source"/>.
    /// </summary>
    /// <param name="source">
    /// Who owns the effect — the node, Pact or status that is asking. Identity only: it is what
    /// <see cref="Remove"/> takes back by, so the same instance has to still be in hand then.
    /// </param>
    void Apply(TEffect effect, object source);

    /// <summary>
    /// Takes <paramref name="source"/>'s claim on <paramref name="effect"/> back off the run.
    /// </summary>
    /// <remarks>
    /// A source that has nothing in force is not an error — see <c>Stat.RemoveAll</c>'s contract,
    /// which is where the rule comes from — so a caller may clean up unconditionally.
    /// </remarks>
    void Remove(TEffect effect, object source);
}

/// <summary>
/// Which handler answers for which effect, this run. One per run, built and filled by
/// <c>RunSession.Start</c>, reached through <c>RunState.Effects</c> — which is <c>internal</c>,
/// because <see cref="Apply"/> is public here (AR §18.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the mechanism the ban on <c>switch (effect.Type)</c> exists to make possible</b>
/// (AR §13). A new primitive is one new file holding a record and a handler, plus one
/// <see cref="Register{TEffect}"/> line; nothing here changes, and nothing that dispatches
/// effects anywhere else in the game changes either. Open for extension, closed for
/// modification, in as many words.
/// </para>
/// <para>
/// <b>Dispatch is by exact runtime type.</b> <see cref="Apply"/> looks up
/// <c>effect.GetType()</c>, not the most-derived registration that would accept it: a walk up the
/// base chain would make "which handler ran?" depend on declaration order somewhere else, and
/// every primitive in this project is a sealed class with no base but <see cref="IEffect"/>. If a
/// primitive ever grows a subclass, it registers its own handler.
/// </para>
/// <para>
/// <b>Not a static, and it never becomes one.</b> A registry is the classic singleton temptation —
/// there is obviously only one table of effect handlers — and it is exactly wrong here, because
/// the handlers hold a run's live objects. A static one would keep the previous run's player alive
/// and apply the next run's nodes to it. AR §1's fourth sentence, and
/// <c>Registry_HoldsNoStatics</c> is the row that holds it.
/// </para>
/// <para>
/// Allocates nothing on <see cref="Apply"/> or <see cref="Remove"/> once registration is done: a
/// dictionary probe and a virtual call through a binding built at <see cref="Register{TEffect}"/>
/// time. Effects are classes, so nothing boxes on the way through.
/// </para>
/// </remarks>
public sealed class EffectRegistry
{
    private readonly Dictionary<Type, IBinding> _bindings = new();

    /// <summary>How many effect types have a handler.</summary>
    /// <remarks>
    /// For the composition root's own sanity check and for tests. It is a count of
    /// <em>types</em>, which is the same as a count of handlers because
    /// <see cref="Register{TEffect}"/> refuses a second one.
    /// </remarks>
    public int HandlerCount => _bindings.Count;

    /// <summary>
    /// Names <paramref name="handler"/> as the one answer for <typeparamref name="TEffect"/>,
    /// for the life of this run.
    /// </summary>
    /// <remarks>
    /// <b>One per type, and a second is refused loudly.</b> Two handlers for one effect are two
    /// opinions about what it does, and whichever way the table resolved it — first wins, last
    /// wins — the other would be silently dropped, which is a bug nobody should have to know
    /// dictionary semantics to find.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TEffect"/> already has a handler.
    /// </exception>
    public void Register<TEffect>(IEffectHandler<TEffect> handler)
        where TEffect : IEffect
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        Type type = typeof(TEffect);

        if (_bindings.ContainsKey(type))
        {
            throw new InvalidOperationException(
                $"An effect handler is already registered for '{type.Name}'. Two handlers for one "
                    + "effect are two opinions about what it does, and one of them would be "
                    + "silently dropped.");
        }

        _bindings.Add(type, new Binding<TEffect>(handler));
    }

    /// <summary>
    /// Whether anything in this run knows what <paramref name="effect"/> means.
    /// </summary>
    /// <remarks>
    /// The validation door. M3-03's tree asks this of every effect it holds at <c>Start</c>, which
    /// is what makes <see cref="Apply"/>'s throw unreachable in a live run: an unregistered
    /// primitive is reported before <c>RunStarted</c> rather than at the moment a player picks
    /// the node that carries it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is null.</exception>
    public bool CanApply(IEffect effect)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        return _bindings.ContainsKey(effect.GetType());
    }

    /// <summary>
    /// Puts <paramref name="effect"/> into force on behalf of <paramref name="source"/>, through
    /// whichever handler answers for its type.
    /// </summary>
    /// <param name="source">
    /// Who owns the effect, passed by the caller — M3-03 passes the <c>SkillSpec</c>, which is
    /// shared, immutable and taken once per run, so it is a reference stable for the run's life
    /// and never confused with another node's.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null. A sourceless effect could
    /// never be taken back, which is <c>Modifier</c>'s rule one layer down.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Nothing is registered for <paramref name="effect"/>'s type. Loud rather than ignored, for
    /// <c>EnemyBehaviourKind</c>'s reason (AR §18.4): an effect that silently did nothing would
    /// ship as a node the player takes and gets nothing for.
    /// </exception>
    public void Apply(IEffect effect, object source)
    {
        Bind(effect, source).Apply(effect, source);
    }

    /// <summary>
    /// Takes <paramref name="source"/>'s claim on <paramref name="effect"/> back, through
    /// whichever handler answers for its type.
    /// </summary>
    /// <remarks>
    /// What a source ends up taking back is the handler's business, and for
    /// <see cref="ModifyStatHandler"/> it is deliberately more than this one effect — see its
    /// remarks.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Nothing is registered for <paramref name="effect"/>'s type.
    /// </exception>
    public void Remove(IEffect effect, object source)
    {
        Bind(effect, source).Remove(effect, source);
    }

    private IBinding Bind(IEffect effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        Type type = effect.GetType();

        if (!_bindings.TryGetValue(type, out IBinding binding))
        {
            throw new KeyNotFoundException(
                $"No effect handler is registered for '{type.Name}'. A primitive is a file and a "
                    + "Register line in RunSession.Start; nothing here needs editing to add one.");
        }

        return binding;
    }

    /// <summary>
    /// What the dictionary actually stores: a non-generic door onto a generic handler.
    /// </summary>
    /// <remarks>
    /// The reason <see cref="EffectRegistry"/> can hold handlers of different generic arguments in
    /// one table and still call them without reflection. The cast back to
    /// <c>TEffect</c> in <see cref="Binding{TEffect}"/> is safe by construction — the key is the
    /// type the value was registered under, and <see cref="Bind"/> looks up by the effect's own
    /// runtime type.
    /// </remarks>
    private interface IBinding
    {
        void Apply(IEffect effect, object source);

        void Remove(IEffect effect, object source);
    }

    private sealed class Binding<TEffect> : IBinding
        where TEffect : IEffect
    {
        private readonly IEffectHandler<TEffect> _handler;

        internal Binding(IEffectHandler<TEffect> handler)
        {
            _handler = handler;
        }

        public void Apply(IEffect effect, object source)
        {
            _handler.Apply((TEffect)effect, source);
        }

        public void Remove(IEffect effect, object source)
        {
            _handler.Remove((TEffect)effect, source);
        }
    }
}

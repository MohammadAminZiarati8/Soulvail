using System;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

// The first effect primitive, and its handler. One file per pair: a primitive is the data and the
// run-side answer to it, and reading either one without the other tells you half of what it does.
// Every later primitive — OnKillTrigger, Heal, SpawnZone, ApplyStatus, ChainDamage — is this shape
// again, plus one Register line in `RunSession.Start`.

/// <summary>
/// Who a <see cref="ModifyStat"/> is aimed at.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two members, and <see cref="Self"/> means <em>the thing that cast it</em> rather than
/// <em>the enemy</em></b> (M4-01a rule 3). A boss buffing itself and a player node buffing the
/// player are the same operation aimed differently, which is the whole reason there is one
/// primitive here rather than two.
/// </para>
/// <para>
/// <b>There is deliberately no <c>Enemy</c> and no <c>Nearest</c>.</b> An effect that debuffs
/// <em>someone else</em> is a different primitive with a selection rule — which target, chosen how,
/// and what happens when there is none — and that is M6's, not this one's. Adding a member for one
/// of those would smuggle a selection rule in as an enum value with nowhere to author it.
/// <b><see cref="Minions"/> is not that member and does not weaken the rule</b> (M5-06a rule 1):
/// what it addresses is a <see cref="MinionRecipe"/>, of which a run has exactly one, for its whole
/// life, found without choosing anything. The rule is about a <em>someone</em>, and a recipe is not
/// one.
/// </para>
/// </remarks>
public enum StatTarget
{
    /// <summary>
    /// This run's player. The default, which is what holds M4-01a's ripple to nothing: every
    /// existing call site and all nine shipped assets keep meaning exactly what they meant.
    /// </summary>
    Player,

    /// <summary>
    /// Whoever is casting. Only legal inside <see cref="ModifyStatHandler.Aiming"/>, and it throws
    /// outside one rather than falling back — see that method.
    /// </summary>
    Self,

    /// <summary>
    /// The run's minions, aimed at the <see cref="MinionRecipe"/> rather than at the bodies —
    /// M5-06a rule 1. Legal only on a class with a <c>MinionSpec</c>; a run whose tree aims here
    /// without one is refused at <c>RunSession.Start</c> (rule 5), because
    /// <see cref="EffectRegistry.CanApply"/> answers <em>"is there a handler for this type"</em> and
    /// a target is a field rather than a type, so the registry's own sweep cannot see it.
    /// </summary>
    Minions,
}

/// <summary>
/// One <see cref="Modifier"/> on one <c>Stat</c>, as data: which number, on whom, which stack
/// position, how much. The primitive nearly every passive in the game is made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>One modifier, not one node.</b> A node granting "+2 damage and +15 %" carries two of these,
/// which is <c>Stat.Add</c>'s own remark — the arithmetic already knows how to pool two
/// contributions from one source, and an effect that carried a list would be inventing a second
/// way to say what the stack says.
/// </para>
/// <para>
/// <b><see cref="ModifierKind.PercentAdd"/> is the kind a node reaches for</b>, and
/// <see cref="ModifierKind.PercentMult"/> is reserved for the loud multipliers — Focus at full
/// ramp, a boss phase, depth scaling, the Claiming — which is <c>ModifierKind</c>'s own remark and
/// GD §13.1's "additively within a family, multiplicatively across". The kind is data and the
/// author's choice; nothing is refused on it here, and M3-12 owes one line per node saying which
/// it used and why.
/// </para>
/// <para>
/// Immutable and shared across every run, like every effect (AR §10.1). The address is not
/// validated here on purpose: an <see cref="IStatBlock"/> is the one site that knows which stats
/// are live on whoever is being aimed at, and it is loud, which is <c>MovementSkillKind</c>'s
/// argument exactly. <b><see cref="Target"/> <em>is</em> validated here</b>, because unlike an
/// address it has no second door: an out-of-range target would fall out of the handler's switch at
/// the moment a player picked the node rather than when the asset was built.
/// </para>
/// </remarks>
public sealed class ModifyStat : IEffect
{
    /// <param name="stat">Which player number this moves.</param>
    /// <param name="kind">
    /// Which of the three stack positions the modifier occupies. See the remarks on this class for
    /// which one a node should want.
    /// </param>
    /// <param name="value">
    /// The amount, read according to <paramref name="kind"/>: units for
    /// <see cref="ModifierKind.Flat"/>, a fraction for either percentage kind — 0.15 is +15 %.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is not one of the three, or <paramref name="value"/> is NaN or
    /// infinite. Both are refused at this door as well as at <see cref="Modifier"/>'s, because this
    /// is where a mistake is <em>authored</em> — a content error caught when the asset is built is
    /// a content error, and the same error caught at the moment a player picks the node is a crash
    /// in a run (AR §18.3).
    /// </exception>
    /// <param name="target">
    /// Who this is aimed at. <b>Defaulted</b>, and that default is what makes M4-01a a widening
    /// rather than a migration: every call site and every shipped asset written before there was a
    /// choice still says the thing it always said.
    /// </param>
    public ModifyStat(
        PlayerStat stat,
        ModifierKind kind,
        float value,
        StatTarget target = StatTarget.Player)
    {
        if (kind is not (ModifierKind.Flat or ModifierKind.PercentAdd or ModifierKind.PercentMult))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Modifier kind must be one of Flat, PercentAdd or PercentMult.");
        }

        // Asked as "is it NaN or infinite?" rather than as a range comparison, for the reason
        // AR §18.3 gives: every comparison against NaN is false, so a range check waves it
        // straight through — and a NaN that reached a Stat would silence its Changed event rather
        // than produce a wrong number.
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "ModifyStat value must be finite.");
        }

        // Checked with Enum.IsDefined rather than as a two-way `is not`, because this enum is the
        // kind that may grow: a third member added without a case in the handler's switch would
        // otherwise pass here and fail in a run. The kind above is written the other way for the
        // opposite reason — ModifierKind is closed and has been since M0.
        if (!Enum.IsDefined(typeof(StatTarget), target))
        {
            throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                "A ModifyStat is aimed at Player, Self or Minions.");
        }

        Stat = stat;
        Kind = kind;
        Value = value;
        Target = target;
    }

    /// <summary>Which number this moves.</summary>
    public PlayerStat Stat { get; }

    /// <summary>Which stack position the modifier occupies.</summary>
    public ModifierKind Kind { get; }

    /// <summary>The amount, read according to <see cref="Kind"/>.</summary>
    public float Value { get; }

    /// <summary>Whose number it is — <see cref="StatTarget.Player"/> unless authored otherwise.</summary>
    public StatTarget Target { get; }
}

/// <summary>
/// What a <see cref="ModifyStat"/> means to this run: one <see cref="Modifier"/> added to, or a
/// source taken back off, the live <c>Stat</c> the effect addresses.
/// </summary>
/// <remarks>
/// <para>
/// The run-side half of the split. It holds this run's player block — the effect holds only an
/// address — so one shared, immutable <see cref="ModifyStat"/> is applied to whoever happens to be
/// playing.
/// </para>
/// <para>
/// <b>It is aimed for the duration of one call, not constructed per combatant</b> (M4-01a rule 5).
/// <c>EffectRegistry</c> registers one handler per effect type for the whole run, so a handler that
/// held a combatant would mean a registry per combatant. <see cref="Aiming"/> sets the caster's
/// block, returns a scope that clears it, and <b>an <see cref="StatTarget.Self"/> effect applied
/// outside a scope throws</b> — see that method for why a fallback is the worst outcome available
/// here.
/// </para>
/// <para>
/// <b><see cref="Remove"/> takes the source back, not the effect</b>, and that is deliberate rather
/// than a simplification. <c>Stat.RemoveAll(source)</c> is the only removal the modifier stack
/// offers, and it is the right one: a node granting "+2 damage and +15 %" is two effects from one
/// source, and taking one of them off while leaving the other would be a half-removed node. The
/// consequence to know is that removing <em>either</em> of that node's two effects removes both,
/// and that is right for V1 — nothing removes a node (respec was deleted with v0.1's meta systems,
/// CH §7), and the first timed buff (M3-06, M3-11) owns its own clock and calls this for its own
/// source, which is exactly a source being taken back in full.
/// </para>
/// <para>
/// Allocates nothing, aiming included: the <see cref="Modifier"/> is a struct handed to
/// <c>Stat.Add</c> by <see langword="in"/>, an <see cref="IStatBlock"/>'s <c>Resolve</c> is a jump
/// table, and the scope <see cref="Aiming"/> hands back is built once in the constructor and reused.
/// </para>
/// </remarks>
public sealed class ModifyStatHandler : IEffectHandler<ModifyStat>
{
    private readonly IStatBlock _player;

    /// <summary>
    /// This run's minion recipe as an address book, or <see langword="null"/> on a class that raises
    /// nothing. Null is a real answer rather than a missing dependency — see the constructor.
    /// </summary>
    private readonly IStatBlock _minions;

    /// <summary>
    /// The one scope instance, handed out by every <see cref="Aiming"/> call. Built here rather
    /// than per call because a <c>using</c> on a freshly allocated scope is an allocation on the
    /// cast path, and a struct scope would box on the way out as <see cref="IDisposable"/>.
    /// Reusing it is safe precisely because <see cref="Aiming"/> refuses to nest.
    /// </summary>
    private readonly AimScope _scope;

    /// <summary>
    /// Whoever is currently casting, or <see langword="null"/> outside a scope. The whole of the
    /// handler's mutable state, and it lives for one <c>Apply</c>/<c>Remove</c> pair.
    /// </summary>
    private IStatBlock _self;

    /// <param name="player">Where each of the player's addresses lives this run.</param>
    /// <param name="minions">
    /// This run's minion recipe as an address book, or <see langword="null"/> on a class that has
    /// none — which is every class but the Gravecaller. <b>Null is a real answer and not a missing
    /// dependency</b> (M5-06a rule 5): it is defaulted for the same reason
    /// <see cref="ModifyStat.Target"/> is, so every call site written before there was a second
    /// block still says what it always said, and the throw that follows a
    /// <see cref="StatTarget.Minions"/> effect reaching it names the class rather than reporting a
    /// null.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    public ModifyStatHandler(IStatBlock player, IStatBlock minions = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _minions = minions;
        _scope = new AimScope(this);
    }

    /// <summary>
    /// Aims this handler at <paramref name="self"/> for the duration of the returned scope, so that
    /// effects carrying <see cref="StatTarget.Self"/> land on the caster.
    /// </summary>
    /// <param name="self">The caster's own block.</param>
    /// <returns>
    /// A scope that un-aims the handler on <see cref="IDisposable.Dispose"/>. The same instance
    /// every time — see <see cref="_scope"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="self"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A scope is already open. Refused rather than nested: a second aim would either silently
    /// shadow the first or silently restore it on the inner dispose, and both are a boss buff
    /// landing on the wrong body with nothing said.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Public rather than <see langword="internal"/>, and the seal is one layer out</b> —
    /// <c>PlayerStats</c>' own argument. <c>Soulvail.Tests.Core</c> has no
    /// <c>InternalsVisibleTo</c> and deliberately never will (AR §18.2, M0-10), so an
    /// <see langword="internal"/> spelling would put every rule about aiming out of reach of the
    /// only assembly that could prove them. What actually keeps this out of Unity's hands is that
    /// <c>RunState.Effects</c> is <see langword="internal"/>: nothing outside core can reach a
    /// registry, let alone a handler inside one.
    /// </para>
    /// <para>
    /// <b>Applying a <see cref="StatTarget.Self"/> effect with no scope open throws, and does not
    /// fall back to the player</b> (M4-01a rule 5). A fallback is the worst outcome this seam can
    /// produce: a boss's self-buff would land on the player, both numbers would move by exactly the
    /// amount somebody authored, nothing would be null and no test would be red. Traps §1's family
    /// wearing a different hat.
    /// </para>
    /// </remarks>
    public IDisposable Aiming(IStatBlock self)
    {
        if (self is null)
        {
            throw new ArgumentNullException(nameof(self));
        }

        if (_self is not null)
        {
            throw new InvalidOperationException(
                "This handler is already aimed. Aiming does not nest — one cast at a time, and a "
                    + "scope left open is a leak rather than a second caster.");
        }

        _self = self;

        return _scope;
    }

    /// <summary>
    /// Adds <paramref name="effect"/>'s modifier to the stat it addresses, tagged with
    /// <paramref name="source"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> is null, or <paramref name="source"/> is — the latter from
    /// <see cref="Modifier"/>, which refuses a modifier nothing could ever take back.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The effect addresses a stat the block it is aimed at has no answer for.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The effect is aimed at <see cref="StatTarget.Self"/> and no <see cref="Aiming"/> scope is
    /// open. Refused rather than sent to the player — see that method.
    /// </exception>
    public void Apply(ModifyStat effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        Block(effect.Target).Resolve(effect.Stat).Add(new Modifier(effect.Kind, effect.Value, source));
    }

    /// <summary>
    /// Takes every modifier <paramref name="source"/> put on the stat
    /// <paramref name="effect"/> addresses back off it — see this class's remarks for why that is
    /// more than the one effect.
    /// </summary>
    /// <remarks>
    /// A source with nothing on that stat is not an error and changes nothing, which is
    /// <c>Stat.RemoveAll</c>'s contract and what lets a caller clean up unconditionally.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The effect addresses a stat the block it is aimed at has no answer for.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The effect is aimed at <see cref="StatTarget.Self"/> and no <see cref="Aiming"/> scope is
    /// open. Refused rather than sent to the player — see that method.
    /// </exception>
    public void Remove(ModifyStat effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        Block(effect.Target).Resolve(effect.Stat).RemoveAll(source);
    }

    /// <summary>
    /// Whose numbers an effect aimed at <paramref name="target"/> moves.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="target"/> is <see cref="StatTarget.Self"/> and no scope is open — see
    /// <see cref="Aiming"/> — or it is <see cref="StatTarget.Minions"/> on a class that raises
    /// nothing. Both are refused rather than sent to the player, and for one reason: a fallback
    /// would move a real number by the authored amount and nothing would report it.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A <see cref="StatTarget"/> member with no case here. Unreachable through
    /// <see cref="ModifyStat"/>'s constructor, which refuses one, and loud anyway for
    /// <c>Stat.Pool</c>'s reason.
    /// </exception>
    /// <remarks>
    /// <b><see cref="StatTarget.Minions"/> is one case beside the other two, and that is the whole
    /// of rule 1.</b> <see cref="Aiming"/> is untouched and does not nest around it: the recipe is
    /// found rather than chosen, so there is no scope to open and nothing wraps a minion node in a
    /// <c>using</c>.
    /// </remarks>
    private IStatBlock Block(StatTarget target)
    {
        return target switch
        {
            StatTarget.Player => _player,
            StatTarget.Self => _self ?? throw new InvalidOperationException(
                "A ModifyStat aimed at Self was applied outside an aiming scope. It is refused "
                    + "rather than sent to the player: a self-buff that quietly landed on the "
                    + "player would move a real number by the authored amount and nothing would "
                    + "report it. Open a scope with ModifyStatHandler.Aiming(caster)."),
            StatTarget.Minions => _minions ?? throw new InvalidOperationException(
                "A ModifyStat aimed at Minions was applied on a class that raises none, so there "
                    + "is no minion recipe to move. It is refused rather than sent to the player "
                    + "for the reason a Self effect outside a scope is: the player's own number "
                    + "would move by the authored amount and nothing would report it. A tree that "
                    + "aims here belongs to a class with a MinionSpec, and RunSession.Start "
                    + "refuses the run before this is ever reached."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(target),
                target,
                "No block is wired to this StatTarget. A member was added to the enum and not to "
                    + "the switch that reads it."),
        };
    }

    /// <summary>
    /// The scope <see cref="Aiming"/> hands back: disposing it un-aims the handler.
    /// </summary>
    /// <remarks>
    /// A private nested class rather than a struct, so that handing it out as
    /// <see cref="IDisposable"/> does not box — and a single long-lived instance, so that aiming
    /// allocates nothing at all. Disposing one that is already disposed clears an already-null
    /// field and is deliberately not an error: a <c>using</c> that unwound through an exception
    /// must be able to clean up without knowing whether it got as far as aiming.
    /// </remarks>
    private sealed class AimScope : IDisposable
    {
        private readonly ModifyStatHandler _handler;

        internal AimScope(ModifyStatHandler handler)
        {
            _handler = handler;
        }

        public void Dispose()
        {
            _handler._self = null;
        }
    }
}

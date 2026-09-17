using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Effects;

// The fourth primitive, and the first that addresses a piece of *content* rather than a number.
// One file per pair, like every primitive before it: the data and the run-side answer to it, and
// reading either one without the other tells you half of what it does.

/// <summary>
/// One <see cref="Modifier"/> on one <em>named skill's</em> cooldown, as data:
/// <em>"−25 % Consecrate cooldown"</em>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It addresses a skill, and that is the whole reason it exists</b> (rule 1).
/// <see cref="PlayerStat"/> gives one member per stat (M3-05 rule 4), so a node naming a skill
/// cannot be a <see cref="ModifyStat"/> — there is no member for <em>"Consecrate's cooldown"</em>
/// and there could not be one, because the set of skills is content and the set of stats is code.
/// M3-06 rule 12 said exactly this and built <c>SkillRunner.CooldownOf(index)</c> and
/// <c>SkillRunner.TryIndexOf(id)</c> so that <em>"that task is a file rather than a refactor"</em>.
/// This is that file.
/// </para>
/// <para>
/// <b>CH §4.1's floor is not this type's business and is deliberately not applied here.</b> A
/// modifier is a contribution to a stack; <c>CooldownRules.Effective</c> is what reads the stack and
/// says no. Clamping at the door would make the floor depend on how many nodes were taken rather
/// than on the authored base, which is the second copy <c>CooldownRules</c> exists to prevent. An
/// 8 s Active floors at 3.2 s however deep the stack goes.
/// </para>
/// <para>
/// <b><see cref="ModifierKind.PercentMult"/> is the kind CH §4.1 describes</b> —
/// <em>"multiplicative and floored at 40 % of base"</em> — and what the word buys is the
/// <em>cost</em>: six −15 % multiplicative nodes reach an 8 s Active's floor where four additive
/// ones do. The kind is still data and still the author's choice; nothing is refused on it here,
/// which is <see cref="ModifyStat"/>'s rule one file over.
/// </para>
/// <para>
/// Immutable and shared across every run, like every effect (AR §10.1). The <see cref="SkillId"/> is
/// validated for shape and not for existence: whether this run owns that skill is a question only
/// the run can answer, and rule 3 says the answer is never an error.
/// </para>
/// </remarks>
public sealed class ModifySkillCooldown : IEffect
{
    /// <param name="skillId">
    /// Which owned Active's cooldown this moves. Any well-formed id: whether the player owns it is
    /// <see cref="ModifySkillCooldownHandler"/>'s business and never a failure.
    /// </param>
    /// <param name="kind">
    /// Which of the three stack positions the modifier occupies. See the remarks on this class for
    /// which one CH §4.1 describes.
    /// </param>
    /// <param name="value">
    /// The amount, read according to <paramref name="kind"/>: seconds for
    /// <see cref="ModifierKind.Flat"/>, a fraction for either percentage kind — −0.25 is −25 %.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="skillId"/> is <c>default(ContentId)</c>. A node that names nobody would be
    /// held pending for ever under rule 3 rather than reported, which is the one way this primitive
    /// can fail silently — so it is refused where it is <em>authored</em> instead (AR §18.3).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is not one of the three, or <paramref name="value"/> is NaN or
    /// infinite. Both are refused at this door as well as at <see cref="Modifier"/>'s, for
    /// <see cref="ModifyStat"/>'s reason: a content error caught when the asset is built is a
    /// content error, and the same error caught at the moment a player picks the node is a crash in
    /// a run.
    /// </exception>
    public ModifySkillCooldown(ContentId skillId, ModifierKind kind, float value)
    {
        if (skillId == default)
        {
            throw new ArgumentException(
                "ModifySkillCooldown must name a skill. The default id belongs to no node, and "
                    + "rule 3 holds a modifier for a skill the player does not own rather than "
                    + "reporting it — so an unnamed one would wait for a skill that can never "
                    + "arrive and nothing anywhere would say so.",
                nameof(skillId));
        }

        if (kind is not (ModifierKind.Flat or ModifierKind.PercentAdd or ModifierKind.PercentMult))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Modifier kind must be one of Flat, PercentAdd or PercentMult.");
        }

        // Asked as "is it NaN or infinite?" rather than as a range comparison, for the reason
        // AR §18.3 gives and ModifyStat spells out: every comparison against NaN is false, so a
        // range check waves it straight through — and a NaN cooldown is a skill that silently never
        // fires again, which is what CooldownRules' own negated spelling is there to survive.
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "ModifySkillCooldown value must be finite.");
        }

        SkillId = skillId;
        Kind = kind;
        Value = value;
    }

    /// <summary>Which owned Active's cooldown this moves.</summary>
    public ContentId SkillId { get; }

    /// <summary>Which stack position the modifier occupies.</summary>
    public ModifierKind Kind { get; }

    /// <summary>The amount, read according to <see cref="Kind"/>.</summary>
    public float Value { get; }
}

/// <summary>
/// What a <see cref="ModifySkillCooldown"/> means to this run: one <see cref="Modifier"/> on the
/// live cooldown <see cref="Stat"/> the named skill holds — or, when the player does not own it
/// yet, a note kept until they do.
/// </summary>
/// <remarks>
/// <para>
/// <b>The address is resolved live, and the <see cref="Stat"/> it reaches is the runner's own</b>
/// (rule 2). <c>SkillRunner.TryIndexOf</c> then <c>SkillRunner.CooldownOf</c> returns the very
/// instance the entry holds — M3-06's <c>Runner_ExposesTheCooldownAddress</c> pins the
/// <see cref="object.ReferenceEquals"/> — so a modifier lands on the number the runner reads each
/// time it starts a cooldown. Nothing is copied and nothing is re-seeded.
/// </para>
/// <para>
/// <b>A node for a skill the player does not own is applied to nothing, silently, and is not an
/// error</b> (rule 3). An Upgrade is only offered if you own its parent (CH §4) and
/// <c>TreeRules</c> refuses a tree whose Upgrade has no parent in the branch (M3-03 rule 1), so in
/// authored content the parent is always owned by the time the child can be taken — but a
/// <b>Passive</b> may carry one too, and nothing gates a passive. Throwing would make a legal tree
/// crash at a pick, and <c>SkillTree.Take</c> cannot throw from inside (M3-03 rule 4 guarantees
/// <c>CanApply</c>, not that every handler finds its target). Refusing and forgetting is worse
/// still: it is a node that lies. So the modifier is held here and spent when the skill arrives.
/// </para>
/// <para>
/// <b>Arrival is a callback from the runner, not an event</b>, and that is rule 3's only mechanism.
/// <c>SkillRunner.Add</c> replaces the entry's cooldown <see cref="Stat"/> with a fresh one seeded
/// from the spec, so a modifier applied before that line would land on a stat that is about to be
/// thrown away — the hook fires as the <em>last</em> thing <c>Add</c> does, after the new stat
/// exists and after the count has moved. It is a callback rather than a subscription to
/// <c>NodeTaken</c> because core does not listen to its own domain events, which is
/// <c>RunSession</c>'s own standing remark and <c>SkillRunner</c>'s.
/// </para>
/// <para>
/// <b><see cref="Remove"/> takes the source back from wherever it landed</b> (rule 4) — the live
/// stat through <c>Stat.RemoveAll(source)</c>, which is the same door M3-05 rule 6 uses, and the
/// pending table by the same key. Nothing in M3 removes a node; the path exists because the first
/// timed cooldown buff will want it, and because a half-removed source is the bug
/// <see cref="ModifyStatHandler"/>'s own remarks describe.
/// </para>
/// <para>
/// Allocates nothing after construction: two fixed arrays sized <see cref="MaxPending"/>, a
/// <see cref="Modifier"/> struct per call, and one delegate made once when the runner is watched.
/// </para>
/// </remarks>
public sealed class ModifySkillCooldownHandler : IEffectHandler<ModifySkillCooldown>
{
    /// <summary>
    /// The most unspent modifiers this may hold at once — modifiers applied for skills the player
    /// does not own yet.
    /// </summary>
    /// <remarks>
    /// CH §5's 27 nodes a class is the ceiling on how many nodes could each carry one, and every
    /// one of them would have to be taken before any of their skills arrived. Thirty-two is
    /// headroom on a state no legal tree can reach, and the arrays are sized once at construction so
    /// nothing grows on a pick.
    /// </remarks>
    public const int MaxPending = 32;

    private readonly SkillRunner _runner;

    /// <summary>Which skill each unspent modifier is waiting for.</summary>
    private readonly ContentId[] _pendingIds = new ContentId[MaxPending];

    /// <summary>
    /// The unspent modifiers themselves, parallel to <see cref="_pendingIds"/>.
    /// </summary>
    /// <remarks>
    /// Two parallel arrays and a count rather than an array of pairs — <c>SkillRunner</c>'s shape
    /// and <c>GrantedShieldPool</c>'s, for their reason: the id is scanned and the modifier is
    /// carried, and one array of each keeps the scan reading one cache line of ids.
    /// </remarks>
    private readonly Modifier[] _pendingModifiers = new Modifier[MaxPending];

    private int _pendingCount;

    /// <param name="runner">
    /// This run's actives and their live cooldowns — the thing that both holds the address and
    /// knows when a new one appears.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="runner"/> is null.</exception>
    public ModifySkillCooldownHandler(SkillRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));

        // Once, here, and never again — the only allocation this class makes after its arrays, and
        // made while the run is being composed rather than while one is played. See the class
        // remarks for why a callback and not an event.
        _runner.WatchArrivals(Spend);
    }

    /// <summary>How many modifiers are waiting for a skill that has not arrived yet.</summary>
    /// <remarks>
    /// For tests and for the composition root's own sanity: rule 3's whole promise is that an
    /// unowned skill is silent, and silence is not observable without this.
    /// </remarks>
    public int PendingCount => _pendingCount;

    /// <summary>
    /// Puts <paramref name="effect"/>'s modifier on the named skill's live cooldown, or holds it
    /// until that skill arrives.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> is null, or <paramref name="source"/> is — the latter from
    /// <see cref="Modifier"/>, which refuses a modifier nothing could ever take back.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="MaxPending"/> modifiers are already waiting. <b>This is not the throw rule 3
    /// forbids</b>: a node for a skill you do not own is a legal, reachable state and stays silent,
    /// where thirty-three unspent ones at once is not reachable from any tree <c>TreeRules</c>
    /// accepts. <c>SkillRunner.Add</c>'s capacity throw exactly — a backstop that would otherwise be
    /// silence.
    /// </exception>
    public void Apply(ModifySkillCooldown effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        // Built before the branch so that a null source is refused the same way down both paths —
        // otherwise a node applied for an unowned skill would be held, and would throw later on a
        // frame that has nothing to do with the pick that caused it.
        var modifier = new Modifier(effect.Kind, effect.Value, source);

        if (_runner.TryIndexOf(effect.SkillId, out int index))
        {
            _runner.CooldownOf(index).Add(modifier);

            return;
        }

        if (_pendingCount == MaxPending)
        {
            throw new InvalidOperationException(
                $"{MaxPending} cooldown modifiers are already waiting for skills this run does not "
                    + $"own, and '{effect.SkillId}' would be one more. CH §5 gives a class 27 nodes, "
                    + "so a tree that could ask for this is not one TreeRules accepts.");
        }

        _pendingIds[_pendingCount] = effect.SkillId;
        _pendingModifiers[_pendingCount] = modifier;
        _pendingCount++;
    }

    /// <summary>
    /// Takes every modifier <paramref name="source"/> put on the named skill's cooldown back off
    /// it — from the live stat and from the pending table alike.
    /// </summary>
    /// <remarks>
    /// A source with nothing on that skill is not an error and changes nothing, which is
    /// <c>Stat.RemoveAll</c>'s contract and what lets a caller clean up unconditionally. Like
    /// <see cref="ModifyStatHandler.Remove"/>, what comes back is the <em>source's</em> claim on
    /// that one skill rather than this one effect: a node carrying <em>"−25 % Consecrate cooldown
    /// and −10 % Bulwark cooldown"</em> is two effects, and removing either takes back only its own
    /// skill's — which is a finer grain than <see cref="ModifyStatHandler"/> manages, because the
    /// address is on the effect.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null.
    /// </exception>
    public void Remove(ModifySkillCooldown effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (_runner.TryIndexOf(effect.SkillId, out int index))
        {
            _runner.CooldownOf(index).RemoveAll(source);
        }

        // And the pending table too, unconditionally: a node taken and removed before its skill
        // arrived must not land the moment it does. The two are not alternatives — a source could
        // legitimately have one modifier live and another pending if the same node named two
        // skills — so both doors are tried.
        DropPending(effect.SkillId, source);
    }

    /// <summary>
    /// The runner has just taken ownership of <paramref name="skillId"/>, whose live cooldown is
    /// <paramref name="cooldown"/>: everything held for it lands now, and is held no longer.
    /// </summary>
    /// <remarks>
    /// <b>Spent, not replayed</b> (rule 3). Each entry is added to the stat and struck from the
    /// table, so the same node cannot reduce the same cooldown twice however many later skills
    /// arrive — which is what <c>Cooldown_PendingIsSpentOnce</c> asserts by counting the stack
    /// rather than reading the value.
    /// </remarks>
    private void Spend(ContentId skillId, Stat cooldown)
    {
        int kept = 0;

        for (int i = 0; i < _pendingCount; i++)
        {
            if (_pendingIds[i].Equals(skillId))
            {
                cooldown.Add(_pendingModifiers[i]);

                continue;
            }

            // Compacted in place rather than swapped with the last, because the order modifiers
            // arrive in is the order a later reader would expect them applied — and the table is at
            // most MaxPending long, so the shift costs nothing worth naming.
            _pendingIds[kept] = _pendingIds[i];
            _pendingModifiers[kept] = _pendingModifiers[i];
            kept++;
        }

        Clear(kept);

        _pendingCount = kept;
    }

    /// <summary>
    /// Strikes every entry waiting for <paramref name="skillId"/> on behalf of
    /// <paramref name="source"/>.
    /// </summary>
    private void DropPending(ContentId skillId, object source)
    {
        int kept = 0;

        for (int i = 0; i < _pendingCount; i++)
        {
            if (_pendingIds[i].Equals(skillId)
                && ReferenceEquals(_pendingModifiers[i].Source, source))
            {
                continue;
            }

            _pendingIds[kept] = _pendingIds[i];
            _pendingModifiers[kept] = _pendingModifiers[i];
            kept++;
        }

        Clear(kept);

        _pendingCount = kept;
    }

    /// <summary>
    /// Blanks the entries from <paramref name="kept"/> up to the old count, so nothing above the
    /// live range holds a reference.
    /// </summary>
    /// <remarks>
    /// A <see cref="Modifier"/> carries its <c>Source</c>, which is a <c>SkillSpec</c> — shared and
    /// immutable, so this leaks nothing a run does not already hold. It is blanked anyway because a
    /// stale entry above the count is the kind of thing a later off-by-one reads as live, and a
    /// default <see cref="ContentId"/> there matches no skill that can ever arrive.
    /// </remarks>
    private void Clear(int kept)
    {
        for (int i = kept; i < _pendingCount; i++)
        {
            _pendingIds[i] = default;
            _pendingModifiers[i] = default;
        }
    }
}

using System;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// The actives the player owns, their live cooldowns, and the tick that fires one without being
/// asked — CH §4.2's <em>"the player does nothing"</em> as a class that owns clocks and casts, and
/// knows nothing about buttons.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything is scheduled as an absolute time</b> against the simulated clock its caller passes
/// in — <see cref="Weapon"/>'s and <see cref="ChargeSkill"/>'s shape, and for their reason. There is
/// no accumulator to drift, so a 30 fps phone and a 120 fps one cast at the same rate.
/// </para>
/// <para>
/// <b>The cooldown is sampled at the start of the cast and held, and the fill is not.</b> A
/// modifier landing mid-cooldown changes the <em>next</em> wait rather than the one running, while
/// <see cref="CooldownFraction"/> is measured against the <em>current</em> effective value so the
/// buff is visible immediately. That is exactly <see cref="ChargeSkill"/>'s bargain, in the same
/// words on purpose: M3-10 draws both fills side by side and they must not answer differently.
/// </para>
/// <para>
/// <b>No throttle, and the arithmetic is why.</b> <c>TriggerSpec.IsMet</c> is allocation-free over
/// at most two clauses (M3-02a rules 5, 6), and a full 27-node tree at CH §4's ~25 % Active is
/// about seven actives and at most fourteen float comparisons a frame. ADR-0003 says core throttles
/// its own expensive work; this is not expensive, and a 10 Hz cadence would make a trigger answer
/// up to 100 ms late on the one system whose whole promise is that it reacts. The class allocates
/// nothing after construction: three fixed arrays sized <see cref="MaxActives"/>, and
/// <see cref="Tick"/> writes no list.
/// </para>
/// <para>
/// <b>Core pushes a skill in here; this class subscribes to nothing.</b> M3-08a's
/// <c>ChooseOffer</c> calls <see cref="Add"/> when the node taken is an Active, and
/// <c>RunSession.Start</c> calls it for each Active in a resumed run's take order — core has no
/// business listening to its own events, which is <c>RunSession</c>'s own remark on
/// <c>PlayerDied</c> and <c>SkillTree.OwnedActives</c>' one object over.
/// </para>
/// </remarks>
public sealed class SkillRunner
{
    /// <summary>
    /// The most actives one run may own.
    /// </summary>
    /// <remarks>
    /// CH §5's 27 nodes at CH §4's ~25 % Active is about seven; twelve is headroom, and the arrays
    /// are sized once at construction so nothing grows on a pick. <b>A tree that would not fit is
    /// refused at <c>Start</c></b> — <c>RunSession.Start</c> compares this against
    /// <c>TreeRules.ActiveCount</c> before the run is announced, which is what makes
    /// <see cref="Add"/>'s own capacity throw unreachable in a live run rather than merely
    /// unlikely. The same shape <c>SkillTree</c>'s constructor gives <c>EffectRegistry.Apply</c>:
    /// sweep the authored content at <c>Start</c>, keep the throw as the backstop.
    /// </remarks>
    public const int MaxActives = 12;

    private readonly EffectRegistry _effects;
    private readonly CombatBlackboard _blackboard;
    private readonly IDomainEvents _events;

    /// <summary>The owned actives in take order, which is the order <see cref="Tick"/> walks.</summary>
    private readonly SkillSpec[] _specs = new SkillSpec[MaxActives];

    /// <summary>
    /// One live cooldown per entry, seeded from <c>ActiveSpec.Cooldown</c>.
    /// </summary>
    /// <remarks>
    /// A fresh <see cref="Stat"/> per entry and never the spec's raw number —
    /// <see cref="ChargeSkill"/>'s constructor's argument, and ADR-0008's: the spec stays what a
    /// designer typed, and this is what M3-12's <c>ModifySkillCooldown</c> puts a modifier on.
    /// </remarks>
    private readonly Stat[] _cooldowns = new Stat[MaxActives];

    /// <summary>The earliest time each entry may fire again. Zero until its first cast.</summary>
    private readonly float[] _readyAt = new float[MaxActives];

    private int _count;

    /// <summary>
    /// The last time this object was given the clock, by either <see cref="Tick"/> or
    /// <see cref="Cast"/>.
    /// </summary>
    /// <remarks>
    /// <c>ChargeSkill._now</c>'s shape and its reason: every state property below is read
    /// against this rather than against a clock of its own, so <see cref="IsReady"/> and
    /// <see cref="CooldownFraction"/> describe the tick the caller is in the middle of and cannot
    /// disagree with each other. <see cref="Cast"/> advances it too, because a manual cast is also
    /// a reading of the caller's clock — which is what lets a handler reading the fraction from
    /// inside <see cref="SkillCast"/> see 1 whichever door the cast came through.
    /// </remarks>
    private float _now;

    /// <param name="effects">Which handler answers for which effect, this run.</param>
    /// <param name="blackboard">
    /// What the character currently perceives — the object every trigger is a predicate over
    /// (ADR-0005). Borrowed, never owned: <c>PlayerCombat</c> writes it.
    /// </param>
    /// <param name="events">Where <see cref="SkillCast"/> goes.</param>
    /// <exception cref="ArgumentNullException">Any of the three is null.</exception>
    public SkillRunner(EffectRegistry effects, CombatBlackboard blackboard, IDomainEvents events)
    {
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>How many actives the player owns.</summary>
    public int Count => _count;

    /// <summary>
    /// Takes ownership of an Active, at the end of the walk order.
    /// </summary>
    /// <param name="skill">
    /// The node that was taken. Must be a <see cref="SkillKind.Active"/> — every other kind is
    /// already in force the moment <c>SkillTree.Take</c> applied its effects, and has nothing here
    /// to own.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="skill"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="skill"/> is not an Active.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="skill"/> is already owned, or the player already owns
    /// <see cref="MaxActives"/>. The second is unreachable in a live run — <c>RunSession.Start</c>
    /// refuses a tree holding more actives than this before the run is announced — and is kept for
    /// the reason <c>EffectRegistry.Apply</c>'s <c>KeyNotFoundException</c> is kept beside
    /// <c>SkillTree</c>'s <c>CanApply</c> sweep: a backstop that would otherwise be silence.
    /// </exception>
    /// <remarks>
    /// Take order is therefore the runner's order, on a fresh run and on a resumed one alike, which
    /// is what makes <see cref="Tick"/>'s walk reproducible from a seed.
    /// </remarks>
    public void Add(SkillSpec skill)
    {
        if (skill is null)
        {
            throw new ArgumentNullException(nameof(skill));
        }

        if (skill.Kind != SkillKind.Active)
        {
            throw new ArgumentException(
                $"'{skill.Id}' is a {skill.Kind} and the runner owns only Actives. Every other "
                    + "kind is already in force the moment its effects were applied.",
                nameof(skill));
        }

        if (TryIndexOf(skill.Id, out _))
        {
            throw new InvalidOperationException(
                $"'{skill.Id}' is already owned. A node is taken once (SkillTree's own gate), so a "
                    + "second Add is a caller that took it twice rather than a player who did.");
        }

        if (_count == MaxActives)
        {
            throw new InvalidOperationException(
                $"The runner holds {MaxActives} actives and '{skill.Id}' would be one more. A tree "
                    + "that could ask for this is refused at Start, so reaching here means one was "
                    + "built from something other than the class's tree.");
        }

        _specs[_count] = skill;
        _cooldowns[_count] = new Stat(skill.Active.Cooldown);
        _readyAt[_count] = 0f;

        _count++;
    }

    /// <summary>The id of the active at <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public ContentId IdAt(int index)
    {
        Require(index);

        return _specs[index].Id;
    }

    /// <summary>The node at <paramref name="index"/> — its text, its kind, its cast list.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public SkillSpec SpecAt(int index)
    {
        Require(index);

        return _specs[index];
    }

    /// <summary>Where <paramref name="id"/> sits in the walk order, if it is owned at all.</summary>
    /// <remarks>
    /// A linear scan rather than a dictionary, for <c>Targeter.IndexOf</c>'s reason: the array is
    /// at most <see cref="MaxActives"/> long and a dictionary would be an allocation at
    /// construction for a table with twelve entries in it.
    /// </remarks>
    public bool TryIndexOf(ContentId id, out int index)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_specs[i].Id.Equals(id))
            {
                index = i;

                return true;
            }
        }

        index = -1;

        return false;
    }

    /// <summary>
    /// The live cooldown of the active at <paramref name="index"/>, in seconds before the floor.
    /// </summary>
    /// <remarks>
    /// <b>The address M3-12's <c>ModifySkillCooldown</c> resolves to.</b> A per-skill cooldown is
    /// not a <c>PlayerStat</c> — that enum addresses one stat per member (M3-05 rule 4) and
    /// <em>"−15 % Consecrate cooldown"</em> names a skill, so it cannot be one. It is a primitive
    /// carrying a <see cref="ContentId"/> and a <see cref="Modifier"/>, and this plus
    /// <see cref="TryIndexOf"/> are the address its handler resolves through. They are here now so
    /// that task is a file rather than a refactor.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public Stat CooldownOf(int index)
    {
        Require(index);

        return _cooldowns[index];
    }

    /// <summary>Whether the active at <paramref name="index"/> may fire right now.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public bool IsReady(int index)
    {
        Require(index);

        return _now >= _readyAt[index];
    }

    /// <summary>
    /// How much of the cooldown at <paramref name="index"/> is left, as a fraction in
    /// <c>[0, 1]</c>: 1 the instant a cast starts, 0 once it is live again. What M3-10's radial
    /// fill draws.
    /// </summary>
    /// <remarks>
    /// Measured against the <em>current</em> effective cooldown rather than the one sampled at the
    /// start of the cast — see this class's remarks, and <see cref="ChargeSkill.CooldownFraction"/>
    /// for the same paragraph.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public float CooldownFraction(int index)
    {
        Require(index);

        float remaining = _readyAt[index] - _now;

        // Negated rather than `remaining <= 0f` so a non-finite clock reads as ready rather than as
        // a NaN fraction, which would leave a radial fill undrawable.
        if (!(remaining > 0f))
        {
            return 0f;
        }

        float cooldown = CooldownRules.Effective(_specs[index].Active.Cooldown, _cooldowns[index].Value);

        // The floor has already answered zero, negative and NaN, so the only value left that cannot
        // be divided by is an infinite one — a stack that made the wait unmeasurable. There is still
        // a real wait, because _readyAt was fixed when the cast started, and a full fill is the
        // honest way to draw one whose length cannot be measured. This is the half of
        // ChargeSkill.CooldownFraction's guard that survives having a floor in front of it.
        if (float.IsInfinity(cooldown))
        {
            return 1f;
        }

        float fraction = remaining / cooldown;

        return fraction < 1f ? fraction : 1f;
    }

    /// <summary>
    /// Advances the clock to <paramref name="now"/> and casts at most one skill.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>At most one, and the walk stops on it</b> (rule 6). Each entry is asked in turn — off
    /// cooldown, and its authored trigger met — and the first that answers yes is cast. There is no
    /// starvation, because a cast puts that skill on cooldown and the next tick reaches the next
    /// one: four ready-and-triggered actives all fire within four frames, ≈ 50 ms at 60 fps, which
    /// is below anything a player can time. Bounded per-frame work is AR §14's ask, and nothing in
    /// CC §6 or CH §4 asks for simultaneity — what they ask for is that Auto feel deliberate, and
    /// four skills detonating on one frame is the opposite of deliberate.
    /// </para>
    /// <para>
    /// <b>A non-finite <paramref name="now"/> casts nothing, and is not guarded against.</b> The
    /// snapshot is the door: <c>Dt</c> is clamped by <c>SnapshotBuilder</c> and
    /// <c>RunState.Time</c> is its sum (AR §18.2), so every other <c>Tick</c> in core trusts the
    /// clock it is handed and this one does too. What it does instead is spell the readiness test
    /// so the unreadable case takes the safe branch — see the loop.
    /// </para>
    /// </remarks>
    /// <param name="dt">
    /// Seconds since the previous tick. Deliberately unread, for the reason
    /// <see cref="ChargeSkill.Tick"/> and <see cref="Weapon.Tick"/> give: every schedule here is an
    /// absolute time against <paramref name="now"/>, which is the same clock and cannot drift the
    /// way a summed accumulator can. It stays in the signature because every <c>Tick</c> in core
    /// takes one.
    /// </param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    public void Tick(float dt, float now)
    {
        _now = now;

        for (int i = 0; i < _count; i++)
        {
            // `!(now >= ready)` rather than `now < ready`, and the difference is the whole of the
            // non-finite story: every comparison against NaN is false, so the natural spelling
            // would read an unreadable clock as *off cooldown* and cast — scheduling a NaN
            // _readyAt that no later comparison can be true against, which is a skill that fires
            // every tick for the rest of the run. AR §18.3.
            if (!(now >= _readyAt[i]))
            {
                continue;
            }

            if (!_specs[i].Active.Trigger.IsMet(_blackboard))
            {
                continue;
            }

            Fire(i, now, auto: true);

            return;
        }
    }

    /// <summary>
    /// Fires the active at <paramref name="index"/> regardless of its trigger — the door M3-07a's
    /// manual cast uses.
    /// </summary>
    /// <param name="index">Which owned active.</param>
    /// <param name="now">Simulated run time, in seconds.</param>
    /// <param name="auto">
    /// Whether the trigger asked for this rather than the player. Carried through to
    /// <see cref="SkillCast.WasAuto"/>, which is what lets CC §6.2 give a manual cast a haptic and
    /// an automatic one none.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when it is still cooling, or when <paramref name="now"/> is
    /// unreadable — and then nothing is applied and nothing is published.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public bool Cast(int index, float now, bool auto)
    {
        Require(index);

        _now = now;

        if (!(now >= _readyAt[index]))
        {
            return false;
        }

        Fire(index, now, auto);

        return true;
    }

    /// <summary>
    /// Back to rest: every owned active ready on the next tick, and none of them forgotten.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not what a stage boundary gets</b> (rule 11). M2-10's standing rule is that a boundary
    /// resets the <see cref="Targeter"/> and never <see cref="PlayerCombat"/> — a door is not a
    /// free heal, and it is not a free set of cooldowns either. Nothing in M3 calls this; it exists
    /// for the reason <see cref="Weapon.Reset"/> does, so the first thing that genuinely needs a
    /// fresh clock has one rather than inventing its own.
    /// </para>
    /// <para>
    /// The entries stay: a reset is a fresh cooldown clock, not a stripped character. The modifier
    /// stacks are left alone for <see cref="ChargeSkill.Reset"/>'s reason — this class puts nothing
    /// on them, so everything there belongs to some other source, and only that source knows
    /// whether it should still be there. The clock itself is deliberately not rewound: it is a
    /// reading of the caller's time rather than state this object owns.
    /// </para>
    /// </remarks>
    public void Reset()
    {
        for (int i = 0; i < _count; i++)
        {
            _readyAt[i] = 0f;
        }
    }

    /// <summary>
    /// Applies the cast effects, starts the cooldown, and says so — in that order (rule 9).
    /// </summary>
    private void Fire(int index, float now, bool auto)
    {
        SkillSpec spec = _specs[index];
        ActiveSpec active = spec.Active;

        // **With the ActiveSpec as the source, and deliberately not the SkillSpec** (rule 8).
        // M3-05 rule 7 says the source is whoever owns the effect, and `SkillTree.Take` already
        // passes the SkillSpec for the take effects — so a node that both grants a passive and
        // buffs on cast needs the two separable, `Stat.RemoveAll(source)` takes a whole source back
        // at once (M3-05 rule 6), and the two objects are already distinct. Nothing is timed yet:
        // the first cast effect with a duration is M3-11's, it owns its own clock, and
        // `EffectRegistry.Remove` is the door it calls.
        for (int i = 0; i < active.OnCast.Count; i++)
        {
            _effects.Apply(active.OnCast[i], active);
        }

        // Sampled here and held — see the class remarks. The floor is what stops a stack driving
        // this to zero or below (ADR-0008 clamps nothing), and it is taken from the *authored*
        // base rather than from Stat.Base so a node that re-based the cooldown could not raise its
        // own floor with it.
        float cooldown = CooldownRules.Effective(active.Cooldown, _cooldowns[index].Value);

        _readyAt[index] = now + cooldown;

        // Last, after the effects and after _readyAt moved, so a handler reading
        // CooldownFraction from inside this sees 1 and a view drawing a buff sees it applied.
        _events.Publish(new SkillCast(spec.Id, cooldown, auto));
    }

    private void Require(int index)
    {
        if (index < 0 || index >= _count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"Owned actives are indexed from 0 and this run owns {_count}.");
        }
    }
}

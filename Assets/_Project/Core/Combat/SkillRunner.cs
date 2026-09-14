using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    /// <summary>
    /// The most skills the player may hold as Manual at once — CC §6.2's four thumb positions.
    /// </summary>
    /// <remarks>
    /// <b>An ergonomic ceiling rather than a storage one</b>, which is why it is four against
    /// <see cref="MaxActives"/>' twelve and why it is not the same kind of number. CC §6.2 draws
    /// four fixed buttons beside the always-present movement one, and a fifth is not a bigger array
    /// but a thumb that cannot reach. The movement skill is not counted here and never will be
    /// (rule 6): it is <c>ChargeSkill</c>, which has no <c>SkillSpec</c>, no trigger and no entry in
    /// this class, so CC §6.2's <em>"4 manual slots, plus the always-present movement button"</em> is
    /// true by construction rather than by an exemption somebody has to remember.
    /// </remarks>
    public const int MaxManualSlots = 4;

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

    /// <summary>
    /// Whether each entry fires itself. True the moment it is added (rule 1).
    /// </summary>
    /// <remarks>
    /// <b>A fourth array beside the three rather than a table of its own</b> (rule 1). Cooldown,
    /// trigger and whether a skill fires itself are three facts about one skill, and the first two
    /// are already indexed by the same <c>i</c>: a second class holding the third would need this
    /// one's index to mean anything and would have to be handed the same <see cref="Add"/> twice.
    /// Sized <see cref="MaxActives"/> with the others, so nothing grows on a pick.
    /// </remarks>
    private readonly bool[] _isAuto = new bool[MaxActives];

    /// <summary>
    /// Which skill sits in each of CC §6.2's four thumb positions;
    /// <c>default(ContentId)</c> for an empty one.
    /// </summary>
    /// <remarks>
    /// <b>Its own short table and deliberately not a fifth array of <see cref="MaxActives"/></b>,
    /// because a slot is a position on a screen rather than a property of a skill: the question
    /// <em>"what is in S3?"</em> is asked by index and answered in one read, where a parallel array
    /// would make it a scan for the entry claiming 3. A skill is in at most one slot, and
    /// <see cref="_isAuto"/> is the other half of that pair — the two are kept in step by
    /// <see cref="SetAutoCast"/> being the only writer of either.
    /// </remarks>
    private readonly ContentId[] _slots = new ContentId[MaxManualSlots];

    /// <summary>
    /// <see cref="_slots"/> as the read-only view <see cref="Slots"/> hands out, wrapped once.
    /// </summary>
    /// <remarks>
    /// <c>TriggerSpec._clausesView</c>'s and <c>SkillTree._takenIdsView</c>'s shape, for their reason
    /// and one of timing: M3-10's HUD polls <see cref="Slots"/> to draw four buttons, so a fresh
    /// <c>Array.AsReadOnly</c> per call would be a per-frame allocation on the path AR §14 is about.
    /// The wrapper also stops the array being cast back to <c>ContentId[]</c> and written through,
    /// which would put a skill in a slot without moving <see cref="_isAuto"/> with it.
    /// </remarks>
    private readonly ReadOnlyCollection<ContentId> _slotsView;

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

        // Once, here, and never again — see the field. This is the only allocation this class makes
        // after its arrays, and it is made before a run has started rather than while one is drawn.
        _slotsView = Array.AsReadOnly(_slots);
    }

    /// <summary>How many actives the player owns.</summary>
    public int Count => _count;

    /// <summary>
    /// How many of CC §6.2's four slots are occupied, from 0 to <see cref="MaxManualSlots"/>.
    /// </summary>
    /// <remarks>
    /// <b>What the UI is required to ask before sending <see cref="SetAutoCast"/> with
    /// <c>auto: false</c></b> (rule 2). Counted rather than kept as a field, because the slot table
    /// is four entries long and a maintained counter would be a second source of truth that could
    /// disagree with it — the one failure this class cannot afford, since the disagreement would be
    /// a ceiling reached with a free slot beside it. Four iterations and no allocation.
    /// </remarks>
    public int ManualSlotCount
    {
        get
        {
            int occupied = 0;

            for (int slot = 0; slot < MaxManualSlots; slot++)
            {
                if (_slots[slot] != default)
                {
                    occupied++;
                }
            }

            return occupied;
        }
    }

    /// <summary>
    /// The four slots in thumb order, empty ones included as <c>default(ContentId)</c> — always
    /// <see cref="MaxManualSlots"/> long.
    /// </summary>
    /// <remarks>
    /// Always full length rather than compacted, because a slot is a position: CC §6.2 draws S1–S4
    /// at fixed places and does not draw the empty ones, so the reader needs to know <em>which</em>
    /// are empty and a shortened list cannot say. The same instance on every call — see
    /// <see cref="_slotsView"/>. <b>M3-07b is what writes this to disk</b>; nothing here persists.
    /// </remarks>
    public IReadOnlyList<ContentId> Slots => _slotsView;

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

        // **Auto is the default and takes no slot** (rule 1). CC §6.1: "every skill defaults to
        // Auto. A player who never opens the menu has a complete, playable game with one button."
        // Written rather than left to the array's own `false`, because the array is reused across
        // nothing — there is no Remove — but the line is where the rule is, and a reader should not
        // have to know which way round `bool`'s default runs to find it.
        _isAuto[_count] = true;

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

    /// <summary>
    /// Whether <paramref name="skillId"/> fires itself — true for every owned active until it is
    /// switched (rule 1).
    /// </summary>
    /// <param name="skillId">An owned active.</param>
    /// <exception cref="KeyNotFoundException">
    /// The runner does not hold <paramref name="skillId"/>. Loud rather than answering
    /// <see langword="true"/>, which would describe a skill that does not exist as one that
    /// auto-casts — <see cref="SetAutoCast"/>'s reason, and the same wiring mistake.
    /// </exception>
    public bool IsAuto(ContentId skillId)
    {
        if (!TryIndexOf(skillId, out int index))
        {
            throw NotOwned(skillId, nameof(IsAuto));
        }

        return _isAuto[index];
    }

    /// <summary>
    /// What is in one of CC §6.2's four slots, or <c>default(ContentId)</c> when it is empty.
    /// </summary>
    /// <param name="slot">Which thumb position, from 0. S1 is slot 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="slot"/> is not one of the <see cref="MaxManualSlots"/>.
    /// </exception>
    /// <remarks>
    /// <c>default(ContentId)</c> for an empty slot, which is the same "nobody" every id in this
    /// project spells that way — <c>RunConfig</c>'s default character, <c>Targeter</c>'s absent
    /// focus. An empty slot is a legal state and not an error; asking one to <em>fire</em> is
    /// (<see cref="CastSlot"/>).
    /// </remarks>
    public ContentId SlotAt(int slot)
    {
        RequireSlot(slot);

        return _slots[slot];
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
            // **Above the cooldown and above the trigger, and the order is a rule rather than an
            // optimisation** (rule 5). A Manual skill's authored condition is never evaluated, which
            // is how CC §6.5's "stop paying for a condition you have decided to judge yourself" is
            // met by construction: switching a skill to Manual is also how a player stops the game
            // asking a question they have taken over. Below the trigger test the skill would still
            // never auto-cast and every behavioural row would stay green — which is why
            // `Manual_TriggerIsNotEvaluated` is a separate row from `Manual_NeverAutoCasts`.
            if (!_isAuto[i])
            {
                continue;
            }

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
    /// Moves a skill between CC §6.1's two states: Auto fires itself, Manual never does and holds
    /// one of <see cref="MaxManualSlots"/> slots.
    /// </summary>
    /// <param name="skillId">An owned active.</param>
    /// <param name="auto">
    /// <see langword="true"/> to hand it back to the trigger, freeing its slot;
    /// <see langword="false"/> to put it under the player's thumb in the lowest free slot.
    /// </param>
    /// <exception cref="KeyNotFoundException">
    /// The runner does not hold <paramref name="skillId"/> (rule 7). A Passive, an Upgrade and a
    /// Keystone never reach this class at all (<see cref="Add"/>), so CC §6.1's <em>"passive skills
    /// have no toggle and no button"</em> needs no check of its own.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// All <see cref="MaxManualSlots"/> slots are occupied and <paramref name="auto"/> is
    /// <see langword="false"/> (rule 2). <b>The UI is required to ask <see cref="ManualSlotCount"/>
    /// first</b>, and CC §6.2's <em>"Manual slots full — which skill goes back to auto?"</em> is a
    /// prompt shown <em>instead of</em> sending this command; taking the answer is two ordinary
    /// calls, the victim to Auto and then the requested one to Manual. That is how <em>"never
    /// silently refuse, and never silently swap"</em> is met with no mechanism of its own: this
    /// class refuses a state it cannot reach, and the screen does the asking (M3-09). Nothing has
    /// moved and nothing is published when it throws.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Changeable at any time, including mid-cooldown, and it costs nothing</b> (rule 8, CH §4.3,
    /// CC §6.3). Neither <c>_readyAt</c> nor the cooldown <see cref="Stat"/> is touched here: a
    /// skill switched to Manual two seconds into an eight-second wait is ready at the same instant
    /// it always was, and switching back does not restart it. The tree is acquired during play, so
    /// the management screen has to work during play — and a switch that reset a cooldown would make
    /// opening that screen a tactical decision, which is the opposite of what CC §6.3 wants it to be.
    /// </para>
    /// <para>
    /// <b>The lowest free slot, and freeing one moves nothing else</b> (rule 3). CC §6.2 draws four
    /// fixed thumb positions, so compacting on a removal would slide S3's skill under the thumb that
    /// had learned S2 — a silent re-bind of muscle memory as the reward for dropping a skill.
    /// </para>
    /// </remarks>
    public void SetAutoCast(ContentId skillId, bool auto)
    {
        if (!TryIndexOf(skillId, out int index))
        {
            throw NotOwned(skillId, nameof(SetAutoCast));
        }

        // **Idempotent in both directions, and the false one is why this is first** (rule 9). Auto
        // twice is the harmless case the rule names; Manual twice is the dangerous one, because
        // without this line it would take a *second* slot for one skill — ManualSlotCount would
        // reach four with two skills owned and the ceiling above would throw for a reason no screen
        // could explain. AR §8: an event describes what happened, and nothing happened here.
        if (_isAuto[index] == auto)
        {
            return;
        }

        int slot;

        if (auto)
        {
            slot = SlotOf(skillId);

            _slots[slot] = default;
            _isAuto[index] = true;

            // −1 rather than the slot it just left: the event says where the skill *is*, and an Auto
            // skill is in no slot. M3-09's list and M3-10's buttons both redraw from this.
            slot = -1;
        }
        else
        {
            slot = FirstFreeSlot();

            if (slot < 0)
            {
                throw new InvalidOperationException(
                    $"All {MaxManualSlots} manual slots are occupied and '{skillId}' would be a "
                        + $"fifth. ManualSlotCount is {ManualSlotCount}: ask it before sending this "
                        + "command, and show CC §6.2's \"which skill goes back to auto?\" instead. "
                        + "Taking that answer is two calls, the victim to Auto and then this one.");
            }

            _slots[slot] = skillId;
            _isAuto[index] = false;
        }

        // Published after the change, carrying the slot, so a listener redrawing from it needs no
        // second read (rule 9).
        _events.Publish(new SkillAutoCastChanged(skillId, auto, slot));
    }

    /// <summary>
    /// Casts whatever is in one of CC §6.2's four slots, regardless of its trigger — the player
    /// pressed S1–S4.
    /// </summary>
    /// <param name="slot">Which thumb position, from 0. S1 is slot 0.</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <returns>
    /// <see langword="false"/> when it is still cooling, which is an ordinary early tap rather than
    /// an error: CC §6.2 answers one with 40 % opacity and no tap response rather than with a buffer
    /// (rule 4). <see langword="false"/> too when <paramref name="now"/> is unreadable — see
    /// <see cref="Cast"/>, which is the door this forwards to and where that is spelled.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="slot"/> is not one of the <see cref="MaxManualSlots"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The slot is empty. CC §6.2 does not draw a button for an empty slot, so a command from one is
    /// a wiring mistake rather than a player action — <c>IPlayerCommands</c>' own standing rule,
    /// <em>"a silent no-op would hide it"</em>. Distinct from cooling, which is a legitimate
    /// <see langword="false"/> above.
    /// </exception>
    public bool CastSlot(int slot, float now)
    {
        RequireSlot(slot);

        ContentId skillId = _slots[slot];

        if (skillId == default)
        {
            throw new InvalidOperationException(
                $"Slot {slot} is empty and has no button on screen. CC §6.2 draws only the occupied "
                    + "slots, so a cast from an empty one is a view sending a command for a control "
                    + "it is not drawing rather than a player pressing anything.");
        }

        // Owned by construction: SetAutoCast is the only writer of _slots and it refuses an id the
        // runner does not hold, so this cannot miss. Asked through TryIndexOf rather than cached
        // beside the slot for TriggerSpec's reason — one place owns the mapping from id to entry,
        // and a second copy is the first thing that could disagree with it.
        TryIndexOf(skillId, out int index);

        // `auto: false`, which is the whole difference a listener can see: CC §6.2 gives a manual
        // cast a haptic and an automatic one none, and SkillCast.WasAuto is how it tells them apart.
        return Cast(index, now, auto: false);
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
    /// <para>
    /// <b>The Auto/Manual flags and the slot table are untouched too</b>, and for the same sentence:
    /// where a skill sits under the player's thumb is part of the character rather than part of the
    /// clock, and a reset that silently emptied four slots would be the loadout equivalent of the
    /// free heal M2-10 refuses at a boundary.
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

    /// <summary>The lowest slot holding nobody, or −1 when all four are taken.</summary>
    private int FirstFreeSlot()
    {
        for (int slot = 0; slot < MaxManualSlots; slot++)
        {
            if (_slots[slot] == default)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>
    /// Which slot <paramref name="skillId"/> occupies. Only ever called for a skill already known to
    /// be Manual, which is what makes the fall-through unreachable.
    /// </summary>
    private int SlotOf(ContentId skillId)
    {
        for (int slot = 0; slot < MaxManualSlots; slot++)
        {
            if (_slots[slot] == skillId)
            {
                return slot;
            }
        }

        // Unreachable: _isAuto and _slots are written together and only by SetAutoCast, so a skill
        // whose flag says Manual is in a slot. Kept as a backstop rather than as a possibility, for
        // the reason Add's capacity throw is kept — the alternative to a loud contradiction here is
        // a silent `_slots[-1] = default`, which is an IndexOutOfRangeException a frame later with
        // nothing to say where it came from.
        throw new InvalidOperationException(
            $"'{skillId}' is flagged Manual but holds no slot. The flag and the table are written "
                + "together by SetAutoCast and nothing else writes either, so they cannot disagree.");
    }

    private KeyNotFoundException NotOwned(ContentId skillId, string member)
    {
        return new KeyNotFoundException(
            $"{member}: the runner does not own '{skillId}'. Only an Active reaches this class, so "
                + "this is a Passive, an Upgrade, a Keystone, or a node nobody has taken — none of "
                + $"which has a toggle or a button. This run owns {_count} active(s).");
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

    private static void RequireSlot(int slot)
    {
        if (slot < 0 || slot >= MaxManualSlots)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                $"Manual slots are indexed from 0 and there are {MaxManualSlots} of them — S1 is "
                    + "slot 0. This is a screen addressing a button CC §6.2 does not draw.");
        }
    }
}

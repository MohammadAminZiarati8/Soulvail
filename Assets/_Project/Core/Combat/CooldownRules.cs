using System;

namespace Soulvail.Core.Combat;

/// <summary>
/// CH §4.1's floor, as arithmetic: the shortest a cooldown is allowed to become, however many
/// nodes are stacked on it. Pure, static and stateless — it is a function, and the only reason it
/// is a type is that C# needs one.
/// </summary>
/// <remarks>
/// <para>
/// <b>A static class rather than a member of <see cref="SkillRunner"/>, because two things obey
/// it.</b> <see cref="SkillRunner"/> floors an active's cooldown and <see cref="ChargeSkill"/>
/// floors the dash's, and two copies of a floor is how one of them stops being the floor — the
/// first time M3-12 authors a node that reduces one of them, the copy nobody remembered is a
/// cooldown that can reach zero. <see cref="FocusResolver"/>'s shape and its reason: AR §7 bans
/// static <em>mutable</em> state, and there is none here.
/// </para>
/// <para>
/// <b>The floor is taken from the <em>authored</em> base, never from <c>Stat.Base</c>.</b> The two
/// are the same number today and the day a node re-bases a cooldown they are not — and a floor
/// computed from a base a node just raised would rise with it, which is a floor that stops
/// flooring. The authored number lives on <c>ActiveSpec.Cooldown</c> and
/// <c>MovementSkillSpec.Cooldown</c>, which is exactly the split those two types already make: the
/// spec stays what a designer typed, the <c>Stat</c> is what the run is currently playing.
/// </para>
/// <para>
/// <b>CH §4.1's word <em>multiplicative</em> is about how a cooldown node is authored, not about
/// what this class does.</b> A "−15 % cooldown" node carries <c>ModifierKind.PercentMult</c>
/// (M3-05 rule 8 reserves it for exactly this kind of loud multiplier); this reads the number the
/// stack produced and clamps it, and would behave identically if a designer authored
/// <c>PercentAdd</c>. What the word buys is the <em>cost</em>: the Charge at 2.5 s floors at 1.0 s,
/// and reaching it takes <b>six</b> −15 % multiplicative nodes (0.85⁵ = 0.444 → 1.11 s, above;
/// 0.85⁶ = 0.377 → 0.94 s, floored) against <b>four</b> additive ones. An 8 s active floors at
/// 3.2 s. M3-12 authors against those numbers, and that the multiplicative route costs half again
/// as many picks is the point of the word.
/// </para>
/// </remarks>
public static class CooldownRules
{
    /// <summary>
    /// The least of its authored self a cooldown may be reduced to — CH §4.1's <em>"floored at
    /// 40 % of base"</em>.
    /// </summary>
    public const float FloorFraction = 0.4f;

    /// <summary>
    /// The shortest <paramref name="live"/> is allowed to be: never under
    /// <see cref="FloorFraction"/> of <paramref name="authoredBase"/>.
    /// </summary>
    /// <param name="authoredBase">
    /// What a designer typed — <c>ActiveSpec.Cooldown</c> or <c>MovementSkillSpec.Cooldown</c>.
    /// Finite and greater than zero.
    /// </param>
    /// <param name="live">
    /// What the modifier stack currently makes of it — a <c>Stat.Value</c>. Anything at all,
    /// including the nonsense a stack can legitimately produce: <c>Stat</c> clamps nothing
    /// (ADR-0008), so a <c>PercentMult</c> of −1 drives a cooldown to zero and a deeper stack
    /// drives it below, and this is the layer that says no.
    /// </param>
    /// <returns>
    /// <paramref name="live"/> when it is above the floor, and the floor otherwise — including
    /// when <paramref name="live"/> is zero, negative or NaN. An infinite
    /// <paramref name="live"/> is returned unchanged: a stack that made the wait unmeasurable has
    /// not earned having it shortened to 40 %, and the callers draw a full fill for it rather than
    /// dividing by it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="authoredBase"/> is not a finite number greater than zero. That is a content
    /// mistake rather than a stack, and <c>ActiveSpec</c> already refuses one at the door
    /// (M3-02a rule 4) — this is the second end of the same rule, for the callers that hand over a
    /// number from somewhere else.
    /// </exception>
    /// <remarks>
    /// Spelled <c>!(live &gt; floor)</c> rather than <c>live &lt; floor</c>, which is AR §18.3's
    /// discipline and load-bearing here: every comparison against NaN is false, so the natural
    /// spelling would wave a NaN cooldown straight through and schedule a <c>_readyAt</c> that no
    /// later comparison can ever be true against — a skill that silently never fires again.
    /// </remarks>
    public static float Effective(float authoredBase, float live)
    {
        // !(value > 0f) rather than value <= 0f so NaN is refused too, and infinity asked about
        // separately because it passes a > 0 test. ActiveSpec's spelling, one layer out.
        if (!(authoredBase > 0f) || float.IsInfinity(authoredBase))
        {
            throw new ArgumentOutOfRangeException(
                nameof(authoredBase),
                authoredBase,
                "An authored cooldown must be a finite number greater than zero — the floor is a "
                    + "fraction of it, and there is no such thing as 40 % of nothing.");
        }

        float floor = FloorFraction * authoredBase;

        return !(live > floor) ? floor : live;
    }
}

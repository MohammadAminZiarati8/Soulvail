using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// The skills module's domain events. Event structs are grouped per module — the one accepted
// exception to one type per file, because an event is three lines and reading a module's
// vocabulary in one place is worth more than the rule. `RunEvents.cs`' precedent. See AR §5, §8 and
// <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// One member today, and the file exists rather than the struct joining `ProgressionEvents.cs`
// because a cast is a *combat* fact and a level is a progression one: M3-07a's Auto/Manual toggle
// and M3-10's four buttons both publish from here, and neither has anything to say about
// experience.

/// <summary>
/// An owned active fired.
/// </summary>
/// <remarks>
/// <para>
/// <b>Published last</b> — after <c>ActiveSpec.OnCast</c> has been applied and after the cooldown
/// has started (M3-06 rule 9). A handler reading <c>RunState.SkillCooldownFraction</c> from inside
/// it therefore sees 1 and a view drawing a buff sees it already in force, rather than a frame of
/// each being a lie. <c>NodeTaken</c>'s ordering, and the same reason.
/// </para>
/// <para>
/// It says what happened and never what to do, and carries only what a listener cannot look up for
/// itself — AR §8. A <c>readonly struct</c> with public readonly fields and a constructor: no
/// properties, no logic, and nothing to allocate when it crosses <c>IDomainEvents</c> by <c>in</c>.
/// </para>
/// </remarks>
public readonly struct SkillCast
{
    /// <summary>Which active fired — the node's id, e.g. <c>skill.oathbound.consecrate</c>.</summary>
    public readonly ContentId SkillId;

    /// <summary>
    /// Seconds until it may fire again, as the floor left it.
    /// </summary>
    /// <remarks>
    /// The <b>effective</b> cooldown rather than the authored one, because that is what a radial
    /// fill is over: a listener drawing the wait from <c>ActiveSpec.Cooldown</c> would draw a bar
    /// that empties early on every skill a node has touched. <c>CooldownRules.Effective</c> is the
    /// number, so it is never under 40 % of what a designer typed.
    /// </remarks>
    public readonly float Cooldown;

    /// <summary>
    /// Whether the trigger fired it rather than the player.
    /// </summary>
    /// <remarks>
    /// True for everything in this task — M3-07a is what gives a skill a Manual mode and a button
    /// to be pressed from. It is on the event rather than derived because the two want different
    /// feedback: CC §6.2 gives a manual cast the haptic and the auto one none, and a listener
    /// cannot tell them apart from the id.
    /// </remarks>
    public readonly bool WasAuto;

    public SkillCast(ContentId skillId, float cooldown, bool wasAuto)
    {
        SkillId = skillId;
        Cooldown = cooldown;
        WasAuto = wasAuto;
    }
}

using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// The skills module's domain events. Event structs are grouped per module — the one accepted
// exception to one type per file, because an event is three lines and reading a module's
// vocabulary in one place is worth more than the rule. `RunEvents.cs`' precedent. See AR §5, §8 and
// <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// Two members, which is the file's own prediction met: a cast is a *combat* fact and a level is a
// progression one, so M3-07a's Auto/Manual toggle landed here beside `SkillCast` rather than in
// `ProgressionEvents.cs`, and neither has anything to say about experience.

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

/// <summary>
/// A skill's CC §6.1 switch moved: it now fires itself, or it now waits for a thumb.
/// </summary>
/// <remarks>
/// <para>
/// <b>Published after the change and only when something moved</b> (M3-07a rule 9).
/// <c>SetAutoCast(id, true)</c> on a skill already Auto publishes nothing, because AR §8 says an
/// event describes what happened and nothing did. M3-09's list and M3-10's four buttons both redraw
/// from this, which is why it carries the slot rather than making each of them read it back.
/// </para>
/// <para>
/// It says nothing about <em>why</em> the switch moved — a player tapping the toggle and a screen
/// taking CC §6.2's "which skill goes back to auto?" answer produce the same event, because from
/// here they are the same thing: two ordinary commands.
/// </para>
/// </remarks>
public readonly struct SkillAutoCastChanged
{
    /// <summary>Which owned active moved.</summary>
    public readonly ContentId SkillId;

    /// <summary>Whether it now fires itself.</summary>
    public readonly bool IsAuto;

    /// <summary>
    /// Which of CC §6.2's four slots it now holds, from 0 — and <c>−1</c> when it is Auto.
    /// </summary>
    /// <remarks>
    /// −1 rather than the slot it just vacated: this says where the skill <em>is</em>, and an Auto
    /// skill is in no slot. A listener redrawing S1–S4 clears whichever button it had been in, which
    /// it knows without being told.
    /// </remarks>
    public readonly int Slot;

    public SkillAutoCastChanged(ContentId skillId, bool isAuto, int slot)
    {
        SkillId = skillId;
        IsAuto = isAuto;
        Slot = slot;
    }
}

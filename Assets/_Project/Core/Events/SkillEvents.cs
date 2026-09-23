using System.Numerics;
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
/// An ability was cast through its cooldown and the Veil was charged for it — CH §3.3's Emberwright
/// (M6-07c rule 7). Published by <c>SkillRunner</c> immediately after the <see cref="SkillCast"/> it
/// paid for.
/// </summary>
/// <remarks>
/// <b>Beside <see cref="SkillCast"/> rather than a flag on it</b>: every subscriber of that event
/// would otherwise have to learn a field that is false in every run of two classes out of three.
/// </remarks>
public readonly struct CastBought
{
    /// <summary>Which active was bought.</summary>
    public readonly ContentId SkillId;

    /// <summary>What it cost, in Veilrot.</summary>
    public readonly float Cost;

    /// <summary>The meter after the price — so a reader needs no second read.</summary>
    public readonly float VeilrotAfter;

    public CastBought(ContentId skillId, float cost, float veilrotAfter)
    {
        SkillId = skillId;
        Cost = cost;
        VeilrotAfter = veilrotAfter;
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

/// <summary>
/// A zone was placed — CC §6.4's Consecrate, and every zone after it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries everything a decal needs and nothing that can be looked up</b> — position, radius
/// and duration, so a view can place it, size it and run its own countdown without a read
/// (<c>SpawnTelegraphed</c>'s shape, M2-12b). Published after the zone exists, so a listener reading
/// <c>RunState.ActiveZoneCount</c> from inside it counts this one.
/// </para>
/// <para>
/// <b>An id, and deliberately not an index</b> (M3-11b, the spec's <c>Index</c> corrected). A zone
/// retiring shifts the ones placed after it down, exactly as <c>TimedEffects</c> does, so an index is
/// a position in a list at one instant rather than a handle: a view keyed by it would retire the
/// wrong decal the first time two zones overlapped and the older one ended. The id is issued from 1
/// and never reused within a run, which is <c>ProjectileFired.Id</c>'s rule and
/// <c>EnemyRegistry</c>'s.
/// </para>
/// </remarks>
public readonly struct ZoneSpawned
{
    /// <summary>Which zone — run-stable, issued from 1.</summary>
    public readonly int Id;

    /// <summary>Where it was placed, in world metres. Where the player stood at the cast.</summary>
    public readonly Vector3 Position;

    /// <summary>How far it reaches, in metres.</summary>
    public readonly float Radius;

    /// <summary>How long it stands, in simulated seconds.</summary>
    public readonly float Duration;

    public ZoneSpawned(int id, Vector3 position, float radius, float duration)
    {
        Id = id;
        Position = position;
        Radius = radius;
        Duration = duration;
    }
}

/// <summary>
/// A zone's time is up. What retires the view.
/// </summary>
/// <remarks>
/// Published after the zone is gone, and after the last pulse it was alive for — the two clocks meet
/// exactly on an authored zone's final second, and <c>ZoneSystem</c> rules that the pulse lands first.
/// </remarks>
public readonly struct ZoneExpired
{
    /// <summary>Which zone — the id <c>ZoneSpawned</c> carried.</summary>
    public readonly int Id;

    public ZoneExpired(int id)
    {
        Id = id;
    }
}

/// <summary>
/// A pulse landed on somebody standing in a zone.
/// </summary>
/// <remarks>
/// <b>It carries what was <em>actually</em> restored, which is zero at full health and zero for the
/// dead</b> — <c>Health.Heal</c>'s own return value, not what the zone was authored to pay. A view
/// that flashed on a wasted pulse would be lying about what the skill did; one that was never told
/// about it could not tell a player standing in a zone at full health from a player standing outside
/// one.
/// </remarks>
public readonly struct ZoneHealed
{
    /// <summary>Which zone — the id <c>ZoneSpawned</c> carried.</summary>
    public readonly int Id;

    /// <summary>Hit points actually restored. Zero is legal and is the honest answer.</summary>
    public readonly float Amount;

    public ZoneHealed(int id, float amount)
    {
        Id = id;
        Amount = amount;
    }
}

/// <summary>
/// A burning zone took hit points off somebody. <see cref="ZoneHealed"/>'s mirror, and a separate
/// struct for its reason: a subscriber that flashes the player is not the one that flashes an enemy.
/// </summary>
/// <remarks>
/// <b>One per enemy damaged, and none for a pulse that reached nobody</b> (M6-07b rule 7) — the
/// opposite of <see cref="ZoneHealed"/>, which is published at zero. A heal has one subject who is
/// always there, so zero is an answer; a burn has none, so an empty pool is an absence. Published
/// after <c>EnemyDamaged</c>, so a subscriber reading the enemy's health sees the burn applied.
/// </remarks>
public readonly struct ZoneBurned
{
    /// <summary>Which zone — the id <c>ZoneSpawned</c> carried.</summary>
    public readonly int Id;

    /// <summary>Who it burned — the enemy's registry id.</summary>
    public readonly int EnemyId;

    /// <summary>Hit points actually taken — <c>DamageResult.Applied</c>, not what was authored.</summary>
    public readonly float Amount;

    public ZoneBurned(int id, int enemyId, float amount)
    {
        Id = id;
        EnemyId = enemyId;
        Amount = amount;
    }
}

namespace Soulvail.Core.Combat;

/// <summary>
/// The player's half of AR §9: everything the character currently perceives about its own fight,
/// in one typed object that a trigger condition can read as a one-line predicate. One per run,
/// owned by <see cref="PlayerCombat"/>, written once a tick and read by everything that has to
/// decide something. See AR §9, CC §6.4 and ADR-0005.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> CC §6.4 gives every active skill a designer-authored auto-cast
/// condition — "player HP &lt; 60 %", "≥3 enemies within 6 m", "an enemy projectile is inbound" —
/// and ADR-0005 says those conditions are pure predicates over this object, carried by the
/// <c>SkillSpec</c> as data. That is why the fields are the *questions a designer asks* rather
/// than the state core happens to hold: <see cref="HpFraction"/> rather than current and maximum
/// HP, <see cref="EnemiesWithin6m"/> rather than a list of enemies. A predicate that had to
/// compute either would be code, and CC §6.4 is explicit that these are authored.
/// </para>
/// <para>
/// <b>Written by one thing.</b> <see cref="PlayerCombat.Tick"/> fills every field of it, every
/// tick, from state it owns. Nothing else writes here and nothing stores anything here that it
/// could not recover otherwise — this is a view onto the fight, not a place to keep things. The
/// same write discipline <c>EnemyBlackboard</c> has, and the same reason: a blackboard with two
/// writers is a bus, and ADR-0005 says it is not one.
/// </para>
/// <para>
/// Public mutable fields, the same deliberate exception <c>WorldSnapshot</c> and
/// <c>EnemyBlackboard</c> take. This is rewritten in full every frame by one writer, properties
/// would add a call per field per frame and hide nothing, and the no-public-fields rule exists to
/// stop Unity components leaking their innards — there is no component within reach of this type.
/// </para>
/// <para>
/// <b>Two fields are placeholders, on purpose.</b> <see cref="Veilrot"/> and
/// <see cref="IncomingProjectiles"/> are zero until M6-04 and M2-07 respectively, and
/// <see cref="PlayerCombat"/> deliberately does not touch them — a field written to zero every
/// tick by something that does not know the answer is worse than one that is honestly untouched,
/// because the first cannot be filled in by the system that eventually learns it. They are here
/// now because AR §9 names them and because a skill authored against a blackboard that lacks them
/// would have to be re-authored.
/// </para>
/// </remarks>
public sealed class CombatBlackboard
{
    /// <summary>
    /// Current HP over the live maximum, in <c>[0, 1]</c>. CC §6.4's Consecrate fires below 0.6.
    /// </summary>
    public float HpFraction;

    /// <summary>
    /// Shield points over the shield's maximum, in <c>[0, 1]</c>; zero for a class without one,
    /// which is every class but the Oathbound.
    /// </summary>
    public float ShieldFraction;

    /// <summary>
    /// Living enemies within 6 m — CC §6.4's Sever trigger ("≥3 enemies within 6 m") and GD §8.1's
    /// clustering pressure seen from the player's side.
    /// </summary>
    public int EnemiesWithin6m;

    /// <summary>
    /// Living enemies within 8 m, which is the Censer's cone range (CC §7): "how many could I
    /// actually hit right now".
    /// </summary>
    public int EnemiesWithin8m;

    /// <summary>
    /// Living enemies within the class's acquire range — 12 m for the Oathbound. The widest of the
    /// three counts, and the one that answers "am I in a fight at all".
    /// </summary>
    public int EnemiesInAcquireRange;

    /// <summary>
    /// The enemy the character is facing, or −1 for none. The same id
    /// <see cref="Targeter.CurrentTargetId"/> carries, so nothing has to hold two answers.
    /// </summary>
    public int CurrentTargetId = -1;

    /// <summary>
    /// <see cref="CurrentTargetId"/> cannot be damaged right now — CC §3.6's "go around". Facing is
    /// held on it and the weapon should not claim it is hitting anything.
    /// </summary>
    public bool IsTargetBlocked;

    /// <summary>
    /// The player is holding a tap-to-focus target (CC §3.4). Not the same as
    /// <see cref="CurrentTargetId"/> being the focused one: a focused enemy that has walked out of
    /// range keeps the focus while scoring picks something else to shoot.
    /// </summary>
    public bool HasFocus;

    /// <summary>
    /// Seconds the stick has been continuously centred. Reset to zero by any frame with input, so
    /// this is "how long have I been standing still", not "how long since I last moved".
    /// </summary>
    public float StationaryTime;

    /// <summary>
    /// Veilrot, GD §13's corruption meter. Zero until M6-04 owns it — see the class remarks.
    /// </summary>
    public float Veilrot;

    /// <summary>
    /// Enemy projectiles currently inbound — CC §6.4's Bulwark trigger. Zero until M2-07 gives
    /// something the means to fire one.
    /// </summary>
    public int IncomingProjectiles;

    /// <summary>
    /// Back to a blank blackboard: every field to its default, and no target rather than enemy
    /// zero.
    /// </summary>
    /// <remarks>
    /// <see cref="CurrentTargetId"/> goes to −1, not to the <see cref="int"/> default, for the
    /// reason <c>EnemyRegistry</c> starts its ids at one: −1 is how "nobody" is spelled everywhere
    /// in this project, and a reader that checked <c>&gt;= 0</c> would read a zeroed field as a
    /// live target. The declaration carries the same initialiser, so a freshly constructed
    /// blackboard already agrees.
    /// </remarks>
    public void Reset()
    {
        HpFraction = 0f;
        ShieldFraction = 0f;
        EnemiesWithin6m = 0;
        EnemiesWithin8m = 0;
        EnemiesInAcquireRange = 0;
        CurrentTargetId = -1;
        IsTargetBlocked = false;
        HasFocus = false;
        StationaryTime = 0f;
        Veilrot = 0f;
        IncomingProjectiles = 0;
    }
}

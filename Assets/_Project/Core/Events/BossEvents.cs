namespace Soulvail.Core.Events;

// A boss fight's domain events. Grouped per module like StageEvents, SpawnEvents and EnemyEvents,
// for the same reason: an event is three lines, and reading a module's vocabulary in one place is
// worth more than one type per file. See AR §5, §8 and
// <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// Three events and no fourth, and the two gaps are deliberate. There is no `BossSpawned` — a boss
// is an ordinary agent and `EnemySpawned` already announces it, which is the whole of M4-01b rule
// 2; and `EnemySpawned` gains no `IsBoss` flag either (rule 7), because M3-13b's `IsElite` is a
// *look* and a boss is not one. What a view actually needs is how many phases the fight has, and
// that rides on `BossPhaseChanged` rather than on a spawn. There is no `BossDied` either:
// `EnemyDied` is that, with the id this event has been naming all fight.

/// <summary>
/// A boss has entered a new phase — GD §9.1 rule 3. Published on the tick the threshold is crossed,
/// immediately before <see cref="BossBeatStarted"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Also published once when the boss stands up, for phase 0.</b> A fight announces the phase it
/// starts in as well as every one it crosses into, so that a view is told how many segments its bar
/// has before the first threshold rather than three-quarters of the way through the fight. That is
/// what makes rule 7's *"what a view needs is the phase count"* true in practice: the event arrives
/// at the same moment <c>EnemySpawned</c> does, and carries the number a flag on the spawn would
/// have carried.
/// </para>
/// <para>
/// <see cref="Phase"/> is 0-based, like <c>BossPhases.Current</c>, and a readout that says
/// <em>"2 of 3"</em> adds the one. Kept 0-based on the wire rather than adjusted here, so that the
/// number a view draws and the number a test asserts cannot be off by one in opposite directions.
/// </para>
/// </remarks>
public readonly struct BossPhaseChanged
{
    /// <summary>Which enemy — the boss's <c>EnemyAgent.Id</c>, the same one every other event names.</summary>
    public readonly int EnemyId;

    /// <summary>The phase just entered, numbered from 0.</summary>
    public readonly int Phase;

    /// <summary>How many phases the fight has in total.</summary>
    public readonly int OfPhases;

    public BossPhaseChanged(int enemyId, int phase, int ofPhases)
    {
        EnemyId = enemyId;
        Phase = phase;
        OfPhases = ofPhases;
    }
}

/// <summary>
/// The invulnerable beat between two phases has opened: the boss takes no damage and does nothing,
/// and whatever it had summoned is already gone.
/// </summary>
/// <remarks>
/// <b>The adds are cleared on this event's tick, not at the beat's end</b> (rule 4). The point of
/// the beat is to reset pressure <em>now</em> — a player who has just been told the fight is
/// changing should see the arena empty immediately, rather than keep fighting Husks around a boss
/// they cannot hurt.
/// </remarks>
public readonly struct BossBeatStarted
{
    /// <summary>Which enemy.</summary>
    public readonly int EnemyId;

    /// <summary>How long the beat lasts, in simulated seconds — the boss's authored <c>BeatSeconds</c>.</summary>
    /// <remarks>
    /// Carried rather than looked up, for <c>SpawnTelegraphed.FiresAt</c>'s reason: whatever draws
    /// the telegraph holds a look book and not the catalog, and a ring that has to ask core how long
    /// it has is a ring that can disagree with the fight about it.
    /// </remarks>
    public readonly float Seconds;

    public BossBeatStarted(int enemyId, float seconds)
    {
        EnemyId = enemyId;
        Seconds = seconds;
    }
}

/// <summary>The beat is over: the boss can be hurt again and resumes acting.</summary>
/// <remarks>
/// Published on the first tick the beat is no longer running, whether or not anything was watching —
/// a view has to put the shield away either way, which is <c>EnemyExploded</c>'s and
/// <c>ProjectileImpacted</c>'s reasoning. It is <em>not</em> published when the boss dies during a
/// beat: nothing can hurt it then, so a beat cannot end that way.
/// </remarks>
public readonly struct BossBeatEnded
{
    /// <summary>Which enemy.</summary>
    public readonly int EnemyId;

    public BossBeatEnded(int enemyId)
    {
        EnemyId = enemyId;
    }
}

using System.Collections.Generic;
using System.Numerics;

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
//
// Five more as of M4-02, and they are a *hazard's* vocabulary rather than a fight's: a ring
// leaving a point, a ring that is over, a crack opening, biting and closing. They live here
// because the only thing that makes one is a boss (GD §9.2's Warden), and they are deliberately
// self-sufficient — `ShockwaveEmitted` carries the origin, the speed and the ceiling, so whatever
// draws the ring integrates its own radius rather than asking core where the ring is this frame.
// That is what keeps `ShockwaveSystem` and `FissureSystem` off `RunState` entirely, unlike
// `ZoneSystem`, which a decal reads two scalars from (M3-11c).

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
/// <para>
/// <b><see cref="EntersBelow"/> is M4-04's, and it is the same sentence as <see cref="OfPhases"/>
/// one step further.</b> A segmented bar needs the segment <em>count</em> to know how many marks to
/// draw and the thresholds themselves to know <em>where</em> — M4-04 rule 4 puts the marks at the
/// phases' own values rather than at even spacing, so that a bar for 1.0 / 0.8 / 0.25 is right
/// rather than merely plausible. Nothing in <c>Soulvail.Game</c> can reach a <c>BossSpec</c>: the
/// only handle a view has on a fight is this event's <see cref="EnemyId"/>, and a HUD holding a
/// <c>ContentCatalog</c> to reverse-look-up the boss whose body an <c>EnemySpawned</c> named would
/// be the seam <c>EnemySpawned.IsElite</c>'s own remarks refuse. So the fact rides the event, which
/// is what <c>ShieldGranted.Total</c>, <c>EnemyDamaged.HpFraction</c>, <c>EnemyTelegraph.Duration</c>
/// and <c>BossBeatStarted.Seconds</c> all do for the same reason.
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

    /// <summary>
    /// Every phase's own <c>BossPhaseSpec.EntersBelow</c>, outermost first — so the first entry is
    /// always 1 and each one after it is strictly lower. <see langword="null"/> when nobody said.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A reference rather than a copy, and it allocates nothing to publish.</b>
    /// <c>BossPhases</c> wraps its own threshold array once at construction and hands the same
    /// read-only view out for the whole fight; this event carries that reference. A fight publishes
    /// this once at phase 0 and once per crossing — never per tick — and even then nothing here is
    /// built. The collection is read-only because the thresholds are the asset's and a view has no
    /// business editing them.
    /// </para>
    /// <para>
    /// <b>Defaulted, on <c>EnemySpawned.IsElite</c>'s terms rather than beside them.</b> That field's
    /// default is legal because <c>false</c> <em>is</em> not-an-Elite; this one's is legal because
    /// <see langword="null"/> <em>is</em> <em>"no thresholds were supplied"</em>, which is what a
    /// fixture staging a bare phase change is saying, and a readout handed one draws its segments
    /// evenly or not at all rather than inventing values. Every publisher in a live run supplies it.
    /// </para>
    /// </remarks>
    public readonly IReadOnlyList<float> EntersBelow;

    public BossPhaseChanged(int enemyId, int phase, int ofPhases, IReadOnlyList<float> entersBelow = null)
    {
        EnemyId = enemyId;
        Phase = phase;
        OfPhases = ofPhases;
        EntersBelow = entersBelow;
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

/// <summary>
/// A shield-slam's ring has left the ground — GD §9.2's Warden, M4-02 rule 2. Published on the
/// tick the slam's windup completed, from the point the body was standing on at that instant.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries everything a ring is, so nothing has to ask core where one is this frame.</b>
/// A radius is <c>Speed × (now − this instant)</c> and a view holds a clock, so the four fields
/// below are a complete description of the ring for its whole life. That is deliberately unlike
/// <c>ZoneSpawned</c>, which is paired with two reads on <c>RunState</c>: a zone is a place that
/// stands still and a ring is an animation, and an animation a view can run itself is one that
/// cannot stutter when a frame is dropped.
/// </para>
/// <para>
/// <b><see cref="Origin"/> is where the boss <em>stood</em>, not where it is</b> (M4-02 rule 2).
/// A ring that followed the body would be inescapable by walking, and walking out is the safe
/// answer GD §9.1 rule 2 requires.
/// </para>
/// </remarks>
public readonly struct ShockwaveEmitted
{
    /// <summary>The ring's run-stable id, issued from 1 and never reused within a run.</summary>
    public readonly int Id;

    /// <summary>Where it expands from, in world metres — the slam point.</summary>
    public readonly Vector3 Origin;

    /// <summary>How fast the ring grows, in metres per second.</summary>
    public readonly float Speed;

    /// <summary>How far it reaches before it is over. Beyond this is the safe ground.</summary>
    public readonly float MaxRadius;

    public ShockwaveEmitted(int id, Vector3 origin, float speed, float maxRadius)
    {
        Id = id;
        Origin = origin;
        Speed = speed;
        MaxRadius = maxRadius;
    }
}

/// <summary>The ring has reached its ceiling and is gone.</summary>
/// <remarks>
/// Published whether or not it hit anybody, for <c>ZoneExpired</c>'s reason: a view has to stop
/// drawing it either way. It says nothing about damage — a ring damages at most once per body
/// and <c>PlayerDamaged</c> is where that is announced.
/// </remarks>
public readonly struct ShockwavePassed
{
    /// <summary>Which ring — the id <see cref="ShockwaveEmitted"/> named.</summary>
    public readonly int Id;

    public ShockwavePassed(int id)
    {
        Id = id;
    }
}

/// <summary>
/// A fissure has opened and is arming — GD §9.1 rule 1's telegraph, and M4-02 rule 3's
/// <em>"a place not to be"</em>. Published on the tick it is placed.
/// </summary>
/// <remarks>
/// <b><see cref="ArmSeconds"/> rides on the event rather than being looked up</b>, for
/// <c>BossBeatStarted.Seconds</c>' and <c>SpawnTelegraphed.FiresAt</c>'s reason: whatever draws
/// the warning holds a look book and not the catalog, and a ring that has to ask core how long it
/// has is a ring that can disagree with the hazard about it.
/// </remarks>
public readonly struct FissureArmed
{
    /// <summary>The fissure's run-stable id, issued from 1 and never reused within a run.</summary>
    public readonly int Id;

    /// <summary>Where it opened, in world metres — under the player's feet at that instant.</summary>
    public readonly Vector3 At;

    /// <summary>How far it reaches, in metres.</summary>
    public readonly float Radius;

    /// <summary>Seconds before it bites. Never under GD §9.1 rule 1's 0.6.</summary>
    public readonly float ArmSeconds;

    public FissureArmed(int id, Vector3 at, float radius, float armSeconds)
    {
        Id = id;
        At = at;
        Radius = radius;
        ArmSeconds = armSeconds;
    }
}

/// <summary>The arm is over: whoever was standing in it has just been hurt.</summary>
/// <remarks>
/// Published whether or not anyone was inside — a view flashes the crack either way, and one that
/// only heard about hits could not tell a fissure that was dodged from one that never fired.
/// </remarks>
public readonly struct FissureFired
{
    /// <summary>Which fissure — the id <see cref="FissureArmed"/> named.</summary>
    public readonly int Id;

    public FissureFired(int id)
    {
        Id = id;
    }
}

/// <summary>The crack has closed and the ground is ordinary again.</summary>
public readonly struct FissureClosed
{
    /// <summary>Which fissure — the id <see cref="FissureArmed"/> named.</summary>
    public readonly int Id;

    public FissureClosed(int id)
    {
        Id = id;
    }
}

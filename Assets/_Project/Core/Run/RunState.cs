using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Run;

/// <summary>
/// Everything the live run is, right now. Core is the single source of truth: views hold no
/// gameplay state, they render events and read intents. Created by <c>RunSession.Start</c> and
/// dropped when the next run starts; the whole object dies with <c>RunScope</c>. See AR §10.2.
/// </summary>
/// <remarks>
/// <para>
/// The constructor and the two setters are <c>internal</c>, which is the point of this type
/// rather than a detail of it: no view can nudge the clock, no adapter can teleport the player by
/// assignment. Unity moves the player because core told it to through a
/// <see cref="PlayerMoveIntent"/>, and reports where that ended up in the next snapshot;
/// <see cref="PlayerPosition"/> is core writing down what it was told, never core deciding.
/// </para>
/// <para>
/// That seal now covers what is reachable through this object too. <see cref="Motor"/> used to be
/// public, and being a live object with a public <c>Tick</c> it let any view advance the player a
/// second time and double-integrate the frame, with nothing in the compiler to stop it. M0-16
/// confirmed that nothing in <c>Soulvail.Game</c> reads the handle at all — the velocity a view
/// needs arrives in a <see cref="PlayerMoveIntent"/>, and its position goes back out through the
/// snapshot — so the handle is <c>internal</c> and the two values anyone actually reads are
/// surfaced as <see cref="PlayerVelocity"/> and <see cref="PlayerFacing"/>.
/// </para>
/// <para>
/// The guards that <see cref="CharacterSpec"/> and <see cref="RunConfig"/> carry are absent here
/// on purpose. Those two are built from authored assets and from a menu choice, so they defend a
/// boundary; this constructor is reachable only from core, with arguments core just built, so a
/// guard here would defend against a bug in the same assembly — and, being unreachable from
/// <c>Soulvail.Tests.Core</c>, could only be tested by opening internals to the test assembly.
/// Opening internals to prove a check against your own mistake is the wrong trade. If this
/// constructor ever becomes public, the guards come with it.
/// </para>
/// <para>
/// Grows a field per milestone — health and shields (M1-02), the stage and its wave (M2-10),
/// level, XP and the tree (M3) — and stays the one place a run's live numbers live.
/// </para>
/// </remarks>
public sealed class RunState
{
    internal RunState(
        ContentId modeId,
        ContentId characterId,
        int seed,
        int stageIndex,
        CharacterSpec character,
        PlayerMotor motor,
        PlayerCombat combat,
        EnemySystem enemies,
        ProjectileSystem projectiles)
    {
        ModeId = modeId;
        CharacterId = characterId;
        Seed = seed;
        StageIndex = stageIndex;
        Character = character;
        Motor = motor;
        Combat = combat;
        Enemies = enemies;
        Projectiles = projectiles;
    }

    /// <summary>The mode being played, e.g. <c>mode.descent</c>.</summary>
    /// <remarks>
    /// Get-only where <see cref="StageIndex"/> is settable, and the asymmetry is the point: a run
    /// goes deeper, but it never becomes a different mode. The id rather than the
    /// <c>ModeSpec</c>, for the reason <see cref="CharacterId"/> is here beside
    /// <see cref="Character"/> — it is what a save writes and what a log line quotes.
    /// </remarks>
    public ContentId ModeId { get; }

    /// <summary>The class being played. Same id as <see cref="Character"/>'s, kept for the log line that quotes it before the spec is dereferenced.</summary>
    public ContentId CharacterId { get; }

    /// <summary>
    /// What the run's random generator was seeded with.
    /// </summary>
    /// <remarks>
    /// Stated by <c>RunConfig</c> and copied here, since M2-02. It used to be read off
    /// <c>IRandom</c> — one source of truth, on M0-09's argument — and the truth is still single,
    /// but it is now checked rather than inherited: <c>RunSession.Start</c> refuses a config
    /// whose seed disagrees with the generator. What changed is that a resumed run has to state
    /// the seed it is continuing rather than discover it (ledger row 6). See
    /// <see href="../../../../Docs/adr/0011-random-streams.md">ADR-0011</see>.
    /// </remarks>
    public int Seed { get; }

    /// <summary>
    /// How deep the run is, numbered from 1.
    /// </summary>
    /// <remarks>
    /// <c>internal set</c> because M2-10's stage flow advances it, and for the reason every
    /// setter in this class is internal: a view that could write the depth would be deciding
    /// something core owns. What a run <em>begins</em> at is <c>RunConfig.StageIndex</c>, checked
    /// against the mode before the run is announced.
    /// </remarks>
    public int StageIndex { get; internal set; }

    /// <summary>The class's authored numbers, resolved from the catalog at <c>Start</c>.</summary>
    public CharacterSpec Character { get; }

    /// <summary>The player's movement, ticked every frame. The run owns it; nothing else may.</summary>
    /// <remarks>
    /// <c>internal</c> because <c>Tick</c> is public on it and calling that from outside core would
    /// silently integrate the frame twice. Read <see cref="PlayerVelocity"/> and
    /// <see cref="PlayerFacing"/> instead; anything that wants to <em>drive</em> the player is
    /// asking the wrong object.
    /// </remarks>
    internal PlayerMotor Motor { get; }

    /// <summary>
    /// The velocity core decided for the player this tick, in metres per second on the ground
    /// plane.
    /// </summary>
    /// <remarks>
    /// The same number the tick's <see cref="PlayerMoveIntent"/> carries, and deliberately so:
    /// this is for anything that wants to <em>read</em> the run's state — a HUD, a debug overlay,
    /// a save — while the intent is how the body is told to act. Naming matches
    /// <see cref="PlayerPosition"/> and <c>WorldSnapshot.PlayerVelocity</c>, which is the same
    /// quantity coming back the other way one frame later.
    /// </remarks>
    public Vector3 PlayerVelocity => Motor.Velocity;

    /// <summary>The direction the player is facing: a unit vector on the ground plane.</summary>
    public Vector3 PlayerFacing => Motor.Facing;

    /// <summary>
    /// The player's health, targeting and perception, ticked every frame. The run owns it; nothing
    /// else may.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> for the third time in this class, and for the reason <see cref="Motor"/> and
    /// <see cref="Enemies"/> give: it is a live object with a public <c>Tick</c>, a public
    /// <c>ApplyDamage</c> and a public <c>Reset</c>, so a public handle would let any view advance
    /// the player's combat a second time, hurt them, or heal them to full, with nothing in the
    /// compiler to object. Everything outside core learns what happens here from the events
    /// <c>PlayerCombat</c> publishes — <c>TargetChanged</c>, <c>PlayerDamaged</c>,
    /// <c>PlayerDied</c>, <c>PlayerShieldChanged</c> — which is what M1-09's reticle and M1-17's
    /// HUD are built on. Anything that genuinely needs a live number gets a narrow read here rather
    /// than the handle.
    /// </remarks>
    internal PlayerCombat Combat { get; }

    /// <summary>Every enemy in the run, and the verbs that create, retire and tick them.</summary>
    /// <remarks>
    /// <c>internal</c> for exactly the reason <see cref="Motor"/> is, and this is the second time
    /// the question M0-16 left standing has been asked: it is a live object with a public
    /// <c>Tick</c>, a public <c>Spawn</c> and a public <c>Clear</c>, so a public handle would let
    /// any view advance the AI a second time, invent an enemy, or empty the arena, with nothing in
    /// the compiler to object. Nothing in <c>Soulvail.Game</c> needs it: a view learns that an
    /// enemy exists from <c>EnemySpawned</c>, learns it is gone from <c>EnemyDespawned</c>, and
    /// reports its position back through the snapshot. Anything outside core that wants to
    /// <em>read</em> the census reads <see cref="EnemyCount"/>; the first thing that genuinely
    /// needs more gets a narrow read here rather than the handle.
    /// </remarks>
    internal EnemySystem Enemies { get; }

    /// <summary>Every shot currently in the air, and the verb that lands one.</summary>
    /// <remarks>
    /// <c>internal</c> for the fourth time in this class, and the question AR §18.2 says every
    /// future field handing out a mutable object owes: it has a public <c>Tick</c>, a public
    /// <c>Fire</c> and a public <c>Clear</c>, so a public handle would let a view land every shot in
    /// the arena a second time, invent one, or quietly empty the sky — with nothing in the compiler
    /// to object. A view learns a shot exists from <c>ProjectileFired</c> and that it is over from
    /// <c>ProjectileImpacted</c>, which is everything M2-09 needs; anything outside core that wants
    /// to <em>read</em> the census reads <see cref="InFlightProjectiles"/>.
    /// </remarks>
    internal ProjectileSystem Projectiles { get; }

    /// <summary>
    /// How many shots are in the air right now — a scalar read, never the handle (AR §18.2).
    /// </summary>
    /// <remarks>
    /// Zero for the whole of M2-07a: nothing fires one until M2-07b's Spitter. It is here now
    /// because <c>DebugOverlay</c> showing a constant zero is what makes the first Spitter's first
    /// bolt visible as a number before M2-09 draws one.
    /// </remarks>
    public int InFlightProjectiles => Projectiles.InFlightCount;

    /// <summary>
    /// How many enemies are registered in the run.
    /// </summary>
    /// <remarks>
    /// Registered, not breathing — the wart <c>EnemyRegistry.AliveCount</c> carries, kept rather
    /// than renamed so the two numbers cannot be mistaken for different quantities. A dead enemy
    /// counts here until M1-11 has published its death and despawned it.
    /// </remarks>
    public int EnemyCount => Enemies.Registry.AliveCount;

    /// <summary>
    /// The player's current hit points, in <c>[0, <see cref="PlayerMaxHp"/>]</c>. The left-hand
    /// number of M1-17's <c>current/max</c> readout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One of the "narrow reads" <see cref="Combat"/> promises instead of the handle, and the four
    /// health ones are here for a different reason from
    /// <see cref="MovementSkillCooldownFraction"/> below. That number has no event that could carry
    /// it; these do — <c>PlayerDamaged</c> carries both fractions after every hit — but no event
    /// carries the state a HUD has to <em>start</em> from. A presenter handling <c>RunStarted</c>
    /// has a full bar to draw and nothing to draw it from, and a run's opening numbers are exactly
    /// the ones an event cannot report because nothing has happened yet.
    /// </para>
    /// <para>
    /// Reads, not a handle. <c>Health</c> has a public <c>ApplyDamage</c>, <c>Heal</c> and
    /// <c>Reset</c>, so exposing it whole would let any view hurt the player or heal them to full —
    /// the precise thing <see cref="Combat"/>'s seal exists to prevent.
    /// </para>
    /// </remarks>
    public float PlayerHp => Combat.Health.Current;

    /// <summary>
    /// The player's live maximum hit points — the right-hand number of the readout, and what
    /// <see cref="PlayerHpFraction"/> is over.
    /// </summary>
    /// <remarks>
    /// The <see cref="Stat"/>'s value as it stands, so a tree node that raises max HP moves the
    /// readout the moment it lands. <c>Health</c> floors the same number at zero for its own
    /// arithmetic and this deliberately does not: the two can only differ for a maximum driven
    /// <em>negative</em>, which nothing in the game does and which is a content mistake better seen
    /// on the HUD than hidden by a second clamp written here.
    /// </remarks>
    public float PlayerMaxHp => Combat.Health.MaxHp.Value;

    /// <summary>
    /// <see cref="PlayerHp"/> over <see cref="PlayerMaxHp"/>, in <c>[0, 1]</c> — what M1-17's HP
    /// bar fills to. The same number <c>PlayerDamaged.HpFraction</c> carries.
    /// </summary>
    public float PlayerHpFraction => Combat.Health.Fraction;

    /// <summary>
    /// The Aegis as a fraction of its maximum, in <c>[0, 1]</c>, and zero for a class without one —
    /// what M1-17's shield ring fills to. The same number <c>PlayerShieldChanged.Fraction</c>
    /// carries.
    /// </summary>
    public float PlayerShieldFraction => Combat.Health.ShieldFraction;

    /// <summary>
    /// The Aegis in absolute points, and zero for a class without one — what a save writes down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second narrow read of the same number as <see cref="PlayerShieldFraction"/>, added at
    /// M2-14a, and the duplication is the point rather than an oversight: a fraction is what a ring
    /// fills to, and it cannot be restored without the maximum that produced it. That maximum is a
    /// <see cref="Stat"/>, so M3's first shield node moves it — and a run resumed against a moved
    /// maximum would come back with a different number of points than it was saved with, silently
    /// and in the player's favour or against it depending on which way the node went.
    /// </para>
    /// <para>
    /// A read, never the handle, for the reason every entry in this block gives (AR §18.2):
    /// <c>Health</c> has a public <c>ApplyDamage</c>. <see cref="PlayerHp"/> is absolute for the
    /// same reason and has been since M1-17, which is why it needed nothing doing to it here.
    /// </para>
    /// </remarks>
    public float PlayerShield => Combat.Health.Shield;

    /// <summary>
    /// How much of the movement skill's cooldown is left, as a fraction in <c>[0, 1]</c>: 1 the
    /// instant a dash starts, 0 while the button is live. What M1-16's radial fill draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first of the "narrow reads" <see cref="Combat"/> promises instead of the handle, and it
    /// is here rather than on an event because there is no event that could carry it: a fill slides
    /// continuously for two and a half seconds, so publishing it would mean an event per frame for
    /// a number the reader is already sampling per frame. The handle stays <c>internal</c>, so this
    /// still cannot be used to advance, hurt or heal anything.
    /// </para>
    /// <para>
    /// Zero when nothing is cooling, which is the same answer a run that has never dashed gives —
    /// the button is live in both cases, and there is nothing else it could usefully say.
    /// </para>
    /// </remarks>
    public float MovementSkillCooldownFraction => Combat.Charge.CooldownFraction;

    /// <summary>
    /// Seconds of simulated run time, summed from each tick's <c>Dt</c>.
    /// </summary>
    /// <remarks>
    /// Simulated, not wall-clock: it does not advance while the game is paused or backgrounded,
    /// which is what makes it fair to show as a run's duration and safe to compare between runs.
    /// Wall-clock is <c>IClock</c>'s job (M2-01), and it is a different number.
    /// </remarks>
    public float Time { get; internal set; }

    /// <summary>
    /// Where the body last reported the player to be.
    /// </summary>
    /// <remarks>
    /// A sense, written from the snapshot each tick. Core never assigns a position to move the
    /// player — collision is resolved by Unity's character controller, so the only honest
    /// position is the one that came back.
    /// </remarks>
    public Vector3 PlayerPosition { get; internal set; }
}

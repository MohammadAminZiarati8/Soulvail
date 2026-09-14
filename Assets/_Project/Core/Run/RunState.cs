using System;
using System.Collections.Generic;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;

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
        ProjectileSystem projectiles,
        LevelTracker progression,
        EffectRegistry effects,
        SkillTree tree,
        SkillRunner skills)
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
        Progression = progression;
        Effects = effects;
        Tree = tree;
        Skills = skills;
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

    /// <summary>The run's experience, level and the picks it is owed. The run owns it; nothing else may.</summary>
    /// <remarks>
    /// <c>internal</c> for the fifth time in this class, and the question AR §18.2 says every field
    /// handing out a mutable object owes: <c>Grant</c> and <c>SpendLevelUp</c> are both public on
    /// it, so a public handle would let a view level the player at will or quietly consume a pick
    /// they were never shown. A HUD learns that experience moved from <c>XpChanged</c> and that a
    /// level was crossed from <c>LeveledUp</c>; the three reads below are the opening state those
    /// events cannot report, for the reason <see cref="PlayerHp"/> gives.
    /// </remarks>
    internal LevelTracker Progression { get; }

    /// <summary>
    /// Which handler answers for which effect, this run. The run owns it; nothing else may.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>internal</c> for the sixth time in this class, and the first time the answer is not about
    /// a <c>Tick</c>: <c>Apply</c> is public on the registry, so a public handle would let a view
    /// put a permanent modifier on the player's damage with nothing in the compiler to object — and
    /// unlike every other seal here, the caller would not even need a handle on the thing it was
    /// changing, because the whole point of the registry is that an address stands in for one.
    /// </para>
    /// <para>
    /// There is no narrow read standing in for it, deliberately. The other five fields each gave up
    /// scalars a HUD needs; nothing outside core has a question about effects yet. What a modifier
    /// <em>did</em> is already visible in the numbers it moved — <see cref="PlayerMaxHp"/> and the
    /// rest are the live <c>Stat</c> values — and M3-09d's tree view reads the tree, which is
    /// M3-03's object rather than this one.
    /// </para>
    /// <para>
    /// M3-03's tree is the only production caller: nothing in a live run calls <c>Apply</c> until
    /// it exists (M3-05 rule 10), so for this task the registry is built, filled and never used.
    /// </para>
    /// </remarks>
    internal EffectRegistry Effects { get; }

    /// <summary>
    /// The run's live skill tree — what is taken, what may be. The run owns it; nothing else may.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>internal</c> for the seventh time in this class, and the plainest case yet:
    /// <c>SkillTree.Take</c> is public, so a public handle here would let a view grant the player a
    /// node — the whole of CH §5's gating bypassed by anything that could reach the object, with
    /// nothing in the compiler to object. <c>Restore</c> is public on it as well and for the same
    /// reason it does not matter: the seal is this line (AR §18.2, M3-03 rule 8).
    /// </para>
    /// <para>
    /// <b>Null for a class with no tree, which is legal until M3-12</b> —
    /// <c>ContentCatalog.TryGetTreeFor</c> answers false for every class this build ships, and
    /// <c>RunSession._flow</c> is the precedent for a run holding null where there is nothing to
    /// compose. The three reads below answer 0, false and empty in that case, so nothing downstream
    /// has to ask which kind of run it is in. M3-14b pins that every <em>shipped</em> character has
    /// one, which is when this null stops being reachable in a build.
    /// </para>
    /// </remarks>
    internal SkillTree Tree { get; }

    /// <summary>
    /// How many tree nodes the player has taken this run — what a save writes down alongside the
    /// ids, and what the debug overlay shows until M3-09d draws the tree.
    /// </summary>
    /// <remarks>
    /// A narrow read rather than the handle, for the reason <see cref="Tree"/> gives. Zero for a
    /// class with no tree, which is the same answer a run that has taken nothing gives — there is
    /// nothing else it could usefully say, and <see cref="TakenNodeIds"/> is empty in both cases.
    /// </remarks>
    public int TakenNodeCount => Tree is null ? 0 : Tree.TakenCount;

    /// <summary>
    /// Whether every node of the class's tree is taken — what M3-08 reads to know a pick has
    /// nothing left to buy and becomes Overflow instead (CH §5.2).
    /// </summary>
    /// <remarks>
    /// False for a class with no tree, and that is the right answer rather than a convenient one: a
    /// run with no tree has nodes it has not taken in exactly the sense a run at the start does, so
    /// M3-08 banks the level either way and needs no second question.
    /// </remarks>
    public bool IsTreeFull => Tree is not null && Tree.IsFull;

    /// <summary>
    /// The nodes taken this run, in take order — the list <c>RunRecorder</c> writes into a
    /// snapshot and <c>SkillTree.Restore</c> replays.
    /// </summary>
    /// <remarks>
    /// <b>Empty, never null</b>, including for a class with no tree, so no reader has to ask —
    /// <c>RunSnapshot.TakenNodeIds</c>' own rule, one layer up. A read-only view of the tree's own
    /// list rather than a copy: the copy that matters is the one <c>RunSnapshot</c>'s constructor
    /// makes, because a save is enqueued and a borrowed buffer would be rewritten under a write
    /// that had not happened yet (M3-01b rule 5).
    /// </remarks>
    public IReadOnlyList<ContentId> TakenNodeIds =>
        Tree is null ? Array.Empty<ContentId>() : Tree.TakenIds;

    /// <summary>
    /// The actives the player owns and their live cooldowns. The run owns it; nothing else may.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>internal</c> for the eighth time in this class, and the reason is <see cref="Tree"/>'s
    /// with two doors instead of one: <c>SkillRunner.Add</c> and <c>SkillRunner.Cast</c> are both
    /// public, so a public handle here would let a view grant the player a skill or fire one — the
    /// whole of CH §4.2's authored trigger bypassed by anything that could reach the object, with
    /// nothing in the compiler to object (AR §18.2, M3-06 rule 13).
    /// </para>
    /// <para>
    /// <b>Never null</b>, unlike <see cref="Tree"/>: a run with no tree still has a runner, and it
    /// owns nothing. That is what lets the four reads below answer without asking which kind of run
    /// they are in, and what lets <c>RunSession.Tick</c> call <c>Tick</c> unconditionally.
    /// </para>
    /// <para>
    /// The seven reads are what M3-10's buttons and the debug overlay need; M3-09b adds the one
    /// that wants seconds rather than a fraction. <b>M3-07a sharpened the seal rather than
    /// loosening it</b>: <c>SetAutoCast</c> and <c>CastSlot</c> are public on the runner, so a
    /// handle here would let a view fire the player's skills <em>and</em> rearrange their thumb.
    /// </para>
    /// </remarks>
    internal SkillRunner Skills { get; }

    /// <summary>How many actives the player owns — zero until a run takes an Active node.</summary>
    /// <remarks>
    /// A narrow read rather than the handle, for the reason <see cref="Skills"/> gives. It is the
    /// count of what can <em>fire</em>, where <see cref="TakenNodeCount"/> is the count of what was
    /// picked: a tree of passives moves the second and never the first.
    /// </remarks>
    public int OwnedActiveCount => Skills.Count;

    /// <summary>The id of the owned active at <paramref name="index"/>, in take order.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public ContentId SkillIdAt(int index) => Skills.IdAt(index);

    /// <summary>
    /// How much of that active's cooldown is left, as a fraction in <c>[0, 1]</c>: 1 the instant a
    /// cast starts, 0 once it is live. What M3-10's radial fill draws.
    /// </summary>
    /// <remarks>
    /// Here rather than on an event for <see cref="MovementSkillCooldownFraction"/>'s reason: a
    /// fill slides continuously, so publishing it would mean an event per frame for a number the
    /// reader is already sampling per frame. The two answer the same way on purpose — M3-10 draws
    /// both side by side.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public float SkillCooldownFraction(int index) => Skills.CooldownFraction(index);

    /// <summary>Whether that active may fire right now.</summary>
    /// <remarks>
    /// Not the negation of <see cref="SkillCooldownFraction"/> being zero, and the pair is worth
    /// having separately for <c>ChargeSkill</c>'s reason: CC §6.2 gives a button that cannot fire
    /// 40 % opacity, which is a different question from how far round the fill has gone.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public bool IsSkillReady(int index) => Skills.IsReady(index);

    /// <summary>
    /// How many of CC §6.2's four manual slots are occupied — <b>what a screen must ask before
    /// sending <c>SetAutoCast(id, false)</c></b>, because the fifth is refused (M3-07a rule 2).
    /// </summary>
    /// <remarks>
    /// This read is the whole of how <em>"never silently refuse, and never silently swap"</em> is
    /// met without a mechanism: core refuses a state it cannot reach, and the screen asks this first
    /// so it can show CC §6.2's question instead of sending a command it knows will throw.
    /// </remarks>
    public int ManualSlotCount => Skills.ManualSlotCount;

    /// <summary>
    /// What is in manual slot <paramref name="slot"/>, or <c>default(ContentId)</c> when it is
    /// empty. S1 is slot 0.
    /// </summary>
    /// <remarks>
    /// Addressed by slot rather than by runner index, which is the read M3-10's four buttons want:
    /// a button is a fixed position and the skill under it changes, where <see cref="SkillIdAt"/>
    /// walks take order and knows nothing about thumbs.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="slot"/> is not one of the four.
    /// </exception>
    public ContentId ManualSlotAt(int slot) => Skills.SlotAt(slot);

    /// <summary>
    /// Whether that skill fires itself — true for every owned active until the player switches it
    /// (M3-07a rule 1).
    /// </summary>
    /// <remarks>
    /// What M3-09's list draws its per-row toggle from. By id rather than by index because the
    /// Skills screen lists skills and the runner's order is an implementation detail of the walk.
    /// </remarks>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// This run does not own <paramref name="skillId"/> as an active.
    /// </exception>
    public bool IsAutoCast(ContentId skillId) => Skills.IsAuto(skillId);

    /// <summary>
    /// CC §6.2's four thumb positions in order, <c>default(ContentId)</c> for an empty one — what
    /// <c>RunRecorder.Take</c> writes to disk (M3-07b rule 8).
    /// </summary>
    /// <remarks>
    /// <b>A read rather than the handle, and it allocates nothing</b>: the runner wraps its slot
    /// table once at construction, so this hands back an object that already exists rather than
    /// building one per boundary. <b>It is a live view, not a snapshot</b> — the next
    /// <c>SetAutoCast</c> is visible through it — which is why <c>RunSnapshot</c> copies what it is
    /// given instead of holding it, and why this is the one read here whose caller has an obligation.
    /// Four empties for a run with no tree, which is every run until M3-12 authors one.
    /// </remarks>
    public IReadOnlyList<ContentId> ManualSkillIds => Skills.Slots;

    /// <summary>The player's level, from 1 — the number beside M3-10b's XP strip.</summary>
    public int Level => Progression.Level;

    /// <summary>
    /// How far into the current level the player is, in <c>[0, 1)</c> — what the XP strip fills to.
    /// The same number <c>XpChanged.Fraction</c> carries.
    /// </summary>
    public float XpFraction => Progression.XpFraction;

    /// <summary>
    /// Experience into the current level in absolute points — what a save writes down.
    /// </summary>
    /// <remarks>
    /// A second narrow read of the same quantity as <see cref="XpFraction"/>, added at M3-01b, and
    /// the duplication is the point rather than an oversight — exactly the pair
    /// <see cref="PlayerShield"/> and <see cref="PlayerShieldFraction"/> make, for the reason M2-14a
    /// rule 4 gives: a fraction is what a strip fills to, and it cannot be restored without the
    /// maximum that produced it. That maximum is <c>XpToNext</c>, which moves with the level by
    /// construction and with the mode's curve whenever CH §5.2's exponent is retuned — so a run
    /// saved as a fraction would resume at a different number of points, silently.
    /// </remarks>
    public float Xp => Progression.Xp;

    /// <summary>
    /// How many picks the player has earned and not yet been given. Read by M3-08's level-up flow
    /// and, until it exists, by the debug overlay alone.
    /// </summary>
    public int PendingLevelUps => Progression.PendingLevelUps;

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

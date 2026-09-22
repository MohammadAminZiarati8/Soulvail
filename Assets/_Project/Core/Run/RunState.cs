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
        SkillRunner skills,
        ZoneSystem zones,
        LureSystem lures,
        MinionSystem minions,
        RisePassive rise,
        LevelUpFlow levelUp,
        SplashFlow splash,
        EssenceWallet wallet,
        Veilrot rot)
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
        Zones = zones;
        Lures = lures;
        Minions = minions;
        Rise = rise;
        LevelUp = levelUp;
        Splash = splash;
        Wallet = wallet;
        Rot = rot;
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
    /// compose. The four reads below answer 0, false, empty and false in that case, so nothing
    /// downstream has to ask which kind of run it is in. M3-14b pins that every <em>shipped</em>
    /// character has one, which is when this null stops being reachable in a build.
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
    /// Whether <paramref name="skillId"/> may be taken right now — CH §5's gating in one question,
    /// and what M3-09d's tree view frames a node <em>Available</em> on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A narrow read rather than the handle, for the reason <see cref="Tree"/> gives, and the
    /// <b>ninth</b> scalar read on this class — <see cref="SkillCooldownSeconds"/> was the eighth.
    /// <b>The seal does not move to let it out</b> (AR §18.2): <c>SkillTree.Take</c> is public, so a
    /// public <see cref="Tree"/> would let a view <em>grant</em> the node this read only asks about.
    /// </para>
    /// <para>
    /// <b>It exists so a screen does not re-derive the gating.</b> The alternative was the tree view
    /// computing CH §5's rules from <see cref="TakenNodeIds"/> and the tree's shape, which is a
    /// second copy of <c>TreeRules</c> in the presentation layer and would drift the first time a
    /// keystone rule changed (M3-09d rule 3). The same bargain M3-09b made for a cooldown in
    /// seconds, and M3-06 rule 1's argument for the floor living in one expression.
    /// </para>
    /// <para>
    /// <b>False for a class with no tree, and false for a stranger</b>, neither of them a throw.
    /// The first is every run in the build until M3-12 authors one, and the second is
    /// <c>SkillTree.IsAvailable</c>'s own rule: a caller asking whether it may draw a node as
    /// available wants an answer, and an id this tree does not hold is not available.
    /// </para>
    /// </remarks>
    public bool IsNodeAvailable(ContentId skillId) => Tree is not null && Tree.IsAvailable(skillId);

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
    /// The reads are what M3-10's buttons and the debug overlay need, plus the one the Skills
    /// screen wanted in seconds rather than as a fraction — <see cref="SkillCooldownSeconds"/>,
    /// added by M3-09b — plus M3-10a's <see cref="SlotCooldownFraction"/> and
    /// <see cref="IsSlotReady"/>, which are the same two questions asked by <em>slot</em> because
    /// that is what a button is. <b>M3-07a sharpened the seal rather than
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

    /// <summary>
    /// How long that active's wait actually is, in seconds, after CH §4.1's 40 % floor — what
    /// M3-09b's Skills screen prints.
    /// </summary>
    /// <remarks>
    /// <b>A read, never the handle</b> (AR §18.2), forwarding to
    /// <c>SkillRunner.EffectiveCooldownOf</c> rather than letting the screen multiply
    /// <see cref="SkillCooldownFraction"/> back up or apply the floor itself — which would be the
    /// second copy of <c>CooldownRules</c> that M3-06 rule 1 exists to prevent. <b>It is the whole
    /// wait rather than what is left of it</b>, and the pair is worth having separately for the
    /// reason <see cref="IsSkillReady"/> is: CC §6.3 asks what the cooldown <em>is</em> on a screen
    /// where the tick is gated, and a countdown behind a stopped simulation would never move.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not an owned active.
    /// </exception>
    public float SkillCooldownSeconds(int index) => Skills.EffectiveCooldownOf(index);

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
    /// How much of the cooldown under manual slot <paramref name="slot"/> is left, as a fraction in
    /// <c>[0, 1]</c>: 1 the instant a cast starts, 0 once it is live. What M3-10a's radial fill
    /// draws. <b>0 for an empty slot.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The <em>tenth</em> scalar read, and it is addressed by slot because that is what a button
    /// is</b> (M3-10a rule 2). <see cref="SkillCooldownFraction"/> is addressed by runner
    /// <em>index</em> — take order, which knows nothing about thumbs — so a button drawing S3 would
    /// have to read <see cref="ManualSlotAt"/> and then search the take order for that id, which is
    /// a presentation layer re-deriving what <c>SkillRunner.TryIndexOf</c> already answers. The
    /// mapping therefore lives here. <b>The seal does not move to let it out</b> (AR §18.2):
    /// <see cref="Skills"/> stays <c>internal</c>, because <c>SetAutoCast</c> and <c>CastSlot</c> are
    /// public on the runner and a handle would let a view fire the player's skills and rearrange
    /// their thumb.
    /// </para>
    /// <para>
    /// <b>An empty slot answers 0 and never throws, unlike <c>CastSlot</c>, which throws for one</b>
    /// (M3-07a rule 4). The two are different questions: asking an empty slot to <em>fire</em> is a
    /// view sending a command for a control it is not drawing, while <em>reading</em> one is what a
    /// button does on every frame it is not drawn — which, until M3-11 and M3-12 author an Active,
    /// is every frame of every run there has ever been.
    /// </para>
    /// </remarks>
    /// <param name="slot">Which thumb position, from 0. S1 is slot 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="slot"/> is not one of the four. A bad <em>slot</em> is still loud, because
    /// that is a screen addressing a button CC §6.2 does not draw — only an <em>empty</em> one is
    /// answered quietly.
    /// </exception>
    public float SlotCooldownFraction(int slot) =>
        TryIndexOfSlot(slot, out int index) ? Skills.CooldownFraction(index) : 0f;

    /// <summary>
    /// Whether the skill under manual slot <paramref name="slot"/> may fire right now.
    /// <b>False for an empty slot.</b>
    /// </summary>
    /// <remarks>
    /// The eleventh scalar read, and <see cref="SlotCooldownFraction"/>'s pair for the reason
    /// <see cref="IsSkillReady"/> is <see cref="SkillCooldownFraction"/>'s: CC §6.2 gives a button
    /// that cannot fire 40 % opacity and no tap response, which is a different question from how far
    /// round the fill has gone. False for an empty slot is the same answer as false for a cooling
    /// one <em>to this read</em>, and the two are told apart by <see cref="ManualSlotAt"/> — which is
    /// what decides whether the button is drawn at all (M3-10a rule 5).
    /// </remarks>
    /// <param name="slot">Which thumb position, from 0. S1 is slot 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="slot"/> is not one of the four.
    /// </exception>
    public bool IsSlotReady(int slot) =>
        TryIndexOfSlot(slot, out int index) && Skills.IsReady(index);

    /// <summary>
    /// Which entry in the runner's walk order sits under <paramref name="slot"/>, if anybody does.
    /// </summary>
    /// <remarks>
    /// The one place the slot → index mapping is written down, so the two reads above cannot come
    /// apart. <c>SlotAt</c> is what refuses a slot outside the four, and it refuses it before this
    /// method can answer anything — so a bad slot is loud and an empty one is not.
    /// </remarks>
    private bool TryIndexOfSlot(int slot, out int index)
    {
        ContentId skillId = Skills.SlotAt(slot);

        if (skillId == default)
        {
            index = -1;

            return false;
        }

        // Cannot miss: SetAutoCast and Restore are the only writers of the slot table and neither
        // puts an id in it that the runner does not hold. Asked rather than assumed for the reason
        // CastSlot asks — one place owns the mapping, and a second copy is the first thing that
        // could disagree with it.
        return Skills.TryIndexOf(skillId, out index);
    }

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

    /// <summary>
    /// The ground a cast has put down this run — CC §6.4's Consecrate, and every zone after it.
    /// </summary>
    /// <remarks>
    /// <b><c>internal</c>, like every other live object here</b> (AR §18.2). <c>Spawn</c>,
    /// <c>Tick</c> and <c>Clear</c> are all public on it, so a handle would let a view place a
    /// healing zone under the player, advance its clock, or delete one mid-fight. The two reads below
    /// are what an overlay or a view gets. <b>Never null</b>, like <see cref="Skills"/>: a run with no
    /// tree still has a ZoneSystem and it stands empty.
    /// </remarks>
    internal ZoneSystem Zones { get; }

    /// <summary>How many zones are standing right now — zero in every run that casts nothing.</summary>
    public int ActiveZoneCount => Zones.Count;

    /// <summary>
    /// Where the zone at <paramref name="index"/> was placed, in world metres.
    /// </summary>
    /// <remarks>
    /// <b>An index into the live zones, in the order they were placed, and not a handle</b>
    /// (<see cref="SkillIdAt"/>'s shape, with one difference worth knowing): a zone retiring shifts
    /// the ones placed after it down, where the runner's entries never leave. Anything following one
    /// zone across ticks follows the id on <c>ZoneSpawned</c>; this is the read something walks from 0
    /// to <see cref="ActiveZoneCount"/> to draw what is on the floor right now.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">There is no zone at that index.</exception>
    public Vector3 ZoneAt(int index) => Zones.PositionAt(index);

    /// <summary>
    /// The corpse decoys a Shroudstep has left standing — CH §3.2, and the first thing in this game
    /// an enemy walks at that is not the player.
    /// </summary>
    /// <remarks>
    /// <b><c>internal</c>, like every other live object here</b> (AR §18.2), and with <em>no</em>
    /// scalar read beside it, which is the one way it differs from <see cref="Zones"/>. A zone needs
    /// <see cref="ActiveZoneCount"/> and <see cref="ZoneAt"/> because a decal is drawn from a place
    /// that persists; a decoy's view (M5-05) is built from the <c>DecoySpawned</c> it was handed and
    /// torn down on the <c>DecoyExpired</c> that follows, the way a projectile's is — so a read here
    /// would be public API nothing asked for. <b>Never null</b>: a run of the Oathbound has a
    /// <c>LureSystem</c> and it stands empty for ever.
    /// </remarks>
    internal LureSystem Lures { get; }

    /// <summary>
    /// The Wights standing on the player's side, or <see langword="null"/> for a class that raises
    /// none — CH §3.2, and every class but the Gravecaller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>internal</c>, like every other live object here</b> (AR §18.2): <c>Spawn</c> stands a
    /// body up and <c>Tick</c> hits enemies with it, so a public handle would let a view raise an
    /// army and swing it. It follows <see cref="Tree"/> and <see cref="LevelUp"/> in being
    /// <em>null</em> rather than empty for a class without the feature, which is M5-04b rule 10's
    /// shape brought forward one task: an Oathbound run holds no <c>MinionSystem</c>, ingests
    /// nothing and ticks nothing, so it is byte-identical to the run it was before this task.
    /// </para>
    /// <para>
    /// <b>No scalar read beside it, for <see cref="Lures"/>'s reason.</b> Nothing outside core can
    /// see a Wight until M5-05a builds the views, and those are built from the
    /// <c>MinionSpawned</c> they were handed and torn down on the <c>MinionDespawned</c> or
    /// <c>MinionDied</c> that follows — so a count here would be public API nobody asked for.
    /// </para>
    /// </remarks>
    internal MinionSystem Minions { get; }

    /// <summary>
    /// CH §3.2's Rise — what turns a quarter of this run's kills into Wights — or
    /// <see langword="null"/> for a class that raises none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>internal</c>, like every other live object here</b> (AR §18.2): <c>OnDeaths</c> stands
    /// bodies up and draws from the run's <c>Drops</c> stream, so a public handle would let a view
    /// raise an army <em>and</em> spend draws the simulation is counting on — the same argument that
    /// keeps <see cref="LevelUp"/> behind four reads.
    /// </para>
    /// <para>
    /// <b>Null in exactly the runs <see cref="Minions"/> is null in</b>, and it is built from the
    /// same <c>MinionSpec</c>: an Oathbound run holds neither, draws nothing from <c>Drops</c>, and
    /// is byte-identical to the run it was before M5-04b (rule 10).
    /// </para>
    /// <para>
    /// <b>No scalar read beside it</b>, for <see cref="Lures"/>'s reason. <c>RisePassive.Raised</c>
    /// exists for a readout, and nothing draws one: a Wight has no view until M5-05a and the
    /// Gravecaller is not selectable until M5-07, so a read here would be public API nobody asked
    /// for. The first screen that wants the number gets one then.
    /// </para>
    /// </remarks>
    internal RisePassive Rise { get; }

    /// <summary>
    /// The run's level-up flow, or null for a class with no tree (M3-03 rule 10).
    /// </summary>
    /// <remarks>
    /// <b><c>internal</c>, and this is the seventh time</b> (AR §18.2). <c>Choose</c> takes a node
    /// and <c>Open</c> consumes the run's <c>Offers</c> stream, so a public handle would let a view
    /// grant the player a skill and spend draws the simulation is counting on — the same argument
    /// that keeps <see cref="Motor"/>, <see cref="Combat"/>, <see cref="Enemies"/>,
    /// <see cref="Projectiles"/>, <see cref="Progression"/>, <see cref="Tree"/> and
    /// <see cref="Skills"/> behind scalar reads. The four reads below are what a screen gets.
    /// </remarks>
    internal LevelUpFlow LevelUp { get; }

    /// <summary>
    /// Whether an offer is on the table — what M3-08b's screen draws and what the pause is held
    /// against.
    /// </summary>
    public bool HasOffer => LevelUp is not null && LevelUp.HasOffer;

    /// <summary>
    /// The ids currently offered, in draw order. Empty when there is none, and for every run whose
    /// class has no tree.
    /// </summary>
    /// <remarks>
    /// <b>A live view over one buffer the next draw rewrites</b>, named here for the reason
    /// <c>WorldSnapshot</c>'s reuse is named everywhere else. It is safe because it is read on a
    /// frame that is not being ticked — the gate is up whenever this is non-empty (M3-08a rule 15).
    /// Nothing may hold it across a <c>ChooseOffer</c>.
    /// </remarks>
    public IReadOnlyList<ContentId> Offer =>
        LevelUp is null ? Array.Empty<ContentId>() : LevelUp.Offer;

    /// <summary>
    /// Whether this frame should open a level-up: a pick is owed, no offer is open, and the class
    /// has a tree to spend it on.
    /// </summary>
    /// <remarks>
    /// <b>One question rather than three, and the third term is why.</b> A run whose class has no
    /// tree <em>banks</em> its levels — <see cref="PendingLevelUps"/> climbs, nothing draws, nothing
    /// pauses, nothing throws (M3-08a rule 5) — and that is every run in the build until M3-12
    /// authors one. A caller that asked only whether picks were owed would pause an empty screen for
    /// the whole of this milestone.
    /// </remarks>
    public bool IsLevelUpPending =>
        LevelUp is not null && !LevelUp.HasOffer && Progression.PendingLevelUps > 0;

    /// <summary>
    /// How many levels this run has spent on CH §5.2's Overflow rather than on a node — 0 for a run
    /// with no tree.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored: nothing on <c>RunSnapshot</c> carries it, and a resumed run
    /// recomputes it as <c>Level − 1 − TakenNodeCount − PendingLevelUps</c> (M3-08a rule 9). The
    /// read exists because the number is otherwise only observable through the stats it moved.
    /// </remarks>
    public int OverflowLevels => LevelUp is null ? 0 : LevelUp.OverflowLevels;

    /// <summary>
    /// CH §5.4's half-tree moment, or null for a class with no tree — the same runs
    /// <see cref="LevelUp"/> is null in.
    /// </summary>
    /// <remarks>
    /// <b><c>internal</c>, and this is the eighth time</b> (AR §18.2). <c>Choose</c> borrows a branch
    /// of a second class for the rest of the run and CH §5.4 says <em>"Reversible: no"</em>, so a
    /// public handle would let a view spend the one irreversible decision a run has — a sharper
    /// version of the argument that keeps <see cref="Tree"/> and <see cref="LevelUp"/> behind scalar
    /// reads. The four reads below are what a screen gets.
    /// </remarks>
    internal SplashFlow Splash { get; }

    /// <summary>
    /// Whether the run should stop and ask CH §5.4's question this frame: half the primary tree is
    /// taken, nothing has been borrowed, there is a class to borrow from, and no offer is on the
    /// table.
    /// </summary>
    /// <remarks>
    /// <b>One question rather than four, and the last term is why it is not simply
    /// <c>SplashFlow.IsPending</c></b> — <see cref="IsLevelUpPending"/>'s shape exactly. A pick taken
    /// on the frame the threshold is crossed can leave a second offer on the table, and the splash
    /// screen opening over it would be two screens wanting one <c>RunPause</c>. The offer is
    /// finished first and the moment is read on the frame after it closes.
    /// <para>
    /// False for a class with no tree, and false for ever in a build with one authored class — CH
    /// §5.4's own branch (rule 3).
    /// </para>
    /// </remarks>
    public bool IsSplashPending => Splash is not null && Splash.IsPending && !HasOffer;

    /// <summary>Whether the splash screen is up — what the pause is held against.</summary>
    public bool IsSplashOpen => Splash is not null && Splash.IsOpen;

    /// <summary>
    /// Whether this run has already borrowed a branch. True for the rest of the run once it has.
    /// </summary>
    /// <remarks>
    /// False for a class with no tree, which is the same answer a run that has not splashed gives —
    /// there is nothing else it could usefully say, and <see cref="SplashCandidates"/> is empty in
    /// both cases.
    /// </remarks>
    public bool HasSplashed => Splash is not null && Splash.HasSplashed;

    /// <summary>
    /// The classes this run may borrow from, in catalog order — the cards on the screen's first page.
    /// </summary>
    /// <remarks>
    /// <b>Empty, never null</b>, including for a class with no tree, so no reader has to ask —
    /// <see cref="TakenNodeIds"/>' own rule. A read-only view built once when the run started rather
    /// than a copy per call, for the reason <see cref="ManualSkillIds"/> is one: a screen draws it on
    /// the frame the moment opens and nothing about it can change afterwards.
    /// </remarks>
    public IReadOnlyList<ContentId> SplashCandidates =>
        Splash is null ? Array.Empty<ContentId>() : Splash.Candidates;

    /// <summary>
    /// The three branches of <paramref name="characterId"/> as the screen's second page draws them:
    /// a name key and the node count <em>after</em> the Keystone is dropped.
    /// </summary>
    /// <remarks>
    /// <b>A read rather than the handle</b> (AR §18.2), and the post-drop count is the whole of why
    /// it is not <c>SkillTreeSpec.Branches</c>: the screen would then draw a number that includes a
    /// node CH §5.4 does not lend (rule 8). A presenter re-deriving it would need the catalog and the
    /// Keystone rule, which is <c>TreeRules</c> in the presentation layer — M3-09d rule 3's refusal.
    /// </remarks>
    /// <param name="characterId">One of <see cref="SplashCandidates"/>.</param>
    /// <exception cref="InvalidOperationException">This run's class has no tree.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="characterId"/> is this run's own, is not in the catalog, or has no tree.
    /// </exception>
    public IReadOnlyList<SplashOption> SplashBranchesOf(ContentId characterId)
    {
        if (Splash is null)
        {
            throw new InvalidOperationException(
                "This run's class has no tree, so there is no half-tree moment and no branch to "
                    + "draw. A screen asking this without SplashCandidates to ask it for is a "
                    + "wiring mistake.");
        }

        return Splash.BranchesOf(characterId);
    }

    /// <summary>
    /// GD §15's second currency, this run — what the player has been paid and has not spent.
    /// </summary>
    /// <remarks>
    /// <b><c>internal</c>, like every other live object here</b> (AR §18.2, M6-01a rule 6). <c>Earn</c> and
    /// <c>Spend</c> are both public on the wallet, so a public handle here would let a view pay
    /// itself for a stage it did not clear — the same argument that keeps <see cref="Progression"/>
    /// and <see cref="LevelUp"/> behind scalar reads, with the shortest route to abuse of any of
    /// them. <b>Never null</b>, like <see cref="Skills"/>: every run has a wallet, whatever its
    /// class and whatever its mode pays.
    /// </remarks>
    internal EssenceWallet Wallet { get; }

    /// <summary>
    /// What the wallet holds, in Essence — the one thing outside core that may ask about the
    /// economy.
    /// </summary>
    /// <remarks>
    /// A narrow read rather than the handle, for the reason <see cref="Wallet"/> gives. One read,
    /// for the HUD M6-03b draws and the debug overlay before it — <see cref="PlayerHp"/>'s bargain,
    /// a dozen reads on. Zero for the whole of this task: nothing spends it until M6-02b and
    /// nothing draws it until M6-03a.
    /// </remarks>
    public int Essence => Wallet.Balance;

    /// <summary>
    /// GD §10's corruption meter, its four thresholds and the Claiming. The run owns it; nothing
    /// else may.
    /// </summary>
    /// <remarks>
    /// <b><c>internal</c>, like every other live object here</b> (AR §18.2, M6-04 rule 6).
    /// <c>Gain</c>, <c>Cleanse</c> and <c>Tick</c> are all public on the meter, so a public handle
    /// would let a view corrupt the player, absolve them, or advance the drain that is killing them
    /// — the argument that keeps <see cref="Wallet"/> and <see cref="Progression"/> behind scalar
    /// reads, with two of the shortest routes to abuse in the project. <b>Never null</b>, like
    /// <see cref="Skills"/>: every run has a meter, whatever its class, and a run that gains nothing
    /// holds one that reads zero.
    /// </remarks>
    internal Veilrot Rot { get; }

    /// <summary>
    /// GD §10's meter, in <c>[0, 100]</c> — what a save writes down and what the Claiming fires on.
    /// </summary>
    /// <remarks>
    /// A narrow read rather than the handle, for the reason <see cref="Rot"/> gives, and the read
    /// <c>RunRecorder.Take</c> has been writing to disk as a literal zero since M6-01b. **The seal
    /// did not move to let it out** (AR §18.2): what a HUD and a recorder need is a number.
    /// </remarks>
    public float Veilrot => Rot.Value;

    /// <summary>
    /// Whether GD §10.2's last row has closed. True for the rest of the run once it has.
    /// </summary>
    /// <remarks>
    /// <b>Separate from <see cref="Veilrot"/> because it cannot be derived from it</b> (M6-04
    /// rule 6). The Claiming is a latch and the meter is not, so a run cleansed from 100 to 40 reads
    /// 40 here with the buffs still on — the one state combination that looks like a bug and is not.
    /// Nothing on disk carries it either: a resumed run comes back Claimed because its saved meter
    /// reads 100, which is why v4 needed no field for it.
    /// </remarks>
    public bool IsClaimed => Rot.IsClaimed;

    /// <summary>
    /// The nodes GD §13.3's Banish has taken out of this run's pool — what a save writes down and
    /// what the offer draw will have to skip.
    /// </summary>
    /// <remarks>
    /// <b>Empty, never null</b>, for <see cref="TakenNodeIds"/>' own rule: no reader ever has to ask.
    /// Empty is the truth for every run until M6-02b sells a Banish, and the shared zero-length
    /// array is what keeps <c>RunRecorder.Take</c> allocation-free until then.
    /// </remarks>
    public IReadOnlyList<ContentId> BanishedNodeIds => Array.Empty<ContentId>();

    /// <summary>
    /// Which of <see cref="TakenNodeIds"/> were taken in GD §13.2's corrupted form — a subset of
    /// that list, and the only thing a resumed run can learn which effects it paid Veilrot for from.
    /// </summary>
    /// <remarks><see cref="BanishedNodeIds"/>' rule exactly. Empty until M6-05a.</remarks>
    public IReadOnlyList<ContentId> PactedNodeIds => Array.Empty<ContentId>();

    /// <summary>
    /// GD §13.4's Ordeals, in the order they were drawn.
    /// </summary>
    /// <remarks><see cref="BanishedNodeIds"/>' rule exactly. Empty until M6-06a.</remarks>
    public IReadOnlyList<ContentId> OrdealIds => Array.Empty<ContentId>();

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
    /// Points of shield a <em>cast</em> has put on the player, across every source — CC §6.4's
    /// Bulwark. Zero for a player with nothing granted, which is every run in this build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the Aegis, and the pair above is the one to read it against.</b>
    /// <see cref="PlayerShield"/> and <see cref="PlayerShieldFraction"/> are CH §3.1's signature
    /// pool with its own maximum and its own refill; this is a separate one that is spent first and
    /// expires on the clock of whatever granted it. There is deliberately no fraction beside it:
    /// granted shield has no maximum to be a fraction of — 35 points from one source and 55 from two
    /// are both simply what is there — so M3-13's treatment of GD §16.2 draws it as an overlay on
    /// the health bar rather than as a second ring.
    /// </para>
    /// <para>
    /// The twelfth scalar read, and the seal did not move to let it out (AR §18.2).
    /// <see cref="Combat"/> stays <c>internal</c> for the reason every entry in this block gives:
    /// <c>Health</c> has a public <c>ApplyDamage</c>, <c>Heal</c>, <c>Reset</c> and — as of M3-11a-i
    /// — a public <c>GrantShield</c>, so a view holding the handle could grant itself a shield as
    /// easily as it could heal to full.
    /// </para>
    /// </remarks>
    public float PlayerGrantedShield => Combat.Health.GrantedShield;

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

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using Vector2 = System.Numerics.Vector2;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The component that has driven the player's body since M2-art with no test on it. <b>M3 ledger
/// row 5</b>, closed here.
/// </summary>
/// <remarks>
/// <para>
/// <b>The behaviour is not re-derived.</b> The file landed without a spec and therefore without the
/// behaviour-rules-to-tests pairing the protocol asks for; this suite pins what it does, and the two
/// places where what it does turned out not to be what the spec's rows assumed are recorded on the
/// rows themselves rather than fixed. Changing behaviour and writing its first test in one PR is how
/// a regression gets blessed.
/// </para>
/// <para>
/// <b>An EditMode fixture can see a trigger fire, and that was probed rather than assumed.</b>
/// <see cref="Animator.SetTrigger(int)"/> on a component with no <c>RuntimeAnimatorController</c>
/// writes nowhere readable and logs a warning; with a controller assigned it works in edit mode
/// exactly as it does in play mode — <c>isInitialized</c> is true, <c>SetTrigger</c> is readable
/// through <see cref="Animator.GetBool(int)"/>, <c>SetFloat</c> round-trips and
/// <see cref="Animator.ResetTrigger(int)"/> clears. So the controller is built here, in code, with
/// the six parameters the component names, and every row in this file is EditMode. PlayMode's count
/// does not move.
/// </para>
/// <para>
/// <b>The attack-speed multiplier is reachable only on some Editors, and which is the Editor's
/// state, not the code's.</b> <c>OnAttacked</c> writes it only when the previous swing was at a
/// positive <c>Time.time</c> and this one is later. Outside play mode <c>Time.time</c> read <b>0</b>
/// at M6-11e after launch, after ten PlayMode passes and on leaving Play, and moved only once the
/// Editor had been the application in front — 1.83 s after a few seconds there. What else resets it
/// was not pinned down: a test run once saw 0 on an Editor that had read 1.94 s the moment before.
/// So the two multiplier rows split on the clock, each <c>Assume</c>s its half, and exactly one of
/// them runs:
/// <see cref="Animator_AttackSpeedIsUnreachableFromAnEditorClock"/> on a clock at zero,
/// <see cref="Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns"/> on one that has moved.
/// Until M6-11e the first <em>asserted</em> the zero, which is a premise rather than a behaviour:
/// an EditMode pass on an Editor that had been in front went red with no code changed (M7 ledger
/// row 8).
/// </para>
/// <para>
/// <c>Awake</c> never runs in EditMode (Traps §5), so <c>_body</c> is null throughout and nothing
/// here calls <c>Step</c> — which is the only method that would touch it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PlayerAnimatorViewTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// The length of <c>Melee_1H_Attack_Slice_Horizontal</c>, duplicated here rather than read from
    /// the class under test. Only the ceiling row uses it, and reading the number from the same
    /// constant on both sides would make its claim true by construction.
    /// </summary>
    private const float AuthoredSwingSeconds = 1.3666667f;

    /// <summary>The ceiling on the clip's speed multiplier, duplicated for the same reason.</summary>
    private const float MaxAttackSpeed = 6f;

    private static readonly ContentId Consecrate = new ContentId("skill.oathbound.consecrate");
    private static readonly ContentId Bulwark = new ContentId("skill.oathbound.bulwark");

    /// <summary>Which way the body was facing. Unread by this component, and any value will do.</summary>
    private static readonly Vector2 Facing = new Vector2(1f, 0f);

    private static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");
    private static readonly int AttackId = Animator.StringToHash("Attack");
    private static readonly int ChargeId = Animator.StringToHash("Charge");
    private static readonly int HitId = Animator.StringToHash("Hit");
    private static readonly int DeadId = Animator.StringToHash("Dead");
    private static readonly int CastId = Animator.StringToHash("Cast");

    private GameObject _body;
    private PlayerAnimatorView _view;
    private Animator _animator;
    private AnimatorController _controller;
    private DomainEventHub _hub;

    [SetUp]
    public void CreateView()
    {
        _controller = BuildController();

        _body = new GameObject("Player");

        // No PlayerView: since RS-02b the view finds one in its parents rather than requiring one
        // beside it, and nothing here reads it — only Step does, and no row in this file calls it.
        // CharacterLookTests.View_FindsPlayerViewInItsParent is the row that does.
        _view = _body.AddComponent<PlayerAnimatorView>();

        _animator = _body.AddComponent<Animator>();
        _animator.runtimeAnimatorController = _controller;

        Dress(_animator);

        _hub = new DomainEventHub();
    }

    [TearDown]
    public void DestroyView()
    {
        _hub?.Dispose();
        _hub = null;

        if (_body != null)
        {
            Object.DestroyImmediate(_body);
        }

        if (_controller != null)
        {
            Object.DestroyImmediate(_controller);
        }
    }

    /// <summary>
    /// The first swing of a run has nothing to measure against, so the clip plays at the speed it
    /// was authored at — and the swing itself still fires.
    /// </summary>
    /// <remarks>
    /// The <c>_lastAttackTime &gt; 0</c> guard, pinned. By the second swing the multiplier is
    /// correct, which at three swings a second is a third of a second of being slightly slow — the
    /// class's own note, and the cost this guard chooses.
    /// </remarks>
    [Test]
    public void Animator_AttackSpeedIsNotWrittenOnTheFirstSwing()
    {
        _view.Construct(_hub);

        _animator.SetFloat(AttackSpeedId, 2.5f);

        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(
            _animator.GetFloat(AttackSpeedId),
            Is.EqualTo(2.5f).Within(1e-4f),
            "Nothing was written: there was no previous swing to measure against.");

        // And the swing itself still played, which is the half of the frame that is not conditional.
        Assert.That(_animator.GetBool(AttackId), Is.True);
    }

    /// <summary>
    /// On a clock at zero no swing writes the multiplier, however the previous one is primed — and
    /// the swing still fires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>OnAttacked</c> writes <c>AttackSpeed</c> only when the previous swing was at a
    /// <em>positive</em> <c>Time.time</c> and this one is later than it. With the clock at zero there
    /// is no value of <c>_lastAttackTime</c> that satisfies both, so the guards are what this row can
    /// see: a primed previous swing ahead of the clock is refused by the second.
    /// </para>
    /// <para>
    /// <b>The zero is assumed, not asserted, since M6-11e.</b> Asserting it pinned the Editor's state
    /// rather than the component's — the row went red on EditMode passes whose Editor had been in
    /// front. On a clock that has moved this row is inconclusive and
    /// <see cref="Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns"/> is the one that runs.
    /// </para>
    /// </remarks>
    [Test]
    public void Animator_AttackSpeedIsUnreachableFromAnEditorClock()
    {
        Assume.That(
            Time.time,
            Is.Zero,
            "The Editor's clock has moved, as it does once the Editor has been in front, so the "
                + "multiplier is reachable: Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns "
                + "is the row that runs.");

        _view.Construct(_hub);

        _animator.SetFloat(AttackSpeedId, 2.5f);

        // Two swings, and a primed previous one. Neither writes, because both conditions in the
        // component depend on a clock that does not move.
        _hub.Publish(new PlayerAttacked(Facing));

        PrimeThePreviousSwing(5f);

        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(_animator.GetFloat(AttackSpeedId), Is.EqualTo(2.5f).Within(1e-4f));

        // And the swing trigger fired anyway, every time, which is the part of OnAttacked that is
        // not behind the clock at all.
        Assert.That(_animator.GetBool(AttackId), Is.True);
    }

    /// <summary>
    /// On a clock that has moved, a swing plays the clip at its authored length over the interval
    /// since the last one — and never faster than <see cref="MaxAttackSpeed"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The row the one above used to name in its failure message</b>, and the half of
    /// <c>OnAttacked</c> no fixture reached until M6-11e: the division and its ceiling. It runs
    /// whenever the Editor's clock has moved, and is inconclusive on a clock at zero.
    /// </para>
    /// <para>
    /// The first swing's interval is half the clock, which puts the previous swing at a positive time
    /// on any clock, and which is exact in binary — so the interval the component measures is this
    /// one to the bit. Above a clock of 0.46 s that swing's ratio is under the ceiling, and the
    /// division is what is asserted; below it only the ceiling can be seen. The second swing is always
    /// quicker than the ceiling allows, so the clamp is asserted on every clock.
    /// </para>
    /// </remarks>
    [Test]
    public void Animator_AttackSpeedFollowsTheSwingRatioWhenTheClockRuns()
    {
        Assume.That(
            Time.time,
            Is.GreaterThan(0f),
            "The Editor's clock stands at zero, as it does after launch and on leaving Play, so the "
                + "multiplier cannot be reached: Animator_AttackSpeedIsUnreachableFromAnEditorClock is "
                + "the row that runs.");

        _view.Construct(_hub);

        float now = Time.time;

        float interval = now / 2f;

        PrimeThePreviousSwing(now - interval);

        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(
            _animator.GetFloat(AttackSpeedId),
            Is.EqualTo(Mathf.Min(AuthoredSwingSeconds / interval, MaxAttackSpeed)).Within(1e-4f),
            $"A swing {interval:F4} s after the last one plays {AuthoredSwingSeconds:F4} s of clip "
                + "in that interval.");

        // Half the shortest interval the ceiling allows, so a clip asked to play at twelve times its
        // speed or more. Cleared first, so the value read below is this swing's write and not the
        // first one's, which on a clock under 0.46 s is the ceiling too.
        float quick = Mathf.Min(now / 2f, AuthoredSwingSeconds / MaxAttackSpeed / 2f);

        _animator.SetFloat(AttackSpeedId, 0f);

        PrimeThePreviousSwing(now - quick);

        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(
            _animator.GetFloat(AttackSpeedId),
            Is.EqualTo(MaxAttackSpeed),
            "Swings quicker than the clip can be sped up to play at the ceiling, not beyond it.");
    }

    /// <summary>A hit that reached hit points is a hit the body reacts to.</summary>
    [Test]
    public void Animator_FlinchesOnARealHit()
    {
        _view.Construct(_hub);

        _hub.Publish(new PlayerDamaged(0f, 10f, 0.9f, 0f, blocked: false));

        Assert.That(_animator.GetBool(HitId), Is.True);
    }

    /// <summary>
    /// The ledger's named row: a blocked hit gets no flinch.
    /// </summary>
    /// <remarks>
    /// CC §7's i-frames turn a hit away entirely, and flinching at it would tell the player they were
    /// hurt when the whole point of the window is that they were not.
    /// </remarks>
    [Test]
    public void Animator_DoesNotFlinchOnABlockedHit()
    {
        _view.Construct(_hub);

        _hub.Publish(new PlayerDamaged(0f, 0f, 1f, 1f, blocked: true));

        Assert.That(_animator.GetBool(HitId), Is.False);
    }

    /// <summary>
    /// The shipped <c>evt.ToHp &lt;= 0f</c> branch, aimed at what it actually catches: a hit nothing
    /// of which reached hit points.
    /// </summary>
    /// <remarks>
    /// <b>The spec's row for this branch was <c>Animator_DoesNotFlinchOnAKillingHit</c>, and it read
    /// the field backwards.</b> <c>PlayerDamaged.ToHp</c> is damage <em>dealt to</em> hit points, not
    /// hit points remaining — <c>HpFraction</c> is the remainder — so <c>ToHp &lt;= 0</c> means the
    /// Aegis or a Bulwark ate the hit whole. That is the same family as <c>Blocked</c>, and it is
    /// precisely the event M3-11a-i's fully absorbed grant publishes:
    /// <c>PlayerDamaged(0, 0, unchanged, unchanged, blocked: false)</c>. A killing hit is the
    /// opposite case and has its own row below.
    /// </remarks>
    [Test]
    public void Animator_DoesNotFlinchWhenNothingReachedHp()
    {
        _view.Construct(_hub);

        // M3-11a-i's shape exactly: absorbed whole by a granted shield, and deliberately not marked
        // blocked, because the i-frames did not do it.
        _hub.Publish(new PlayerDamaged(0f, 0f, 1f, 1f, blocked: false));

        Assert.That(_animator.GetBool(HitId), Is.False);
    }

    /// <summary>
    /// A killing hit <b>does</b> flinch, and then the death latches. Found in core's publish order
    /// rather than assumed.
    /// </summary>
    /// <remarks>
    /// <c>PlayerCombat.ApplyDamage</c> publishes <c>PlayerDamaged</c> and only then
    /// <c>PlayerDied</c>, and <c>_dead</c> is set by nothing but the second one — so on the killing
    /// tick the damage arrives at a component that does not yet know it is over, with a positive
    /// <c>ToHp</c> and <c>Blocked</c> false. The flinch fires. Nothing in the file special-cases the
    /// killing blow, and this row records that as the shipped behaviour rather than correcting it:
    /// the flinch is one frame and the death clip takes the body on the next, which is a defensible
    /// read of GD §16.3 and not obviously a bug.
    /// </remarks>
    [Test]
    public void Animator_FlinchesOnTheKillingHitAndThenGoesDead()
    {
        _view.Construct(_hub);

        _hub.Publish(new PlayerDamaged(0f, 40f, 0f, 0f, blocked: false));

        Assert.That(
            _animator.GetBool(HitId),
            Is.True,
            "Core publishes the damage before the death, so this hit reached a living body.");

        _hub.Publish(new PlayerDied(12f));

        Assert.That(_animator.GetBool(DeadId), Is.True);
    }

    /// <summary>
    /// Death is the one state with no way out, and it survives everything published after it.
    /// </summary>
    /// <remarks>
    /// <c>PlayerDamaged</c> genuinely can arrive after <c>PlayerDied</c> — the killing blow and a
    /// second enemy's strike can land on the same tick — which is why the flag is latched here as
    /// well as in the controller.
    /// </remarks>
    [Test]
    public void Animator_DoesNothingAfterDeath()
    {
        _view.Construct(_hub);

        _hub.Publish(new PlayerDied(12f));

        ResetTriggers();

        _hub.Publish(new PlayerDamaged(0f, 10f, 0f, 0f, blocked: false));
        _hub.Publish(new PlayerAttacked(Facing));
        _hub.Publish(new ChargeStarted(Facing));
        _hub.Publish(new SkillCast(Consecrate, 12f, wasAuto: true));

        Assert.That(_animator.GetBool(HitId), Is.False, "A corpse does not flinch.");
        Assert.That(_animator.GetBool(AttackId), Is.False, "A corpse does not swing.");
        Assert.That(_animator.GetBool(ChargeId), Is.False, "A corpse does not dash.");
        Assert.That(_animator.GetBool(CastId), Is.False, "A corpse does not cast.");

        Assert.That(_animator.GetBool(DeadId), Is.True, "And it is still dead.");
    }

    /// <summary>The dash has a pose, and it is fired from the fact rather than from the input.</summary>
    [Test]
    public void Animator_ChargeFires()
    {
        _view.Construct(_hub);

        _hub.Publish(new ChargeStarted(Facing));

        Assert.That(_animator.GetBool(ChargeId), Is.True);
    }

    /// <summary>Rule 7: a cast has a pose, and this task is what gives it one.</summary>
    [Test]
    public void Animator_CastFires()
    {
        _view.Construct(_hub);

        _hub.Publish(new SkillCast(Consecrate, 12f, wasAuto: false));

        Assert.That(_animator.GetBool(CastId), Is.True);

        // And nothing else moved: a cast is not a swing and not a dash.
        Assert.That(_animator.GetBool(AttackId), Is.False);
        Assert.That(_animator.GetBool(ChargeId), Is.False);
    }

    /// <summary>
    /// Rule 7's other half: one cast clip for twelve nodes, and no parameter named after a content
    /// id.
    /// </summary>
    /// <remarks>
    /// A trigger per skill would be an animator parameter per content id — a designer adding a node
    /// would be adding a parameter, and a node whose parameter nobody added would silently play
    /// nothing. The second assertion is what makes the first one mean something: every parameter hash
    /// this component holds is a <c>readonly</c> field fixed at construction, so there is nowhere for
    /// a hash derived from an event to be kept.
    /// </remarks>
    [Test]
    public void Animator_CastIsNotPerSkill()
    {
        _view.Construct(_hub);

        _hub.Publish(new SkillCast(Consecrate, 12f, wasAuto: false));

        Assert.That(_animator.GetBool(CastId), Is.True, "Consecrate fired the one cast trigger.");

        ResetTriggers();

        _hub.Publish(new SkillCast(Bulwark, 8f, wasAuto: true));

        Assert.That(_animator.GetBool(CastId), Is.True, "And so did Bulwark, on the same trigger.");

        foreach (FieldInfo field in typeof(PlayerAnimatorView).GetFields(Private))
        {
            if (field.FieldType != typeof(int))
            {
                continue;
            }

            Assert.That(
                field.IsInitOnly,
                Is.True,
                $"{field.Name} is a mutable parameter hash. Every hash this component holds is "
                    + "fixed at construction — a per-skill trigger would need somewhere to keep a "
                    + "hash derived from an event, and there is deliberately nowhere.");
        }
    }

    /// <summary>
    /// It subscribes from <c>Construct</c>, so a component injected after the run has started still
    /// hears everything.
    /// </summary>
    /// <remarks>
    /// <c>HudPresenter</c>'s reason: subscribing in <c>OnEnable</c> or <c>Start</c> orders this
    /// against nothing, and <c>RunScope</c> injects during its own <c>Awake</c>. A body that missed
    /// the opening exchange of a fight would simply stand still through it.
    /// </remarks>
    [Test]
    public void Animator_SubscribesFromConstruct()
    {
        _hub.Publish(new RunStarted(new ContentId("character.oathbound"), seed: 7));

        _view.Construct(_hub);

        _hub.Publish(new PlayerAttacked(Facing));

        Assert.That(_animator.GetBool(AttackId), Is.True);
    }

    /// <summary>A body destroyed mid-run stops being handed events for a component Unity has killed.</summary>
    [Test]
    public void Animator_DestroyDropsSubscriptions()
    {
        _view.Construct(_hub);

        Object.DestroyImmediate(_body);
        _body = null;

        Assert.DoesNotThrow(() =>
        {
            _hub.Publish(new PlayerAttacked(Facing));
            _hub.Publish(new PlayerDamaged(0f, 10f, 0.9f, 0f, blocked: false));
            _hub.Publish(new ChargeStarted(Facing));
            _hub.Publish(new SkillCast(Consecrate, 12f, wasAuto: true));
            _hub.Publish(new PlayerDied(12f));
        });
    }

    /// <summary>
    /// The existing null guards, pinned: a component with no <see cref="Animator"/> assigned hears
    /// every event and does nothing at all.
    /// </summary>
    /// <remarks>
    /// The <see cref="Animator"/> is removed rather than left dressed without a controller, and that
    /// is the honest shape of "undressed": the component returns before touching an animator at all,
    /// where an animator with no controller would log <em>"Animator is not playing an
    /// AnimatorController"</em> on every call — a warning this component never causes, because it
    /// never gets that far.
    /// </remarks>
    [Test]
    public void Animator_UndressedIsSilent()
    {
        typeof(PlayerAnimatorView).GetField("_animator", Private).SetValue(_view, null);

        _view.Construct(_hub);

        Assert.DoesNotThrow(() =>
        {
            _hub.Publish(new PlayerAttacked(Facing));
            _hub.Publish(new PlayerDamaged(0f, 10f, 0.9f, 0f, blocked: false));
            _hub.Publish(new ChargeStarted(Facing));
            _hub.Publish(new SkillCast(Consecrate, 12f, wasAuto: true));
            _hub.Publish(new PlayerDied(12f));
        });
    }

    /// <summary>The one door on the public surface.</summary>
    [Test]
    public void Animator_ConstructRefusesANullHub()
    {
        Assert.Throws<ArgumentNullException>(() => _view.Construct(null));
    }

    /// <summary>
    /// Every parameter this component sets exists on the controller the game ships — not on the
    /// double <see cref="BuildController"/> builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The double is why this row exists.</b> It has named <c>Cast</c> since M3-06 and
    /// <c>AC_Player.controller</c> never did, so every row above passed while every cast in Play
    /// logged <em>"Parameter 'Hash -1299573048' does not exist"</em> — found only when
    /// <c>BootSmokeTests</c> resumed a saved run with an Active and failed on the warning (M6-02a,
    /// Findings). A missing parameter is now a red row naming it rather than a smoke test.
    /// </para>
    /// <para>
    /// <b>The component's own hashes are read, by reflection, rather than a list typed here.</b> A
    /// list in this file is a third place the names live and would miss the next field exactly as
    /// the double missed this one. Every private <see cref="int"/> field named <c>_…Id</c> is a
    /// parameter hash by this component's convention; the count is asserted so the sweep cannot
    /// pass by finding nothing.
    /// </para>
    /// </remarks>
    [Test]
    public void Animator_EveryParameterItSetsExistsOnTheShippedController()
    {
        const string path = "Assets/_Project/Animation/Controllers/AC_Player.controller";

        var shipped = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        Assert.That(shipped, Is.Not.Null, $"No AnimatorController at {path}.");

        var authored = new HashSet<int>();

        foreach (AnimatorControllerParameter parameter in shipped.parameters)
        {
            authored.Add(parameter.nameHash);
        }

        var missing = new List<string>();
        int swept = 0;

        foreach (FieldInfo field in typeof(PlayerAnimatorView).GetFields(Private))
        {
            if (field.FieldType != typeof(int) || !field.Name.EndsWith("Id", StringComparison.Ordinal))
            {
                continue;
            }

            swept++;

            var hash = (int)field.GetValue(_view);

            if (!authored.Contains(hash))
            {
                missing.Add($"{field.Name} (hash {hash})");
            }
        }

        Assert.That(swept, Is.EqualTo(7), "Speed, AttackSpeed, Attack, Charge, Hit, Dead and Cast.");
        Assert.That(missing, Is.Empty, $"{path} lacks a parameter PlayerAnimatorView sets.");
    }

    /// <summary>
    /// A controller with the six parameters this component names, and nothing else — no states, no
    /// transitions, no clips.
    /// </summary>
    /// <remarks>
    /// Enough to make the <see cref="Animator"/> initialise and hold values, which is all any row
    /// here reads. Nothing consumes a trigger, so a fired one stays set until
    /// <see cref="ResetTriggers"/> clears it — which is what lets a row assert that a second event
    /// fired the <em>same</em> trigger rather than a different one.
    /// </remarks>
    private static AnimatorController BuildController()
    {
        var controller = new AnimatorController
        {
            name = "AC_PlayerTestDouble",

            // Never written to disk, and gone with the fixture: an AnimatorController made with new
            // is an asset-shaped object that the AssetDatabase would otherwise be entitled to keep.
            hideFlags = HideFlags.HideAndDontSave,
        };

        controller.AddLayer("Base Layer");

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("AttackSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Charge", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

        return controller;
    }

    /// <summary>Puts the animator on the component's private serialized field.</summary>
    private void Dress(Animator animator) =>
        typeof(PlayerAnimatorView).GetField("_animator", Private).SetValue(_view, animator);

    /// <summary>Says the previous swing happened at <paramref name="time"/> on the play clock.</summary>
    private void PrimeThePreviousSwing(float time) =>
        typeof(PlayerAnimatorView).GetField("_lastAttackTime", Private).SetValue(_view, time);

    /// <summary>
    /// Clears every trigger, so a row can say "and then this one fired" rather than "one of these
    /// was set at some point".
    /// </summary>
    private void ResetTriggers()
    {
        _animator.ResetTrigger(AttackId);
        _animator.ResetTrigger(ChargeId);
        _animator.ResetTrigger(HitId);
        _animator.ResetTrigger(CastId);
    }
}

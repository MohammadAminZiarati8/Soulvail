using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Core.Stage;
using Soulvail.Game.Adapters;
using Soulvail.Game.Composition;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// GD §16.2's player row, finally showing the shield Bulwark put on them: a segment ahead of the
/// fill, in absorption order, and an Aegis ring that does not move.
/// </summary>
/// <remarks>
/// <para>
/// <b>A fixture of its own because <c>HpBarView</c> and <c>HudPresenter</c> have never had one.</b>
/// The spec's Files table names two test files, both of them about the enemy; its nine
/// <c>Player_</c> rows had nowhere to live, because the only assertions ever made about either class
/// are <c>PaletteTests.HpBar_DrawsThePaletteColours</c> and the three rows M3-10b added to
/// <c>XpBarViewTests</c>. This is that third file, and the deviation is recorded in <em>As built</em>.
/// </para>
/// <para>
/// <b>Over a real <c>RunSession</c> and the shipped <c>Hud.prefab</c></b> — <c>XpBarViewTests</c>'
/// shape and its reasons. <c>RunState</c>'s constructor is <c>internal</c> and this assembly has no
/// <c>InternalsVisibleTo</c> (AR §18.2), so a fake session cannot produce one — and the segment is
/// sized against <c>PlayerMaxHp</c>, which means a fake would have nothing to divide by.
/// </para>
/// <para>
/// The two events are published into the run's hub rather than earned through a <c>GrantShield</c>
/// node, deliberately: what is asserted here is that the HUD draws what core said. That core says it
/// correctly — <c>Total</c> across every source, <c>Removed</c> as what was left — is M3-11a's, over
/// the object that produces it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class GrantedShieldTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string TreeId = "tree.oathbound";
    private const string ArenaId = "arena.pillars";

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>The Oathbound's authored maximum, quoted so the arithmetic below reads.</summary>
    private const float MaxHp = 140f;

    /// <summary>CC §7's Aegis: 30 points, and the one pool this task must not touch.</summary>
    private const float AegisPoints = 30f;

    /// <summary>
    /// Branch a's whole first tier plus one node each in b and c — <c>XpBarViewTests</c>' five-node
    /// tree, copied because <c>SkillTreeSpec</c> refuses anything smaller: CH §5 gives every class
    /// exactly three branches so the UI is built once, and a one-branch tree throws at construction.
    /// </summary>
    private const string NodeOne = "skill.test.one";

    private const string NodeTwo = "skill.test.two";
    private const string NodeThree = "skill.test.three";
    private const string NodeFour = "skill.test.four";
    private const string NodeFive = "skill.test.five";

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private const BindingFlags Everything =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    private DomainEventHub _hub;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private RunSession _session;
    private RunConfig _config;

    private GameObject _hud;
    private HudPresenter _presenter;
    private HpBarView _bar;
    private ShieldRingView _ring;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<IDisposable> _disposables = new List<IDisposable>();

    [SetUp]
    public void CreateWorld()
    {
        _hub = new DomainEventHub();
        _random = new FixedRandom(7, Alternating(8_192));
        _clock = new FixedClock(Instant);
    }

    [TearDown]
    public void DestroyWorld()
    {
        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            _disposables[i].Dispose();
        }

        _disposables.Clear();

        foreach (GameObject go in _spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();

        _hub?.Dispose();

        _session = null;
        _presenter = null;
        _bar = null;
        _ring = null;
    }

    // ---- The segment (rules 10, 11) --------------------------------------------------------------

    [Test]
    public void Player_ShowsAGrantedShield()
    {
        StartRun();
        BuildHud();

        Assert.That(Segment().enabled, Is.False, "A run with no grant draws no segment.");

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));

        // 35 points over the live maximum. The maximum comes off the run rather than off the event,
        // because no event carries it and a segment sized against a stale one would be wrong for the
        // whole of a Vitality node's life.
        Assert.That(Segment().enabled, Is.True);
        Assert.That(Width(), Is.EqualTo(35f / MaxHp).Within(1e-4f));
    }

    [Test]
    public void Player_SegmentSitsAheadOfTheFill()
    {
        GameObject bare = BareBar();

        try
        {
            _bar.Set(0.5f);
            _bar.SetGrantedShield(35f / MaxHp);

            var rect = (RectTransform)Segment().transform;

            // Drawn ahead of the fill, which is also where the points actually are: ApplyDamage
            // spends granted shield first, then the Aegis, then hit points, so the bar reads in
            // absorption order rather than in an order chosen for looks.
            Assert.That(rect.anchorMin.x, Is.EqualTo(0.5f).Within(1e-4f), "It begins where the fill ends.");
            Assert.That(rect.anchorMax.x, Is.EqualTo(0.5f + (35f / MaxHp)).Within(1e-4f));
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    [Test]
    public void Player_SegmentStaysOnTheBarAtFullHealth()
    {
        GameObject bare = BareBar();

        try
        {
            _bar.Set(1f);
            _bar.SetGrantedShield(0.25f);

            var rect = (RectTransform)Segment().transform;

            // **The case the whole feature turns on, and the one a plain clamp gets wrong.** Bulwark
            // grants a shield at the moment the player is about to be hit, which is usually at full
            // health — so "ahead of the fill" is off the end of the bar, and a clamped segment would
            // collapse to nothing in exactly the situation the node exists for. It keeps its width
            // and gives way at the start instead: the bright band takes the bar's right-hand end,
            // still in absorption order, still inside the row HudPresenter.Place laid out.
            Assert.That(rect.anchorMax.x, Is.EqualTo(1f).Within(1e-4f), "It never runs past the bar.");
            Assert.That(rect.anchorMin.x, Is.EqualTo(0.75f).Within(1e-4f), "And it is still 25 % wide.");
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    [Test]
    public void Player_SegmentIsNeverWiderThanTheBar()
    {
        GameObject bare = BareBar();

        try
        {
            _bar.Set(1f);

            // A grant worth more than the player's whole maximum is legal — M3-11a puts no ceiling on
            // the pool — and the bar simply runs out of room to say so. Ledger row 4 gets the
            // question of whether that needs a number beside it; it is not one the Editor can answer.
            _bar.SetGrantedShield(1f);

            var rect = (RectTransform)Segment().transform;

            Assert.That(rect.anchorMin.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(rect.anchorMax.x, Is.EqualTo(1f).Within(1e-4f));
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    [Test]
    public void Player_ShrinksAsItIsSpent()
    {
        StartRun();
        BuildHud();

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));
        _hub.Publish(new ShieldGranted(0f, 15f, 5f));

        // The segment follows Total, not the grant: a partial spend is the pool going down, and a
        // view that tracked Amount would draw 35 points the player no longer has.
        Assert.That(Width(), Is.EqualTo(15f / MaxHp).Within(1e-4f));
    }

    [Test]
    public void Player_HidesWhenTheTotalIsZero()
    {
        StartRun();
        BuildHud();

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));

        Assert.That(Segment().enabled, Is.True, "Setup: the segment is up.");

        _hub.Publish(new ShieldGrantExpired(35f, 0f));

        Assert.That(Segment().enabled, Is.False);
    }

    [Test]
    public void Player_StaysWhileAnotherGrantRuns()
    {
        StartRun();
        BuildHud();

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));
        _hub.Publish(new ShieldGranted(20f, 55f, 5f));

        // ShieldGrantExpired.Total is what is *left*, not zero — the row that field exists for. A
        // view that assumed an expiry emptied the pool would erase points the player still has, and
        // would do it at the exact moment they are about to be hit.
        _hub.Publish(new ShieldGrantExpired(35f, 20f));

        Assert.That(Segment().enabled, Is.True);
        Assert.That(Width(), Is.EqualTo(20f / MaxHp).Within(1e-4f));
    }

    [Test]
    public void Player_DrawsTheOpeningStateOnRunStarted()
    {
        StartRun();
        BuildHud();

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));

        Assert.That(Segment().enabled, Is.True, "Setup: something is on the bar to be redrawn away.");

        _hub.Publish(new RunStarted(_config.CharacterId, _config.Seed));

        // **This row asserts zero, and it always will.** RunState.PlayerGrantedShield reads
        // Combat.Health.GrantedShield, TimedEffects.Clear forgets every grant at the run's end
        // (M3-11a rule 8), and nothing restores one across a resume — so there is no run in this
        // project, now or later, whose opening granted shield is anything but zero. It is read anyway
        // on HudPresenter's standing reason: whether RunStarted has already been published depends on
        // an order Unity does not give, so the HUD must never be the thing that knows which of those
        // two facts is keeping it correct. Said out loud here rather than left to look like a row
        // that passes by accident.
        Assert.That(_session.State.PlayerGrantedShield, Is.EqualTo(0f));
        Assert.That(Segment().enabled, Is.False);
    }

    // ---- What it must not touch (rules 10, 12, 13) -----------------------------------------------

    [Test]
    public void Player_AegisRingIsUntouched()
    {
        StartRun();
        BuildHud();

        float before = Field<float>(_ring, "_shown");

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));

        // M3-11a rule 5 from this side: the ring draws ShieldSpec's 30 points, its delay and its
        // refill, and CH §3.1 makes that the Oathbound's signature. A second arc for the granted pool
        // would make one readout mean two pools with different rules.
        Assert.That(Field<float>(_ring, "_shown"), Is.EqualTo(before).Within(1e-6f));
        Assert.That(before, Is.EqualTo(1f).Within(1e-4f), "A fresh run's Aegis is full.");
        Assert.That(_session.State.PlayerShield, Is.EqualTo(AegisPoints).Within(1e-4f));
    }

    [Test]
    public void Player_TextDoesNotMentionIt()
    {
        StartRun();
        BuildHud();

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));

        // GD §16.2's player row is "never ambiguous", and "140/140 (+35)" is a third number in a
        // 320 dp row that M3-10b has just added a level label to. The segment says it; the text does
        // not say it again.
        Assert.That(Field<TMP_Text>(_presenter, "_hpText").text, Is.EqualTo("140/140"));
    }

    [Test]
    public void Player_HoldsNoHealth()
    {
        foreach (Type view in new[] { typeof(HpBarView), typeof(ShieldRingView), typeof(EnemyHealthBar) })
        {
            string[] offenders = view.GetFields(Everything)
                .Where(f => f.FieldType == typeof(float) || f.FieldType == typeof(int))
                .Select(f => f.Name)
                .Where(NamesPoints)
                .ToArray();

            // Rule 13: three views, three inputs — an event's fraction, an event's total, one read on
            // the opening frame — and no hit points anywhere. GD §16.2's whole table is presentation
            // over facts core has already settled, and a view that kept a copy of the number would be
            // a second answer to a question core owns.
            Assert.That(
                offenders,
                Is.Empty,
                $"{view.Name} keeps something named for points rather than for a width.");
        }
    }

    // ---- Doors (guard rows) ----------------------------------------------------------------------

    [Test]
    public void Player_SegmentDrawsThePaletteColour()
    {
        GameObject bare = BareBar();

        try
        {
            _bar.Set(0.5f);
            _bar.SetGrantedShield(0.25f);

            // Palette.PlayerBlocked: already this bar's "that one did not land" cyan, which is exactly
            // what a granted shield is about to make true. It is a brightened Palette.Player, so it
            // stays inside GD §16.4's player family and is still separable from the fill at the seam
            // the two share — which a second colour of the same value would not be.
            Assert.That(Segment().color, Is.EqualTo(Palette.PlayerBlocked));
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    [Test]
    public void Player_SegmentIgnoresANonFiniteFraction()
    {
        GameObject bare = BareBar();

        try
        {
            _bar.Set(1f);

            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                _bar.SetGrantedShield(0.25f);
                _bar.SetGrantedShield(bad);

                // Read as *none* rather than clamped. Mathf.Clamp01 is two comparisons and every
                // comparison against NaN is false, so a NaN passes straight through both bounds and
                // into an anchor — and a RectTransform with a NaN anchor is a canvas that stops
                // laying out, with nothing reported (AR §18.3).
                Assert.That(Segment().enabled, Is.False, $"{bad} reached the rect.");
            }
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    [Test]
    public void Player_DropsItsTwoNewSubscriptions()
    {
        StartRun();
        BuildHud();

        Object.DestroyImmediate(_hud);
        _spawned.Remove(_hud);
        _hud = null;

        // A HUD destroyed before its scope — a scene reload, an arena opened without a run — would
        // otherwise stay in both subscriber lists and be handed events for a component Unity has
        // killed. The nine subscriptions are dropped together; these are the two that are new.
        Assert.DoesNotThrow(() => _hub.Publish(new ShieldGranted(35f, 35f, 5f)));
        Assert.DoesNotThrow(() => _hub.Publish(new ShieldGrantExpired(35f, 0f)));
    }

    [Test]
    public void Prefab_IsDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        var bar = prefab.GetComponentInChildren<HpBarView>(true);

        Assert.That(bar, Is.Not.Null, "HpBarView did not load off Hud.prefab (Traps §5).");

        // Read back through SerializedObject rather than reflection, because that is what reads what
        // the asset actually stored rather than what a field initialiser would produce on a fresh
        // instance (Traps §7).
        var serialized = new SerializedObject(bar);

        var segment = serialized.FindProperty("_shieldSegment").objectReferenceValue as Image;

        Assert.That(segment, Is.Not.Null, "The HP bar has no granted-shield segment wired.");

        Assert.That(
            segment.enabled,
            Is.False,
            "The prefab ships the segment on, so a run with no grant opens showing one.");

        // Still under the safe area, like everything else on this prefab: `includeInactive` is
        // mandatory rather than defensive, because every object on a prefab *asset* reports
        // activeInHierarchy false — the default overload answers null for a correctly dressed prefab,
        // which cost M3-10a a red row.
        Assert.That(
            segment.GetComponentInParent<Soulvail.Game.Controls.SafeAreaFitter>(includeInactive: true),
            Is.Not.Null,
            "The segment is not under a SafeAreaFitter, so a notch can cover it.");

        Assert.That(
            new SerializedObject(bar).FindProperty("_blockedSeconds").floatValue,
            Is.EqualTo(0.06f),
            "The blocked flash's timing moved, and this task took a segment and nothing else.");
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>The bar under test, off the shipped prefab, with no presenter and no run.</summary>
    /// <remarks>
    /// The geometry rows are about <c>HpBarView</c> alone and a whole run would be scaffolding for a
    /// rect. The prefab rather than a hand-built bar, so the claims are about the asset the game
    /// draws (Traps §5).
    /// </remarks>
    private GameObject BareBar()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        GameObject hud = Object.Instantiate(prefab);

        _bar = hud.GetComponentInChildren<HpBarView>(true);

        Assert.That(_bar, Is.Not.Null, "HpBarView did not load off Hud.prefab (Traps §5).");

        return hud;
    }

    /// <summary>The shipped HUD, injected and started — the screen under test is the asset.</summary>
    private void BuildHud()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        _hud = Object.Instantiate(prefab);
        _spawned.Add(_hud);

        // Pinned, because a Canvas instantiated in EditMode has never been driven by its own
        // CanvasScaler — XpBarViewTests sets it for the same reason.
        _hud.GetComponent<Canvas>().scaleFactor = 1f;

        _presenter = _hud.GetComponent<HudPresenter>();
        _bar = _hud.GetComponentInChildren<HpBarView>(true);
        _ring = _hud.GetComponentInChildren<ShieldRingView>(true);

        Assert.That(_presenter, Is.Not.Null, "HudPresenter did not load off Hud.prefab (Traps §5).");
        Assert.That(_bar, Is.Not.Null, "HpBarView did not load off Hud.prefab (Traps §5).");
        Assert.That(_ring, Is.Not.Null, "ShieldRingView did not load off Hud.prefab (Traps §5).");

        _presenter.Construct(_hub, _session, new SceneLoader(), Track(new InputAdapter()));

        Invoke(_presenter, "Start");
    }

    private Image Segment() => Field<Image>(_bar, "_shieldSegment");

    /// <summary>How wide the segment is, as a fraction of the bar.</summary>
    private float Width()
    {
        var rect = (RectTransform)Segment().transform;

        return rect.anchorMax.x - rect.anchorMin.x;
    }

    /// <summary>Whether a field's name is about points rather than about a width.</summary>
    private static bool NamesPoints(string name) =>
        name.Contains("Hp", StringComparison.Ordinal)
        || name.Contains("MaxHealth", StringComparison.Ordinal)
        || name.Contains("Points", StringComparison.OrdinalIgnoreCase)
        || name.Contains("HitPoints", StringComparison.OrdinalIgnoreCase);

    private void StartRun()
    {
        IReadOnlyList<SkillSpec> skills = new[]
        {
            Node(NodeOne),
            Node(NodeTwo),
            Node(NodeThree),
            Node(NodeFour),
            Node(NodeFive),
        };

        _catalog = new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk() },
            new[] { Mode() },
            skills,
            new[] { Tree() });

        _session = new RunSession(
            _catalog,
            _random,
            _hub,
            new RecordingIntents(),
            new RunRecorder(_random, _clock, _hub),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        _config = new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            _random.Seed,
            1,
            SpawnPlan.Empty,
            null);

        _session.Start(_config);
    }

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);

        return disposable;
    }

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, Private).Invoke(target, null);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);

    private static SkillSpec Node(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new IEffect[]
        {
            new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f),
        });

    private static SkillTreeSpec Tree() => new SkillTreeSpec(
        new ContentId(TreeId),
        new ContentId(OathboundId),
        new[]
        {
            Branch('a', new[] { NodeOne, NodeTwo, NodeThree }),
            Branch('b', new[] { NodeFour }),
            Branch('c', new[] { NodeFive }),
        });

    private static SkillBranchSpec Branch(char letter, string[] tier)
    {
        var ids = new ContentId[tier.Length];

        for (int i = 0; i < tier.Length; i++)
        {
            ids[i] = new ContentId(tier[i]);
        }

        return new SkillBranchSpec(
            new LocKey($"branch.{letter}"),
            new IReadOnlyList<ContentId>[] { ids });
    }

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(AegisPoints, 3f, 1f));

    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    private static ModeSpec Mode()
    {
        var scaling = new ScalingSpec(
            new BudgetCurve(20f, 6f, 0.04f),
            new WaveCurve(2, 1000, 2, 2),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));

        return new ModeSpec(
            new ContentId(ModeId),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            scaling,
            new XpCurve(20f, 12f, 1.4f),
            new[] { new RosterEntry(new ContentId(HuskId), 1) },
            new[] { new ContentId(ArenaId) });
    }

    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

[TestFixture]
public sealed class SkillArcTests
{
    private const string ModeId = "mode.test";
    private const string HuskId = "enemy.husk";
    private const string ArenaId = "arena.pillars";
    private const int DeviceCap = 28;
    private static readonly ContentId VolleyId = new ContentId("skill.ranger.volley");
    private readonly List<SkillCast> _casts = new List<SkillCast>();
    private readonly List<float> _castTimes = new List<float>();
    private GameObject _hud;
    private GameObject _eventRoot;
    private SkillArc _arc;
    private RunSession _session;
    private SkillArcInput _input;
    private ContentCatalog _catalog;
    private PathSpec _path;
    private SkillSpec[] _extras;
    private readonly Buzz _buzz = new Buzz();
    private float _observedTime;
    private RunRecorder _recorder;
    private RunSnapshot _saved;
    private int _target;
    private int _volleyArrows;

    [TearDown]
    public void TearDown()
    {
        if (_hud != null)
            Object.DestroyImmediate(_hud);
        if (_eventRoot != null)
            Object.DestroyImmediate(_eventRoot);
        _casts.Clear();
        _castTimes.Clear();
        _buzz.Count = 0;
        _target = 0;
        _volleyArrows = 0;
    }

    [Test]
    public void Arc_OneCirclePerActive_InOrder()
    {
        Build(extraActives: 2);
        Assert.That(_arc.Actives.Select(c => c.Id), Is.EqualTo(new[] { VolleyId, new ContentId("skill.marksman.rain-of-arrows"), new ContentId("skill.marksman.binding-shot"), new ContentId("skill.marksman.hunters-snare"), new ContentId("skill.marksman.killshot"), _extras[0].Id }));
        Assert.That(_arc.Passives.Count, Is.EqualTo(4), "two Ranger passives plus the two Marksman passives");
    }

    [Test]
    public void Arc_PathHiddenUntilChosen()
    {
        Build(chosen: false, extraActives: 2);
        Assert.That(_arc.Actives.Select(c => c.Id), Is.EqualTo(new[] { VolleyId }));
        Assert.That(_arc.Passives.Count, Is.EqualTo(2));
        _session.ChoosePath(_path.Id);
        _arc.Refresh();
        Assert.That(_arc.Actives.Count, Is.EqualTo(6));
        Assert.That(_arc.Passives.Count, Is.EqualTo(4));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void Arc_SpacingAndBounds(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 centre = HudLayout.ArcCentreDp(i, count) + new Vector2(360f, 0f);
            Assert.That(centre.x - 26f, Is.GreaterThanOrEqualTo(360f * 0.45f));
            Assert.That(centre.x + 26f, Is.LessThanOrEqualTo(360f));
            Assert.That(centre.y - 26f, Is.GreaterThanOrEqualTo(0f));
            Assert.That(centre.y + 26f, Is.LessThanOrEqualTo(780f));
            for (int j = 0; j < i; j++)
            {
                Assert.That(Vector2.Distance(centre, HudLayout.ArcCentreDp(j, count) + new Vector2(360f, 0f)),
                    Is.GreaterThanOrEqualTo(60f - 0.001f));
            }
        }
    }

    [Test]
    public void Arc_MirrorIncludesRollAndPassives()
    {
        Build(extraActives: 3);
        var before = _arc.Actives.Concat(_arc.Passives).Select(b => ((RectTransform)b.transform).anchoredPosition).ToArray();
        var so = new SerializedObject(_arc);
        so.FindProperty("_leftHanded").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        _arc.Refresh();
        var after = _arc.Actives.Concat(_arc.Passives).Select(b => (RectTransform)b.transform).ToArray();
        for (int i = 0; i < after.Length; i++)
        {
            Assert.That(after[i].anchorMin.x, Is.Zero);
            Assert.That(after[i].anchoredPosition.x, Is.EqualTo(-before[i].x));
            Assert.That(after[i].anchoredPosition.y, Is.EqualTo(before[i].y));
        }
        var roll = (RectTransform)_hud.GetComponentInChildren<SkillButton>(true).transform;
        Assert.That(roll.anchorMin.x, Is.Zero);
    }

    [Test]
    public void Circle_CoolingShowsSweepAndSeconds()
    {
        Build();
        TakeExtra();
        _session.CastSkillNow(_extras[0].Id);
        _arc.Refresh();
        Assert.That(Extra().Sweep, Is.EqualTo(1f));
        Assert.That(Extra().Seconds, Is.EqualTo("8"));
        Assert.That(Extra().ReadyGlow, Is.False);
    }

    [Test]
    public void Circle_ReadyGlows()
    {
        Build();
        TakeExtra();
        Assert.That(Extra().ReadyGlow, Is.True);
        Assert.That(Extra().Sweep, Is.Zero);
        Assert.That(Extra().Seconds, Is.Empty);
    }

    [Test]
    public void Circle_AutoBadge()
    {
        Build();
        TakeExtra();
        Assert.That(Extra().AutoBadge, Is.True);
        _input.Toggle(_extras[0].Id);
        _input.Poll();
        _arc.Refresh();
        Assert.That(Extra().AutoBadge, Is.False);
    }

    [Test]
    public void Circle_LockedAboveLevel_ShowsLevel()
    {
        Build(level: 12);
        Assert.That(Extra().IsLocked, Is.True);
        Assert.That(Extra().LevelLabel, Is.EqualTo("Lv 14"));
        Tap(Extra());
        _input.Poll();
        Assert.That(_casts, Is.Empty);
    }

    [Test]
    public void Tap_CastsEvenWhenAuto()
    {
        Build();
        TakeExtra();
        Tap(Extra());
        Assert.That(_casts, Is.Empty, "pointer handler bypassed CommandPhase");
        _input.Poll();
        Assert.That(_casts.Count, Is.EqualTo(1));
        Assert.That(_casts[0].WasAuto, Is.False);
        Assert.That(_session.State.IsAutoCast(_extras[0].Id), Is.True);
        _input.Poll();
        Assert.That(_casts.Count, Is.EqualTo(1), "tap survived polling");
    }

    [Test]
    public void Tap_Cooling_Refused()
    {
        Build();
        TakeExtra();
        _session.CastSkillNow(_extras[0].Id);
        Tap(Extra());
        _input.Poll();
        Assert.That(_casts.Count, Is.EqualTo(1));
    }

    [TestCase(0.449f, false)]
    [TestCase(0.45f, true)]
    [TestCase(0.9f, true)]
    public void LongPress_TogglesAuto(float held, bool toggles)
    {
        Build();
        TakeExtra();
        SkillArcButton button = Extra();
        button.OnPointerDown(Pointer());
        typeof(SkillArcButton).GetField("_pressedAt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(button, 0f);
        button.StepHold(held);
        _input.Poll();
        Assert.That(_session.State.IsAutoCast(button.Id), Is.EqualTo(!toggles));
        Assert.That(_buzz.Count, Is.EqualTo(toggles ? 1 : 0));
        if (toggles)
        {
            button.StepHold(held + 1f);
            button.OnPointerUp(Pointer());
            _input.Poll();
            Assert.That(_session.State.IsAutoCast(button.Id), Is.False);
            Assert.That(_casts, Is.Empty, "long release also tapped");
            Assert.That(_buzz.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public void LongPress_CancelledAndDisabledNeverCast()
    {
        Build();
        TakeExtra();
        Extra().OnPointerDown(Pointer());
        Extra().OnPointerExit(Pointer());
        Extra().StepHold(Time.unscaledTime + 1f);
        Extra().OnPointerUp(Pointer());
        _input.Poll();
        Assert.That(_casts, Is.Empty);
        Assert.That(_session.State.IsAutoCast(_extras[0].Id), Is.True);
    }

    [Test]
    public void LongPress_RespectsTheHapticsSetting()
    {
        Build();
        TakeExtra();
        var input = new SkillArcInput(_session, _session, _buzz, HapticsSettings.InMemory(false));
        input.Toggle(_extras[0].Id);
        input.Poll();
        Assert.That(_session.State.IsAutoCast(_extras[0].Id), Is.False);
        Assert.That(_buzz.Count, Is.Zero);
    }

    [Test]
    public void AutoCast_WithinGap_Deferred()
    {
        Build(extraActives: 2);
        TakeExtra(0);
        TakeExtra(1);
        Tick(0.1f);
        Assert.That(_casts.Count, Is.EqualTo(1));
        Tick(0.499f);
        Assert.That(_casts.Count, Is.EqualTo(1));
        Tick(0.002f);
        Assert.That(_casts.Count, Is.EqualTo(2));
        Assert.That(_castTimes[1] - _castTimes[0], Is.GreaterThanOrEqualTo(0.5f));
    }

    [Test]
    public void Tap_IgnoresGap_AndDefersTheNextAuto()
    {
        Build(extraActives: 3);
        for (int i = 0; i < 3; i++)
            TakeExtra(i);
        Tick(0.1f);
        Tick(0.1f);
        _session.CastSkillNow(_extras[1].Id);
        Assert.That(_casts.Count, Is.EqualTo(2));
        Assert.That(_casts[1].WasAuto, Is.False);
        Tick(0.499f);
        Assert.That(_casts.Count, Is.EqualTo(2));
        Tick(0.002f);
        Assert.That(_casts.Count, Is.EqualTo(3));
    }

    [Test]
    public void Corner_PassivesLitAndLocked()
    {
        Build(level: 12);
        ContentId steady = _path.Skills[0].Ranks.Tier(1)[0];
        _session.TakeNode(steady);
        _arc.Refresh();
        Assert.That(_arc.Passives.Single(b => b.Id == steady).IsLocked, Is.False);
        var eagle = _arc.Passives.Single(b => b.Id == new ContentId("skill.marksman.eagle-eye"));
        Assert.That(eagle.IsLocked, Is.True);
        Assert.That(eagle.LevelLabel, Is.EqualTo("Lv 14"));
    }

    [Test]
    public void Corner_NotRaycastTargets()
    {
        Build();
        Assert.That(_arc.Passives.Count, Is.GreaterThan(0));
        foreach (var passive in _arc.Passives)
            Assert.That(passive.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), Is.True);
        foreach (var active in _arc.Actives)
            Assert.That(active.GetComponent<Image>().raycastTarget, Is.True);
    }

    [Test]
    public void Corner_OverflowStaysClearOfRoll()
    {
        Vector2 roll = new Vector2(-HudLayout.RollRightDp, HudLayout.RollBottomDp);
        for (int i = 0; i < 10; i++)
        {
            Vector2 centre = HudLayout.PassiveCentreDp(i);
            Assert.That(Vector2.Distance(centre, roll), Is.GreaterThanOrEqualTo(50f));
            Assert.That(centre.x - 14f, Is.GreaterThanOrEqualTo(-360f));
            Assert.That(centre.x + 14f, Is.LessThanOrEqualTo(0f));
            Assert.That(centre.y - 14f, Is.GreaterThanOrEqualTo(0f));
        }
    }

    [Test]
    public void SixToggles_AndVolley_RestoreFromSave()
    {
        Build(extraActives: 3);
        _session.TakeNode(VolleyId);
        _input.Toggle(VolleyId);
        _input.Poll();
        var binding = new ContentId("skill.marksman.binding-shot");
        _session.TakeNode(binding);
        _input.Toggle(binding);
        _input.Poll();
        var snare = new ContentId("skill.marksman.hunters-snare");
        _session.TakeNode(snare);
        _input.Toggle(snare);
        _input.Poll();
        foreach (var skill in _extras)
        {
            _session.TakeNode(skill.Id);
            _input.Toggle(skill.Id);
            _input.Poll();
        }
        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(6));
        _recorder.Take(_session.State, 1);
        var saved = _saved;
        Assert.That(saved.ManualSkillIds.Count, Is.EqualTo(6));
        _session.End();
        _session.Start(new RunConfig(saved.ModeId, saved.CharacterId, saved.Seed, saved.StageIndex, SpawnPlan.Empty, saved));
        Assert.That(_session.State.IsAutoCast(VolleyId), Is.False);
        Assert.That(_session.State.IsAutoCast(binding), Is.False);
        Assert.That(_session.State.IsAutoCast(snare), Is.False);
        foreach (var skill in _extras)
            Assert.That(_session.State.IsAutoCast(skill.Id), Is.False);
    }

    [Test]
    public void Volley_ManualHoldsReady_AndTapConsumesNextShot()
    {
        Build();
        _session.TakeNode(VolleyId);
        _session.SetAutoCast(VolleyId, false);
        var combat = typeof(RunState).GetProperty("Combat", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_session.State);
        Volley volley = (Volley)combat.GetType().GetProperty("Volley").GetValue(combat);
        for (int i = 0; i < volley.EveryCount; i++)
            Assert.That(volley.Loose(), Is.False);
        Assert.That(volley.IsReady, Is.True);
        Assert.That(volley.Loose(), Is.False);
        _session.CastSkillNow(VolleyId);
        Assert.That(volley.Loose(), Is.True);
        Assert.That(volley.IsReady, Is.False);
        _arc.Refresh();
        Assert.That(_arc.Actives[0].AutoBadge, Is.False);
    }

    [Test]
    public void Volley_TapFiresOnNextTick_WithoutWaitingForTheWeapon()
    {
        Build(withTarget: true);
        _session.TakeNode(VolleyId);
        _session.SetAutoCast(VolleyId, false);
        var combat = typeof(RunState).GetProperty("Combat", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_session.State);
        Volley volley = (Volley)combat.GetType().GetProperty("Volley").GetValue(combat);
        for (int i = 0; i < volley.EveryCount; i++)
            volley.Loose();
        Assert.That(volley.IsReady, Is.True);
        _input.Tap(VolleyId);
        _input.Poll();
        Tick(1f / 60f);
        Assert.That(_volleyArrows, Is.GreaterThan(0));
        Assert.That(_casts.Count, Is.EqualTo(1));
        Assert.That(_casts[0].SkillId, Is.EqualTo(VolleyId));
        Assert.That(_casts[0].WasAuto, Is.False);
    }

    [Test]
    public void Volley_AutoRespectsGapAfterManualCast()
    {
        Build(withTarget: true);
        _session.TakeNode(VolleyId);
        var combat = typeof(RunState).GetProperty("Combat", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_session.State);
        Volley volley = (Volley)combat.GetType().GetProperty("Volley").GetValue(combat);
        for (int i = 0; i < volley.EveryCount; i++)
            volley.Loose();
        TakeExtra();
        _session.CastSkillNow(_extras[0].Id);
        Tick(0.1f);
        Assert.That(_volleyArrows, Is.Zero);
        Assert.That(volley.IsReady, Is.True);
        Tick(0.401f);
        Assert.That(_volleyArrows, Is.GreaterThan(0));
        Assert.That(_casts.Count, Is.EqualTo(2));
        Assert.That(_casts[1].WasAuto, Is.True);
    }

    private void Build(bool chosen = true, int level = 30, int extraActives = 1, bool withTarget = false)
    {
        var character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/_Project/Data/Characters/Ranger.asset");
        var tree = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>("Assets/_Project/Data/Trees/Ranger.asset").ToSpec();
        _path = AssetDatabase.LoadAssetAtPath<PathDefinition>("Assets/_Project/Data/Paths/Marksman.asset").ToSpec(new ContentId(character.Id));
        var skills = new List<SkillSpec>();
        foreach (string guid in AssetDatabase.FindAssets("t:SkillDefinition", new[] { "Assets/_Project/Data/Skills" }))
        {
            SkillSpec skill = AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetDatabase.GUIDToAssetPath(guid)).ToSpec();
            if (skill.Id.Value.StartsWith("skill.ranger.", StringComparison.Ordinal) || skill.Id.Value.StartsWith("skill.marksman.", StringComparison.Ordinal))
                skills.Add(skill);
        }
        var pathSkills = _path.Skills.ToList();
        if (extraActives == 3)
        {
            // Timing/save stress rows use three synthetic actives in place of Killshot;
            // six shipped skills plus three extras would exceed core's eight-skill path limit.
            pathSkills.RemoveAt(5);
        }
        _extras = new SkillSpec[extraActives];
        for (int i = 0; i < extraActives; i++)
        {
            _extras[i] = new SkillSpec(new ContentId($"skill.test.arc{i}"), new LocKey("test.skill.name"),
                new LocKey("test.skill.description"), SkillKind.Active, Array.Empty<IEffect>(),
                new ActiveSpec(8f, new TriggerSpec(new[] { new TriggerClause(TriggerField.HpFraction, TriggerComparison.AtLeast, 0f) }),
                    new IEffect[] { new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.01f) }));
            skills.Add(_extras[i]);
            pathSkills.Add(new PathSkillSpec(new SkillBranchSpec(_extras[i].NameKey,
                new[] { (IReadOnlyList<ContentId>)new[] { _extras[i].Id } }), 14));
        }
        _path = new PathSpec(_path.Id, _path.CharacterId, _path.NameKey, _path.DescriptionKey, pathSkills);
        _catalog = new ContentCatalog(new[] { character.ToSpec() }, new[] { Husk() }, new[] { Mode(true) }, skills,
            new[] { tree }, paths: new[] { _path });
        var hub = new DomainEventHub();
        hub.Subscribe<SkillCast>(cast => { _casts.Add(cast); _castTimes.Add(_observedTime); });
        hub.Subscribe<EnemySpawned>(e => _target = e.Id);
        hub.Subscribe<ProjectileFired>(e => { if (e.IsVolley) _volleyArrows++; });
        hub.Subscribe<RunSnapshotTaken>(e => _saved = e.Snapshot);
        var random = new FixedRandom(7);
        var instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);
        _session = new RunSession(_catalog, random, hub, new RecordingIntents(), (_recorder = new RunRecorder(random, new FixedClock(instant), hub)), 64, DeviceCap, 64);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, new ContentId(ModeId), new ContentId(character.Id), 7, 1,
            new RandomState(101, 102, 103, 104, 105), 90f, 0f, 0f, instant, level, 0f, level - 1,
            Array.Empty<ContentId>(), new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        var plan = withTarget ? new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(HuskId), new System.Numerics.Vector3(0f, 0f, 5f)) }) : SpawnPlan.Empty;
        _session.Start(new RunConfig(saved.ModeId, saved.CharacterId, 7, 1, plan, saved));
        if (chosen)
            _session.ChoosePath(_path.Id);
        _input = new SkillArcInput(_session, _session, _buzz, HapticsSettings.InMemory(true));
        _hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/Hud.prefab"));
        _arc = _hud.GetComponentInChildren<SkillArc>(true);
        var localizer = new TableLocalizer(AssetDatabase.LoadAssetAtPath<LocalizationTable>("Assets/_Project/Data/Localisation/English.asset"));
        _arc.Construct(_session, _catalog, new SkillIconBook(new Dictionary<ContentId, Sprite>()),
            new CharacterLookBook(new Dictionary<ContentId, CharacterLook> { [saved.CharacterId] = character.ToLook() }), _input, localizer);
    }

    private void TakeExtra(int index = 0) { _session.TakeNode(_extras[index].Id); _arc.Refresh(); }
    private SkillArcButton Extra(int index = 0) => _arc.Actives.Single(b => b.Id == _extras[index].Id);
    private PointerEventData Pointer()
    {
        if (_eventRoot == null)
            _eventRoot = new GameObject("EventSystem", typeof(EventSystem));
        return new PointerEventData(_eventRoot.GetComponent<EventSystem>()) { pointerId = 42 };
    }
    private void Tap(SkillArcButton button) { button.OnPointerDown(Pointer()); button.OnPointerUp(Pointer()); }
    private void Tick(float dt)
    {
        _observedTime = _session.State.Time + dt;
        var snapshot = new WorldSnapshot(64) { Dt = dt, SpawnPoints = new[] { new System.Numerics.Vector3(12f, 0f, 0f), new System.Numerics.Vector3(-12f, 0f, 0f), new System.Numerics.Vector3(0f, 0f, 12f) } };
        if (_target > 0)
        {
            ref EnemySense sense = ref snapshot.AddEnemy();
            sense.Id = _target;
            sense.Position = new System.Numerics.Vector3(0f, 0f, 5f);
            sense.HasLineOfSight = true;
        }
        _session.Tick(snapshot);
    }
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

    private static ModeSpec Mode(bool free = false)
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

            // CH §5.2's shipped levelling curve, ToReach(N) = 20 + 12·N^1.4, written out rather
            // than taken from Tests.Core's `Scalings` — that helper is `internal` and this fixture
            // is in another assembly.
            new XpCurve(20f, 12f, 1.4f),
            new[] { new RosterEntry(new ContentId(HuskId), 1) },
            new[] { new ArenaEntry(new ContentId(ArenaId)) }, hasFreePick: free);
    }

    private sealed class Buzz : IVibrator
    {
        public int Count { get; set; }
        public void Pulse(int milliseconds, float amplitude01) => Count++;
    }
}

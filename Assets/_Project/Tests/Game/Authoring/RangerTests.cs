using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// The Ranger as a class you can pick: its asset against RS-03c's table, its look, its place in the
/// boot catalog, and the fourth card that draws it. RS-03c rules 1–3 and 6 —
/// <c>EmberwrightTests</c>' shape, one class on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every number here is the spec's table, and the table is an engineering call</b> inside GD
/// §6.1's bands, which RS-03e measures and moves. <see cref="Ranger_NumbersAreTheTables"/> pins the
/// table; <see cref="Ranger_StaysInsideTheBands"/> pins the bands, so a retune that stays in them
/// reddens the first row and not the second.
/// </para>
/// <para>
/// <b>The card row reads the prefab and the Menu scene as files</b>, and does the layout arithmetic
/// from anchors and offsets rather than asking a canvas: an EditMode fixture has no screen for a
/// <c>CanvasScaler</c> to scale against, and at the reference resolution a match of 0.5 scales by
/// exactly 1, so the reference rectangle is the canvas.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RangerTests
{
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string ClassSelectPath = "Assets/_Project/Prefabs/UI/ClassSelect.prefab";
    private const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";
    private const string BodyPath = "Assets/_Project/Prefabs/Player/Bodies/Ranger.prefab";
    private const string ArrowPath = "Assets/_Project/Prefabs/Projectiles/Arrow.prefab";

    private const string RangerId = "character.ranger";

    private static readonly string[] RosterIds =
    {
        "character.oathbound", "character.gravecaller", "character.emberwright", RangerId,
    };

    /// <summary>GD §6.1's bands for a class.</summary>
    private const float HpLow = 70f;

    private const float HpHigh = 140f;
    private const float SpeedLow = 3.0f;
    private const float SpeedHigh = 3.4f;

    /// <summary>GD §6.2's hits to kill a stage-1 Husk.</summary>
    private const int BandLow = 3;

    private const int BandHigh = 5;

    /// <summary>The Menu canvas's <c>CanvasScaler</c>, as <c>Menu.unity</c> authors it.</summary>
    private const float ReferenceWidth = 1920f;

    private const float ReferenceHeight = 1080f;

    private const int EnemyCapacity = 8;
    private const float Tolerance = 1e-5f;

    // ---- Rule 1: in the catalog, fourth, free ------------------------------------------------------

    [Test]
    public void Ranger_ConvertsAndIsInTheCatalog()
    {
        CharacterSpec spec = Ranger();

        Assert.That(spec.Id.Value, Is.EqualTo(RangerId), "the owner's name, 2026-09-25.");
        Assert.That(spec.NameKey.Key, Is.EqualTo("character.ranger.name"));
        Assert.That(spec.DescriptionKey.Key, Is.EqualTo("character.ranger.description"));

        ContentCatalog catalog = BootCatalog();

        Assert.That(catalog.Characters.Count, Is.EqualTo(4), "CH §3's three and the Ranger boot.");

        for (int i = 0; i < RosterIds.Length; i++)
        {
            Assert.That(catalog.Characters[i].Id.Value, Is.EqualTo(RosterIds[i]), $"catalog slot {i}.");
        }

        Assert.That(catalog.Character(new ContentId(RangerId)).Unlock, Is.Null,
            "free while it is built: no price, so owned from the start (the owner's ruling).");
    }

    // ---- Rule 2: the table, and the bands ----------------------------------------------------------

    [Test]
    public void Ranger_NumbersAreTheTables()
    {
        CharacterSpec spec = Ranger();

        Assert.That(spec.MaxHp, Is.EqualTo(90f).Within(Tolerance), "HP");
        Assert.That(spec.Movement.Speed, Is.EqualTo(3.2f).Within(Tolerance), "Speed");

        WeaponSpec bow = spec.Weapon;

        Assert.That(bow.Kind, Is.EqualTo(WeaponKind.Projectile), "Bow: a projectile.");
        Assert.That(bow.Damage, Is.EqualTo(13f).Within(Tolerance), "Bow: 13 damage.");
        Assert.That(bow.SwingsPerSecond, Is.EqualTo(2.2f).Within(Tolerance), "Bow: 2.2 shots/s.");
        Assert.That(bow.Range, Is.EqualTo(10f).Within(Tolerance), "Bow: 10 m.");
        Assert.That(bow.DamageFrame, Is.EqualTo(0.72f).Within(Tolerance), "Bow: damage frame 0.72.");
        Assert.That(bow.ShotSpeed, Is.EqualTo(30f).Within(Tolerance), "Bow: 30 m/s.");
        Assert.That(bow.ShotRadius, Is.EqualTo(0.5f).Within(Tolerance), "Bow: radius 0.5.");
        Assert.That(bow.ConeAngleDeg, Is.EqualTo(360f).Within(Tolerance), "WeaponSpec's one spelling for a projectile.");
        Assert.That(bow.ShotSpread, Is.Zero, "Reserved (M5-01).");
        Assert.That(bow.FiresWhileMoving, Is.False, "Fires while moving: no (RS-03a).");

        Assert.That(spec.Targeting.AcquireRange, Is.EqualTo(12f).Within(Tolerance), "Acquire: 12 m.");
        Assert.That(spec.Focus.Delay, Is.EqualTo(0.4f).Within(Tolerance), "Focus: 0.4 s.");
        Assert.That(spec.Focus.RampTime, Is.EqualTo(1f).Within(Tolerance), "Focus: 1 s.");
        Assert.That(spec.Focus.MaxMultiplier, Is.EqualTo(1.3f).Within(Tolerance), "Focus: ×1.3.");

        MovementSkillSpec roll = spec.MovementSkill;

        Assert.That(roll.Kind, Is.EqualTo(MovementSkillKind.Charge), "a roll is a Charge.");
        Assert.That(roll.Distance, Is.EqualTo(5f).Within(Tolerance), "Roll: 5 m.");
        Assert.That(roll.Duration, Is.EqualTo(0.3f).Within(Tolerance), "Roll: 0.3 s.");
        Assert.That(roll.Cooldown, Is.EqualTo(2.5f).Within(Tolerance), "Roll: cooldown 2.5 s.");
        Assert.That(roll.Damage, Is.Zero, "Roll: no damage, so it touches nobody (RS-03a rule 8).");
        Assert.That(roll.Knockback, Is.Zero, "Roll: no knockback.");
        Assert.That(roll.IFrameTrail, Is.EqualTo(0.05f).Within(Tolerance), "Roll: i-frame trail 0.05 s.");

        Assert.That(spec.Volley, Is.Not.Null, "the fan the owner picked.");
        Assert.That(spec.Volley.Arrows, Is.EqualTo(3), "Volley: 3 arrows.");
        Assert.That(spec.Volley.FanAngleDeg, Is.EqualTo(30f).Within(Tolerance), "Volley: 30°.");
        Assert.That(spec.Volley.DamageMultiplier, Is.EqualTo(1.5f).Within(Tolerance), "Volley: ×1.5.");

        Assert.That(spec.Shield, Is.Null, "no shield.");
        Assert.That(spec.Minions, Is.Null, "no minions.");
        Assert.That(spec.Kindling, Is.Null, "no Kindling.");
        Assert.That(spec.Veilrot, Is.Null, "no Veilrot relationship: it meets the Veil neutrally.");

        // The table's own arithmetic: resting DPS is what the card draws, and planted DPS is the
        // Focus ramp's ×1.3 — under the Oathbound's resting 39.
        float resting = bow.Damage * bow.SwingsPerSecond;
        CharacterSpec oathbound = Character("Assets/_Project/Data/Characters/Oathbound.asset");

        Assert.That(MathF.Round(resting), Is.EqualTo(29f), "the card's 29 DPS.");
        Assert.That(MathF.Round(resting * spec.Focus.MaxMultiplier), Is.EqualTo(37f), "37 DPS planted.");
        Assert.That(oathbound.Weapon.Damage * oathbound.Weapon.SwingsPerSecond, Is.EqualTo(39f).Within(Tolerance),
            "the Oathbound's 39, the table's comparison.");
    }

    [Test]
    public void Ranger_StaysInsideTheBands()
    {
        CharacterSpec spec = Ranger();

        Assert.That(spec.MaxHp, Is.InRange(HpLow, HpHigh), "GD §6.1's HP band.");
        Assert.That(spec.Movement.Speed, Is.InRange(SpeedLow, SpeedHigh), "GD §6.1's speed band.");

        IReadOnlyList<string> enemies = ContentValidationTests.PathsOf<EnemyDefinition>();

        Assert.That(enemies, Is.Not.Empty, "the premise: the sweep found the roster.");

        foreach (string path in enemies)
        {
            EnemySpec enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path).ToSpec();

            Assert.That(spec.Movement.Speed, Is.GreaterThan(enemy.MoveSpeed), $"a kiter outruns {enemy.Id}.");
        }

        float husk = HuskHpAt(1);
        int hits = (int)Math.Ceiling(husk / spec.Weapon.Damage);

        Assert.That(husk, Is.EqualTo(36f).Within(Tolerance), "the premise: a stage-1 Husk is 36 HP.");
        Assert.That(hits, Is.EqualTo(3), "ceil(36 / 13), with no tree and no Focus.");
        Assert.That(hits, Is.InRange(BandLow, BandHigh), "GD §6.2's three to five, at the opening.");
    }

    // ---- Rule 3: the look --------------------------------------------------------------------------

    [Test]
    public void Ranger_WearsItsBodyAndFliesArrows()
    {
        CharacterLook look = Definition().ToLook();

        var body = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
        var arrow = AssetDatabase.LoadAssetAtPath<ProjectileView>(ArrowPath);

        Assert.That(body, Is.Not.Null, $"No body prefab at {BodyPath}.");
        Assert.That(arrow, Is.Not.Null, $"No ProjectileView on {ArrowPath}.");

        Assert.That(look.Body, Is.SameAs(body), "RS-02b's Bodies/Ranger.");
        Assert.That(look.Projectile, Is.SameAs(arrow), "RS-02c's Arrow.prefab.");
    }

    // ---- Rule 6: four cards on the Menu --------------------------------------------------------------

    [Test]
    public void Cards_FourFitTheMenu()
    {
        // The reference, read off the Menu scene rather than restated: one CanvasScaler, 1920 × 1080
        // at match 0.5, which is the only canvas ClassSelect.prefab is ever drawn under.
        string[] menu = File.ReadAllLines(MenuScenePath);

        Assert.That(Lines(menu, "m_ReferenceResolution:"), Is.EqualTo(new[] { "m_ReferenceResolution: {x: 1920, y: 1080}" }));
        Assert.That(Lines(menu, "m_MatchWidthOrHeight:"), Is.EqualTo(new[] { "m_MatchWidthOrHeight: 0.5" }));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClassSelectPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {ClassSelectPath}.");

        var root = (RectTransform)prefab.transform;

        Assert.That(root.anchorMin, Is.EqualTo(Vector2.zero), "the screen fills its canvas.");
        Assert.That(root.anchorMax, Is.EqualTo(Vector2.one));
        Assert.That(root.offsetMin, Is.EqualTo(Vector2.zero));
        Assert.That(root.offsetMax, Is.EqualTo(Vector2.zero));

        var canvas = new Rect(-ReferenceWidth / 2f, -ReferenceHeight / 2f, ReferenceWidth, ReferenceHeight);

        var cards = (ClassCard[])typeof(ClassSelectPresenter)
            .GetField("_cards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(prefab.GetComponent<ClassSelectPresenter>());

        Assert.That(cards, Has.Length.EqualTo(4), "a card for each of the four classes.");

        var rects = new Rect[cards.Length];

        for (int i = 0; i < cards.Length; i++)
        {
            Assert.That(cards[i], Is.Not.Null, $"_cards[{i}] is an empty slot.");

            var rect = (RectTransform)cards[i].transform;

            Assert.That(rect.parent, Is.SameAs(root), $"card {i} sits directly on the screen.");
            Assert.That(rect.localScale, Is.EqualTo(UnityEngine.Vector3.one), $"card {i} is unscaled.");
            Assert.That(rect.localRotation, Is.EqualTo(Quaternion.identity), $"card {i} is unrotated.");

            rects[i] = In(rect, canvas);

            Assert.That(Contains(canvas, rects[i]), Is.True, $"card {i} at {rects[i]} leaves the 1920 × 1080 reference.");

            for (int j = 0; j < i; j++)
            {
                Assert.That(cards[j], Is.Not.SameAs(cards[i]), $"cards {j} and {i} are one card.");
                Assert.That(rects[i].Overlaps(rects[j]), Is.False, $"card {i} at {rects[i]} overlaps card {j} at {rects[j]}.");
            }
        }

        // And nothing else on the screen is under one.
        foreach (string other in new[] { "Title", "Balance", "Back" })
        {
            var rect = (RectTransform)root.Find(other);

            Assert.That(rect, Is.Not.Null, $"the screen has no {other}.");

            Rect drawn = In(rect, canvas);

            for (int i = 0; i < rects.Length; i++)
            {
                Assert.That(drawn.Overlaps(rects[i]), Is.False, $"{other} at {drawn} overlaps card {i} at {rects[i]}.");
            }
        }
    }

    // ---- Fixtures ----------------------------------------------------------------------------------

    private static CharacterDefinition Definition()
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(RangerPath);

        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {RangerPath}.");

        return definition;
    }

    private static CharacterSpec Ranger() => Definition().ToSpec();

    private static CharacterSpec Character(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {path}.");

        return definition.ToSpec();
    }

    /// <summary>
    /// A Husk's maximum hit points at <paramref name="stage"/>, through the shipped
    /// <see cref="DepthScaling"/> and <c>Descent.asset</c>'s own curve — <c>EmberwrightTests.HuskHpAt</c>.
    /// </summary>
    private static float HuskHpAt(int stage)
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        EnemyAgent husk = registry.Spawn(AssetDatabase.LoadAssetAtPath<EnemyDefinition>(HuskPath).ToSpec(), Vector3.Zero);

        new DepthScaling(AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath).ToSpec().Scaling).Apply(husk, stage);

        return husk.Health.MaxHp.Value;
    }

    /// <summary>A catalog built from <c>BootScope.prefab</c>'s own <c>_characters</c> list.</summary>
    private static ContentCatalog BootCatalog()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {BootScopePath}.");

        var scope = prefab.GetComponent<BootScope>();

        Assert.That(scope, Is.Not.Null, "BootScope did not load off its own prefab (Traps §5).");

        SerializedProperty characters = new SerializedObject(scope).FindProperty("_characters");
        var specs = new List<CharacterSpec>(characters.arraySize);

        for (int i = 0; i < characters.arraySize; i++)
        {
            var definition = characters.GetArrayElementAtIndex(i).objectReferenceValue as CharacterDefinition;

            Assert.That(definition, Is.Not.Null, $"_characters[{i}] is an empty slot.");

            specs.Add(definition.ToSpec());
        }

        return new ContentCatalog(specs);
    }

    /// <summary>Every trimmed line of <paramref name="lines"/> that starts with <paramref name="field"/>.</summary>
    private static string[] Lines(string[] lines, string field)
    {
        var found = new List<string>();

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith(field, StringComparison.Ordinal))
            {
                found.Add(trimmed);
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// Where <paramref name="child"/> is drawn inside <paramref name="parent"/>, a rectangle in the
    /// parent's own space: the anchors placed on the parent, then the offsets added. What a
    /// <c>RectTransform</c> does, without asking a canvas to have done it.
    /// </summary>
    private static Rect In(RectTransform child, Rect parent)
    {
        Vector2 anchorMin = parent.min + Vector2.Scale(child.anchorMin, parent.size);
        Vector2 anchorMax = parent.min + Vector2.Scale(child.anchorMax, parent.size);

        return Rect.MinMaxRect(
            anchorMin.x + child.offsetMin.x,
            anchorMin.y + child.offsetMin.y,
            anchorMax.x + child.offsetMax.x,
            anchorMax.y + child.offsetMax.y);
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.xMin >= outer.xMin && inner.yMin >= outer.yMin && inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;
}

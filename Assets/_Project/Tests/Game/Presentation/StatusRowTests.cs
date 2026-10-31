using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>MK-05: the status row under the HP bar, drawn off the shipped HUD prefab.</summary>
[TestFixture]
public sealed class StatusRowTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    private GameObject _hud;
    private StatusRow _row;

    [SetUp]
    public void BuildRow()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");
        _hud = Object.Instantiate(prefab);
        _row = _hud.GetComponentInChildren<StatusRow>(true);
        Assert.That(_row, Is.Not.Null, "The HUD has no StatusRow.");
    }

    [TearDown]
    public void Destroy() => Object.DestroyImmediate(_hud);

    [Test]
    public void Row_SweepAndStacks()
    {
        var aim = new ContentId(SteadyAim.StatusKey);
        _row.Draw(0, new StatusEntry(new ContentId("status.test.slow"), false, 1.5f, 6f, 1));
        _row.Draw(1, new StatusEntry(aim, true, float.PositiveInfinity, float.PositiveInfinity, 4));
        _row.HideFrom(2);

        Assert.That(_row.Sweep(0).enabled, Is.True);
        Assert.That(_row.Sweep(0).type, Is.EqualTo(Image.Type.Filled));
        Assert.That(_row.Sweep(0).sprite, Is.Not.Null, "a filled image needs a sprite to sweep");
        Assert.That(_row.Sweep(0).fillAmount, Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(_row.Stacks(0).enabled, Is.False, "one stack shows no count");

        Assert.That(_row.Sweep(1).enabled, Is.False, "untimed shows no sweep");
        Assert.That(_row.Stacks(1).enabled, Is.True);
        Assert.That(_row.Stacks(1).text, Is.EqualTo("4"));
        Assert.That(_row.Icon(1).sprite, Is.Not.Null, "Steady Aim has an icon");

        Assert.That(_row.IsShown(0) && _row.IsShown(1), Is.True);
        Assert.That(_row.IsShown(2), Is.False);
        _row.HideFrom(1);
        Assert.That(_row.IsShown(1), Is.False);

        RectTransform first = _row.Rim(0).rectTransform;
        RectTransform second = _row.Rim(1).rectTransform;
        float px = first.sizeDelta.x / HudLayout.StatusSizeDp;
        Assert.That(px, Is.GreaterThan(0f));
        Assert.That(first.anchoredPosition.x, Is.EqualTo(HudLayout.BarLeftDp * px).Within(0.01f));
        Assert.That(second.anchoredPosition.x - first.anchoredPosition.x,
            Is.EqualTo((HudLayout.StatusSizeDp + HudLayout.StatusGapDp) * px).Within(0.01f));
        Assert.That(first.anchoredPosition.y, Is.EqualTo(-HudLayout.SkillRowTopDp * px).Within(0.01f));
    }

    [Test]
    public void Row_RimColours()
    {
        _row.Draw(0, new StatusEntry(new ContentId("status.test.slow"), false, 1f, 1f, 1));
        _row.Draw(1, new StatusEntry(new ContentId("status.test.haste"), true, 1f, 1f, 1));

        Assert.That(_row.Rim(0).color, Is.EqualTo(StatusRow.DebuffRim));
        Assert.That(_row.Rim(1).color, Is.EqualTo(StatusRow.BuffRim));
        Assert.That(StatusRow.BuffRim, Is.EqualTo(Palette.Player));
        Color debuff = StatusRow.DebuffRim;
        Assert.That(debuff.b, Is.GreaterThan(debuff.g), "violet, not red");
        Assert.That(debuff.b, Is.GreaterThanOrEqualTo(debuff.r), "violet, not red");
        Assert.That(debuff, Is.Not.EqualTo(Palette.Danger));
    }
}

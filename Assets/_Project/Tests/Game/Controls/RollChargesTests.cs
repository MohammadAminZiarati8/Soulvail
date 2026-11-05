using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Game.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Soulvail.Tests.Game.Controls;

[TestFixture]
public sealed class RollChargesTests
{
    private DomainEventHub _hub;
    private RunSession _session;
    private GameObject _hud;
    private SkillButton _button;

    [SetUp]
    public void BindHud()
    {
        _hub = new DomainEventHub();
        _session = SkirmisherPathTests.StartRanger(14, _hub);
        _session.ChoosePath(new ContentId("path.ranger.skirmisher"));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/Hud.prefab");
        _hud = Object.Instantiate(prefab);
        _button = _hud.GetComponentInChildren<SkillButton>(true);
        _button.Construct(_session);
        InvokeButton("Start");
    }

    [TearDown]
    public void DestroyHud()
    {
        _session?.End();
        _hub?.Dispose();
        Object.DestroyImmediate(_hud);
    }

    [Test]
    public void OneCharge_NoPips()
    {
        Roll();
        Assert.That(Pips(), Is.Empty);
        Assert.That(_button.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.4f));
    }

    [Test]
    public void TwoCharges_TwoPips_FillAsReady()
    {
        LearnSecondWind();
        AssertPips(Palette.Wind, Palette.Wind);

        Roll();
        AssertPips(Palette.Wind, Palette.Ink);

        Advance(0.6f);
        Roll();
        AssertPips(Palette.Ink, Palette.Ink);

        Advance(1f);
        AssertPips(Palette.Wind, Palette.Ink);

        Advance(2f);
        AssertPips(Palette.Wind, Palette.Wind);
    }

    [Test]
    public void OneReady_ButtonBright_SweepShowsRefill()
    {
        LearnSecondWind();
        Roll();
        Advance(0.6f);

        Assert.That(_button.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
        Assert.That(_button.GetComponent<Image>().sprite.name, Is.EqualTo("RollButton_Ready"));
        Image sweep = _button.GetComponentsInChildren<Image>().Single(image => image.type == Image.Type.Filled);
        Assert.That(sweep.fillAmount, Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(sweep.fillAmount, Is.EqualTo(1f - _session.State.MovementSkillCooldownFraction));
    }

    private void LearnSecondWind()
    {
        _session.TakeNode(new ContentId("skill.skirmisher.second-wind"));
        _session.TakeNode(new ContentId("skill.skirmisher.second-wind-2"));
        _session.TakeNode(new ContentId("skill.skirmisher.second-wind-3"));
        InvokeButton("Update");
    }

    private void Roll()
    {
        _session.MovementSkill();
        Advance(1f / 60f);
    }

    private void Advance(float seconds)
    {
        var snapshot = new WorldSnapshot(64);
        while (seconds > 0f)
        {
            snapshot.Dt = Mathf.Min(seconds, 1f / 60f);
            _session.Tick(snapshot);
            seconds -= snapshot.Dt;
        }

        InvokeButton("Update");
    }

    private Image[] Pips() => _button.GetComponentsInChildren<Image>()
        .Where(image => image.name.StartsWith("Roll Charge ", System.StringComparison.Ordinal)).ToArray();

    private void AssertPips(Color first, Color second)
    {
        Image[] pips = Pips();
        Assert.That(pips.Select(pip => pip.color), Is.EqualTo(new[] { first, second }));
        Assert.That(pips.All(pip => !pip.raycastTarget), Is.True);
    }

    private void InvokeButton(string method) => typeof(SkillButton)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(_button, null);
}

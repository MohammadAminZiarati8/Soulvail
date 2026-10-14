using System;
using NUnit.Framework;
using Soulvail.Editor.Look;
using UnityEngine;

namespace Soulvail.Tests.Game.Editor;

/// <summary>
/// The pure half of the look-capture tool: the UI pass over the world pass, and the file's name (UI-07b).
/// </summary>
[TestFixture]
public sealed class LookCompositorTests
{
    private static readonly Color32 Red = new(255, 0, 0, 255);
    private static readonly Color32 Blue = new(0, 0, 255, 255);

    [Test]
    public void Over_OpaqueUi_IsTheUi()
    {
        Color32[] result = LookCompositor.Over(new[] { Red }, new[] { Blue });

        Assert.That(result[0], Is.EqualTo(Blue));
    }

    [Test]
    public void Over_TransparentUi_IsTheWorld()
    {
        Color32[] result = LookCompositor.Over(new[] { Red }, new[] { new Color32(0, 0, 255, 0) });

        Assert.That(result[0], Is.EqualTo(Red));
    }

    [Test]
    public void Over_HalfAlpha_Blends()
    {
        Color32 pixel = LookCompositor.Over(new[] { Red }, new[] { new Color32(0, 0, 255, 128) })[0];

        Assert.That(pixel.r, Is.EqualTo(127).Within(1));
        Assert.That(pixel.g, Is.EqualTo(0).Within(1));
        Assert.That(pixel.b, Is.EqualTo(128).Within(1));
        Assert.That(pixel.a, Is.EqualTo(255).Within(1));
    }

    [Test]
    public void Over_LengthMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => LookCompositor.Over(new[] { Red, Red }, new[] { Blue }));
    }

    [Test]
    public void FileName_NamesSceneSizeDpiAndTime()
    {
        string name = LookCompositor.FileName("Run", 1080, 2400, 420.4f, new DateTime(2026, 10, 14, 15, 30, 12));

        Assert.That(name, Is.EqualTo("Run_1080x2400_420dpi_20261014-153012.png"));
    }
}

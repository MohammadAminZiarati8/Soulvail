using NUnit.Framework;
using UnityEditor;

namespace Soulvail.Tests.Game.Build;

/// <summary>
/// UI-08a: the build is portrait only, and a phone held upside down must not flip the HUD.
/// </summary>
public sealed class OrientationTests
{
    [Test]
    public void Orientation_IsPortraitOnly()
    {
        Assert.AreEqual(UIOrientation.Portrait, PlayerSettings.defaultInterfaceOrientation);
        Assert.IsFalse(PlayerSettings.allowedAutorotateToPortrait, "portrait");
        Assert.IsFalse(PlayerSettings.allowedAutorotateToPortraitUpsideDown, "portrait upside down");
        Assert.IsFalse(PlayerSettings.allowedAutorotateToLandscapeLeft, "landscape left");
        Assert.IsFalse(PlayerSettings.allowedAutorotateToLandscapeRight, "landscape right");
    }
}

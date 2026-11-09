using NUnit.Framework;
using UnityEditor;

namespace Soulvail.Tests.Game.Editor;

[TestFixture]
public sealed class OrientationSettingsTests
{
    [Test]
    public void Orientation_IsLandscapeAutoRotate()
    {
        Assert.That(PlayerSettings.defaultInterfaceOrientation, Is.EqualTo(UIOrientation.AutoRotation));
        Assert.That(PlayerSettings.allowedAutorotateToLandscapeLeft, Is.True);
        Assert.That(PlayerSettings.allowedAutorotateToLandscapeRight, Is.True);
        Assert.That(PlayerSettings.allowedAutorotateToPortrait, Is.False);
        Assert.That(PlayerSettings.allowedAutorotateToPortraitUpsideDown, Is.False);
    }
}

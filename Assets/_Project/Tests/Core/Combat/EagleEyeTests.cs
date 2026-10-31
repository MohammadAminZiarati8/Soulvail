using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Tests.Core.Fakes;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class EagleEyeTests
{
    [TestCase(5.9f, 1f)]
    [TestCase(6f, 1f)]
    [TestCase(6.1f, 1.2f)]
    public void EagleEye_FarOnly(float distance, float multiplier)
    {
        var eye = new EagleEye();
        eye.FarBonus.Base = 0.2f;
        Assert.That(eye.MultiplierAt(distance), Is.EqualTo(multiplier).Within(0.00001f));
    }

    [Test]
    public void Multipliers_Compose()
    {
        var aim = new SteadyAim(new SilentEvents());
        aim.PerStack.Base = 0.05f;
        aim.OnLoose();
        aim.OnLoose();
        var eye = new EagleEye();
        eye.FarBonus.Base = 0.2f;
        Assert.That(aim.OnLoose() * eye.MultiplierAt(7f), Is.EqualTo(1.32f).Within(0.00001f));
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Common;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Tests.Core;

/// <summary>
/// The guards behind ADR-0001 and AR §18.2: <c>Soulvail.Core</c> is pure C#, and the boundary
/// rules that hold it that way are pinned by reflection rather than by review. The asmdef's
/// <c>noEngineReferences</c> flag enforces the first at compile time; this fails the build if
/// someone ever clears that flag, which is the moment the hexagon stops being one.
/// </summary>
[TestFixture]
public sealed class AssemblyPurityTests
{
    [Test]
    public void CoreAssembly_ReferencesNoUnityAssemblies()
    {
        AssemblyName[] referenced = typeof(StateMachine<>).Assembly.GetReferencedAssemblies();

        var offenders = new List<string>();
        foreach (AssemblyName reference in referenced)
        {
            if (reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)
                || reference.Name.StartsWith("UnityEditor", StringComparison.Ordinal))
            {
                offenders.Add(reference.Name);
            }
        }

        Assert.That(
            offenders,
            Is.Empty,
            "Soulvail.Core must never reference the engine. Offending references: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The M2-01 rule-3 decision, pinned rather than remembered: <see cref="IClock"/> is
    /// <c>UtcNow</c> and nothing else. A monotonic or elapsed member is the obvious second one,
    /// and the argument against adding it before something needs it (AR §6) survives exactly as
    /// long as somebody remembers making it.
    /// </summary>
    [Test]
    public void IClock_HasExactlyOneMember()
    {
        Type clock = typeof(IClock);

        PropertyInfo[] properties = clock.GetProperties();
        Assert.That(properties, Has.Length.EqualTo(1));
        Assert.That(properties[0].Name, Is.EqualTo(nameof(IClock.UtcNow)));
        Assert.That(properties[0].CanWrite, Is.False, "A settable clock is a clock core can move.");

        // Everything else the type could carry. Methods are filtered down to the ones that are not
        // the property's own accessor, since a property is a method pair at the metadata level.
        var methods = new List<string>();
        foreach (MethodInfo method in clock.GetMethods())
        {
            if (!method.IsSpecialName)
            {
                methods.Add(method.Name);
            }
        }

        Assert.That(methods, Is.Empty, "IClock grows a member when a mechanic needs one, not before.");
        Assert.That(clock.GetEvents(), Is.Empty);
        Assert.That(clock.GetFields(), Is.Empty);
    }

    /// <summary>
    /// AR §18.2: there is no clock in the session. Simulated time is the sum of each tick's
    /// <c>Dt</c>, and wall-clock is a different number for a different purpose. Asserted rather
    /// than reviewed because the failure mode is a plausible-looking constructor parameter that
    /// nobody questions.
    /// </summary>
    /// <remarks>
    /// The array is the seam: M2-14a widens this to every type in <c>Soulvail.Core.Run</c>, at
    /// which point the literal below becomes a namespace sweep and the loop stays as it is.
    /// </remarks>
    [Test]
    public void RunSession_TakesNoClock()
    {
        Type[] runTypes = { typeof(RunSession) };

        foreach (Type type in runTypes)
        {
            foreach (ConstructorInfo constructor in type.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    Assert.That(
                        parameter.ParameterType,
                        Is.Not.EqualTo(typeof(IClock)),
                        $"{type.Name} takes an IClock. Simulated time is the sum of each tick's "
                        + "Dt (AR §18.2); wall-clock belongs to persistence, not to the run.");
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Common;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;

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
    /// <para>
    /// <b>Widened at M2-14a from <c>RunSession</c> to every type in <c>Soulvail.Core.Run</c></b>,
    /// which is what the array was a seam for. An invariant that names one class is one refactor
    /// away from being decorative, and M2-14a is the task that gave it something to be tempted by:
    /// a snapshot is stamped with wall-clock, and the obvious way to reach one from
    /// <c>RunSession.Start</c> is a constructor parameter nobody would question. It takes a
    /// <c>RunRecorder</c> instead — see <see cref="Recorder_MayTakeAClock"/>, which is the
    /// exception stated out loud so this row cannot be read as a ban on the recorder too.
    /// </para>
    /// <para>
    /// The sweep is over the loaded assembly rather than a list, so a type added to the namespace
    /// tomorrow is covered without anyone remembering to add it here — which is the whole
    /// difference between an invariant and a note.
    /// </para>
    /// </remarks>
    [Test]
    public void Run_NoTypeTakesAClock()
    {
        Type[] runTypes = RunNamespaceTypes();

        Assert.That(
            runTypes,
            Is.Not.Empty,
            "The namespace sweep found nothing, so this row would pass against any code at all.");

        // The seam it replaced, kept as an explicit member of the set: whatever else the sweep
        // finds, it has to find this one.
        Assert.That(runTypes, Contains.Item(typeof(RunSession)));

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

    /// <summary>
    /// The exception <see cref="Run_NoTypeTakesAClock"/>'s rule states, asserted rather than
    /// assumed: <c>RunRecorder</c> takes an <see cref="IClock"/>, and is allowed to.
    /// </summary>
    /// <remarks>
    /// AR §18.2's rule is about the <em>simulation</em> reading wall-clock. A save stamp is not
    /// simulation — it is the one number that says when a file was written — and the recorder is in
    /// <c>Soulvail.Core.Save</c> precisely so that the sweep above can be as wide as it is. Without
    /// this row, the next reader of that sweep would reasonably conclude that core may not hold a
    /// clock anywhere, and the fix for the bug they were chasing would be to delete the stamp.
    /// </remarks>
    [Test]
    public void Recorder_MayTakeAClock()
    {
        var found = false;

        foreach (ConstructorInfo constructor in typeof(RunRecorder).GetConstructors())
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                found |= parameter.ParameterType == typeof(IClock);
            }
        }

        Assert.That(
            found,
            Is.True,
            "RunRecorder is the one type in core that holds wall-clock. If it stopped taking one, "
            + "either the stamp moved or it moved — and the sweep above would need re-reading.");

        Assert.That(
            typeof(RunRecorder).Namespace,
            Is.EqualTo("Soulvail.Core.Save"),
            "It is outside Soulvail.Core.Run on purpose: that is what lets the sweep be a "
            + "namespace rather than a list with an exception in it.");
    }

    /// <summary>Every type declared in <c>Soulvail.Core.Run</c>, from the compiled assembly.</summary>
    private static Type[] RunNamespaceTypes()
    {
        var types = new List<Type>();

        foreach (Type type in typeof(RunSession).Assembly.GetTypes())
        {
            if (type.Namespace == "Soulvail.Core.Run")
            {
                types.Add(type);
            }
        }

        return types.ToArray();
    }
}

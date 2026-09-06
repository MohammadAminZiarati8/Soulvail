using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Common;

namespace Soulvail.Tests.Core;

/// <summary>
/// The guard behind ADR-0001: <c>Soulvail.Core</c> is pure C#. The asmdef's
/// <c>noEngineReferences</c> flag enforces it at compile time; this fails the build if
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
}

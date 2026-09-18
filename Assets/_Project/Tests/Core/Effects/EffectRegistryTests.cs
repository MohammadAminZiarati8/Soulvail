using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Effects;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

/// <summary>
/// <c>EffectRegistry</c>: that an effect reaches its own handler and nobody else's, that a second
/// handler for one type is refused, that an unregistered one is loud, and that dispatch costs
/// nothing.
/// </summary>
/// <remarks>
/// <para>
/// Written against fixture effects rather than against <c>ModifyStat</c>, deliberately. The
/// registry's whole claim is that it knows nothing about any particular primitive, and a row that
/// proved dispatch using the one primitive that exists would be proving it about
/// <c>ModifyStat</c> instead. <c>ModifyStatTests</c> is where the shipped pair is tested.
/// </para>
/// <para>
/// The fixture effects are nested private classes for the same reason: nothing outside this file
/// should be able to register a handler for them, and two of them exist only so that
/// <c>Apply_TwoTypesTwoHandlers</c> has a wrong door to check nobody went through.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EffectRegistryTests
{
    // ---- Rule 2: one handler per type, and a loud absence ----------------------------------------

    [Test]
    public void Register_TwiceForOneType_Throws()
    {
        var registry = new EffectRegistry();

        registry.Register<Alpha>(new Counting<Alpha>());

        Assert.That(
            () => registry.Register<Alpha>(new Counting<Alpha>()),
            Throws.InvalidOperationException,
            "Two handlers for one effect are two opinions about what it does, and one of them "
                + "would be silently dropped.");

        Assert.That(registry.HandlerCount, Is.EqualTo(1), "The refusal left the table alone.");
    }

    [Test]
    public void Register_Null_Throws()
    {
        var registry = new EffectRegistry();

        Assert.That(
            () => registry.Register<Alpha>(null),
            Throws.ArgumentNullException);

        Assert.That(registry.HandlerCount, Is.Zero);
    }

    // ---- Rule 1: data and handler, matched by type -----------------------------------------------

    [Test]
    public void Apply_DispatchesToTheHandler()
    {
        var registry = new EffectRegistry();
        var handler = new Counting<Alpha>();
        var effect = new Alpha();
        var source = new object();

        registry.Register<Alpha>(handler);

        registry.Apply(effect, source);

        Assert.That(handler.Applied, Is.EqualTo(1));
        Assert.That(handler.Removed, Is.Zero);
        Assert.That(handler.LastEffect, Is.SameAs(effect));
        Assert.That(handler.LastSource, Is.SameAs(source));
    }

    [Test]
    public void Remove_DispatchesToTheHandler()
    {
        var registry = new EffectRegistry();
        var handler = new Counting<Alpha>();
        var effect = new Alpha();
        var source = new object();

        registry.Register<Alpha>(handler);

        registry.Remove(effect, source);

        Assert.That(handler.Removed, Is.EqualTo(1));
        Assert.That(handler.Applied, Is.Zero);
        Assert.That(handler.LastEffect, Is.SameAs(effect));
        Assert.That(handler.LastSource, Is.SameAs(source));
    }

    [Test]
    public void Apply_TwoTypesTwoHandlers()
    {
        var registry = new EffectRegistry();
        var alphas = new Counting<Alpha>();
        var betas = new Counting<Beta>();

        registry.Register<Alpha>(alphas);
        registry.Register<Beta>(betas);

        Assert.That(registry.HandlerCount, Is.EqualTo(2));

        var source = new object();

        registry.Apply(new Alpha(), source);

        Assert.That(alphas.Applied, Is.EqualTo(1));
        Assert.That(betas.Applied, Is.Zero, "Beta's handler is not Alpha's.");

        registry.Apply(new Beta(), source);

        Assert.That(betas.Applied, Is.EqualTo(1));
        Assert.That(alphas.Applied, Is.EqualTo(1), "And Alpha's handler ran once, not twice.");
    }

    [Test]
    public void Apply_Unregistered_ThrowsNamingTheType()
    {
        var registry = new EffectRegistry();

        registry.Register<Alpha>(new Counting<Alpha>());

        Assert.That(
            () => registry.Apply(new Beta(), new object()),
            Throws.TypeOf<KeyNotFoundException>()
                .With.Message.Contains(nameof(Beta)),
            "The message has to name the type, or the error says only that something somewhere "
                + "was not registered.");
    }

    [Test]
    public void Remove_Unregistered_ThrowsNamingTheType()
    {
        var registry = new EffectRegistry();

        Assert.That(
            () => registry.Remove(new Beta(), new object()),
            Throws.TypeOf<KeyNotFoundException>().With.Message.Contains(nameof(Beta)));
    }

    [Test]
    public void CanApply_AnswersRegistration()
    {
        var registry = new EffectRegistry();

        registry.Register<Alpha>(new Counting<Alpha>());

        Assert.That(registry.CanApply(new Alpha()), Is.True);
        Assert.That(
            registry.CanApply(new Beta()),
            Is.False,
            "The validation door M3-03 asks at Start, which is what makes Apply's throw "
                + "unreachable in a live run.");
    }

    [Test]
    public void Apply_NullArguments_Throw()
    {
        var registry = new EffectRegistry();
        var effect = new Alpha();

        registry.Register<Alpha>(new Counting<Alpha>());

        Assert.That(() => registry.Apply(null, new object()), Throws.ArgumentNullException);
        Assert.That(() => registry.Apply(effect, null), Throws.ArgumentNullException);
        Assert.That(() => registry.Remove(null, new object()), Throws.ArgumentNullException);
        Assert.That(() => registry.Remove(effect, null), Throws.ArgumentNullException);
        Assert.That(() => registry.CanApply(null), Throws.ArgumentNullException);
    }

    // ---- Rule 3: a pick is a moment the frame is already busy ------------------------------------

    [Test]
    public void Apply_AllocatesNothing()
    {
        var registry = new EffectRegistry();
        var effect = new Alpha();
        var source = new object();

        registry.Register<Alpha>(new Counting<Alpha>());

        // A dictionary probe and a virtual call through the binding. Effects are classes, so
        // nothing boxes on the way through — which is the reason IEffect is an interface on
        // sealed classes rather than a struct discriminated union.
        AllocationAssert.None(() => registry.Apply(effect, source));
    }

    // ---- AR §1: a registry is the classic singleton temptation -----------------------------------

    [Test]
    public void Registry_HoldsNoStatics()
    {
        FieldInfo[] statics = typeof(EffectRegistry).GetFields(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        var names = new List<string>();

        foreach (FieldInfo field in statics)
        {
            names.Add(field.Name);
        }

        Assert.That(
            names,
            Is.Empty,
            "There is obviously only one table of effect handlers, which is exactly why a static "
                + "one is wrong: its handlers hold a run's live objects, so it would apply the "
                + "next run's nodes to the last run's player. Offenders: "
                + string.Join(", ", names));
    }

    [Test]
    public void Registries_DoNotShareHandlers()
    {
        var first = new EffectRegistry();
        var second = new EffectRegistry();

        first.Register<Alpha>(new Counting<Alpha>());

        Assert.That(second.HandlerCount, Is.Zero, "A second run starts with an empty table.");
        Assert.That(second.CanApply(new Alpha()), Is.False);
    }

    /// <summary>An effect that means nothing, which is all a dispatch row needs it to mean.</summary>
    private sealed class Alpha : IEffect
    {
    }

    /// <summary>A second one, so a row can check nobody went through the wrong door.</summary>
    private sealed class Beta : IEffect
    {
    }

    /// <summary>Counts what reached it and remembers the last pair of arguments.</summary>
    private sealed class Counting<TEffect> : IEffectHandler<TEffect>
        where TEffect : IEffect
    {
        internal int Applied { get; private set; }

        internal int Removed { get; private set; }

        internal IEffect LastEffect { get; private set; }

        internal object LastSource { get; private set; }

        public void Apply(TEffect effect, object source)
        {
            Applied++;
            LastEffect = effect;
            LastSource = source;
        }

        public void Remove(TEffect effect, object source)
        {
            Removed++;
            LastEffect = effect;
            LastSource = source;
        }
    }
}

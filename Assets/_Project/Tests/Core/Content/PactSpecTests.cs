using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// <c>PactSpec</c> and the one place it attaches: GD §13.2's corrupted form of a node, its Veilrot
/// band, and the kind it is refused on (M6-05a).
/// </summary>
/// <remarks>
/// The effects are a private marker, for <c>SkillSpecTests</c>' reason: neither spec looks inside an
/// <see cref="IEffect"/>, so a row written against <c>ModifyStat</c> would be proving something
/// about the primitive instead. What a Pact's effects <em>do</em> is <c>SkillTreeTests</c>'.
/// </remarks>
[TestFixture]
public sealed class PactSpecTests
{
    // ---- The block ---------------------------------------------------------------------------------

    [Test]
    public void Pact_CarriesItsThree()
    {
        var first = new Marker();
        var second = new Marker();
        var effects = new List<IEffect> { first, second };
        var key = new LocKey("skill.test.pact.description");

        var pact = new PactSpec(effects, 15f, key);

        Assert.That(pact.Veilrot, Is.EqualTo(15f));
        Assert.That(pact.DescriptionKey, Is.EqualTo(key));
        Assert.That(pact.Effects, Is.EqualTo(new IEffect[] { first, second }));

        // Copied: the caller's list is not retained, so editing it afterwards changes nothing.
        effects.Clear();

        Assert.That(pact.Effects, Has.Count.EqualTo(2));
        Assert.That(pact.Effects, Is.Not.SameAs(effects));
    }

    [Test]
    public void Pact_RefusesAnEmptyOrNullEffect()
    {
        // Rule 7: the same copy-and-null-check a clean node's list goes through.
        Assert.Throws<ArgumentException>(
            () => new PactSpec(Array.Empty<IEffect>(), 15f, Key()),
            "A Pact that does nothing is a price for nothing.");

        var thrown = Assert.Throws<ArgumentException>(
            () => new PactSpec(new IEffect[] { new Marker(), null }, 15f, Key()));

        Assert.That(thrown.Message, Does.Contain("[1]"), "The index of the null entry.");

        Assert.Throws<ArgumentNullException>(() => new PactSpec(null, 15f, Key()));
    }

    [Test]
    public void Pact_RefusesAVeilrotOutsideTheBand()
    {
        foreach (float veilrot in new[] { 9.9f, 20.1f, float.NaN, float.PositiveInfinity })
        {
            var thrown = Assert.Throws<ArgumentOutOfRangeException>(
                () => new PactSpec(OneEffect(), veilrot, Key()),
                $"{veilrot} is outside GD §13.2's band.");

            Assert.That(thrown.Message, Does.Contain("10").And.Contain("20"), "The message names the band.");
        }
    }

    [Test]
    public void Pact_AcceptsBothEnds()
    {
        Assert.DoesNotThrow(() => new PactSpec(OneEffect(), PactSpec.MinVeilrot, Key()));
        Assert.DoesNotThrow(() => new PactSpec(OneEffect(), PactSpec.MaxVeilrot, Key()));

        Assert.That(PactSpec.MinVeilrot, Is.EqualTo(10f));
        Assert.That(PactSpec.MaxVeilrot, Is.EqualTo(20f));
    }

    [Test]
    public void Pact_RefusesADefaultDescriptionKey()
    {
        // SkillSpec's forgotten-field rule: default(LocKey) carries a null past the struct.
        Assert.Throws<ArgumentException>(() => new PactSpec(OneEffect(), 15f, default));
    }

    [Test]
    public void Pact_HasNoNameKeyOfItsOwn()
    {
        // Rule 4: the corrupted node keeps the clean one's name so the player recognises the node
        // they have been offered clean before. A NameKey here would be a second name to drift.
        Assert.That(typeof(PactSpec).GetProperty("NameKey"), Is.Null);
    }

    // ---- On a node ---------------------------------------------------------------------------------

    [Test]
    public void Skill_CarriesAPact()
    {
        PactSpec pact = Pact(12f);

        SkillSpec spec = Node(SkillKind.Passive, pact);

        Assert.That(spec.HasPact, Is.True);
        Assert.That(spec.Pact, Is.SameAs(pact));
        Assert.That(spec.Pact.Veilrot, Is.EqualTo(12f));
    }

    [Test]
    public void Skill_WithoutOneIsUnchanged()
    {
        // Rule 2: six arguments, exactly as the seventy call sites that predate Pacts build one.
        IEffect effect = new Marker();

        var spec = new SkillSpec(
            Id(),
            new LocKey("skill.test.node.name"),
            new LocKey("skill.test.node.description"),
            SkillKind.Upgrade,
            new[] { effect },
            null,
            new ContentId("skill.test.parent"));

        Assert.That(spec.HasPact, Is.False);
        Assert.That(spec.Pact, Is.Null);

        Assert.That(spec.Id, Is.EqualTo(Id()));
        Assert.That(spec.NameKey.Key, Is.EqualTo("skill.test.node.name"));
        Assert.That(spec.DescriptionKey.Key, Is.EqualTo("skill.test.node.description"));
        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Upgrade));
        Assert.That(spec.Effects, Is.EqualTo(new[] { effect }));
        Assert.That(spec.Active, Is.Null);
        Assert.That(spec.ParentId, Is.EqualTo(new ContentId("skill.test.parent")));
    }

    [Test]
    public void Skill_RefusesAPactOnAnActive()
    {
        // Rule 3: an Active's power is on cast, so its corrupted form is a second ActiveSpec the
        // runner has no door for. The message names the node and who builds the door.
        var thrown = Assert.Throws<ArgumentException>(() => new SkillSpec(
            Id(),
            new LocKey("skill.test.node.name"),
            new LocKey("skill.test.node.description"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(
                8f,
                new TriggerSpec(new[]
                {
                    new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f),
                }),
                OneEffect()),
            pact: Pact(15f)));

        Assert.That(thrown.Message, Does.Contain(Id().Value));
        Assert.That(thrown.Message, Does.Contain("M7-04"));
        Assert.That(thrown.ParamName, Is.EqualTo("pact"));
    }

    [Test]
    public void Skill_AcceptsAPactOnUpgradeAndKeystone()
    {
        Assert.DoesNotThrow(() => new SkillSpec(
            Id(),
            new LocKey("skill.test.node.name"),
            new LocKey("skill.test.node.description"),
            SkillKind.Upgrade,
            OneEffect(),
            parentId: new ContentId("skill.test.parent"),
            pact: Pact(15f)));

        Assert.DoesNotThrow(() => Node(SkillKind.Keystone, Pact(15f)));
        Assert.DoesNotThrow(() => Node(SkillKind.Passive, Pact(15f)));
    }

    // ---- Fixtures ----------------------------------------------------------------------------------

    private static ContentId Id() => new ContentId("skill.test.node");

    private static LocKey Key() => new LocKey("skill.test.node.pact.description");

    private static IReadOnlyList<IEffect> OneEffect() => new IEffect[] { new Marker() };

    private static PactSpec Pact(float veilrot) => new PactSpec(OneEffect(), veilrot, Key());

    private static SkillSpec Node(SkillKind kind, PactSpec pact) => new SkillSpec(
        Id(),
        new LocKey("skill.test.node.name"),
        new LocKey("skill.test.node.description"),
        kind,
        OneEffect(),
        pact: pact);

    /// <summary>An effect that is nothing but an effect — <c>SkillSpecTests</c>' marker.</summary>
    private sealed class Marker : IEffect
    {
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// The Content module's fixture: one module, one test file. <see cref="MovementSpec"/> arrived
/// with M0-07; <see cref="ContentId"/>, <see cref="LocKey"/>, <see cref="CharacterSpec"/> and
/// <see cref="ContentCatalog"/> with M0-08. Later content types (enemy, skill, mode specs) join
/// them here.
/// </summary>
[TestFixture]
public sealed class ContentTests
{
    [Test]
    public void Spec_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(0f, 0.06f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, 0f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, 0.08f, 0f));

        // Negative is the same mistake as zero, and the two times are divisors — a negative
        // accel time yields a negative rate, which drives the velocity away from its target.
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(-5.4f, 0.06f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, -0.06f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, -0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, 0.08f, -720f));

        // NaN has to be rejected too, and it is the case the obvious `value <= 0f` guard lets
        // through: every comparison against NaN is false. One NaN reaching the motor turns its
        // velocity and facing to NaN on the first tick, and NaN survives all later arithmetic —
        // the character would never move again, with nothing in the log to say why.
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(float.NaN, 0.06f, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, float.NaN, 0.08f, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, float.NaN, 720f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementSpec(5.4f, 0.06f, 0.08f, float.NaN));

        // The Oathbound's own numbers (CC §7) must pass, or every guard above would be satisfied
        // by a constructor that rejected everything.
        Assert.DoesNotThrow(() => new MovementSpec(5.4f, 0.06f, 0.08f, 720f));
    }

    [Test]
    public void Spec_StoresValues()
    {
        var spec = new MovementSpec(5.4f, 0.06f, 0.08f, 720f);

        Assert.That(spec.Speed, Is.EqualTo(5.4f));
        Assert.That(spec.AccelTime, Is.EqualTo(0.06f));
        Assert.That(spec.DecelTime, Is.EqualTo(0.08f));
        Assert.That(spec.TurnSpeedDeg, Is.EqualTo(720f));
    }

    [Test]
    public void ContentId_ValidForms_Accepted()
    {
        Assert.That(new ContentId("a.b").Value, Is.EqualTo("a.b"));
        Assert.That(new ContentId("character.oathbound").Value, Is.EqualTo("character.oathbound"));
        Assert.That(new ContentId("skill.x.y_z-1").Value, Is.EqualTo("skill.x.y_z-1"));

        // Digits are head characters too, so a segment may be entirely numeric.
        Assert.That(new ContentId("mode.d2").Value, Is.EqualTo("mode.d2"));

        // ToString is the id, so an id drops into a log line or a message unquoted.
        Assert.That(new ContentId("character.oathbound").ToString(), Is.EqualTo("character.oathbound"));
    }

    [Test]
    public void ContentId_InvalidForms_Rejected()
    {
        Assert.Throws<ArgumentException>(() => new ContentId(""));
        Assert.Throws<ArgumentException>(() => new ContentId("single"));
        Assert.Throws<ArgumentException>(() => new ContentId("Has.Upper"));
        Assert.Throws<ArgumentException>(() => new ContentId("a b.c"));
        Assert.Throws<ArgumentException>(() => new ContentId(".a.b"));
        Assert.Throws<ArgumentException>(() => new ContentId("a.b."));
        Assert.Throws<ArgumentException>(() => new ContentId("a..b"));
        Assert.Throws<ArgumentException>(() => new ContentId(null));

        // `_` and `-` are legal only after the first segment, so an id can never open with
        // punctuation-adjacent noise from a renamed asset.
        Assert.Throws<ArgumentException>(() => new ContentId("a_b.c"));
        Assert.Throws<ArgumentException>(() => new ContentId("a-b.c"));

        // The message carries the offending value: content errors are read by whoever authored
        // the asset, not by whoever wrote the guard.
        ArgumentException ex = Assert.Throws<ArgumentException>(() => new ContentId("Has.Upper"));
        Assert.That(ex.Message, Does.Contain("Has.Upper"));
    }

    [Test]
    public void ContentId_TryParse_MatchesIsValid()
    {
        string[] inputs =
        {
            "a.b", "character.oathbound", "skill.x.y_z-1",
            "", "single", "Has.Upper", "a b.c", ".a.b", "a.b.", "a..b", null,
        };

        foreach (string input in inputs)
        {
            string shown = input ?? "<null>";
            bool valid = ContentId.IsValid(input);
            bool parsed = ContentId.TryParse(input, out ContentId id);

            Assert.That(parsed, Is.EqualTo(valid), $"TryParse disagreed with IsValid for '{shown}'.");

            if (parsed)
            {
                Assert.That(id.Value, Is.EqualTo(input), $"TryParse lost the value for '{shown}'.");
            }
            else
            {
                Assert.That(id.Value, Is.Null, $"TryParse returned false but set an id for '{shown}'.");
            }
        }
    }

    [Test]
    public void ContentId_Equality_IsOrdinal()
    {
        // Built at runtime rather than written as a literal: two identical literals are the same
        // interned instance, so a reference comparison would pass this test while proving
        // nothing about the two ids that matter — one from a save file, one from an asset.
        var built = new ContentId(new string(new[] { 'a', '.', 'b' }));
        var literal = new ContentId("a.b");
        var other = new ContentId("a.c");

        Assert.That(built == literal, Is.True);
        Assert.That(built.Equals(literal), Is.True);
        Assert.That(built.Equals((object)literal), Is.True);
        Assert.That(built.GetHashCode(), Is.EqualTo(literal.GetHashCode()));

        Assert.That(built == other, Is.False);
        Assert.That(built != other, Is.True);
        Assert.That(built.Equals(other), Is.False);
        Assert.That(built.Equals("a.b"), Is.False);
    }

    [Test]
    public void ContentId_Default_NotEqualToAny()
    {
        ContentId none = default;
        var some = new ContentId("a.b");

        Assert.That(none.Value, Is.Null);
        Assert.That(none == some, Is.False);
        Assert.That(some == none, Is.False);
        Assert.That(none != some, Is.True);
        Assert.That(none == default(ContentId), Is.True);

        // A default id reaches a hash lookup or a log line like any other; neither may throw.
        Assert.That(none.GetHashCode(), Is.EqualTo(0));
        Assert.That(none.ToString(), Is.Empty);
    }

    [Test]
    public void LocKey_Valid_Accepted()
    {
        var key = new LocKey("character.oathbound.name");

        Assert.That(key.Key, Is.EqualTo("character.oathbound.name"));
        Assert.That(key.ToString(), Is.EqualTo("character.oathbound.name"));

        // Not a ContentId: a key is not checked against that grammar, or against any table.
        Assert.DoesNotThrow(() => new LocKey("UI_Descend"));
        Assert.DoesNotThrow(() => new LocKey("single"));
    }

    [Test]
    public void LocKey_Invalid_Rejected()
    {
        Assert.Throws<ArgumentException>(() => new LocKey(""));
        Assert.Throws<ArgumentException>(() => new LocKey("has space"));
        Assert.Throws<ArgumentException>(() => new LocKey(null));

        // Any whitespace, anywhere — a trailing space or a stray newline from an authored field
        // would otherwise become a key that silently never resolves.
        Assert.Throws<ArgumentException>(() => new LocKey("   "));
        Assert.Throws<ArgumentException>(() => new LocKey("trailing "));
        Assert.Throws<ArgumentException>(() => new LocKey("has\ttab"));
        Assert.Throws<ArgumentException>(() => new LocKey("has\nnewline"));
    }

    [Test]
    public void LocKey_Equality_IsOrdinal()
    {
        var built = new LocKey(new string(new[] { 'a', '.', 'b' }));
        var literal = new LocKey("a.b");
        var other = new LocKey("a.c");
        LocKey none = default;

        Assert.That(built == literal, Is.True);
        Assert.That(built.Equals(literal), Is.True);
        Assert.That(built.GetHashCode(), Is.EqualTo(literal.GetHashCode()));
        Assert.That(built != other, Is.True);
        Assert.That(none == literal, Is.False);
        Assert.That(none.ToString(), Is.Empty);
    }

    [Test]
    public void CharacterSpec_StoresValues()
    {
        var id = new ContentId("character.oathbound");
        var nameKey = new LocKey("character.oathbound.name");
        MovementSpec movement = OathboundMovement();
        TargetingSpec targeting = OathboundTargeting();
        WeaponSpec weapon = OathboundWeapon();
        FocusSpec focus = OathboundFocus();
        MovementSkillSpec movementSkill = OathboundMovementSkill();

        var spec = new CharacterSpec(id, nameKey, 120f, movement, targeting, weapon, focus, movementSkill);

        Assert.That(spec.Id, Is.EqualTo(id));
        Assert.That(spec.NameKey, Is.EqualTo(nameKey));
        Assert.That(spec.MaxHp, Is.EqualTo(120f));
        Assert.That(spec.Movement, Is.SameAs(movement));
        Assert.That(spec.Targeting, Is.SameAs(targeting));
        Assert.That(spec.Weapon, Is.SameAs(weapon));
        Assert.That(spec.Focus, Is.SameAs(focus));
        Assert.That(spec.MovementSkill, Is.SameAs(movementSkill));
    }

    [Test]
    public void CharacterSpec_InvalidHp_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CharacterSpec(OathboundId(), OathboundNameKey(), 0f, OathboundMovement(), OathboundTargeting(), OathboundWeapon(), OathboundFocus(), OathboundMovementSkill()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CharacterSpec(OathboundId(), OathboundNameKey(), -1f, OathboundMovement(), OathboundTargeting(), OathboundWeapon(), OathboundFocus(), OathboundMovementSkill()));

        // Same NaN hole as MovementSpec's guard, and the same spelling closes it.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CharacterSpec(OathboundId(), OathboundNameKey(), float.NaN, OathboundMovement(), OathboundTargeting(), OathboundWeapon(), OathboundFocus(), OathboundMovementSkill()));

        Assert.DoesNotThrow(
            () => new CharacterSpec(OathboundId(), OathboundNameKey(), 1f, OathboundMovement(), OathboundTargeting(), OathboundWeapon(), OathboundFocus(), OathboundMovementSkill()));
    }

    [Test]
    public void CharacterSpec_NullMovement_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CharacterSpec(OathboundId(), OathboundNameKey(), 120f, null, OathboundTargeting(), OathboundWeapon(), OathboundFocus(), OathboundMovementSkill()));
    }

    [Test]
    public void CharacterSpec_NullTargeting_Throws()
    {
        // Unlike the shield, targeting is required (M1-03): auto-aim is the default control
        // mode for every class (CC §3), so a null here is a forgotten field rather than a class
        // that does not aim. The guard is what stops it becoming a NullReferenceException
        // somewhere inside M1-04's targeting loop instead.
        Assert.Throws<ArgumentNullException>(
            () => new CharacterSpec(OathboundId(), OathboundNameKey(), 120f, OathboundMovement(), null, OathboundWeapon(), OathboundFocus(), OathboundMovementSkill()));
    }

    [Test]
    public void CharacterSpec_NullWeapon_Throws()
    {
        // Required for the same reason targeting is (M1-10): CC §4 gives every class a basic
        // attack, with no ammunition and no cost, so a null is a forgotten field rather than a
        // class that does not attack. Without the guard it would be a character who aims perfectly
        // and never swings — a bug that looks like broken targeting rather than missing content.
        Assert.Throws<ArgumentNullException>(
            () => new CharacterSpec(OathboundId(), OathboundNameKey(), 120f, OathboundMovement(), OathboundTargeting(), null, OathboundFocus(), OathboundMovementSkill()));
    }

    [Test]
    public void CharacterSpec_NullFocus_Throws()
    {
        // Required one step further out than the weapon (M1-13): standing still is not a class
        // feature, so there is no "this class has no Focus" to say with a null — a class that
        // should not ramp authors a MaxMultiplier of 1. It is also load-bearing: FocusTracker owns
        // the stationary clock CombatBlackboard.StationaryTime mirrors, and a class without one
        // would silently stop counting a CC §6.4 trigger field.
        Assert.Throws<ArgumentNullException>(
            () => new CharacterSpec(OathboundId(), OathboundNameKey(), 120f, OathboundMovement(), OathboundTargeting(), OathboundWeapon(), null, OathboundMovementSkill()));
    }

    [Test]
    public void CharacterSpec_NullMovementSkill_Throws()
    {
        // Required for the reason the weapon is (M1-14), and CC §5 says it in its opening line:
        // every class has exactly one movement skill, on a permanent button. A null is a forgotten
        // field rather than a class that cannot dash, and it would produce a dead button in the one
        // place the design has no fallback for — the dodge is the whole defensive layer for a class
        // without an Aegis.
        Assert.Throws<ArgumentNullException>(
            () => new CharacterSpec(OathboundId(), OathboundNameKey(), 120f, OathboundMovement(), OathboundTargeting(), OathboundWeapon(), OathboundFocus(), null));
    }

    [Test]
    public void CharacterSpec_DefaultId_Throws()
    {
        // A spec with no id would sit in the catalog under a key that Character() reports as
        // missing, so it is refused where the data is built.
        Assert.Throws<ArgumentException>(
            () => new CharacterSpec(default, OathboundNameKey(), 120f, OathboundMovement(), OathboundTargeting(), OathboundWeapon(), OathboundFocus(), OathboundMovementSkill()));
    }

    [Test]
    public void Catalog_LooksUpById()
    {
        CharacterSpec spec = Oathbound();
        var catalog = new ContentCatalog(new[] { spec });

        Assert.That(catalog.Character(spec.Id), Is.SameAs(spec));
        Assert.That(catalog.TryGetCharacter(spec.Id, out CharacterSpec found), Is.True);
        Assert.That(found, Is.SameAs(spec));
        Assert.That(catalog.Characters.Count, Is.EqualTo(1));
        Assert.That(catalog.Characters[0], Is.SameAs(spec));

        // The id that finds it is a value, not the instance that was registered.
        Assert.That(catalog.Character(new ContentId("character.oathbound")), Is.SameAs(spec));
    }

    [Test]
    public void Catalog_UnknownId_Throws_NamingId()
    {
        var catalog = new ContentCatalog(Array.Empty<CharacterSpec>());

        KeyNotFoundException ex = Assert.Throws<KeyNotFoundException>(
            () => catalog.Character(new ContentId("x.y")));

        Assert.That(ex.Message, Does.Contain("x.y"));
    }

    [Test]
    public void Catalog_TryGet_FalseForUnknown()
    {
        var catalog = new ContentCatalog(Array.Empty<CharacterSpec>());

        Assert.That(catalog.TryGetCharacter(new ContentId("x.y"), out CharacterSpec spec), Is.False);
        Assert.That(spec, Is.Null);
    }

    [Test]
    public void Catalog_DefaultId_IsUnknown()
    {
        var catalog = new ContentCatalog(new[] { Oathbound() });

        Assert.That(catalog.TryGetCharacter(default, out CharacterSpec spec), Is.False);
        Assert.That(spec, Is.Null);
        Assert.Throws<KeyNotFoundException>(() => catalog.Character(default));
    }

    [Test]
    public void Catalog_DuplicateId_Throws_NamingId()
    {
        var first = new CharacterSpec(OathboundId(), OathboundNameKey(), 120f, OathboundMovement(), OathboundTargeting(), OathboundWeapon(), OathboundFocus(), OathboundMovementSkill());
        var second = new CharacterSpec(OathboundId(), OathboundNameKey(), 200f, OathboundMovement(), OathboundTargeting(), OathboundWeapon(), OathboundFocus(), OathboundMovementSkill());

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ContentCatalog(new[] { first, second }));

        Assert.That(ex.Message, Does.Contain("character.oathbound"));
    }

    [Test]
    public void Catalog_CopiesInput()
    {
        CharacterSpec spec = Oathbound();
        var list = new List<CharacterSpec> { spec };

        var catalog = new ContentCatalog(list);
        list.Clear();

        Assert.That(catalog.Characters.Count, Is.EqualTo(1));
        Assert.That(catalog.Character(spec.Id), Is.SameAs(spec));
    }

    [Test]
    public void Catalog_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ContentCatalog(null));
        Assert.Throws<ArgumentException>(() => new ContentCatalog(new CharacterSpec[] { null }));
    }

    [Test]
    public void Catalog_ModeLookupAndDuplicates()
    {
        // The third kind (M2-02), asserted against the same four properties the character rows
        // above claim: found by value, missed by an unknown id, missed by default, and refused as
        // a duplicate with the id in the message. One row rather than five, because what is being
        // checked is that modes went through the same indexer as everything else — if they had
        // not, the first assertion would already be a different shape.
        ModeSpec descent = Mode("mode.descent");
        var catalog = new ContentCatalog(Array.Empty<CharacterSpec>(), null, new[] { descent });

        Assert.That(catalog.Mode(new ContentId("mode.descent")), Is.SameAs(descent));
        Assert.That(catalog.TryGetMode(descent.Id, out ModeSpec found), Is.True);
        Assert.That(found, Is.SameAs(descent));
        Assert.That(catalog.Modes.Count, Is.EqualTo(1));
        Assert.That(catalog.Modes[0], Is.SameAs(descent));

        Assert.That(catalog.TryGetMode(new ContentId("mode.nothing"), out ModeSpec missing), Is.False);
        Assert.That(missing, Is.Null);
        Assert.That(catalog.TryGetMode(default, out _), Is.False);

        KeyNotFoundException unknown = Assert.Throws<KeyNotFoundException>(
            () => catalog.Mode(new ContentId("mode.nothing")));
        Assert.That(unknown.Message, Does.Contain("mode.nothing"));

        ArgumentException duplicate = Assert.Throws<ArgumentException>(
            () => new ContentCatalog(
                Array.Empty<CharacterSpec>(),
                null,
                new[] { Mode("mode.descent"), Mode("mode.descent") }));
        Assert.That(duplicate.Message, Does.Contain("mode.descent"));

        Assert.Throws<ArgumentException>(
            () => new ContentCatalog(Array.Empty<CharacterSpec>(), null, new ModeSpec[] { null }));

        // Omitted entirely is a catalog with no modes, not a null one — the same bargain the
        // enemy list has made since M1-07.
        Assert.That(new ContentCatalog(Array.Empty<CharacterSpec>()).Modes, Is.Empty);
    }

    private static ModeSpec Mode(string id) => new ModeSpec(
        new ContentId(id),
        new LocKey(id + ".name"),
        1,
        true,
        0,
        Scalings.Design(),
        Array.Empty<RosterEntry>());

    private static ContentId OathboundId() => new ContentId("character.oathbound");

    private static LocKey OathboundNameKey() => new LocKey("character.oathbound.name");

    private static MovementSpec OathboundMovement() => new MovementSpec(5.4f, 0.06f, 0.08f, 720f);

    /// <summary>CC §7's targeting table: range 12, weights 3 / 2 / 1 / 1.5, cadence 0.1.</summary>
    private static TargetingSpec OathboundTargeting() => new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f);

    /// <summary>CC §7's attack table — the Censer: 13 damage, 3 /s, 8 m, 60°, frame at 0.4.</summary>
    private static WeaponSpec OathboundWeapon() => new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f);

    /// <summary>CC §7's "Focus delay / cap / ramp" row: 0.4 s, ×1.3, over 1.0 s.</summary>
    private static FocusSpec OathboundFocus() => new FocusSpec(0.4f, 1f, 1.3f);

    /// <summary>CC §7's Charge table: 10 m / 0.22 s, 2.5 s, 20 / 5 m, buffer 0.15, trail 0.05.</summary>
    private static MovementSkillSpec OathboundMovementSkill() =>
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f);

    private static CharacterSpec Oathbound() =>
        new CharacterSpec(
            OathboundId(),
            OathboundNameKey(),
            120f,
            OathboundMovement(),
            OathboundTargeting(),
            OathboundWeapon(),
            OathboundFocus(),
            OathboundMovementSkill());
}

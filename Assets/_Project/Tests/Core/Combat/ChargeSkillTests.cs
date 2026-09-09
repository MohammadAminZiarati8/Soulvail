using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// M1-14's seven rules: what a press is worth and for how long, when the dash starts, how long it
/// lasts, how long it protects, when the button lights up again, and what a tick costs.
/// </summary>
/// <remarks>
/// <para>
/// CC §7's Charge row throughout — 10 m over 0.22 s, i-frames for the duration plus 0.05 s, a 2.5 s
/// cooldown and a 0.15 s input buffer — so a failure reads as "the Oathbound's dodge stopped
/// behaving the way the design says" rather than as an arithmetic puzzle. One row overrides the
/// cooldown with a modifier, and says why.
/// </para>
/// <para>
/// <b>Time is passed in absolutely, not accumulated.</b> Every row names the moment it is asserting
/// at, because that is how the class itself is written: nothing here counts down, so a test that
/// summed a clock would be measuring the fixture's float error rather than the skill's rules. The
/// boundaries are approached from both sides — 0.219 and 0.221 for the end of the dash, 2.49 and
/// 2.51 for the end of the cooldown — which is the honest way to pin an inequality without asserting
/// anything about a tick that lands exactly on it.
/// </para>
/// <para>
/// <b>Nothing here moves a character.</b> The 10 m of travel, the suspended motor, the pass-through
/// damage and the knockback are M1-15's, and <see cref="MovementSkillSpec.Distance"/>,
/// <see cref="MovementSkillSpec.Damage"/> and <see cref="MovementSkillSpec.Knockback"/> are
/// therefore authored and validated here but never acted on.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ChargeSkillTests
{
    // CC §7, Charge: "Distance / duration — 10 m / 0.22 s", "i-frames — duration + 0.05 s",
    // "Cooldown — 2.5 s", "Damage / knockback — 20 / 5 m", "Input buffer — 0.15 s".
    private const float Distance = 10f;
    private const float Duration = 0.22f;
    private const float CooldownSeconds = 2.5f;
    private const float InputBuffer = 0.15f;
    private const float Damage = 20f;
    private const float Knockback = 5f;
    private const float IFrameTrail = 0.05f;

    /// <summary>
    /// The <c>dt</c> every row passes. Deliberately arbitrary: <c>ChargeSkill.Tick</c> does not read
    /// it, because every schedule in the class is absolute against <c>now</c>. A row that mattered
    /// to this number would be testing an accumulator the class does not have.
    /// </summary>
    private const float Step = 1f / 60f;

    /// <summary>Where the character is looking: +Z on the ground plane, which is <c>Y</c> in XZ.</summary>
    private static readonly Vector2 Forward = new(0f, 1f);

    /// <summary>A full stick to the right — +X, so no row can confuse it with <see cref="Forward"/>.</summary>
    private static readonly Vector2 Right = new(1f, 0f);

    // ---- Construction --------------------------------------------------------------------------

    [Test]
    public void Ctor_NullSpec_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChargeSkill(null));
    }

    [Test]
    public void Spec_Invalid_Throws()
    {
        // A dash of no length, no duration or no cooldown is not a dash. The duration one is the
        // load-bearing guard: it is the window M1-15 suspends the motor for, and zero would be a
        // skill that fires, protects for the trail alone and moves nothing.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(distance: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(duration: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(cooldown: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(cooldown: -1f));

        // The same NaN hole every spec in Core/Content closes with the same spelling.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(duration: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(duration: float.PositiveInfinity));

        // Zero is legal for the four that are design statements rather than mistakes: a movement
        // skill that only repositions (M5-03's Shroudstep) deals no damage and no knockback, and a
        // class that wants neither latency cushion authors them away.
        Assert.DoesNotThrow(() => Spec(inputBuffer: 0f));
        Assert.DoesNotThrow(() => Spec(damage: 0f));
        Assert.DoesNotThrow(() => Spec(knockback: 0f));
        Assert.DoesNotThrow(() => Spec(iFrameTrail: 0f));

        // Negative is not. An i-frame trail of −0.05 would end the invulnerability *before* the dash
        // it belongs to, which is the opposite of what CC §5 asks the number for.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(iFrameTrail: -0.05f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(inputBuffer: -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(damage: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(knockback: float.PositiveInfinity));
    }

    // ---- Rules 1 and 2: the press and the start ------------------------------------------------

    [Test]
    public void Request_WhenReady_StartsNextTick()
    {
        var skill = new ChargeSkill(Spec());

        Assert.That(skill.IsReady, Is.True, "A fresh skill has no cooldown to wait out.");
        Assert.That(skill.IsActive, Is.False);

        skill.Request(0f);

        // The press does nothing on its own — the dash starts on a tick, like everything else in
        // core, so a command and the frame that acts on it stay separable.
        Assert.That(skill.IsActive, Is.False, "Request records; Tick decides.");

        bool started = skill.Tick(Step, 0.016f, Right, Forward);

        Assert.That(started, Is.True, "True on the tick the dash starts, and only then.");
        Assert.That(skill.IsActive, Is.True);
        Assert.That(skill.IsInvulnerable, Is.True);
        Assert.That(skill.IsReady, Is.False, "A dash in flight is not a button that can be pressed.");

        AssertDirection(skill.Direction, Right, "The stick aims the dash.");

        // And the press is spent: a second tick with nothing new asked for starts nothing.
        Assert.That(skill.Tick(Step, 0.032f, Right, Forward), Is.False);
    }

    [Test]
    public void Direction_FallsBackToFacing()
    {
        var skill = new ChargeSkill(Spec());

        skill.Request(0f);

        // CC §5: "current stick direction; facing direction if the stick is neutral". A dash with no
        // stick is a step forward rather than a dash to nowhere, which matters because the stick is
        // exactly what a player has let go of while standing still and burning Focus.
        Assert.That(skill.Tick(Step, 0f, Vector2.Zero, Forward), Is.True);

        AssertDirection(skill.Direction, Forward, "A neutral stick falls back to facing.");
    }

    [Test]
    public void Direction_IsNormalised()
    {
        var skill = new ChargeSkill(Spec());

        skill.Request(0f);

        // A stick at half deflection: length 0.5, pointing the same way as (0.6, 0.8). The dash is a
        // fixed 10 m however far the thumb travelled, so the magnitude is thrown away and only the
        // bearing is kept — a half-pushed stick dodges exactly as far as a full one.
        Assert.That(skill.Tick(Step, 0f, new Vector2(0.3f, 0.4f), Forward), Is.True);

        AssertDirection(skill.Direction, new Vector2(0.6f, 0.8f), "Normalised, not scaled.");
    }

    // ---- Rule 4: the windows -------------------------------------------------------------------

    [Test]
    public void IFrames_CoverDurationPlusTrail()
    {
        var skill = new ChargeSkill(Spec());

        StartAt(skill, 0f);

        TickAt(skill, 0.21f);
        Assert.That(skill.IsInvulnerable, Is.True, "Mid-dash.");
        Assert.That(skill.IsActive, Is.True);

        TickAt(skill, 0.26f);

        // The trail, and the whole reason it exists: the dash is over, the character is standing
        // still again, and damage is still being ignored. Without these 50 ms a dodge that visually
        // cleared an attack is undone by the frames between the glass and the simulation, which
        // CC §5 calls fatal in a game built on dodging.
        Assert.That(skill.IsInvulnerable, Is.True, "0.22 + 0.05 = 0.27 — still covered at 0.26.");
        Assert.That(skill.IsActive, Is.False, "But the dash itself ended at 0.22.");

        TickAt(skill, 0.28f);
        Assert.That(skill.IsInvulnerable, Is.False);
    }

    [Test]
    public void Active_EndsAtDuration()
    {
        var skill = new ChargeSkill(Spec());

        StartAt(skill, 0f);

        TickAt(skill, 0.219f);
        Assert.That(skill.IsActive, Is.True);

        TickAt(skill, 0.221f);
        Assert.That(skill.IsActive, Is.False, "0.22 s of dash, not a frame more.");

        // Over, but not available — the cooldown runs from the start of the dash, not its end.
        Assert.That(skill.IsReady, Is.False);
    }

    [Test]
    public void Cooldown_FromStart()
    {
        var skill = new ChargeSkill(Spec());

        StartAt(skill, 0f);

        TickAt(skill, 2.49f);

        Assert.That(skill.IsReady, Is.False);
        Assert.That(skill.IsActive, Is.False, "Long finished — this is the wait, not the dash.");

        TickAt(skill, 2.51f);

        // 2.5 s measured from the moment the dash began, so the real gap between two dashes is
        // 2.28 s of standing there. CC §5's cooldown is a commitment made at the start.
        Assert.That(skill.IsReady, Is.True);
    }

    // ---- Rule 3: the input buffer --------------------------------------------------------------

    [Test]
    public void BufferedPress_FiresWhenCooldownEnds()
    {
        var skill = new ChargeSkill(Spec());

        StartAt(skill, 0f);

        skill.Request(2.4f);

        Assert.That(TickAt(skill, 2.45f), Is.False, "0.05 s early — the cooldown has 0.05 s left.");

        // This is the buffer's entire purpose, and CC §5 spells it out: "a tap just before cooldown
        // ends still fires". A player watching the button fill up and pressing as it completes is
        // 50-100 ms of touch latency away from being told they pressed too soon.
        Assert.That(TickAt(skill, 2.5f), Is.True, "0.1 s old, inside the 0.15 s buffer.");
        Assert.That(skill.IsActive, Is.True);
    }

    [Test]
    public void StalePress_Discarded()
    {
        var skill = new ChargeSkill(Spec());

        StartAt(skill, 0f);

        skill.Request(2f);

        // Half a second old by the time the dash is possible — three times the buffer. The buffer
        // absorbs latency, not intent: a press made that long ago describes a fight that has moved
        // on, and firing it would dash the player somewhere they wanted to go half a second earlier.
        Assert.That(TickAt(skill, 2.5f), Is.False);
        Assert.That(skill.IsReady, Is.True, "The button is live — it is the press that expired.");
        Assert.That(skill.IsActive, Is.False);
    }

    [Test]
    public void Request_WhileActive_QueuedWithinBuffer()
    {
        var skill = new ChargeSkill(Spec());

        StartAt(skill, 0f);

        // Pressed 0.02 s before the dash ends: a player mashing the button mid-dodge.
        skill.Request(0.2f);

        Assert.That(TickAt(skill, 0.3f), Is.False, "The dash is over, but the cooldown has just begun.");

        // The press was live at 0.3 (0.1 s old) and merely early, so it stayed pending — and then it
        // aged out, because 0.2 + 0.15 is long past by the time the cooldown ends at 2.5. Being
        // early and being stale are the same rule read at two ages, and only the early one is kept.
        Assert.That(TickAt(skill, 2.5f), Is.False);
        Assert.That(skill.IsReady, Is.True);
    }

    [Test]
    public void CannotRestart_WhileActive()
    {
        var skill = new ChargeSkill(Spec());

        skill.Request(0f);
        Assert.That(skill.Tick(Step, 0f, Right, Forward), Is.True, "Arranged: dashing east.");

        skill.Request(0.1f);

        // Live press, no cooldown in the way — and still nothing, because a dash is in flight. CC §5
        // makes the dash a commitment; re-aiming it mid-flight would be steering, which is the one
        // thing the fixed direction exists to prevent.
        Assert.That(skill.Tick(Step, 0.11f, Forward, Forward), Is.False);
        Assert.That(skill.IsActive, Is.True);

        AssertDirection(skill.Direction, Right, "Still the direction it launched with.");
    }

    // ---- Rules 4 and 5: the fraction and the stat -----------------------------------------------

    [Test]
    public void CooldownFraction_Timeline()
    {
        var skill = new ChargeSkill(Spec());

        Assert.That(skill.CooldownFraction, Is.Zero, "Nothing to wait for before the first dash.");

        StartAt(skill, 0f);

        // What M1-16's radial fill draws: full the instant it is spent, empty when it is back.
        Assert.That(skill.CooldownFraction, Is.EqualTo(1f).Within(1e-4f));

        TickAt(skill, 1.25f);
        Assert.That(skill.CooldownFraction, Is.EqualTo(0.5f).Within(1e-4f));

        TickAt(skill, 2.5f);
        Assert.That(skill.CooldownFraction, Is.Zero);

        // And it stays empty rather than going negative once the wait is over.
        TickAt(skill, 4f);
        Assert.That(skill.CooldownFraction, Is.Zero);
    }

    [Test]
    public void CooldownModifier_Applies()
    {
        var skill = new ChargeSkill(Spec());
        var source = new object();

        skill.Cooldown.Add(new Modifier(ModifierKind.PercentAdd, -0.2f, source));

        Assert.That(skill.Cooldown.Value, Is.EqualTo(2f).Within(1e-4f), "2.5 × 0.8 — M3-12's node in advance.");

        StartAt(skill, 0f);

        TickAt(skill, 1.99f);
        Assert.That(skill.IsReady, Is.False);

        // ADR-0008 reaching the dash: the skill never reads spec.Cooldown after construction, so a
        // tree node, a Pact or an Elite affix shortens the dodge without a line of this class
        // knowing any of them exist.
        TickAt(skill, 2.01f);
        Assert.That(skill.IsReady, Is.True);
    }

    // ---- Rule 6: reset -------------------------------------------------------------------------

    [Test]
    public void Reset_MakesReady()
    {
        var skill = new ChargeSkill(Spec());

        StartAt(skill, 0f);
        TickAt(skill, 1f);

        Assert.That(skill.IsReady, Is.False, "Arranged: mid-cooldown.");
        Assert.That(skill.CooldownFraction, Is.GreaterThan(0f));

        skill.Reset();

        // What a new stage or a respawn gets: the player does not arrive owing a cooldown to a fight
        // that is over.
        Assert.That(skill.IsReady, Is.True);
        Assert.That(skill.IsActive, Is.False);
        Assert.That(skill.IsInvulnerable, Is.False);
        Assert.That(skill.CooldownFraction, Is.Zero);
        Assert.That(skill.Direction, Is.EqualTo(Vector2.Zero));

        // And the next press fires immediately rather than being measured against the old schedule.
        skill.Request(1f);
        Assert.That(skill.Tick(Step, 1f, Right, Forward), Is.True);
    }

    // ---- Rule 7: the cost ----------------------------------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        var skill = new ChargeSkill(Spec());

        StartAt(skill, 0f);

        // The steady state by a wide margin — every frame of every cooldown, and every frame of not
        // having pressed the button at all. AllocationAssert rather than a hand-rolled probe: on
        // Unity's Mono GC.GetAllocatedBytesForCurrentThread() is stubbed to zero and would pass code
        // that allocates freely (M0-02).
        AllocationAssert.None(() => skill.Tick(Step, 1f, Right, Forward));

        // And the start path, which is the one that reads the Stat and normalises the stick — the
        // two places an allocation could plausibly hide.
        var starter = new ChargeSkill(Spec());

        AllocationAssert.None(() =>
        {
            starter.Reset();
            starter.Request(1f);
            starter.Tick(Step, 1f, new Vector2(0.3f, 0.4f), Forward);
        });
    }

    /// <summary>Ticks once at <paramref name="now"/> with a neutral stick and a +Z facing.</summary>
    private static bool TickAt(ChargeSkill skill, float now) =>
        skill.Tick(Step, now, Vector2.Zero, Forward);

    /// <summary>Arranges a dash launched at <paramref name="now"/>, pointing east.</summary>
    private static void StartAt(ChargeSkill skill, float now)
    {
        skill.Request(now);

        Assert.That(skill.Tick(Step, now, Right, Forward), Is.True, "Arranged: the dash started.");
    }

    /// <summary>
    /// Asserts a ground-plane direction component by component. <c>Y</c> is the world's Z: these are
    /// XZ vectors, and NUnit's structural comparison on two floats would fail on the last bit of a
    /// normalisation that is correct.
    /// </summary>
    private static void AssertDirection(Vector2 actual, Vector2 expected, string because)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-4f), because);
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1e-4f), because);
    }

    /// <summary>CC §7's Charge row, with whichever number a row overrides.</summary>
    private static MovementSkillSpec Spec(
        MovementSkillKind kind = MovementSkillKind.Charge,
        float distance = Distance,
        float duration = Duration,
        float cooldown = CooldownSeconds,
        float inputBuffer = InputBuffer,
        float damage = Damage,
        float knockback = Knockback,
        float iFrameTrail = IFrameTrail)
    {
        return new MovementSkillSpec(
            kind,
            distance,
            duration,
            cooldown,
            inputBuffer,
            damage,
            knockback,
            iFrameTrail);
    }
}

using System;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// The M1-02 spec's nine rules, plus three rows for guards this task decided on rather than
/// inherited: <see cref="ShieldSpec"/>'s positive numbers, <see cref="Health"/>'s constructor,
/// and the finiteness of the <c>now</c> and <c>dt</c> the caller supplies.
/// </summary>
/// <remarks>
/// The Oathbound is the running example throughout — 140 HP, a 30-point Aegis recharging at 15
/// per second after 4 quiet seconds, and 0.5 s of hit i-frames (CC §7, CH §3.1) — because those
/// are the numbers <c>Oathbound.asset</c> ships and the ones the rules were written against.
/// Enemies are the other shape the component has to serve, and they appear as
/// <see cref="Unshielded"/>: no <see cref="ShieldSpec"/>, no i-frames.
/// </remarks>
[TestFixture]
public sealed class HealthTests
{
    private const float OathboundMaxHp = 140f;
    private const float AegisMax = 30f;
    private const float AegisRechargeDelay = 4f;
    private const float AegisRefillPerSecond = 15f;
    private const float OathboundHitIFrames = 0.5f;

    /// <summary>
    /// Tight enough that no two of the Oathbound's numbers satisfy each other's assertion.
    /// </summary>
    private const float Tolerance = 1e-4f;

    /// <summary>
    /// Where the allocation test parks what it reads, so the calls cannot be optimised away.
    /// </summary>
    private float _sink;

    // ---- Rule 2: shield first, remainder to HP -----------------------------------------

    [Test]
    public void Damage_HitsShieldFirst()
    {
        Health health = Oathbound();

        DamageResult result = health.ApplyDamage(20f, 0f);

        Assert.That(result.ToShield, Is.EqualTo(20f).Within(Tolerance));
        Assert.That(result.ToHp, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(result.Applied, Is.EqualTo(20f).Within(Tolerance));
        Assert.That(result.Blocked, Is.False);
        Assert.That(result.Killed, Is.False);

        Assert.That(health.Shield, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(health.Current, Is.EqualTo(OathboundMaxHp).Within(Tolerance),
            "A hit the Aegis fully absorbed must not touch HP at all.");
    }

    [Test]
    public void Damage_OverflowsToHp()
    {
        Health health = Oathbound();
        health.ApplyDamage(20f, 0f);

        // A second later, so the first hit's 0.5 s of i-frames have run out. Nothing has ticked,
        // so the shield is still the 10 the first hit left it at.
        DamageResult result = health.ApplyDamage(25f, 1f);

        Assert.That(result.ToShield, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(result.ToHp, Is.EqualTo(15f).Within(Tolerance));
        Assert.That(health.Shield, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(health.Current, Is.EqualTo(125f).Within(Tolerance));
    }

    [Test]
    public void Damage_NoShield_HitsHp()
    {
        Health health = Unshielded();

        DamageResult result = health.ApplyDamage(30f, 0f);

        Assert.That(result.ToShield, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(result.ToHp, Is.EqualTo(30f).Within(Tolerance));
        Assert.That(health.Current, Is.EqualTo(110f).Within(Tolerance));
        Assert.That(health.HasShield, Is.False);
        Assert.That(health.ShieldMax, Is.EqualTo(0f));
        Assert.That(health.ShieldFraction, Is.EqualTo(0f),
            "No shield reads as zero, not as a division by a zero maximum.");
    }

    // ---- Rule 1: nothing happens, and the three ways of saying so -----------------------

    [Test]
    public void Damage_Zero_IsNone()
    {
        Health health = Oathbound();

        DamageResult result = health.ApplyDamage(0f, 0f);

        Assert.That(result.Applied, Is.EqualTo(0f));
        Assert.That(result.Blocked, Is.False, "Nothing arrived, so nothing was turned away.");
        Assert.That(result.Killed, Is.False);

        // Rule 3 keys off Applied, not off having been called. A real hit at the same instant
        // must still land — if the zero-damage call had started i-frames, this would be Blocked,
        // and a weapon that reports a miss as 0 damage would make its target invulnerable.
        DamageResult real = health.ApplyDamage(10f, 0f);

        Assert.That(real.Blocked, Is.False);
        Assert.That(real.Applied, Is.EqualTo(10f).Within(Tolerance));
    }

    [Test]
    public void Damage_WhenDead_IsNone()
    {
        Health health = Unshielded();
        health.ApplyDamage(OathboundMaxHp, 0f);
        Assert.That(health.IsDead, Is.True);

        DamageResult result = health.ApplyDamage(5f, 1f);

        // None, not Blocked — the spec's distinction, and it is load-bearing: a view that
        // flashes a shrug on Blocked should not shrug on behalf of a corpse.
        Assert.That(result.Applied, Is.EqualTo(0f));
        Assert.That(result.Blocked, Is.False);
        Assert.That(result.Killed, Is.False, "Killed is true once per life, on the fatal call.");
        Assert.That(health.Current, Is.EqualTo(0f));
    }

    // ---- Rule 3: hit i-frames ------------------------------------------------------------

    [Test]
    public void Damage_StartsHitIFrames()
    {
        Health health = Oathbound();

        DamageResult first = health.ApplyDamage(5f, 1.0f);
        DamageResult during = health.ApplyDamage(5f, 1.4f);
        DamageResult after = health.ApplyDamage(5f, 1.6f);

        Assert.That(first.Applied, Is.EqualTo(5f).Within(Tolerance));

        // 1.4 is inside the window the first hit opened to 1.5.
        Assert.That(during.Blocked, Is.True);
        Assert.That(during.Applied, Is.EqualTo(0f));

        // 1.6 is past it. The window does not extend itself from the blocked call.
        Assert.That(after.Blocked, Is.False);
        Assert.That(after.Applied, Is.EqualTo(5f).Within(Tolerance));

        Assert.That(health.Shield, Is.EqualTo(AegisMax - 10f).Within(Tolerance),
            "Exactly two of the three hits got through.");
    }

    // ---- Rule 2: death -------------------------------------------------------------------

    [Test]
    public void Damage_Kills_AtZero()
    {
        var health = new Health(new Stat(10f), null, 0f);

        DamageResult result = health.ApplyDamage(10f, 0f);

        Assert.That(result.Killed, Is.True);
        Assert.That(result.ToHp, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(health.IsDead, Is.True);
        Assert.That(health.Current, Is.EqualTo(0f));
        Assert.That(health.Fraction, Is.EqualTo(0f));
    }

    // ---- Rule 4: shield recharge ---------------------------------------------------------

    [Test]
    public void Shield_RechargesAfterDelay()
    {
        Health health = Oathbound();
        health.ApplyDamage(AegisMax, 0f);
        Assert.That(health.Shield, Is.EqualTo(0f).Within(Tolerance));

        // Up to 3.9 s in 0.1 s steps. `now` is computed from the step index rather than
        // accumulated, so 39 additions of 0.1f cannot drift the last step across the deadline
        // and turn this row into a coin flip.
        for (int step = 1; step <= 39; step++)
        {
            health.Tick(0.1f, step * 0.1f);
        }

        Assert.That(health.Shield, Is.EqualTo(0f).Within(Tolerance),
            "The 4 s delay has not elapsed, so nothing has come back yet.");

        health.Tick(0.5f, 4.4f);

        // 6, not 7.5: the step covers [3.9, 4.4] and only the 0.4 s after the deadline counts.
        // Crediting the whole step would hand out a frame of free shield at every recharge, and
        // the size of the gift would depend on the frame rate.
        Assert.That(health.Shield, Is.EqualTo(6f).Within(1e-3f));
    }

    [Test]
    public void Shield_RechargeDelayResetsOnDamage()
    {
        Health health = Oathbound();
        health.ApplyDamage(AegisMax, 0f);

        health.Tick(1f, 4.5f);
        Assert.That(health.Shield, Is.EqualTo(7.5f).Within(1e-3f), "Recharging: 0.5 s past the deadline.");

        // A hit at 4.5 pushes the deadline out to 8.5.
        health.ApplyDamage(1f, 4.5f);
        Assert.That(health.Shield, Is.EqualTo(6.5f).Within(1e-3f));

        health.Tick(1f, 8f);
        Assert.That(health.Shield, Is.EqualTo(6.5f).Within(1e-3f),
            "8.0 is still inside the new delay — the timer restarted, it did not carry on.");

        health.Tick(1f, 9f);
        Assert.That(health.Shield, Is.EqualTo(6.5f + 7.5f).Within(1e-3f),
            "Only the 0.5 s of [8.0, 9.0] that is past 8.5 counts.");
    }

    [Test]
    public void Shield_ClampsAtMax()
    {
        Health health = Oathbound();
        health.ApplyDamage(1f, 0f);
        Assert.That(health.Shield, Is.EqualTo(29f).Within(Tolerance));

        health.Tick(1f, 10f);

        // A full second at 15/s is 15 points into a 1-point hole.
        Assert.That(health.Shield, Is.EqualTo(AegisMax).Within(Tolerance));
        Assert.That(health.ShieldFraction, Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void Shield_NoRechargeWhenDead()
    {
        Health health = Oathbound();
        health.ApplyDamage(AegisMax, 0f);
        health.ApplyDamage(OathboundMaxHp, 1f);
        Assert.That(health.IsDead, Is.True);

        health.Tick(1f, 10f);

        Assert.That(health.Shield, Is.EqualTo(0f).Within(Tolerance),
            "A corpse does not regenerate — the Aegis would be back before the death event was.");
    }

    // ---- Rule 5: healing ------------------------------------------------------------------

    [Test]
    public void Heal_ClampsAndReturnsActual()
    {
        Health health = Unshielded();
        health.ApplyDamage(10f, 0f);
        Assert.That(health.Current, Is.EqualTo(130f).Within(Tolerance));

        float healed = health.Heal(20f);

        Assert.That(healed, Is.EqualTo(10f).Within(Tolerance),
            "The return is what went in, not what was offered — a lifesteal node needs to know.");
        Assert.That(health.Current, Is.EqualTo(OathboundMaxHp).Within(Tolerance));

        Assert.That(health.Heal(20f), Is.EqualTo(0f), "Already full.");
    }

    [Test]
    public void Heal_WhenDead_Zero()
    {
        Health health = Unshielded();
        health.ApplyDamage(OathboundMaxHp, 0f);

        float healed = health.Heal(50f);

        Assert.That(healed, Is.EqualTo(0f));
        Assert.That(health.IsDead, Is.True, "Healing is not resurrection; Reset is.");
        Assert.That(health.Current, Is.EqualTo(0f));
    }

    // ---- Rule 6: following a live maximum -------------------------------------------------

    [Test]
    public void MaxHpDrop_ClampsCurrent()
    {
        var maxHp = new Stat(OathboundMaxHp);
        var health = new Health(maxHp, null, 0f);

        maxHp.Base = 100f;
        Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance), "Dropped by a Base change.");

        // The same rule has to hold however the value moved, so all three routes are exercised:
        // Base, a modifier, and taking that modifier off again.
        var node = new object();
        maxHp.Add(new Modifier(ModifierKind.PercentAdd, -0.5f, node));
        Assert.That(health.Current, Is.EqualTo(50f).Within(Tolerance), "Dropped by a modifier.");

        maxHp.RemoveAll(node);
        Assert.That(maxHp.Value, Is.EqualTo(100f).Within(Tolerance));
        Assert.That(health.Current, Is.EqualTo(50f).Within(Tolerance),
            "Removing the modifier restores the maximum, never the HP — otherwise a buff cycled " +
            "on and off would be a free heal every time.");
    }

    [Test]
    public void MaxHpRise_KeepsCurrent()
    {
        var maxHp = new Stat(100f);
        var health = new Health(maxHp, null, 0f);

        maxHp.Base = OathboundMaxHp;

        Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance),
            "+40 max HP is not a 40-point heal, or every such node becomes a panic button.");

        // The fraction reads against the live maximum, so the bar visibly empties as the
        // maximum grows — which is the honest picture.
        Assert.That(health.Fraction, Is.EqualTo(100f / 140f).Within(Tolerance));
    }

    // ---- Rules 7 and 8: external invulnerability, and Reset --------------------------------

    [Test]
    public void ExternalInvulnerable_Blocks()
    {
        Health health = Unshielded();

        health.SetExternalInvulnerable(true);
        DamageResult blocked = health.ApplyDamage(10f, 0f);

        Assert.That(blocked.Blocked, Is.True);
        Assert.That(health.Current, Is.EqualTo(OathboundMaxHp).Within(Tolerance));

        health.SetExternalInvulnerable(false);
        DamageResult applied = health.ApplyDamage(10f, 0f);

        // At the same instant, and it lands: the blocked call started no i-frames of its own.
        Assert.That(applied.Blocked, Is.False);
        Assert.That(applied.Applied, Is.EqualTo(10f).Within(Tolerance));
    }

    [Test]
    public void ExternalInvulnerable_IndependentOfHitIFrames()
    {
        Health noIFrames = Unshielded();

        Assert.That(noIFrames.IsInvulnerable, Is.False);
        noIFrames.SetExternalInvulnerable(true);
        Assert.That(noIFrames.IsInvulnerable, Is.True, "With hitIFrames 0, the flag is the only source.");
        noIFrames.SetExternalInvulnerable(false);
        Assert.That(noIFrames.IsInvulnerable, Is.False);

        // And the other direction: lowering the flag must not cut short i-frames it never
        // raised. A Charge ending mid-window would otherwise strip the protection of the hit
        // that was landing as it started.
        Health oathbound = Oathbound();
        oathbound.ApplyDamage(5f, 1f);
        oathbound.SetExternalInvulnerable(true);
        oathbound.SetExternalInvulnerable(false);

        Assert.That(oathbound.IsInvulnerable, Is.True, "The hit i-frames from 1.0 run to 1.5.");
        Assert.That(oathbound.ApplyDamage(5f, 1.2f).Blocked, Is.True);
    }

    [Test]
    public void Reset_RestoresAll()
    {
        Health health = Oathbound();
        health.ApplyDamage(AegisMax, 0f);
        health.ApplyDamage(OathboundMaxHp, 1f);
        health.SetExternalInvulnerable(true);
        Assert.That(health.IsDead, Is.True);

        health.Reset();

        Assert.That(health.Current, Is.EqualTo(OathboundMaxHp).Within(Tolerance));
        Assert.That(health.Shield, Is.EqualTo(AegisMax).Within(Tolerance));
        Assert.That(health.IsDead, Is.False);
        Assert.That(health.IsInvulnerable, Is.False,
            "The external flag goes down too — whoever raised it is gone and cannot lower it.");

        // The killing blow at 1.0 opened i-frames to 1.5. A hit at 1.0 landing is what proves
        // they were cleared rather than merely out of view.
        Assert.That(health.ApplyDamage(5f, 1f).Applied, Is.EqualTo(5f).Within(Tolerance));
    }

    // ---- M3-11a-i rules 4 to 8: the granted shield pool ------------------------------------
    //
    // The third pool, spent before the Aegis, put on by a cast and taken back by whatever granted
    // it. Every row here goes through Health's own surface: GrantedShieldPool is internal and
    // Soulvail.Tests.Core has no InternalsVisibleTo (M0-10), so what is asserted is the contract
    // that has to be true rather than the table that implements it.

    [Test]
    public void Health_GrantedShieldAbsorbsFirst()
    {
        Health health = Oathbound();
        var bulwark = new object();

        health.GrantShield(35f, bulwark);
        DamageResult result = health.ApplyDamage(20f, 0f);

        Assert.That(health.GrantedShield, Is.EqualTo(15f).Within(Tolerance));
        Assert.That(health.Shield, Is.EqualTo(AegisMax).Within(Tolerance), "The Aegis was not touched.");
        Assert.That(health.Current, Is.EqualTo(OathboundMaxHp).Within(Tolerance));

        // **The finding this row exists to pin.** DamageResult.ToShield means *the Aegis* — CH
        // §3.1's Martyr keystone sums it — so a hit swallowed whole by a granted shield reports
        // zero on both amounts and is not Blocked. It reads like a hit that did nothing, and
        // M3-13b is the task that has to tell those apart when it draws the pool.
        Assert.That(result.Blocked, Is.False);
        Assert.That(result.ToShield, Is.Zero, "Granted points are deliberately not reported as Aegis.");
        Assert.That(result.ToHp, Is.Zero);

        // …but the hit still happened, so it still opened i-frames. Read as `toShield + toHp` the
        // branch that does this would not have run at all.
        Assert.That(health.IsInvulnerable, Is.True, "A fully absorbed hit still starts i-frames.");
    }

    [Test]
    public void Health_SpillsIntoTheAegisThenHp()
    {
        Health health = Oathbound();
        var bulwark = new object();

        health.GrantShield(35f, bulwark);
        health.ApplyDamage(80f, 0f);

        // 35 out of the grant, 30 out of the Aegis, the last 15 into HP.
        Assert.That(health.GrantedShield, Is.Zero);
        Assert.That(health.Shield, Is.Zero);
        Assert.That(health.Current, Is.EqualTo(125f).Within(Tolerance));
    }

    [Test]
    public void Health_AegisNumbersAreUnchanged()
    {
        Health health = Oathbound();

        health.GrantShield(35f, new object());

        // The ring still means the Aegis. Raising ShieldMax to express a temporary shield would
        // have made all three of these lie for as long as the grant ran.
        Assert.That(health.ShieldMax, Is.EqualTo(AegisMax).Within(Tolerance));
        Assert.That(health.ShieldFraction, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(health.Shield, Is.EqualTo(AegisMax).Within(Tolerance));
        Assert.That(health.HasShield, Is.True);
    }

    [Test]
    public void Health_GrantStacksPerSource()
    {
        Health health = Oathbound();
        var bulwark = new object();

        health.GrantShield(35f, bulwark);
        health.GrantShield(35f, bulwark);

        // One source, so the second cast refreshes. Per-call stacking would make CH §4.1's floored
        // cooldown into permanent immunity, which is what the floor exists to prevent.
        Assert.That(health.GrantedShield, Is.EqualTo(35f).Within(Tolerance));
    }

    [Test]
    public void Health_TwoSourcesStack()
    {
        Health health = Oathbound();

        health.GrantShield(35f, new object());
        health.GrantShield(20f, new object());

        // Two different things, so they add. This is also the row that makes a single float
        // impossible: see Health_SpendsTheOldestGrantFirst for why.
        Assert.That(health.GrantedShield, Is.EqualTo(55f).Within(Tolerance));
    }

    [Test]
    public void Health_RefreshSetsTheSourceRatherThanStacking()
    {
        Health health = Oathbound();
        var bulwark = new object();

        health.GrantShield(35f, bulwark);
        health.ApplyDamage(20f, 0f);
        Assert.That(health.GrantedShield, Is.EqualTo(15f).Within(Tolerance));

        health.GrantShield(35f, bulwark);

        // **Renamed from the spec's `Health_RefreshDoesNotRestoreSpentPoints`, because 35 *is* the
        // 20 spent points handed back and the old name claimed the opposite of its own number.**
        // The rule is that a refresh *sets* the source's contribution: it does not stack to 50, and
        // it does not leave the source at what it had left. Both wrong answers are named below so
        // this row cannot pass by accident.
        Assert.That(health.GrantedShield, Is.EqualTo(35f).Within(Tolerance));
        Assert.That(health.GrantedShield, Is.Not.EqualTo(50f).Within(Tolerance), "A refresh is not a stack.");
        Assert.That(health.GrantedShield, Is.Not.EqualTo(15f).Within(Tolerance), "…nor a no-op.");
    }

    [Test]
    public void Health_SpendsTheOldestGrantFirst()
    {
        Health health = Oathbound();
        var first = new object();
        var second = new object();

        health.GrantShield(35f, first);
        health.GrantShield(20f, second);

        health.ApplyDamage(20f, 0f);

        // The total is 35 whichever source paid, so the total cannot say. Removing the older one is
        // what asks the question: it took the whole 20, so it has 15 left and what remains after it
        // goes is the younger grant's untouched 20. Spent the other way round this would read 0.
        Assert.That(health.GrantedShield, Is.EqualTo(35f).Within(Tolerance));
        Assert.That(health.RemoveGrantedShield(first), Is.True);
        Assert.That(health.GrantedShield, Is.EqualTo(20f).Within(Tolerance));
    }

    [Test]
    public void Health_RefreshKeepsItsPlaceInTheSpendOrder()
    {
        Health health = Oathbound();
        var first = new object();
        var second = new object();

        health.GrantShield(35f, first);
        health.GrantShield(20f, second);
        health.GrantShield(35f, first);

        health.ApplyDamage(10f, 0f);
        health.RemoveGrantedShield(first);

        // Rule 6's "resets nothing else" reaches the slot as well as the neighbours: a refreshed
        // source does not go to the back of the queue. Had it moved, the 10 would have come out of
        // the younger grant and this would read 10.
        Assert.That(health.GrantedShield, Is.EqualTo(20f).Within(Tolerance));
    }

    [Test]
    public void Health_GrantIsNotRecharged()
    {
        Health health = Oathbound();

        health.GrantShield(35f, new object());
        health.ApplyDamage(20f, 0f);

        for (int i = 0; i < 100; i++)
        {
            health.Tick(0.1f, 0.1f * (i + 1));
        }

        // Ten seconds, well past the Aegis's 4 s delay. The Aegis is what refills; a cast's points
        // are gone when the timer says and never before.
        Assert.That(health.GrantedShield, Is.EqualTo(15f).Within(Tolerance));
    }

    [Test]
    public void Health_HealDoesNotTouchIt()
    {
        Health health = Oathbound();

        // 30 into the Aegis and 40 into HP, leaving 100 of 140.
        health.ApplyDamage(70f, 0f);
        health.GrantShield(15f, new object());

        Assert.That(health.Heal(30f), Is.EqualTo(30f).Within(Tolerance));

        Assert.That(health.Current, Is.EqualTo(130f).Within(Tolerance));
        Assert.That(health.GrantedShield, Is.EqualTo(15f).Within(Tolerance), "Heal restores HP and nothing else.");
        Assert.That(health.Shield, Is.Zero, "…and it never refilled the Aegis either.");
    }

    [Test]
    public void Health_RemoveTakesWhatIsLeft()
    {
        Health health = Oathbound();
        var bulwark = new object();

        health.GrantShield(35f, bulwark);
        health.ApplyDamage(20f, 0f);

        Assert.That(health.RemoveGrantedShield(bulwark), Is.True);

        // 15 came off, not 35. Removing what was *granted* would have driven the pool five points
        // below zero and cost a player who had already spent it.
        Assert.That(health.GrantedShield, Is.Zero);
        Assert.That(health.GrantedShield, Is.Not.Negative);
        Assert.That(health.Current, Is.EqualTo(OathboundMaxHp).Within(Tolerance), "HP was never involved.");

        // And the pool really is out of the way: the next hit goes straight to the Aegis. The hit
        // above opened i-frames to 0.5, so this one is taken after them.
        health.ApplyDamage(10f, 1f);
        Assert.That(health.Shield, Is.EqualTo(20f).Within(Tolerance));
    }

    [Test]
    public void Health_RemoveUnknownSource_IsFalse()
    {
        Health health = Oathbound();
        var granted = new object();
        var never = new object();

        Assert.That(health.RemoveGrantedShield(never), Is.False, "Nothing was ever granted.");

        health.GrantShield(35f, granted);

        Assert.That(health.RemoveGrantedShield(never), Is.False, "…and a stranger takes nothing off.");
        Assert.That(health.GrantedShield, Is.EqualTo(35f).Within(Tolerance));

        // A source holding nothing is still a source that is *held*, so it removes and answers
        // true — which is what lets an expiry clean up unconditionally.
        health.ApplyDamage(35f, 0f);
        Assert.That(health.GrantedShield, Is.Zero);
        Assert.That(health.RemoveGrantedShield(granted), Is.True);
        Assert.That(health.RemoveGrantedShield(granted), Is.False, "…and only once.");
    }

    [Test]
    public void Health_ResetClearsThePool()
    {
        Health health = Oathbound();
        var bulwark = new object();

        health.GrantShield(35f, bulwark);
        health.Reset();

        Assert.That(health.GrantedShield, Is.Zero);

        // Forgotten, not merely emptied: the source is gone, so removing it now finds nothing.
        Assert.That(health.RemoveGrantedShield(bulwark), Is.False);
    }

    [Test]
    public void Health_GrantedShieldIsInvulnerableSafe()
    {
        // The external flag — a Charge dash, CC §5.
        Health charging = Oathbound();
        charging.GrantShield(35f, new object());
        charging.SetExternalInvulnerable(true);

        Assert.That(charging.ApplyDamage(20f, 0f).Blocked, Is.True);
        Assert.That(charging.GrantedShield, Is.EqualTo(35f).Within(Tolerance), "Nothing was spent.");

        // And hit i-frames, the other path. Both are answered before anything is spent because the
        // pool is spent *after* the IsInvulnerable guard, exactly as the Aegis always has been.
        Health hit = Oathbound();
        hit.ApplyDamage(5f, 1f);
        hit.GrantShield(35f, new object());

        Assert.That(hit.ApplyDamage(20f, 1.2f).Blocked, Is.True, "The i-frames from 1.0 run to 1.5.");
        Assert.That(hit.GrantedShield, Is.EqualTo(35f).Within(Tolerance));
    }

    [Test]
    public void Health_GrantRefusesNullSourceAndNonFiniteAmount()
    {
        Health health = Oathbound();
        var bulwark = new object();

        // A sourceless grant could never be taken back — Modifier's rule one layer down — so it
        // would be a shield that lasted the run.
        Assert.Throws<ArgumentNullException>(() => health.GrantShield(35f, null));
        Assert.Throws<ArgumentNullException>(() => health.RemoveGrantedShield(null));

        // A NaN in the pool would make GrantedShield NaN for the rest of the run: every comparison
        // against it is false, so it would absorb every hit forever with nothing logged.
        Assert.Throws<ArgumentOutOfRangeException>(() => health.GrantShield(float.NaN, bulwark));
        Assert.Throws<ArgumentOutOfRangeException>(() => health.GrantShield(float.PositiveInfinity, bulwark));
        Assert.Throws<ArgumentOutOfRangeException>(() => health.GrantShield(float.NegativeInfinity, bulwark));
        Assert.Throws<ArgumentOutOfRangeException>(() => health.GrantShield(-1f, bulwark));

        Assert.That(health.GrantedShield, Is.Zero, "Not one of them got in.");

        // Zero is legal, and it holds the source: it is the state the pool reaches by being spent,
        // so refusing it here would refuse a value the class already produces.
        Assert.DoesNotThrow(() => health.GrantShield(0f, bulwark));
        Assert.That(health.GrantedShield, Is.Zero);
        Assert.That(health.RemoveGrantedShield(bulwark), Is.True, "A source at zero is still held.");
    }

    [Test]
    public void Health_GrantCapacityThrows()
    {
        Health health = Oathbound();
        var sources = new object[9];

        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = new object();
        }

        for (int i = 0; i < 8; i++)
        {
            health.GrantShield(1f, sources[i]);
        }

        Assert.That(health.GrantedShield, Is.EqualTo(8f).Within(Tolerance));

        // A ninth *source* is refused rather than grown into: this is walked from the damage path,
        // and a silently growing array there is the kind of thing only a phone ever finds.
        InvalidOperationException full = Assert.Throws<InvalidOperationException>(
            () => health.GrantShield(1f, sources[8]));

        Assert.That(full.Message, Does.Contain("8"), "The refusal names the capacity.");

        // Refreshing one of the eight is not a ninth source and is still allowed at capacity.
        Assert.DoesNotThrow(() => health.GrantShield(5f, sources[0]));
        Assert.That(health.GrantedShield, Is.EqualTo(12f).Within(Tolerance));

        // And a slot freed by a removal takes the ninth.
        Assert.That(health.RemoveGrantedShield(sources[3]), Is.True);
        Assert.DoesNotThrow(() => health.GrantShield(1f, sources[8]));
    }

    [Test]
    public void Health_GrantedShieldAllocatesNothing()
    {
        Health health = Oathbound();
        var first = new object();
        var second = new object();
        float clock = 0f;

        AllocationAssert.None(() =>
        {
            clock += 1f;
            health.Reset();

            health.GrantShield(35f, first);
            health.GrantShield(20f, second);

            // The Total walk, on the path RunState and the debug overlay read every frame.
            _sink += health.GrantedShield;

            // 80 spends the whole 55-point pool, then 25 of the Aegis — so the spend loop, the
            // Aegis branch and the i-frame branch all run rather than returning early.
            _sink += health.ApplyDamage(80f, clock).Applied;

            // Removal walks and shifts, which is the one path with a nested loop in it.
            health.RemoveGrantedShield(first);
            health.RemoveGrantedShield(second);
        });

        Assert.That(_sink, Is.GreaterThan(0f), "The probe measured calls that actually did something.");
    }

    // ---- Rule 9: allocation ----------------------------------------------------------------

    [Test]
    public void ApplyAndTick_AllocateNothing()
    {
        Health health = Oathbound();
        float clock = 0f;

        AllocationAssert.None(() =>
        {
            clock += 1f;

            // Reset first, so every iteration walks the same live paths instead of settling into
            // "already dead, already blocked" after the first few — and so Reset is measured too.
            health.Reset();

            // 35 rather than 5, so the hit overflows the 30-point Aegis into HP and leaves
            // headroom for the Heal below — otherwise both the overflow branch and Heal's
            // arithmetic return early and go unmeasured.
            _sink += health.ApplyDamage(35f, clock).Applied;
            health.Tick(0.016f, clock);

            // A second tick well past the recharge delay, because the first one returns before
            // the refill arithmetic. Without it the branch this row most needs to measure — the
            // Min/Max clamp — never runs.
            health.Tick(1f, clock + 5f);

            _sink += health.Heal(5f);
        });

        Assert.That(_sink, Is.GreaterThan(0f), "The probe measured calls that actually did something.");
    }

    // ---- Guards this task decided on, beyond the spec's rules -------------------------------

    [Test]
    public void ShieldSpec_RefusesNonPositiveNumbers()
    {
        // A shield that absorbs nothing, never refills, or refills instantly is better said by
        // passing no ShieldSpec at all — so all three are refused rather than normalised.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShieldSpec(0f, 4f, 15f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShieldSpec(30f, 0f, 15f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShieldSpec(30f, 4f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShieldSpec(-30f, 4f, 15f));

        // The hole `value <= 0f` would leave open, and the one it would not: NaN passes every
        // comparison, and infinity passes a positivity test while meaning "never".
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShieldSpec(float.NaN, 4f, 15f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShieldSpec(30f, float.PositiveInfinity, 15f));

        Assert.DoesNotThrow(() => new ShieldSpec(AegisMax, AegisRechargeDelay, AegisRefillPerSecond));
    }

    [Test]
    public void Constructor_RefusesNullMaxHpAndInvalidIFrames()
    {
        Assert.Throws<ArgumentNullException>(() => new Health(null, null, 0f));

        Assert.Throws<ArgumentOutOfRangeException>(() => new Health(new Stat(10f), null, -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Health(new Stat(10f), null, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Health(new Stat(10f), null, float.PositiveInfinity));

        // Zero is legal and is what every enemy uses.
        Assert.DoesNotThrow(() => new Health(new Stat(10f), null, 0f));
    }

    [Test]
    public void Time_MustBeFinite()
    {
        Health health = Oathbound();

        // A run's clock is the sum of each tick's Dt, so one non-finite value would be
        // permanent: the i-frame deadline and the recharge deadline are both derived from it,
        // every comparison against NaN is false, and the symptom would be i-frames that never
        // fire and an Aegis that never returns — silently, for the rest of the run.
        Assert.Throws<ArgumentOutOfRangeException>(() => health.ApplyDamage(5f, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => health.Tick(0.016f, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => health.Tick(0.016f, float.PositiveInfinity));

        // A negative step would drain the shield rather than fill it.
        Assert.Throws<ArgumentOutOfRangeException>(() => health.Tick(-0.016f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => health.Tick(float.NaN, 1f));

        // A NaN *amount* is not an exception: rule 1's `!(amount > 0f)` already answers "no
        // damage" for it, which is the right answer and needs no throw.
        Assert.DoesNotThrow(() => health.ApplyDamage(float.NaN, 1f));
        Assert.That(health.Current, Is.EqualTo(OathboundMaxHp).Within(Tolerance));
    }

    private static Health Oathbound() =>
        new Health(
            new Stat(OathboundMaxHp),
            new ShieldSpec(AegisMax, AegisRechargeDelay, AegisRefillPerSecond),
            OathboundHitIFrames);

    /// <summary>
    /// The shape every enemy takes (M1-05): no shield spec at all, and no i-frames, so each hit
    /// that reaches it counts.
    /// </summary>
    private static Health Unshielded() => new Health(new Stat(OathboundMaxHp), null, 0f);
}

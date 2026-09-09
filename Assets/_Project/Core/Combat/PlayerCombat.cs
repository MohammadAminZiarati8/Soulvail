using System;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Combat;

/// <summary>
/// The player's combat brain: what can hurt them, what they are aiming at, what they can perceive,
/// and which of that is worth telling anyone about. The one object that turns the run's enemies
/// into a target and a facing. See AR §3, §5 and §9, and CC §3.
/// </summary>
/// <remarks>
/// <para>
/// <b>It composes, it does not reimplement.</b> <see cref="Health"/> owns HP, the Aegis and
/// i-frames (M1-02); <see cref="Targeter"/> owns the 10 Hz decision and the focus override
/// (M1-04); <see cref="TargetScorer"/> owns the arithmetic (M1-03). What is new here is the wiring
/// none of them is allowed to know about: turning live <c>EnemyAgent</c>s into the plain numbers
/// the scorer reads, turning a chosen id into a direction the motor can turn towards, and turning
/// a <see cref="DamageResult"/> into the player's vocabulary of events. Each of those is a
/// translation between two things that must not name each other.
/// </para>
/// <para>
/// <b>The candidate buffer is the reason this type has a capacity.</b> Targeting runs against a
/// <see cref="ReadOnlySpan{T}"/> of <see cref="TargetCandidate"/> gathered fresh each tick, and
/// AR §4.3 forbids allocating on a per-frame path — so the array is built once, at the run's
/// concurrency cap, and refilled in place. It is the same number the snapshot and the enemy
/// registry are built with, and for the same reason: three buffers that disagree about how many
/// enemies may exist is three chances to be blind to one.
/// </para>
/// <para>
/// <b>It fires a weapon but resolves nothing.</b> The <see cref="Weapon"/> decides when a swing
/// starts and when its damage lands; what that damage touches is a physical question, so the
/// damage frame leaves as a <see cref="ConeHitIntent"/> and the answer comes back as a fact in
/// M1-11. Between the two, this class knows only that it asked.
/// </para>
/// <para>
/// <b>Nothing here ends the run.</b> A death is published and then let go of (M1-17 wires the
/// flow), which is also why the weapon keeps its cadence through it: until the death flow exists
/// there is nothing to stop, and a rule here about not swinging while dead would be a second
/// opinion on when a run is over.
/// </para>
/// </remarks>
public sealed class PlayerCombat
{
    /// <summary>
    /// The radius <c>CombatBlackboard.EnemiesWithin6m</c> counts. CC §6.4's Sever fires at three
    /// enemies inside it, and GD §8.1's clustering is what puts them there.
    /// </summary>
    private const float CloseRadius = 6f;

    /// <summary>
    /// The radius <c>CombatBlackboard.EnemiesWithin8m</c> counts — the Censer's cone range (CC §7),
    /// so this is "how many could I actually hit right now".
    /// </summary>
    private const float WeaponRadius = 8f;

    /// <summary>
    /// How far the shield fraction must drift from the last reported value before another
    /// <see cref="PlayerShieldChanged"/> is worth publishing. See that event's remarks: at CC §7's
    /// refill rate one 120 fps frame moves it by 0.004, so a per-frame event would be 120 a second
    /// to describe something invisible.
    /// </summary>
    private const float ShieldFractionEpsilon = 0.005f;

    /// <summary>
    /// Below this distance the player and the target are the same point and there is no direction
    /// to give. The same floor <c>EnemySystem</c> uses, for the same reason: normalising a
    /// separation of 1e-9 yields a unit vector made of noise, and normalising zero yields a NaN
    /// facing the motor would never recover from.
    /// </summary>
    private const float MinDirectionDistance = 1e-4f;

    /// <summary>Compared squared, so the facing never takes a square root it will discard.</summary>
    private const float MinDirectionDistanceSquared = MinDirectionDistance * MinDirectionDistance;

    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;
    private readonly TargetingSpec _targeting;

    /// <summary>
    /// Preallocated at the run's enemy capacity and refilled in place every tick. Never handed out
    /// — <see cref="Targeter.Tick"/> borrows a span over the filled prefix and keeps nothing.
    /// </summary>
    private readonly TargetCandidate[] _candidates;

    /// <summary>
    /// The shield fraction as last announced, by either a <see cref="PlayerShieldChanged"/> or the
    /// <see cref="PlayerDamaged"/> that carried one.
    /// </summary>
    /// <remarks>
    /// The baseline is what was last *reported*, not what the value was one tick ago, and the
    /// difference is the whole rule. A refill moves the fraction by less than
    /// <see cref="ShieldFractionEpsilon"/> per frame at any frame rate a phone runs at, so a
    /// tick-to-tick comparison would never fire at all and the Aegis would silently refill behind
    /// a HUD that never redrew it.
    /// </remarks>
    private float _lastReportedShieldFraction;

    /// <summary>
    /// The last <see cref="ConeHitIntent.RequestId"/> issued. Pre-incremented, so the first swing
    /// of a run asks question 1 and 0 is never a real request.
    /// </summary>
    private int _lastConeRequestId;

    /// <param name="spec">
    /// The class being played. Read once, here: its health numbers seed <see cref="Health"/>, its
    /// <see cref="CharacterSpec.Targeting"/> is shared by the scorer and the targeter, which both
    /// need it and must agree about it, and its <see cref="CharacterSpec.Weapon"/> seeds
    /// <see cref="Weapon"/>.
    /// </param>
    /// <param name="events">Where the five combat events go.</param>
    /// <param name="intents">
    /// Where each damage frame's <see cref="ConeHitIntent"/> is written. Held here rather than
    /// reached through the run, because this is the object that knows a swing landed and the
    /// question has to leave on the tick that produced it.
    /// </param>
    /// <param name="enemyCapacity">
    /// The most enemies a run may hold at once, which is how long the candidate buffer is. It must
    /// be the number the enemy registry and the <c>WorldSnapshot</c> were built with — see the
    /// class remarks.
    /// </param>
    /// <exception cref="ArgumentNullException">Any of the three references is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="enemyCapacity"/> is not positive.</exception>
    public PlayerCombat(CharacterSpec spec, IDomainEvents events, IIntentSink intents, int enemyCapacity)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        _events = events ?? throw new ArgumentNullException(nameof(events));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));

        if (enemyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(enemyCapacity),
                enemyCapacity,
                "enemyCapacity must be greater than zero.");
        }

        _targeting = spec.Targeting;
        _candidates = new TargetCandidate[enemyCapacity];

        // MaxHp is a fresh Stat rather than the spec's raw number, because this is the live
        // maximum a tree node or a Pact applies to (ADR-0008). The spec stays what a designer
        // typed. A null Shield is a class without an Aegis, which Health skips entirely.
        Health = new Health(new Stat(spec.MaxHp), spec.Shield, spec.HitIFrames);

        // One TargetingSpec, two readers. The scorer filters on AcquireRange and the targeter
        // applies the same boundary to bypass it for a focused enemy, so two copies of the number
        // would be two chances for the override to disagree with the scoring it overrides.
        Targeter = new Targeter(new TargetScorer(spec.Targeting), spec.Targeting);

        // The class's basic attack, live. Its two Stats are seeded from the spec and are where
        // every damage and fire-rate modifier in the game lands, M1-13's Focus ramp first.
        Weapon = new Weapon(spec.Weapon);

        Blackboard = new CombatBlackboard();

        // A full Aegis is fraction 1, and a class without one is 0. Either way the baseline starts
        // where the shield does, so the first tick of a run announces nothing.
        _lastReportedShieldFraction = Health.ShieldFraction;
    }

    /// <summary>HP, the Aegis, i-frames and death. The player's, and nobody else's.</summary>
    public Health Health { get; }

    /// <summary>Which enemy the character is facing, and why. Ticked from <see cref="Tick"/>.</summary>
    /// <remarks>
    /// Exposed rather than wrapped: everything the targeter decides is read straight off it, and a
    /// mirror of its properties here would be a second answer that could only ever disagree with
    /// the first. <see cref="FocusAt"/> and <see cref="ClearFocus"/> are the one exception, and
    /// they earn it — see their remarks.
    /// </remarks>
    public Targeter Targeter { get; }

    /// <summary>
    /// The character's basic attack and its cadence. Ticked from <see cref="Tick"/>; exposed for
    /// the same reason <see cref="Targeter"/> is, and because its two <see cref="Stat"/>s are what
    /// a modifier has to reach.
    /// </summary>
    public Weapon Weapon { get; }

    /// <summary>What the player perceives, refilled every tick. See <see cref="CombatBlackboard"/>.</summary>
    public CombatBlackboard Blackboard { get; }

    /// <summary>The player's HP has reached zero.</summary>
    public bool IsDead => Health.IsDead;

    /// <summary>
    /// A unit vector on the ground plane pointing at the current target, or <see langword="null"/>
    /// when there is nothing to point at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Non-null on a blocked target too. CC §3.6's answer to a Warden is to hold facing on it and
    /// show the glyph — the character is looking at the thing it cannot hurt, which is what makes
    /// "go around" legible — so blockedness changes the reticle, never the facing.
    /// </para>
    /// <para>
    /// Null is the honest answer rather than a stale direction, and <c>PlayerMotor</c> reads it as
    /// "face the way you are moving". Also null when the target is standing exactly on the player:
    /// there is genuinely no direction there, and inventing one from float noise would make the
    /// character spin.
    /// </para>
    /// </remarks>
    public Vector3? FaceDirection { get; private set; }

    /// <summary>
    /// What one second of fire is expected to do — CC §3.2's threshold for the finisher bonus.
    /// </summary>
    /// <remarks>
    /// Copied off the <see cref="Weapon"/> at the top of every tick rather than read through it on
    /// demand, so the number the scorer weighs and the number a debug overlay shows are the same
    /// one, taken at the same moment. It is a mirror of a live stat and therefore has to be
    /// refreshed; the alternative — a property forwarding to the weapon — would be one fewer field
    /// and one more way for <see cref="Reset"/> to disagree with itself.
    /// </remarks>
    public float DpsOneSecond { get; private set; }

    /// <summary>
    /// The <see cref="ConeHitIntent.RequestId"/> of the swing waiting on an answer, or −1 when
    /// nothing is in flight.
    /// </summary>
    /// <remarks>
    /// What M1-11's <c>ReportConeHits</c> matches a fact against. Kept because the round trip is
    /// asynchronous by construction: the body answers at least a frame later, and by then the only
    /// way to know whether a report belongs to the swing that is still owed one is to have written
    /// down which swing that was. A report carrying any other id is stale and can be dropped
    /// without guessing.
    /// </remarks>
    public int PendingConeRequestId { get; private set; } = -1;

    /// <summary>
    /// Applies <paramref name="amount"/> to the player at time <paramref name="now"/> and announces
    /// what happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single door damage comes through, so that "the player was hurt" has one publisher. M1-18
    /// calls it for a Husk's strike; M2-07 will for a projectile.
    /// </para>
    /// <para>
    /// A call that did nothing says nothing. <see cref="Health"/> reports
    /// <see cref="DamageResult.None"/> for a non-positive amount and for a target that is already
    /// dead, and neither is news — publishing a <see cref="PlayerDamaged"/> of zeroes would flash
    /// the HUD for a hit that never arrived, and a second event for a corpse would make death
    /// arrive twice. A <see cref="DamageResult.Blocked"/> result is the opposite case and is
    /// published: something arrived and the i-frames turned it away, which CC §7's half-second is
    /// invisible without.
    /// </para>
    /// </remarks>
    /// <param name="amount">Damage to apply. Zero, negative and NaN all do nothing.</param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>, never a wall clock.</param>
    /// <returns>What <see cref="Health"/> did, unchanged, for a caller that has its own conclusions to draw.</returns>
    public DamageResult ApplyDamage(float amount, float now)
    {
        DamageResult result = Health.ApplyDamage(amount, now);

        // Spelled as "nothing arrived" rather than as a comparison against None, so a future
        // DamageResult field cannot quietly change what counts as silence.
        if (!result.Blocked && !(result.Applied > 0f))
        {
            return result;
        }

        // Moved before the publish, so a handler that damages the player again from inside this
        // event sees a baseline that already includes this hit.
        _lastReportedShieldFraction = Health.ShieldFraction;

        _events.Publish(new PlayerDamaged(
            result.ToShield,
            result.ToHp,
            Health.Fraction,
            Health.ShieldFraction,
            result.Blocked));

        // Exactly once per life without a flag to remember it: Killed is true only on the call that
        // took HP to zero, and every later call finds a dead target and returns None above.
        if (result.Killed)
        {
            _events.Publish(new PlayerDied(now));
        }

        return result;
    }

    /// <summary>
    /// The player tapped <paramref name="worldPoint"/>: focus whatever living enemy is nearest it
    /// within <see cref="FocusResolver.RadiusMetres"/>, or drop the focus when nothing is (CC §3.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place a point becomes a focus, which is why it is a method here rather than a
    /// forward to <see cref="Targeter.Focus"/>: the targeter is handed ids and knows nothing about
    /// space, and the resolver is pure arithmetic that knows nothing about targeting. Joining them
    /// is exactly this class's job, the same translation it already does between agents and
    /// candidates.
    /// </para>
    /// <para>
    /// <b>Tapping bare ground clears.</b> CC §3.4 is explicit, and it is the only way back to
    /// auto-aim a thumb has: there is no second gesture, and a focus the player cannot cancel would
    /// be a worse override than none.
    /// </para>
    /// <para>
    /// Nothing lands until the next <see cref="Tick"/> — <see cref="Targeter.Focus"/> defers, for
    /// the reason it documents — so a tap and its consequence are separated by at most one frame,
    /// and the <c>TargetChanged</c> that announces it comes out of the tick like every other
    /// targeting decision rather than from inside an input callback.
    /// </para>
    /// </remarks>
    /// <param name="worldPoint">Where the tap landed on the ground plane, in world metres. Y is ignored.</param>
    /// <param name="enemies">
    /// Every registered enemy — <c>EnemyRegistry.Alive</c>, the same span <see cref="Tick"/> is
    /// handed. Passed in rather than held, because this class deliberately owns no registry: it is
    /// given the world each time it is asked to think about it, and a retained span would be a
    /// dangling one the moment anything spawned.
    /// </param>
    public void FocusAt(Vector3 worldPoint, ReadOnlySpan<EnemyAgent> enemies)
    {
        int id = FocusResolver.Resolve(worldPoint, enemies, FocusResolver.RadiusMetres);

        // Spelled as the two branches rather than relying on Focus(-1) folding into ClearFocus,
        // because the reader here should not have to know that it does.
        if (id >= 0)
        {
            Targeter.Focus(id);
            return;
        }

        Targeter.ClearFocus();
    }

    /// <summary>Drops the player's focus, returning target selection to scoring.</summary>
    /// <remarks>
    /// A forward, and the only one on this class. It exists so that the two halves of the focus
    /// command arrive through one object — <c>RunSession</c> implements <c>IPlayerCommands</c> by
    /// calling this pair, and having one of them reach into <see cref="Targeter"/> while the other
    /// did not would make the port's two members look like they belonged to different systems.
    /// </remarks>
    public void ClearFocus() => Targeter.ClearFocus();

    /// <summary>
    /// One frame of the player's fight: gather, target, tick health, perceive, face, swing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order is the rule.</b> Candidates are built from positions the run has already
    /// ingested this frame, so targeting decides on where enemies are now rather than where they
    /// were; the target is chosen before the blackboard records it; the facing is derived from the
    /// decision the targeter just made; and the weapon swings last, at whatever that decision left
    /// it pointing at. Reversing any pair would leave one of them answering with last frame's
    /// world.
    /// </para>
    /// <para>
    /// <see cref="DpsOneSecond"/> is refreshed before the targeting pass rather than after it, so
    /// CC §3.2's finisher bonus is weighed against this tick's fire rate. A weapon that just got
    /// 30 % faster should start preferring the wounded enemy immediately, not next frame.
    /// </para>
    /// <para>
    /// No null guard on <paramref name="snapshot"/>, for the reason <c>RunSession.Tick</c> gives:
    /// this runs 60 times a second against one instance the builder owns for a whole run, so a null
    /// could only be the first tick after a mis-wired scope — a failure that arrives immediately
    /// and unmissably either way.
    /// </para>
    /// </remarks>
    /// <param name="dt">Seconds since the previous tick — the snapshot's <c>Dt</c>.</param>
    /// <param name="now">Simulated run time at the end of this step.</param>
    /// <param name="snapshot">This frame's world, for the player's position and the move stick.</param>
    /// <param name="enemies">
    /// Every registered enemy, the dead included — <c>EnemyRegistry.Alive</c>. Borrowed for the
    /// duration of the call and never retained.
    /// </param>
    /// <param name="bodyFacing">
    /// Which way the character is currently pointing: <c>PlayerMotor.Facing</c>, a unit vector on
    /// the ground plane. The swing arc is centred on it, so it is the body's facing rather than the
    /// direction to the target — a cone that always landed on the target regardless of where the
    /// character was pointing is exactly the auto-aim CC §3.5 says players stop trusting.
    /// <para>
    /// Passed in rather than read, because the motor is the run's to tick and this class must not
    /// hold a handle it could advance. It is the facing as of the previous tick — the run turns the
    /// motor <em>after</em> combat decides where to look — so a swing that lands while the
    /// character is still turning is aimed up to one frame of rotation behind the pose that gets
    /// drawn: 12° at 60 fps, inside a 60° arc, and only while turning. The same one-frame lag
    /// ADR-0003 accepts on positions, and cheaper than the alternative, which is a frame of latency
    /// on the targeting decision itself.
    /// </para>
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// There are more enemies than the capacity this was built with. Loud rather than truncated:
    /// silently dropping the tail would make the character blind to enemies the run knows about,
    /// and the two numbers come from one constant precisely so this cannot happen.
    /// </exception>
    public void Tick(
        float dt,
        float now,
        WorldSnapshot snapshot,
        ReadOnlySpan<EnemyAgent> enemies,
        Vector3 bodyFacing)
    {
        DpsOneSecond = Weapon.DpsOneSecond;

        int count = BuildCandidates(snapshot.PlayerPosition, enemies);

        Targeter.Tick(dt, new ReadOnlySpan<TargetCandidate>(_candidates, 0, count), DpsOneSecond);

        if (Targeter.ChangedThisTick)
        {
            _events.Publish(new TargetChanged(
                Targeter.CurrentTargetId,
                // "This target is the focused one", not "a focus is held" — see the event's
                // remarks. Guarded against −1 so that no target and no focus cannot both be −1 and
                // read as a match.
                Targeter.CurrentTargetId >= 0 && Targeter.CurrentTargetId == Targeter.FocusedTargetId,
                Targeter.IsCurrentBlocked));
        }

        Health.Tick(dt, now);
        PublishShieldIfDrifted();

        UpdateBlackboard(dt, snapshot, count);
        UpdateFaceDirection(snapshot.PlayerPosition, enemies);

        TickWeapon(dt, now, snapshot.PlayerPosition, bodyFacing, count);
    }

    /// <summary>
    /// Back to the start of a run: full health, no target, no focus, a weapon at rest, a blank
    /// blackboard and no facing.
    /// </summary>
    /// <remarks>
    /// What a respawn or a new stage gets instead of a rebuilt <see cref="PlayerCombat"/>, for the
    /// reason <c>Health.Reset</c> and <c>Targeter.Reset</c> exist: rebuilding would allocate and
    /// would leave the old <see cref="Health"/> subscribed to a <see cref="Stat"/> nobody owns.
    /// Nothing is published — a reset is not a heal and not a death, and a HUD reading a full bar
    /// after one has been told to redraw by whatever asked for the reset.
    /// </remarks>
    public void Reset()
    {
        Health.Reset();
        Targeter.Reset();
        Weapon.Reset();
        Blackboard.Reset();

        FaceDirection = null;
        DpsOneSecond = 0f;
        _lastReportedShieldFraction = Health.ShieldFraction;

        // The counter goes back to zero with everything else, so request ids stay monotonic within
        // the stretch of run they have to be matched across. Nothing can be in flight: a reset
        // happens between stages, and a report that arrives after one belongs to a swing whose
        // enemies have been cleared out from under it.
        PendingConeRequestId = -1;
        _lastConeRequestId = 0;
    }

    /// <summary>Rules 1–7 of M1-10: swing, announce, and ask the body what the swing touched.</summary>
    /// <remarks>
    /// <para>
    /// Last in the tick, because everything it needs was decided earlier in the same tick: the
    /// target comes from the targeting pass, and the distance from the candidate buffer that pass
    /// read. The weapon itself knows none of that — it is handed one <see cref="bool"/> and
    /// answers with timing.
    /// </para>
    /// <para>
    /// The two moments leave by different doors on purpose. A swing start is news, so it is
    /// published; a damage frame is a question only the body can answer, so it is an intent. That
    /// is ADR-0003's split, and it is why the view can play a windup on a swing that ends up
    /// hitting nothing.
    /// </para>
    /// </remarks>
    private void TickWeapon(float dt, float now, Vector3 playerPosition, Vector3 bodyFacing, int count)
    {
        WeaponTick tick = Weapon.Tick(dt, now, IsTargetInWeaponRange(count));

        // Read once and shared by both, so the arc a view draws and the wedge the body sweeps
        // cannot describe two different swings.
        var facingXZ = new Vector2(bodyFacing.X, bodyFacing.Z);

        if (tick.SwingStarted)
        {
            _events.Publish(new PlayerAttacked(facingXZ));
        }

        if (!tick.DamageFrame)
        {
            return;
        }

        _lastConeRequestId++;
        PendingConeRequestId = _lastConeRequestId;

        _intents.ConeHit(new ConeHitIntent(
            _lastConeRequestId,
            playerPosition,
            facingXZ,
            Weapon.Range,
            Weapon.ConeAngleDeg));
    }

    /// <summary>
    /// Rule 5: is there something worth swinging at — a live, unblocked target inside the weapon's
    /// reach?
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distance comes from the candidate buffer rather than from a fresh subtraction, because
    /// the buffer's is the number targeting just decided on. Deriving it again from the agents
    /// would be a second answer to the same question, and the two would differ on exactly the tick
    /// an enemy crossed the boundary.
    /// </para>
    /// <para>
    /// Blocked targets are excluded, which is CC §3.6 arriving where it matters: the character
    /// keeps facing the Warden it cannot hurt — see <see cref="FaceDirection"/> — and stops
    /// swinging at it, so the game says "go around" without the weapon pretending to connect. The
    /// blocked enemy is not swung *through* either; a cone that fired anyway would kill whatever
    /// stood behind it and make the block look like a lie.
    /// </para>
    /// </remarks>
    private bool IsTargetInWeaponRange(int count)
    {
        int id = Targeter.CurrentTargetId;

        if (id < 0 || Targeter.IsCurrentBlocked)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            ref readonly TargetCandidate candidate = ref _candidates[i];

            if (candidate.Id == id)
            {
                return candidate.Distance <= Weapon.Range;
            }
        }

        // Unreachable while the targeter is fed from this same buffer, and cheap to be right about:
        // a target with no candidate behind it is nothing to swing at.
        return false;
    }

    /// <summary>
    /// Fills the candidate buffer from the live agents and returns how many entries are valid.
    /// </summary>
    /// <remarks>
    /// CC §3.1's steps 1 and 2, and the one place an <c>EnemyAgent</c> becomes plain numbers. The
    /// dead are included rather than filtered, carrying <c>Hp</c> 0 and <c>IsVulnerable</c> false:
    /// a corpse sits in the registry until M1-11 despawns it, and the targeter reads exactly those
    /// two facts to drop a focus and to force an immediate retarget. Filtering here would hide the
    /// death from the code whose job is to react to it.
    /// </remarks>
    private int BuildCandidates(Vector3 playerPosition, ReadOnlySpan<EnemyAgent> enemies)
    {
        if (enemies.Length > _candidates.Length)
        {
            throw new InvalidOperationException(
                $"PlayerCombat was built for {_candidates.Length} enemies but was handed "
                    + $"{enemies.Length}. The candidate buffer, the enemy registry and the world "
                    + "snapshot must all be built with the same capacity.");
        }

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyAgent agent = enemies[i];
            Vector3 position = agent.Position;

            // XZ, like every other distance in this project: the height difference between a
            // player capsule's centre and an enemy's is a rendering detail, and counting it would
            // inflate every distance the acquire range is checked against.
            float dx = position.X - playerPosition.X;
            float dz = position.Z - playerPosition.Z;

            _candidates[i] = new TargetCandidate(
                agent.Id,
                MathF.Sqrt((dx * dx) + (dz * dz)),
                agent.Spec.TargetPriority,
                agent.Spec.IsElite,
                agent.IsAlive && agent.IsVulnerable,
                agent.Health.Current);
        }

        return enemies.Length;
    }

    /// <summary>Rule 2's last step: everything a trigger condition may ask about, refilled.</summary>
    private void UpdateBlackboard(float dt, WorldSnapshot snapshot, int count)
    {
        int within6 = 0;
        int within8 = 0;
        int inRange = 0;

        for (int i = 0; i < count; i++)
        {
            ref readonly TargetCandidate candidate = ref _candidates[i];

            // `Hp > 0` is aliveness arriving by the other route — the buffer carries the dead so
            // the targeter can see them, and the counts are about live threats. Spelled as the
            // negated positive so a NaN reads as dead and is skipped rather than counted.
            if (!(candidate.Hp > 0f))
            {
                continue;
            }

            float distance = candidate.Distance;

            // Three independent tests rather than nested bands, because the three radii are
            // authored separately and the widest is a per-class number: nesting them would encode
            // an ordering that a class with an 8 m acquire range would break.
            if (distance <= CloseRadius)
            {
                within6++;
            }

            if (distance <= WeaponRadius)
            {
                within8++;
            }

            if (distance <= _targeting.AcquireRange)
            {
                inRange++;
            }
        }

        Blackboard.HpFraction = Health.Fraction;
        Blackboard.ShieldFraction = Health.ShieldFraction;
        Blackboard.EnemiesWithin6m = within6;
        Blackboard.EnemiesWithin8m = within8;
        Blackboard.EnemiesInAcquireRange = inRange;
        Blackboard.CurrentTargetId = Targeter.CurrentTargetId;
        Blackboard.IsTargetBlocked = Targeter.IsCurrentBlocked;
        Blackboard.HasFocus = Targeter.HasFocus;

        // Cleared by any frame with input rather than decayed, so this really is "how long have I
        // been standing still". A NaN stick fails the test and resets it, which is the safe
        // direction: a trigger that waits for stillness should not fire on a broken input.
        Blackboard.StationaryTime = snapshot.MoveInput.LengthSquared() == 0f
            ? Blackboard.StationaryTime + dt
            : 0f;

        // Veilrot and IncomingProjectiles are deliberately untouched — see CombatBlackboard.
    }

    /// <summary>Rule 3: a unit XZ direction to the current target, or null.</summary>
    private void UpdateFaceDirection(Vector3 playerPosition, ReadOnlySpan<EnemyAgent> enemies)
    {
        int id = Targeter.CurrentTargetId;

        if (id < 0)
        {
            FaceDirection = null;
            return;
        }

        // A linear scan rather than a lookup, for the reason Targeter.IndexOf gives: the span is at
        // most GD §11's enemy cap, and a dictionary would allocate on a per-frame path. The
        // candidate buffer cannot answer this — TargetCandidate deliberately carries no position,
        // which is what keeps scoring a pure function of numbers.
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyAgent agent = enemies[i];

            if (agent.Id != id)
            {
                continue;
            }

            Vector3 position = agent.Position;
            float dx = position.X - playerPosition.X;
            float dz = position.Z - playerPosition.Z;
            float lengthSquared = (dx * dx) + (dz * dz);

            // Negated positive again: a NaN separation reads as "no direction" and the motor holds
            // its facing, rather than being handed a NaN it can never recover from.
            if (!(lengthSquared > MinDirectionDistanceSquared))
            {
                FaceDirection = null;
                return;
            }

            float length = MathF.Sqrt(lengthSquared);
            FaceDirection = new Vector3(dx / length, 0f, dz / length);
            return;
        }

        // Unreachable while the targeter is fed from this same span, and cheap to be right about:
        // an id with no agent behind it is nothing to look at.
        FaceDirection = null;
    }

    /// <summary>Rule 2's shield event: published on drift from the last reported value, not per frame.</summary>
    private void PublishShieldIfDrifted()
    {
        float fraction = Health.ShieldFraction;

        // "Return unless the drift is provably small", which is Stat.RaiseIfValueChanged's
        // spelling and semantics: a fraction that somehow went non-finite fails the comparison and
        // is therefore reported, rather than silencing this event for the rest of the run the way
        // the opposite spelling would. Silence is the worse failure — a frozen shield bar looks
        // like a bug in the shield.
        if (MathF.Abs(fraction - _lastReportedShieldFraction) < ShieldFractionEpsilon)
        {
            return;
        }

        _lastReportedShieldFraction = fraction;
        _events.Publish(new PlayerShieldChanged(fraction));
    }
}

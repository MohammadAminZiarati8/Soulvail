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
/// (M1-04); <see cref="TargetScorer"/> owns the arithmetic (M1-03); <see cref="FocusTracker"/> owns
/// the stationary clock and the ramp it is worth (M1-13). What is new here is the wiring
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
/// <b>It fires a weapon but decides nothing about geometry.</b> The <see cref="Weapon"/> decides
/// when a swing starts and when its damage lands; what that damage touches is a physical question,
/// so the damage frame leaves as a <see cref="ConeHitIntent"/> and the answer comes back as a list
/// of ids through <see cref="ResolveConeHits"/> (M1-11). Between the two, this class knows only
/// that it asked — and when the answer arrives it still never looks at a position, only at who was
/// named.
/// </para>
/// <para>
/// <b>Unless the weapon throws something, in which case nothing is asked at all</b> (M5-01). A
/// <see cref="WeaponKind.Projectile"/> damage frame produces a <see cref="Projectile"/> on
/// <see cref="PendingShot"/> rather than an intent, because an arrival is a point and a moment core
/// computes for itself — the body is owed a question only when the answer depends on colliders core
/// does not hold. The cadence above is shared exactly between the two kinds: <see cref="Weapon"/>
/// knows neither exists.
/// </para>
/// <para>
/// <b>The dash is the same round trip, asked twice as long.</b> <see cref="Charge"/> decides when
/// CC §5's dodge fires and which way it goes and nothing else (M1-14); this class raises the
/// i-frames it implies, sends the movement out as a <see cref="ChargeIntent"/>, and turns the ids
/// the body reports back through <see cref="ResolveChargeHits"/> into damage and knockback. Where a
/// cone is one wedge asked about once, a dash is a line swept over 0.22 s and reported many times,
/// so what makes the answer safe is not a request id but a window and a memory of who has already
/// been hit.
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
    /// A full circle — the widest wedge there is, and what <see cref="ConeAngle"/> clamps a live
    /// <see cref="Weapon.ConeAngleDeg"/> down to.
    /// </summary>
    /// <remarks>
    /// The same number <c>WeaponSpec</c> refuses an authored angle above, held separately rather
    /// than shared because the two say different things: there it is a validation of what a
    /// designer may type, here it is a ceiling on what a modifier stack may produce. A cone angle
    /// is the whole arc, not the half-angle.
    /// </remarks>
    private const float MaxConeAngleDeg = 360f;

    /// <summary>
    /// Below this distance the player and the target are the same point and there is no direction
    /// to give. The same floor <c>EnemySystem</c> uses, for the same reason: normalising a
    /// separation of 1e-9 yields a unit vector made of noise, and normalising zero yields a NaN
    /// facing the motor would never recover from.
    /// </summary>
    private const float MinDirectionDistance = 1e-4f;

    /// <summary>Compared squared, so the facing never takes a square root it will discard.</summary>
    private const float MinDirectionDistanceSquared = MinDirectionDistance * MinDirectionDistance;

    /// <summary>
    /// How long after a dash ends core still accepts a <see cref="ResolveChargeHits"/> report, in
    /// seconds.
    /// </summary>
    /// <remarks>
    /// The sweep is resolved at the end of the body's frame, after core has already advanced its
    /// clock past the dash's last instant, so a window that closed exactly on
    /// <c>Duration</c> would throw away the frame that finishes the movement. A tenth of a second is
    /// six frames at 60 fps and three at 30 — wide enough for a phone that is struggling, and far
    /// too narrow for the next dash, which cannot arrive for another 2.5 s.
    /// </remarks>
    private const float ChargeReportGrace = 0.1f;

    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;
    private readonly TargetingSpec _targeting;

    /// <summary>
    /// The class being played, as a content id — what a shot this character fires is stamped with.
    /// </summary>
    /// <remarks>
    /// The only thing this class keeps off <c>CharacterSpec</c> that is not a number or a block it
    /// reads every tick, and it is here because <c>Projectile.SpecId</c> is what a view picks a mesh
    /// from: a player's bolt has to be identifiable as <em>this class's</em> bolt, and the alternative
    /// — inventing an id for the weapon — would be a second name for something already named.
    /// </remarks>
    private readonly ContentId _characterId;

    /// <summary>
    /// The class's basic attack as authored: the block <see cref="Weapon"/> deliberately does not
    /// read past its own four stats — <see cref="WeaponSpec.Kind"/>,
    /// <see cref="WeaponSpec.ShotSpeed"/> and <see cref="WeaponSpec.ShotRadius"/>.
    /// </summary>
    /// <remarks>
    /// The same split <see cref="_movementSkill"/> makes, for the same reason and with the same
    /// boundary: <see cref="Weapon"/> owns <em>when</em> a swing lands, and these three describe
    /// <em>what</em> lands, which is this class's question. A weapon does not need to know it throws
    /// anything (M5-01 rule 1) — which is what left <c>Weapon.cs</c> untouched by the task that added
    /// a second kind.
    /// </remarks>
    private readonly WeaponSpec _weaponSpec;

    /// <summary>
    /// The class's authored movement skill: the numbers <see cref="Charge"/> deliberately does not
    /// read — <see cref="MovementSkillSpec.Distance"/>, <see cref="MovementSkillSpec.Duration"/>
    /// and <see cref="MovementSkillSpec.Knockback"/> — which are exactly the ones that describe
    /// what happens in the world rather than when.
    /// <para>
    /// <see cref="MovementSkillSpec.Damage"/> was a fourth until M3-12a, and is now read off
    /// <see cref="ChargeSkill.Damage"/> so that a node can move it. The spec still seeds that stat;
    /// what changed is which of the two this class asks.
    /// </para>
    /// </summary>
    private readonly MovementSkillSpec _movementSkill;

    /// <summary>
    /// Preallocated at the run's enemy capacity and refilled in place every tick. Never handed out
    /// — <see cref="Targeter.Tick"/> borrows a span over the filled prefix and keeps nothing.
    /// </summary>
    private readonly TargetCandidate[] _candidates;

    /// <summary>
    /// The enemies this report has already damaged, in <c>[0, <see cref="_hitCount"/>)</c>. Rule
    /// 2's dedupe, and it lives for the length of one <see cref="ResolveConeHits"/> call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sized to the enemy capacity rather than to the report, because the report's length is the
    /// body's to choose and this array's is not allowed to be. It cannot overflow: an entry is
    /// written only for an id that damage actually reached, which means a registered, living
    /// agent, and there are at most a capacity of those.
    /// </para>
    /// <para>
    /// A list of ids rather than a bitset, which the spec asked for and this cannot be: enemy ids
    /// increase for the whole run and are never reused (<c>EnemyRegistry</c> rule 1), so by the
    /// hundredth spawn they are far past any bit index a capacity-sized set could offer. Scanning
    /// it is O(k²) in the number of enemies one swing hits — at most 64 × 64 comparisons, three
    /// times a second, against a dictionary that would allocate.
    /// </para>
    /// </remarks>
    private readonly int[] _hitIds;

    /// <summary>How many entries of <see cref="_hitIds"/> the current report has filled.</summary>
    private int _hitCount;

    /// <summary>
    /// The enemies the dash in progress has already damaged, in
    /// <c>[0, <see cref="_chargeHitCount"/>)</c>. The same buffer <see cref="_hitIds"/> is, sized
    /// and scanned the same way and for the same reasons.
    /// </summary>
    /// <remarks>
    /// It lives for a whole dash rather than for one call, and that is the difference between the
    /// two mechanics. A cone is asked about once, so its dedupe only has to survive a single report;
    /// a dash is swept frame by frame and reported over and over, so "each enemy takes 20 once per
    /// Charge" is a memory that has to outlast every report inside the window. Cleared when a dash
    /// starts, never when one is answered.
    /// </remarks>
    private readonly int[] _chargeHitIds;

    /// <summary>How many entries of <see cref="_chargeHitIds"/> the dash in progress has filled.</summary>
    private int _chargeHitCount;

    /// <summary>
    /// The last moment a <see cref="ResolveChargeHits"/> report will be believed:
    /// <c>start + Duration + <see cref="ChargeReportGrace"/></c>. Negative infinity before the first
    /// dash of a run, which is in the past for any clock — so a report that arrives before anyone
    /// has dashed is refused by the same comparison that refuses a late one, without a flag to say
    /// so.
    /// </summary>
    private float _chargeHitWindowUntil = float.NegativeInfinity;

    /// <summary>
    /// Whether <see cref="ChargeSkill.IsInvulnerable"/> was true as of the previous tick. The edge
    /// this class watches for: it is what turns a property anyone can read into the one
    /// <see cref="ChargeEnded"/> a run gets per dash.
    /// </summary>
    private bool _wasChargeInvulnerable;

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

    /// <summary>
    /// Which way the swing that is owed an answer was facing, on the ground plane. Written on the
    /// damage frame beside <see cref="PendingConeRequestId"/>, read once by
    /// <see cref="ResolveConeHits"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A field rather than a parameter on <see cref="ResolveConeHits"/>, and the report is why</b>
    /// (M3-12b rule 7). The answer arrives at least a frame later, from a body that was handed the
    /// facing on the <c>ConeHitIntent</c> and has no reason to hand it back — so a parameter would
    /// mean the body remembering core's geometry across a round trip and every caller of
    /// <c>ReportConeHits</c> carrying it, to say something core already knew when it asked the
    /// question. The facing belongs to the pending cone exactly as its request id does, and lives
    /// beside it.
    /// </para>
    /// <para>
    /// It is the swing's facing and not the direction to each enemy, which is
    /// <c>ResolveChargeHits</c>' answer one method down and <c>EnemyKnockbackIntent.DirectionXZ</c>'s
    /// own rule: everything one sweep catches is swept the same way, which reads as a shove rather
    /// than as an explosion.
    /// </para>
    /// </remarks>
    private Vector2 _pendingConeFacingXZ;

    /// <param name="spec">
    /// The class being played. Read once, here: its health numbers seed <see cref="Health"/>, its
    /// <see cref="CharacterSpec.Targeting"/> is shared by the scorer and the targeter, which both
    /// need it and must agree about it, and its <see cref="CharacterSpec.Weapon"/> seeds
    /// <see cref="Weapon"/>.
    /// </param>
    /// <param name="events">Where the five combat events go.</param>
    /// <param name="intents">
    /// Where each damage frame's <see cref="ConeHitIntent"/>, each dash's <see cref="ChargeIntent"/>
    /// and every <see cref="EnemyKnockbackIntent"/> a dash causes are written. Held here rather than
    /// reached through the run, because this is the object that knows a swing landed and a dash
    /// began, and both have to leave on the tick that produced them.
    /// </param>
    /// <param name="enemyCapacity">
    /// The most enemies a run may hold at once, which is how long the candidate buffer and the
    /// cone-hit dedupe buffer are. It must be the number the enemy registry and the
    /// <c>WorldSnapshot</c> were built with — see the class remarks.
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
        _movementSkill = spec.MovementSkill;
        _characterId = spec.Id;
        _weaponSpec = spec.Weapon;
        _candidates = new TargetCandidate[enemyCapacity];
        _hitIds = new int[enemyCapacity];
        _chargeHitIds = new int[enemyCapacity];

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

        // Built after the weapon and handed its fire rate, which is the whole of the coupling: the
        // tracker moves one Stat and has no idea it belongs to a weapon. ADR-0008's first live
        // modifier, and the shape every later source of "+attack speed" copies.
        Focus = new FocusTracker(spec.Focus, Weapon.FireRate, _events);

        // The class's dodge, live. Its Cooldown is a Stat for the reason the weapon's two are, and
        // it is handed the whole spec rather than the cooldown alone because M5-03's Shroudstep and
        // M6-07's Blink are the same clock with a different payload — see MovementSkillKind.
        Charge = new ChargeSkill(spec.MovementSkill);

        // Zero, and the only one of M3-12a's five that is new rather than promoted — see the
        // property's own remarks for what a node has to do to move it.
        HealPerKill = new Stat(0f);

        // Zero too, and for the same reason and with the same trap in it: a swing shoves nobody
        // until a node says so. See the property's own remarks.
        SwingKnockback = new Stat(0f);

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

    /// <summary>
    /// CC §4.3's Focus ramp: the stationary clock, the level it is worth, and the modifier that
    /// puts it on <see cref="Weapon"/>'s fire rate. Ticked first in <see cref="Tick"/>; exposed for
    /// the same reason <see cref="Targeter"/> is.
    /// </summary>
    /// <remarks>
    /// Standing still, not the tap-to-focus of <see cref="FocusAt"/> — the design gives the two
    /// mechanics one word and this class holds both, so they are worth telling apart here: this
    /// property is a fire rate, <see cref="FocusAt"/> is a target.
    /// </remarks>
    public FocusTracker Focus { get; }

    /// <summary>
    /// CC §5's dodge: when it may fire, which way it goes, how long it protects, and how much of
    /// the cooldown is left. Ticked first in <see cref="Tick"/>; exposed for the same reason
    /// <see cref="Targeter"/> is, and because a press has to be able to reach
    /// <see cref="ChargeSkill.Request"/> from the command port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one property here a caller is expected to <em>write</em> to, through
    /// <see cref="ChargeSkill.Request"/> — which is a request rather than a trigger, so what it
    /// actually does is still this class's to decide on the next tick. Everything else on it is a
    /// read: <see cref="ChargeSkill.IsActive"/> is what suspends the motor (<c>RunSession</c>),
    /// <see cref="ChargeSkill.CooldownFraction"/> is what M1-16's button fills, and
    /// <see cref="ChargeSkill.IsInvulnerable"/> is deliberately *not* read by anything outside —
    /// the i-frames it implies are already on <see cref="Health"/>, and a second reader would be a
    /// second opinion on whether the player can be hurt.
    /// </para>
    /// <para>
    /// Named for the Oathbound's version and typed as the concrete class, and M5-03 is what
    /// settled that rather than what changed it: a Shroudstep is <em>this</em> clock with a corpse
    /// dropped on its start edge, so the second movement skill in the game added one branch to
    /// <see cref="TickCharge"/> and no interface. An <c>IMovementSkill</c> would be an abstraction
    /// with one implementation and two payloads, which is a switch spelled expensively.
    /// </para>
    /// </remarks>
    public ChargeSkill Charge { get; }

    /// <summary>
    /// Hit points restored for each enemy that dies, live. Zero until a node says otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠ Base 0 means a percentage modifier on this stat does nothing at all.</b>
    /// <see cref="Stat"/> computes <c>(Base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)</c>,
    /// so with a base of zero and no <see cref="ModifierKind.Flat"/> on the stack every percentage
    /// multiplies into zero. <b><see cref="ModifierKind.Flat"/> is the only kind that can ever move
    /// this number</b>, and a node authored as "+50 % heal per kill" heals nothing, silently and
    /// with no error anywhere. M3-12c's Retribution must be authored Flat — and a Flat node is
    /// what any *later* percentage node would then scale, which is the order to grant them in.
    /// </para>
    /// <para>
    /// <b>A stat rather than an on-kill trigger, deliberately</b> (M3-12a rule 5). One number on a
    /// kill is a number; the day a node wants "kills grant shield" or "kills leave a zone" is the
    /// day that earns the trigger primitive M3-05's Out of scope names.
    /// </para>
    /// <para>
    /// The base is zero rather than something small on purpose: a non-zero base would make every
    /// class in the game heal on kill, which is a balance change no design document asks for. The
    /// cost of zero is the paragraph above, and it is paid in writing rather than in arithmetic.
    /// </para>
    /// </remarks>
    public Stat HealPerKill { get; }

    /// <summary>
    /// How far each enemy a swing hits is shoved, in metres, live. Zero until a node says otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠ Base 0 means a percentage modifier on this stat does nothing at all.</b>
    /// <see cref="Stat"/> computes <c>(Base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)</c>,
    /// so with a base of zero and no <see cref="ModifierKind.Flat"/> on the stack every percentage
    /// multiplies into zero. <b><see cref="ModifierKind.Flat"/> is the only kind that can ever move
    /// this number</b> — <see cref="HealPerKill"/>'s trap exactly, measured there in M3-12a and
    /// written down here before it could be found twice.
    /// </para>
    /// <para>
    /// <b>The difference is that here the trap is unreachable, and that is what
    /// <c>KnockbackOnSwing</c> buys.</b> That primitive carries one distance and no kind, and its
    /// handler chooses <see cref="ModifierKind.Flat"/> itself, so there is no Inspector field a
    /// designer can get wrong — which is M3-12b rule 6's whole argument for a named primitive over
    /// a <see cref="PlayerStat"/> member. The warning stands anyway because this property is
    /// <see langword="public"/> and the day a second door reaches it, the arithmetic will not have
    /// changed. Nothing in <see cref="PlayerStat"/> names it, and
    /// <c>Charge_KnockbackIsNotAddressable</c> is the row that keeps it that way.
    /// </para>
    /// <para>
    /// Read once per swing by <see cref="ResolveConeHits"/>, which emits an
    /// <c>EnemyKnockbackIntent</c> per enemy hit only while the value is finite and above zero
    /// (rule 8): a stack driven negative is a pull and one driven non-finite is a teleport, and
    /// nothing in the design has asked for either.
    /// </para>
    /// </remarks>
    public Stat SwingKnockback { get; }

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
    /// The shot this tick's damage frame produced, or <see langword="null"/>. Read and cleared by
    /// the run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This class fires nothing; it offers a shot</b> (M5-01 rule 6). It deliberately owns no
    /// <see cref="ProjectileSystem"/>, for the reason it owns no registry: it is handed the world each
    /// time it is asked to think about it, and a constructor argument here would ripple through every
    /// fixture that builds one. <c>RunSession.Tick</c> takes the shot immediately after the combat
    /// step and puts it in the air on the same tick.
    /// </para>
    /// <para>
    /// <b>Overwritten rather than queued.</b> A <see cref="Projectile"/> in a nullable is a struct in
    /// a struct — nothing allocates, and there is no buffer to size. A second damage frame before the
    /// first shot was taken replaces it, which is <see cref="PendingConeRequestId"/>'s bargain
    /// exactly: a shot decided two frames ago was aimed with two-frame-old positions and is worth
    /// less than the newest one. It cannot happen while the run takes one every tick.
    /// </para>
    /// </remarks>
    public Projectile? PendingShot { get; private set; }

    /// <summary>Takes the pending shot, leaving none behind.</summary>
    /// <remarks>
    /// The clearing half matters more than the taking half: a shot left here would be fired again on
    /// the next tick that read it, so "take" is the only operation offered and there is no way to
    /// look without also consuming. Called unconditionally once a tick by <c>RunSession</c>, which is
    /// why it answers <see langword="false"/> rather than throwing when there is nothing.
    /// </remarks>
    /// <param name="shot">The shot, or <see langword="default"/> when there was none.</param>
    /// <returns>Whether there was a shot to take.</returns>
    public bool TryTakeShot(out Projectile shot)
    {
        if (PendingShot is null)
        {
            shot = default;
            return false;
        }

        shot = PendingShot.Value;
        PendingShot = null;

        return true;
    }

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
    /// Pays out <see cref="HealPerKill"/> for <paramref name="kills"/> deaths, and reports how much
    /// hit point actually went in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other end of <c>EnemySystem.DrainKills</c>, called once a tick by <c>RunSession</c> and
    /// by nothing else — beside the experience drain and, like it, after the death check, so a kill
    /// that lands on the tick the player dies heals a corpse nothing (M3-01a rule 6's ordering).
    /// </para>
    /// <para>
    /// <b>Unconditional and cheap.</b> The overwhelming majority of ticks pass zero, and every run
    /// this build ships has <see cref="HealPerKill"/> at zero for all of them, so the common path is
    /// one multiply and a <c>Heal</c> that returns immediately — <c>Health.Heal</c> is already
    /// silent for a non-positive amount, for a full bar and for a corpse. There is no branch here
    /// for a caller to get wrong.
    /// </para>
    /// <para>
    /// Nothing is published. A heal is not news the way damage is, and the HUD reads
    /// <c>Health.Fraction</c> — M3-13b is the task that decides whether a heal should flash.
    /// </para>
    /// </remarks>
    /// <param name="kills">
    /// Deaths since the last drain. Zero and negative both heal nothing, the second because a
    /// negative count is a caller bug that must not become a heal of negative size.
    /// </param>
    /// <returns>The hit points actually restored; zero on almost every tick of almost every run.</returns>
    public float HealForKills(int kills)
    {
        if (kills <= 0)
        {
            return 0f;
        }

        // Read once for the whole payout, like the cone's damage and for the same reason: three
        // kills drained together are worth three times one number, not the sum of three readings.
        return Health.Heal(kills * HealPerKill.Value);
    }

    /// <summary>
    /// The body has answered the outstanding <see cref="ConeHitIntent"/>: everyone in
    /// <paramref name="enemyIds"/> takes one swing's damage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Half of a round trip, and the half that has to be suspicious.</b> The question left on a
    /// damage frame and the answer arrives at least a frame later, so a report can be stale, a
    /// duplicate, or name enemies that have died in between. Each of those is handled by refusing
    /// rather than by guessing: no pending request means nothing is owed an answer and this report
    /// is dropped whole (which is also what makes a second report for one swing a no-op), and the
    /// pending id is cleared before any damage lands so that nothing a handler does from inside an
    /// <c>EnemyDamaged</c> can spend the same swing twice.
    /// </para>
    /// <para>
    /// <b>The report is matched by "is one owed", not by id.</b> Only one cone can be outstanding
    /// — a second damage frame overwrites <see cref="PendingConeRequestId"/> rather than queueing,
    /// because a swing whose answer arrives after the next swing has already been thrown is a
    /// swing whose geometry is two frames stale and worth less than the newest one. The id
    /// therefore rides on the intent for the body's own bookkeeping and for a log to make sense
    /// of, and this method needs only the flag. M1-15's Charge is the first thing that can put two
    /// requests in one tick, and it brings its own fact rather than sharing this one.
    /// </para>
    /// <para>
    /// <b>Damage is read once, for the whole report.</b> One swing is one number, so an enemy hit
    /// at the start of the arc and one at the end take the same 13 even if a modifier lands
    /// between them — which cannot happen inside this loop today, and would be a bug the day
    /// something publishes into a stat from an <c>EnemyDamaged</c> handler.
    /// </para>
    /// <para>
    /// <b>And the swing shoves, once a node says so</b> (M3-12b rule 5). <see cref="SwingKnockback"/>
    /// has a base of zero, so every run this build plays takes one comparison and emits nothing;
    /// with a <c>KnockbackOnSwing</c> node taken, each enemy the swing reached gets an
    /// <c>EnemyKnockbackIntent</c> in the direction the swing was thrown. The machinery is the
    /// Charge's, unchanged since M1-15 — which is what that struct's own remarks predicted would
    /// happen the day something else pushed an enemy.
    /// </para>
    /// <para>
    /// Nothing here allocates: a span in, a preallocated dedupe buffer, a struct result per id, and
    /// a struct intent per shove.
    /// </para>
    /// </remarks>
    /// <param name="enemyIds">
    /// Who the body found standing in the wedge. Duplicates are ignored, ids it does not recognise
    /// and ids that are already dead are no-ops — a view lagging a frame behind a death is normal
    /// and must not be an error. Borrowed for the duration of the call and never retained.
    /// </param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>.</param>
    /// <param name="enemies">
    /// The run's enemies, for the one thing this needs from them. Passed in rather than held, for
    /// the reason <see cref="FocusAt"/> gives: this class deliberately owns no registry and is
    /// handed the world each time it is asked to think about it.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// More distinct enemies were damaged by one swing than this was built for — see
    /// <see cref="_hitIds"/>, which is unreachable while the capacities agree, and loud rather than
    /// silently un-deduplicated if they ever do not.
    /// </exception>
    public void ResolveConeHits(ReadOnlySpan<int> enemyIds, float now, EnemySystem enemies)
    {
        if (PendingConeRequestId < 0)
        {
            return;
        }

        PendingConeRequestId = -1;

        float damage = Weapon.Damage.Value;

        // Read once for the whole report, like the damage above and for its reason: one swing is
        // one shove, whatever a handler does between two enemies.
        //
        // **Asked here rather than per enemy, and the spelling is the guard** (rule 8, AR §18.3).
        // `> 0f` is false for NaN as well as for everything at or below zero, so a stack nobody can
        // read shoves nobody; infinity is asked about separately because it passes a `> 0` test,
        // and an infinite shove is a teleport rather than a knockback. A base of zero means the
        // common path — every run this build plays — takes one comparison and emits nothing.
        float knockback = SwingKnockback.Value;
        bool shoves = knockback > 0f && !float.IsInfinity(knockback);

        // The swing's facing, sampled when the question was asked. Read before the loop so that
        // every enemy one report names is swept the same way, which is EnemyKnockbackIntent's own
        // rule and what tells a cone shove apart from a blast.
        Vector2 shove = _pendingConeFacingXZ;

        _hitCount = 0;

        for (int i = 0; i < enemyIds.Length; i++)
        {
            int id = enemyIds[i];

            if (AlreadyHit(id))
            {
                continue;
            }

            // `this` is what a blast would be resolved against: killing an enemy can now hurt the
            // player, because an archetype carrying an ExplosionSpec goes off where it died
            // (M2-08 rule 3). A cone hit that kills a Bloater in melee therefore costs the swinger.
            DamageResult result = enemies.ApplyDamage(id, damage, now, this);

            // Recorded only when the hit reached something, which is what bounds the buffer: an
            // id that resolved to nothing costs nothing to process again, and one that was
            // already a corpse is a no-op the second time for the same reason it was the first.
            if (!result.Blocked && !(result.Applied > 0f))
            {
                continue;
            }

            RecordHit(id);

            // **Below RecordHit, so the shove is per enemy the swing actually reached** (rule 5).
            // An id that resolved to nothing or was already a corpse has been skipped above and is
            // not shoved; one the swing killed *is*, for the reason ResolveChargeHits gives — it
            // was hit, the shove is what that looks like, and a corpse sliding a metre while it
            // dissolves is better than one that plants itself the instant it dies.
            if (shoves)
            {
                _intents.EnemyKnockback(new EnemyKnockbackIntent(id, shove, knockback));
            }
        }
    }

    /// <summary>
    /// The body has swept part of a dash: everyone in <paramref name="enemyIds"/> is passed
    /// through, for one Charge's damage and one Charge's shove.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other half of a round trip, and a different shape from
    /// <see cref="ResolveConeHits"/>.</b> A cone is one wedge at one instant, matched by a pending
    /// request and spent by the first answer. A dash is a line swept over 0.22 s that no single
    /// overlap describes, so the body reports it frame by frame and this method is called several
    /// times per dash on purpose. What keeps that safe is not a request id but the two things below:
    /// a window, and a memory.
    /// </para>
    /// <para>
    /// <b>The window is a clock, not a flag</b> — <see cref="_chargeHitWindowUntil"/>, which is the
    /// dash's end plus <see cref="ChargeReportGrace"/>. A report from before anyone dashed and a
    /// report from a dash that finished last second are refused by the same comparison, so there is
    /// no state to get out of step with the skill's own.
    /// </para>
    /// <para>
    /// <b>The memory is per dash, not per report.</b> <see cref="_chargeHitIds"/> is cleared when a
    /// dash starts and holds until the next one, which is what makes CC §5's "20 to everything
    /// passed through" a per-Charge promise: an enemy the body names on ten consecutive frames of
    /// one sweep takes 20, not 200.
    /// </para>
    /// <para>
    /// <b>Aliveness is asked here rather than inferred from the damage</b>, unlike
    /// <see cref="ResolveConeHits"/>, and the difference matters for a skill nobody has written yet.
    /// A Charge always damages, so "did anything land" and "was there anyone there" are the same
    /// question for it — but M5-03's Shroudstep is authored at zero damage with knockback of its
    /// own, and reading the answer off the damage would leave it shoving nothing. The registry is
    /// asked directly, which costs one dictionary lookup per id and never allocates.
    /// </para>
    /// <para>
    /// The knockback goes out even for an enemy this hit killed. It was passed through, the shove
    /// is what that looks like, and a corpse sliding a metre while it dissolves is better than one
    /// that plants itself the instant it dies.
    /// </para>
    /// </remarks>
    /// <param name="enemyIds">
    /// Who the body has swept through so far. Duplicates, ids an earlier frame of this dash already
    /// reported, unknown ids and corpses are all no-ops. Borrowed for the call and never retained.
    /// </param>
    /// <param name="now">Simulated run time, in seconds — <c>RunState.Time</c>.</param>
    /// <param name="enemies">
    /// The run's enemies, for the aliveness check and the damage. Passed in rather than held, for
    /// the reason <see cref="ResolveConeHits"/> gives.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// One dash damaged more distinct enemies than this was built for — unreachable while the
    /// capacities agree, and loud rather than silently un-deduplicated if they ever do not.
    /// </exception>
    public void ResolveChargeHits(ReadOnlySpan<int> enemyIds, float now, EnemySystem enemies)
    {
        // Negated rather than `now > _chargeHitWindowUntil`, so a report stamped with a clock that
        // has gone non-finite is refused rather than believed. The same direction every comparison
        // in this class takes.
        if (!(now <= _chargeHitWindowUntil))
        {
            return;
        }

        // Read once for the whole report, like the cone's damage and for the same reason: one dash
        // is one number, whatever a handler does in between.
        //
        // **This is the line the old comment said would change, and M3-12a is where it did.** The
        // damage now comes off ChargeSkill's Stat, so a node can reach it; the knockback still
        // comes off the spec, deliberately — 4 m is a positioning number rather than a power one,
        // and none of v1's twelve nodes wants it (M3-12a rule 3).
        float damage = Charge.Damage.Value;
        float knockback = _movementSkill.Knockback;

        // The dash's direction, not the direction to each enemy: everything a Charge passes through
        // is swept the same way. See EnemyKnockbackIntent.DirectionXZ.
        Vector2 direction = Charge.Direction;

        for (int i = 0; i < enemyIds.Length; i++)
        {
            int id = enemyIds[i];

            if (AlreadyChargeHit(id))
            {
                continue;
            }

            if (!enemies.Registry.TryGet(id, out EnemyAgent agent) || !agent.IsAlive)
            {
                continue;
            }

            // Recorded before the damage lands, so that nothing a handler does from inside an
            // EnemyDamaged can get the same enemy hit twice by this dash.
            RecordChargeHit(id);

            // `this` for the reason ResolveConeHits passes it: a Charge that kills a Bloater is a
            // Charge that set one off, and the dash's own i-frames are what decide whether the
            // blast reaches the player — PlayerCombat.ApplyDamage owns that question, not the blast.
            enemies.ApplyDamage(id, damage, now, this);

            _intents.EnemyKnockback(new EnemyKnockbackIntent(id, direction, knockback));
        }
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
    /// <param name="lures">
    /// Where a Shroudstep's decoy goes, or null for a run that has none. Passed in rather than
    /// held, for the reason this class is handed the enemies and the snapshot: it owns no view of
    /// the world, and a constructor argument would ripple through every fixture that builds one.
    /// Null means "nowhere to put a decoy" and the blink still happens, which is what every run of
    /// the Oathbound is — see <see cref="TickCharge"/>.
    /// </param>
    public void Tick(
        float dt,
        float now,
        WorldSnapshot snapshot,
        ReadOnlySpan<EnemyAgent> enemies,
        Vector3 bodyFacing,
        LureSystem lures = null)
    {
        // Before the ramp, because the ramp asks it a question. A dash that started this tick has
        // to be in flight by the time "am I moving" is answered, or the tick it begins on would be
        // counted as another tick of standing still.
        TickCharge(dt, now, snapshot.MoveInput, bodyFacing, snapshot.PlayerPosition, lures);

        // Then the ramp, and before DpsOneSecond is read below. It is the only thing in the tick
        // that changes the fire rate, so running it here is what lets the rest of the tick — the
        // finisher bonus, the swing cadence — see this frame's rate rather than last frame's.
        //
        // Two ways to be moving, and the dash is the one that is invisible to the stick: CC §5's
        // 10 m happen with the thumb wherever it likes, including nowhere, and a ramp that survived
        // one would pay out for the dodge it is supposed to be the alternative to. M1-13 left this
        // expression as the seam and this is it being used.
        //
        // Spelled as "not exactly zero" rather than "greater than zero", which is the same
        // direction the old inline clock took and matters for one input: a NaN stick fails the
        // equality and counts as movement, so a broken input resets the ramp instead of quietly
        // ramping forever. See FocusTracker.LevelAt for the other half of the same care.
        Focus.Tick(dt, snapshot.MoveInput.LengthSquared() != 0f || Charge.IsActive);

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
                Targeter.IsCurrentBlocked,
                // The other half of the same question, and the only one the event never carried
                // (M2-12a): a focus is held, and it is not what the gun is on. Guarded against the
                // focused-and-current case above rather than duplicating it, so the two fields are
                // never both set and a view can render them as two states instead of three.
                Targeter.FocusedTargetId >= 0 && Targeter.FocusedTargetId != Targeter.CurrentTargetId
                    ? Targeter.FocusedTargetId
                    : -1));
        }

        Health.Tick(dt, now);
        PublishShieldIfDrifted();

        UpdateBlackboard(count, snapshot.PlayerPosition);
        UpdateFaceDirection(snapshot.PlayerPosition, enemies);

        TickWeapon(dt, now, snapshot.PlayerPosition, bodyFacing, count, enemies);
    }

    /// <summary>
    /// Back to the start of a run: full health, no target, no focus of either kind, a weapon at
    /// rest, a dodge off cooldown, a blank blackboard and no facing.
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

        // After the weapon, and it does take its modifier off where Weapon.Reset deliberately does
        // not: the tracker is the source of that modifier, so it is the one thing entitled to
        // decide it should go. A ramp left on the stat would be +30 % fire rate earned by standing
        // still once, before a stage that has not started yet.
        Focus.Reset();

        // The dash goes back to rest with everything else, and the three fields that track it here
        // go with it. Health.Reset above has already lowered the external flag — a dash interrupted
        // by a reset would otherwise leave the next life invulnerable with nothing holding the flag
        // to lower it — so this is the other half of that: the tracker of the edge, so the next dash
        // is what publishes the next ChargeEnded, and the window, so a report from the dash that
        // was interrupted cannot damage enemies the reset has cleared out from under it.
        Charge.Reset();

        _wasChargeInvulnerable = false;
        _chargeHitWindowUntil = float.NegativeInfinity;
        _chargeHitCount = 0;

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

        // And the other kind of damage frame's leftover, for the same reason as the line above: a
        // shot offered on the last tick of a stage and never taken would be fired into the next one,
        // aimed at a point in an arena that no longer exists.
        PendingShot = null;
    }

    /// <summary>
    /// Rules 1–3 of M1-15: advance the dodge, and act on the two edges it has — the tick it starts
    /// on, and the tick its protection runs out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two edges, not one, because CC §5 protects a dash for longer than it lasts.</b> The
    /// movement ends at <c>Duration</c>; the i-frames end 0.05 s later, and this method is written
    /// around the second of those — that is where the external flag comes down and where
    /// <see cref="ChargeEnded"/> goes out. The first edge is nobody's business here: the motor is
    /// <c>RunSession</c>'s, and it reads <see cref="ChargeSkill.IsActive"/> for itself.
    /// </para>
    /// <para>
    /// <b>The start branch and the end branch are exclusive.</b> A dash starting on the very tick
    /// another's protection lapses would otherwise lower the flag it had just raised. It cannot
    /// happen while the cooldown is twenty times the i-frame window, but the shape costs nothing and
    /// means a shorter cooldown from M3-12 can never open the hole: while a dash is starting, the
    /// only edge that exists is the start.
    /// </para>
    /// <para>
    /// <b>Invulnerability is set here and read from <see cref="Health"/>.</b> The dash does not check
    /// itself when damage arrives — <c>Health.IsInvulnerable</c> is the one answer to "can the player
    /// be hurt", and this is a caller raising a flag it is also responsible for lowering, exactly as
    /// <c>Health.SetExternalInvulnerable</c> asks.
    /// </para>
    /// <para>
    /// <b>And as of M5-03 the start edge has a payload, which is the only thing a second
    /// <see cref="MovementSkillKind"/> changed anywhere</b> (rule 1). A Shroudstep is a
    /// <see cref="ChargeSkill"/> with a corpse behind it: the cooldown, the buffer, the i-frames
    /// and the <see cref="ChargeIntent"/> are the Charge's, unmodified, and the one branch below is
    /// the difference. No <c>IMovementSkill</c> and no second skill class — that would be an
    /// abstraction with one implementation and a second payload, which is the trade
    /// <see cref="Charge"/>'s own remarks refuse.
    /// </para>
    /// </remarks>
    private void TickCharge(
        float dt,
        float now,
        Vector2 stickXZ,
        Vector3 bodyFacing,
        Vector3 playerPosition,
        LureSystem lures)
    {
        // The stick aims it, the body's facing is the fallback when the stick is centred — CC §5,
        // and ChargeSkill's rule rather than this method's. The facing is the one from last tick's
        // turn, for the reason TickWeapon's is: combat runs before the motor.
        bool started = Charge.Tick(dt, now, stickXZ, new Vector2(bodyFacing.X, bodyFacing.Z));
        bool invulnerable = Charge.IsInvulnerable;

        if (started)
        {
            Health.SetExternalInvulnerable(true);

            // Both cleared on the start rather than on the end of the previous dash, so a report
            // that arrives late is refused by a window that has already closed rather than by state
            // some earlier tick had to remember to tidy.
            _chargeHitWindowUntil = now + _movementSkill.Duration + ChargeReportGrace;
            _chargeHitCount = 0;

            // The instruction and the announcement, in that order and by different doors: the body
            // is told where to go, everyone else is told that a dodge happened. ADR-0003's split,
            // the same one a swing makes.
            _intents.Charge(new ChargeIntent(
                Charge.Direction,
                _movementSkill.Distance,
                _movementSkill.Duration));

            _events.Publish(new ChargeStarted(Charge.Direction));

            // **And the corpse, where the blink *left*** (rule 6). CH §3.2 says the Shroudstep
            // "leaves a corpse-decoy" — a thing left behind — so it is dropped from the player's
            // position as of this tick rather than from the destination. Dropped at the
            // destination it would stand on top of the player and taunt the whole arena straight
            // at them, which inverts the mechanic.
            //
            // Below the ChargeStarted rather than above it, so a listener handling the dodge has
            // not yet been told about a decoy the dodge is what produced.
            //
            // Guarded on the kind and not on the duration: the spec has already refused a
            // Shroudstep with no decoy and a Charge with one (MovementSkillSpec, rule 5), so this
            // is the one branch and there is no second opinion about what the number means. A null
            // lure system is a run with nowhere to put one and the blink still happens, exactly as
            // a refused drop does.
            if (_movementSkill.Kind == MovementSkillKind.Shroudstep && lures is not null)
            {
                // The return value is deliberately not read. Drop answers NoLure at capacity and
                // the player believes they blinked, which is ProjectileSystem.Fire's reading
                // applied to the one dropper that would otherwise need to know the lure system's
                // capacity (rule 4).
                lures.Drop(playerPosition, now, _movementSkill.DecoyDuration);
            }
        }
        else if (_wasChargeInvulnerable && !invulnerable)
        {
            Health.SetExternalInvulnerable(false);

            _events.Publish(new ChargeEnded());
        }

        _wasChargeInvulnerable = invulnerable;
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
    /// <para>
    /// <b>And as of M5-01 the damage frame has two shapes, which is the only thing a second
    /// <see cref="WeaponKind"/> changed anywhere.</b> A cone asks the body a question it alone can
    /// answer; a bolt asks nothing, because an arrival is a point and a moment core computes itself
    /// (<see cref="ProjectileSystem"/>). The timing above is shared exactly — see
    /// <see cref="Weapon"/>, which is untouched and knows neither kind exists.
    /// </para>
    /// </remarks>
    private void TickWeapon(
        float dt,
        float now,
        Vector3 playerPosition,
        Vector3 bodyFacing,
        int count,
        ReadOnlySpan<EnemyAgent> enemies)
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

        switch (_weaponSpec.Kind)
        {
            case WeaponKind.Cone:
                ThrowCone(playerPosition, facingXZ);
                return;

            case WeaponKind.Projectile:
                OfferShot(playerPosition, enemies);
                return;

            default:
                // Unreachable: WeaponSpec refuses an undefined kind at the door, which is what makes
                // this line a statement about that guard rather than a branch anybody can take. Loud
                // rather than silent, because the silent version is a class whose basic attack does
                // nothing at all and never says so.
                throw new InvalidOperationException(
                    $"The weapon's kind is {_weaponSpec.Kind}, which PlayerCombat cannot throw. "
                        + "WeaponSpec is meant to have refused it.");
        }
    }

    /// <summary>
    /// A <see cref="WeaponKind.Cone"/> damage frame: the wedge leaves as a question and core
    /// remembers what it asked.
    /// </summary>
    private void ThrowCone(Vector3 playerPosition, Vector2 facingXZ)
    {
        _lastConeRequestId++;
        PendingConeRequestId = _lastConeRequestId;

        // The swing's geometry is remembered with its request id, because the shove
        // ResolveConeHits owes is measured in the direction the swing was thrown rather than in
        // whatever direction the player has turned by the time the body answers (rule 7).
        _pendingConeFacingXZ = facingXZ;

        // Sampled here, at the moment the swing is thrown, and never cached (M3-12a rules 2 and 6):
        // a node taken mid-stage widens the very next swing rather than the one after it, which is
        // the whole of what makes a pick feel like it did something.
        _intents.ConeHit(new ConeHitIntent(
            _lastConeRequestId,
            playerPosition,
            facingXZ,
            ConeRange(Weapon.Range.Value),
            ConeAngle(Weapon.ConeAngleDeg.Value)));
    }

    /// <summary>
    /// A <see cref="WeaponKind.Projectile"/> damage frame: a shot aimed at where the target will be,
    /// left on <see cref="PendingShot"/> for the run to put in the air (M5-01 rules 6 and 8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No target, no shot — and a target that died between the swing start and this moment is no
    /// target.</b> <c>Weapon.Tick</c> already refuses to <em>start</em> a swing without something in
    /// range, so the lead is never solved against an enemy that does not exist; what this method adds
    /// is the other end of CC §4.2's "not a commitment". A cone resolves against whoever is standing
    /// in the wedge, so a swing thrown at a Husk that died mid-windup still kills the two beside it —
    /// but a bolt is aimed at a point, and one aimed at where a corpse used to be would land on nobody
    /// and look like a miss the player did not make. It is no shot instead.
    /// </para>
    /// <para>
    /// <b>The velocity is the agent's live one, not a heading.</b> <c>EnemyAgent.Velocity</c> is what
    /// the body reported the enemy actually doing this frame (M1-06), which is the number CC §3.7's
    /// solve is written against: an intended direction would lead a Husk that is standing against a
    /// wall as though it were still walking.
    /// </para>
    /// <para>
    /// <b>The damage is the live stat and is sampled here</b>, at the moment the shot is decided, for
    /// the reason the cone's is (M3-12a rules 2 and 6): a node taken mid-stage is in the very next
    /// bolt. The speed and the radius come off the authored spec, which is where they stay until
    /// something asks to move them — see <see cref="WeaponSpec.ShotSpeed"/>.
    /// </para>
    /// <para>
    /// <b><c>SourceId</c> is 0, which means "nobody", and that is the honest answer.</b> That field is
    /// an <em>enemy</em> id — a record of which agent fired, never resolved against the registry — and
    /// the player is not in the registry. A view wanting where the player's bolt came from wants
    /// <c>Projectile.Origin</c>, which is exactly what it is handed.
    /// </para>
    /// <para>
    /// A linear scan for the target agent, for the reason <see cref="UpdateFaceDirection"/> gives: the
    /// span is at most the enemy cap and a dictionary would allocate on a per-frame path. The
    /// candidate buffer cannot answer it — <see cref="TargetCandidate"/> deliberately carries neither
    /// a position nor a velocity.
    /// </para>
    /// </remarks>
    private void OfferShot(Vector3 playerPosition, ReadOnlySpan<EnemyAgent> enemies)
    {
        int id = Targeter.CurrentTargetId;

        if (id < 0)
        {
            return;
        }

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyAgent agent = enemies[i];

            if (agent.Id != id)
            {
                continue;
            }

            // Registered is not breathing. The span carries corpses on purpose (see
            // BuildCandidates), and the one that the targeter has not moved off yet is precisely the
            // case this method is here to refuse.
            if (!agent.IsAlive)
            {
                return;
            }

            PendingShot = new Projectile(
                _characterId,
                sourceId: 0,
                playerPosition,
                ProjectileLead.Solve(
                    playerPosition, agent.Position, agent.Velocity, _weaponSpec.ShotSpeed),
                _weaponSpec.ShotSpeed,
                _weaponSpec.ShotRadius,
                ShotDamage(Weapon.Damage.Value),
                ShotSide.AtEnemies);

            return;
        }

        // Unreachable while the targeter is fed from this same span, and cheap to be right about: a
        // target with no agent behind it is nothing to shoot at.
    }

    /// <summary>
    /// A live <see cref="Weapon.Damage"/> as a number a shot can actually carry: not negative, with
    /// an unreadable value answered by zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ConeAngle"/> and <see cref="ConeRange"/>'s job for the one number a bolt has that a
    /// cone does not have to clamp, and it is here because <see cref="Projectile"/>'s door
    /// <em>throws</em> where a <c>ConeHitIntent</c>'s does not. <see cref="Stat"/> clamps nothing
    /// (ADR-0008), so a stack driving weapon damage negative or non-finite is reachable — a −200 %
    /// Pact, an overflow inside the stack — and reaching that door with one would end the run from
    /// inside a damage frame, on a phone, mid-stage. Zero says "this shot deals nothing" and says it
    /// reversibly: take the modifier off and the next bolt is ordinary, which is the same bargain
    /// <c>Weapon.TryGetInterval</c> strikes with a silenced fire rate.
    /// </para>
    /// <para>
    /// The shot still flies and still publishes its impact, which is the difference between this and
    /// refusing to fire: a bolt that does nothing is visible and diagnosable, where a weapon that
    /// silently stopped shooting looks like a bug in the targeting.
    /// </para>
    /// <para>
    /// The other three numbers the shot carries need no equivalent. The speed and the radius are
    /// authored and <see cref="WeaponSpec"/> has already refused a non-finite one; the origin is the
    /// position the snapshot brought in and the aim point is <see cref="ProjectileLead.Solve"/>'s,
    /// which is documented never to manufacture a NaN from finite inputs.
    /// </para>
    /// </remarks>
    private static float ShotDamage(float damage)
    {
        // The negated comparison, so NaN lands on zero rather than falling through — the spelling
        // ConeAngle uses, for its reason.
        return damage > 0f && !float.IsInfinity(damage) ? damage : 0f;
    }

    /// <summary>
    /// A live <see cref="Weapon.ConeAngleDeg"/> as an angle a wedge can actually be built from:
    /// clamped into <c>[0, 360]</c>, with a negative or unreadable value answered by zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M3-12a rule 7, and the layer <see cref="Stat"/> leaves this to (ADR-0008): <c>WeaponSpec</c>
    /// refuses an *authored* angle outside <c>(0, 360]</c>, but a modifier stack can put the live
    /// value anywhere, and the swing is where that finally has to mean something.
    /// </para>
    /// <para>
    /// <b>NaN is refused rather than widened, which is the opposite of what it may look like.</b>
    /// It is *less* meaningful than a negative number, not more — answering it with 360 would draw
    /// the widest wedge in the game for a value nobody can read, quietly turning the Censer
    /// omnidirectional. Zero says "this swing connects with nothing" and says it reversibly: take
    /// the modifier off and the next swing is ordinary, which is exactly the bargain
    /// <c>Weapon.TryGetInterval</c> strikes with a silenced fire rate. It falls out of the negated
    /// positive below rather than needing a test of its own.
    /// </para>
    /// <para>
    /// <b>Positive infinity is the one non-finite value that is not refused</b>, and the asymmetry
    /// with <see cref="ConeRange"/> is deliberate: an infinite angle has a meaning — "as wide as
    /// there is" — and there is a widest wedge to clamp it to, where an infinite *reach* has no
    /// maximum to land on and is refused instead. Both are reachable only by arithmetic overflow
    /// inside the stack, since <c>Modifier</c> and <c>Stat.Base</c> both refuse a non-finite input
    /// at the door.
    /// </para>
    /// <para>
    /// The refusal is a zero-angle intent rather than no intent at all, matching
    /// <see cref="ConeRange"/>. The swing has already started and been announced by the time this
    /// runs; suppressing the intent would leave <see cref="PendingConeRequestId"/> waiting on a
    /// report that can never arrive, where a wedge that hits nothing comes back empty and clears
    /// it on the ordinary path.
    /// </para>
    /// </remarks>
    private static float ConeAngle(float degrees)
    {
        // The negated positive, so NaN lands on zero rather than falling through to the clamp.
        if (!(degrees > 0f))
        {
            return 0f;
        }

        return degrees < MaxConeAngleDeg ? degrees : MaxConeAngleDeg;
    }

    /// <summary>
    /// A live <see cref="Weapon.Range"/> as a reach a wedge can be built from: non-negative, with
    /// an unreadable value answered by zero.
    /// </summary>
    /// <remarks>
    /// M3-12a rule 7, and <see cref="ConeAngle"/>'s reasoning for its spelling and its answer. A
    /// negative reach is a wedge with no depth, which hits nothing — the honest reading of a stack
    /// that drove a weapon's reach below zero, and reversible in one frame. Unlike an angle there
    /// is no maximum reach to clamp an infinite one to, so it is refused with the rest; see
    /// <see cref="ConeAngle"/> on why the two differ.
    /// </remarks>
    private static float ConeRange(float metres)
    {
        return metres > 0f && !float.IsInfinity(metres) ? metres : 0f;
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
                // The same clamped reach the intent is built from, so "worth swinging at" and
                // "inside the wedge" can never disagree — a range driven negative stops the swing
                // rather than throwing one that reaches nothing. Sampled here rather than cached,
                // which is what makes a longer weapon acquire sooner on the very next tick
                // (M3-12a rules 2 and 6).
                return candidate.Distance <= ConeRange(Weapon.Range.Value);
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
    /// <remarks>
    /// It takes neither the step nor the snapshot any more. Both were here for the stationary
    /// clock, which M1-13 moved to <see cref="FocusTracker"/> — the object that has to know what
    /// counts as movement — leaving this method to copy the answer rather than compute a second
    /// one. See <see cref="CombatBlackboard.StationaryTime"/>.
    /// </remarks>
    /// <param name="count">How many candidates the gathering pass filled.</param>
    /// <param name="playerPosition">
    /// Where the body reported the player this tick. Handed in rather than recovered, for the
    /// reason the stationary clock is copied rather than recomputed: this method writes the
    /// blackboard and does not decide anything on it. See
    /// <see cref="CombatBlackboard.PlayerPosition"/> for who reads it.
    /// </param>
    private void UpdateBlackboard(int count, Vector3 playerPosition)
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

        // The ramp's two numbers, mirrored rather than recomputed. The tracker was advanced at the
        // top of this tick, so both are this frame's.
        Blackboard.FocusRampLevel = Focus.Level;
        Blackboard.StationaryTime = Focus.StationaryTime;

        // Written down, not decided (M3-11b rule 2): the same position the snapshot brought in and
        // RunState holds, copied here so that a cast reads this frame's feet rather than last
        // frame's. Nothing in TriggerField compares against it.
        Blackboard.PlayerPosition = playerPosition;

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

    /// <summary>Whether this report has already spent its swing on <paramref name="id"/>.</summary>
    private bool AlreadyHit(int id)
    {
        for (int i = 0; i < _hitCount; i++)
        {
            if (_hitIds[i] == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Remembers that <paramref name="id"/> has taken this swing's damage.</summary>
    private void RecordHit(int id)
    {
        if (_hitCount >= _hitIds.Length)
        {
            throw new InvalidOperationException(
                $"PlayerCombat was built for {_hitIds.Length} enemies but one swing damaged more "
                    + "than that. The dedupe buffer, the enemy registry and the world snapshot "
                    + "must all be built with the same capacity.");
        }

        _hitIds[_hitCount] = id;
        _hitCount++;
    }

    /// <summary>Whether the dash in progress has already spent itself on <paramref name="id"/>.</summary>
    private bool AlreadyChargeHit(int id)
    {
        for (int i = 0; i < _chargeHitCount; i++)
        {
            if (_chargeHitIds[i] == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Remembers that <paramref name="id"/> has taken this dash's damage.</summary>
    private void RecordChargeHit(int id)
    {
        if (_chargeHitCount >= _chargeHitIds.Length)
        {
            throw new InvalidOperationException(
                $"PlayerCombat was built for {_chargeHitIds.Length} enemies but one Charge passed "
                    + "through more than that. The dedupe buffer, the enemy registry and the world "
                    + "snapshot must all be built with the same capacity.");
        }

        _chargeHitIds[_chargeHitCount] = id;
        _chargeHitCount++;
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

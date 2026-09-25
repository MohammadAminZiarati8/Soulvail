using System;
using System.Collections.Generic;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEngine;
using VContainer.Unity;

namespace Soulvail.Game.Sandbox;

/// <summary>
/// The Ranger sandbox's frame: the stick in, core's motor, targeter and weapon deciding, and the
/// body, the bow and the arrows showing what they decided. <c>RunTicker</c>'s order with one
/// character and no run. RS-02a.
/// </summary>
/// <remarks>
/// <para>
/// <b>Core decides every number here; this class only wires the three together.</b>
/// <see cref="PlayerMotor"/> turns the stick into a velocity and a facing, <see cref="Targeter"/>
/// picks the dummy to face at 10 Hz with CC §3.3's hysteresis, and <see cref="Weapon"/> keeps the
/// bow's cadence and says when an arrow leaves. The rest is what <c>RunSession</c> does between
/// them in a run: face the target, fire when it is in range, time the flight as XZ distance over
/// shot speed (AR §18.4). A sandbox has no <c>RunState</c> to put that in. It stays here, one
/// character wide, and a run never sees it.
/// </para>
/// <para>
/// <b>It publishes the facts a run publishes.</b> <see cref="TargetChanged"/>,
/// <see cref="HoldFireChanged"/>, <see cref="PlayerAttacked"/>, <see cref="ProjectileFired"/> and
/// <see cref="ProjectileImpacted"/> go into the scope's hub. <c>RangerAnimatorView</c> and the
/// game's own <see cref="ProjectileViews"/> draw from them exactly as they would from core.
/// </para>
/// <para>
/// <b>The Ranger shoots standing still</b>, by the owner's ruling of 2026-09-25. While it runs it
/// faces where it is going, holds its fire, and drops a shot being drawn with no arrow. Stopped, it
/// turns to its target and shoots. Shooting on the move is to be a skill, so the sandbox carries it
/// as a switch, <c>shootWhileMoving</c>. This is the Ranger's rule and not CC §4.2's: attacking
/// still never slows the Ranger, but moving stops it attacking. Core has had the rule since RS-03a
/// (<c>PlayerCombat.IsHoldingFire</c>); this loop keeps a copy one character wide, and says so
/// with the same fact.
/// </para>
/// <para>
/// <b>A dummy cannot die.</b> It stands at 1 HP, vulnerable, with priority 1, and an arrow that
/// arrives plays its flinch. Damage, death and respawn are a run's.
/// </para>
/// </remarks>
public sealed class RangerSandboxLoop : IStartable, ITickable, IDisposable
{
    /// <summary>The trigger on a dummy's controller that plays its flinch.</summary>
    public const string HitTrigger = "Hit";

    /// <summary>A dummy's archetype priority (CC §3.2): all equal, so distance decides.</summary>
    private const int DummyPriority = 1;

    /// <summary>A dummy's HP, as the targeter sees it: alive, and never killed.</summary>
    private const float DummyHp = 1f;

    /// <summary>
    /// Below this speed the body counts as stopped, in m/s. <c>PlayerMotor</c> uses the same number
    /// for the point below which a velocity's direction is noise rather than intent.
    /// </summary>
    private const float StillSpeed = 0.05f;

    /// <summary>
    /// Who <see cref="ProjectileFired.SourceId"/> says fired: nobody, which is the player — the
    /// field names an enemy, and 0 is none.
    /// </summary>
    private const int PlayerSource = 0;

    private readonly InputAdapter _input;
    private readonly DomainEventHub _hub;
    private readonly PlayerView _player;
    private readonly ProjectileViews _arrows;
    private readonly Transform _muzzle;
    private readonly Animator[] _dummies;
    private readonly float _aimHeight;
    private readonly float _arrowSpeed;
    private readonly float _acquireRange;
    private readonly bool _shootWhileMoving;

    private readonly PlayerMotor _motor;
    private readonly Targeter _targeter;
    private readonly Weapon _weapon;

    /// <summary>
    /// The candidates handed to the targeter, one slot per dummy, filled in place each step so
    /// the frame allocates nothing (AR §14).
    /// </summary>
    private readonly TargetCandidate[] _candidates;

    /// <summary>Arrows in the air, each landing at a moment on <see cref="_now"/>.</summary>
    private readonly List<Flight> _flights;

    /// <summary>
    /// Which shooter <see cref="ProjectileFired.SpecId"/> names. Not authored content: the Ranger
    /// has no <c>CharacterDefinition</c>, and its id is the owner's to choose (RS-01c).
    /// </summary>
    private readonly ContentId _shooterId = new ContentId("sandbox.ranger");

    private readonly int _hitId = Animator.StringToHash(HitTrigger);

    /// <summary>Simulated seconds since the first step: the sum of the clamped steps.</summary>
    private float _now;

    /// <summary>The dummy the shot in progress was started at, as a candidate id, or −1.</summary>
    private int _shotTarget = -1;

    /// <summary>Arrow ids, issued from 1 as <c>ProjectileSystem</c> issues them.</summary>
    private int _nextArrowId = 1;

    /// <summary>What the last <see cref="TargetChanged"/> said was faced, or −1.</summary>
    private int _facedId = -1;

    /// <summary>Whether the last <see cref="TargetChanged"/> said it was blocked.</summary>
    private bool _facedBlocked;

    /// <summary>What the last <see cref="HoldFireChanged"/> said. A run starts not holding, as core's does.</summary>
    private bool _holding;

    /// <param name="input">The one reader of the Input System (M0-14).</param>
    /// <param name="hub">Where the frame's facts are published.</param>
    /// <param name="player">The body: moved and turned by the motor's intent.</param>
    /// <param name="arrows">The game's projectile census, drawing each arrow from the facts.</param>
    /// <param name="movement">How the Ranger moves — CC §2.4.</param>
    /// <param name="targeting">How it picks what to face — CC §3.</param>
    /// <param name="weapon">
    /// The bow: a <see cref="WeaponKind.Projectile"/> weapon, whose damage frame is the moment
    /// the arrow leaves and whose shot speed times its flight.
    /// </param>
    /// <param name="muzzle">Where an arrow leaves from: the bow in the Ranger's hand.</param>
    /// <param name="dummies">The targets. Each one's controller has a <see cref="HitTrigger"/>.</param>
    /// <param name="aimHeight">How far above a dummy's feet an arrow is aimed, in metres.</param>
    /// <param name="shootWhileMoving">
    /// The running shot: face the target and shoot while the stick moves the body. Off, the Ranger
    /// shoots only standing still.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="weapon"/> is not a projectile weapon.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="aimHeight"/> is negative or not finite.</exception>
    public RangerSandboxLoop(
        InputAdapter input,
        DomainEventHub hub,
        PlayerView player,
        ProjectileViews arrows,
        MovementSpec movement,
        TargetingSpec targeting,
        WeaponSpec weapon,
        Transform muzzle,
        Animator[] dummies,
        float aimHeight,
        bool shootWhileMoving = false)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _arrows = arrows ?? throw new ArgumentNullException(nameof(arrows));
        _dummies = dummies ?? throw new ArgumentNullException(nameof(dummies));

        // Unity's ==: the scope supplies these from serialized fields, and an unassigned or
        // destroyed object is a live reference that only compares equal to null through the
        // engine's operator.
        _player = player == null ? throw new ArgumentNullException(nameof(player)) : player;
        _muzzle = muzzle == null ? throw new ArgumentNullException(nameof(muzzle)) : muzzle;

        if (movement is null)
        {
            throw new ArgumentNullException(nameof(movement));
        }

        if (targeting is null)
        {
            throw new ArgumentNullException(nameof(targeting));
        }

        if (weapon is null)
        {
            throw new ArgumentNullException(nameof(weapon));
        }

        if (weapon.Kind != WeaponKind.Projectile)
        {
            throw new ArgumentException(
                $"The Ranger's bow must be a {WeaponKind.Projectile} weapon; this one is a " +
                $"{weapon.Kind}. A cone has no shot speed, so no arrow could be timed.",
                nameof(weapon));
        }

        if (!(aimHeight >= 0f) || float.IsInfinity(aimHeight))
        {
            throw new ArgumentOutOfRangeException(
                nameof(aimHeight),
                aimHeight,
                "aimHeight must be a finite number of metres, zero or more.");
        }

        _aimHeight = aimHeight;
        _arrowSpeed = weapon.ShotSpeed;
        _acquireRange = targeting.AcquireRange;
        _shootWhileMoving = shootWhileMoving;

        // +Z, as a run begins: the camera is behind the character and nothing is in reach yet.
        _motor = new PlayerMotor(movement, System.Numerics.Vector3.UnitZ);
        _targeter = new Targeter(new TargetScorer(targeting), targeting);
        _weapon = new Weapon(weapon);

        _candidates = new TargetCandidate[_dummies.Length];
        _flights = new List<Flight>(8);
    }

    /// <summary>
    /// The dummy the targeter has chosen, as a candidate id — its index in the list plus one — or −1.
    /// It is chosen while the Ranger runs as well, and faced only once it stops.
    /// </summary>
    public int CurrentTargetId => _targeter.CurrentTargetId;

    /// <summary>Arrows in the air.</summary>
    public int ArrowsInFlight => _flights.Count;

    /// <summary>Starts reading the stick.</summary>
    public void Start()
    {
        _input.Enable();
    }

    /// <summary>One frame, on the frame's own delta.</summary>
    public void Tick()
    {
        Step(Time.deltaTime);
    }

    /// <summary>
    /// One frame: sense the dummies, decide the target, move the body, advance the bow, land
    /// the arrows, and draw them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The step is clamped at <see cref="SnapshotBuilder.MaxDt"/>, as every run step is, so a
    /// hitch runs the sandbox in slow motion rather than teleporting the Ranger through a dummy.
    /// Public so an EditMode fixture can step it; <see cref="Tick"/> is the only caller in play.
    /// </para>
    /// <para>
    /// <b>The target is decided before the body moves</b>, so the motor turns toward this step's
    /// target rather than the last one's (<c>RunSession.TickBody</c>). <b>The bow is advanced
    /// after</b>, so an arrow leaves from where the Ranger stands this frame. Its flight is drawn
    /// last, on the same clamped step core would time it with (M2-09 rule 3).
    /// </para>
    /// </remarks>
    /// <param name="dt">Seconds since the last step. Zero, negative or non-finite does nothing.</param>
    public void Step(float dt)
    {
        if (!(dt > 0f) || float.IsInfinity(dt))
        {
            return;
        }

        dt = Mathf.Min(dt, SnapshotBuilder.MaxDt);
        _now += dt;

        Vector3 self = _player.Position;
        int count = Gather(self);

        _targeter.Tick(dt, new ReadOnlySpan<TargetCandidate>(_candidates, 0, count), _weapon.DpsOneSecond);

        // Straight through, as SnapshotBuilder maps it: the camera's yaw is 0, so stick X is world
        // X and stick Y is world Z.
        Vector2 stick = _input.Move;
        bool steering = stick.sqrMagnitude > 0f;

        int target = _targeter.CurrentTargetId;
        float distance = float.PositiveInfinity;
        System.Numerics.Vector3? face = null;

        if (TryGetDummy(target, out Animator dummy))
        {
            Vector3 toward = dummy.transform.position - self;

            toward.y = 0f;
            distance = toward.magnitude;

            // A running Ranger faces where it runs. The turn to the target begins the moment the
            // stick is let go, while the body is still slowing, so it is facing by the time it stops.
            if (_shootWhileMoving || !steering)
            {
                face = toward.ToNum();
            }
        }

        _motor.Tick(dt, stick.ToNum(), face);
        _player.Apply(new PlayerMoveIntent(_motor.Velocity, _motor.Facing), dt);

        // Engaged: standing still, or allowed to shoot on the move. Read after the move, so the
        // first frame of a push already counts as running.
        bool engaged = _shootWhileMoving || (!steering && _motor.Velocity.Length() <= StillSpeed);

        Face(target, _targeter.IsCurrentBlocked);
        Hold(!engaged);

        // A shot being drawn when the Ranger starts to run is dropped, so no arrow leaves on the
        // move. Resetting also means the next shot starts the moment it stops, not a cadence later.
        if (!engaged && _weapon.IsSwinging)
        {
            _weapon.Reset();
        }

        bool inRange = engaged && target >= 0 && !_targeter.IsCurrentBlocked && distance <= _weapon.Range.Value;
        WeaponTick shot = _weapon.Tick(dt, _now, inRange);

        if (shot.SwingStarted)
        {
            _shotTarget = target;
            _hub.Publish(new PlayerAttacked(new System.Numerics.Vector2(_motor.Facing.X, _motor.Facing.Z)));
        }

        if (shot.DamageFrame)
        {
            Loose();
        }

        Land();

        _arrows.Step(dt);
    }

    /// <summary>
    /// Stops reading the stick, and lands every arrow still in the air so the census returns
    /// each body to its pool.
    /// </summary>
    public void Dispose()
    {
        _input.Disable();

        for (int i = 0; i < _flights.Count; i++)
        {
            _hub.Publish(new ProjectileImpacted(_flights[i].Id, _flights[i].Target, false));
        }

        _flights.Clear();
    }

    /// <summary>
    /// Writes one candidate per dummy inside the acquire range and returns how many there are.
    /// </summary>
    /// <remarks>
    /// <b>The range is the caller's filter, not the targeter's</b> — CC §3.1 step 1, gather
    /// within <c>acquireRange</c>. <see cref="Targeter"/> reads a non-empty span in which nothing
    /// scores as CC §3.6's "something is there but none of it can be hurt", and holds facing on
    /// the nearest, blocked. Handed every dummy on the field, it turned the Ranger toward one
    /// 14 m away with nothing in reach — the first capture at RS-02a.
    /// </remarks>
    private int Gather(Vector3 self)
    {
        int count = 0;

        for (int i = 0; i < _dummies.Length; i++)
        {
            Animator dummy = _dummies[i];

            if (dummy == null)
            {
                continue;
            }

            Vector3 toward = dummy.transform.position - self;

            toward.y = 0f;

            float distance = toward.magnitude;

            // Negated, so a NaN distance is left out rather than admitted (AR §18.3).
            if (!(distance <= _acquireRange))
            {
                continue;
            }

            _candidates[count++] = new TargetCandidate(
                i + 1,
                distance,
                DummyPriority,
                false,
                true,
                DummyHp);
        }

        return count;
    }

    /// <summary>
    /// Publishes <see cref="TargetChanged"/> when the targeter's choice changes, running or not.
    /// </summary>
    /// <remarks>
    /// Until RS-03d this said −1 while the Ranger ran, which is the fact's meaning for <em>nothing
    /// to face</em> and not for <em>not shooting</em>. A run keeps naming the target, and the
    /// reticle keeps showing what the Ranger will shoot when it stops; the bow comes down on
    /// <see cref="Hold"/> instead (RS-03d rule 5).
    /// </remarks>
    private void Face(int id, bool blocked)
    {
        if (id == _facedId && blocked == _facedBlocked)
        {
            return;
        }

        _facedId = id;
        _facedBlocked = blocked;
        _hub.Publish(new TargetChanged(id, false, blocked, -1));
    }

    /// <summary>
    /// Publishes <see cref="HoldFireChanged"/> when the Ranger starts or stops holding its fire, as
    /// <c>PlayerCombat.Tick</c> does. A loop with the running shot on never holds, so it never says so.
    /// </summary>
    private void Hold(bool holding)
    {
        if (holding == _holding)
        {
            return;
        }

        _holding = holding;
        _hub.Publish(new HoldFireChanged(holding));
    }

    /// <summary>
    /// The arrow leaves: from the bow, at the dummy the shot was started at, over XZ distance
    /// divided by the shot speed.
    /// </summary>
    /// <remarks>
    /// The shot's own target, not whatever is faced now. The damage frame belongs to the swing
    /// (<see cref="Weapon"/>), so an arrow drawn at one dummy is loosed at it.
    /// </remarks>
    private void Loose()
    {
        if (!TryGetDummy(_shotTarget, out Animator dummy))
        {
            return;
        }

        Vector3 origin = _muzzle.position;
        Vector3 aim = dummy.transform.position + (Vector3.up * _aimHeight);

        var run = new Vector2(aim.x - origin.x, aim.z - origin.z);
        float flight = run.magnitude / _arrowSpeed;

        int id = _nextArrowId++;
        System.Numerics.Vector3 target = aim.ToNum();

        _hub.Publish(new ProjectileFired(id, _shooterId, PlayerSource, origin.ToNum(), target, flight));
        _flights.Add(new Flight(id, _shotTarget, _now + flight, target));
    }

    /// <summary>Every arrow whose moment has come lands: the census drops it and the dummy flinches.</summary>
    private void Land()
    {
        for (int i = _flights.Count - 1; i >= 0; i--)
        {
            Flight flight = _flights[i];

            if (flight.ArrivesAt > _now)
            {
                continue;
            }

            _flights.RemoveAt(i);
            _hub.Publish(new ProjectileImpacted(flight.Id, flight.Target, true));

            if (TryGetDummy(flight.Dummy, out Animator dummy))
            {
                dummy.SetTrigger(_hitId);
            }
        }
    }

    private bool TryGetDummy(int candidateId, out Animator dummy)
    {
        int index = candidateId - 1;

        dummy = index >= 0 && index < _dummies.Length ? _dummies[index] : null;

        return dummy != null;
    }

    /// <summary>One arrow in the air: its id, the dummy it was loosed at, and when it lands.</summary>
    private readonly struct Flight
    {
        public readonly int Id;
        public readonly int Dummy;
        public readonly float ArrivesAt;
        public readonly System.Numerics.Vector3 Target;

        public Flight(int id, int dummy, float arrivesAt, System.Numerics.Vector3 target)
        {
            Id = id;
            Dummy = dummy;
            ArrivesAt = arrivesAt;
            Target = target;
        }
    }
}

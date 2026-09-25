using System;
using Soulvail.Core.Content;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and RangerShowcase.unity's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Sandbox
{
    /// <summary>
    /// The composition root of <c>RangerShowcase.unity</c>: the game's own input, camera, body and
    /// arrow census around one Ranger and a field of dummies, with no run. RS-02a.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A child of <c>BootScope</c>, like every scope in the project.</b>
    /// <c>VContainerSettings</c> names it the root, so pressing Play here boots it exactly as
    /// pressing Play in <c>Run.unity</c> does. <c>BootFlow</c> sets the frame rate and time scale
    /// and leaves the scene alone, because it only navigates out of <c>Boot</c>.
    /// </para>
    /// <para>
    /// <b>The bow's numbers are placeholders, not the Ranger's kit.</b> The kit — damage, fire
    /// rate, range, what it is for — is RS-03's and the owner's. One shot a second is the rate at
    /// which <c>AC_Ranger</c>'s draw and release play near their authored speed.
    /// </para>
    /// <para>
    /// <b>The Ranger shoots standing still.</b> <c>Shoot While Moving</c> previews the running shot
    /// the owner wants as a skill. It is a switch here because a skill needs the kit RS-03 builds.
    /// </para>
    /// </remarks>
    public sealed class RangerSandboxScope : LifetimeScope
    {
        [Header("Scene")]
        [Tooltip("The Ranger's body: the root with the CharacterController. Required.")]
        [SerializeField] private PlayerView _player;

        [Tooltip("The Ranger's animator view, on its body: the Bodies/Ranger nested under the " +
                 "Ranger object (RS-02b). Required: it is injected here.")]
        [SerializeField] private RangerAnimatorView _animatorView;

        [Tooltip("Where an arrow leaves from: the bow in the Ranger's hand. Required.")]
        [SerializeField] private Transform _muzzle;

        [Tooltip("The arrow body, flown by the game's projectile census. Prefabs/Projectiles/Arrow.prefab.")]
        [SerializeField] private ProjectileView _arrowPrefab;

        [Tooltip("The targets. Each Animator's controller needs a 'Hit' trigger.")]
        [SerializeField] private Animator[] _dummies = Array.Empty<Animator>();

        [Header("Movement — CC §2.4 and §2.5")]
        [Min(0.1f)]
        [SerializeField] private float _moveSpeed = 3f;

        [Min(0.001f)]
        [SerializeField] private float _accelTime = 0.06f;

        [Min(0.001f)]
        [SerializeField] private float _decelTime = 0.08f;

        [Min(1f)]
        [SerializeField] private float _turnSpeedDeg = 720f;

        [Header("Bow — placeholders until RS-03")]
        [Tooltip("Shots per second. At 1, AC_Ranger's draw and release play at 1.2× their authored speed.")]
        [Min(0.1f)]
        [SerializeField] private float _shotsPerSecond = 1f;

        [Tooltip("How far the bow reaches, in metres.")]
        [Min(0.5f)]
        [SerializeField] private float _range = 10f;

        [Tooltip("How far away a dummy is faced and the bow raised, in metres. At least the range.")]
        [Min(0.5f)]
        [SerializeField] private float _acquireRange = 12f;

        [Tooltip("Where in a shot the arrow leaves, as a fraction of the interval. 0.72 is just " +
                 "past full draw at one shot a second.")]
        [Range(0f, 0.99f)]
        [SerializeField] private float _releaseFraction = 0.72f;

        [Tooltip("Arrow speed along the ground, in m/s.")]
        [Min(1f)]
        [SerializeField] private float _arrowSpeed = 30f;

        [Tooltip("How high above a dummy's feet an arrow is aimed, in metres.")]
        [Min(0f)]
        [SerializeField] private float _aimHeight = 0.9f;

        [Tooltip("The running shot. Off: the Ranger shoots only standing still, and running lowers " +
                 "the bow (the owner's ruling). On: it faces its target and shoots on the move, " +
                 "strafing, the way the skill it is to become would. Read when Play starts.")]
        [SerializeField] private bool _shootWhileMoving;

        /// <summary>
        /// Prewarmed arrow bodies. At one shot a second and a third of a second in the air, one is
        /// ever in flight; four covers a tuned-up bow without a hitch on the first volley.
        /// </summary>
        private const int ArrowPrewarm = 4;

        /// <summary>CC §3.2's distance weight, elite bonus and finisher bonus, and §3.3's hysteresis.</summary>
        private const float DistanceWeight = 3f;

        private const float EliteBonus = 2f;
        private const float FinisherBonus = 1f;
        private const float Hysteresis = 1.5f;

        /// <summary>CC §3.1's loop rate: 10 Hz.</summary>
        private const float TargetingCadence = 0.1f;

        /// <summary>
        /// Damage per arrow. Nothing in the sandbox has health to take it; the spec requires a
        /// positive number.
        /// </summary>
        private const float ArrowDamage = 1f;

        /// <summary>
        /// The cone a projectile weapon must declare: the whole circle, since a shot is aimed by
        /// its target rather than by an arc. <c>WeaponSpec</c> refuses anything else.
        /// </summary>
        private const float FullCircleDeg = 360f;

        /// <summary>
        /// The arrow's hit radius. Unread here, since an arrow lands on the dummy it was loosed at,
        /// and required positive by a projectile weapon's spec.
        /// </summary>
        private const float ArrowRadius = 0.3f;

        /// <exception cref="MissingReferenceException">A required reference is not dressed.</exception>
        protected override void Configure(IContainerBuilder builder)
        {
            Require(_player, nameof(PlayerView), "the Ranger object — without it nothing moves");
            Require(_animatorView, nameof(RangerAnimatorView), "the Ranger body nested under the Ranger object — without it the body never changes pose");
            Require(_muzzle, "Muzzle", "the bow under the Ranger's left handslot — without it no arrow has anywhere to leave from");
            Require(_arrowPrefab, "Arrow Prefab", "Prefabs/Projectiles/Arrow.prefab — without it every shot is invisible");

            builder.Register<DomainEventHub>(Lifetime.Scoped);
            builder.Register<InputAdapter>(Lifetime.Scoped);

            builder.RegisterComponent(_player);
            builder.RegisterComponent(_animatorView);

            builder.RegisterInstance(new MovementSpec(_moveSpeed, _accelTime, _decelTime, _turnSpeedDeg));

            builder.RegisterInstance(new TargetingSpec(
                Mathf.Max(_acquireRange, _range),
                DistanceWeight,
                EliteBonus,
                FinisherBonus,
                Hysteresis,
                TargetingCadence));

            builder.RegisterInstance(new WeaponSpec(
                WeaponKind.Projectile,
                ArrowDamage,
                _shotsPerSecond,
                _range,
                FullCircleDeg,
                _releaseFraction,
                _arrowSpeed,
                ArrowRadius));

            // The arrow as the default and no class look book (RS-02c rule 4): the sandbox has no
            // class, so every shot it fires flies Arrow.prefab directly.
            builder.Register(
                resolver => new ProjectileViews(
                    resolver,
                    _arrowPrefab,
                    transform,
                    resolver.Resolve<DomainEventHub>(),
                    ArrowPrewarm,
                    looks: null),
                Lifetime.Scoped);

            builder.RegisterEntryPoint<RangerSandboxLoop>(Lifetime.Scoped)
                .WithParameter("muzzle", _muzzle)
                .WithParameter("dummies", _dummies ?? Array.Empty<Animator>())
                .WithParameter("aimHeight", _aimHeight)
                .WithParameter("shootWhileMoving", _shootWhileMoving);
        }

        private static void Require(UnityEngine.Object reference, string field, string where)
        {
            if (reference == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RangerSandboxScope)} has no {field} assigned. Drag {where}.");
            }
        }
    }
}

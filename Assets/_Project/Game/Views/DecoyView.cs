using System;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and VFX_Decoy.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One corpse standing in the arena. Core decided that a Shroudstep left something behind and
    /// where it stands; this is the object standing there, and it decides nothing at all. See AR §3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It has no collider, no health bar and no <c>Step</c>, and each absence is a decision.</b>
    /// A decoy does not move, does not fade and cannot be hit (M5-03 rule 7): it is not an entity at
    /// all — no health, no registry slot, no place in the snapshot — so the whole of its effect on
    /// the world is the one question <c>LureSystem.TryGetLure</c> answers about it. A collider here
    /// would be a shape the player's own swing could find, and <c>ConeOverlapQuery</c> resolves a
    /// physics hit through <c>EnemyViews.TryGetId</c>, so what it would report is a <em>stranger's</em>
    /// id — <see cref="MinionView"/>'s rule 1, arriving at the one body in the game that has even
    /// less business being in that index than a Wight.
    /// </para>
    /// <para>
    /// <b>A body rather than a decal, and that is rule 1 rather than a preference.</b>
    /// <c>VFX_TelegraphRing</c> and <c>VFX_ConsecrateZone</c> are flat quads on the floor because
    /// what they mark is <em>ground</em>. A decoy is a thing an enemy walks at and swings at, and a
    /// flat patch under their feet would read as a hazard — which is <see cref="Palette.Danger"/>'s
    /// reserved meaning and the one colour GD §16.4 says is <em>"used for nothing else, ever."</em>
    /// So it is the shared body at the player's silhouette, in the player's own cyan, at half alpha:
    /// yours, and dead.
    /// </para>
    /// <para>
    /// <b>There is nothing per-frame for this to own.</b> <see cref="ProjectileView"/>,
    /// <see cref="TelegraphRingView"/>, <see cref="ZoneView"/> and <see cref="BossBeatView"/> are
    /// each stepped with <c>snapshot.Dt</c> because each is interpolating something core is also
    /// counting down. A decoy interpolates nothing — M5-03 rule 1 is that <em>"nothing about one
    /// changes after it is dropped"</em> — so its whole life is <em>appear</em> and <em>disappear</em>,
    /// and both moments arrive as events. A <c>Step</c> here would be a clock running beside core's
    /// for no reason, and the class that had one would eventually be given something to do with it.
    /// </para>
    /// <para>
    /// <b>An unbound view is not a live decoy.</b> <see cref="DecoyViews"/> rents one from a
    /// <see cref="ViewPool{T}"/> per <c>DecoySpawned</c> and returns it on <c>DecoyExpired</c>, so
    /// the same object stands in for one corpse, then another, with a different id.
    /// <see cref="Bind"/> and <see cref="Unbind"/> are that seam.
    /// </para>
    /// </remarks>
    public sealed class DecoyView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// The id of a view that is not standing in for anything. Zero, because
        /// <c>LureSystem</c> hands out ids from 1 and spells the same number
        /// <c>LureSystem.NoLure</c>, so no live decoy can ever collide with it.
        /// </summary>
        public const int Unbound = 0;

        /// <summary>
        /// How solid a corpse is drawn: half (rule 1).
        /// </summary>
        /// <remarks>
        /// <b>Ours, and it only means anything because the surface is a transparent one.</b> The
        /// body's material is alpha-blended, unlike <c>M_BoneGrey</c> and <c>M_PlayerCyan</c>, which
        /// are <c>_Surface: 0</c> and would draw this tint fully solid — the same class of mistake
        /// <c>M_TelegraphRing</c> made for six prefabs until rule 8's keyword (ledger row 3). Half
        /// is the separator that says <em>dead</em> against the player's own body at full strength;
        /// what a phone makes of it is a device question and nobody has looked at one.
        /// </remarks>
        public const float Alpha = 0.5f;

        [Tooltip("The body's renderer — the Mesh child. Assigned rather than searched for, for " +
                 "MinionView's reason: a GetComponentInChildren per instance is a cost for a " +
                 "reference the prefab already knows. Optional — a body with no renderer is " +
                 "invisible rather than broken, which is what an EditMode fixture builds.")]
        [SerializeField] private Renderer _renderer;

        [Tooltip("What the body's _BaseColor is driven to. GD §16.4's player cyan at half alpha: " +
                 "the corpse is the player's own silhouette left behind, so it is their colour, " +
                 "and it is emphatically not Palette.Danger — a red body on the floor is the one " +
                 "thing in this game that means 'this will hurt you' (rule 1).")]
        [SerializeField]
        private Color _tint = new Color(Palette.Player.r, Palette.Player.g, Palette.Player.b, Alpha);

        /// <summary>
        /// URP's base-colour property, resolved once. An instance field rather than a static, for
        /// the reason this project bans mutable statics outright and <see cref="MinionView"/> holds
        /// the same id the same way.
        /// </summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _block;
        private int _id = Unbound;

        /// <summary>The core-side id this body stands for, or <see cref="Unbound"/>.</summary>
        public int Id => _id;

        /// <summary>Whether this view is currently standing in for a live decoy.</summary>
        public bool IsBound => _id != Unbound;

        /// <summary>Where the body is standing.</summary>
        /// <remarks>
        /// Read by the fixture and by nothing in the game. A decoy reports nothing back to core —
        /// core placed it, core owns its clock, and it never moves — so unlike
        /// <see cref="MinionView.Position"/> this is not a fact on its way into a snapshot.
        /// </remarks>
        public Vector3 Position => transform.position;

        /// <summary>What this body is drawn as. Authored, and read by nothing at runtime.</summary>
        /// <remarks>
        /// Exposed so <c>DecoyViewsTests</c> can pin the shipped prefab against
        /// <see cref="Palette.Player"/> at <see cref="Alpha"/> — and against
        /// <see cref="Palette.Danger"/>, which rule 1 forbids here and GD §16.4 forbids anywhere
        /// that is not a threat. A recolour has to be a deliberate edit with a red row under it
        /// rather than a drift.
        /// </remarks>
        public Color Tint => _tint;

        /// <summary>
        /// Puts this body into service as <paramref name="id"/>, standing at
        /// <paramref name="position"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="id"/> is not positive. <c>LureSystem</c> issues ids from 1 and spells
        /// zero <c>NoLure</c>, so a zero or negative one means the caller invented it — and a body
        /// bound to <see cref="Unbound"/> could never be retired by its own <c>DecoyExpired</c>,
        /// which would leave it standing for the rest of the run.
        /// </exception>
        public void Bind(int id, Vector3 position)
        {
            if (id <= Unbound)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    id,
                    "A decoy id must be positive; LureSystem issues them from 1.");
            }

            _id = id;

            transform.position = position;
        }

        /// <summary>Takes this body out of service. Idempotent.</summary>
        public void Unbind()
        {
            _id = Unbound;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <see cref="DecoyViews"/> calls <see cref="Bind"/> on
        /// the very next line with the id and the position this body is being put into service with,
        /// and the pool has neither to give. <see cref="MinionView.OnSpawn"/>'s bargain.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// <b>Shorter even than <see cref="MinionView.OnDespawn"/>, and the shortness is the
        /// point.</b> A Wight leaves an id, a velocity and a fall speed behind it; a corpse leaves
        /// an id, because it never moved and nothing on this prefab remembers anything else.
        /// Anything added to <c>VFX_Decoy.prefab</c> that remembers something joins the list here
        /// (AR §18.4).
        /// </remarks>
        public void OnDespawn()
        {
            Unbind();
        }

        private void Awake()
        {
            ApplyTint();
        }

        /// <summary>
        /// Drives the body's base colour to <see cref="_tint"/>, once, for the life of the instance.
        /// </summary>
        /// <remarks>
        /// Through a <see cref="MaterialPropertyBlock"/> rather than by assigning a material, which
        /// is <c>EnemyLook</c>'s bargain and <see cref="MinionView.Awake"/>'s: both corpses a run
        /// can hold keep one shared material and one draw call (GD §11.3). In <c>Awake</c> rather
        /// than on the rental because there is one kind of decoy, its colour never changes, and
        /// nothing else on this prefab writes <c>_BaseColor</c> — so there is no second writer for a
        /// rental to undo.
        /// </remarks>
        private void ApplyTint()
        {
            // Unity's ==: an unassigned renderer is a real answer, not a mis-wiring. A fixture
            // builds a body with no mesh at all.
            if (_renderer == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();

            _renderer.GetPropertyBlock(_block);
            _block.SetColor(_baseColorId, _tint);
            _renderer.SetPropertyBlock(_block);
        }
    }
}

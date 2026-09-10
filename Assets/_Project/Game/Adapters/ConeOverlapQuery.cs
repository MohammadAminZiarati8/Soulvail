using System;
using Soulvail.Core.Run;
using Soulvail.Game.Views;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The body's answer to <see cref="ConeHitIntent"/>: a sphere overlap and an angle test, turned
/// into the enemy ids that were standing in the wedge. Step 5 of AR §4.3, and the one physics
/// query in the combat loop.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides nothing.</b> Who is in the cone is a fact about the world, which is exactly the
/// kind of thing core cannot know and must be told (ADR-0003); what that costs an enemy is core's,
/// and happens inside <c>ReportConeHits</c> a moment later. Nothing here reads health, checks
/// whether a target is blocked, or looks at the targeter's choice — an enemy standing in the wedge
/// is reported whether or not the gun was aimed at it, because CC §4.2's arc is the arc.
/// </para>
/// <para>
/// <b>Sphere plus dot product, never per-swing colliders.</b> CC §4.2 is explicit about the
/// method, and the reason is the same one AR §14 gives for everything else in this path: a swing
/// happens three times a second per character, and spawning a trigger volume to resolve it would
/// be an object created and destroyed six times a second before a single enemy has moved.
/// </para>
/// <para>
/// <b>The geometry comes from the request, not from the player.</b> <see cref="Query"/> reads
/// <see cref="ConeHitIntent.Origin"/> and <see cref="ConeHitIntent.FacingXZ"/> rather than the
/// live transform, because the answer is given at least a frame after the question was asked and
/// resolving against wherever the body has since walked to would silently widen every swing by a
/// frame of movement.
/// </para>
/// <para>
/// <b>Allocation-free by construction.</b> The collider buffer is filled once and reused for the
/// life of the run, the overlap is the non-allocating overload, and the ids go out through a
/// <see cref="Span{T}"/> the caller owns. AR §14: nothing in the adapters that feed core allocates
/// per frame, and this one runs on the frames that already have the most happening in them.
/// </para>
/// </remarks>
public sealed class ConeOverlapQuery
{
    /// <summary>
    /// Colliders one swing is built to see. Comfortably past GD §11's 18-enemy concurrency cap,
    /// because the sphere is cast at the weapon's full range and a wedge that only opens 60° still
    /// sweeps up everything standing in the other 300°.
    /// </summary>
    public const int DefaultCapacity = 32;

    private readonly Collider[] _colliders;
    private readonly EnemyViews _views;
    private readonly LayerMask _enemyLayer;

    private bool _warnedAboutCapacity;

    /// <param name="capacity">
    /// How many colliders one overlap may return. See <see cref="DefaultCapacity"/>.
    /// </param>
    /// <param name="enemyLayer">
    /// The layers a swing can hit — the <c>Enemy</c> layer (M1-07). A mask of zero would find
    /// nothing, on every swing, for the whole run, so it is rejected here rather than discovered
    /// during a playtest.
    /// </param>
    /// <param name="views">
    /// The arena's bodies, for turning a collider back into the id core knows it by.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="enemyLayer"/> selects no layers.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="views"/> is null.</exception>
    public ConeOverlapQuery(int capacity, LayerMask enemyLayer, EnemyViews views)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "A swing must be able to see at least one collider.");
        }

        if (enemyLayer.value == 0)
        {
            throw new ArgumentException(
                "The enemy layer mask is empty, so every swing would sweep an empty sphere and " +
                "the Censer would never hit anything. Set RunScope's Enemy Layer field to the " +
                "Enemy layer.",
                nameof(enemyLayer));
        }

        _views = views ?? throw new ArgumentNullException(nameof(views));
        _colliders = new Collider[capacity];
        _enemyLayer = enemyLayer;
    }

    /// <summary>How many colliders one <see cref="Query"/> can see. The size <paramref name="ids"/> wants.</summary>
    public int Capacity => _colliders.Length;

    /// <summary>
    /// Fills <paramref name="ids"/> with the distinct enemies standing inside <paramref name="cone"/>
    /// and returns how many there were.
    /// </summary>
    /// <param name="cone">The wedge core asked about, in world metres and degrees.</param>
    /// <param name="ids">
    /// Where the answer goes. Sized <see cref="Capacity"/> by the caller; a shorter span simply
    /// stops early, because dropping a hit is better than throwing inside a frame.
    /// </param>
    /// <returns>How many ids were written. Zero is a real answer and must still be reported.</returns>
    /// <remarks>
    /// <para>
    /// Deduplicated by id rather than by collider, so a future enemy built out of several colliders
    /// is still hit once — CC §4.2's "one damage event per enemy per swing". The scan is linear over
    /// what has been written so far, which at this size beats a hash set and allocates nothing.
    /// </para>
    /// <para>
    /// A collider on the enemy layer that resolves to no live enemy is skipped in silence. That is
    /// normal rather than exceptional: <c>EnemyViews</c> destroys a body on <c>EnemyDespawned</c>,
    /// and a corpse's collider is disabled by <c>EnemyHitFeedback</c> the moment it dies, so the
    /// window where physics still knows about something core does not is a frame wide and expected.
    /// </para>
    /// </remarks>
    public int Query(in ConeHitIntent cone, Span<int> ids)
    {
        Vector3 origin = cone.Origin.ToUnity();
        Vector2 facing = cone.FacingXZ.ToUnity();

        // Triggers included deliberately: an enemy body *is* a trigger (Enemy.prefab, M1-07), so
        // the default QueryTriggerInteraction would find nothing at all.
        int found = Physics.OverlapSphereNonAlloc(
            origin,
            cone.Range,
            _colliders,
            _enemyLayer,
            QueryTriggerInteraction.Collide);

        if (found >= _colliders.Length)
        {
            WarnAboutCapacityOnce();
        }

        int count = 0;

        for (int i = 0; i < found; i++)
        {
            if (count >= ids.Length)
            {
                break;
            }

            if (!_views.TryGetId(_colliders[i], out int id))
            {
                continue;
            }

            if (!_views.TryGet(id, out EnemyView view) || view == null)
            {
                continue;
            }

            if (!IsInCone(origin, facing, view.Position, cone.Range, cone.AngleDeg))
            {
                continue;
            }

            if (Contains(ids, count, id))
            {
                continue;
            }

            ids[count] = id;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Is <paramref name="point"/> inside the horizontal wedge at <paramref name="origin"/>?
    /// </summary>
    /// <param name="origin">The wedge's apex, in world metres.</param>
    /// <param name="facingXZ">The direction it is centred on, on the ground plane. Need not be unit length.</param>
    /// <param name="point">The point being tested, in world metres. Its height is ignored.</param>
    /// <param name="range">How far the wedge reaches, in metres. Zero or negative is an empty wedge.</param>
    /// <param name="angleDeg">The full opening angle: 60 means ±30° either side of <paramref name="facingXZ"/>.</param>
    /// <remarks>
    /// <para>
    /// <b>Everything is measured on XZ</b>, exactly as every enemy sense is (M1-06). The height
    /// between a player capsule's centre and an enemy's is a rendering detail, and counting it would
    /// shorten the reach of every swing by an amount nobody authored.
    /// </para>
    /// <para>
    /// <b>The range is guarded before it is squared.</b> A negative range is not "nothing within
    /// it" — squaring turns −1 into 1 and a nonsense argument silently becomes a plausible
    /// one-metre one. Same trap as M1-09's focus radius, in a new place.
    /// </para>
    /// <para>
    /// <b>A point at the apex is inside.</b> A dummy standing on top of the player has no direction
    /// from the player, so no angle test can be run on it — and the only answer that is not absurd
    /// is that the thing you are inside of is in front of you.
    /// </para>
    /// </remarks>
    public static bool IsInCone(Vector3 origin, Vector2 facingXZ, Vector3 point, float range, float angleDeg)
    {
        if (!(range > 0f))
        {
            return false;
        }

        float dx = point.x - origin.x;
        float dz = point.z - origin.z;
        float distanceSq = (dx * dx) + (dz * dz);

        if (distanceSq > range * range)
        {
            return false;
        }

        // Standing on the apex. Tested before the facing, so it holds even for a swing with no
        // direction at all.
        if (distanceSq <= 0f)
        {
            return true;
        }

        if (!(angleDeg > 0f))
        {
            return false;
        }

        float facingSq = (facingXZ.x * facingXZ.x) + (facingXZ.y * facingXZ.y);

        // No direction, no wedge. Core normalises the facing before it sends it, so this is a
        // malformed request rather than a case the game reaches.
        if (!(facingSq > 0f))
        {
            return false;
        }

        // Normalised rather than compared against a scaled cosine, which would need its own case
        // for wedges wider than 180° where the cosine goes negative. One square root on a path
        // that runs three times a second is not the place to be clever.
        float cosine = ((facingXZ.x * dx) + (facingXZ.y * dz))
                       / Mathf.Sqrt(facingSq * distanceSq);

        return cosine >= Mathf.Cos(Mathf.Min(angleDeg, 360f) * 0.5f * Mathf.Deg2Rad);
    }

    private static bool Contains(Span<int> ids, int count, int id)
    {
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <remarks>
    /// <para>
    /// Once per run rather than once per swing, for the reason <c>EnemyViews</c> warns once: at the
    /// cap this is true on every swing for as long as the arena is crowded, and three log lines a
    /// second would cost more than the enemies being complained about.
    /// </para>
    /// <para>
    /// A full buffer is a <em>maybe</em>, not a miss: the non-allocating overlap returns how many
    /// it wrote and never how many it found, so a sweep that fills the array exactly is
    /// indistinguishable from one that overflowed it. The warning says so rather than claiming a
    /// hit was dropped, because a false certainty here would send someone hunting a bug that may
    /// not exist.
    /// </para>
    /// </remarks>
    private void WarnAboutCapacityOnce()
    {
        if (_warnedAboutCapacity)
        {
            return;
        }

        _warnedAboutCapacity = true;

        Debug.LogWarning(
            $"A swing's overlap filled its {_colliders.Length}-collider buffer. Anything else " +
            "standing in the sphere was never considered and could not be hit, and there is no " +
            "way from here to tell whether there was. Raise the capacity ConeOverlapQuery is " +
            "registered with in RunScope.");
    }
}

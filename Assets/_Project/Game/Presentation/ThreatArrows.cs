using System;
using System.Collections.Generic;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace Soulvail.Game.Presentation;

/// <summary>
/// The screen-edge arrows GD §16.1 lists and GD §12.4's on-screen rule requires. A listener and a
/// projector: it owns no simulation, answers core nothing, and decides only where on the border of
/// a phone screen a thing that is off it should be pointed at from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two meanings, one grammar.</b> GD §16.4 reserves saturated red-orange for danger and cyan for
/// the player, so a threat off screen gets a red-orange arrow and the enemy the player
/// <em>tapped</em> — ledger row 12's held focus — gets a cyan one. An enemy that is both gets the
/// cyan arrow, because "the thing you picked is over there" is the more specific statement and the
/// player already knows it can hurt them. Answering the two rows with two separate indicators is
/// exactly what the ledger complained would happen if they were built apart.
/// </para>
/// <para>
/// <b>It holds its own census rather than asking for one.</b> <see cref="EnemyViews"/> can say where
/// a body is but not who is alive — it indexes by id and exposes no enumeration — so this subscribes
/// to the same three events and keeps a list of ids, resolving each one's position through the
/// census on the frame it draws. That is also what makes GD §7.3's stall rule possible at all: the
/// rule is about <em>how many</em> are left, which is a census question and not a position one.
/// </para>
/// <para>
/// <b>A fixed set of instances, not a <c>ViewPool</c>.</b> The pool constrains its bodies to
/// <c>IPoolable</c> and exists so a rented body can be told to forget its last life; an arrow
/// remembers nothing, because every frame rewrites its position, rotation, colour and alpha from
/// scratch. A pair of empty callbacks would be ceremony. Said out loud because M2-09 and M2-12b both
/// use the pool and both need to, so this looks like an oversight unless it is named.
/// </para>
/// <para>
/// <b>It owns its own late frame</b>, as <see cref="VContainer.Unity.ILateTickable"/> rather than as
/// a <c>MonoBehaviour</c>. <c>ReticleView</c>'s reason: the bodies move inside <c>RunTicker.Tick</c>,
/// which is the Update phase, so an arrow placed in <c>Update</c> points at where its threat stood
/// last frame. AR §18.1's <em>"a view that answers core with a fact cannot own its own
/// <c>Update</c>"</em> does not apply — this answers core nothing — and this is the first object in
/// the project that legitimately owns one, which is worth saying rather than leaving to be
/// rediscovered.
/// </para>
/// <para>
/// <b>The safe area is handled by where it is parented, not here.</b> The arrow root hangs under the
/// HUD's <c>SafeArea</c> object, whose <c>SafeAreaFitter</c> already insets it for a notch; the
/// border this places arrows on is that parent's own rect. One implementation of "avoid the notch"
/// rather than a second copy of it (GD §12.4, rule 8).
/// </para>
/// </remarks>
public sealed class ThreatArrows : ILateTickable, IDisposable
{
    /// <summary>
    /// Metres. An enemy further than this is off screen but cannot reach you, so it is scenery,
    /// not a threat.
    /// </summary>
    /// <remarks>
    /// The Spitter's 14 m standoff plus margin — the longest reach any archetype in M2 has — and
    /// deliberately one number rather than a per-archetype question: a Husk closing at 13 m off
    /// screen is worth an arrow too, and an archetype table the presentation layer had to consult
    /// would be a second copy of the roster.
    /// </remarks>
    public const float ThreatRange = 16f;

    /// <summary>
    /// GD §7.3: when this many or fewer survive and <see cref="StallTime"/> passes, every survivor
    /// gets an arrow whatever the range.
    /// </summary>
    public const int StallSurvivors = 3;

    /// <summary>Seconds in the stall band before the survivors are pointed at. GD §7.3.</summary>
    public const float StallTime = 8f;

    /// <summary>
    /// GD §12.4's thumb rule: the bottom-left and bottom-right ~15 % of the frame belong to thumbs,
    /// and nothing critical may resolve there. An arrow whose natural edge point lands inside one
    /// slides along the border to the nearest point that does not.
    /// </summary>
    public const float ThumbFraction = 0.15f;

    /// <summary>GD §16.4's saturated red-orange, <c>#FF4A1F</c>. Danger, and nothing else, ever.</summary>
    public static readonly Color Danger = new Color(1f, 0.290f, 0.122f, 1f);

    /// <summary>GD §16.4's cyan, <c>#22D3EE</c>. The player — and so the enemy the player picked.</summary>
    public static readonly Color HeldFocus = new Color(0.133f, 0.827f, 0.933f, 1f);

    /// <summary>Metres at which an arrow is at its brightest and largest.</summary>
    private const float NearMetres = 4f;

    /// <summary>Alpha and scale at <see cref="ThreatRange"/> and beyond, and at <see cref="NearMetres"/>.</summary>
    private const float FarAlpha = 0.35f;
    private const float NearAlpha = 1f;
    private const float FarScale = 0.7f;
    private const float NearScale = 1.15f;

    /// <summary>
    /// UI units the border is held inside the parent rect by, so a rotated arrow sits fully on
    /// screen rather than half beyond it.
    /// </summary>
    private const float EdgeMargin = 28f;

    /// <summary>The id <c>TargetChanged</c> carries when no focus is being held out of range.</summary>
    private const int NoFocus = -1;

    /// <summary>Not in the stall band at all, as opposed to having just entered it.</summary>
    private const float NotStalling = -1f;

    private readonly IObjectResolver _resolver;
    private readonly RectTransform _prefab;
    private readonly RectTransform _parent;
    private readonly Camera _camera;
    private readonly EnemyViews _views;
    private readonly PlayerView _player;
    private readonly Func<float> _clock;

    /// <summary>
    /// Every arrow instance this object has made, in service or parked. Pre-sized to the prewarm so
    /// the list itself never grows while a wave is on screen.
    /// </summary>
    private readonly List<Arrow> _arrows;

    /// <summary>
    /// Who is alive, in spawn order. Sized to the snapshot's enemy capacity for the reason the path
    /// and sight caches are: the arena's population is bounded by it, so this never grows in a run.
    /// </summary>
    private readonly List<int> _alive = new List<int>(BootInstaller.SnapshotEnemyCapacity);

    private readonly IDisposable _spawnedSubscription;
    private readonly IDisposable _diedSubscription;
    private readonly IDisposable _despawnedSubscription;
    private readonly IDisposable _targetSubscription;

    /// <summary>When the population entered the stall band, or <see cref="NotStalling"/>.</summary>
    private float _stallSince = NotStalling;

    private int _heldFocusId = NoFocus;
    private bool _disposed;

    /// <param name="resolver">
    /// The run's container, so instances are made through it rather than through
    /// <c>Object.Instantiate</c> — the rule <see cref="EnemyViews"/> and <c>ProjectileViews</c>
    /// keep. Nothing on the arrow prefab takes an <c>[Inject]</c> today; going through the container
    /// anyway is what stops the first component that does from being silently deaf.
    /// </param>
    /// <param name="arrowPrefab">One arrow. Every threat and every held focus shares it, tinted.</param>
    /// <param name="parent">
    /// The arrow root on the HUD, under <c>SafeArea</c>. Its rect is the border arrows are placed on,
    /// which is how the notch is avoided — see the class remarks.
    /// </param>
    /// <param name="camera">The arena's camera, for deciding what is off screen and in which direction.</param>
    /// <param name="views">The run's bodies, for resolving a live id to a position.</param>
    /// <param name="player">
    /// The player's body. <see cref="ThreatRange"/> is measured from the character, not from the
    /// camera: the camera stands ~8.7 m back along its own axis (<c>FollowCamera</c>, 16 m at 57°),
    /// so a camera-relative range would be wrong by half the Spitter's standoff.
    /// </param>
    /// <param name="hub">The run's event hub, subscribed to for the length of this object's life.</param>
    /// <param name="clock">
    /// Wall-clock seconds, for GD §7.3's stall timer. A cosmetic view timer, which AR §18.2 names as
    /// the deliberate exception to <c>snapshot.Dt</c>: nothing about it is simulated and no outcome
    /// depends on it. Passed in rather than read from <c>Time</c> here for <c>HapticsListener</c>'s
    /// reason — eight seconds is not a thing a test can wait for.
    /// </param>
    /// <param name="prewarm">
    /// How many arrows to build before the run starts, so the only <c>Instantiate</c> calls of a
    /// whole run happen while the scene is still loading (AR §14, GD §11.3).
    /// </param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="prewarm"/> is negative.</exception>
    public ThreatArrows(
        IObjectResolver resolver,
        RectTransform arrowPrefab,
        RectTransform parent,
        Camera camera,
        EnemyViews views,
        PlayerView player,
        DomainEventHub hub,
        Func<float> clock,
        int prewarm = 8)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        // Unity's == rather than `is null`: a destroyed or unassigned Object is a live reference
        // that only compares equal to null through the engine's operator.
        if (arrowPrefab == null)
        {
            throw new ArgumentNullException(nameof(arrowPrefab));
        }

        if (parent == null)
        {
            throw new ArgumentNullException(nameof(parent));
        }

        if (camera == null)
        {
            throw new ArgumentNullException(nameof(camera));
        }

        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        if (prewarm < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prewarm),
                prewarm,
                "An arrow count cannot be negative.");
        }

        _prefab = arrowPrefab;
        _parent = parent;
        _camera = camera;
        _views = views ?? throw new ArgumentNullException(nameof(views));
        _player = player;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        _arrows = new List<Arrow>(prewarm);

        for (int i = 0; i < prewarm; i++)
        {
            _arrows.Add(Build());
        }

        // Subscribed in the constructor, which is AR §18.1's rule: a subscriber that must hear the
        // opening of a run takes its subscription from the dependency chain, never from a Start of
        // its own, because VContainer orders no two entry points' Starts.
        _spawnedSubscription = hub.Subscribe<EnemySpawned>(OnSpawned);

        // Died as well as despawned, because rule 5 says *living* and the two are 0.6 s apart: a
        // corpse holds its body for the length of its dissolve, and an arrow pointing at one is the
        // game saying "it is still coming for you" about something that is already dead.
        _diedSubscription = hub.Subscribe<EnemyDied>(OnDied);
        _despawnedSubscription = hub.Subscribe<EnemyDespawned>(OnDespawned);

        _targetSubscription = hub.Subscribe<TargetChanged>(OnTargetChanged);
    }

    /// <summary>Arrows on screen right now. <c>DebugOverlay</c> reads it.</summary>
    public int Count { get; private set; }

    /// <summary>How many arrow instances exist, drawn or parked. For the prewarm rows.</summary>
    public int InstanceCount => _arrows.Count;

    /// <summary>
    /// Places every arrow this frame deserves and parks the rest.
    /// </summary>
    /// <remarks>
    /// Allocates nothing: the census is a pre-sized list of ints, the projection is struct
    /// arithmetic, and the instances were built in the constructor.
    /// </remarks>
    public void LateTick()
    {
        if (_disposed)
        {
            return;
        }

        float now = _clock();

        bool stalled = _stallSince >= 0f && now - _stallSince >= StallTime;

        Vector3 playerPosition = _player == null ? Vector3.zero : _player.Position;

        int used = 0;

        for (int i = 0; i < _alive.Count; i++)
        {
            int id = _alive[i];

            // A miss is not an error, for ReticleView's reason: the census and the bodies are
            // updated by different events and may legitimately disagree for one frame.
            if (!_views.TryGet(id, out EnemyView view) || view == null)
            {
                continue;
            }

            Vector3 position = view.Position;

            if (!TryFrameDirection(position, out Vector2 direction))
            {
                // On screen. GD §12.4's rule is about damage from *outside* the frustum, and an
                // arrow pointing at something the player can already see is noise on a 6-inch
                // screen (GD §11.3, P1).
                continue;
            }

            bool held = id == _heldFocusId;

            float distance = DistanceXZ(playerPosition, position);

            // Spelled as "within" rather than "not beyond", so a NaN position drops the arrow
            // instead of pinning one to the border for ever (AR §18.3).
            if (!held && !stalled && !(distance <= ThreatRange))
            {
                continue;
            }

            Place(ArrowAt(used), direction, distance, held ? HeldFocus : Danger);

            used++;
        }

        for (int i = used; i < _arrows.Count; i++)
        {
            Hide(_arrows[i]);
        }

        Count = used;
    }

    /// <summary>
    /// Stops listening and destroys every arrow it built.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _spawnedSubscription.Dispose();
        _diedSubscription.Dispose();
        _despawnedSubscription.Dispose();
        _targetSubscription.Dispose();

        for (int i = 0; i < _arrows.Count; i++)
        {
            Arrow arrow = _arrows[i];

            if (arrow.Transform == null)
            {
                continue;
            }

            // The play-mode split ViewPool.Dispose makes, and for the same reason (M0-14): in edit
            // mode Object.Destroy destroys nothing and logs an *error* rather than throwing, which
            // would both leak the instance and redden every EditMode test that disposes one of
            // these.
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(arrow.Transform.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(arrow.Transform.gameObject);
            }
        }

        _arrows.Clear();
        _alive.Clear();

        Count = 0;
    }

    /// <summary>
    /// Where on the frame <paramref name="world"/> lies, as a direction from the middle of it —
    /// or false when it is on screen and needs no arrow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The direction is in <em>fractions of the frame</em>, both components, which is what lets one
    /// number do for a 20:9 phone and a square test rect alike: the caller multiplies it by the
    /// border's own width and height.
    /// </para>
    /// <para>
    /// <b>A threat behind the camera is not mirrored.</b> <c>WorldToViewportPoint</c> returns a
    /// point for a negative depth as readily as for a positive one, and that point is the true
    /// position reflected through the centre — so an enemy behind the player would be pointed at
    /// through the top of the screen, which is the classic projection sign bug. Behind is answered
    /// in camera space instead, with the depth folded into the downward component: behind the camera
    /// is behind the player, which on a camera pitched 57° down is the bottom of the frame, and a
    /// threat exactly behind therefore reads as straight down rather than as an undefined centre
    /// point.
    /// </para>
    /// </remarks>
    private bool TryFrameDirection(Vector3 world, out Vector2 direction)
    {
        Vector3 local = _camera.transform.InverseTransformPoint(world);

        if (local.z > 0f)
        {
            Vector3 viewport = _camera.WorldToViewportPoint(world);

            if (viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f)
            {
                direction = Vector2.zero;

                return false;
            }

            direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
        }
        else
        {
            var behind = new Vector2(local.x, local.y + local.z);

            // Normalised so that the two branches hand the caller the same kind of quantity: the
            // branch above is already a fraction of the frame, and metres are not.
            float length = behind.magnitude;

            direction = length > 0f ? behind / length : Vector2.zero;
        }

        if (direction.sqrMagnitude <= 0f || !IsFinite(direction))
        {
            // Directly behind, or a position that is not a number. Straight down is the honest
            // answer for the first and a harmless one for the second.
            direction = new Vector2(0f, -1f);
        }

        return true;
    }

    /// <summary>
    /// Puts one arrow on the border, pointed at its threat, sized and faded by how close it is.
    /// </summary>
    private void Place(Arrow arrow, Vector2 direction, float distance, Color colour)
    {
        if (arrow.Transform == null)
        {
            return;
        }

        Rect rect = _parent.rect;

        float marginX = Mathf.Min(EdgeMargin, rect.width * 0.5f);
        float marginY = Mathf.Min(EdgeMargin, rect.height * 0.5f);

        float halfWidth = Mathf.Max((rect.width * 0.5f) - marginX, 0f);
        float halfHeight = Mathf.Max((rect.height * 0.5f) - marginY, 0f);

        // The direction is a fraction of the frame in each axis, so it becomes a screen direction
        // by being scaled back up by the border's own dimensions. Skipping this would put an arrow
        // for a threat 30° off the axis at 45° on a 20:9 phone.
        var scaled = new Vector2(direction.x * halfWidth, direction.y * halfHeight);

        Vector2 point = OnBorder(scaled, halfWidth, halfHeight);

        point = OutsideTheThumbs(point, halfWidth, halfHeight);

        arrow.Transform.anchoredPosition = point + rect.center;

        // The prefab points along its own +Y, so the rotation is the direction's angle less the
        // quarter turn between "up" and "along +X".
        float degrees = (Mathf.Atan2(scaled.y, scaled.x) * Mathf.Rad2Deg) - 90f;

        arrow.Transform.localRotation = Quaternion.Euler(0f, 0f, degrees);

        // Proximity is alpha and size, never a number: a phone screen is small and eight arrows
        // with distances written on them is noise (GD §11.3, P1, rule 10).
        float nearness = Mathf.InverseLerp(ThreatRange, NearMetres, distance);

        float scale = Mathf.Lerp(FarScale, NearScale, nearness);

        arrow.Transform.localScale = new Vector3(scale, scale, 1f);

        colour.a = Mathf.Lerp(FarAlpha, NearAlpha, nearness);

        for (int i = 0; i < arrow.Graphics.Length; i++)
        {
            Graphic graphic = arrow.Graphics[i];

            if (graphic != null)
            {
                graphic.color = colour;
            }
        }

        if (!arrow.Transform.gameObject.activeSelf)
        {
            arrow.Transform.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Where a ray from the middle of the border leaves it, along <paramref name="direction"/>.
    /// </summary>
    private static Vector2 OnBorder(Vector2 direction, float halfWidth, float halfHeight)
    {
        float scaleX = Mathf.Abs(direction.x) > 0f ? halfWidth / Mathf.Abs(direction.x) : float.PositiveInfinity;
        float scaleY = Mathf.Abs(direction.y) > 0f ? halfHeight / Mathf.Abs(direction.y) : float.PositiveInfinity;

        float scale = Mathf.Min(scaleX, scaleY);

        if (float.IsInfinity(scale) || float.IsNaN(scale))
        {
            return new Vector2(0f, -halfHeight);
        }

        return new Vector2(
            Mathf.Clamp(direction.x * scale, -halfWidth, halfWidth),
            Mathf.Clamp(direction.y * scale, -halfHeight, halfHeight));
    }

    /// <summary>
    /// Slides <paramref name="point"/> along the border until it is out of GD §12.4's two thumb
    /// corners, or leaves it where it is when it was never in one.
    /// </summary>
    /// <remarks>
    /// Done by walking the perimeter rather than by pushing the point out of a box, because the
    /// point has to stay <em>on the border</em>: a shortest-distance escape from a corner box would
    /// move it inwards, and an arrow floating in the middle of the screen says nothing at all. The
    /// perimeter is measured clockwise from the middle of the bottom edge, which is the one point
    /// between the two exclusions — so neither of them wraps around the seam and both are a single
    /// interval.
    /// </remarks>
    private static Vector2 OutsideTheThumbs(Vector2 point, float halfWidth, float halfHeight)
    {
        float width = halfWidth * 2f;
        float height = halfHeight * 2f;

        if (width <= 0f || height <= 0f)
        {
            return point;
        }

        float thumbWidth = width * ThumbFraction;
        float thumbHeight = height * ThumbFraction;

        float perimeter = (width + height) * 2f;

        // Bottom-right: the last stretch of the bottom edge, then the first of the right edge.
        float rightStart = halfWidth - thumbWidth;
        float rightEnd = halfWidth + thumbHeight;

        // Bottom-left: the last stretch of the left edge, then the first of the bottom edge back to
        // the seam.
        float leftStart = perimeter - halfWidth - thumbHeight;
        float leftEnd = perimeter - halfWidth + thumbWidth;

        float t = Perimeter(point, halfWidth, halfHeight);

        if (t > rightStart && t < rightEnd)
        {
            t = t - rightStart < rightEnd - t ? rightStart : rightEnd;
        }
        else if (t > leftStart && t < leftEnd)
        {
            t = t - leftStart < leftEnd - t ? leftStart : leftEnd;
        }
        else
        {
            return point;
        }

        return PointAt(t, halfWidth, halfHeight);
    }

    /// <summary>
    /// How far clockwise round the border <paramref name="point"/> lies, from the middle of the
    /// bottom edge.
    /// </summary>
    private static float Perimeter(Vector2 point, float halfWidth, float halfHeight)
    {
        float height = halfHeight * 2f;
        float width = halfWidth * 2f;

        // Bottom edge, right half: the seam itself.
        if (point.y <= -halfHeight && point.x >= 0f)
        {
            return point.x;
        }

        // Right edge, going up.
        if (point.x >= halfWidth)
        {
            return halfWidth + (point.y + halfHeight);
        }

        // Top edge, going left.
        if (point.y >= halfHeight)
        {
            return halfWidth + height + (halfWidth - point.x);
        }

        // Left edge, going down.
        if (point.x <= -halfWidth)
        {
            return halfWidth + height + width + (halfHeight - point.y);
        }

        // Bottom edge, left half, back to the seam.
        return halfWidth + (height * 2f) + width + (point.x + halfWidth);
    }

    /// <summary>The inverse of <see cref="Perimeter"/>.</summary>
    private static Vector2 PointAt(float t, float halfWidth, float halfHeight)
    {
        float height = halfHeight * 2f;
        float width = halfWidth * 2f;

        if (t <= halfWidth)
        {
            return new Vector2(t, -halfHeight);
        }

        t -= halfWidth;

        if (t <= height)
        {
            return new Vector2(halfWidth, -halfHeight + t);
        }

        t -= height;

        if (t <= width)
        {
            return new Vector2(halfWidth - t, halfHeight);
        }

        t -= width;

        if (t <= height)
        {
            return new Vector2(-halfWidth, halfHeight - t);
        }

        t -= height;

        return new Vector2(-halfWidth + t, -halfHeight);
    }

    /// <summary>XZ, because every distance in this game is XZ (AR §18.4).</summary>
    private static float DistanceXZ(Vector3 from, Vector3 to)
    {
        float dx = to.x - from.x;
        float dz = to.z - from.z;

        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }

    private static bool IsFinite(Vector2 value)
    {
        return !float.IsNaN(value.x)
            && !float.IsNaN(value.y)
            && !float.IsInfinity(value.x)
            && !float.IsInfinity(value.y);
    }

    private static void Hide(Arrow arrow)
    {
        if (arrow.Transform != null && arrow.Transform.gameObject.activeSelf)
        {
            arrow.Transform.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// The <paramref name="index"/>th arrow, building one more if this frame wants more than exist.
    /// </summary>
    /// <remarks>
    /// It grows once and keeps what it grew, so a run that momentarily shows nine arrows pays for
    /// the ninth exactly once. The prewarm is what makes that path cold in practice.
    /// </remarks>
    private Arrow ArrowAt(int index)
    {
        while (_arrows.Count <= index)
        {
            _arrows.Add(Build());
        }

        return _arrows[index];
    }

    /// <remarks>
    /// The four-argument overload, and not the three-argument one, for <c>ViewPool.Create</c>'s
    /// reason: that one reparents the instance to null when the resolver's origin is a
    /// <c>LifetimeScope</c>, which would take every arrow out of the HUD and leave it drawn by
    /// nothing.
    /// </remarks>
    private Arrow Build()
    {
        Transform prefabTransform = _prefab.transform;

        RectTransform instance = _resolver.Instantiate(
            _prefab,
            prefabTransform.position,
            prefabTransform.rotation,
            _parent);

        instance.gameObject.SetActive(false);

        // Once, here, rather than per frame: a GetComponentsInChildren in LateUpdate would be the
        // "GetComponent in Update" the conventions ban, eight times a frame.
        return new Arrow(instance, instance.GetComponentsInChildren<Graphic>(includeInactive: true));
    }

    private void OnSpawned(EnemySpawned evt)
    {
        if (!_alive.Contains(evt.Id))
        {
            _alive.Add(evt.Id);
        }

        RecountSurvivors();
    }

    private void OnDied(EnemyDied evt) => Retire(evt.Id);

    private void OnDespawned(EnemyDespawned evt) => Retire(evt.Id);

    /// <remarks>
    /// Idempotent, because both events name the same id and arrive one after the other — a death
    /// takes the enemy off this list and the despawn that follows finds nothing to remove.
    /// </remarks>
    private void Retire(int id)
    {
        if (_alive.Remove(id))
        {
            RecountSurvivors();
        }
    }

    /// <summary>
    /// Starts, holds or clears GD §7.3's eight-second clock.
    /// </summary>
    /// <remarks>
    /// The clock starts when the population <em>enters</em> the band and is not restarted by a kill
    /// inside it: three survivors becoming two is the player making progress on exactly the problem
    /// the rule exists to end, and restarting the wait for it would punish them for it. It is
    /// cleared by the population leaving the band in either direction — a wave arriving, or the
    /// arena emptying.
    /// </remarks>
    private void RecountSurvivors()
    {
        if (_alive.Count > 0 && _alive.Count <= StallSurvivors)
        {
            if (_stallSince < 0f)
            {
                _stallSince = _clock();
            }

            return;
        }

        _stallSince = NotStalling;
    }

    private void OnTargetChanged(TargetChanged evt) => _heldFocusId = evt.HeldFocusId;

    /// <summary>One arrow and the graphics on it, resolved once at construction.</summary>
    private readonly struct Arrow
    {
        public readonly RectTransform Transform;
        public readonly Graphic[] Graphics;

        public Arrow(RectTransform transform, Graphic[] graphics)
        {
            Transform = transform;
            Graphics = graphics;
        }
    }
}

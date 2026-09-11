using Soulvail.Core.Run;
using Soulvail.Game.Views;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The senses. Once a frame it writes down everything core cannot know for itself — the stick,
/// where the body ended up, how much time passed — into the one <see cref="WorldSnapshot"/> the
/// run owns. See AR §4.2 and §4.3.
/// </summary>
/// <remarks>
/// <para>
/// It reports and never interprets. Every number here is a fact Unity is the authority on;
/// the moment one of them is derived from a rule rather than measured, that rule has escaped
/// core. The one transformation it does own is the mapping from stick axes to world axes,
/// because which way is "up" on screen is a camera question and the camera is Unity's.
/// </para>
/// <para>
/// Enemies arrive the same way as of M1-07, through <see cref="EnemyViews.CopyInto"/>: one
/// <c>EnemySense</c> per body standing in the scene, written after the player's fields so the
/// single <see cref="WorldSnapshot.Clear"/> that opens the frame is unambiguously this method's.
/// </para>
/// <para>
/// Pathfinding joins them in M1-19, and it is here rather than in <see cref="EnemyViews"/> because
/// of what it needs: a route is measured from an enemy <em>to the player</em>, and the census knows
/// only where its own bodies are. This class already holds both ends of that line.
/// </para>
/// </remarks>
public sealed class SnapshotBuilder
{
    /// <summary>
    /// The longest step core is ever handed, in seconds — 50 ms, or 20 fps.
    /// </summary>
    /// <remarks>
    /// A hitch, a scene load, a breakpoint or a phone waking from sleep can hand
    /// <c>Time.deltaTime</c> a third of a second, and an unclamped simulation would advance the
    /// player a metre and a half through whatever was in front of them in a single
    /// <c>CharacterController.Move</c>. Clamping makes the game briefly run in slow motion
    /// instead, which is the failure mode nobody notices. It is applied here rather than in core
    /// because the frame's length is an engine fact, and core is entitled to trust its snapshot.
    /// </remarks>
    public const float MaxDt = 1f / 20f;

    private readonly PlayerView _player;
    private readonly InputAdapter _input;
    private readonly EnemyViews _enemies;
    private readonly NavPathSense _paths;

    /// <summary>
    /// The door out of this arena, or null for an arena dressed without one.
    /// </summary>
    /// <remarks>
    /// A <c>Transform</c> rather than a cached position, so an arena that moves its gate — a door
    /// that slides open, an arena prefab positioned at runtime by M2-11a — is answered on the frame
    /// it moves it. It is read every frame for the price of one <c>transform.position</c>, which is
    /// the same price the player's own position already costs.
    /// </remarks>
    private readonly Transform _gate;

    /// <summary>
    /// Simulated seconds since the run's first frame — the sum of the clamped <c>Dt</c> this class
    /// hands core, which is the same number <c>RunState.Time</c> arrives at from the other side.
    /// </summary>
    /// <remarks>
    /// Kept here rather than read from <c>Time.time</c>, and it is the one clock in this file. The
    /// path cache measures its cadence against it, so it has to be the clock that stops when the
    /// game is paused and that advances in the same clamped steps a hitching frame gives core —
    /// otherwise a scene load would expire every cached route at once, on the frame least able to
    /// afford recomputing them.
    /// </remarks>
    private float _elapsed;

    /// <param name="player">The body, asked where it is and what it was told to do.</param>
    /// <param name="input">The one reader of the Input System (M0-14).</param>
    /// <param name="enemies">
    /// Every enemy body in the scene, asked the same two questions (M1-07). Taken as a
    /// dependency rather than found, so the builder never searches a scene and the run's census
    /// has exactly one owner.
    /// </param>
    /// <param name="paths">
    /// The NavMesh, asked which way each enemy should walk (M1-19). A dependency like the rest, so
    /// a headless test can build a frame without one — see <see cref="Build"/>.
    /// </param>
    /// <param name="gate">
    /// The door out of the arena, or null for one dressed without a door (M2-10). Null is a real
    /// answer rather than a missing one, for the reason a null <paramref name="paths"/> is: the M0
    /// grey box has no gate, and a run in it is a workflow rather than a fault — core reads
    /// <c>HasGate</c> false and parks its stage flow at the door it has not got.
    /// </param>
    public SnapshotBuilder(
        PlayerView player,
        InputAdapter input,
        EnemyViews enemies,
        NavPathSense paths,
        Transform gate)
    {
        _player = player;
        _input = input;
        _enemies = enemies;
        _paths = paths;
        _gate = gate;
    }

    /// <summary>
    /// Refills <paramref name="snapshot"/> in place for this frame.
    /// </summary>
    /// <param name="snapshot">The run's single snapshot instance. Cleared, then written.</param>
    /// <param name="dt">
    /// Seconds since the last frame, before clamping. The clamped value is what core receives and
    /// what the views must integrate with — read it back from <see cref="WorldSnapshot.Dt"/>
    /// rather than reusing this argument.
    /// </param>
    /// <remarks>
    /// No null guards and no allocations, for the same reason <c>RunSession.Tick</c> has neither:
    /// this runs 60 times a second against instances the run scope built once, so the only null
    /// possible is a mis-wired scope on the first frame — which fails immediately and
    /// unmissably — while a per-frame allocation would be a permanent drip into the GC.
    /// </remarks>
    public void Build(WorldSnapshot snapshot, float dt)
    {
        snapshot.Clear();

        snapshot.Dt = Mathf.Min(dt, MaxDt);

        _elapsed += snapshot.Dt;

        // Straight through, no rotation: the stick's X is world X and its Y is world Z. The M0
        // camera has yaw 0, so this is camera-relative by construction. When the camera can be
        // turned, the rotation goes *here* — core is handed a world-space stick and must never
        // learn that a camera exists.
        snapshot.MoveInput = _input.Move.ToNum();

        // Written down, not decided. Unity resolved the collision; this is where the body actually
        // ended up, which core records and never overrides.
        snapshot.PlayerPosition = _player.Position.ToNum();
        snapshot.PlayerVelocity = _player.Velocity.ToNum();

        // Reported every frame rather than once at composition, and the `!= null` is Unity's
        // lifetime check rather than C#'s: a gate destroyed mid-run is a null the operator catches
        // and a plain reference comparison does not. An arena with no door says so, and core parks
        // its stage flow rather than throwing (M2-10 rule 15).
        snapshot.HasGate = _gate != null;

        if (snapshot.HasGate)
        {
            snapshot.GatePosition = _gate.position.ToNum();
        }

        // Last, and after the Clear above rather than owning one of its own: the enemies are the
        // only variable-length part of the frame, and the count they leave behind is what core
        // reads to know how many of the array's slots are this frame's (AR §4.2).
        _enemies.CopyInto(snapshot);

        WritePathDirections(snapshot);
    }

    /// <summary>
    /// Fills every enemy slot's <c>PathDirectionToPlayer</c> from the NavMesh.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second pass over the slots <see cref="EnemyViews.CopyInto"/> has just written, rather than
    /// a line inside it, because the two answers come from different places: the census knows where
    /// its bodies are, and only this class also knows where the player is. It reads the position
    /// back out of the slot instead of asking the view again, so the route is measured from exactly
    /// the position core is about to be told about — the alternative would be a direction computed
    /// from one place and a position reported from another.
    /// </para>
    /// <para>
    /// A null <c>NavPathSense</c> leaves the zero <c>CopyInto</c> already wrote, which core reads as
    /// "no path" and every behaviour answers with the straight line. That is not a convenience for
    /// tests: an arena whose NavMesh has not been baked should still be playable, and it was, for
    /// the whole of M1 before this.
    /// </para>
    /// <para>
    /// Allocates nothing: a <c>ref</c> into the snapshot's array, two struct conversions per enemy,
    /// and a cache lookup that only occasionally becomes a path search.
    /// </para>
    /// </remarks>
    private void WritePathDirections(WorldSnapshot snapshot)
    {
        if (_paths is null)
        {
            return;
        }

        Vector3 player = snapshot.PlayerPosition.ToUnity();

        for (int i = 0; i < snapshot.EnemyCount; i++)
        {
            ref EnemySense sense = ref snapshot.Enemies[i];

            sense.PathDirectionToPlayer = _paths
                .DirectionFor(sense.Id, sense.Position.ToUnity(), player, _elapsed)
                .ToNum();
        }
    }
}

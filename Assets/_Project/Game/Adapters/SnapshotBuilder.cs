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
/// Enemies land here in M1-06. Until then <see cref="WorldSnapshot.EnemyCount"/> stays at zero,
/// which <see cref="WorldSnapshot.Clear"/> already guarantees — there is nothing to add and
/// nothing to skip.
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

    /// <param name="player">The body, asked where it is and what it was told to do.</param>
    /// <param name="input">The one reader of the Input System (M0-14).</param>
    public SnapshotBuilder(PlayerView player, InputAdapter input)
    {
        _player = player;
        _input = input;
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

        // Straight through, no rotation: the stick's X is world X and its Y is world Z. The M0
        // camera has yaw 0, so this is camera-relative by construction. When the camera can be
        // turned, the rotation goes *here* — core is handed a world-space stick and must never
        // learn that a camera exists.
        snapshot.MoveInput = _input.Move.ToNum();

        // Written down, not decided. Unity resolved the collision; this is where the body actually
        // ended up, which core records and never overrides.
        snapshot.PlayerPosition = _player.Position.ToNum();
        snapshot.PlayerVelocity = _player.Velocity.ToNum();
    }
}

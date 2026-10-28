using UnityEngine;

namespace Soulvail.Game.Views;

/// <summary>
/// The weight of a body's flinch layer, set from the layer's own state: 0 between flinches, 1 while
/// one plays, and the crossfade's progress into or out of it. RS-09a, JG-09.
/// </summary>
/// <remarks>
/// <b>Why a view owns this weight.</b> Every flinch layer overrides part of the body and its states do
/// not write defaults (which would snap the masked bones to the bind pose). At weight 1 such a layer's
/// empty state does not hand the bones back: it holds the last pose the flinch wrote — measured as the
/// Ranger's arms 76–128° off every later shot, and a Rootling 44° off its idle. At weight 0 between
/// flinches it holds nothing, so the layer rests at 0 in every controller and a view raises it.
/// </remarks>
public static class FlinchLayer
{
    /// <summary>The flinch layer's name in every controller that has one.</summary>
    public const string Name = "Flinch";

    /// <summary>The layer's weight for its state.</summary>
    /// <param name="inHit">The layer's current state is <c>Hit</c>.</param>
    /// <param name="toHit">The layer is crossfading into <c>Hit</c>.</param>
    /// <param name="inTransition">The layer is crossfading.</param>
    /// <param name="transitionTime">The crossfade's normalised time, clamped to [0, 1]. Non-finite reads as 0.</param>
    public static float Weight(bool inHit, bool toHit, bool inTransition, float transitionTime)
    {
        float t = float.IsNaN(transitionTime) || float.IsInfinity(transitionTime) ? 0f : Mathf.Clamp01(transitionTime);

        if (inTransition)
        {
            if (toHit)
            {
                return inHit ? 1f : t;
            }

            if (inHit)
            {
                return 1f - t;
            }
        }

        return inHit ? 1f : 0f;
    }

    /// <summary>
    /// Writes <see cref="Weight"/> for the state <paramref name="animator"/> reached on its last
    /// evaluation, applied on its next.
    /// </summary>
    /// <remarks>
    /// So a flinch fades in over its layer's own crossfade, one frame late — too short to see. A
    /// negative <paramref name="layer"/> (a controller without the layer), a null or uninitialised
    /// Animator does nothing.
    /// </remarks>
    /// <param name="animator">The body's Animator.</param>
    /// <param name="layer">The flinch layer's index, from <c>GetLayerIndex(Name)</c>.</param>
    /// <param name="hitStateId">The short-name hash of the layer's <c>Hit</c> state.</param>
    public static void Drive(Animator animator, int layer, int hitStateId)
    {
        if (layer < 0 || animator == null || !animator.isInitialized)
        {
            return;
        }

        bool inTransition = animator.IsInTransition(layer);
        bool inHit = animator.GetCurrentAnimatorStateInfo(layer).shortNameHash == hitStateId;
        bool toHit = inTransition && animator.GetNextAnimatorStateInfo(layer).shortNameHash == hitStateId;
        float time = inTransition ? animator.GetAnimatorTransitionInfo(layer).normalizedTime : 0f;

        animator.SetLayerWeight(layer, Weight(inHit, toHit, inTransition, time));
    }
}

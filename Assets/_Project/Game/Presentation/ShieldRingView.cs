using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The Aegis, as a ring beside the health bar: full when the shield is up, empty while it is
    /// spent, sweeping back round as CC §7's 15 points a second refill it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A ring rather than a second bar</b>, because the two numbers are read for different
    /// reasons and must never be confused for one another. HP is the run; the Aegis is this fight,
    /// and it comes back on its own. A stacked bar would invite the player to add them up.
    /// </para>
    /// <para>
    /// <b>No delay and no trail</b>, unlike <see cref="HpBarView"/>. A shield that has just been
    /// spent is information about right now — the next hit reaches HP — and a ghost describing the
    /// shield the player used to have would be the opposite of urgent.
    /// </para>
    /// <para>
    /// It holds no shield. Every number arrives through <see cref="Set"/>, from the presenter that
    /// listens to core's events.
    /// </para>
    /// </remarks>
    public sealed class ShieldRingView : MonoBehaviour
    {
        [Tooltip("The ring itself. Its Image must be Filled / Radial360 — fillAmount is the only " +
                 "thing written to it.")]
        [SerializeField] private Image _fill;

        /// <summary>
        /// The fraction as last written, so a frame that changed nothing costs no canvas rebuild.
        /// Seeded outside <c>[0, 1]</c> so the first <see cref="Set"/> always draws.
        /// </summary>
        private float _shown = -1f;

        /// <summary>
        /// Draws <paramref name="fraction"/> of the Aegis.
        /// </summary>
        /// <param name="fraction">
        /// Shield points over the shield's maximum, in <c>[0, 1]</c>; zero for a class that has no
        /// shield at all. Clamped rather than trusted, for the reason <see cref="HpBarView.Set"/>
        /// clamps: uGUI draws an out-of-range <c>fillAmount</c> as a full or an empty ring rather
        /// than reporting it.
        /// </param>
        public void Set(float fraction)
        {
            float clamped = Mathf.Clamp01(fraction);

            // Not merely an optimisation: assigning fillAmount marks the graphic dirty and queues a
            // canvas rebuild. The refill publishes about twenty-five times over its two seconds
            // (see PlayerShieldChanged), so this is the difference between twenty-five rebuilds and
            // one per event that actually moved the ring.
            if (Mathf.Approximately(clamped, _shown))
            {
                return;
            }

            _shown = clamped;

            // Unity's ==, and a silent return: Set can be called before Start has run — core
            // publishes RunStarted from an entry point, and Unity gives no order against a
            // component's own Start — so an undressed reference is reported by the guard in Start
            // rather than by a null on the first event of the run.
            if (_fill == null)
            {
                return;
            }

            _fill.fillAmount = clamped;
        }

        /// <exception cref="MissingReferenceException">The ring is not dressed.</exception>
        /// <remarks>
        /// Checked in <c>Start</c> rather than <c>Awake</c> for the reason every other view in this
        /// project gives: <c>RunScope</c> builds its container from its own <c>Awake</c>, and Unity
        /// gives no order between two of those.
        /// </remarks>
        private void Start()
        {
            if (_fill == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(ShieldRingView)} has no fill assigned. Drag the Ring child onto its " +
                    "Fill field — without it the Aegis is 30 points of survivability the player " +
                    "has no way of knowing they have.");
            }
        }
    }
}

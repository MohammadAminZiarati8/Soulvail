using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and EnemyShowcase.unity's references to this component
// would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Sandbox
{
    /// <summary>
    /// Starts a showcase body in one named state of its controller, which loops it
    /// (<c>EnemyShowcase.unity</c>).
    /// </summary>
    /// <remarks>
    /// An Animator's entry state belongs to its controller, not to the instance, so six frogs sharing
    /// <c>AC_Frog_Showcase</c> would all play its default state. This names the state each one starts
    /// in; every state in a showcase controller transitions back to itself, so it loops from there.
    /// Showcase scenes only: a run's bodies are driven by <c>EnemyAnimatorView</c>.
    /// </remarks>
    [RequireComponent(typeof(Animator))]
    public sealed class ShowcaseClip : MonoBehaviour
    {
        [Tooltip("The state in this body's showcase controller to play and loop — the clip's name.")]
        [SerializeField] private string _state = "Idle";

        /// <summary>The state this body starts in.</summary>
        public string State => _state;

        private void Start()
        {
            var animator = GetComponent<Animator>();
            if (animator.runtimeAnimatorController != null)
            {
                animator.Play(_state, 0, 0f);
            }
        }
    }
}

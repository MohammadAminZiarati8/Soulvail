using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Unity.AI.Navigation;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, so every arena prefab's reference to this component
// would silently deserialise as null, with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Arena
{
    /// <summary>
    /// One hand-built arena (GD §7.2). It decides nothing and ticks nothing: it is a set of named
    /// places, a barrier, a door, and the NavMesh those places are true about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing here has an <c>Update</c>, nothing subscribes and nothing decides.</b>
    /// <see cref="ArenaPool"/> reads it when a stage arrives, and core is told the two facts it
    /// needs — the door and the spawn points — through the snapshot, the same way it is told where
    /// the player is. An arena that answered core directly would be a second brain in a room.
    /// </para>
    /// <para>
    /// <b>It validates itself against GD §7.2, in the Editor, as a warning.</b> The spec asks for an
    /// error; a warning is what the three <c>*Definition</c> ScriptableObjects already use for
    /// exactly this job, and an error here would fail every test that builds an arena fixture —
    /// Unity's test framework treats an unexpected <c>LogError</c> as a failure. The hard assertions
    /// are <c>ArenaViewTests</c>, which run the same rules against the shipped prefabs; this is the
    /// copy a designer sees while dragging a pillar.
    /// </para>
    /// <para>
    /// <b>The NavMesh is the surface's own business.</b> <see cref="NavMeshSurface"/> adds its baked
    /// data when it is enabled and removes it when it is disabled, so raising and parking an arena
    /// is all the wiring rule 7 needs — and no arena ever bakes at runtime, which on a phone would
    /// be a multi-frame hitch behind a 0.3 s fade.
    /// </para>
    /// </remarks>
    public sealed class ArenaView : MonoBehaviour
    {
        /// <summary>The layer every cover pillar sits on — <see href="../../../../Docs/plan/ROADMAP.md">ledger row 13</see>.</summary>
        /// <remarks>
        /// A layer rather than a tag or a component because the consumer is a <c>Physics.Raycast</c>
        /// mask (M2-11b), and a mask is the one form of that question that costs nothing per call.
        /// The name is here rather than a serialized <c>LayerMask</c> because this class only ever
        /// <em>counts</em> pillars for validation — the mask that will actually be raycast against is
        /// authored on the scope, where a renamed layer is a visible diff (M1-12's rule for the
        /// enemy mask).
        /// </remarks>
        public const string CoverLayerName = "Cover";

        /// <summary>Fewest spawn points an arena may author (rule 2).</summary>
        public const int MinSpawnPoints = 3;

        /// <summary>GD §7.2's cover band: 3–6 pillars, placed off-centre.</summary>
        public const int MinCoverPillars = 3;

        /// <inheritdoc cref="MinCoverPillars" />
        public const int MaxCoverPillars = 6;

        [Tooltip("This arena's content id, e.g. 'arena.pillars'. It must match the mode's roster " +
                 "and the asset's own name — Arena_Pillars.prefab ↔ arena.pillars.")]
        [SerializeField] private string _id = "arena.new";

        [Tooltip("Where the player stands when this arena is raised. Every spawn point owes it " +
                 "SpawnDirector.MinPlayerDistance of clearance (GD §12.4).")]
        [SerializeField] private Transform _playerStart;

        [Tooltip("The door out. What core tests the player's distance against — an arena without " +
                 "one is playable and cannot be left, which parks the stage flow at its gate.")]
        [SerializeField] private Transform _gate;

        [Tooltip("Where the director may put a body. At least three, none within 6 m of the " +
                 "player start.")]
        [SerializeField] private Transform[] _spawnPoints = Array.Empty<Transform>();

        [Tooltip("Shown while the arena is sealed: the wall across the door for the length of the " +
                 "fight. Optional — an arena without one still seals as far as core is concerned.")]
        [SerializeField] private GameObject _barrier;

        [Tooltip("Shown once the stage is cleared: the door the player walks into. Optional on " +
                 "the same terms as the barrier.")]
        [SerializeField] private GameObject _door;

        [Tooltip("This arena's baked NavMesh. Baked per prefab and never at runtime — a bake at a " +
                 "stage boundary is a multi-frame hitch behind a 0.3 s fade.")]
        [SerializeField] private NavMeshSurface _surface;

        /// <summary>
        /// The world positions of <see cref="_spawnPoints"/>, refilled on read. Sized once.
        /// </summary>
        private Vector3[] _points = Array.Empty<Vector3>();

        private ReadOnlyCollection<Vector3> _pointsView = Array.AsReadOnly(Array.Empty<Vector3>());

        /// <summary>How many markers were non-null the last time the buffer was sized.</summary>
        private int _pointCount = -1;

        private ContentId _parsedId;

        /// <summary>
        /// The exact string <see cref="_parsedId"/> was parsed from, compared by reference. Unity
        /// hands back the same instance until the field is reassigned, so this caches the parse
        /// without ever going stale.
        /// </summary>
        private string _parsedFrom;

        /// <summary>This arena's content id, e.g. <c>arena.pillars</c>.</summary>
        /// <exception cref="ArgumentException">
        /// The authored text is not a valid <see cref="ContentId"/>. Loud rather than silent: an
        /// arena whose id does not parse can never be matched to a mode's roster, and the failure
        /// would otherwise surface as "no prefab carries arena.pillars" while the prefab is right
        /// there.
        /// </exception>
        public ContentId Id
        {
            get
            {
                if (!ReferenceEquals(_parsedFrom, _id))
                {
                    _parsedId = new ContentId(_id);
                    _parsedFrom = _id;
                }

                return _parsedId;
            }
        }

        /// <summary>Where the player stands when this arena is raised, in world metres.</summary>
        /// <remarks>The arena's own origin when no marker is dressed, which keeps an undressed
        /// prefab playable rather than teleporting the player to nowhere.</remarks>
        public Vector3 PlayerStart => _playerStart == null ? transform.position : _playerStart.position;

        /// <summary>Where the door out is — what core tests the player's distance against.</summary>
        public Vector3 GatePosition => _gate == null ? transform.position : _gate.position;

        /// <summary>Whether this arena has a door at all.</summary>
        /// <remarks>
        /// False parks <c>StageFlow</c> at its gate rather than throwing (M2-10 rule 15), which is
        /// what every grey box was and what a scene dressed for one experiment still wants.
        /// </remarks>
        public bool HasGate => _gate != null;

        /// <summary>
        /// Where the director may put a body, in world metres, in the order they were authored.
        /// </summary>
        /// <remarks>
        /// <b>Read from the markers on every call, into a buffer sized once.</b> An arena's points
        /// do not move, so a cache would be correct — but it would also be a copy that a reparented
        /// or repositioned arena could silently disagree with, and the read costs one
        /// <c>transform.position</c> per point, once a stage. Unassigned elements are skipped, so a
        /// hole in the array shows up as a short list that validation refuses rather than as a
        /// <c>NullReferenceException</c> at the first wave.
        /// </remarks>
        public IReadOnlyList<Vector3> SpawnPoints
        {
            get
            {
                int count = CountMarkers();

                if (count != _pointCount)
                {
                    _points = new Vector3[count];
                    _pointsView = Array.AsReadOnly(_points);
                    _pointCount = count;
                }

                int written = 0;

                for (int i = 0; i < _spawnPoints.Length; i++)
                {
                    if (_spawnPoints[i] == null)
                    {
                        continue;
                    }

                    _points[written++] = _spawnPoints[i].position;
                }

                return _pointsView;
            }
        }

        /// <summary>How many cover pillars this arena has on the <see cref="CoverLayerName"/> layer.</summary>
        /// <remarks>
        /// Counted by layer rather than from an authored list, and that is the whole point: a list
        /// can disagree with the scene, while the layer is the same fact M2-11b's raycast mask will
        /// read. A pillar that was never put on the layer is therefore invisible here too — which
        /// is exactly the failure worth reporting.
        /// </remarks>
        public int CoverCount
        {
            get
            {
                int layer = LayerMask.NameToLayer(CoverLayerName);

                if (layer < 0)
                {
                    return 0;
                }

                int count = 0;

                // GetComponentsInChildren would allocate; an arena is a shallow hierarchy and this
                // runs in validation and in tests, never in a frame.
                CountCover(transform, layer, ref count);

                return count;
            }
        }

        /// <summary>This arena's baked NavMesh, or null for one that was never baked.</summary>
        public NavMeshSurface Surface => _surface;

        /// <summary>Barrier up, door shut. Called when a stage arrives (M2-11a rule 5).</summary>
        public void Seal()
        {
            if (_barrier != null)
            {
                _barrier.SetActive(true);
            }

            if (_door != null)
            {
                _door.SetActive(false);
            }
        }

        /// <summary>Barrier down, door open. Called when a stage is cleared.</summary>
        public void Open()
        {
            if (_barrier != null)
            {
                _barrier.SetActive(false);
            }

            if (_door != null)
            {
                _door.SetActive(true);
            }
        }

        /// <summary>
        /// Every way this arena breaks GD §7.2, as one string, or null when it breaks none.
        /// </summary>
        /// <remarks>
        /// One method for both readers — <see cref="OnValidate"/> and <c>ArenaViewTests</c> — so
        /// the rule a designer is warned about and the rule the suite asserts cannot drift apart.
        /// It allocates freely: it runs in the Editor, against an asset, never in a run.
        /// </remarks>
        public string DescribeFaults()
        {
            var faults = new List<string>();

            if (!ContentId.IsValid(_id))
            {
                faults.Add(
                    $"'{_id}' is not a valid content id — expected lowercase dot-separated "
                        + "segments, at least two, e.g. 'arena.pillars'");
            }

            if (_playerStart == null)
            {
                faults.Add("it has no player start, so the player would be left wherever the "
                           + "previous arena put them");
            }

            IReadOnlyList<Vector3> points = SpawnPoints;

            if (points.Count < MinSpawnPoints)
            {
                faults.Add(
                    $"it authors {points.Count} spawn point(s) and needs at least {MinSpawnPoints}");
            }

            Vector3 start = PlayerStart;

            for (int i = 0; i < points.Count; i++)
            {
                float dx = points[i].x - start.x;
                float dz = points[i].z - start.z;
                float distance = Mathf.Sqrt((dx * dx) + (dz * dz));

                if (distance < SpawnDirector.MinPlayerDistance)
                {
                    faults.Add(
                        $"spawn point {i} is {distance:F1} m from the player start on XZ, inside "
                            + $"GD §12.4's {SpawnDirector.MinPlayerDistance} m clearance — the "
                            + "director can never use it");
                }
            }

            int cover = CoverCount;

            if (cover < MinCoverPillars || cover > MaxCoverPillars)
            {
                faults.Add(
                    $"it has {cover} object(s) on the '{CoverLayerName}' layer and GD §7.2 wants "
                        + $"{MinCoverPillars}–{MaxCoverPillars} cover pillars");
            }

            if (_surface == null)
            {
                faults.Add("it has no NavMeshSurface, so nothing in it can path");
            }
            else if (_surface.navMeshData == null)
            {
                faults.Add("its NavMeshSurface has no baked data — bake it on the prefab; nothing "
                           + "bakes at runtime");
            }

            return faults.Count == 0 ? null : string.Join("; ", faults);
        }

        /// <summary>
        /// The number of dressed markers, which is what <see cref="SpawnPoints"/> is sized to.
        /// </summary>
        private int CountMarkers()
        {
            int count = 0;

            for (int i = 0; i < _spawnPoints.Length; i++)
            {
                if (_spawnPoints[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Whether not one of this arena's places has been dressed yet.</summary>
        private bool IsUndressed() =>
            _playerStart == null
            && _gate == null
            && _surface == null
            && _barrier == null
            && _door == null
            && (_spawnPoints is null || _spawnPoints.Length == 0);

        private static void CountCover(Transform root, int layer, ref int count)
        {
            foreach (Transform child in root)
            {
                if (child.gameObject.layer == layer)
                {
                    count++;
                }

                CountCover(child, layer, ref count);
            }
        }

        /// <remarks>
        /// The loud place for a hand-authored spec violation, and it is loud <em>here</em> rather
        /// than at the first stage of a run: an arena half its own waves cannot legally use is a
        /// mistake made with a mouse, and the moment to hear about it is while the mouse is still
        /// in the designer's hand. The asset is passed as the log context, so clicking selects it.
        /// </remarks>
        private void OnValidate()
        {
            // An arena nobody has dressed yet is being authored rather than being broken — and the
            // first moment this runs is the frame the component is added, before a single field
            // could have been filled in. Reporting there would make adding the component to a new
            // prefab an error, and every fixture that builds one noisy.
            if (IsUndressed())
            {
                return;
            }

            string faults = DescribeFaults();

            if (faults is null)
            {
                return;
            }

            Debug.LogWarning($"ArenaView '{name}': {faults}.", this);
        }
    }
}

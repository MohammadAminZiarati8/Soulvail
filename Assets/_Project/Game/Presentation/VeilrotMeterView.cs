using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CoreVeilrot = Soulvail.Core.Run.Veilrot;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// GD §10's meter, on screen: a vertical fill down the right edge, four marks where the
    /// thresholds are, and one more mark once the Claiming has begun.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A view, and it subscribes to nothing</b> (M6-03b rule 1). <c>HudPresenter</c> holds the
    /// subscriptions and calls <see cref="Set"/> — <c>HpBarView</c>'s and <c>BossBarView</c>'s
    /// bargain, for their reason: a prefab-dressed view that reached for the hub would need injecting
    /// individually, and <c>HudPresenter</c> is the one thing on this prefab <c>RunScope</c> registers.
    /// </para>
    /// <para>
    /// <b>The fill is the meter's value and the Claiming is a mark, because at 100 the two stop
    /// agreeing</b> (rule 4). A run that reaches 100 and then buys Cleanse sits at 40 and is still
    /// Claimed (M6-04 rule 6). Drawn as a full bar, the Claiming would show 100 over a meter at 40;
    /// dropped with the value, it would tell the player the gamble was refundable. So the fill follows
    /// the value down and the mark never comes off — <see cref="IsClaimed"/> is a latch here exactly
    /// as <c>RunState.IsClaimed</c> is one in core.
    /// </para>
    /// <para>
    /// <b>Every colour is <see cref="Palette.Veilrot"/> or <see cref="Palette.Neutral"/>, and never
    /// <see cref="Palette.Danger"/></b> (rule 5). The Claiming is the most dangerous state the game
    /// has, which is exactly why the reserved colour tempts here, and GD §16.4 says <em>"for nothing
    /// else, ever"</em>; <c>BossBarView</c> made the same call for a band that is up for a fight, and
    /// this one is up for a run. The two alphas are feel numbers, which the palette's own remarks
    /// permit a view to hold.
    /// </para>
    /// <para>
    /// <b>The thresholds are asked of core one at a time</b> (rule 3): <c>Veilrot.ThresholdCount</c>
    /// and <c>Veilrot.Threshold(int)</c> hand out floats by value, where M6-04's drafted
    /// <c>static readonly float[]</c> would have been a handle any caller could write through.
    /// </para>
    /// </remarks>
    public sealed class VeilrotMeterView : MonoBehaviour
    {
        [Tooltip("The meter's empty length, drawn in Palette.Veilrot at the track alpha below.")]
        [SerializeField] private Image _track;

        [Tooltip("The fill. Its Image must be Filled / Vertical / Bottom — fillAmount is the only " +
                 "number written to it.")]
        [SerializeField] private Image _fill;

        [Tooltip("One threshold mark, inactive. Cloned once per GD §10.2 row and anchored at its " +
                 "fraction of the meter's height.")]
        [SerializeField] private Image _mark;

        [Tooltip("The Claiming's mark — up from the moment the meter first reaches 100, and never " +
                 "down again (rule 4).")]
        [SerializeField] private Image _claimedMark;

        [Tooltip("How opaque the track is against the fill. A feel number, not a colour.")]
        [Range(0f, 1f)]
        [SerializeField] private float _trackAlpha = 0.25f;

        [Tooltip("How thick a threshold mark is, in dp.")]
        [SerializeField] private float _markThicknessDp = 2f;

        /// <summary>The clones of <see cref="_mark"/>, one per threshold, built once.</summary>
        private readonly List<Image> _marks = new List<Image>(4);

        private float _fraction;
        private bool _isClaimed;

        /// <summary>The meter, 0–1. What a test reads instead of a pixel.</summary>
        public float Fill => _fraction;

        /// <summary>Whether the Claiming mark is up — <c>RunState.IsClaimed</c>, drawn.</summary>
        public bool IsClaimed => _isClaimed;

        /// <summary>How many threshold marks are placed. Four once <see cref="PlaceThresholds"/> ran.</summary>
        public int MarkCount => _marks.Count;

        /// <summary>Where mark <paramref name="index"/> sits, as a fraction of the meter's height.</summary>
        public float MarkFraction(int index) => ((RectTransform)_marks[index].transform).anchorMin.y;

        /// <summary>
        /// Draws <paramref name="value"/> of <c>Veilrot.Max</c>, with or without the Claiming.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A non-finite value leaves the meter where it was, <c>HpBarView.Set</c>'s bargain: a NaN
        /// reaching a <c>fillAmount</c> is a graphic that never draws again. A finite value outside
        /// the range is clamped rather than refused — core cannot produce one, and 0 and 1 are the
        /// honest ends of the bar.
        /// </para>
        /// <para>
        /// <paramref name="isClaimed"/> can raise the mark and never lower it (rule 4).
        /// </para>
        /// </remarks>
        public void Set(float value, bool isClaimed)
        {
            if (isClaimed && !_isClaimed)
            {
                _isClaimed = true;
            }

            if (float.IsFinite(value))
            {
                _fraction = Mathf.Clamp01(value / CoreVeilrot.Max);
            }

            Paint();
        }

        /// <summary>Lays the four marks out from core's thresholds — rule 3.</summary>
        /// <param name="pixelsPerDp">Canvas units per dp, for the marks' thickness.</param>
        /// <remarks>
        /// Anchored fractionally, so 25 % of the meter is 25 % of it at every size the presenter lays
        /// it out at; only the thickness is dp. Cloned on the first call and re-placed on any later
        /// one, so a second layout pass never doubles the marks. A nonsense
        /// <paramref name="pixelsPerDp"/> or thickness keeps the template's authored height.
        /// </remarks>
        public void PlaceThresholds(float pixelsPerDp)
        {
            if (_mark == null)
            {
                return;
            }

            _mark.gameObject.SetActive(false);

            for (int i = 0; i < CoreVeilrot.ThresholdCount; i++)
            {
                if (i == _marks.Count)
                {
                    _marks.Add(Instantiate(_mark, _mark.transform.parent));
                }

                Image mark = _marks[i];
                var rect = (RectTransform)mark.transform;
                float at = CoreVeilrot.Threshold(i) / CoreVeilrot.Max;

                rect.anchorMin = new Vector2(0f, at);
                rect.anchorMax = new Vector2(1f, at);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;

                float thickness = _markThicknessDp * pixelsPerDp;

                rect.sizeDelta = new Vector2(
                    0f,
                    float.IsFinite(thickness) && thickness > 0f ? thickness : rect.sizeDelta.y);

                mark.gameObject.SetActive(true);
            }

            Paint();
        }

        /// <summary>Writes the fill, the mark and the four colours.</summary>
        /// <remarks>
        /// In code rather than dressed on the prefab, so the palette is the only place the colours
        /// live (M3-13a rule 8). <c>Graphic.color</c> skips an unchanged value, so repainting on every
        /// <see cref="Set"/> costs a comparison and no rebuild.
        /// </remarks>
        private void Paint()
        {
            if (_fill != null)
            {
                _fill.fillAmount = _fraction;
                _fill.color = Palette.Veilrot;
            }

            if (_track != null)
            {
                Color track = Palette.Veilrot;

                track.a = _trackAlpha;
                _track.color = track;
            }

            for (int i = 0; i < _marks.Count; i++)
            {
                _marks[i].color = Palette.Neutral;
            }

            if (_claimedMark != null)
            {
                _claimedMark.color = Palette.Veilrot;

                if (_claimedMark.gameObject.activeSelf != _isClaimed)
                {
                    _claimedMark.gameObject.SetActive(_isClaimed);
                }
            }
        }
    }
}

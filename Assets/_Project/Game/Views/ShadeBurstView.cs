using Soulvail.Game.Pooling;
using UnityEngine;

namespace Soulvail.Game.Views
{
    public sealed class ShadeBurstView : MonoBehaviour, IPoolable
    {
        private const float Seconds = 0.4f;
        private const int Segments = 64;

        [SerializeField] private LineRenderer _smoke;
        [SerializeField] private LineRenderer _edge;
        [SerializeField] private ParticleSystem _puff;
        [SerializeField] private Color _smokeTint = new(0.07f, 0.31f, 0.38f, 0.68f);
        [SerializeField] private Color _edgeTint = new(0.21f, 0.65f, 0.72f, 0.82f);
        [SerializeField] private Color _slowTint = new Color32(255, 253, 238, 235);

        private readonly Vector3[] _points = new Vector3[Segments];
        private Color _currentEdge;
        private float _remaining;

        public float Remaining => _remaining;

        public void OnSpawn()
        {
        }

        public void Flash(Vector3 centre, float radius, bool slows)
        {
            transform.position = centre + Vector3.up * GroundLift.Height;
            DrawCircle(_smoke, radius);
            DrawCircle(_edge, radius);
            _currentEdge = slows ? _slowTint : _edgeTint;
            _remaining = Seconds;
            FadeTo(1f);
            _puff.Play(false);
        }

        public bool Step(float dt)
        {
            _remaining = Mathf.Max(0f, _remaining - dt);
            FadeTo(_remaining / Seconds);
            return _remaining == 0f;
        }

        public void OnDespawn()
        {
            _puff.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            _remaining = 0f;
        }

        private void DrawCircle(LineRenderer line, float radius)
        {
            // Inset the stroke so its outer edge marks the reach.
            float centreline = Mathf.Max(0f, radius - line.widthMultiplier * 0.5f);
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * (2f * Mathf.PI / Segments);
                _points[i] = new Vector3(Mathf.Cos(angle) * centreline, Mathf.Sin(angle) * centreline, 0f);
            }

            line.positionCount = Segments;
            line.SetPositions(_points);
        }

        private void FadeTo(float opacity)
        {
            Color smoke = _smokeTint;
            smoke.a *= opacity;
            _smoke.startColor = smoke;
            _smoke.endColor = smoke;

            Color edge = _currentEdge;
            edge.a *= opacity;
            _edge.startColor = edge;
            _edge.endColor = edge;
        }
    }
}

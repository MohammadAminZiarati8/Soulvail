using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    public sealed class AxeView : MonoBehaviour
    {
        private const float SpinSpeed = 900f;
        private const float FlashSeconds = 0.1f;

        [SerializeField] private Transform _model;
        [SerializeField] private TrailRenderer _trail;

        private float _angle;
        private float _flash;
        private Vector3 _modelScale;

        public int Id { get; private set; }

        public void Show(int id, Vector3 position)
        {
            Id = id;
            _angle = 0f;
            _flash = 0f;
            _modelScale = _model.localScale;
            transform.position = position;
            _model.localRotation = Quaternion.identity;
            _trail.startColor = Palette.Player;
            Color tail = Palette.Player;
            tail.a = 0f;
            _trail.endColor = tail;
            gameObject.SetActive(true);
            _trail.Clear();
        }

        public void Turn()
        {
            _flash = FlashSeconds;
            _trail.startColor = Palette.Wind;
            _model.localScale = _modelScale * 1.3f;
        }

        public void Draw(Vector3 position, float dt)
        {
            transform.position = position;
            _angle = Mathf.Repeat(_angle + SpinSpeed * dt, 360f);
            _model.localRotation = Quaternion.Euler(0f, _angle, 0f);
            if (_flash > 0f)
            {
                _flash -= dt;
                if (_flash <= 0f)
                {
                    ResetFlash();
                }
            }
        }

        public void Hide()
        {
            Id = 0;
            gameObject.SetActive(false);
            ResetFlash();
            _trail.Clear();
        }

        private void ResetFlash()
        {
            _model.localScale = _modelScale;
            _trail.startColor = Palette.Player;
        }
    }
}

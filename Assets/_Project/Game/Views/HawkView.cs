using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    public sealed class HawkView : MonoBehaviour
    {
        private const float TravelSeconds = 0.45f;
        private const float BurstSeconds = 0.25f;
        private const float GroundLift = 0.035f;
        private static readonly int Flap = Animator.StringToHash("Base Layer.Flap");
        private static readonly int Dive = Animator.StringToHash("Base Layer.Dive");
        private static readonly int Strike = Animator.StringToHash("Base Layer.Strike");

        [SerializeField] private Animator _animator;
        [SerializeField] private SpriteRenderer _shadow;
        [SerializeField] private SpriteRenderer _burst;

        private IDisposable _arrived;
        private IDisposable _left;
        private IDisposable _dove;
        private IDisposable _struck;
        private Func<Vector3> _readPosition;
        private Camera _camera;
        private Vector3 _diveTarget;
        private Vector3 _burstPosition;
        private Vector3 _departureStart;
        private Vector3 _departureEnd;
        private float _travel;
        private float _burstAge;
        private float _groundY;
        private bool _entering;
        private bool _leaving;

        public void Bind(DomainEventHub hub, Func<Vector3> readPosition, Camera camera)
        {
            _readPosition = readPosition;
            _camera = camera;
            _arrived = hub.Subscribe<HawkArrived>(_ => Arrive());
            _left = hub.Subscribe<HawkLeft>(_ => Leave());
            _dove = hub.Subscribe<HawkDove>(OnDive);
            _struck = hub.Subscribe<HawkStruck>(_ => OnStrike());
        }

        public void Step(float dt)
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            Vector3 previous = _animator.transform.position;
            if (_leaving)
            {
                _travel += dt;
                if (_travel >= TravelSeconds)
                {
                    gameObject.SetActive(false);
                    return;
                }

                _animator.transform.position = Vector3.Lerp(_departureStart, _departureEnd, _travel / TravelSeconds);
            }
            else
            {
                transform.position = _readPosition();
                if (_entering)
                {
                    _animator.transform.position = Vector3.Lerp(Offscreen(transform.position, -0.2f),
                        transform.position, _travel / TravelSeconds);
                    _travel += dt;
                    _entering = _travel < TravelSeconds;
                }
                else
                {
                    _animator.transform.localPosition = Vector3.zero;
                }
            }

            Vector3 position = _animator.transform.position;
            Vector3 heading = position - previous;
            heading.y = 0f;
            if (heading.sqrMagnitude > 0.000001f)
            {
                _animator.transform.rotation = Quaternion.LookRotation(heading);
            }

            _shadow.transform.SetPositionAndRotation(new Vector3(position.x, _groundY + GroundLift, position.z),
                Quaternion.Euler(90f, 0f, 0f));

            if (_burst.enabled)
            {
                _burstAge += dt;
                DrawBurst();
            }
        }

        private void LateUpdate() => Step(Time.deltaTime);

        private void Arrive()
        {
            _entering = true;
            _leaving = false;
            _travel = 0f;
            _burst.enabled = false;
            _shadow.enabled = true;
            gameObject.SetActive(true);
            _animator.Play(Flap, 0, 0f);
        }

        private void Leave()
        {
            _leaving = true;
            _travel = 0f;
            _departureStart = _animator.transform.position;
            _departureEnd = Offscreen(_departureStart, 1.2f);
            _animator.Play(Flap, 0, 0f);
        }

        private void OnDive(HawkDove evt)
        {
            _entering = false;
            _diveTarget = evt.To.ToUnity();
            _groundY = _diveTarget.y;
            _animator.Play(Dive, 0, 0f);
        }

        private void OnStrike()
        {
            _animator.Play(Strike, 0, 0f);
            _burstPosition = _diveTarget + Vector3.up * 0.25f;
            _burstAge = 0f;
            DrawBurst();
        }

        private void DrawBurst()
        {
            float progress = Mathf.Clamp01(_burstAge / BurstSeconds);
            _burst.enabled = progress < 1f;
            Color colour = Palette.Player;
            colour.a = 1f - progress;
            _burst.color = colour;
            _burst.transform.SetPositionAndRotation(_burstPosition, _camera.transform.rotation);
            _burst.transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 1.1f, progress);
        }

        private Vector3 Offscreen(Vector3 anchor, float x)
        {
            Vector3 viewport = _camera.WorldToViewportPoint(anchor);
            viewport.x = x;
            return _camera.ViewportToWorldPoint(viewport);
        }

        private void OnDestroy()
        {
            _arrived?.Dispose();
            _left?.Dispose();
            _dove?.Dispose();
            _struck?.Dispose();
        }
    }
}

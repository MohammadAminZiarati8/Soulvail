using UnityEngine;
using UnityEngine.InputSystem;

namespace Soulvail
{
    /// <summary>
    /// Showcase-only driver for the Mage in MageShowcase.unity: moves the transform from the
    /// Move action and drives AC_Mage's Speed / IsWalking / Attack parameters. This is a scratch
    /// animation test rig, not gameplay — the real player is driven by core through intents.
    /// </summary>
    public class MageAnimator : MonoBehaviour
    {
        private const string MapName = "Player";
        private const string MoveActionName = "Move";
        private const string AttackActionName = "Attack";
        private const string SprintActionName = "Sprint";

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int IsWalkingParameter = Animator.StringToHash("IsWalking");
        private static readonly int AttackParameter = Animator.StringToHash("Attack");

        [SerializeField] private Animator _animator;
        [SerializeField] private InputActionAsset _actions;

        [Header("Movement")]
        [SerializeField] private float _walkSpeed = 2.7f;
        [SerializeField] private float _runSpeed = 5.4f;
        [SerializeField] private float _acceleration = 20f;
        [SerializeField] private float _turnDegreesPerSecond = 720f;

        private InputActionMap _map;
        private InputAction _move;
        private InputAction _attack;
        private InputAction _sprint;
        private float _speed;

        private void Awake()
        {
            if (_animator == null || _actions == null)
            {
                Debug.LogError($"{nameof(MageAnimator)}: assign both the Animator and the InputActionAsset.", this);
                enabled = false;
                return;
            }

            _map = _actions.FindActionMap(MapName, throwIfNotFound: true);
            _move = _map.FindAction(MoveActionName, throwIfNotFound: true);
            _attack = _map.FindAction(AttackActionName, throwIfNotFound: true);
            _sprint = _map.FindAction(SprintActionName, throwIfNotFound: true);
        }

        private void OnEnable()
        {
            _map?.Enable();
        }

        private void OnDisable()
        {
            _map?.Disable();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            Move(deltaTime);
            Attack();
        }

        private void Move(float deltaTime)
        {
            Vector2 raw = _move.ReadValue<Vector2>();
            Vector3 direction = new Vector3(raw.x, 0f, raw.y);
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            float topSpeed = _sprint.IsPressed() ? _runSpeed : _walkSpeed;
            _speed = Mathf.MoveTowards(_speed, direction.magnitude * topSpeed, _acceleration * deltaTime);

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion facing = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(direction, Vector3.up),
                    _turnDegreesPerSecond * deltaTime);
                Vector3 position = transform.position + (direction.normalized * (_speed * deltaTime));

                transform.SetPositionAndRotation(position, facing);
            }

            _animator.SetFloat(SpeedParameter, _speed);
            _animator.SetBool(IsWalkingParameter, _speed > 0.1f);
        }

        private void Attack()
        {
            if (_attack.WasPressedThisFrame())
            {
                _animator.SetTrigger(AttackParameter);
            }
        }
    }
}

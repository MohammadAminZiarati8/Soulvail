using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VContainer;

namespace Soulvail.Game.Views
{
    [RequireComponent(typeof(EnemyView))]
    public sealed class HierophantView : MonoBehaviour, IPoolable
    {
        private const float ReleaseSeconds = 0.24f;
        private const float ThrowStrike = 0.43f;
        // The imported 32-frame staff chop drops through its impact pose at frame 18.
        private const float SealStrike = 18f / 32f;

        [SerializeField] private Animator _animator;
        [SerializeField] private AnimationClip _sealMotion;
        [SerializeField] private Material _footprintMaterial;
        [SerializeField] private GameObject _sealPrefab;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioClip _throwCue;
        [SerializeField] private AudioClip _sealCue;

        private readonly MeshRenderer[] _sealLights = new MeshRenderer[3];
        private readonly Vector3[] _vertices = new Vector3[32];
        private readonly Color[] _colors = new Color[32];
        private readonly int[] _rectangle = { 0, 1, 2, 2, 1, 3 };
        private readonly int[] _outlineTriangles = new int[48];

        private IDisposable[] _subscriptions;
        private EnemyView _body;
        private Transform _floor;
        private Transform _footprint;
        private MeshRenderer _warning;
        private MeshRenderer _fill;
        private MeshRenderer _outline;
        private MeshRenderer _release;
        private Mesh _warningMesh;
        private Mesh _fillMesh;
        private Mesh _outlineMesh;
        private Mesh _releaseMesh;
        private SunSealEruption _eruption;
        private HierophantCastEffects _castEffects;
        private Transform _staff;
        private HierophantFireWave _fire;
        private HierophantHealChannel _heal;
        private LineRenderer _ward;
        private Vector3 _wardSource;
        private HierophantArmed _cast;
        private string _pose;
        private float _progress;
        private float _flash;
        private float _recovery;
        private bool _dead;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            DisposeSubscriptions();
            _body = GetComponent<EnemyView>();
            _staff = _animator.transform.Find(
                "Rig_Medium/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r/hand.r/handslot.r/PriestStaff");
            _subscriptions = new[]
            {
                events.Subscribe<HierophantSealsPlaced>(OnSealsPlaced),
                events.Subscribe<HierophantArmed>(OnArmed),
                events.Subscribe<HierophantTellProgress>(OnProgress),
                events.Subscribe<HierophantImpact>(OnImpact),
                events.Subscribe<HierophantFireWave>(OnFire),
                events.Subscribe<HierophantHealChannel>(OnHeal),
                events.Subscribe<HierophantWard>(OnWard),
                events.Subscribe<BossPhaseChanged>(OnPhase),
                events.Subscribe<EnemyDied>(OnDeath),
            };
            OnSpawn();
        }

        public void OnSpawn() => ResetPresentation();
        public void OnDespawn() => ResetPresentation();
        private void OnEnable() => Play("Idle");
        private void OnDisable() => ResetPresentation();

        private bool IsMine(int id) =>
            !_dead && isActiveAndEnabled && _body != null && _body.IsBound && _body.Id == id;

        private void CreateFloor()
        {
            _floor = new GameObject("Hierophant floor visuals").transform;
            _floor.gameObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(_floor.gameObject, gameObject.scene);
            _footprint = new GameObject("Footprint").transform;
            _footprint.SetParent(_floor, false);
            _warning = CreateLayer("Warning", 0, out _warningMesh);
            _fill = CreateLayer("Progress", 1, out _fillMesh);
            _release = CreateLayer("Release", 2, out _releaseMesh);
            _outline = CreateLayer("Warning outline", 6, out _outlineMesh);
            _eruption = new SunSealEruption(_floor, _footprintMaterial);
            _castEffects = new HierophantCastEffects(_floor, _footprint, _footprintMaterial);
            var tether = new GameObject("Priest ward");
            tether.transform.SetParent(_floor, false);
            _ward = tether.AddComponent<LineRenderer>();
            _ward.sharedMaterial = _footprintMaterial;
            _ward.sortingOrder = 5;
            _ward.shadowCastingMode = ShadowCastingMode.Off;
            _ward.receiveShadows = false;
            _ward.positionCount = 2;
            _ward.widthMultiplier = 0.24f;
            _ward.startColor = Palette.Vertex(new Color(1f, 0.73f, 0.24f));
            _ward.endColor = Palette.Vertex(new Color(1f, 0.95f, 0.66f));
            _ward.enabled = false;

            var lightColor = new MaterialPropertyBlock();
            lightColor.SetColor("_BaseColor", Color.Lerp(Palette.Danger, Color.white, 0.55f));
            var plaqueColor = new MaterialPropertyBlock();
            for (int i = 0; i < _sealLights.Length; i++)
            {
                GameObject seal = Instantiate(_sealPrefab, _floor);
                var plaque = seal.transform.Find("Sandstone plaque").GetComponent<MeshRenderer>();
                plaqueColor.SetColor("_BaseColor", Color.Lerp(plaque.sharedMaterial.color, Color.black, 0.3f));
                plaque.SetPropertyBlock(plaqueColor);
                _sealLights[i] = seal.transform.Find("Light").GetComponent<MeshRenderer>();
                _sealLights[i].SetPropertyBlock(lightColor);
            }
        }

        private MeshRenderer CreateLayer(string label, int order, out Mesh mesh)
        {
            var layer = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(_footprint, false);
            layer.transform.localPosition = Vector3.up * (0.075f + order * 0.01f);
            var renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _footprintMaterial;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            mesh = new Mesh { name = "Hierophant " + label };
            mesh.MarkDynamic();
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            return renderer;
        }

        private void OnSealsPlaced(HierophantSealsPlaced evt)
        {
            if (!IsMine(evt.BossId))
                return;

            if (_floor == null)
                CreateFloor();

            PlaceSeal(0, evt.West);
            PlaceSeal(1, evt.Centre);
            PlaceSeal(2, evt.East);
            _floor.gameObject.SetActive(true);
        }

        private void PlaceSeal(int slot, System.Numerics.Vector2 centre) =>
            _sealLights[slot].transform.parent.position = new Vector3(centre.X, 0f, centre.Y);

        private void OnArmed(HierophantArmed evt)
        {
            if (!IsMine(evt.BossId) || _floor == null || !_floor.gameObject.activeSelf)
                return;

            ClearAttack();
            _cast = evt;
            bool fire = evt.Move == HierophantMove.FireThrow;
            var facing = new Vector3(evt.Facing.X, 0f, evt.Facing.Y);
            Quaternion heading = facing != Vector3.zero
                ? Quaternion.LookRotation(facing)
                : Quaternion.identity;
            _footprint.SetPositionAndRotation(evt.Origin.ToUnity(), heading);
            DrawStrip(_warningMesh, 0f, evt.Reach, Palette.Danger, 0.56f);
            _warning.enabled = true;
            DrawOutline();
            _outline.enabled = true;
            SetSealLight(true);

            if (fire)
                Play("ThrowTell", 0f);
            else
                Play("SealTell", _sealMotion.length * SealStrike / evt.Seconds);

            _audio.clip = fire ? _throwCue : _sealCue;
            if (Application.isPlaying)
                _audio.Play();
        }

        private void OnProgress(HierophantTellProgress evt)
        {
            if (!IsMine(evt.BossId) || _cast.Seconds == 0f || !_warning.enabled || evt.Fraction <= _progress)
                return;

            _progress = evt.Fraction;
            DrawStrip(_fillMesh, 0f, _cast.Reach * _progress, Palette.Danger, 0.8f);
            _fill.enabled = true;
            if (_cast.Move == HierophantMove.FireThrow)
                Play("ThrowTell", 0f, _progress * ThrowStrike);
        }

        private void DrawStrip(Mesh mesh, float rear, float front, Color color, float opacity)
        {
            float width = _cast.HalfWidth;
            _vertices[0] = new Vector3(-width, 0f, rear);
            _vertices[1] = new Vector3(-width, 0f, front);
            _vertices[2] = new Vector3(width, 0f, rear);
            _vertices[3] = new Vector3(width, 0f, front);
            mesh.SetVertices(_vertices, 0, 4);
            mesh.SetTriangles(_rectangle, 0, calculateBounds: false);
            mesh.RecalculateBounds();
            Tint(mesh, color, opacity);
        }

        private void DrawOutline()
        {
            float width = _cast.HalfWidth;
            float reach = _cast.Reach;
            float edge = Mathf.Min(0.24f, width * 0.5f, reach * 0.25f);
            var shadow = Palette.Vertex(new Color(0.20f, 0.036f, 0.018f));
            Color orange = Palette.Vertex(Color.Lerp(Palette.Danger, Color.white, 0.26f));
            Frame(0, width, reach, 0f, edge, shadow);
            Frame(4, width, reach, edge * 0.30f, edge * 0.47f, orange);
            _outlineMesh.SetVertices(_vertices, 0, 32);
            _outlineMesh.SetColors(_colors, 0, 32);
            _outlineMesh.SetTriangles(_outlineTriangles, 0, calculateBounds: false);
            _outlineMesh.RecalculateBounds();
        }

        private void Frame(int firstQuad, float width, float reach, float inset, float thickness, Color color)
        {
            float left = -width + inset;
            float right = width - inset;
            float far = reach - inset;
            OutlineQuad(firstQuad, left, left + thickness, inset, far, color);
            OutlineQuad(firstQuad + 1, right - thickness, right, inset, far, color);
            OutlineQuad(firstQuad + 2, left + thickness, right - thickness, inset, inset + thickness, color);
            OutlineQuad(firstQuad + 3, left + thickness, right - thickness, far - thickness, far, color);
        }

        private void OutlineQuad(int quad, float left, float right, float near, float far, Color color)
        {
            int vertex = quad * 4;
            float y = quad < 4 ? 0f : 0.008f;
            _vertices[vertex] = new Vector3(left, y, near);
            _vertices[vertex + 1] = new Vector3(left, y, far);
            _vertices[vertex + 2] = new Vector3(right, y, near);
            _vertices[vertex + 3] = new Vector3(right, y, far);
            Array.Fill(_colors, color, vertex, 4);
            for (int i = 0; i < _rectangle.Length; i++)
                _outlineTriangles[quad * _rectangle.Length + i] = vertex + _rectangle[i];
        }

        private void Tint(Mesh mesh, Color color, float opacity)
        {
            color.a = opacity;
            Array.Fill(_colors, Palette.Vertex(color), 0, mesh.vertexCount);
            mesh.SetColors(_colors, 0, mesh.vertexCount);
        }

        private void OnImpact(HierophantImpact evt)
        {
            if (!IsMine(evt.BossId) || _cast.Seconds == 0f || !_warning.enabled
                || _cast.Move != evt.Move || _cast.SealSlot != evt.SealSlot)
                return;

            HideWarning();
            _recovery = evt.RecoverySeconds;
            if (evt.Move == HierophantMove.FireThrow)
            {
                Play("ThrowRelease", 0f, ThrowStrike);
                return;
            }

            DrawStrip(_releaseMesh, 0f, _cast.Reach, Palette.Danger, 1f);
            TintRelease(1f);
            _release.enabled = true;
            _flash = ReleaseSeconds;
            Play("SealRelease", _sealMotion.length * (1f - SealStrike) / ReleaseSeconds, SealStrike);
            _eruption.Begin(_footprint, _cast.Reach, _cast.HalfWidth);
        }

        private void OnFire(HierophantFireWave evt)
        {
            if (!IsMine(evt.BossId) || _cast.Seconds == 0f || _cast.Move != HierophantMove.FireThrow
                || _warning.enabled)
                return;

            _fire = evt;
            if (!evt.Active)
            {
                _release.enabled = false;
                _castEffects.ClearFire();
                _cast = default;
                return;
            }

            _release.enabled = evt.Front > evt.Rear;
            if (_release.enabled)
                DrawStrip(_releaseMesh, evt.Rear, evt.Front, new Color(1f, 0.39f, 0.025f), 0.75f);
            _castEffects.Fire(_cast.HalfWidth, evt.Rear, evt.Front, evt.Fraction);
            Play("ThrowRelease", 0f, Mathf.Lerp(ThrowStrike, 0.99f, Mathf.Clamp01(evt.Fraction / 0.55f)));
        }

        private void OnHeal(HierophantHealChannel evt)
        {
            if (!IsMine(evt.BossId) || _castEffects == null)
                return;

            _heal = evt;
            if (evt.Active)
                UpdateHeal();
            else
                _castEffects.ClearHeal();
        }

        private void UpdateHeal() =>
            _castEffects.Heal(_heal.PriestPosition.ToUnity(), transform.position + Vector3.up * 1.8f, _heal.Fraction);

        private void TintRelease(float fraction) =>
            Tint(_releaseMesh, Color.Lerp(Palette.Danger, Color.white, 0.65f), fraction * 0.32f);

        private void OnPhase(BossPhaseChanged evt)
        {
            if (IsMine(evt.EnemyId))
            {
                ClearAttack();
                _eruption?.Clear();
                _heal = default;
                _castEffects?.ClearHeal();
                if (_ward != null)
                    _ward.enabled = false;
                Play("Idle");
            }
        }

        private void OnWard(HierophantWard evt)
        {
            if (!IsMine(evt.BossId) || _ward == null)
                return;

            _ward.enabled = evt.Active;
            if (!evt.Active)
                return;

            _wardSource = evt.PriestPosition.ToUnity() + Vector3.up * 1.25f;
            UpdateWard();
        }

        private void UpdateWard()
        {
            _ward.SetPosition(0, _wardSource);
            _ward.SetPosition(1, transform.position + Vector3.up * 1.8f);
        }

        private void OnDeath(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            {
                ResetPresentation();
                _dead = true;
                Play("Death");
            }
        }

        public void Step(float dt)
        {
            if (_body == null || !IsMine(_body.Id) || !(dt >= 0f) || !float.IsFinite(dt))
                return;

            if (_ward != null && _ward.enabled)
                UpdateWard();
            if (_heal.Active)
                UpdateHeal();
            if (_cast.Seconds > 0f && _cast.Move == HierophantMove.FireThrow && _staff != null)
            {
                Vector3 tip = _staff.TransformPoint(Vector3.forward * 1.34f);
                if (_fire.Active)
                    _castEffects.Launch(tip, _fire.Front, _fire.Fraction);
                else if (_warning.enabled)
                    _castEffects.Charge(tip, _progress);
            }
            _eruption?.Step(dt);

            _recovery = Mathf.Max(0f, _recovery - dt);
            if (_flash > 0f)
            {
                _flash = Mathf.Max(0f, _flash - dt);
                if (_flash > 0f)
                {
                    TintRelease(_flash / ReleaseSeconds);
                    return;
                }

                _release.enabled = false;
                SetSealLight(false);
                _cast = default;
            }

            if (_cast.Seconds != 0f)
                return;

            string pose = _recovery > 0f ? "Recovery" : _body.Velocity.sqrMagnitude > 0.01f ? "Walk" : "Idle";
            if (_pose != pose)
                Play(pose);
        }

        private void LateUpdate() => Step(Time.deltaTime);

        private void Play(string state, float speed = 1f, float start = 0f)
        {
            _pose = state;
            if (_animator == null)
                return;

            _animator.speed = speed;
            if (_animator.isActiveAndEnabled)
                _animator.Play(state, 0, start);
        }

        private void SetSealLight(bool lit)
        {
            if (_cast.Move == HierophantMove.SunSeal && (uint)_cast.SealSlot < (uint)_sealLights.Length)
                _sealLights[_cast.SealSlot].enabled = lit;
        }

        private void HideWarning()
        {
            _warning.enabled = false;
            _fill.enabled = false;
            _outline.enabled = false;
            _audio.Stop();
            _audio.clip = null;
        }

        private void ClearAttack()
        {
            if (_floor != null)
            {
                HideWarning();
                _release.enabled = false;
                SetSealLight(false);
                _castEffects.ClearFire();
            }
            _cast = default;
            _fire = default;
            _progress = _flash = _recovery = 0f;
        }

        private void ResetPresentation()
        {
            ClearAttack();
            _eruption?.Clear();
            _heal = default;
            _castEffects?.ClearHeal();
            if (_floor != null)
            {
                _ward.enabled = false;
                _floor.gameObject.SetActive(false);
            }
            _dead = false;
            Play("Idle");
        }

        private void DisposeSubscriptions()
        {
            if (_subscriptions == null)
                return;

            foreach (IDisposable subscription in _subscriptions)
                subscription.Dispose();
        }

        private void OnDestroy()
        {
            DisposeSubscriptions();
            _eruption?.Dispose();
            _castEffects?.Dispose();
            if (Application.isPlaying)
            {
                if (_floor != null)
                    Destroy(_floor.gameObject);
                Destroy(_warningMesh);
                Destroy(_fillMesh);
                Destroy(_outlineMesh);
                Destroy(_releaseMesh);
            }
            else
            {
                if (_floor != null)
                    DestroyImmediate(_floor.gameObject);
                DestroyImmediate(_warningMesh);
                DestroyImmediate(_fillMesh);
                DestroyImmediate(_outlineMesh);
                DestroyImmediate(_releaseMesh);
            }
        }
    }
}

using Ironfront.Core;
using Ironfront.InputBindings;
using UnityEngine;

namespace Ironfront.CameraControl
{
    /// <summary>
    /// Angled overhead RTS camera. This component sits on a ground-level rig; the camera is positioned
    /// at an orbit offset (pitch/yaw/distance) from it. WASD/arrows and screen-edge panning move the rig,
    /// the mouse wheel changes the distance within limits.
    /// </summary>
    public class RtsCameraController : MonoBehaviour
    {
        [SerializeField] private Camera controlledCamera;

        [Header("Angle")]
        [SerializeField, Range(20f, 89f)] private float pitch = 55f;
        [SerializeField] private float yaw;

        [Header("Pan")]
        [Tooltip("Pan speed in world units per second at the reference zoom distance.")]
        [SerializeField] private float panSpeed = 25f;
        [SerializeField] private bool edgePanEnabled = true;
        [SerializeField] private float edgeBorderPixels = 12f;
        [SerializeField] private Vector2 boundsMin = new Vector2(-50f, -50f);
        [SerializeField] private Vector2 boundsMax = new Vector2(50f, 50f);

        [Header("Zoom")]
        [SerializeField] private float minDistance = 12f;
        [SerializeField] private float maxDistance = 60f;
        [SerializeField] private float zoomStep = 4f;
        [SerializeField] private float zoomSmoothing = 12f;
        [SerializeField] private float startDistance = 35f;

        private float _targetDistance;
        private float _distance;

        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public float TargetDistance => _targetDistance;
        public float CurrentDistance => _distance;

        private void Awake()
        {
            if (controlledCamera == null) controlledCamera = GetComponentInChildren<Camera>();
            _targetDistance = _distance = Mathf.Clamp(startDistance, minDistance, maxDistance);
            ApplyCameraTransform();
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            Vector2 pan = RtsInput.Pan?.ReadValue<Vector2>() ?? Vector2.zero;
            if (edgePanEnabled && Application.isFocused)
            {
                Vector2 edge = CameraMath.EdgePanDirection(RtsInput.PointerPosition,
                    new Vector2(Screen.width, Screen.height), edgeBorderPixels);
                pan += edge;
            }
            Pan(pan, dt);

            float scroll = RtsInput.Zoom?.ReadValue<float>() ?? 0f;
            if (scroll != 0f && !RtsInput.PointerOverUI())
                Zoom(scroll);

            _distance = Mathf.Lerp(_distance, _targetDistance, 1f - Mathf.Exp(-zoomSmoothing * dt));
            ApplyCameraTransform();
        }

        /// <summary>Moves the rig on the ground plane. Speed scales with zoom so panning feels consistent.</summary>
        public void Pan(Vector2 input, float deltaTime)
        {
            if (input == Vector2.zero) return;
            float speed = panSpeed * (_distance / startDistance);
            Vector3 next = transform.position + CameraMath.PanToWorld(input, yaw) * speed * deltaTime;
            transform.position = CameraMath.ClampToBounds(next, boundsMin, boundsMax);
        }

        /// <summary>Applies one scroll step; positive zooms in.</summary>
        public void Zoom(float scroll)
        {
            _targetDistance = CameraMath.ApplyZoom(_targetDistance, scroll, zoomStep, minDistance, maxDistance);
        }

        private void ApplyCameraTransform()
        {
            if (controlledCamera == null) return;
            var t = controlledCamera.transform;
            t.position = transform.position + CameraMath.OrbitOffset(pitch, yaw, _distance);
            t.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void OnValidate()
        {
            maxDistance = Mathf.Max(maxDistance, minDistance);
            if (!Application.isPlaying)
            {
                _distance = Mathf.Clamp(startDistance, minDistance, maxDistance);
                if (controlledCamera == null) controlledCamera = GetComponentInChildren<Camera>();
                ApplyCameraTransform();
            }
        }
    }
}

using Ironfront.Buildings;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Combat
{
    /// <summary>
    /// World-space health bar that faces the camera. Shown while the object is damaged or selected;
    /// the fill shrinks and shifts from green to red as health drops.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class HealthBar : MonoBehaviour
    {
        [SerializeField] private float height = 2.6f;
        [SerializeField] private float width = 1.8f;
        [SerializeField] private float thickness = 0.22f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private Health _health;
        private SelectableUnit _unit;
        private Building _building;
        private Transform _root, _fill;
        private Renderer _fillRenderer;
        private MaterialPropertyBlock _block;
        private Camera _camera;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _unit = GetComponent<SelectableUnit>();
            _building = GetComponent<Building>();
            _block = new MaterialPropertyBlock();
        }

        private void Start()
        {
            var visuals = CombatFx.Visuals;
            _root = new GameObject("HealthBar").transform;
            _root.SetParent(transform, false);
            _root.gameObject.layer = 2;

            var bg = CombatFx.Primitive(PrimitiveType.Cube, "Background", Vector3.zero,
                new Vector3(width + 0.08f, thickness + 0.08f, 0.01f), visuals != null ? visuals.HealthBarBackground : null);
            bg.transform.SetParent(_root, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.01f);

            var fill = CombatFx.Primitive(PrimitiveType.Cube, "Fill", Vector3.zero,
                new Vector3(width, thickness, 0.01f), visuals != null ? visuals.HealthBarFill : null);
            fill.transform.SetParent(_root, false);
            fill.transform.localPosition = Vector3.zero;
            _fill = fill.transform;
            _fillRenderer = fill.GetComponent<Renderer>();

            _root.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_root == null || _health == null || _health.Model == null) return;

            bool selected = (_unit != null && _unit.IsSelected) || (_building != null && _building.IsSelected);
            bool show = _health.IsAlive && (_health.Model.IsDamaged || selected);
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;

            if (_camera == null) _camera = Camera.main;
            _root.position = transform.position + Vector3.up * height;
            if (_camera != null) _root.rotation = _camera.transform.rotation;

            float f = Mathf.Clamp01(_health.Model.Fraction);
            _fill.localScale = new Vector3(width * f, thickness, 0.01f);
            _fill.localPosition = new Vector3(-width * (1f - f) * 0.5f, 0f, 0f);
            _block.SetColor(BaseColor, f > 0.5f ? Color.Lerp(Color.yellow, Color.green, (f - 0.5f) * 2f) : Color.Lerp(Color.red, Color.yellow, f * 2f));
            _fillRenderer.SetPropertyBlock(_block);
        }
    }
}

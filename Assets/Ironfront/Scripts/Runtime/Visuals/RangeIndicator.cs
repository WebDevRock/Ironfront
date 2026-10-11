using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Data;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Ground rings showing a weapon's reach (Milestone 007): the maximum range in amber and, for artillery, the
    /// minimum range in red. Shown while the owner is selected; the placement ghost of a defence shows the same
    /// rings via <see cref="CreateRing"/>.
    /// </summary>
    public class RangeIndicator : MonoBehaviour
    {
        public static readonly Color MaxColor = new Color(1f, 0.72f, 0.28f, 0.75f);
        public static readonly Color MinColor = new Color(1f, 0.3f, 0.25f, 0.75f);

        private GameObject _rings;
        private SelectableUnit _unit;
        private Building _building;

        private void Start()
        {
            _unit = GetComponent<SelectableUnit>();
            _building = GetComponent<Building>();
            WeaponDefinition weapon = null;
            var cu = GetComponent<CombatUnit>();
            if (cu != null) weapon = cu.Weapon;
            var dt = GetComponent<DefenceTurret>();
            if (dt != null) weapon = dt.Weapon;
            if (weapon == null) { enabled = false; return; }

            _rings = new GameObject("RangeRings") { layer = 2 };
            _rings.transform.SetParent(transform, false);
            CreateRing(_rings.transform, weapon.Range + Extent(), MaxColor);
            if (weapon.MinRange > 0f) CreateRing(_rings.transform, weapon.MinRange + Extent(), MinColor);
            _rings.SetActive(false);
        }

        private float Extent() => _building != null && _building.Definition != null ? Mathf.Max(_building.Definition.FootprintSize.x, _building.Definition.FootprintSize.y) * 0.5f : 0.6f;

        private void LateUpdate()
        {
            if (_rings == null) return;
            bool show = (_unit != null && _unit.IsSelected) || (_building != null && _building.IsSelected);
            if (_rings.activeSelf != show) _rings.SetActive(show);
        }

        /// <summary>A flat circle (LineRenderer) of <paramref name="radius"/> around the parent's origin.</summary>
        public static LineRenderer CreateRing(Transform parent, float radius, Color color, int segments = 72)
        {
            var go = new GameObject("Ring") { layer = 2 };
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segments;
            lr.widthMultiplier = 0.12f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var visuals = CombatFx.Visuals;
            if (visuals != null) lr.sharedMaterial = visuals.FxTracer != null ? visuals.FxTracer : visuals.MoveMarker;
            lr.startColor = lr.endColor = color;
            lr.alignment = LineAlignment.TransformZ;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, -0.08f));
            }
            return lr;
        }
    }
}

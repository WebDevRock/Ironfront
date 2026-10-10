using System.Collections.Generic;
using System.Linq;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Economy;
using Ironfront.Selection;
using Ironfront.Units;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Top-left panel: what is selected, its health and combat stats, its current order, and the controls.
    /// The panel is a raycast target, so clicks on it are consumed by the UI rather than the world.
    /// </summary>
    public class SelectionHud : MonoBehaviour
    {
        [SerializeField] private SelectionManager selectionManager;
        [SerializeField] private Text label;
        [Tooltip("Seconds between refreshes while something is selected (health, cargo and orders change continuously).")]
        [SerializeField] private float detailRefreshInterval = 0.25f;

        private const string Help =
            "LMB: select   Drag: box select   Shift+LMB: toggle   WASD / edge: pan   Wheel: zoom\n" +
            "RMB: move / attack enemy / harvest / rally   F: attack-move   X: stop   H: hold";

        private float _refreshTimer;

        private void OnEnable()
        {
            if (selectionManager != null)
            {
                selectionManager.Selection.Changed += Refresh;
                selectionManager.StructureSelectionChanged += OnStructureChanged;
            }
            Refresh();
        }

        private void OnDisable()
        {
            if (selectionManager != null)
            {
                selectionManager.Selection.Changed -= Refresh;
                selectionManager.StructureSelectionChanged -= OnStructureChanged;
            }
        }

        private void Update()
        {
            if (selectionManager == null) return;
            if (selectionManager.Selection.Count == 0 && selectionManager.SelectedStructure == null) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = detailRefreshInterval;
            Refresh();
        }

        private void OnStructureChanged(Building _) => Refresh();

        private void Refresh()
        {
            if (label == null) return;
            label.text = $"{Describe()}\n{Help}";
        }

        private string Describe()
        {
            if (selectionManager == null) return "";
            var units = selectionManager.Selection.Items.Where(u => u != null).ToList();

            if (units.Count == 0)
            {
                var b = selectionManager.SelectedStructure;
                if (b == null) return "Selected units: 0";
                string text = $"Selected: {(b.Definition != null ? b.Definition.DisplayName : b.name)}  {HealthText(b.GetComponent<Health>())}";
                if (b.GetComponent<VehicleFactory>() != null) text += "\nRMB ground: set rally point";
                return text;
            }

            if (units.Count == 1) return DescribeUnit(units[0]);

            int armed = 0;
            float hp = 0f, max = 0f;
            var orders = new Dictionary<string, int>();
            foreach (var u in units)
            {
                var h = u.GetComponent<Health>();
                if (h != null) { hp += h.Current; max += h.Max; }
                var c = u.GetComponent<CombatUnit>();
                string order = c != null && c.Machine != null ? c.Machine.Order.ToString() : "Unarmed";
                if (c != null) armed++;
                orders[order] = orders.TryGetValue(order, out var n) ? n + 1 : 1;
            }
            string summary = string.Join(", ", orders.Select(kv => $"{kv.Key} x{kv.Value}"));
            return $"Selected units: {units.Count} ({armed} armed)  HP {hp:0}/{max:0}\nOrders: {summary}";
        }

        private static string DescribeUnit(SelectableUnit unit)
        {
            var identity = unit.GetComponent<UnitIdentity>();
            string name = identity != null && identity.Definition != null ? $"{identity.DisplayName} ({unit.name})" : unit.name;
            string text = $"Selected: {name}  {HealthText(unit.GetComponent<Health>())}";

            var harvester = unit.GetComponent<Harvester>();
            if (harvester != null && harvester.Machine != null) text += $"\n{harvester.StatusText}";

            var combat = unit.GetComponent<CombatUnit>();
            if (combat != null && combat.Weapon != null)
            {
                var w = combat.Weapon;
                text += $"\nWeapon: {w.DisplayName} {w.Damage:0} dmg, range {w.Range:0}m, every {w.Cooldown:0.0}s" +
                        $"\nOrder: {combat.DescribeOrder()}";
            }
            return text;
        }

        private static string HealthText(Health h) =>
            h == null ? "" : $"HP {Mathf.CeilToInt(h.Current)}/{h.Max:0}  Armour {h.Armour * 100f:0}%";
    }
}

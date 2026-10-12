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
        [Tooltip("Optional portrait of the selected unit or structure type.")]
        [SerializeField] private Image portrait;
        [Tooltip("Seconds between refreshes while something is selected (health, cargo and orders change continuously).")]
        [SerializeField] private float detailRefreshInterval = 0.25f;

        private const string Help =
            "LMB: select   Drag: box select   Shift+LMB: toggle   WASD / edge: pan   Wheel: zoom\n" +
            "RMB: move / attack enemy / harvest / rally / repair pad   F: attack-move   X: stop   H: hold   R: repair   T: deploy MCV";

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
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.Hud.Auto();
            if (label == null) return;
            label.text = $"{Describe()}\n{Help}";
            UpdatePortrait();
        }

        private void UpdatePortrait()
        {
            if (portrait == null || selectionManager == null) return;
            Sprite icon = null;
            var first = selectionManager.Selection.Items.FirstOrDefault(u => u != null);
            if (first != null)
            {
                var identity = first.GetComponent<UnitIdentity>();
                icon = identity != null && identity.Definition != null ? identity.Definition.Icon : null;
            }
            else if (selectionManager.SelectedStructure != null && selectionManager.SelectedStructure.Definition != null)
                icon = selectionManager.SelectedStructure.Definition.Icon;
            if (portrait.sprite != icon) portrait.sprite = icon;
            bool show = icon != null;
            if (portrait.gameObject.activeSelf != show) portrait.gameObject.SetActive(show);
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
                if (b.Repair != null && b.Repair.IsOn)
                    text += b.Repair.State == BuildingRepair.Status.PausedNoCredits ? "\nRepair: paused (insufficient credits)" : "\nRepair: on";
                // Milestone 010: territory and sale.
                if (b.Definition != null && b.Definition.TerritoryRadius > 0f)
                    text += $"\nConstruction territory: {b.Definition.TerritoryRadius:0} m radius";
                if (b.Sale != null && b.Sale.IsDismantling)
                    text += $"\nSelling: {b.Sale.Progress01 * 100f:0}% (refund about {b.Sale.EstimatedRefund})";
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
            var mcv = unit.GetComponent<MobileCommandVehicle>();
            if (mcv != null)
                text += mcv.IsDeploying ? $"\nDeploying: {mcv.Progress01 * 100f:0}% (immobile)" : "\nT: deploy into a Forward Command Centre here";
            var repair = RepairClient.Of(unit);
            if (repair != null && repair.IsBusy)
                text += repair.QueuePosition >= 0 ? $"\nRepair: {repair.State} (place {repair.QueuePosition + 1} in queue)" : $"\nRepair: {repair.State}";

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

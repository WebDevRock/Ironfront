using Ironfront.Buildings;
using Ironfront.Economy;
using Ironfront.Selection;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Minimal HUD panel showing the selection and controls. The panel is a raycast target, so clicks on it
    /// are consumed by the UI rather than the selection system.
    /// </summary>
    public class SelectionHud : MonoBehaviour
    {
        [SerializeField] private SelectionManager selectionManager;
        [SerializeField] private Text label;
        [Tooltip("Seconds between refreshes of a selected harvester's cargo readout.")]
        [SerializeField] private float detailRefreshInterval = 0.25f;

        private const string Help =
            "LMB: select   Drag: box select   Shift+LMB: toggle\n" +
            "RMB: move / harvest / rally   WASD / edge: pan   Wheel: zoom";

        private Harvester _watchedHarvester;
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
            Watch(null);
        }

        private void Update()
        {
            // Cargo changes continuously while harvesting; refresh that one readout at a low rate.
            if (_watchedHarvester == null) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = detailRefreshInterval;
            Refresh();
        }

        private void OnStructureChanged(Building _) => Refresh();

        private void Refresh()
        {
            if (label == null) return;

            string detail;
            Harvester harvester = null;
            int count = selectionManager != null ? selectionManager.Selection.Count : 0;

            if (count == 0 && selectionManager != null && selectionManager.SelectedStructure != null)
            {
                var b = selectionManager.SelectedStructure;
                detail = $"Selected: {(b.Definition != null ? b.Definition.DisplayName : b.name)}";
                if (b.GetComponent<VehicleFactory>() != null) detail += "  (RMB ground: set rally point)";
            }
            else if (count == 1)
            {
                var unit = selectionManager.Selection.Items[0];
                harvester = unit != null ? unit.GetComponent<Harvester>() : null;
                detail = harvester != null && harvester.Machine != null
                    ? $"Selected: {unit.name} - {harvester.StatusText}"
                    : $"Selected: {(unit != null ? unit.name : "-")}";
            }
            else
            {
                detail = $"Selected units: {count}";
            }

            Watch(harvester);
            label.text = $"{detail}\n{Help}";
        }

        private void Watch(Harvester harvester)
        {
            if (_watchedHarvester == harvester) return;
            if (_watchedHarvester != null) _watchedHarvester.StateChanged -= OnHarvesterState;
            _watchedHarvester = harvester;
            if (_watchedHarvester != null) _watchedHarvester.StateChanged += OnHarvesterState;
        }

        private void OnHarvesterState(Core.Harvesting.HarvesterState _) => Refresh();
    }
}

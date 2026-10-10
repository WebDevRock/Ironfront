using Ironfront.Selection;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Minimal HUD panel showing the selection count and controls. The panel is a raycast target, so
    /// clicks on it are consumed by the UI rather than the selection system.
    /// </summary>
    public class SelectionHud : MonoBehaviour
    {
        [SerializeField] private SelectionManager selectionManager;
        [SerializeField] private Text label;

        private const string Help =
            "LMB: select   Drag: box select   Shift+LMB: toggle\n" +
            "RMB: move   WASD / screen edge: pan   Wheel: zoom";

        private void OnEnable()
        {
            if (selectionManager != null) selectionManager.Selection.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (selectionManager != null) selectionManager.Selection.Changed -= Refresh;
        }

        private void Refresh()
        {
            if (label == null) return;
            int count = selectionManager != null ? selectionManager.Selection.Count : 0;
            label.text = $"Selected units: {count}\n{Help}";
        }
    }
}

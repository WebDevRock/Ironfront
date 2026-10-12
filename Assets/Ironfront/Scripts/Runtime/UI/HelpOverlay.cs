using System.Text;
using Ironfront.InputBindings;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>F1 toggles a panel listing every control and shortcut (keys and descriptions in aligned columns).</summary>
    public class HelpOverlay : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text keysLabel;
        [SerializeField] private Text descriptionsLabel;

        public static readonly (string keys, string description)[] Controls =
        {
            ("Left click", "Select unit or structure (double-click: all of that type on screen)"),
            ("Left drag", "Box select    •    Shift + click: add / remove"),
            ("Right click", "Move  •  attack enemy  •  harvest ore  •  set factory rally point"),
            ("F, then click", "Attack-move (engage enemies on the way)"),
            ("X  /  H", "Stop  /  Hold position"),
            ("R  /  T", "Repair at a Repair Pad  /  Deploy a Mobile Command Vehicle"),
            ("Build", "Only inside your construction territory (around the HQ and Forward Command Centres)"),
            ("Ctrl + 1–9", "Assign control group"),
            ("1–9", "Select control group (press twice: centre the camera on it)"),
            ("Space", "Jump to the latest alert"),
            ("WASD / arrows / edge", "Pan camera    •    Mouse wheel: zoom"),
            ("Minimap", "Left click: move camera    •    Right click: move selected units"),
            ("Esc", "Cancel placement or attack-move"),
            ("F1  /  F9", "This help  /  AI debug panel (match)"),
        };

        public bool IsVisible => panel != null && panel.activeSelf;

        private void Start()
        {
            var keys = new StringBuilder("CONTROLS\n\n");
            var descriptions = new StringBuilder("\n\n");
            foreach (var (k, d) in Controls)
            {
                keys.Append(k).Append('\n');
                descriptions.Append(d).Append('\n');
            }
            keys.Append("\nBuild from the sidebar; hover a button for costs and details.");
            if (keysLabel != null) keysLabel.text = keys.ToString();
            if (descriptionsLabel != null) descriptionsLabel.text = descriptions.ToString();
            if (panel != null) panel.SetActive(false);
        }

        private void Update()
        {
            var help = RtsInput.Help;
            if (help != null && help.WasPressedThisFrame()) Toggle();
        }

        public void Toggle()
        {
            if (panel != null) panel.SetActive(!panel.activeSelf);
        }
    }
}

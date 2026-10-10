using System;
using Ironfront.Construction;
using Ironfront.Core.Construction;
using Ironfront.InputBindings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>While placing a building, shows the cost and whether / why the site is valid next to the cursor.</summary>
    public class PlacementHint : MonoBehaviour
    {
        [SerializeField] private BuildingPlacementController placement;
        [SerializeField] private RectTransform panel;
        [SerializeField] private Text label;

        public string ShownText => label != null && panel != null && panel.gameObject.activeSelf ? label.text : "";

        private void Update()
        {
            if (panel == null || placement == null) return;
            bool show = placement.IsPlacing && !RtsInput.PointerOverUI();
            if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
            if (!show) return;

            var def = placement.Pending;
            bool valid = placement.CurrentResult == PlacementResult.Valid;
            label.text = $"{def.DisplayName}  ${def.Cost}\n" + (valid ? "Valid site: left-click to build" : PlacementRules.Describe(placement.CurrentResult)) +
                         "\nRight-click / Esc: cancel";
            label.color = valid ? UiTheme.Good : UiTheme.Danger;

            var canvas = panel.GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            panel.anchoredPosition = RtsInput.PointerPosition / scale + new Vector2(24f, -24f);
        }
    }
}

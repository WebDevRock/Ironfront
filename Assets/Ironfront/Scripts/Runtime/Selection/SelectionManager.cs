using System.Collections.Generic;
using Ironfront.Core;
using Ironfront.InputBindings;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Selection
{
    /// <summary>
    /// Handles left-click selection: single click, shift-click toggle and drag-rectangle selection.
    /// Gestures that start over UI are ignored.
    /// </summary>
    public class SelectionManager : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private LayerMask unitLayers = ~0;
        [SerializeField] private SelectionBoxView selectionBox;
        [Tooltip("Pointer travel in pixels before a press becomes a drag-select.")]
        [SerializeField] private float dragThreshold = 8f;
        [SerializeField] private float maxRayDistance = 500f;

        public SelectionSet<SelectableUnit> Selection { get; } = new SelectionSet<SelectableUnit>();

        public Camera ViewCamera => viewCamera;

        private bool _pressActive;
        private bool _dragging;
        private Vector2 _pressPosition;

        private void Awake()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            Selection.Changed += SyncIndicators;
        }

        private void OnDestroy()
        {
            Selection.Changed -= SyncIndicators;
        }

        private void Update()
        {
            Selection.RemoveWhere(u => u == null || !u.isActiveAndEnabled);

            var select = RtsInput.Select;
            if (select == null) return;

            Vector2 pointer = RtsInput.PointerPosition;

            if (select.WasPressedThisFrame())
            {
                // Pointer over UI: the press belongs to the UI, not to the selection system.
                _pressActive = !RtsInput.PointerOverUI();
                _dragging = false;
                _pressPosition = pointer;
            }

            if (!_pressActive) return;

            if (!_dragging && SelectionRules.IsDrag(_pressPosition, pointer, dragThreshold))
                _dragging = true;

            if (_dragging && selectionBox != null)
                selectionBox.Show(SelectionRules.RectFromCorners(_pressPosition, pointer));

            if (select.WasReleasedThisFrame())
            {
                bool additive = RtsInput.AdditiveHeld;
                if (_dragging)
                    SelectInScreenRect(SelectionRules.RectFromCorners(_pressPosition, pointer), additive);
                else
                    ClickAt(pointer, additive);

                _pressActive = false;
                _dragging = false;
                if (selectionBox != null) selectionBox.Hide();
            }
        }

        /// <summary>Applies a single click at a screen position.</summary>
        public void ClickAt(Vector2 screenPosition, bool additive)
        {
            SelectionRules.ApplyClick(Selection, RaycastUnit(screenPosition), additive);
        }

        /// <summary>Selects every unit whose pivot projects inside the screen rectangle.</summary>
        public void SelectInScreenRect(Rect screenRect, bool additive)
        {
            var inBox = new List<SelectableUnit>();
            if (viewCamera != null)
            {
                foreach (var unit in SelectableUnit.All)
                {
                    if (SelectionRules.IsInsideBox(screenRect, viewCamera.WorldToScreenPoint(unit.transform.position)))
                        inBox.Add(unit);
                }
            }
            SelectionRules.ApplyBox(Selection, inBox, additive);
        }

        public SelectableUnit RaycastUnit(Vector2 screenPosition)
        {
            if (viewCamera == null) return null;
            Ray ray = viewCamera.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out var hit, maxRayDistance, unitLayers, QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<SelectableUnit>();
            return null;
        }

        private void SyncIndicators()
        {
            foreach (var unit in SelectableUnit.All)
                unit.SetSelected(Selection.Contains(unit));
        }
    }
}

using System;
using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Core;
using Ironfront.InputBindings;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Selection
{
    /// <summary>
    /// Handles left-click selection: single click, shift-click toggle and drag-rectangle selection of the
    /// player's units, plus single-click selection of the player's structures. Gestures that start over UI,
    /// or while another system owns the pointer (building placement), are ignored.
    /// </summary>
    public class SelectionManager : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private LayerMask unitLayers = ~0;
        [SerializeField] private LayerMask structureLayers = 0;
        [SerializeField] private SelectionBoxView selectionBox;
        [Tooltip("Pointer travel in pixels before a press becomes a drag-select.")]
        [SerializeField] private float dragThreshold = 8f;
        [SerializeField] private float maxRayDistance = 500f;

        public SelectionSet<SelectableUnit> Selection { get; } = new SelectionSet<SelectableUnit>();

        /// <summary>The selected structure, if any. Units and a structure are never selected together.</summary>
        public Building SelectedStructure { get; private set; }

        public event Action<Building> StructureSelectionChanged;

        public Camera ViewCamera => viewCamera;

        [Tooltip("Seconds within which a second click on a unit of the same type selects all of that type on screen.")]
        [SerializeField] private float doubleClickWindow = 0.35f;

        private Units.UnitIdentity _lastClickedType;
        private float _lastClickTime = float.NegativeInfinity;

        private bool _pressActive;
        private bool _dragging;
        private Vector2 _pressPosition;

        private int PlayerFactionId =>
            MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;

        private void Awake()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            Selection.Changed += OnUnitSelectionChanged;
            Health.AnyDied += OnAnyDied;
        }

        private void OnDestroy()
        {
            Selection.Changed -= OnUnitSelectionChanged;
            Health.AnyDied -= OnAnyDied;
        }

        /// <summary>Drops destroyed units/structures from the selection immediately (before the object is gone).</summary>
        private void OnAnyDied(Health dead)
        {
            var unit = dead.GetComponent<SelectableUnit>();
            if (unit != null) Selection.Remove(unit);
            if (SelectedStructure != null && SelectedStructure.gameObject == dead.gameObject) SelectStructure(null);
        }

        private void Update()
        {
            Selection.RemoveWhere(u => u == null || !u.isActiveAndEnabled);
            if (SelectedStructure != null && !SelectedStructure.isActiveAndEnabled) SelectStructure(null);

            var select = RtsInput.Select;
            if (select == null) return;

            Vector2 pointer = RtsInput.PointerPosition;

            if (select.WasPressedThisFrame())
            {
                // Pointer over UI, or consumed by placement: the press does not belong to selection.
                _pressActive = !RtsInput.PointerOverUI() && !RtsInputMode.WorldPointerBlocked;
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
            var hit = RaycastSelectable(screenPosition);

            if (hit is Building building)
            {
                if (additive) return; // shift-click on a structure leaves the unit selection alone
                Selection.Clear();
                SelectStructure(building);
                return;
            }

            if (!additive) SelectStructure(null);
            var unit = hit as SelectableUnit;

            // Double-click on a unit: select every player unit of the same type that is on screen.
            var identity = unit != null ? unit.GetComponent<UnitIdentity>() : null;
            float now = Time.unscaledTime;
            if (!additive && identity != null && identity.Definition != null && _lastClickedType != null &&
                _lastClickedType.Definition == identity.Definition && now - _lastClickTime <= doubleClickWindow)
            {
                SelectAllOfTypeOnScreen(identity.Definition);
                _lastClickedType = null;
                return;
            }
            _lastClickedType = identity;
            _lastClickTime = now;

            SelectionRules.ApplyClick(Selection, unit, additive);
        }

        /// <summary>Selects every player unit of the given type whose position is on screen.</summary>
        public void SelectAllOfTypeOnScreen(Data.UnitDefinition type)
        {
            if (viewCamera == null || type == null) return;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            var matches = new List<SelectableUnit>();
            foreach (var u in SelectableUnit.All)
            {
                if (!IsPlayerOwned(u)) continue;
                var id = u.GetComponent<UnitIdentity>();
                if (id == null || id.Definition != type) continue;
                if (SelectionRules.IsInsideBox(screen, viewCamera.WorldToScreenPoint(u.transform.position))) matches.Add(u);
            }
            SelectStructure(null);
            Selection.Replace(matches);
        }

        /// <summary>Selects every player unit whose pivot projects inside the screen rectangle.</summary>
        public void SelectInScreenRect(Rect screenRect, bool additive)
        {
            var inBox = new List<SelectableUnit>();
            if (viewCamera != null)
            {
                foreach (var unit in SelectableUnit.All)
                {
                    if (!IsPlayerOwned(unit)) continue;
                    if (SelectionRules.IsInsideBox(screenRect, viewCamera.WorldToScreenPoint(unit.transform.position)))
                        inBox.Add(unit);
                }
            }
            if (!additive) SelectStructure(null);
            SelectionRules.ApplyBox(Selection, inBox, additive);
        }

        public void SelectStructure(Building building)
        {
            if (building != null && FactionMember.Of(building) != PlayerFactionId) building = null;
            if (SelectedStructure == building) return;
            if (SelectedStructure != null) SelectedStructure.SetSelected(false);
            SelectedStructure = building;
            if (SelectedStructure != null) SelectedStructure.SetSelected(true);
            StructureSelectionChanged?.Invoke(SelectedStructure);
        }

        /// <summary>Player-owned unit under the pointer, or null.</summary>
        public SelectableUnit RaycastUnit(Vector2 screenPosition) => RaycastSelectable(screenPosition) as SelectableUnit;

        /// <summary>Player-owned unit or structure under the pointer, or null.</summary>
        public Component RaycastSelectable(Vector2 screenPosition)
        {
            if (viewCamera == null) return null;
            Ray ray = viewCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, maxRayDistance, unitLayers | structureLayers, QueryTriggerInteraction.Ignore))
                return null;

            var unit = hit.collider.GetComponentInParent<SelectableUnit>();
            if (unit != null) return IsPlayerOwned(unit) ? unit : null;

            var building = hit.collider.GetComponentInParent<Building>();
            if (building != null && FactionMember.Of(building) == PlayerFactionId) return building;
            return null;
        }

        private bool IsPlayerOwned(Component c) => FactionMember.Of(c) == PlayerFactionId;

        private void OnUnitSelectionChanged()
        {
            foreach (var unit in SelectableUnit.All)
                unit.SetSelected(Selection.Contains(unit));
            if (Selection.Count > 0) SelectStructure(null);
        }
    }
}

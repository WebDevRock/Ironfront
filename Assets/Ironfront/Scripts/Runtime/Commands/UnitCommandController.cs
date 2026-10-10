using System.Collections.Generic;
using Ironfront.Core;
using Ironfront.InputBindings;
using Ironfront.Selection;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Commands
{
    /// <summary>
    /// Right-click on the ground issues a move order to the current selection, spread into a grid formation.
    /// </summary>
    public class UnitCommandController : MonoBehaviour
    {
        [SerializeField] private SelectionManager selectionManager;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private float formationSpacing = 2f;
        [SerializeField] private float maxRayDistance = 500f;

        public float FormationSpacing => formationSpacing;

        private void Awake()
        {
            if (selectionManager == null) selectionManager = FindAnyObjectByType<SelectionManager>();
        }

        private void Update()
        {
            var command = RtsInput.Command;
            if (command == null || !command.WasPressedThisFrame()) return;
            if (RtsInput.PointerOverUI()) return;

            if (TryGetGroundPoint(RtsInput.PointerPosition, out var point))
                IssueMove(point);
        }

        public bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
        {
            point = default;
            var cam = selectionManager != null ? selectionManager.ViewCamera : null;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, maxRayDistance, groundLayers, QueryTriggerInteraction.Ignore))
                return false;
            point = hit.point;
            return true;
        }

        /// <summary>Moves the selected units into formation around <paramref name="target"/>. Returns orders accepted.</summary>
        public int IssueMove(Vector3 target)
        {
            if (selectionManager == null) return 0;

            var movers = new List<UnitMover>();
            var positions = new List<Vector3>();
            foreach (var unit in selectionManager.Selection.Items)
            {
                if (unit == null) continue;
                var mover = unit.GetComponent<UnitMover>();
                if (mover == null) continue;
                movers.Add(mover);
                positions.Add(unit.transform.position);
            }
            if (movers.Count == 0) return 0;

            Vector3 facing = target - FormationCalculator.Centroid(positions);
            var slots = FormationCalculator.GetGridSlots(target, facing, movers.Count, formationSpacing);
            var assignment = FormationCalculator.AssignSlots(positions, slots);

            int accepted = 0;
            for (int i = 0; i < movers.Count; i++)
            {
                if (movers[i].MoveTo(slots[assignment[i]])) accepted++;
            }
            return accepted;
        }
    }
}

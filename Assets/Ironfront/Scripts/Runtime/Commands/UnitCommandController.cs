using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Core;
using Ironfront.Economy;
using Ironfront.InputBindings;
using Ironfront.Selection;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Commands
{
    /// <summary>
    /// Right-click orders: on the ground, the selected units move there in a grid formation; on an Ore
    /// deposit, selected harvesters harvest it; with only a factory selected, the click sets its rally point.
    /// </summary>
    public class UnitCommandController : MonoBehaviour
    {
        [SerializeField] private SelectionManager selectionManager;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private LayerMask resourceLayers = 0;
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
            if (RtsInput.PointerOverUI() || RtsInputMode.WorldPointerBlocked) return;

            Vector2 pointer = RtsInput.PointerPosition;

            var deposit = RaycastDeposit(pointer);
            bool hasGround = TryGetGroundPoint(pointer, out var point);

            if (deposit != null && IssueHarvest(deposit) > 0)
            {
                // Non-harvesters in the selection still move next to the deposit.
                if (hasGround) IssueMove(point, includeHarvesters: false);
                return;
            }

            if (!hasGround) return;

            if (selectionManager.Selection.Count == 0 && selectionManager.SelectedStructure != null)
            {
                var factory = selectionManager.SelectedStructure.GetComponent<VehicleFactory>();
                if (factory != null) factory.SetRallyPoint(point);
                return;
            }

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

        private OreDeposit RaycastDeposit(Vector2 screenPosition)
        {
            var cam = selectionManager != null ? selectionManager.ViewCamera : null;
            if (cam == null || resourceLayers == 0) return null;
            if (!Physics.Raycast(cam.ScreenPointToRay(screenPosition), out var hit, maxRayDistance, resourceLayers, QueryTriggerInteraction.Ignore))
                return null;
            var deposit = hit.collider.GetComponentInParent<OreDeposit>();
            return deposit != null && !deposit.IsDepleted ? deposit : null;
        }

        /// <summary>Orders every selected harvester to harvest <paramref name="deposit"/>. Returns how many did.</summary>
        public int IssueHarvest(OreDeposit deposit)
        {
            if (selectionManager == null || deposit == null) return 0;
            int count = 0;
            foreach (var unit in selectionManager.Selection.Items)
            {
                var harvester = unit != null ? unit.GetComponent<Harvester>() : null;
                if (harvester == null) continue;
                harvester.OrderHarvest(deposit);
                count++;
            }
            return count;
        }

        /// <summary>Moves the selected units into formation around <paramref name="target"/>. Returns orders accepted.</summary>
        /// <remarks>A move order takes harvesters out of their harvest loop until they are ordered to harvest again.</remarks>
        public int IssueMove(Vector3 target, bool includeHarvesters = true)
        {
            if (selectionManager == null) return 0;

            var movers = new List<UnitMover>();
            var positions = new List<Vector3>();
            foreach (var unit in selectionManager.Selection.Items)
            {
                if (unit == null) continue;
                var mover = unit.GetComponent<UnitMover>();
                if (mover == null) continue;

                var harvester = unit.GetComponent<Harvester>();
                if (harvester != null)
                {
                    if (!includeHarvesters) continue;
                    harvester.StopHarvesting();
                }

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

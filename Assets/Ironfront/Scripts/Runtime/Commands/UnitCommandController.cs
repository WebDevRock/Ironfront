using System;
using System.Collections.Generic;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Core;
using Ironfront.Core.Combat;
using Ironfront.Economy;
using Ironfront.InputBindings;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Commands
{
    /// <summary>
    /// Orders for the selected units. Right-click: on an enemy, armed units attack it; on an Ore deposit,
    /// harvesters harvest it; on the ground, units move there in a grid formation (or, with only a factory
    /// selected, its rally point is set). Attack-move, stop and hold are issued through
    /// <see cref="OrderInputController"/>.
    /// </summary>
    public class UnitCommandController : MonoBehaviour
    {
        [SerializeField] private SelectionManager selectionManager;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private LayerMask resourceLayers = 0;
        [Tooltip("Layers containing attackable units and structures.")]
        [SerializeField] private LayerMask targetLayers = 0;
        [SerializeField] private float formationSpacing = 2f;
        [SerializeField] private float maxRayDistance = 500f;

        public float FormationSpacing => formationSpacing;
        public SelectionManager Selection => selectionManager;

        /// <summary>Order feedback hooks (markers, HUD).</summary>
        public event Action<Vector3> MoveOrdered;
        public event Action<Vector3> AttackMoveOrdered;
        public event Action<Health> AttackOrdered;

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

            var enemy = RaycastEnemy(pointer);
            if (enemy != null && IssueAttack(enemy) > 0) return;

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

        // ---------- Raycasts ----------

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

        /// <summary>Living unit or structure under the pointer that is hostile to the player, or null.</summary>
        public Health RaycastEnemy(Vector2 screenPosition)
        {
            var cam = selectionManager != null ? selectionManager.ViewCamera : null;
            if (cam == null || targetLayers == 0) return null;
            if (!Physics.Raycast(cam.ScreenPointToRay(screenPosition), out var hit, maxRayDistance, targetLayers, QueryTriggerInteraction.Ignore))
                return null;
            var health = hit.collider.GetComponentInParent<Health>();
            if (health == null || !health.IsAlive) return null;
            return DamageRules.AreHostile(PlayerFactionId, health.FactionId) ? health : null;
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

        private static int PlayerFactionId =>
            MatchController.Current != null ? MatchController.Current.PlayerFactionId : MatchController.DefaultPlayerFactionId;

        // ---------- Orders ----------

        /// <summary>Orders every selected armed unit to attack <paramref name="target"/>. Returns how many did.</summary>
        public int IssueAttack(Health target)
        {
            if (selectionManager == null || target == null || !target.IsAlive) return 0;
            int count = 0;
            foreach (var unit in selectionManager.Selection.Items)
            {
                var combat = unit != null ? unit.GetComponent<CombatUnit>() : null;
                if (combat == null || combat.Machine == null) continue;
                combat.OrderAttack(target);
                count++;
            }
            if (count > 0) AttackOrdered?.Invoke(target);
            return count;
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
        /// <remarks>Armed units travel without engaging. A move order takes harvesters out of their harvest loop.</remarks>
        public int IssueMove(Vector3 target, bool includeHarvesters = true)
        {
            int accepted = IssueFormation(target, includeHarvesters, attackMove: false);
            if (accepted > 0) MoveOrdered?.Invoke(target);
            return accepted;
        }

        /// <summary>
        /// Armed units advance in formation to <paramref name="target"/>, engaging enemies on the way.
        /// Unarmed units in the selection simply move there.
        /// </summary>
        public int IssueAttackMove(Vector3 target)
        {
            int accepted = IssueFormation(target, includeHarvesters: true, attackMove: true);
            if (accepted > 0) AttackMoveOrdered?.Invoke(target);
            return accepted;
        }

        /// <summary>Attack the enemy under the pointer if there is one, otherwise attack-move to the ground point.</summary>
        public bool IssueAttackAtScreen(Vector2 screenPosition)
        {
            var enemy = RaycastEnemy(screenPosition);
            if (enemy != null) return IssueAttack(enemy) > 0;
            return TryGetGroundPoint(screenPosition, out var point) && IssueAttackMove(point) > 0;
        }

        /// <summary>Cancels movement and attack orders of the selection.</summary>
        public int IssueStop()
        {
            int count = 0;
            foreach (var unit in SelectedUnits())
            {
                var combat = unit.GetComponent<CombatUnit>();
                if (combat != null && combat.Machine != null) combat.OrderStop();
                else
                {
                    var harvester = unit.GetComponent<Harvester>();
                    if (harvester != null) harvester.StopHarvesting();
                    var mover = unit.GetComponent<UnitMover>();
                    if (mover != null) mover.Stop();
                }
                count++;
            }
            return count;
        }

        /// <summary>Armed units hold position (engage in range, never pursue); unarmed units stop.</summary>
        public int IssueHold()
        {
            int count = 0;
            foreach (var unit in SelectedUnits())
            {
                var combat = unit.GetComponent<CombatUnit>();
                if (combat != null && combat.Machine != null) combat.OrderHold();
                else
                {
                    var mover = unit.GetComponent<UnitMover>();
                    if (mover != null) mover.Stop();
                }
                count++;
            }
            return count;
        }

        private IEnumerable<SelectableUnit> SelectedUnits()
        {
            if (selectionManager == null) yield break;
            // Copy: orders never change the selection, but destroyed units are skipped.
            foreach (var unit in new List<SelectableUnit>(selectionManager.Selection.Items))
                if (unit != null) yield return unit;
        }

        private int IssueFormation(Vector3 target, bool includeHarvesters, bool attackMove)
        {
            if (selectionManager == null) return 0;

            var units = new List<SelectableUnit>();
            var positions = new List<Vector3>();
            foreach (var unit in SelectedUnits())
            {
                if (unit.GetComponent<UnitMover>() == null) continue;
                var harvester = unit.GetComponent<Harvester>();
                if (harvester != null && !includeHarvesters) continue;
                units.Add(unit);
                positions.Add(unit.transform.position);
            }
            if (units.Count == 0) return 0;

            Vector3 facing = target - FormationCalculator.Centroid(positions);
            var slots = FormationCalculator.GetGridSlots(target, facing, units.Count, formationSpacing);
            var assignment = FormationCalculator.AssignSlots(positions, slots);

            int accepted = 0;
            for (int i = 0; i < units.Count; i++)
            {
                Vector3 slot = slots[assignment[i]];
                var combat = units[i].GetComponent<CombatUnit>();
                if (combat != null && combat.Machine != null)
                {
                    if (attackMove) combat.OrderAttackMove(slot);
                    else combat.OrderMove(slot);
                    accepted++;
                    continue;
                }

                var harvester = units[i].GetComponent<Harvester>();
                if (harvester != null) harvester.StopHarvesting();
                if (units[i].GetComponent<UnitMover>().MoveTo(slot)) accepted++;
            }
            return accepted;
        }
    }
}

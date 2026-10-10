using System;
using Ironfront.Combat;
using Ironfront.InputBindings;
using Ironfront.Selection;
using UnityEngine;

namespace Ironfront.Commands
{
    /// <summary>
    /// Keyboard shortcuts and targeting mode for combat orders:
    /// F = attack-move (then left-click a location, or an enemy to attack it; right-click / Esc cancels),
    /// X = stop, H = hold position. The same actions are exposed for the HUD buttons.
    /// </summary>
    public class OrderInputController : MonoBehaviour
    {
        [SerializeField] private UnitCommandController commands;
        [SerializeField] private SelectionManager selection;

        public bool IsTargeting { get; private set; }

        /// <summary>Raised when targeting mode starts or ends.</summary>
        public event Action ModeChanged;

        /// <summary>Short confirmation of the last order, for the HUD.</summary>
        public event Action<string> OrderIssued;

        private void Awake()
        {
            if (commands == null) commands = FindAnyObjectByType<UnitCommandController>();
            if (selection == null) selection = FindAnyObjectByType<SelectionManager>();
        }

        private void OnDisable()
        {
            if (IsTargeting) EndTargeting();
        }

        private void Update()
        {
            if (RtsInputMode.IsPlacingBuilding)
            {
                if (IsTargeting) EndTargeting();
                return;
            }

            if (IsTargeting)
            {
                if (!HasArmedSelection) { EndTargeting(); return; }

                var cancel = RtsInput.Cancel;
                var command = RtsInput.Command;
                if ((cancel != null && cancel.WasPressedThisFrame()) || (command != null && command.WasPressedThisFrame()))
                {
                    RtsInputMode.ConsumePointerThisFrame();
                    EndTargeting();
                    return;
                }

                var select = RtsInput.Select;
                if (select != null && select.WasPressedThisFrame() && !RtsInput.PointerOverUI())
                {
                    RtsInputMode.ConsumePointerThisFrame();
                    var enemy = commands.RaycastEnemy(RtsInput.PointerPosition);
                    bool ok = commands.IssueAttackAtScreen(RtsInput.PointerPosition);
                    EndTargeting();
                    if (ok) OrderIssued?.Invoke(enemy != null ? $"Attack {enemy.DisplayName}" : "Attack-move");
                }
                return;
            }

            if (Pressed(RtsInput.AttackMove)) BeginAttackMove();
            else if (Pressed(RtsInput.Stop)) Stop();
            else if (Pressed(RtsInput.Hold)) Hold();
        }

        public bool HasArmedSelection
        {
            get
            {
                if (selection == null) return false;
                foreach (var u in selection.Selection.Items)
                    if (u != null && u.GetComponent<CombatUnit>() != null) return true;
                return false;
            }
        }

        /// <summary>Enters targeting mode; the next left click chooses the attack-move destination or target.</summary>
        public bool BeginAttackMove()
        {
            if (!HasArmedSelection || RtsInputMode.IsPlacingBuilding) return false;
            IsTargeting = true;
            RtsInputMode.IsTargeting = true;
            ModeChanged?.Invoke();
            return true;
        }

        public void CancelTargeting()
        {
            if (IsTargeting) EndTargeting();
        }

        public void Stop()
        {
            if (commands != null && commands.IssueStop() > 0) OrderIssued?.Invoke("Stop");
        }

        public void Hold()
        {
            if (commands != null && commands.IssueHold() > 0) OrderIssued?.Invoke("Hold position");
        }

        private void EndTargeting()
        {
            IsTargeting = false;
            RtsInputMode.IsTargeting = false;
            ModeChanged?.Invoke();
        }

        private static bool Pressed(UnityEngine.InputSystem.InputAction action) => action != null && action.WasPressedThisFrame();
    }
}

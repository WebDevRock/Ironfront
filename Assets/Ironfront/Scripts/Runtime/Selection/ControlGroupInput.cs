using System;
using System.Collections.Generic;
using Ironfront.CameraControl;
using Ironfront.Core;
using Ironfront.InputBindings;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.Selection
{
    /// <summary>
    /// Control groups: Ctrl+1–9/0 stores the current selection, 1–9/0 recalls it, pressing the same number
    /// twice quickly also centres the camera on the group.
    /// </summary>
    public class ControlGroupInput : MonoBehaviour
    {
        [SerializeField] private SelectionManager selection;
        [SerializeField] private RtsCameraController cameraController;
        [SerializeField] private float doubleTapWindow = 0.4f;

        private readonly ControlGroups<SelectableUnit> _groups = new ControlGroups<SelectableUnit>();
        private DoubleTapDetector _doubleTap;

        public ControlGroups<SelectableUnit> Groups => _groups;

        /// <summary>Raised with a short message ("Group 2: 5 units") for the HUD.</summary>
        public event Action<string> Feedback;

        private void Awake()
        {
            if (selection == null) selection = FindAnyObjectByType<SelectionManager>();
            if (cameraController == null) cameraController = FindAnyObjectByType<RtsCameraController>();
            _doubleTap = new DoubleTapDetector(doubleTapWindow);
        }

        private void Update()
        {
            if (RtsInputMode.IsPlacingBuilding) return;
            for (int g = 0; g < ControlGroups<SelectableUnit>.GroupCount; g++)
            {
                var action = RtsInput.ControlGroup(g);
                if (action == null || !action.WasPressedThisFrame()) continue;
                if (RtsInput.GroupModifierHeld) Assign(g);
                else Recall(g, _doubleTap.Press(g, Time.unscaledTime));
                break;
            }
        }

        public void Assign(int group)
        {
            _groups.Assign(group, selection.Selection.Items);
            Feedback?.Invoke($"Group {Label(group)} assigned: {_groups.Count(group)} units");
        }

        /// <summary>Selects the group; with <paramref name="focusCamera"/> also centres the camera on it.</summary>
        public void Recall(int group, bool focusCamera)
        {
            var members = _groups.Get(group, u => u != null && u.isActiveAndEnabled);
            if (members.Count == 0) return;
            selection.Selection.Replace(members);
            if (focusCamera && cameraController != null)
            {
                var positions = new List<Vector3>(members.Count);
                foreach (var m in members) positions.Add(m.transform.position);
                cameraController.Focus(FormationCalculator.Centroid(positions));
            }
        }

        private static string Label(int group) => group == 0 ? "0" : group.ToString();
    }
}

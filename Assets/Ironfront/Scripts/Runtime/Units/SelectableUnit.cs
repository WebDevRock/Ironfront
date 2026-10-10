using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Units
{
    /// <summary>
    /// Marks a unit as selectable by the player and drives its selection indicator.
    /// Keeps a registry of active instances so systems don't need scene searches.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectableUnit : MonoBehaviour
    {
        private static readonly List<SelectableUnit> s_All = new List<SelectableUnit>();
        public static IReadOnlyList<SelectableUnit> All => s_All;

        [SerializeField] private GameObject selectionIndicator;

        public bool IsSelected { get; private set; }

        public GameObject SelectionIndicator => selectionIndicator;

        private void OnEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
            ApplyIndicator();
        }

        private void OnDisable()
        {
            s_All.Remove(this);
        }

        public void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            ApplyIndicator();
        }

        private void ApplyIndicator()
        {
            if (selectionIndicator != null) selectionIndicator.SetActive(IsSelected);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_All.Clear();
    }
}

using System.Collections.Generic;
using Ironfront.Combat;
using Ironfront.Commands;
using Ironfront.Selection;
using UnityEngine;

namespace Ironfront.UI
{
    /// <summary>
    /// Visual order feedback: a short-lived ground marker where a move (green) or attack-move (orange)
    /// order was given and a red flash on an attacked target, plus a persistent red ring under every enemy
    /// currently targeted by a selected unit.
    /// </summary>
    public class OrderFeedback : MonoBehaviour
    {
        [SerializeField] private UnitCommandController commands;
        [SerializeField] private SelectionManager selectionManager;

        private readonly List<Transform> _rings = new List<Transform>();
        private readonly HashSet<Health> _targets = new HashSet<Health>();

        private void OnEnable()
        {
            if (commands == null) return;
            commands.MoveOrdered += OnMove;
            commands.AttackMoveOrdered += OnAttackMove;
            commands.AttackOrdered += OnAttack;
        }

        private void OnDisable()
        {
            if (commands == null) return;
            commands.MoveOrdered -= OnMove;
            commands.AttackMoveOrdered -= OnAttackMove;
            commands.AttackOrdered -= OnAttack;
        }

        private void OnMove(Vector3 p) => CombatFx.GroundMarker(p, 2.5f, CombatFx.Visuals != null ? CombatFx.Visuals.MoveMarker : null);
        private void OnAttackMove(Vector3 p) => CombatFx.GroundMarker(p, 2.5f, CombatFx.Visuals != null ? CombatFx.Visuals.AttackMoveMarker : null);

        private void OnAttack(Health target)
        {
            if (target == null) return;
            CombatFx.GroundMarker(target.transform.position, Size(target) * 1.4f,
                CombatFx.Visuals != null ? CombatFx.Visuals.AttackMarker : null, 0.6f);
        }

        private void LateUpdate()
        {
            using var perfScope = Ironfront.Diagnostics.PerfMarkers.Hud.Auto();
            _targets.Clear();
            if (selectionManager != null)
            {
                foreach (var unit in selectionManager.Selection.Items)
                {
                    var combat = unit != null ? unit.GetComponent<CombatUnit>() : null;
                    var t = combat != null ? combat.CurrentTarget : null;
                    if (t != null && t.IsAlive) _targets.Add(t);
                }
            }

            int i = 0;
            foreach (var t in _targets)
            {
                var ring = GetRing(i++);
                float size = Size(t);
                ring.position = t.transform.position + Vector3.up * 0.06f;
                ring.localScale = new Vector3(size, 0.02f, size);
                if (!ring.gameObject.activeSelf) ring.gameObject.SetActive(true);
            }
            for (; i < _rings.Count; i++)
                if (_rings[i].gameObject.activeSelf) _rings[i].gameObject.SetActive(false);
        }

        private Transform GetRing(int index)
        {
            while (_rings.Count <= index)
            {
                var go = CombatFx.Primitive(PrimitiveType.Cylinder, "TargetRing", Vector3.zero, Vector3.one,
                    CombatFx.Visuals != null ? CombatFx.Visuals.AttackMarker : null);
                go.transform.SetParent(transform, true);
                _rings.Add(go.transform);
            }
            return _rings[index];
        }

        private static float Size(Health h)
        {
            var c = h.GetComponent<Collider>();
            if (c == null) return 2f;
            var b = c.bounds.size;
            return Mathf.Max(b.x, b.z) + 0.8f;
        }
    }
}

using Ironfront.Commands;
using Ironfront.Selection;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Combat order buttons (Attack-Move [F], Stop [X], Hold [H]) plus a status line showing targeting mode
    /// or the last order. Only visible while the selection contains armed units.
    /// </summary>
    public class OrdersHud : MonoBehaviour
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private OrderInputController orders;
        [SerializeField] private SelectionManager selectionManager;
        [SerializeField] private Font font;
        [SerializeField] private float statusDuration = 3f;

        private static readonly Color ButtonColor = new Color(0.18f, 0.2f, 0.24f, 0.95f);
        private static readonly Color ActiveColor = new Color(0.55f, 0.25f, 0.1f, 0.95f);

        private Image _attackMoveImage;
        private Text _status;
        private float _statusTimer;

        private void Start()
        {
            if (panel == null || orders == null || selectionManager == null)
            {
                Debug.LogError("[Ironfront] OrdersHud is missing references.", this);
                enabled = false;
                return;
            }
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var row = new GameObject("Buttons", typeof(RectTransform));
            row.layer = panel.gameObject.layer;
            row.transform.SetParent(panel, false);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = true;
            row.AddComponent<LayoutElement>().preferredHeight = 34f;

            _attackMoveImage = CreateButton(row.transform, "Order_AttackMove", "Attack-Move [F]", () => orders.BeginAttackMove());
            CreateButton(row.transform, "Order_Stop", "Stop [X]", orders.Stop);
            CreateButton(row.transform, "Order_Hold", "Hold [H]", orders.Hold);

            var statusGo = new GameObject("Status", typeof(RectTransform));
            statusGo.layer = panel.gameObject.layer;
            statusGo.transform.SetParent(panel, false);
            _status = statusGo.AddComponent<Text>();
            _status.font = font;
            _status.fontSize = 13;
            _status.color = new Color(1f, 0.85f, 0.5f);
            _status.alignment = TextAnchor.MiddleCenter;
            _status.raycastTarget = false;
            statusGo.AddComponent<LayoutElement>().preferredHeight = 20f;

            orders.ModeChanged += Refresh;
            orders.OrderIssued += OnOrderIssued;
            selectionManager.Selection.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (orders != null)
            {
                orders.ModeChanged -= Refresh;
                orders.OrderIssued -= OnOrderIssued;
            }
            if (selectionManager != null) selectionManager.Selection.Changed -= Refresh;
        }

        private void Update()
        {
            if (_statusTimer <= 0f) return;
            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f) Refresh();
        }

        private void OnOrderIssued(string order)
        {
            _status.text = "Order: " + order;
            _statusTimer = statusDuration;
        }

        private void Refresh()
        {
            if (panel == null) return;
            bool armed = orders.HasArmedSelection;
            if (panel.gameObject.activeSelf != armed) panel.gameObject.SetActive(armed);
            if (_attackMoveImage != null) _attackMoveImage.color = orders.IsTargeting ? ActiveColor : ButtonColor;

            if (orders.IsTargeting)
            {
                _status.text = "ATTACK-MOVE: left-click a location or an enemy  (right-click / Esc cancels)";
                _statusTimer = 0f;
            }
            else if (_statusTimer <= 0f)
            {
                _status.text = "Right-click enemy: attack   Right-click ground: move";
            }
        }

        private Image CreateButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = ButtonColor;
            var button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.layer = parent.gameObject.layer;
            textGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)textGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.text = label;
            return image;
        }
    }
}

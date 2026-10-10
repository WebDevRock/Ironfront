using System;
using Ironfront.Construction;
using Ironfront.Core.Construction;
using Ironfront.InputBindings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>The single tooltip panel. Appears after a short delay next to the pointer and stays on screen.</summary>
    public class TooltipView : MonoBehaviour
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private Text label;
        [SerializeField] private float delay = 0.35f;

        private TooltipTrigger _current;
        private float _hover;

        public static TooltipView Current { get; private set; }
        public bool IsVisible => panel != null && panel.gameObject.activeSelf;
        public string ShownText => label != null ? label.text : "";

        private void Awake()
        {
            Current = this;
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        public void Show(TooltipTrigger trigger)
        {
            _current = trigger;
            _hover = 0f;
        }

        public void Hide(TooltipTrigger trigger)
        {
            if (_current != trigger) return;
            _current = null;
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_current == null || panel == null) return;
            _hover += Time.unscaledDeltaTime;
            if (_hover < delay) return;

            string text = _current.Text != null ? _current.Text() : null;
            if (string.IsNullOrEmpty(text))
            {
                panel.gameObject.SetActive(false);
                return;
            }
            label.text = text;
            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();

            // Place left of / above the pointer, clamped to the screen.
            var canvas = panel.GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            Vector2 pointer = RtsInput.PointerPosition / scale;
            Vector2 size = new Vector2(panel.sizeDelta.x, label.preferredHeight + 16f);
            panel.sizeDelta = size;
            Vector2 screen = new Vector2(Screen.width, Screen.height) / scale;
            float x = Mathf.Clamp(pointer.x - size.x - 12f, 4f, screen.x - size.x - 4f);
            float y = Mathf.Clamp(pointer.y + 12f, 4f, screen.y - size.y - 4f);
            panel.anchoredPosition = new Vector2(x, y);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}

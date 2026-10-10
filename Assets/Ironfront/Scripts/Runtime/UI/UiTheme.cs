using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>Shared HUD palette and widget helpers so panels look consistent (dark steel, blue accent).</summary>
    public static class UiTheme
    {
        public static readonly Color Panel = new Color(0.05f, 0.07f, 0.09f, 0.78f);
        public static readonly Color PanelStrong = new Color(0.04f, 0.05f, 0.07f, 0.92f);
        public static readonly Color Button = new Color(0.18f, 0.2f, 0.24f, 0.95f);
        public static readonly Color Accent = new Color(0.35f, 0.62f, 1f);
        public static readonly Color Text = new Color(0.92f, 0.94f, 0.97f);
        public static readonly Color Muted = new Color(0.65f, 0.72f, 0.8f);
        public static readonly Color Good = new Color(0.45f, 0.95f, 0.55f);
        public static readonly Color Warning = new Color(1f, 0.8f, 0.35f);
        public static readonly Color Danger = new Color(1f, 0.38f, 0.32f);

        public static Font DefaultFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Text Label(Transform parent, string name, int size, Color color, TextAnchor anchor = TextAnchor.UpperLeft, Font font = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font != null ? font : DefaultFont;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static void Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }
    }
}

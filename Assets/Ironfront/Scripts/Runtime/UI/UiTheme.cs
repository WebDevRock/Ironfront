using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>
    /// Shared HUD palette and widget helpers (Milestone 006 art direction: gunmetal panels with chamfered corners,
    /// signal-amber headings, off-white text). Panels and buttons use the generated <see cref="UiSkin"/> sprites
    /// when available and fall back to flat colour otherwise (tests, tools).
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Panel = new Color(0.075f, 0.085f, 0.095f, 0.9f);
        public static readonly Color PanelStrong = new Color(0.05f, 0.058f, 0.066f, 0.95f);
        public static readonly Color Button = new Color(0.17f, 0.19f, 0.21f, 1f);
        public static readonly Color ButtonActive = new Color(0.22f, 0.42f, 0.24f, 1f);
        public static readonly Color Accent = new Color(1f, 0.72f, 0.28f);
        public static readonly Color Text = new Color(0.93f, 0.93f, 0.9f);
        public static readonly Color Muted = new Color(0.64f, 0.67f, 0.66f);
        public static readonly Color Good = new Color(0.52f, 0.9f, 0.45f);
        public static readonly Color Warning = new Color(1f, 0.78f, 0.3f);
        public static readonly Color Danger = new Color(1f, 0.38f, 0.3f);
        public static readonly Color Progress = new Color(0.32f, 0.62f, 0.3f, 0.9f);

        /// <summary>Generated sprites; set by <see cref="UiSkinProvider"/> (runtime) or the scene builders (editor).</summary>
        public static UiSkin Skin { get; set; }

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

        /// <summary>Styles an Image as a chamfered panel (sliced skin sprite) in the given colour.</summary>
        public static void ApplyPanel(Image image, Color color)
        {
            image.color = color;
            if (Skin == null || Skin.Panel == null) return;
            image.sprite = Skin.Panel;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.5f;
        }

        /// <summary>Styles a button: sliced sprite plus hover/press/disabled tints that read on dark panels.</summary>
        public static void StyleButton(Button button, Image image, Color? baseColor = null)
        {
            image.color = baseColor ?? Button;
            if (Skin != null && Skin.Button != null)
            {
                image.sprite = Skin.Button;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1.5f;
            }
            var c = button.colors;
            c.normalColor = Color.white;
            c.highlightedColor = new Color(1.25f, 1.22f, 1.15f, 1f);
            c.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            c.selectedColor = Color.white;
            c.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.55f);
            c.colorMultiplier = 1.4f;
            c.fadeDuration = 0.06f;
            button.colors = c;
            if (button.GetComponent<UiSound>() == null) button.gameObject.AddComponent<UiSound>();
        }

        /// <summary>Bar sprite for progress fills and meters.</summary>
        public static void ApplyBar(Image image, Color color)
        {
            image.color = color;
            if (Skin == null || Skin.Bar == null) return;
            image.sprite = Skin.Bar;
            image.type = Image.Type.Sliced;
        }

        /// <summary>A drop shadow that keeps text legible over the battlefield.</summary>
        public static void Shadow(Graphic g)
        {
            if (g.GetComponent<Shadow>() != null) return;
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.75f);
            s.effectDistance = new Vector2(1.2f, -1.2f);
        }
    }
}

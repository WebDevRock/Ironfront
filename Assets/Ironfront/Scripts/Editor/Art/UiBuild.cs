using Ironfront.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Ironfront.EditorTools.Art
{
    /// <summary>Editor-side widget construction in the Ironfront UI style (used by the scene builders).</summary>
    public static class UiBuild
    {
        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = RtsSandboxBuilder.NewUiObject(name, parent);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.anchoredPosition = position;
            r.sizeDelta = size;
            return r;
        }

        public static Image Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color, bool raycast = true)
        {
            var r = Rect(name, parent, anchor, position, size);
            var img = r.gameObject.AddComponent<Image>();
            UiTheme.ApplyPanel(img, color);
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Full-screen dimmer that blocks clicks to the world while a menu is open.</summary>
        public static Image Dim(string name, Transform parent, float alpha = 0.6f)
        {
            var go = RtsSandboxBuilder.NewUiObject(name, parent);
            UiTheme.Stretch((RectTransform)go.transform);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.02f, 0.025f, 0.03f, alpha);
            img.raycastTarget = true;
            return img;
        }

        public static Text Text(string name, Transform parent, string text, int size, Color color, TextAnchor anchor, Vector2 position, Vector2 box,
            FontStyle style = FontStyle.Normal)
        {
            var r = Rect(name, parent, new Vector2(0.5f, 0.5f), position, box);
            var t = r.gameObject.AddComponent<Text>();
            t.font = UiTheme.DefaultFont;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            UiTheme.Shadow(t);
            return t;
        }

        public static Button Button(string name, Transform parent, string label, Vector2 position, Vector2 size, Color? color = null, int fontSize = 18)
        {
            var r = Rect(name, parent, new Vector2(0.5f, 0.5f), position, size);
            var img = r.gameObject.AddComponent<Image>();
            var b = r.gameObject.AddComponent<Button>();
            UiTheme.StyleButton(b, img, color);
            var t = Text("Label", r, label, fontSize, UiTheme.Text, TextAnchor.MiddleCenter, Vector2.zero, size, FontStyle.Bold);
            UiTheme.Stretch(t.rectTransform);
            return b;
        }

        /// <summary>Labelled horizontal slider [0, 1].</summary>
        public static Slider Slider(string name, Transform parent, string label, Vector2 position, float width)
        {
            var row = Rect(name, parent, new Vector2(0.5f, 0.5f), position, new Vector2(width, 30f));
            var text = Text("Label", row, label, 15, UiTheme.Text, TextAnchor.MiddleLeft, new Vector2(-width * 0.5f + 80f, 0f), new Vector2(160f, 30f));
            text.rectTransform.anchoredPosition = new Vector2(-width * 0.5f + 80f, 0f);

            float sw = width - 180f;
            var sliderRect = Rect("Slider", row, new Vector2(0.5f, 0.5f), new Vector2(width * 0.5f - sw * 0.5f, 0f), new Vector2(sw, 14f));
            var bg = sliderRect.gameObject.AddComponent<Image>();
            UiTheme.ApplyBar(bg, new Color(0f, 0f, 0f, 0.6f));
            var fillArea = RtsSandboxBuilder.NewUiObject("Fill Area", sliderRect);
            UiTheme.Stretch((RectTransform)fillArea.transform, 2f);
            var fill = RtsSandboxBuilder.NewUiObject("Fill", fillArea.transform);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            UiTheme.ApplyBar(fillImg, UiTheme.Accent);
            var handleArea = RtsSandboxBuilder.NewUiObject("Handle Slide Area", sliderRect);
            UiTheme.Stretch((RectTransform)handleArea.transform);
            var handle = RtsSandboxBuilder.NewUiObject("Handle", handleArea.transform);
            var handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(14f, 22f);
            var handleImg = handle.AddComponent<Image>();
            UiTheme.ApplyPanel(handleImg, UiTheme.Text);

            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.8f;
            return slider;
        }

        /// <summary>A card with five volume sliders and a Back button; returns (root, back button, panel component).</summary>
        public static (GameObject root, Button back) AudioOptions(Transform parent, Vector2 position)
        {
            var card = Panel("OptionsPanel", parent, new Vector2(0.5f, 0.5f), position, new Vector2(520f, 380f), UiTheme.PanelStrong);
            Text("Title", card.transform, "OPTIONS — AUDIO", 22, UiTheme.Accent, TextAnchor.MiddleCenter, new Vector2(0f, 150f), new Vector2(480f, 40f), FontStyle.Bold);
            var master = Slider("Master", card.transform, "Master", new Vector2(0f, 90f), 460f);
            var fx = Slider("Effects", card.transform, "Effects", new Vector2(0f, 45f), 460f);
            var ui = Slider("Interface", card.transform, "Interface", new Vector2(0f, 0f), 460f);
            var voice = Slider("Voice", card.transform, "Unit radio", new Vector2(0f, -45f), 460f);
            var amb = Slider("Ambience", card.transform, "Ambience", new Vector2(0f, -90f), 460f);
            var back = Button("BackButton", card.transform, "Back", new Vector2(0f, -150f), new Vector2(200f, 44f));
            var options = card.gameObject.AddComponent<AudioOptionsPanel>();
            RtsSandboxBuilder.SetRef(options, "master", master);
            RtsSandboxBuilder.SetRef(options, "effects", fx);
            RtsSandboxBuilder.SetRef(options, "interfaceVolume", ui);
            RtsSandboxBuilder.SetRef(options, "voice", voice);
            RtsSandboxBuilder.SetRef(options, "ambience", amb);
            return (card.gameObject, back);
        }

        /// <summary>In-match pause menu with options, plus the MENU button that opens it.</summary>
        public static void GameMenu(Transform canvas)
        {
            var open = Button("MenuButton", canvas, "MENU  (F10)", Vector2.zero, new Vector2(130f, 34f), null, 14);
            var openRect = (RectTransform)open.transform;
            openRect.anchorMin = openRect.anchorMax = openRect.pivot = new Vector2(1f, 0f);
            openRect.anchoredPosition = new Vector2(-10f, 10f);

            var root = Dim("GameMenu", canvas, 0.55f).gameObject;
            var card = Panel("Card", root.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 360f), UiTheme.PanelStrong);
            Text("Title", card.transform, "PAUSED", 30, UiTheme.Accent, TextAnchor.MiddleCenter, new Vector2(0f, 135f), new Vector2(340f, 50f), FontStyle.Bold);
            var resume = Button("ResumeButton", card.transform, "Resume", new Vector2(0f, 60f), new Vector2(260f, 46f));
            var options = Button("OptionsButton", card.transform, "Options", new Vector2(0f, 4f), new Vector2(260f, 46f));
            var restart = Button("RestartButton", card.transform, "Restart match", new Vector2(0f, -52f), new Vector2(260f, 46f));
            var menu = Button("MainMenuButton", card.transform, "Main menu", new Vector2(0f, -108f), new Vector2(260f, 46f));
            var (optionsRoot, back) = AudioOptions(root.transform, Vector2.zero);

            var gm = canvas.gameObject.AddComponent<Ironfront.UI.GameMenu>();
            RtsSandboxBuilder.SetRef(gm, "panel", root);
            RtsSandboxBuilder.SetRef(gm, "options", optionsRoot);
            RtsSandboxBuilder.SetRef(gm, "openButton", open);
            RtsSandboxBuilder.SetRef(gm, "resumeButton", resume);
            RtsSandboxBuilder.SetRef(gm, "optionsButton", options);
            RtsSandboxBuilder.SetRef(gm, "restartButton", restart);
            RtsSandboxBuilder.SetRef(gm, "mainMenuButton", menu);
            RtsSandboxBuilder.SetRef(gm, "optionsBackButton", back);
            root.transform.SetAsLastSibling();
            root.SetActive(false);
        }
    }
}

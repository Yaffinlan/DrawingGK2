using UnityEngine;

namespace Drawing
{
    /// <summary>
    /// The control hint, styled after the game's own build-mode legend (rounded keycap badge
    /// plus a label), but placed top-right: the game already owns the bottom-right corner
    /// during build mode.
    ///
    /// IMGUI is used on purpose - it needs no scene UI wiring and cannot fight the game's
    /// uGUI / TextMeshPro layout.
    /// </summary>
    internal sealed class ProjectionHud
    {
        private static readonly Color TextColor = new Color(0.85f, 0.70f, 0.41f);
        private static readonly Color KeyFill = new Color(0.23f, 0.29f, 0.35f);
        private static readonly Color KeyBorder = new Color(0.55f, 0.62f, 0.69f);
        private static readonly Color KeyText = new Color(0.94f, 0.96f, 0.98f);

        private readonly Settings _settings;

        private GUIStyle _label;
        private GUIStyle _keyLabel;
        private GUIStyle _info;
        private Texture2D _keyTex;

        private const float RowHeight = 26f;
        private const float KeyWidth = 30f;
        private const float KeyHeight = 22f;
        private const float KeyGap = 9f;
        private const float Margin = 18f;

        public ProjectionHud(Settings settings)
        {
            _settings = settings;
        }

        public void Draw()
        {
            var plugin = DrawingPlugin.Instance;
            if (plugin == null || !_settings.ShowHud)
            {
                return;
            }
            // Never query BuildController.Instance here: on scenes without a build controller
            // (main menu, world map) that getter logs an error on every single call, and
            // OnGUI runs every frame.
            var controller = plugin.Controller;
            if (controller?.ActiveBuildController == null)
            {
                return;
            }

            EnsureStyles();

            var rows = new[]
            {
                new Row(KeyName(_settings.PlaceKey), "Поставить"),
                new Row(KeyName(_settings.RemoveKey), "Убрать"),
                new Row(KeyName(_settings.CycleTileKey), "Тип ленты"),
                new Row(KeyName(_settings.ClearAllKey), "Очистить"),
                new Row(KeyName(_settings.ToggleVisibleKey), controller.Visible ? "Скрыть" : "Показать"),
            };

            float y = Margin;
            for (int i = 0; i < rows.Length; i++)
            {
                DrawRow(rows[i], y);
                y += RowHeight;
            }

            y += 4f;
            string zone = string.IsNullOrEmpty(controller.ZoneId) ? "?" : controller.ZoneId;
            DrawInfo(new Vector2(Screen.width - Margin, y), zone + "  ·  " + controller.Count);

            var hotkeys = plugin.Hotkeys;
            string toast = hotkeys?.CurrentToast;
            if (!string.IsNullOrEmpty(toast))
            {
                DrawToast(toast);
            }
        }

        private readonly struct Row
        {
            public readonly string Key;
            public readonly string Text;
            public Row(string key, string text) { Key = key; Text = text; }
        }

        private void DrawRow(Row row, float y)
        {
            float w = Mathf.Max(KeyWidth, _label.CalcSize(new GUIContent(row.Text)).x);
            float left = Screen.width - Margin - w;

            DrawKey(new Rect(left, y + (RowHeight - KeyHeight) * 0.5f, KeyWidth, KeyHeight), row.Key);
            GUI.Label(new Rect(left + KeyWidth + KeyGap, y, w, RowHeight), row.Text, _label);
        }

        private void DrawKey(Rect r, string key)
        {
            // Rounded keycap: a 1px border ring with a slightly lighter inner face.
            Color prev = GUI.color;
            GUI.color = KeyBorder;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = KeyFill;
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), Texture2D.whiteTexture);
            GUI.color = prev;

            var prevAlign = _keyLabel.alignment;
            _keyLabel.alignment = TextAnchor.MiddleCenter;
            GUI.Label(r, key, _keyLabel);
            _keyLabel.alignment = prevAlign;
        }

        private void DrawInfo(Vector2 anchor, string text)
        {
            var content = new GUIContent(text);
            float w = _info.CalcSize(content).x;
            GUI.Label(new Rect(anchor.x - w, anchor.y, w, 20f), content, _info);
        }

        private void DrawToast(string text)
        {
            var content = new GUIContent(text);
            float w = _info.CalcSize(content).x + 8f;
            float h = 22f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 26f, w, h);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(r, content, _info);
        }

        private static string KeyName(KeyCode k)
        {
            if (k == KeyCode.Mouse2) return "MMB";
            if (k == KeyCode.Mouse1) return "RMB";
            if (k == KeyCode.Mouse0) return "LMB";
            return k.ToString();
        }

        private void EnsureStyles()
        {
            if (_label != null)
            {
                return;
            }
            var skin = GUI.skin;
            _label = new GUIStyle(skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = TextColor }
            };
            _keyLabel = new GUIStyle(skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = KeyText }
            };
            _info = new GUIStyle(skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.62f, 0.66f, 0.70f) }
            };
            _keyTex = Texture2D.whiteTexture;
            _ = _keyTex;
        }
    }
}

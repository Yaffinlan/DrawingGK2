using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Drawing
{
    internal static class Log
    {
        public static readonly ManualLogSource Source =
            BepInEx.Logging.Logger.CreateLogSource("Drawing");

        public static void Info(string msg) => Source.LogInfo(msg);
        public static void Warn(string msg) => Source.LogWarning(msg);
        public static void Error(string msg) => Source.LogError(msg);
        public static void Error(string msg, Exception e) => Source.LogError(msg + "\n" + e);
    }

    /// <summary>
    /// User-tweakable options. Hotkeys are parsed from strings so they can be changed in
    /// BepInEx.cfg without recompiling.
    /// </summary>
    internal sealed class Settings
    {
        public KeyCode PlaceKey = KeyCode.Mouse2;
        public KeyCode RemoveKey = KeyCode.Delete;
        public KeyCode ClearAllKey = KeyCode.F9;
        public KeyCode ToggleVisibleKey = KeyCode.H;
        public KeyCode CycleTileKey = KeyCode.C;
        public KeyCode InspectVariantsKey = KeyCode.F8;

        public bool ShowHud = true;
        public bool VerboseLogging = false;
        public int MaxProjections = 200;
        public float Opacity = 0.38f;
        public Color ValidTint = new Color(0.31f, 0.76f, 0.97f, 1f);
        public Color InvalidTint = new Color(1f, 0.44f, 0.26f, 1f);
        public bool RemoveShadows = true;

        public VariantMode VariantMode = VariantMode.Finished;

        private readonly Dictionary<string, string> _variantOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void Load(ConfigFile cfg)
        {
            PlaceKey = ParseKey(cfg.Bind("Hotkeys", "PlaceProjection", "Mouse2",
                "Key that stamps a projection at the current build cursor. Default: Mouse2 (middle mouse button).").Value, KeyCode.Mouse2);

            RemoveKey = ParseKey(cfg.Bind("Hotkeys", "RemoveProjection", "Delete",
                "Key that deletes the projection under the build cursor. Default: Delete.").Value, KeyCode.Delete);

            ClearAllKey = ParseKey(cfg.Bind("Hotkeys", "ClearAllProjections", "F9",
                "Key that removes every projection in the current zone. Default: F9.").Value, KeyCode.F9);

            ToggleVisibleKey = ParseKey(cfg.Bind("Hotkeys", "ToggleProjectionVisibility", "H",
                "Key that shows/hides all projections in the current zone. Default: H.").Value, KeyCode.H);

            CycleTileKey = ParseKey(cfg.Bind("Hotkeys", "CycleConveyorTile", "C",
                "Key that cycles the conveyor tile under the build cursor: " +
                "auto -> centre -> end -> single. Default: C.").Value, KeyCode.C);

            InspectVariantsKey = ParseKey(cfg.Bind("Hotkeys", "InspectModelVariants", "F8",
                "Key that logs every model variation of the selected blueprint to BepInEx\\LogOutput.log. " +
                "Use it once per building to learn the variation ids, then pin them in [VariantOverrides].").Value, KeyCode.F8);

            ShowHud = cfg.Bind("Display", "ShowHud", true,
                "Show the small on-screen legend while in build mode.").Value;

            Opacity = Mathf.Clamp01(cfg.Bind("Display", "ProjectionOpacity", 0.38f,
                "0 = invisible, 1 = fully opaque. Projections are always non-solid.").Value);

            RemoveShadows = cfg.Bind("Display", "DisableProjectionShadows", true,
                "Stop projections from casting shadows.").Value;

            ValidTint = ParseColor(cfg.Bind("Display", "TintValidPlacement", "#4FC3F7",
                "Tint of a projection stamped where the real blueprint would fit.").Value, ValidTint);

            InvalidTint = ParseColor(cfg.Bind("Display", "TintInvalidPlacement", "#FF7043",
                "Tint of a projection stamped where the real blueprint would NOT fit.").Value, InvalidTint);

            MaxProjections = Mathf.Clamp(
                cfg.Bind("Safety", "MaxProjectionsPerZone", 200,
                    "Hard cap on projections per zone. Protects against a stuck key spamming ghosts.").Value,
                1, 2000);

            VerboseLogging = cfg.Bind("Debug", "VerboseLogging", false,
                "Log every projection that gets stamped (wgo id, rotation, position). " +
                "Turn on when a specific blueprint misbehaves.").Value;

            string mode = cfg.Bind("Appearance", "VariantMode", "finished",
                "Which model to show for a projection:\n" +
                "  finished - the built machine (the variation the build cursor does NOT show)\n" +
                "  default  - whatever the object considers its default state\n" +
                "  cursor   - copy the build cursor's variation (shows the scaffold)").Value;
            switch ((mode ?? "finished").Trim().ToLowerInvariant())
            {
                case "cursor": VariantMode = VariantMode.Cursor; break;
                case "default": VariantMode = VariantMode.Default; break;
                default: VariantMode = VariantMode.Finished; break;
            }

            // Per-blueprint pinning. Format: "blueprintId=variationId" separated by ';' or newlines.
            // Kept as one string so it survives round-tripping through BepInEx's config file.
            string raw = cfg.Bind("Appearance", "VariantOverrides", "",
                "Pin the model per blueprint, semicolon separated: blueprintId=variationId\n" +
                "Example: conveyor_furnace_t1_place=conveyor_furnace_t1\n" +
                "Press F8 in build mode to list the variation ids of the selected blueprint.").Value;
            LoadVariantOverrides(raw);
        }

        private void LoadVariantOverrides(string raw)
        {
            _variantOverrides.Clear();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }
            string[] parts = raw.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    Log.Warn("VariantOverrides: cannot parse '" + part.Trim() + "' (expected id=variation)");
                    continue;
                }
                string id = part.Substring(0, eq).Trim();
                string variation = part.Substring(eq + 1).Trim();
                if (id.Length > 0 && variation.Length > 0)
                {
                    _variantOverrides[id] = variation;
                }
            }
            if (_variantOverrides.Count > 0)
            {
                Log.Info("VariantOverrides: " + _variantOverrides.Count + " pinned blueprint(s).");
            }
        }

        public string VariantOverrideFor(string blueprintId)
        {
            if (string.IsNullOrEmpty(blueprintId))
            {
                return null;
            }
            return _variantOverrides.TryGetValue(blueprintId, out string v) ? v : null;
        }

        private static KeyCode ParseKey(string raw, KeyCode fallback)
        {
            if (!string.IsNullOrWhiteSpace(raw) &&
                Enum.TryParse(raw.Trim(), true, out KeyCode parsed) &&
                Enum.IsDefined(typeof(KeyCode), parsed))
            {
                return parsed;
            }
            Log.Warn("Could not parse KeyCode '" + raw + "', using " + fallback);
            return fallback;
        }

        private static Color ParseColor(string raw, Color fallback)
        {
            if (!string.IsNullOrWhiteSpace(raw) && ColorUtility.TryParseHtmlString(raw.Trim(), out Color parsed))
            {
                return parsed;
            }
            Log.Warn("Could not parse colour '" + raw + "', using " + fallback);
            return fallback;
        }
    }
}

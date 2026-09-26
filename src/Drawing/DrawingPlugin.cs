using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Drawing
{
    /// <summary>
    /// Drawing - a planning aid for Graveyard Keeper 2.
    ///
    /// While in build mode you can stamp a translucent, weightless "drawing" of the currently
    /// selected blueprint. Drawings cost nothing, ignore build limits, never block a real
    /// placement and are stored in their own JSON file, so the game save is never modified.
    ///
    /// Default keys (all rebindable in BepInEx.cfg):
    ///   Mouse2          stamp a drawing at the build cursor
    ///   Delete          delete the drawing under the build cursor
    ///   F9              clear every drawing in the current zone
    ///   H               show / hide all drawings
    ///   F8              log the model variations of the selected blueprint
    /// </summary>
    [BepInPlugin(PluginGuid, "Drawing", PluginVersion)]
    [BepInProcess("GraveyardKeeper2.exe")]
    public class DrawingPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "drawing.gk2";
        public const string PluginVersion = "1.0.0";

        private Settings _settings;
        private ProjectionController _controller;
        private ProjectionHud _hud;
        private ProjectionHotkeys _hotkeys;

        public static DrawingPlugin Instance { get; private set; }

        internal Settings Settings => _settings;
        internal ProjectionController Controller => _controller;
        internal ProjectionHotkeys Hotkeys => _hotkeys;

        private void Awake()
        {
            Instance = this;
            _settings = new Settings();
            _settings.Load(Config);

            _controller = new ProjectionController(_settings);
            _hud = new ProjectionHud(_settings);
            _hotkeys = new ProjectionHotkeys(_settings, _controller);

            try
            {
                new Harmony(PluginGuid).PatchAll();
                Log.Info("Harmony hooks installed.");
            }
            catch (System.Exception e)
            {
                Log.Error("Could not install Harmony hooks - the mod will not work.", e);
                return;
            }

            Log.Info("Ready. Version " + PluginVersion +
                     " | variant mode: " + _settings.VariantMode +
                     " | drawings file: " + ProjectionStore.FilePath);
        }

        private void Update()
        {
            _hotkeys.Tick();
        }

        private void OnGUI()
        {
            _hud.Draw();
        }

        private void OnApplicationQuit()
        {
            _controller?.LeaveZone();
            ProjectionStore.Flush();
            _controller?.Dispose();
        }

        private void OnDestroy()
        {
            _controller?.LeaveZone();
            ProjectionStore.Flush();
            _controller?.Dispose();
        }
    }
}


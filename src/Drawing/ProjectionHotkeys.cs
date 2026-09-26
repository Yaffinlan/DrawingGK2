using UnityEngine;

namespace Drawing
{
    /// <summary>
    /// Translates raw Unity key presses into projection actions. The mod uses its own keys so
    /// the player's real bindings (rotate / build / back) keep working untouched, and it only
    /// polls while build mode is actually open.
    /// </summary>
    internal sealed class ProjectionHotkeys
    {
        private readonly Settings _settings;
        private readonly ProjectionController _controller;

        public ProjectionHotkeys(Settings settings, ProjectionController controller)
        {
            _settings = settings;
            _controller = controller;
        }

        public void Tick()
        {
            BuildController bc = _controller.ActiveBuildController;
            if (bc == null)
            {
                return;
            }

            // No time gating here. Update() already runs exactly once per frame and
            // Input.GetKeyDown is true on exactly one frame per press, so any extra gate
            // silently *eats* presses: at 60 fps a 0.02 s gate passes on every other frame
            // only, which made every second click do nothing.
            if (IsDown(_settings.ClearAllKey))
            {
                int n = _controller.ClearAll();
                Toast(n > 0 ? "Cleared " + n + " drawing(s)" : "No drawings to clear");
                return;
            }

            if (IsDown(_settings.ToggleVisibleKey))
            {
                _controller.ToggleVisible();
                Toast(_controller.Visible ? "Drawings shown" : "Drawings hidden");
                return;
            }

            if (IsDown(_settings.InspectVariantsKey))
            {
                WgoBuildPointer inspect = FindPointer(bc);
                if (inspect?.Target == null)
                {
                    Toast("Pick a blueprint first");
                    return;
                }
                Log.Info("[Inspect] " + VariantSelector.Describe(inspect.Target, null));
                Toast("Variations written to BepInEx\\LogOutput.log");
                return;
            }

            if (IsDown(_settings.PlaceKey))
            {
                WgoBuildPointer pointer = FindPointer(bc);
                if (pointer == null)
                {
                    Toast("Pick a placeable blueprint (remove mode has none)");
                    return;
                }
                if (_controller.Stamp(pointer))
                {
                    Toast("Drawing placed");
                }
                else
                {
                    // Surface the real reason instead of a generic message - this used to be
                    // the single biggest source of "it only works half the time" confusion.
                    Toast("Not placed: " + _controller.LastFailure);
                }
                return;
            }

            if (IsDown(_settings.RemoveKey))
            {
                if (_controller.RemoveUnderCursor(ResolveBuildCamera(), Input.mousePosition))
                {
                    Toast("Drawing removed");
                }
            }

            if (IsDown(_settings.CycleTileKey))
            {
                string tile = _controller.CycleTileUnderCursor(ResolveBuildCamera(), Input.mousePosition);
                Toast(tile == null ? "No conveyor cell under the cursor" : "Tile: " + tile);
            }
        }

        /// <summary>
        /// Only a plain press counts. If a player rebinds a mod key onto a game key, holding a
        /// modifier turns the mod action off rather than firing alongside the game action.
        /// </summary>
        private static bool IsDown(KeyCode key)
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                return false;
            }
            return Input.GetKeyDown(key);
        }

        /// <summary>
        /// BuildController keeps its BuildPointer in a private field, but BuildPointer is a
        /// Unity component on a child object, so a lookup is enough and needs no reflection.
        /// </summary>
        private static WgoBuildPointer FindPointer(BuildController bc)
        {
            BuildPointer bp = bc.GetComponentInChildren<BuildPointer>(true);
            return bp?.PointerObject as WgoBuildPointer;
        }

        private static Camera ResolveBuildCamera()
        {
            try
            {
                // In build mode the active rig is a Cinemachine vcam, but it drives the same
                // world camera component, so this is what actually renders the grid.
                CameraSystem cs = CameraSystem.Instance;
                if (cs != null)
                {
                    Camera c = cs.MainCamera?.Camera;
                    if (c != null)
                    {
                        return c;
                    }
                }
            }
            catch
            {
                // fall through to the generic lookups
            }

            if (Camera.main != null)
            {
                return Camera.main;
            }
            Camera[] all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].isActiveAndEnabled)
                {
                    return all[i];
                }
            }
            return null;
        }

        public void Toast(string message)
        {
            ToastMessage = message;
            ToastUntil = Time.unscaledTime + 2.2f;
        }

        public string ToastMessage { get; private set; }
        public float ToastUntil { get; private set; }

        public string CurrentToast => Time.unscaledTime < ToastUntil ? ToastMessage : null;
    }
}



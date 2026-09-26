using HarmonyLib;
using UnityEngine;

namespace Drawing
{
    /// <summary>Build mode opened: bring the plan for this zone back to life.</summary>
    [HarmonyPatch(typeof(BuildController), nameof(BuildController.EnableBuildMode))]
    internal static class Patch_EnableBuildMode
    {
        private static void Postfix(BuildController __instance, BuildData buildData, WorldZone worldZone)
        {
            var plugin = DrawingPlugin.Instance;
            if (plugin?.Controller == null || worldZone == null)
            {
                return;
            }
            try
            {
                GameScene scene = MainGame.PlayerController?.CurrentGameScene;
                plugin.Controller.EnterZone(worldZone, scene, __instance);
            }
            catch (System.Exception e)
            {
                Log.Error("EnterZone failed", e);
            }
        }
    }

    /// <summary>Build mode closed: persist and tear the ghosts down.</summary>
    [HarmonyPatch(typeof(BuildController), nameof(BuildController.DisableBuildMode))]
    internal static class Patch_DisableBuildMode
    {
        private static void Postfix()
        {
            try
            {
                DrawingPlugin.Instance?.Controller?.LeaveZone();
            }
            catch (System.Exception e)
            {
                Log.Error("LeaveZone failed", e);
            }
        }
    }

    /// <summary>
    /// A mechanism was really placed. Retire the projection that was standing in the same
    /// spot, so the plan reflects what actually exists.
    /// </summary>
    [HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.TryDoBuildAction))]
    internal static class Patch_WgoBuildPointer_TryDoBuildAction
    {
        private static void Postfix(WgoBuildPointer __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }
            try
            {
                Wgo target = __instance.Target;
                if (target?.Data == null)
                {
                    return;
                }
                DrawingPlugin.Instance?.Controller?
                    .RetireNear(__instance.BuildData?.WgoId, target.Data.Position);
            }
            catch (System.Exception e)
            {
                Log.Error("RetireNear failed", e);
            }
        }
    }

    /// <summary>Same, for conveyor cells (ConveyorBuildPointer overrides the method).</summary>
    [HarmonyPatch(typeof(ConveyorBuildPointer), nameof(ConveyorBuildPointer.TryDoBuildAction))]
    internal static class Patch_ConveyorBuildPointer_TryDoBuildAction
    {
        private static void Postfix(ConveyorBuildPointer __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }
            try
            {
                Wgo target = __instance.Target;
                if (target?.Data == null)
                {
                    return;
                }
                DrawingPlugin.Instance?.Controller?
                    .RetireNear(__instance.BuildData?.WgoId, target.Data.Position);
            }
            catch (System.Exception e)
            {
                Log.Error("RetireNear (conveyor) failed", e);
            }
        }
    }

    /// <summary>
    /// Safety net for scene unloads and other exits that bypass DisableBuildMode:
    /// if the game is no longer in build mode but we still hold a zone, drop it.
    /// </summary>
    [HarmonyPatch(typeof(MainGame), nameof(MainGame.Update))]
    internal static class Patch_MainGame_Update
    {
        private static void Postfix()
        {
            var plugin = DrawingPlugin.Instance;
            if (plugin?.Controller == null || plugin.Controller.Zone == null)
            {
                return;
            }
            // ActiveBuildController is null once build mode closes or the scene unloads.
            if (plugin.Controller.ActiveBuildController == null)
            {
                try
                {
                    plugin.Controller.LeaveZone();
                }
                catch (System.Exception e)
                {
                    Log.Error("Safety-net LeaveZone failed", e);
                }
            }
        }
    }
}



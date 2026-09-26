using System;
using System.Collections.Generic;
using UnityEngine;

namespace Drawing
{
    /// <summary>Runtime view of one stored projection.</summary>
    internal sealed class ProjectionGhost
    {
        public ProjectionEntry Entry;
        public Wgo Wgo;
        public GameObject Root;
        public Color Tint;
        public Bounds WorldBounds;
    }

    /// <summary>
    /// Owns the ghost objects for the zone that is currently in build mode.
    /// Nothing here is registered with the save system, the chunk manager's world list or
    /// physics, so projections can never block a real build or corrupt a save.
    /// </summary>
    internal sealed class ProjectionController
    {
        private readonly Settings _settings;
        private readonly List<ProjectionGhost> _ghosts = new List<ProjectionGhost>();

        private GameObject _container;
        private WorldZone _zone;
        private string _zoneId;
        private GameScene _scene;
        private BuildController _buildController;
        private bool _visible = true;

        // Blueprints whose variation list we already dumped to the log, so the log stays
        // readable when the same workbench is projected ten times.
        private readonly HashSet<string> _inspectedIds = new HashSet<string>(StringComparer.Ordinal);

        public int Count => _ghosts.Count;
        public bool Visible => _visible;
        public string ZoneId => _zoneId;
        public WorldZone Zone => _zone;

        /// <summary>Why the last <see cref="Stamp"/> refused. Shown in the on-screen toast.</summary>
        public string LastFailure { get; private set; } = "";
        public List<ProjectionGhost> Ghosts => _ghosts;

        public ProjectionController(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Cached reference to the live build controller. The mod deliberately never touches
        /// <c>BuildController.Instance</c> while outside build mode: that getter logs an error
        /// on every call in a scene without a build controller (main menu, world map, ...),
        /// which would flood Player.log.
        /// </summary>
        public BuildController ActiveBuildController =>
            _buildController != null && _buildController.IsBuildModeActive ? _buildController : null;

        public void EnterZone(WorldZone zone, GameScene scene, BuildController buildController)
        {
            LeaveZone();
            if (zone == null || scene == null)
            {
                return;
            }
            _zone = zone;
            _scene = scene;
            _zoneId = zone.Id;
            _buildController = buildController;

            _container = new GameObject("GK2_Projections_" + _zoneId);
            _container.transform.SetParent(scene.transform, false);
            // The container deliberately stays active even when empty. Wgo.Spawn and
            // UpdateChunkVisibility expect an active hierarchy; spawning under an inactive
            // parent produced ghosts that never became visible. An empty active container
            // costs nothing.

            var stored = ProjectionStore.GetZone(_zoneId);
            int failed = 0;
            foreach (ProjectionEntry entry in stored)
            {
                if (!TrySpawn(entry, _settings.ValidTint, out _))
                {
                    failed++;
                }
            }
            _container.SetActive(_visible);
            Log.Info("Projections: entered zone '" + _zoneId + "', " + _ghosts.Count +
                     " ghost(s) restored" + (failed > 0 ? (" (" + failed + " failed to spawn - unknown blueprint id?)") : ""));
        }

        public void LeaveZone()
        {
            if (_ghosts.Count > 0 || _container != null)
            {
                ProjectionStore.Flush();
            }
            ClearGhosts();
            _zone = null;
            _scene = null;
            _zoneId = null;
            _buildController = null;
        }

        private void ClearGhosts()
        {
            _ghosts.Clear();
            if (_container != null)
            {
                UnityEngine.Object.Destroy(_container);
                _container = null;
            }
            // NOTE: ghost materials are deliberately NOT released here. They are stateless and
            // shared per (texture, tint), so keeping them across zone changes avoids rebuilding
            // ~100 materials every time build mode is re-entered. They are freed on shutdown.
        }

        /// <summary>Called once when the plugin goes away.</summary>
        public void Dispose()
        {
            ClearGhosts();
            Ghostifier.ReleaseOwnedMaterials();
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Stamps a projection that mirrors the blueprint currently under the cursor.</summary>
        public bool Stamp(WgoBuildPointer pointer)
        {
            if (pointer == null)
            {
                LastFailure = "no blueprint selected";
                return false;
            }
            if (_container == null)
            {
                LastFailure = "zone not ready (re-open build mode)";
                return false;
            }
            BuildData buildData = pointer.BuildData;
            Wgo target = pointer.Target;
            if (buildData == null)
            {
                LastFailure = "no blueprint selected";
                return false;
            }
            if (target == null || target.Data == null)
            {
                LastFailure = "the build cursor has no preview yet";
                return false;
            }
            BuildingDef def = buildData.Definition;
            if (def != null && def.buildingMode == BuildingDef.BuildingMode.Remove)
            {
                LastFailure = "remove mode has nothing to project";
                return false;
            }

            // Exactly the data a real build would use (see WgoBuildPointer.TryDoBuildAction).
            string wgoId = buildData.WgoId;
            if (string.IsNullOrEmpty(wgoId))
            {
                LastFailure = "blueprint has no object id";
                return false;
            }
            Vector3 pos = target.Data.Position;

            if (_settings.VerboseLogging)
            {
                Log.Info("stamp: id='" + wgoId + "' def='" + (def != null ? def.id : "?") +
                         "' mode=" + (def != null ? def.buildingMode.ToString() : "?") +
                         " preview='" + (def != null ? def.customWgoPlacePreview : "") +
                         "' targetId='" + target.Data.id + "' pos=" + pos +
                         " hasWgoPart=" + (target.MainWgoPart != null));
            }

            string variationId = null;
            int rotationIndex = -1;
            try
            {
                WgoPart part = target.MainWgoPart;
                if (part != null && part.WgoPartData != null && target.CanBeRotated() &&
                    part.WgoPartData.rotationIndex != -1)
                {
                    variationId = part.WgoPartData.variationId;
                    rotationIndex = part.WgoPartData.rotationIndex;
                }
            }
            catch (Exception e)
            {
                Log.Warn("Could not read pointer rotation: " + e.Message);
            }

            var entry = new ProjectionEntry(wgoId, pos, variationId, rotationIndex);

            // Stamping the same cell again replaces what is there instead of failing: that is
            // how you change the blueprint or spin a mechanism you already projected. Only one
            // projection per cell makes sense - two of them would be a contradictory plan.
            ProjectionGhost sameCell = FindAt(pos, SameCellSqrDistance);
            if (sameCell != null)
            {
                ProjectionStore.Remove(_zoneId, sameCell.Entry);
                DestroyGhost(sameCell);
            }

            if (_ghosts.Count >= _settings.MaxProjections)
            {
                LastFailure = "zone limit reached (" + _settings.MaxProjections + ")";
                Log.Warn("Refusing to add projection: zone '" + _zoneId + "' already holds " +
                         _ghosts.Count + " (cap " + _settings.MaxProjections + ").");
                return false;
            }

            if (!TrySpawn(entry, PointerTint(pointer), out ProjectionGhost ghost) || ghost == null)
            {
                LastFailure = "object '" + wgoId + "' could not be spawned";
                return false;
            }
            ProjectionStore.Add(_zoneId, entry);
            if (_container != null)
            {
                _container.SetActive(_visible);
            }
            // A new cell changes its neighbours' tile types, so re-tile the whole belt run.
            if (ConveyorTiler.IsAutoTileable(wgoId))
            {
                RetileConveyors();
            }
            return true;
        }

        private const float SameCellSqrDistance = 0.0004f; // (0.02 world units) squared

        // ---------------------------------------------------------------- conveyor tiling

        /// <summary>
        /// Recomputes the tile type of every conveyor cell in the zone from its neighbours, so
        /// a chain of belts reads as a chain: through-pieces in the middle, terminated ends at
        /// the extremities, lone tiles by themselves. The build cursor always draws 'centre',
        /// which is why a plan made with it never showed where a belt line began or ended.
        /// </summary>
        public void RetileConveyors()
        {
            var cells = new List<ConveyorTiler.Cell>();
            var ghosts = new List<ProjectionGhost>();
            foreach (ProjectionGhost g in _ghosts)
            {
                if (g?.Entry == null || !ConveyorTiler.IsAutoTileable(g.Entry.WgoId))
                {
                    continue;
                }
                cells.Add(new ConveyorTiler.Cell(g.Entry.Position.x, g.Entry.Position.z, g.Entry.WgoId));
                ghosts.Add(g);
            }
            if (cells.Count == 0)
            {
                return;
            }

            int changed = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                ProjectionGhost ghost = ghosts[i];
                ConveyorTiler.TileResult tile = ConveyorTiler.Resolve(
                    ConveyorTiler.NeighboursOf(cells[i], cells),
                    ghost.Entry.TileChoice,
                    KnownVariations(ghost.Wgo));

                if (string.Equals(ghost.Entry.VariationId, tile.Variation, StringComparison.Ordinal) &&
                    ghost.Entry.RotationIndex == tile.Rotation)
                {
                    continue;
                }
                ghost.Entry.VariationId = tile.Variation;
                ghost.Entry.RotationIndex = tile.Rotation;
                RespawnGhost(ghost);
                changed++;
            }
            if (changed > 0)
            {
                ProjectionStore.Flush();
            }
        }

        private static string[] KnownVariations(Wgo wgo)
        {
            List<WgoPartState> variations = wgo?.MainWgoPart?.Variations;
            if (variations == null || variations.Count == 0)
            {
                return null;
            }
            var ids = new List<string>(variations.Count);
            foreach (WgoPartState v in variations)
            {
                if (v != null && !string.IsNullOrEmpty(v.variationId) && !ids.Contains(v.variationId))
                {
                    ids.Add(v.variationId);
                }
            }
            return ids.ToArray();
        }

        /// <summary>Throws away and re-spawns one ghost so it picks up its new state.</summary>
        private void RespawnGhost(ProjectionGhost ghost)
        {
            ProjectionEntry entry = ghost.Entry;
            Color tint = ghost.Tint;
            if (ghost.Wgo != null)
            {
                UnityEngine.Object.Destroy(ghost.Wgo.gameObject);
            }
            _ghosts.Remove(ghost);
            if (_container != null)
            {
                _container.SetActive(true);
            }
            TrySpawn(entry, tint, out _);
        }

        /// <summary>Cycles a cell's tile by hand: auto -> centre -> end -> single -> auto.</summary>
        public string CycleTileUnderCursor(Camera cam, Vector2 screenPos)
        {
            ProjectionGhost hit = PickUnderCursor(cam, screenPos, 90f);
            if (hit?.Entry == null || !ConveyorTiler.IsAutoTileable(hit.Entry.WgoId))
            {
                LastFailure = "no conveyor cell under the cursor";
                return null;
            }
            hit.Entry.SetTile((ConveyorTile)(((int)hit.Entry.TileChoice + 1) % 4));
            RetileConveyors();
            LastFailure = "";
            return hit.Entry.TileChoice == ConveyorTile.Auto ? "auto" : hit.Entry.VariationId;
        }

        private ProjectionGhost FindAt(Vector3 pos, float sqrDistance)
        {
            foreach (ProjectionGhost g in _ghosts)
            {
                if ((g.Entry.Position - pos).sqrMagnitude < sqrDistance)
                {
                    return g;
                }
            }
            return null;
        }

        private Color PointerTint(WgoBuildPointer pointer)
        {
            bool valid = IsPointerValid(pointer);
            return valid ? _settings.ValidTint : _settings.InvalidTint;
        }

        private static bool IsPointerValid(WgoBuildPointer pointer)
        {
            // shownAsActive is the game's own "would this build succeed right now" flag.
            try
            {
                System.Reflection.FieldInfo f = typeof(BuildPointerObject)
                    .GetField("shownAsActive", System.Reflection.BindingFlags.Instance |
                                                  System.Reflection.BindingFlags.NonPublic);
                if (f != null && f.GetValue(pointer) is bool b)
                {
                    return b;
                }
            }
            catch (Exception e)
            {
                Log.Warn("Could not read pointer validity: " + e.Message);
            }
            return true;
        }

        /// <summary>Drops a projection that is visually under the build cursor.</summary>
        public bool RemoveUnderCursor(Camera cam, Vector2 screenPos)
        {
            ProjectionGhost hit = PickUnderCursor(cam, screenPos, 90f);
            if (hit == null)
            {
                return false;
            }
            DestroyGhost(hit);
            ProjectionStore.Remove(_zoneId, hit.Entry);
            if (ConveyorTiler.IsAutoTileable(hit.Entry.WgoId))
            {
                RetileConveyors();
            }
            return true;
        }

        public int ClearAll()
        {
            int n = _ghosts.Count;
            ClearGhosts();
            ProjectionStore.ClearZone(_zoneId);
            if (_scene != null)
            {
                _container = new GameObject("GK2_Projections_" + _zoneId);
                _container.transform.SetParent(_scene.transform, false);
            }
            return n;
        }

        public void ToggleVisible()
        {
            _visible = !_visible;
            if (_container != null)
            {
                _container.SetActive(_visible);
            }
        }

        /// <summary>
        /// Called after a mechanism was really built: retire the matching projection so the
        /// plan does not keep showing something that now exists for real.
        /// </summary>
        public void RetireNear(string wgoId, Vector3 pos, float radius = 0.05f)
        {
            if (_ghosts.Count == 0)
            {
                return;
            }
            float r2 = radius * radius;
            var doomed = new List<ProjectionGhost>();
            foreach (ProjectionGhost g in _ghosts)
            {
                if (!string.Equals(g.Entry.WgoId, wgoId, StringComparison.Ordinal))
                {
                    continue;
                }
                if ((g.Entry.Position - pos).sqrMagnitude <= r2)
                {
                    doomed.Add(g);
                }
            }
            foreach (ProjectionGhost g in doomed)
            {
                DestroyGhost(g);
                ProjectionStore.Remove(_zoneId, g.Entry);
            }
            if (doomed.Count > 0 && ConveyorTiler.IsAutoTileable(wgoId))
            {
                RetileConveyors();
            }
        }

        private void DestroyGhost(ProjectionGhost ghost)
        {
            if (ghost?.Root != null)
            {
                UnityEngine.Object.Destroy(ghost.Root);
            }
            _ghosts.Remove(ghost);
        }

        // ------------------------------------------------------------------ picking

        public ProjectionGhost PickUnderCursor(Camera cam, Vector2 screenPos, float radiusPixels)
        {
            if (cam == null || _ghosts.Count == 0)
            {
                return null;
            }
            ProjectionGhost best = null;
            float bestDist = float.MaxValue;
            float bestDepth = float.MaxValue;

            foreach (ProjectionGhost g in _ghosts)
            {
                if (g.Root == null || g.Wgo == null)
                {
                    continue;
                }
                Vector3 viewport = cam.WorldToViewportPoint(g.Entry.Position);
                if (viewport.z <= 0f)
                {
                    continue; // behind the build camera
                }
                Vector2 pixel = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
                float dist = (pixel - screenPos).magnitude;
                if (dist > radiusPixels)
                {
                    continue;
                }
                if (viewport.z < bestDepth - 0.05f)
                {
                    continue; // something is clearly closer to the camera
                }
                if (dist < bestDist - 0.5f)
                {
                    best = g;
                    bestDist = dist;
                    bestDepth = viewport.z;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>
        /// Builds the same WgoData the game itself would build for this blueprint.
        ///
        /// Conveyor elements are the important case: the game places them with
        /// <see cref="ConveyorWgoData"/>, whose constructor creates a ConveyorComponent. A plain
        /// WgoData leaves that component out, so a conveyor cell rendered as a drawing showed
        /// only its bare frame and direction markers instead of an actual belt - which made it
        /// impossible to judge where a belt would even fit.
        ///
        /// The decision mirrors ConveyorBuildPointer vs WgoBuildPointer: only blueprints whose
        /// BuildingMode is ConveyorPlace get a ConveyorWgoData. Mode is read from
        /// GameBalance.buildableWgos (keyed by wgoId) so that drawings loaded from disk - which
        /// store no mode - are classified correctly without a save-format change.
        /// </summary>
        /// <summary>Writes the tile variation and rotation onto the data, the way the game does.</summary>
        private static void ApplyState(WgoData data, string variationId, int rotationIndex)
        {
            if (data.MainWgoPartData == null)
            {
                return;
            }
            if (!string.IsNullOrEmpty(variationId))
            {
                data.MainWgoPartData.variationId = variationId;
            }
            if (rotationIndex >= 0)
            {
                data.MainWgoPartData.rotationIndex = rotationIndex;
            }
        }

        /// <summary>
        /// The spawn resolves the WGO's default state, which may not be the one we asked for -
        /// and for conveyor cells, whose only listed state is rot=0, it also clobbers the
        /// rotation we set. Re-assert both afterwards: variation first, then rotation, because
        /// applying a variation rewrites rotationIndex from the matched state.
        /// </summary>
        private static void ReassertState(Wgo wgo, string variationId, int rotationIndex)
        {
            WgoPart part = wgo?.MainWgoPart;
            if (part == null || part.WgoPartData == null)
            {
                return;
            }
            if (!string.IsNullOrEmpty(variationId) &&
                !string.Equals(part.WgoPartData.variationId, variationId, StringComparison.Ordinal))
            {
                WgoPartState st = part.GetWgoPartState(variationId, -1);
                if (st != null)
                {
                    part.ApplyWgoPartState(variationId, st.rotationIndex);
                }
                else
                {
                    Log.Warn("Requested variation '" + variationId + "' does not exist on '" +
                             part.Id + "' (have: " + ListVariationIds(part) + ")");
                }
            }
            if (rotationIndex >= 0)
            {
                part.WgoPartData.rotationIndex = rotationIndex;
            }
        }

        private static string ListVariationIds(WgoPart part)
        {
            var sb = new System.Text.StringBuilder();
            if (part.Variations != null)
            {
                foreach (WgoPartState v in part.Variations)
                {
                    if (v == null)
                    {
                        continue;
                    }
                    if (sb.Length > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append('\'').Append(v.variationId).Append('\'');
                }
            }
            return sb.Length == 0 ? "<none>" : sb.ToString();
        }

        private static WgoData CreateWgoData(ProjectionEntry entry, string sceneId)
        {
            bool isConveyor = false;
            try
            {
                if (GameBalance.Me?.buildableWgos != null &&
                    GameBalance.Me.buildableWgos.TryGetValue(entry.WgoId, out BuildingDef def))
                {
                    isConveyor = def != null && def.buildingMode == BuildingDef.BuildingMode.ConveyorPlace;
                }
            }
            catch (Exception e)
            {
                Log.Warn("Could not classify '" + entry.WgoId + "' as conveyor: " + e.Message);
            }

            WgoData data;
            if (isConveyor && GameBalance.Me?.conveyorWgosCache != null &&
                GameBalance.Me.conveyorWgosCache.TryGetValue(entry.WgoId, out WGODef conveyorDef))
            {
                data = new ConveyorWgoData(conveyorDef.conveyorType, entry.WgoId, entry.Position, sceneId);
            }
            else
            {
                if (isConveyor)
                {
                    Log.Warn("'" + entry.WgoId + "' is a ConveyorPlace blueprint but is missing " +
                             "from GameBalance.conveyorWgosCache - falling back to a plain WgoData.");
                }
                data = new WgoData(entry.WgoId, entry.Position, sceneId);
            }

            data.isTempObject = true;
            ApplyState(data, entry.VariationId, entry.RotationIndex);
            return data;
        }

        private bool TrySpawn(ProjectionEntry entry, Color tint, out ProjectionGhost ghost)
        {
            ghost = null;
            if (_container == null || _scene == null || string.IsNullOrEmpty(entry.WgoId))
            {
                return false;
            }
            try
            {
                WgoData data = CreateWgoData(entry, _scene.Id);

                Wgo wgo = Wgo.Spawn(
                    data,
                    _container.transform,
                    registerInChunkManagerIfStatic: true,
                    ignoreChunkRegistration: true,
                    applyDefaultWgoPartState: true);

                if (wgo == null)
                {
                    Log.Warn("Wgo.Spawn returned null for '" + entry.WgoId + "'");
                    return false;
                }

                // Pick the model BEFORE ghosting. For conveyor cells the tile was already
                // computed by ConveyorTiler and written onto the data; for everything else
                // VariantSelector decides which mesh variant to show.
                if (!ConveyorTiler.IsAutoTileable(entry.WgoId))
                {
                    VariantSelector.Apply(wgo, entry.WgoId, entry.VariationId, entry.RotationIndex, _settings);
                }
                ReassertState(wgo, entry.VariationId, entry.RotationIndex);

                string actualVariation = wgo.MainWgoPart?.WgoPartData?.variationId ?? "";
                int actualRotation = wgo.MainWgoPart?.WgoPartData?.rotationIndex ?? -1;

                wgo.UpdateChunkVisibility(isVisible: true);
                wgo.name = "drawing_" + entry.WgoId;

                int renderers = wgo.GetComponentsInChildren<Renderer>(true).Length;
                if (renderers == 0)
                {
                    // The object exists but has nothing to draw: from the player's point of
                    // view this is indistinguishable from "the projection did not place".
                    Log.Warn("Drawing '" + entry.WgoId + "' spawned with NO renderers " +
                             "- the blueprint id probably has no spawnable prefab of its own " +
                             "(it may only exist as a conveyor element or as a workbench " +
                             "extension). pos=" + entry.Position);
                }

                Ghostifier.Apply(wgo.gameObject, tint, _settings.Opacity, _settings.RemoveShadows);

                // The stored variation is what the *cursor* showed; remember what we actually
                // rendered so re-stamping keeps the same model.
                entry.VariationId = actualVariation;
                entry.RotationIndex = actualRotation;

                if (_settings.VerboseLogging || !_inspectedIds.Add(entry.WgoId))
                {
                    // First time we see this blueprint: dump its variations once so the log
                    // explains what the finished/cursor variants actually are.
                    Log.Info(VariantSelector.Describe(wgo, actualVariation));
                }
                if (_settings.VerboseLogging)
                {
                    Log.Info("drawing '" + entry.WgoId + "' ok: renderers=" + renderers +
                             " variation='" + actualVariation + "' rot=" + actualRotation +
                             " pos=" + entry.Position);
                }

                ghost = new ProjectionGhost
                {
                    Entry = entry,
                    Wgo = wgo,
                    Root = wgo.gameObject,
                    Tint = tint,
                    WorldBounds = ComputeBounds(wgo.gameObject)
                };
                _ghosts.Add(ghost);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Failed to spawn drawing for '" + entry.WgoId + "'", e);
                return false;
            }
        }

        private static Bounds ComputeBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one * 0.1f);
            }
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                b.Encapsulate(renderers[i].bounds);
            }
            return b;
        }
    }
}



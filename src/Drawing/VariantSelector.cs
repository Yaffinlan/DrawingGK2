using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Drawing
{
    /// <summary>
    /// Picks which model variation a projection should display.
    ///
    /// A WGO's mesh is a list of mutually exclusive <see cref="WgoPartState"/> variations
    /// (WgoPart.Variations). For the basement workbenches one of them is the build-site
    /// scaffold and another is the finished machine. The build cursor always shows the
    /// scaffold, so blindly copying its variationId gives a projection of something that does
    /// not exist yet.
    ///
    /// Modes:
    ///   cursor   - copy the variation the build cursor is showing (old behaviour)
    ///   finished - use the one variation that differs from the cursor's, i.e. the built model
    ///   default  - let the object resolve its own default state
    /// Per-blueprint overrides in the config win over all of that.
    /// </summary>
    internal static class VariantSelector
    {
        /// <summary>Human readable dump of every variation, for the F8 diagnostic key.</summary>
        public static string Describe(Wgo wgo, string pointerVariation)
        {
            var sb = new StringBuilder();
            if (wgo == null)
            {
                return "no wgo";
            }
            sb.Append("wgo '").Append(wgo.Data?.id).Append("' variations:");
            AppendPart(sb, wgo.MainWgoPart, pointerVariation, "main");
            if (wgo.AdditionalWgoParts != null)
            {
                for (int i = 0; i < wgo.AdditionalWgoParts.Count; i++)
                {
                    AppendPart(sb, wgo.AdditionalWgoParts[i], null, "add#" + i);
                }
            }
            return sb.ToString();
        }

        private static void AppendPart(StringBuilder sb, WgoPart part, string pointerVariation, string label)
        {
            if (part == null)
            {
                sb.Append(" [").Append(label).Append("]=<none>");
                return;
            }
            sb.Append(" [").Append(label).Append("] part='").Append(part.Id).Append("'");
            List<WgoPartState> variations = part.Variations;
            if (variations == null || variations.Count == 0)
            {
                sb.Append(" no-variations");
                return;
            }
            for (int i = 0; i < variations.Count; i++)
            {
                WgoPartState v = variations[i];
                if (v == null)
                {
                    continue;
                }
                sb.Append("\n      [").Append(i).Append("] id='").Append(v.variationId)
                  .Append("' rot=").Append(v.rotationIndex)
                  .Append(v.isDefault ? " DEFAULT" : "")
                  .Append(string.Equals(v.variationId, pointerVariation, StringComparison.Ordinal) ? " <-cursor" : "")
                  .Append(" active=").Append(v.gameObject != null && v.gameObject.activeSelf)
                  .Append(" meshes=").Append(CountRenderers(v.gameObject))
                  .Append(" size=").Append(SizeOf(v.gameObject));
            }
        }

        private static int CountRenderers(GameObject go)
        {
            if (go == null)
            {
                return 0;
            }
            return go.GetComponentsInChildren<Renderer>(true).Length;
        }

        private static string SizeOf(GameObject go)
        {
            if (go == null)
            {
                return "-";
            }
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0)
            {
                return "-";
            }
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
            {
                b.Encapsulate(rs[i].bounds);
            }
            return b.size.x.ToString("0.00") + "x" + b.size.y.ToString("0.00") + "x" + b.size.z.ToString("0.00");
        }

        /// <summary>
        /// Applies the chosen variation to the main part of a freshly spawned projection.
        /// Rotation is always taken from the cursor so the ghost lines up with the real build.
        /// </summary>
        public static void Apply(Wgo wgo, string blueprintId, string pointerVariation,
                                  int rotationIndex, Settings settings)
        {
            if (wgo == null)
            {
                return;
            }
            WgoPart part = wgo.MainWgoPart;
            if (part == null || part.Variations == null || part.Variations.Count == 0)
            {
                return;
            }

            // 1) explicit per-blueprint override
            string forced = settings.VariantOverrideFor(blueprintId);
            if (!string.IsNullOrEmpty(forced))
            {
                if (TryApply(part, forced, rotationIndex))
                {
                    return;
                }
                Log.Warn("Variant override '" + forced + "' for '" + blueprintId +
                         "' not found - available: " + ListIds(part));
            }

            // 2) configured behaviour
            switch (settings.VariantMode)
            {
                case VariantMode.Cursor:
                    return;

                case VariantMode.Default:
                {
                    part.ApplyDefaultWgoPartState();
                    // keep the orientation the player chose
                    if (rotationIndex != -1 && part.Variations != null)
                    {
                        foreach (WgoPartState v in part.Variations)
                        {
                            if (v != null && v.rotationIndex == rotationIndex &&
                                string.Equals(v.variationId, part.WgoPartData.variationId, StringComparison.Ordinal))
                            {
                                TryApply(part, v.variationId, v.rotationIndex);
                                break;
                            }
                        }
                    }
                    return;
                }

                case VariantMode.Finished:
                default:
                {
                    string chosen = PickFinished(part, pointerVariation);
                    if (!string.IsNullOrEmpty(chosen) && TryApply(part, chosen, rotationIndex))
                    {
                        return;
                    }
                    // Nothing conclusive: keep whatever the spawn produced.
                    return;
                }
            }
        }

        /// <summary>
        /// WgoPart.ApplyWgoPartState returns void, so existence has to be checked first.
        /// Rotation is part of the state key, hence the two-step lookup.
        /// </summary>
        private static bool TryApply(WgoPart part, string variationId, int rotationIndex)
        {
            if (part == null || string.IsNullOrEmpty(variationId))
            {
                return false;
            }
            WgoPartState exact = part.GetWgoPartState(variationId, rotationIndex);
            if (exact != null)
            {
                part.ApplyWgoPartState(exact.variationId, exact.rotationIndex);
                return true;
            }
            if (rotationIndex != -1)
            {
                // rotationIndex == -1 means "any" in GetWgoPartState, so ask again without it
                // and keep whatever rotation that variation was authored with.
                WgoPartState any = part.GetWgoPartState(variationId, -1);
                if (any != null)
                {
                    part.ApplyWgoPartState(any.variationId, any.rotationIndex);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// "Finished" == the single variation that is not the one the build cursor shows.
        /// If that is ambiguous (0 or >1 candidates) we do not guess.
        /// </summary>
        private static string PickFinished(WgoPart part, string pointerVariation)
        {
            List<WgoPartState> variations = part.Variations;
            WgoPartState cursor = null;
            var others = new List<WgoPartState>();

            foreach (WgoPartState v in variations)
            {
                if (v == null)
                {
                    continue;
                }
                bool isCursor = pointerVariation != null &&
                                string.Equals(v.variationId, pointerVariation, StringComparison.Ordinal) &&
                                (part.WgoPartData == null || v.rotationIndex == part.WgoPartData.rotationIndex);
                if (isCursor)
                {
                    cursor = v;
                }
                else
                {
                    others.Add(v);
                }
            }

            if (cursor == null)
            {
                // We do not know which one the cursor showed. If the object has exactly two
                // variations, the finished one is the one flagged isDefault=false is no help
                // either - so fall back to the one the cursor most likely showed: none.
                return null;
            }
            if (others.Count != 1)
            {
                return null; // ambiguous - do not guess
            }
            return others[0].variationId;
        }

        private static string ListIds(WgoPart part)
        {
            var sb = new StringBuilder();
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
                sb.Append('\'').Append(v.variationId).Append('\'').Append('/').Append(v.rotationIndex);
                if (v.isDefault)
                {
                    sb.Append("(default)");
                }
            }
            return sb.ToString();
        }
    }

    internal enum VariantMode
    {
        /// <summary>Mirror the build cursor's variation (scaffold for workbenches).</summary>
        Cursor,

        /// <summary>Use the object's own default state.</summary>
        Default,

        /// <summary>Use the single variation that differs from the cursor's - the built model.</summary>
        Finished,
    }
}

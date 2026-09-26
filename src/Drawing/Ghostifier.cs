using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drawing
{
    /// <summary>
    /// Turns a freshly spawned preview WGO into a weightless, see-through "blueprint ghost":
    /// no colliders, no navmesh carving, no shadows, and a flat unlit translucent material.
    ///
    /// Why a dedicated material instead of making the original one transparent: this build has
    /// no URP shaders compiled in (Shader.Find("Universal Render Pipeline/Unlit") is null), and
    /// each WGO uses its own hand-written shader. Poking _Mode/_Surface/_BaseColor into those
    /// produced solid black silhouettes. A known built-in transparent unlit shader with the
    /// object's own texture is deterministic, cheap (one material per texture, shared) and
    /// reads exactly like a blueprint.
    /// </summary>
    internal static class Ghostifier
    {
        private static readonly string[] ShaderPreference =
        {
            "Unlit/Transparent",      // confirmed present in this build
            "Sprites/Default",        // confirmed present
            "Unlit/Texture",
            "UI/Default",
        };

        // (texture, tint) -> ghost material, shared by every ghost that looks the same
        private static readonly Dictionary<string, Material> Shared = new Dictionary<string, Material>();
        private static readonly List<Material> All = new List<Material>();
        private static Shader _shader;
        private static bool _shaderResolved;

        /// <summary>Set when a source shader had to be swapped, so the log can explain itself.</summary>
        public static int SwappedMaterials { get; private set; }
        public static string ResolvedShaderName { get; private set; } = "<not resolved yet>";

        public static void Apply(GameObject root, Color tint, float opacity, bool disableShadows)
        {
            if (root == null)
            {
                return;
            }
            StripPhysics(root);
            StripTint(root);
            MakeTranslucent(root, tint, opacity, disableShadows);
        }

        /// <summary>
        /// Colliders are disabled in place (immediate, so the very next grid scan already
        /// ignores the ghost) instead of deactivating the GameObject - that would take the
        /// mesh with it whenever a renderer shares the collider's GameObject.
        /// </summary>
        private static void StripPhysics(GameObject root)
        {
            foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
            {
                try
                {
                    col.enabled = false;
                    col.isTrigger = true;
                    if (col.GetComponent<Renderer>() == null)
                    {
                        col.gameObject.SetActive(false);
                    }
                }
                catch (Exception e)
                {
                    Log.Warn("Collider strip failed: " + e.Message);
                }
            }

            foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                try
                {
                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                }
                catch { /* best effort */ }
            }

            foreach (NavMeshCutBoxCustom cut in root.GetComponentsInChildren<NavMeshCutBoxCustom>(true))
            {
                try
                {
                    cut.gameObject.SetActive(false);
                }
                catch { /* best effort */ }
            }
        }

        /// <summary>
        /// The game tints a preview through its own selection-tint shader parameter. Combined
        /// with our own material that produced muddy double-tinting, so it is cleared instead.
        /// </summary>
        private static void StripTint(GameObject root)
        {
            try
            {
                Wgo wgo = root.GetComponent<Wgo>();
                wgo?.SetSelectionTint(Color.white, 0f);
            }
            catch (Exception e)
            {
                Log.Warn("Could not clear selection tint: " + e.Message);
            }
        }

        private static void MakeTranslucent(GameObject root, Color tint, float opacity, bool disableShadows)
        {
            Color c = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(opacity));
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null)
                {
                    continue;
                }
                try
                {
                    Material src = r.sharedMaterial;
                    if (src != null)
                    {
                        Material ghost = GetGhostMaterial(src, c);
                        if (ghost != null)
                        {
                            // sharedMaterial: our own instance, the game's asset is untouched.
                            r.sharedMaterial = ghost;
                            SwappedMaterials++;
                        }
                    }
                    if (disableShadows)
                    {
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveShadows = false;
                        r.lightProbeUsage = LightProbeUsage.Off;
                        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    }
                }
                catch (Exception e)
                {
                    Log.Warn("Transparency failed on " + r.GetType().Name + ": " + e.Message);
                }
            }
        }

        private static Material GetGhostMaterial(Material src, Color tint)
        {
            if (!_shaderResolved)
            {
                _shaderResolved = true;
                foreach (string name in ShaderPreference)
                {
                    _shader = Shader.Find(name);
                    if (_shader != null)
                    {
                        ResolvedShaderName = name;
                        break;
                    }
                }
                if (_shader == null)
                {
                    // Last resort: reuse the object's own shader but force the transparent
                    // queue and blend state. Ugly, but visible.
                    ResolvedShaderName = "<none - falling back to source shader>";
                    Log.Warn("No known transparent shader found; falling back to source shaders.");
                }
                else
                {
                    Log.Info("Ghost shader: " + ResolvedShaderName);
                }
            }

            Texture tex = ExtractTexture(src);
            if (_shader == null)
            {
                return null;
            }
            Color c = tint;

            // One material per (texture, tint). Most WGOs of the same kind share a texture, so
            // a whole basement of projections costs a handful of materials, not hundreds.
            string key = (tex != null ? tex.GetInstanceID().ToString() : "notexture") +
                         "|" + c.r.ToString("F3") + "," + c.g.ToString("F3") + "," + c.b.ToString("F3");
            if (Shared.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            var m = new Material(_shader) { name = "proj_" + src.name };
            if (tex != null)
            {
                AssignTexture(m, tex);
            }
            m.color = c;
            if (m.HasProperty("_Color"))
            {
                m.SetColor("_Color", c);
            }
            if (m.HasProperty("_TintColor"))
            {
                m.SetColor("_TintColor", c);
            }
            if (m.HasProperty(Shader.PropertyToID("_SrcBlend")))
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            }
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = true;

            if (tex != null)
            {
                Shared[key] = m;
            }
            All.Add(m);
            return m;
        }

        private static readonly int P_MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int P_BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int P_Texture = Shader.PropertyToID("_Texture");

        private static Texture ExtractTexture(Material src)
        {
            if (src == null)
            {
                return null;
            }
            if (src.HasProperty(P_MainTex) && src.GetTexture(P_MainTex) != null)
            {
                return src.GetTexture(P_MainTex);
            }
            if (src.HasProperty(P_BaseMap) && src.GetTexture(P_BaseMap) != null)
            {
                return src.GetTexture(P_BaseMap);
            }
            if (src.HasProperty(P_Texture) && src.GetTexture(P_Texture) != null)
            {
                return src.GetTexture(P_Texture);
            }
            return src.mainTexture;
        }

        private static void AssignTexture(Material m, Texture tex)
        {
            if (m.HasProperty(P_MainTex))
            {
                m.SetTexture(P_MainTex, tex);
            }
            if (m.HasProperty(P_BaseMap))
            {
                m.SetTexture(P_BaseMap, tex);
            }
            if (m.HasProperty(P_Texture))
            {
                m.SetTexture(P_Texture, tex);
            }
            if (m.HasProperty("_BaseColor"))
            {
                m.SetColor("_BaseColor", Color.white);
            }
        }

        public static void ReleaseOwnedMaterials()
        {
            foreach (Material m in All)
            {
                if (m == null)
                {
                    continue;
                }
                try
                {
                    UnityEngine.Object.Destroy(m);
                }
                catch { /* best effort */ }
            }
            All.Clear();
            Shared.Clear();
            SwappedMaterials = 0;
        }
    }
}


// SMSModForge — Vanilla Level Extractor (Unity Editor script)
//
// HOW TO USE
//   1. Copy this file into your Starmaker Story Unity project under any
//      `Assets/Editor/` folder (Unity treats anything inside an `Editor`
//      folder as editor-only code).
//   2. Open the `CoreGameScene` scene — the one containing `5_Levels`.
//   3. From Unity's main menu pick:  Tools › SMSModForge › Extract Vanilla
//      Levels…   You'll be prompted for an output folder. The recommended
//      target is `<SMSModForge repo>/SMSModForge/Resources/VanillaLevelArt/`,
//      matching how VanillaBustArt already ships.
//   4. Re-build the editor so the PNGs + catalog land next to
//      SMSModForge.exe.
//
// WHAT GETS EXTRACTED
//   For every child of `5_Levels` (each is one vanilla "place"):
//     * <LevelGoName>/Base.PNG        ← the level GameObject's own sprite
//     * <LevelGoName>/Secondary.PNG   ← its secondary-sprite child, when the
//                                       level follows the usual two-sprite
//                                       layout (the distance/blur layer)
//     * <LevelGoName>/_extra/<path>.PNG ← every OTHER SpriteRenderer in the
//                                       level, so props/overlays are
//                                       available too and nothing is lost
//   …plus ONE `vanilla_levels.json` at the output root describing the full
//   GameObject hierarchy of every level: name, path, activeSelf, local
//   transform, and a per-node component summary (with sprite name, sorting
//   layer/order and bounds for SpriteRenderers).
//
//   The JSON is the important half: it's the baseline the editor needs to
//   (a) show a vanilla level's real hierarchy when authoring a vanilla
//   extension, and (b) tell an AUTHORED CHANGE apart from "this already
//   exists in the vanilla scene", so an extension only applies its delta
//   instead of rebuilding the whole tree.
//
// NOTES
//   * Pixel reads use the same RenderTexture blit + ReadPixels detour as
//     SMSModForgeArtExtractor, so non-readable / GPU-compressed textures
//     work without touching importer settings on the source assets.
//   * Sprites are cropped with `Sprite.textureRect` (correct whether or not
//     the sprite is atlas-packed); ReadPixels and textureRect share a
//     bottom-left origin, so no vertical flip is applied.
//   * INACTIVE levels and children are walked too — most vanilla levels sit
//     inactive in the scene until the player travels to them, so skipping
//     them would extract almost nothing.
//   * Re-running overwrites, so this doubles as the refresh path.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SMSModForge.EditorTools
{
    public static class SMSModForgeLevelExtractor
    {
        private const string MenuPath = "Tools/SMSModForge/Extract Vanilla Levels…";
        private const string LevelsRootName = "5_Levels";
        private const string CatalogFileName = "vanilla_levels.json";

        // ── Entry point ──────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var levelsRoot = GameObject.Find(LevelsRootName) ?? FindInactiveByName(LevelsRootName);
            if (levelsRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "SMSModForge — Extract Vanilla Levels",
                    "Could not find a GameObject named '" + LevelsRootName +
                    "' in the currently open scene. Open the game's CoreGameScene " +
                    "before running this command.",
                    "OK");
                return;
            }

            string defaultRoot = Path.GetDirectoryName(Application.dataPath) ?? "";
            string outDir = EditorUtility.SaveFolderPanel(
                "Choose output folder (recommended: SMSModForge/Resources/VanillaLevelArt)",
                defaultRoot, "VanillaLevelArt");
            if (string.IsNullOrEmpty(outDir)) return;

            int levels = 0, sprites = 0, nodes = 0;
            var json = new StringBuilder();
            json.Append("{\n  \"generator\": \"SMSModForgeLevelExtractor\",\n");
            json.Append("  \"unityVersion\": ").Append(Quote(Application.unityVersion)).Append(",\n");
            json.Append("  \"levels\": [\n");

            var levelTransforms = new List<Transform>();
            foreach (Transform level in levelsRoot.transform) levelTransforms.Add(level);

            for (int i = 0; i < levelTransforms.Count; i++)
            {
                var level = levelTransforms[i];
                EditorUtility.DisplayProgressBar("SMSModForge — Extract Vanilla Levels",
                    level.name, (float)i / Mathf.Max(1, levelTransforms.Count));

                string levelDir = Path.Combine(outDir, Sanitize(level.name));
                Directory.CreateDirectory(levelDir);

                sprites += ExtractLevelArt(level, levelDir);

                json.Append("    {\n");
                json.Append("      \"goName\": ").Append(Quote(level.name)).Append(",\n");
                json.Append("      \"siblingIndex\": ").Append(level.GetSiblingIndex()).Append(",\n");
                json.Append("      \"activeSelf\": ").Append(level.gameObject.activeSelf ? "true" : "false").Append(",\n");
                json.Append("      \"hierarchy\":\n");
                int written = 0;
                AppendNode(json, level, "", 8, ref written);
                nodes += written;
                json.Append("\n    }");
                if (i < levelTransforms.Count - 1) json.Append(',');
                json.Append('\n');
                levels++;
            }

            json.Append("  ]\n}\n");
            EditorUtility.ClearProgressBar();

            string catalogPath = Path.Combine(outDir, CatalogFileName);
            File.WriteAllText(catalogPath, json.ToString(), new UTF8Encoding(false));

            EditorUtility.DisplayDialog(
                "SMSModForge — Extract Vanilla Levels",
                "Extracted " + levels + " level(s), " + sprites + " sprite(s) and " +
                nodes + " GameObject node(s) to:\n\n" + outDir +
                "\n\nCopy this folder into <SMSModForge repo>/SMSModForge/Resources/" +
                "VanillaLevelArt/ and rebuild the editor.",
                "OK");
            Debug.Log("[SMSModForge] Extracted " + levels + " level(s), " + sprites +
                      " sprite(s), " + nodes + " node(s) to " + outDir);
        }

        // ── Art ──────────────────────────────────────────────────────────

        /// <summary>
        /// Write the level's own sprite as Base.PNG, its secondary-sprite child
        /// (the usual two-sprite level layout) as Secondary.PNG, and every other
        /// SpriteRenderer under `_extra/` keyed by its hierarchy path.
        /// </summary>
        private static int ExtractLevelArt(Transform level, string levelDir)
        {
            int written = 0;

            var ownSr = level.GetComponent<SpriteRenderer>();
            if (ownSr != null && ownSr.sprite != null)
                if (WriteSprite(ownSr.sprite, Path.Combine(levelDir, "Base.PNG"))) written++;

            // Vanilla levels follow "main sprite + secondary sprite child";
            // child(1) is the secondary when it carries its own renderer.
            Transform secondary = level.childCount > 1 ? level.GetChild(1) : null;
            var secondarySr = secondary != null ? secondary.GetComponent<SpriteRenderer>() : null;
            if (secondarySr != null && secondarySr.sprite != null)
                if (WriteSprite(secondarySr.sprite, Path.Combine(levelDir, "Secondary.PNG"))) written++;

            foreach (var sr in level.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null) continue;
                if (sr == ownSr || sr == secondarySr) continue;
                string rel = PathFrom(level, sr.transform);
                string dest = Path.Combine(Path.Combine(levelDir, "_extra"), Sanitize(rel) + ".PNG");
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                if (WriteSprite(sr.sprite, dest)) written++;
            }
            return written;
        }

        /// <summary>Crop a sprite out of its (possibly atlas-packed, possibly
        /// non-readable) texture and write it as a PNG.</summary>
        private static bool WriteSprite(Sprite sprite, string destPath)
        {
            var src = sprite.texture;
            if (src == null) return false;

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;

                var r = sprite.textureRect;
                int x = Mathf.RoundToInt(r.x), y = Mathf.RoundToInt(r.y);
                int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
                if (w <= 0 || h <= 0) return false;

                readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(x, y, w, h), 0, 0);
                readable.Apply();

                File.WriteAllBytes(destPath, readable.EncodeToPNG());
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SMSModForge] Sprite '" + sprite.name + "' failed: " + ex.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        // ── Hierarchy ────────────────────────────────────────────────────

        /// <summary>
        /// Emit one JSON object per GameObject, recursively. Everything the
        /// editor needs to mirror the vanilla structure and to diff an authored
        /// change against it: identity, active state, local TRS, component list,
        /// and renderer details where present.
        /// </summary>
        private static void AppendNode(StringBuilder sb, Transform t, string parentPath,
                                       int indent, ref int count)
        {
            count++;
            string pad = new string(' ', indent);
            string path = string.IsNullOrEmpty(parentPath) ? t.name : parentPath + "/" + t.name;

            sb.Append(pad).Append("{\n");
            sb.Append(pad).Append("  \"name\": ").Append(Quote(t.name)).Append(",\n");
            sb.Append(pad).Append("  \"path\": ").Append(Quote(path)).Append(",\n");
            sb.Append(pad).Append("  \"activeSelf\": ").Append(t.gameObject.activeSelf ? "true" : "false").Append(",\n");
            sb.Append(pad).Append("  \"localPosition\": ").Append(V3(t.localPosition)).Append(",\n");
            sb.Append(pad).Append("  \"localEulerAngles\": ").Append(V3(t.localEulerAngles)).Append(",\n");
            sb.Append(pad).Append("  \"localScale\": ").Append(V3(t.localScale)).Append(",\n");

            // Component type names, kept as a flat array so anything reading the
            // older catalogs still works.
            sb.Append(pad).Append("  \"components\": [");
            var comps = t.GetComponents<Component>();
            bool firstComp = true;
            foreach (var c in comps)
            {
                if (c == null) continue;                  // missing script
                if (c is Transform) continue;             // implied
                if (!firstComp) sb.Append(", ");
                sb.Append(Quote(c.GetType().Name));
                firstComp = false;
            }
            sb.Append("],\n");

            // ...and the same components with their serialized values. A type
            // name alone says a level HAS a ParallaxMouseEffect; it doesn't say
            // what that effect is set to, which is what reproducing the level
            // actually needs. Read through SerializedObject rather than
            // hand-coding each type: there are ~50 across the vanilla levels,
            // and this way a component nobody anticipated still comes through.
            sb.Append(pad).Append("  \"componentValues\": [");
            bool firstCv = true;
            foreach (var c in comps)
            {
                if (c == null || c is Transform) continue;
                if (!firstCv) sb.Append(", ");
                sb.Append("{ \"type\": ").Append(Quote(c.GetType().Name));
                sb.Append(", \"params\": ");
                AppendSerializedValues(sb, c);
                sb.Append(" }");
                firstCv = false;
            }
            sb.Append("],\n");

            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sb.Append(pad).Append("  \"spriteRenderer\": { ");
                sb.Append("\"sprite\": ").Append(Quote(sr.sprite != null ? sr.sprite.name : ""));
                sb.Append(", \"sortingLayer\": ").Append(Quote(sr.sortingLayerName));
                sb.Append(", \"sortingOrder\": ").Append(sr.sortingOrder.ToString(CultureInfo.InvariantCulture));
                sb.Append(", \"color\": ").Append(Quote(ColorUtility.ToHtmlStringRGBA(sr.color)));
                sb.Append(", \"enabled\": ").Append(sr.enabled ? "true" : "false");
                // The material's own alpha, where it has one. The renderer's
                // colour is only half of how solid an object looks: the game's
                // NPCCoreReflectionMat fades to 0.58 through the MATERIAL while
                // leaving the renderer opaque white, so reading the colour alone
                // shows a reflection at full strength.
                sb.Append(", \"materialAlpha\": ").Append(MaterialAlpha(sr).ToString("0.###", CultureInfo.InvariantCulture));
                // Read straight off the renderer rather than left to the
                // serialized walk: m_Materials is an array whose element sits
                // past that walk's depth cap, so the name never came through —
                // and the name is how a reflection is identifiable at all.
                sb.Append(", \"material\": ")
                  .Append(Quote(sr.sharedMaterial != null ? sr.sharedMaterial.name : ""));
                if (sr.sprite != null)
                {
                    sb.Append(", \"pixelsPerUnit\": ")
                      .Append(sr.sprite.pixelsPerUnit.ToString("0.####", CultureInfo.InvariantCulture));
                    sb.Append(", \"pixelSize\": [")
                      .Append(Mathf.RoundToInt(sr.sprite.rect.width)).Append(", ")
                      .Append(Mathf.RoundToInt(sr.sprite.rect.height)).Append(']');
                }
                sb.Append(" },\n");
            }

            sb.Append(pad).Append("  \"children\":");
            if (t.childCount == 0)
            {
                sb.Append(" []\n");
            }
            else
            {
                sb.Append("\n").Append(pad).Append("  [\n");
                for (int i = 0; i < t.childCount; i++)
                {
                    AppendNode(sb, t.GetChild(i), path, indent + 4, ref count);
                    if (i < t.childCount - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(pad).Append("  ]\n");
            }
            sb.Append(pad).Append("}");
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static string PathFrom(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        /// <summary>
        /// Serialize one component's inspector-visible values as a flat JSON
        /// object.
        /// <para/>
        /// Flat on purpose: keys are the serialized property paths Unity itself
        /// uses ("m_Size.x", "wordColors.Array.size"), so a value can be found
        /// and set again by name without modelling every component's shape.
        /// Only leaf primitives are emitted — object references become the
        /// referenced asset's NAME, since a scene instance id means nothing
        /// outside the project. Arrays are capped, and the whole walk is
        /// depth-limited, so a ParticleSystem can't produce a megabyte of JSON.
        /// </summary>
        private static void AppendSerializedValues(StringBuilder sb, Component c)
        {
            sb.Append('{');
            bool first = true;
            try
            {
                var so = new SerializedObject(c);
                var p = so.GetIterator();
                int emitted = 0;
                // enterChildren: true on the first step to descend into the
                // object; NextVisible walks the rest in declaration order.
                bool enter = true;
                while (p.NextVisible(enter) && emitted < MaxComponentValues)
                {
                    enter = false;
                    if (p.depth > MaxComponentDepth) continue;
                    if (p.propertyPath == "m_Script") continue;   // the asset ref, not a setting

                    string value = SerializedLeaf(p);
                    if (value == null) continue;                  // container or unsupported

                    if (!first) sb.Append(", ");
                    sb.Append(Quote(p.propertyPath)).Append(": ").Append(value);
                    first = false;
                    emitted++;
                }
            }
            catch
            {
                // A component whose SerializedObject can't be built (a broken
                // script reference) contributes nothing rather than aborting
                // the whole extraction.
            }
            sb.Append('}');
        }

        private const int MaxComponentValues = 96;
        private const int MaxComponentDepth = 3;

        /// <summary>JSON for a leaf property, or null when it isn't one.</summary>
        private static string SerializedLeaf(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:   return p.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:   return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:     return p.floatValue.ToString("0.#####", CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:    return Quote(p.stringValue);
                case SerializedPropertyType.Enum:
                    return Quote(p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                                 ? p.enumDisplayNames[p.enumValueIndex] : p.enumValueIndex.ToString());
                case SerializedPropertyType.Color:     return Quote(ColorUtility.ToHtmlStringRGBA(p.colorValue));
                case SerializedPropertyType.Vector2:   return "[" + F(p.vector2Value.x) + ", " + F(p.vector2Value.y) + "]";
                case SerializedPropertyType.Vector3:   return "[" + F(p.vector3Value.x) + ", " + F(p.vector3Value.y) + ", " + F(p.vector3Value.z) + "]";
                case SerializedPropertyType.ObjectReference:
                    // The asset's name is the only part that survives leaving
                    // the project — an instance id would be noise.
                    return Quote(p.objectReferenceValue != null ? p.objectReferenceValue.name : "");
                default:
                    return null;
            }
        }

        /// <summary>
        /// A renderer's material alpha, or 1 when it doesn't express one.
        /// Checks the properties the project's sprite shaders actually use, in
        /// the order they'd win: an explicit _Alpha, then a tint colour's alpha.
        /// Reads sharedMaterial so nothing is instantiated by the extraction.
        /// </summary>
        private static float MaterialAlpha(SpriteRenderer sr)
        {
            var m = sr != null ? sr.sharedMaterial : null;
            if (m == null) return 1f;
            try
            {
                if (m.HasProperty("_Alpha")) return Mathf.Clamp01(m.GetFloat("_Alpha"));
                if (m.HasProperty("_Color")) return Mathf.Clamp01(m.GetColor("_Color").a);
            }
            catch { /* a shader without those properties — treat as opaque */ }
            return 1f;
        }

        private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        private static string V3(Vector3 v)
            => "[" + v.x.ToString("0.#####", CultureInfo.InvariantCulture) + ", "
                   + v.y.ToString("0.#####", CultureInfo.InvariantCulture) + ", "
                   + v.z.ToString("0.#####", CultureInfo.InvariantCulture) + "]";

        private static string Quote(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>File-system-safe form of a GameObject name / path. Vanilla
        /// level names contain spaces and a few contain punctuation.</summary>
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
                sb.Append(ch == '/' ? Path.DirectorySeparatorChar
                        : Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
            return sb.ToString();
        }

        /// <summary>Most vanilla levels sit inactive until travelled to, so the
        /// usual GameObject.Find (active-only) isn't enough.</summary>
        private static GameObject FindInactiveByName(string name)
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != name) continue;
                if (go.hideFlags != HideFlags.None) continue;
                if (string.IsNullOrEmpty(go.scene.name)) continue;   // skip prefab assets
                return go;
            }
            return null;
        }
    }
}

// SMSModForge — Level NPC Extractor (Unity Editor script)
//
// HOW TO USE
//   1. Copy this file into your NPC Unity project under any `Assets/Editor/`
//      folder (create one if it doesn't exist — Unity treats anything inside
//      an `Editor` folder as editor-only code).
//   2. Open the scene that contains your levels.
//   3. From Unity's main menu pick:  Tools › SMSModForge › Extract Level NPCs…
//      You'll be prompted for an output `.json` file location.
//   4. Send the resulting JSON back — it's what the NPC pack format gets
//      designed against.
//
// WHAT GETS EXTRACTED
//   Every GameObject beneath an `NPCs` object, anywhere under the levels root
//   (`5_Levels`, `5_Level`, or whatever you point it at). The whole subtree is
//   dumped verbatim, because the shape between `NPCs` and an actual NPC is not
//   assumed to follow any pattern:
//
//     levels[]                     — one per level that owns an NPCs object
//       npcsPath                   — hierarchy path of the NPCs object itself
//       tree                       — the full subtree, recursively:
//         name / path / depth / activeSelf
//         transform                — localPosition, localEulerAngles,
//                                    localRotation (quaternion, so a 3-axis
//                                    shadow rotation round-trips exactly),
//                                    localScale, lossyScale
//         components[]             — EVERY component, by type name
//         spriteRenderer           — sprite + texture (name, size, pivot,
//                                    pixelsPerUnit, rect), sorting layer +
//                                    order, colour, flip, drawMode, material
//                                    name, SHADER NAME and every shader
//                                    property with its current value
//         particleSystem           — the modules that matter for reproducing
//                                    a "Wet" style preset
//         scripts[]                — any MonoBehaviour's serialized fields,
//                                    read generically via SerializedObject, so
//                                    physics/jiggle components come through
//                                    without this script knowing their types
//         guess                    — "npc" / "container", a HINT ONLY (see below)
//
//   textures[] and materials[] are also emitted as flat de-duplicated tables
//   with asset paths, so the art can be located and exported.
//
// NOTES
//   * `guess` is a heuristic (a renderer whose material exposes jiggle-ish
//     properties, or that has Blink/Wet/shadow-looking children). It is a
//     convenience for reading the dump, NOT a classification to build on —
//     the full tree is always present, so nothing is lost if the guess is
//     wrong.
//   * Pure reflection / SerializedObject: no compile-time dependency on any
//     game or Game Creator assembly, so it drops into any project.
//   * Inactive objects are included; that matters because NPC variants are
//     usually parked disabled.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SMSModForgeNpcExtractor
{
    // Roots tried in order when the user doesn't have one selected.
    private static readonly string[] CandidateRoots = { "5_Levels", "5_Level", "Levels" };
    private const string NpcContainerName = "NPCs";

    [MenuItem("Tools/SMSModForge/Extract Level NPCs…")]
    private static void Extract()
    {
        Transform root = FindRoot();
        if (root == null)
        {
            EditorUtility.DisplayDialog("SMSModForge",
                "Couldn't find a levels root (tried: " + string.Join(", ", CandidateRoots) + ").\n\n" +
                "Select the levels root GameObject in the Hierarchy and run this again.",
                "OK");
            return;
        }

        string path = EditorUtility.SaveFilePanel("Save NPC dump", "", "npc_dump.json", "json");
        if (string.IsNullOrEmpty(path)) return;

        _textures.Clear();
        _materials.Clear();

        var sb = new StringBuilder();
        sb.Append("{\n");
        Field(sb, 1, "generatedUtc", DateTime.UtcNow.ToString("o")); sb.Append(",\n");
        Field(sb, 1, "levelsRoot", Path(root)); sb.Append(",\n");
        Field(sb, 1, "scene", root.gameObject.scene.name); sb.Append(",\n");

        // Any NPCs container anywhere under the root; levels don't have to be
        // direct children and the container doesn't have to be at a fixed depth.
        var containers = new List<Transform>();
        CollectContainers(root, containers);

        Indent(sb, 1); sb.Append("\"levels\": [\n");
        for (int i = 0; i < containers.Count; i++)
        {
            Transform npcs = containers[i];
            Transform level = npcs.parent != null ? npcs.parent : npcs;
            Indent(sb, 2); sb.Append("{\n");
            Field(sb, 3, "level", level.name); sb.Append(",\n");
            Field(sb, 3, "levelPath", Path(level)); sb.Append(",\n");
            Field(sb, 3, "npcsPath", Path(npcs)); sb.Append(",\n");
            Indent(sb, 3); sb.Append("\"tree\": ");
            WriteNode(sb, npcs, 3, 0);
            sb.Append("\n");
            Indent(sb, 2); sb.Append(i < containers.Count - 1 ? "},\n" : "}\n");
        }
        Indent(sb, 1); sb.Append("],\n");

        WriteTable(sb, "textures", _textures);
        sb.Append(",\n");
        WriteTable(sb, "materials", _materials);
        sb.Append("\n}\n");

        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[SMSModForge] NPC dump written: {path}  ({containers.Count} level(s), " +
                  $"{_textures.Count} texture(s), {_materials.Count} material(s))");
        EditorUtility.RevealInFinder(path);
    }

    private static Transform FindRoot()
    {
        if (Selection.activeTransform != null) return Selection.activeTransform;
        foreach (string name in CandidateRoots)
        {
            var go = GameObject.Find(name);
            if (go != null) return go.transform;
        }
        return null;
    }

    private static void CollectContainers(Transform t, List<Transform> into)
    {
        if (string.Equals(t.name, NpcContainerName, StringComparison.OrdinalIgnoreCase))
        {
            into.Add(t);
            return;   // don't recurse past a container; its whole subtree is dumped
        }
        for (int i = 0; i < t.childCount; i++) CollectContainers(t.GetChild(i), into);
    }

    // ── node ────────────────────────────────────────────────────────────

    private static void WriteNode(StringBuilder sb, Transform t, int ind, int depth)
    {
        sb.Append("{\n");
        Field(sb, ind + 1, "name", t.name); sb.Append(",\n");
        Field(sb, ind + 1, "path", Path(t)); sb.Append(",\n");
        Field(sb, ind + 1, "depth", depth); sb.Append(",\n");
        Field(sb, ind + 1, "activeSelf", t.gameObject.activeSelf); sb.Append(",\n");
        Field(sb, ind + 1, "guess", Guess(t)); sb.Append(",\n");

        // Transform: euler for readability, quaternion for exactness. A shadow
        // rotated on all three axes must round-trip without gimbal ambiguity.
        Indent(sb, ind + 1); sb.Append("\"transform\": {\n");
        Field(sb, ind + 2, "localPosition", V3(t.localPosition)); sb.Append(",\n");
        Field(sb, ind + 2, "localEulerAngles", V3(t.localEulerAngles)); sb.Append(",\n");
        Field(sb, ind + 2, "localRotation", V4(t.localRotation)); sb.Append(",\n");
        Field(sb, ind + 2, "localScale", V3(t.localScale)); sb.Append(",\n");
        Field(sb, ind + 2, "lossyScale", V3(t.lossyScale)); sb.Append("\n");
        Indent(sb, ind + 1); sb.Append("},\n");

        var comps = t.GetComponents<Component>();
        var names = new List<string>();
        foreach (var c in comps) names.Add(c == null ? "<missing script>" : c.GetType().Name);
        Indent(sb, ind + 1); sb.Append("\"components\": [");
        for (int i = 0; i < names.Count; i++)
        {
            sb.Append(Str(names[i]));
            if (i < names.Count - 1) sb.Append(", ");
        }
        sb.Append("],\n");

        var sr = t.GetComponent<SpriteRenderer>();
        if (sr != null) { WriteSpriteRenderer(sb, sr, ind + 1); sb.Append(",\n"); }

        var ps = t.GetComponent<ParticleSystem>();
        if (ps != null) { WriteParticleSystem(sb, ps, ind + 1); sb.Append(",\n"); }

        WriteScripts(sb, comps, ind + 1); sb.Append(",\n");

        Indent(sb, ind + 1); sb.Append("\"children\": [");
        if (t.childCount == 0) sb.Append("]\n");
        else
        {
            sb.Append("\n");
            for (int i = 0; i < t.childCount; i++)
            {
                Indent(sb, ind + 2);
                WriteNode(sb, t.GetChild(i), ind + 2, depth + 1);
                sb.Append(i < t.childCount - 1 ? ",\n" : "\n");
            }
            Indent(sb, ind + 1); sb.Append("]\n");
        }
        Indent(sb, ind); sb.Append("}");
    }

    /// <summary>Reading aid only — the full tree is dumped regardless.</summary>
    private static string Guess(Transform t)
    {
        var sr = t.GetComponent<SpriteRenderer>();
        if (sr == null) return "container";
        if (sr.sharedMaterial != null && sr.sharedMaterial.shader != null)
        {
            string sh = sr.sharedMaterial.shader.name.ToLowerInvariant();
            if (sh.Contains("jiggle") || sh.Contains("wobble")) return "npc";
        }
        for (int i = 0; i < t.childCount; i++)
        {
            string n = t.GetChild(i).name.ToLowerInvariant();
            if (n.Contains("blink") || n.Contains("shadow") || n.Contains("wet")) return "npc";
        }
        return "sprite";
    }

    // ── sprite renderer + shader ────────────────────────────────────────

    private static void WriteSpriteRenderer(StringBuilder sb, SpriteRenderer sr, int ind)
    {
        Indent(sb, ind); sb.Append("\"spriteRenderer\": {\n");
        Field(sb, ind + 1, "enabled", sr.enabled); sb.Append(",\n");
        Field(sb, ind + 1, "sortingLayerName", sr.sortingLayerName); sb.Append(",\n");
        Field(sb, ind + 1, "sortingOrder", sr.sortingOrder); sb.Append(",\n");
        Field(sb, ind + 1, "color", Hex(sr.color)); sb.Append(",\n");
        Field(sb, ind + 1, "flipX", sr.flipX); sb.Append(",\n");
        Field(sb, ind + 1, "flipY", sr.flipY); sb.Append(",\n");
        Field(sb, ind + 1, "drawMode", sr.drawMode.ToString()); sb.Append(",\n");
        Field(sb, ind + 1, "maskInteraction", sr.maskInteraction.ToString()); sb.Append(",\n");
        if (sr.drawMode != SpriteDrawMode.Simple) { Field(sb, ind + 1, "size", V2(sr.size)); sb.Append(",\n"); }

        if (sr.sprite != null)
        {
            var sp = sr.sprite;
            Indent(sb, ind + 1); sb.Append("\"sprite\": {\n");
            Field(sb, ind + 2, "name", sp.name); sb.Append(",\n");
            Field(sb, ind + 2, "assetPath", AssetDatabase.GetAssetPath(sp)); sb.Append(",\n");
            Field(sb, ind + 2, "rect", $"{sp.rect.x},{sp.rect.y},{sp.rect.width},{sp.rect.height}"); sb.Append(",\n");
            Field(sb, ind + 2, "pixelsPerUnit", sp.pixelsPerUnit); sb.Append(",\n");
            // Normalised pivot — the anchor a "fixed resolution but scalable"
            // sprite grows around.
            Field(sb, ind + 2, "pivotNormalized",
                  V2(new Vector2(sp.pivot.x / Mathf.Max(1f, sp.rect.width),
                                 sp.pivot.y / Mathf.Max(1f, sp.rect.height)))); sb.Append(",\n");
            Field(sb, ind + 2, "texture", Texture(sp.texture)); sb.Append("\n");
            Indent(sb, ind + 1); sb.Append("},\n");
        }
        else { Indent(sb, ind + 1); sb.Append("\"sprite\": null,\n"); }

        Field(sb, ind + 1, "material", Material(sr.sharedMaterial)); sb.Append("\n");
        Indent(sb, ind); sb.Append("}");
    }

    // ── particles ───────────────────────────────────────────────────────

    private static void WriteParticleSystem(StringBuilder sb, ParticleSystem ps, int ind)
    {
        var main = ps.main;
        var em = ps.emission;
        var shape = ps.shape;
        Indent(sb, ind); sb.Append("\"particleSystem\": {\n");
        Field(sb, ind + 1, "duration", main.duration); sb.Append(",\n");
        Field(sb, ind + 1, "loop", main.loop); sb.Append(",\n");
        Field(sb, ind + 1, "startLifetime", main.startLifetime.constant); sb.Append(",\n");
        Field(sb, ind + 1, "startSpeed", main.startSpeed.constant); sb.Append(",\n");
        Field(sb, ind + 1, "startSize", main.startSize.constant); sb.Append(",\n");
        Field(sb, ind + 1, "startColor", Hex(main.startColor.color)); sb.Append(",\n");
        Field(sb, ind + 1, "gravityModifier", main.gravityModifier.constant); sb.Append(",\n");
        Field(sb, ind + 1, "maxParticles", main.maxParticles); sb.Append(",\n");
        Field(sb, ind + 1, "simulationSpace", main.simulationSpace.ToString()); sb.Append(",\n");
        Field(sb, ind + 1, "playOnAwake", main.playOnAwake); sb.Append(",\n");
        Field(sb, ind + 1, "emissionEnabled", em.enabled); sb.Append(",\n");
        Field(sb, ind + 1, "emissionRateOverTime", em.rateOverTime.constant); sb.Append(",\n");
        Field(sb, ind + 1, "shapeEnabled", shape.enabled); sb.Append(",\n");
        Field(sb, ind + 1, "shapeType", shape.shapeType.ToString()); sb.Append(",\n");
        Field(sb, ind + 1, "shapeScale", V3(shape.scale)); sb.Append(",\n");
        var psr = ps.GetComponent<ParticleSystemRenderer>();
        Field(sb, ind + 1, "rendererMaterial", psr != null ? Material(psr.sharedMaterial) : "null"); sb.Append("\n");
        Indent(sb, ind); sb.Append("}");
    }

    // ── arbitrary MonoBehaviours (jiggle/physics components etc.) ───────

    private static void WriteScripts(StringBuilder sb, Component[] comps, int ind)
    {
        Indent(sb, ind); sb.Append("\"scripts\": [");
        var behaviours = new List<MonoBehaviour>();
        foreach (var c in comps) if (c is MonoBehaviour mb) behaviours.Add(mb);
        if (behaviours.Count == 0) { sb.Append("]"); return; }

        sb.Append("\n");
        for (int i = 0; i < behaviours.Count; i++)
        {
            var mb = behaviours[i];
            Indent(sb, ind + 1); sb.Append("{\n");
            Field(sb, ind + 2, "type", mb.GetType().FullName); sb.Append(",\n");
            Field(sb, ind + 2, "enabled", mb.enabled); sb.Append(",\n");
            Indent(sb, ind + 2); sb.Append("\"fields\": {\n");
            // SerializedObject reaches every serialized field without this
            // script knowing the component's type.
            var so = new SerializedObject(mb);
            var it = so.GetIterator();
            var lines = new List<string>();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;
                lines.Add(Indented(ind + 3) + Str(it.propertyPath) + ": " + PropValue(it));
            }
            for (int k = 0; k < lines.Count; k++)
                sb.Append(lines[k]).Append(k < lines.Count - 1 ? ",\n" : "\n");
            Indent(sb, ind + 2); sb.Append("}\n");
            Indent(sb, ind + 1); sb.Append(i < behaviours.Count - 1 ? "},\n" : "}\n");
        }
        Indent(sb, ind); sb.Append("]");
    }

    private static string PropValue(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer:   return p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean:   return p.boolValue ? "true" : "false";
            case SerializedPropertyType.Float:     return Num(p.floatValue);
            case SerializedPropertyType.String:    return Str(p.stringValue);
            case SerializedPropertyType.Color:     return Str(Hex(p.colorValue));
            case SerializedPropertyType.Vector2:   return Str(V2(p.vector2Value));
            case SerializedPropertyType.Vector3:   return Str(V3(p.vector3Value));
            case SerializedPropertyType.Vector4:   return Str(V4(p.vector4Value));
            case SerializedPropertyType.Enum:      return Str(p.enumNames != null && p.enumValueIndex >= 0 &&
                                                              p.enumValueIndex < p.enumNames.Length
                                                              ? p.enumNames[p.enumValueIndex]
                                                              : p.enumValueIndex.ToString());
            case SerializedPropertyType.ObjectReference:
                return Str(p.objectReferenceValue == null ? "" :
                           p.objectReferenceValue.name + " (" + p.objectReferenceValue.GetType().Name + ")");
            case SerializedPropertyType.AnimationCurve:
                return Str("<AnimationCurve keys=" + (p.animationCurveValue != null
                           ? p.animationCurveValue.length : 0) + ">");
            default:
                return Str("<" + p.propertyType + ">");
        }
    }

    // ── de-duplicated asset tables ──────────────────────────────────────

    private static readonly Dictionary<string, string> _textures = new Dictionary<string, string>();
    private static readonly Dictionary<string, string> _materials = new Dictionary<string, string>();

    private static string Texture(Texture2D tex)
    {
        if (tex == null) return "";
        if (!_textures.ContainsKey(tex.name))
        {
            var sb = new StringBuilder();
            sb.Append("{ ");
            sb.Append("\"assetPath\": ").Append(Str(AssetDatabase.GetAssetPath(tex))).Append(", ");
            sb.Append("\"width\": ").Append(tex.width).Append(", ");
            sb.Append("\"height\": ").Append(tex.height).Append(", ");
            sb.Append("\"filterMode\": ").Append(Str(tex.filterMode.ToString())).Append(", ");
            sb.Append("\"format\": ").Append(Str(tex.format.ToString()));
            sb.Append(" }");
            _textures[tex.name] = sb.ToString();
        }
        return tex.name;
    }

    /// <summary>Records the material and EVERY shader property with its current
    /// value — that's what makes the jiggle setup reproducible.</summary>
    private static string Material(Material mat)
    {
        if (mat == null) return "";
        string key = mat.name;
        if (!_materials.ContainsKey(key))
        {
            var sb = new StringBuilder();
            sb.Append("{ ");
            sb.Append("\"assetPath\": ").Append(Str(AssetDatabase.GetAssetPath(mat))).Append(", ");
            sb.Append("\"shader\": ").Append(Str(mat.shader != null ? mat.shader.name : "")).Append(", ");
            sb.Append("\"renderQueue\": ").Append(mat.renderQueue).Append(", ");
            sb.Append("\"properties\": { ");
            if (mat.shader != null)
            {
                int n = ShaderUtil.GetPropertyCount(mat.shader);
                var parts = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    string pn = ShaderUtil.GetPropertyName(mat.shader, i);
                    switch (ShaderUtil.GetPropertyType(mat.shader, i))
                    {
                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:
                            parts.Add(Str(pn) + ": " + Num(mat.GetFloat(pn))); break;
                        case ShaderUtil.ShaderPropertyType.Color:
                            parts.Add(Str(pn) + ": " + Str(Hex(mat.GetColor(pn)))); break;
                        case ShaderUtil.ShaderPropertyType.Vector:
                            parts.Add(Str(pn) + ": " + Str(V4(mat.GetVector(pn)))); break;
                        case ShaderUtil.ShaderPropertyType.TexEnv:
                            var t = mat.GetTexture(pn);
                            parts.Add(Str(pn) + ": " + Str(t != null ? t.name : "")); break;
                    }
                }
                sb.Append(string.Join(", ", parts.ToArray()));
            }
            sb.Append(" } }");
            _materials[key] = sb.ToString();
        }
        return key;
    }

    private static void WriteTable(StringBuilder sb, string name, Dictionary<string, string> table)
    {
        Indent(sb, 1); sb.Append(Str(name)).Append(": {");
        if (table.Count == 0) { sb.Append("}"); return; }
        sb.Append("\n");
        int i = 0;
        foreach (var kv in table)
        {
            Indent(sb, 2); sb.Append(Str(kv.Key)).Append(": ").Append(kv.Value);
            sb.Append(++i < table.Count ? ",\n" : "\n");
        }
        Indent(sb, 1); sb.Append("}");
    }

    // ── tiny JSON helpers ───────────────────────────────────────────────

    private static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    private static string Num(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    private static string V2(Vector2 v) => $"{Num(v.x)},{Num(v.y)}";
    private static string V3(Vector3 v) => $"{Num(v.x)},{Num(v.y)},{Num(v.z)}";
    private static string V4(Vector4 v) => $"{Num(v.x)},{Num(v.y)},{Num(v.z)},{Num(v.w)}";
    private static string V4(Quaternion q) => $"{Num(q.x)},{Num(q.y)},{Num(q.z)},{Num(q.w)}";
    private static string Hex(Color c) =>
        "#" + ColorUtility.ToHtmlStringRGBA(c);

    private static string Str(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
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
                    if (ch < 32) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static string Indented(int n) => new string(' ', n * 2);
    private static void Indent(StringBuilder sb, int n) => sb.Append(Indented(n));

    private static void Field(StringBuilder sb, int ind, string key, string value)
    { Indent(sb, ind); sb.Append(Str(key)).Append(": ").Append(Str(value)); }
    private static void Field(StringBuilder sb, int ind, string key, bool value)
    { Indent(sb, ind); sb.Append(Str(key)).Append(": ").Append(value ? "true" : "false"); }
    private static void Field(StringBuilder sb, int ind, string key, int value)
    { Indent(sb, ind); sb.Append(Str(key)).Append(": ").Append(value.ToString(CultureInfo.InvariantCulture)); }
    private static void Field(StringBuilder sb, int ind, string key, float value)
    { Indent(sb, ind); sb.Append(Str(key)).Append(": ").Append(Num(value)); }
}

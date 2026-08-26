// SMSModForge — GC2 Dialogue Actor Extractor (Unity Editor script)
//
// HOW TO USE
//   1. Copy this file into your Starmaker Story Unity project under any
//      `Assets/Editor/` folder (create one if it doesn't exist — Unity
//      treats anything inside an `Editor` folder as editor-only code).
//   2. From Unity's main menu pick:  Tools › SMSModForge › Extract Actors…
//      You'll be prompted for an output `.json` file location.
//   3. Send the resulting JSON file back — it is consumed when authoring
//      the `actors` array of SMSAndroidsPack/modpack.json.
//
// WHAT GETS EXTRACTED
//   Every Game Creator 2 Dialogue `Actor` ScriptableObject in the project
//   (`AssetDatabase.FindAssets("t:Actor")`). For each actor the dump
//   records:
//     * assetName / assetPath        — the .asset file
//     * displayName                  — the speech-line name, when it is a
//                                      literal constant (see below)
//     * displayNameSource            — "constant", "dynamic:<type>" or
//                                      "assetName" (the fallback)
//     * portrait                     — GC2 Portrait mode (None/Primary/…)
//     * overrideSpeechSkin           — the override SpeechSkin asset name
//     * expressions[]                — id + hash + sprite asset for each
//                                      entry in the actor's expression set
//
// NOTES
//   * Pure reflection / SerializedObject — the script has NO compile-time
//     dependency on the Game Creator assemblies, so it drops into any
//     project and compiles regardless of how the GC2 DLLs are referenced.
//   * Actors whose name is a *dynamic* property (resolved from a variable
//     at run-time) have no literal name to read; `displayName` falls back
//     to the asset name and `displayNameSource` records the property type.
//   * Re-running the extractor just produces a fresh JSON file — it never
//     touches the source assets.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SMSModForge.UnityTools
{
    public static class SMSModForgeActorExtractor
    {
        private const string MenuPath = "Tools/SMSModForge/Extract Actors...";

        // ── Output model (JsonUtility-serializable) ──────────────────────

        [Serializable]
        private sealed class ExpressionDump
        {
            public string id;          // IdString readable value (the expression name)
            public int hash;           // IdString hash — handy for cross-referencing
            public string sprite;      // expression sprite asset name, if any
            public string spritePath;  // expression sprite asset path, if any
        }

        [Serializable]
        private sealed class ActorDump
        {
            public string assetName;
            public string assetPath;
            public string displayName;
            public string displayNameSource;
            public string portrait;
            public string overrideSpeechSkin;
            public List<ExpressionDump> expressions = new List<ExpressionDump>();
        }

        [Serializable]
        private sealed class ActorDumpFile
        {
            public int actorCount;
            public List<ActorDump> actors = new List<ActorDump>();
        }

        // ── Entry point ──────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void Run()
        {
            string[] guids = AssetDatabase.FindAssets("t:Actor");
            var file = new ActorDumpFile();

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(
                        "Extracting GC2 Dialogue actors",
                        path + " (" + (i + 1) + "/" + guids.Length + ")",
                        (float)i / Mathf.Max(1, guids.Length));
                    if (cancel) break;

                    try
                    {
                        var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                        if (so == null) continue;
                        var dump = ExtractActor(so, path);
                        if (dump != null) file.actors.Add(dump);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[SMSModForge] Actor extract failed for '" +
                                         path + "': " + ex.Message);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            file.actorCount = file.actors.Count;

            if (file.actorCount == 0)
            {
                EditorUtility.DisplayDialog(
                    "SMSModForge — Extract Actors",
                    "No Game Creator Dialogue 'Actor' assets were found in this " +
                    "project.\n\nThe extractor looks for ScriptableObject assets of " +
                    "type 'Actor' (the type created via Game Creator › Dialogue › " +
                    "Actor). Make sure the project containing them is open.",
                    "OK");
                return;
            }

            string defaultRoot = Path.GetDirectoryName(Application.dataPath) ?? "";
            string outPath = EditorUtility.SaveFilePanel(
                "Save extracted actors", defaultRoot, "SMSAndroidsActors", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            File.WriteAllText(outPath, JsonUtility.ToJson(file, true));

            EditorUtility.DisplayDialog(
                "SMSModForge — Extract Actors",
                "Extracted " + file.actorCount + " actor(s) to:\n\n" + outPath,
                "OK");
            Debug.Log("[SMSModForge] Extracted " + file.actorCount +
                      " actor(s) to " + outPath);
        }

        // ── Per-actor extraction ─────────────────────────────────────────

        private static ActorDump ExtractActor(ScriptableObject so, string path)
        {
            var sObj = new SerializedObject(so);

            var actant = sObj.FindProperty("m_Actant");
            var expressionsRoot = sObj.FindProperty("m_Expressions");

            // A GC2 Dialogue Actor always carries both. Anything else that
            // happens to share the 'Actor' type name is skipped.
            if (actant == null && expressionsRoot == null) return null;

            var dump = new ActorDump
            {
                assetName = so.name,
                assetPath = path,
            };

            string source;
            string name = ResolvePropertyGetString(actant, "m_Name", out source);
            if (string.IsNullOrEmpty(name))
            {
                dump.displayName = so.name;
                dump.displayNameSource = source ?? "assetName";
            }
            else
            {
                dump.displayName = name;
                dump.displayNameSource = source;
            }

            var portrait = sObj.FindProperty("m_Portrait");
            if (portrait != null && portrait.propertyType == SerializedPropertyType.Enum)
            {
                dump.portrait = (portrait.enumValueIndex >= 0 &&
                                 portrait.enumValueIndex < portrait.enumNames.Length)
                    ? portrait.enumNames[portrait.enumValueIndex]
                    : portrait.intValue.ToString();
            }

            var skin = sObj.FindProperty("m_OverrideSpeechSkin");
            if (skin != null &&
                skin.propertyType == SerializedPropertyType.ObjectReference &&
                skin.objectReferenceValue != null)
            {
                dump.overrideSpeechSkin = skin.objectReferenceValue.name;
            }

            dump.expressions = ExtractExpressions(expressionsRoot);
            return dump;
        }

        /// <summary>
        /// Resolve a GC2 <c>PropertyGetString</c> to its literal value.
        /// The structure is <c>&lt;field&gt; → m_Property (SerializeReference)
        /// → m_Value (string)</c>; <c>m_Value</c> only exists on the
        /// constant-string property type (<c>GetStringString</c>). For any
        /// other (dynamic) property type the literal cannot be read, so
        /// <paramref name="source"/> is set to <c>dynamic:&lt;type&gt;</c>
        /// and null is returned.
        /// </summary>
        private static string ResolvePropertyGetString(
            SerializedProperty owner, string fieldName, out string source)
        {
            source = "assetName";
            if (owner == null) return null;

            var propGet = owner.FindPropertyRelative(fieldName);   // PropertyGetString
            if (propGet == null) return null;

            var inner = propGet.FindPropertyRelative("m_Property"); // [SerializeReference]
            if (inner == null) return null;

            var value = inner.FindPropertyRelative("m_Value");
            if (value != null && value.propertyType == SerializedPropertyType.String)
            {
                source = "constant";
                return value.stringValue;
            }

            string typeName = inner.managedReferenceFullTypename;
            source = string.IsNullOrEmpty(typeName)
                ? "dynamic"
                : "dynamic:" + ShortTypeName(typeName);
            return null;
        }

        // ── Expressions ──────────────────────────────────────────────────

        private static List<ExpressionDump> ExtractExpressions(SerializedProperty expressionsRoot)
        {
            var result = new List<ExpressionDump>();
            if (expressionsRoot == null) return result;

            // Expressions wrapper → m_Expressions (Expression[]).
            var array = expressionsRoot.FindPropertyRelative("m_Expressions");
            if (array == null || !array.isArray) return result;

            for (int i = 0; i < array.arraySize; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                if (element == null) continue;
                result.Add(ExtractOneExpression(element));
            }
            return result;
        }

        private static ExpressionDump ExtractOneExpression(SerializedProperty element)
        {
            var dump = new ExpressionDump();

            // The id is an IdString; its readable half serialises as a
            // string field, the lookup half as an int. Prefer descendants
            // whose path mentions "Id" so we don't grab an unrelated string.
            SerializedProperty idString = null;
            SerializedProperty idHash = null;
            UnityEngine.Object spriteRef = null;

            foreach (var child in Descendants(element))
            {
                bool underId = child.propertyPath.IndexOf("Id", StringComparison.OrdinalIgnoreCase) >= 0;

                if (child.propertyType == SerializedPropertyType.String)
                {
                    if (underId && idString == null) idString = child.Copy();
                    else if (idString == null) idString = child.Copy();
                }
                else if (child.propertyType == SerializedPropertyType.Integer)
                {
                    if (underId && idHash == null) idHash = child.Copy();
                }
                else if (child.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var v = child.objectReferenceValue;
                    if (spriteRef == null && (v is Sprite || v is Texture))
                        spriteRef = v;
                }
            }

            if (idString != null) dump.id = idString.stringValue;
            if (idHash != null) dump.hash = idHash.intValue;
            if (spriteRef != null)
            {
                dump.sprite = spriteRef.name;
                dump.spritePath = AssetDatabase.GetAssetPath(spriteRef);
            }
            return dump;
        }

        // ── SerializedProperty helpers ───────────────────────────────────

        /// <summary>Enumerate every descendant property of <paramref name="parent"/>.</summary>
        private static IEnumerable<SerializedProperty> Descendants(SerializedProperty parent)
        {
            if (parent == null) yield break;
            var it = parent.Copy();
            var end = parent.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                if (SerializedProperty.EqualContents(it, end)) yield break;
                enter = true;
                yield return it;
            }
        }

        /// <summary>
        /// Trim a <c>managedReferenceFullTypename</c> ("Assembly Namespace.Type")
        /// down to just the short type name for compact diagnostics.
        /// </summary>
        private static string ShortTypeName(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return fullTypeName;
            int space = fullTypeName.LastIndexOf(' ');
            string typePart = space >= 0 ? fullTypeName.Substring(space + 1) : fullTypeName;
            int dot = typePart.LastIndexOf('.');
            return dot >= 0 ? typePart.Substring(dot + 1) : typePart;
        }
    }
}

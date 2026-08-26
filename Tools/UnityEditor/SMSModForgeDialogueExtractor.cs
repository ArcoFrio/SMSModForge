// SMSModForge — GC2 Dialogue Extractor (Unity Editor script)
//
// HOW TO USE
//   1. Copy this file into your Starmaker Story *asset*
//      Unity project under any `Assets/Editor/` folder (create one if it
//      doesn't exist — Unity treats anything inside an `Editor` folder as
//      editor-only code). This must be the project that contains the
//      dialogue *prefabs* (the ones shipped in `dialoguebundle`).
//   2. From Unity's main menu pick:  Tools › SMSModForge › Extract
//      Dialogues…   You'll be prompted for an output `.json` file.
//   3. Send the resulting JSON file back — it is consumed when authoring
//      the `dialogues` array of a pack's modpack.json.
//
// WHAT GETS EXTRACTED
//   Every prefab asset in the project whose ROOT GameObject carries a
//   Game Creator 2 `Dialogue` component (`GameCreator.Runtime.Dialogue.
//   Dialogue`). For each such prefab the dump records, faithfully and in
//   full:
//     * assetName / assetPath              — the .prefab file
//     * hierarchy                          — the prefab's complete child
//       GameObject tree (names, active state, components). This is where
//       a mod's "marker" children live — `Scene1`..`SceneN`,
//       `DialogueActivator`, `DialogueFinisher`, `MouthActivator`,
//       `SpriteFocus` — the GameObjects a dialogue node's instructions
//       toggle on/off and that MainStory.cs polls.
//     * dialogue.tree                      — the GC2 dialogue graph:
//         - rootIds                        — the root node id list
//         - nodes[]                        — every node, with id /
//           parent / children, a human-readable `summary` (text, actor,
//           expression, node-type, instruction & condition type lists),
//           and the COMPLETE `value` — a generic, recursive dump of the
//           GC2 `Node` object: its `m_NodeType`, `m_Text`, `m_Acting`,
//           `m_Conditions`, `m_OnStart` / `m_OnFinish` instruction lists,
//           `m_Tag`, `m_Jump`, `m_Duration`, `m_Audio`, `m_Animation`,
//           etc. Every polymorphic `[SerializeReference]` instruction /
//           condition records its full managed type name + all fields,
//           recursively, so NOTHING is lost.
//     * summary                            — file-level roll-ups of every
//       instruction type, condition type, node type, marker child name
//       and component type seen across all dialogues. Handy for planning
//       the GC2 → ModForge action/condition translation.
//
// NOTES
//   * Pure reflection / SerializedObject — the script has NO compile-time
//     dependency on the Game Creator assemblies, so it drops into any
//     project and compiles regardless of how the GC2 DLLs are referenced.
//   * Because every node's `value` is a *generic* property dump, the
//     extractor never needs to know which concrete GC2 Instruction or
//     Condition subclasses the dialogue authors used — it captures them
//     all, including any custom / modded ones.
//   * GameObject references inside an instruction (e.g. a "Set Active"
//     instruction pointing at a `Scene2` marker child) are resolved to
//     the marker's name + its path within the prefab, so the GC2 →
//     ModForge translation can see exactly which marker each node drives.
//   * Re-running the extractor just produces a fresh JSON file — it never
//     touches the source assets.
//   * Scanning every prefab in the project can take a while on a large
//     project; a cancellable progress bar is shown.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SMSModForge.UnityTools
{
    public static class SMSModForgeDialogueExtractor
    {
        private const string MenuPath = "Tools/SMSModForge/Extract Dialogues...";
        private const string ExtractorVersion = "1.0";

        // Recursion guard for the generic SerializedProperty dump. GC2's
        // PropertyGet chains and node sequences nest a handful of levels;
        // 18 is comfortably deep without risking a runaway walk.
        private const int MaxDepth = 18;

        // Component type names that carry no logic worth dumping — for
        // these the hierarchy records just the type name. Anything else
        // (Triggers, Instruction/Condition holders, custom behaviours…)
        // gets a full field dump so marker-side logic isn't missed.
        private static readonly HashSet<string> ShallowComponentTypes = new HashSet<string>
        {
            "Transform", "RectTransform", "CanvasRenderer", "MeshFilter",
            "MeshRenderer", "Canvas", "CanvasGroup", "GraphicRaycaster",
        };

        // ── File-level roll-up sets (populated during extraction) ────────
        private static SortedSet<string> s_instructionTypes;
        private static SortedSet<string> s_conditionTypes;
        private static SortedSet<string> s_nodeTypes;
        private static SortedSet<string> s_managedTypes;
        private static SortedSet<string> s_markerNames;
        private static SortedSet<string> s_componentTypes;

        // Root transform of the prefab currently being extracted — used to
        // tell "GameObject reference inside this prefab" apart from
        // "external asset reference".
        private static Transform s_prefabRoot;

        // ── Entry point ──────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void Run()
        {
            s_instructionTypes = new SortedSet<string>(StringComparer.Ordinal);
            s_conditionTypes   = new SortedSet<string>(StringComparer.Ordinal);
            s_nodeTypes        = new SortedSet<string>(StringComparer.Ordinal);
            s_managedTypes     = new SortedSet<string>(StringComparer.Ordinal);
            s_markerNames      = new SortedSet<string>(StringComparer.Ordinal);
            s_componentTypes   = new SortedSet<string>(StringComparer.Ordinal);

            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            var dialogues = new JArr();

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(
                        "Extracting GC2 dialogues",
                        path + " (" + (i + 1) + "/" + guids.Length + ")",
                        (float)i / Mathf.Max(1, guids.Length));
                    if (cancel) break;

                    try
                    {
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (go == null) continue;

                        var dialogueComponent = FindDialogueComponent(go);
                        if (dialogueComponent == null) continue;

                        var dump = ExtractDialogue(go, dialogueComponent, path);
                        if (dump != null) dialogues.Add(dump);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[SMSModForge] Dialogue extract failed for '" +
                                         path + "': " + ex.Message + "\n" + ex.StackTrace);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (dialogues.Items.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "SMSModForge — Extract Dialogues",
                    "No prefabs with a Game Creator 'Dialogue' component on the " +
                    "root GameObject were found in this project.\n\nMake sure the " +
                    "Unity project containing the dialogue prefabs is the one " +
                    "currently open.",
                    "OK");
                return;
            }

            // ── Assemble the output document ─────────────────────────────
            var summary = new JObj();
            summary.Add("nodeTypes",        ToJArr(s_nodeTypes));
            summary.Add("instructionTypes", ToJArr(s_instructionTypes));
            summary.Add("conditionTypes",   ToJArr(s_conditionTypes));
            summary.Add("markerChildNames", ToJArr(s_markerNames));
            summary.Add("componentTypes",   ToJArr(s_componentTypes));
            summary.Add("allManagedTypes",  ToJArr(s_managedTypes));

            var root = new JObj();
            root.Add("extractorVersion", ExtractorVersion);
            root.Add("generatedUtc", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            root.Add("dialogueCount", dialogues.Items.Count);
            root.Add("summary", summary);
            root.Add("dialogues", dialogues);

            string defaultRoot = Path.GetDirectoryName(Application.dataPath) ?? "";
            string outPath = EditorUtility.SaveFilePanel(
                "Save extracted dialogues", defaultRoot, "ExtractedDialogues", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            var sb = new StringBuilder(1 << 20);
            WriteValue(sb, root, 0);
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));

            EditorUtility.DisplayDialog(
                "SMSModForge — Extract Dialogues",
                "Extracted " + dialogues.Items.Count + " dialogue(s) to:\n\n" + outPath,
                "OK");
            Debug.Log("[SMSModForge] Extracted " + dialogues.Items.Count +
                      " dialogue(s) to " + outPath);
        }

        // ── Dialogue detection ───────────────────────────────────────────

        /// <summary>
        /// Return the GC2 <c>Dialogue</c> component on a prefab's root
        /// GameObject, or null. Matched by type name + namespace (no
        /// compile-time GC2 dependency) and confirmed by the presence of
        /// the serialized <c>m_Story</c> field.
        /// </summary>
        private static Component FindDialogueComponent(GameObject root)
        {
            var components = root.GetComponents<Component>();
            foreach (var c in components)
            {
                if (c == null) continue;
                var t = c.GetType();
                if (t.Name != "Dialogue") continue;
                if (t.Namespace != "GameCreator.Runtime.Dialogue") continue;
                var so = new SerializedObject(c);
                if (so.FindProperty("m_Story") != null) return c;
            }
            return null;
        }

        // ── Per-dialogue extraction ──────────────────────────────────────

        private static JObj ExtractDialogue(GameObject root, Component dialogueComponent, string path)
        {
            s_prefabRoot = root.transform;

            var dump = new JObj();
            dump.Add("assetName", root.name);
            dump.Add("assetPath", path);
            dump.Add("rootActive", root.activeSelf);

            // The prefab's full child hierarchy — names, active state and
            // components. This is where the Scene1..N / DialogueActivator /
            // DialogueFinisher / MouthActivator / SpriteFocus markers live.
            dump.Add("hierarchy", DumpHierarchy(root.transform, root.transform, isRoot: true));

            // The actual GC2 dialogue graph.
            var sObj = new SerializedObject(dialogueComponent);
            var storyProp = sObj.FindProperty("m_Story");
            var contentProp = storyProp != null ? storyProp.FindPropertyRelative("m_Content") : null;

            if (contentProp == null)
            {
                dump.Add("dialogueError", "m_Story.m_Content not found on the Dialogue component");
                return dump;
            }

            dump.Add("dialogue", DumpDialogueContent(contentProp));
            return dump;
        }

        // ── GameObject hierarchy dump ────────────────────────────────────

        private static JObj DumpHierarchy(Transform t, Transform prefabRoot, bool isRoot)
        {
            var go = t.gameObject;
            var node = new JObj();
            node.Add("name", go.name);
            node.Add("active", go.activeSelf);
            if (!isRoot)
            {
                node.Add("path", TransformPath(t, prefabRoot));
                s_markerNames.Add(go.name);
            }

            // Components on this GameObject.
            var comps = new JArr();
            var components = go.GetComponents<Component>();
            foreach (var c in components)
            {
                comps.Add(DumpComponent(c));
            }
            node.Add("components", comps);

            // Children.
            if (t.childCount > 0)
            {
                var children = new JArr();
                for (int i = 0; i < t.childCount; i++)
                {
                    children.Add(DumpHierarchy(t.GetChild(i), prefabRoot, isRoot: false));
                }
                node.Add("children", children);
            }
            return node;
        }

        /// <summary>
        /// Dump a single component. The GC2 <c>Dialogue</c> component is
        /// recorded as a stub (its graph is dumped in the dedicated
        /// <c>dialogue</c> section). Pure-visual / engine components record
        /// only their type name. Logic-bearing components (Triggers,
        /// Instruction/Condition holders, custom behaviours) get a full
        /// recursive field dump so any marker-side logic is preserved.
        /// </summary>
        private static object DumpComponent(Component c)
        {
            var obj = new JObj();
            if (c == null)
            {
                obj.Add("type", "<missing script>");
                return obj;
            }

            var t = c.GetType();
            string typeName = t.Name;
            s_componentTypes.Add(typeName);
            obj.Add("type", typeName);
            if (!string.IsNullOrEmpty(t.Namespace)) obj.Add("namespace", t.Namespace);

            var behaviour = c as Behaviour;
            if (behaviour != null) obj.Add("enabled", behaviour.enabled);

            if (typeName == "Dialogue" && t.Namespace == "GameCreator.Runtime.Dialogue")
            {
                obj.Add("note", "dialogue graph dumped in the 'dialogue' section");
                return obj;
            }

            if (typeName == "SpriteRenderer")
            {
                var sr = c as SpriteRenderer;
                obj.Add("sprite", sr != null && sr.sprite != null ? sr.sprite.name : null);
                return obj;
            }

            if (ShallowComponentTypes.Contains(typeName))
                return obj;

            // Everything else: full recursive field dump. Captures GC2
            // Triggers, Instructions/Conditions holders and any custom
            // MonoBehaviours that might sit on a marker GameObject.
            try
            {
                var so = new SerializedObject(c);
                var fields = new JObj();
                foreach (var child in TopLevelProperties(so))
                {
                    if (child.name == "m_Script") continue;
                    fields.Add(child.name, DumpProperty(child, 1));
                }
                if (fields.Items.Count > 0) obj.Add("fields", fields);
            }
            catch (Exception ex)
            {
                obj.Add("dumpError", ex.Message);
            }
            return obj;
        }

        // ── Dialogue content (the GC2 graph) dump ────────────────────────

        private static JObj DumpDialogueContent(SerializedProperty contentProp)
        {
            var result = new JObj();

            // Skin + roles live directly on Content.
            var skinProp = contentProp.FindPropertyRelative("m_DialogueSkin");
            if (skinProp != null && skinProp.propertyType == SerializedPropertyType.ObjectReference)
                result.Add("dialogueSkin", DumpObjectRef(skinProp.objectReferenceValue));

            var rolesProp = contentProp.FindPropertyRelative("m_Roles");
            if (rolesProp != null && rolesProp.isArray)
                result.Add("roles", DumpProperty(rolesProp, 1));

            // The tree backing store.
            var dataProp  = contentProp.FindPropertyRelative("m_Data");   // TSerializableDictionary<int, TTreeDataItem<Node>>
            var nodesProp = contentProp.FindPropertyRelative("m_Nodes");  // TSerializableDictionary<int, TreeNode>
            var rootsProp = contentProp.FindPropertyRelative("m_Roots");  // List<int>

            // Root id list.
            var rootIds = new JArr();
            if (rootsProp != null && rootsProp.isArray)
                for (int i = 0; i < rootsProp.arraySize; i++)
                    rootIds.Add((long)rootsProp.GetArrayElementAtIndex(i).intValue);
            result.Add("rootIds", rootIds);

            // Build the id -> {parent, children} map from m_Nodes.
            var parentOf = new Dictionary<int, int>();
            var childrenOf = new Dictionary<int, List<int>>();
            if (nodesProp != null)
            {
                var keys   = nodesProp.FindPropertyRelative("m_Keys");
                var values = nodesProp.FindPropertyRelative("m_Values");
                if (keys != null && values != null && keys.isArray && values.isArray)
                {
                    int count = Mathf.Min(keys.arraySize, values.arraySize);
                    for (int i = 0; i < count; i++)
                    {
                        int id = keys.GetArrayElementAtIndex(i).intValue;
                        var treeNode = values.GetArrayElementAtIndex(i);

                        var parentProp = FindFirstRelative(treeNode, "m_Parent", "m_ParentId");
                        if (parentProp != null) parentOf[id] = parentProp.intValue;

                        var childrenProp = FindFirstRelative(treeNode, "m_Children", "m_ChildIds");
                        if (childrenProp != null && childrenProp.isArray)
                        {
                            var list = new List<int>();
                            for (int c = 0; c < childrenProp.arraySize; c++)
                                list.Add(childrenProp.GetArrayElementAtIndex(c).intValue);
                            childrenOf[id] = list;
                        }
                    }
                }
            }

            // Raw m_Nodes dump as a safety net, in case the parent/children
            // field names differ from the assumed ones above.
            if (nodesProp != null)
                result.Add("treeNodesRaw", DumpProperty(nodesProp, 1));

            // Walk m_Data — the id -> Node payload store.
            var nodesOut = new JArr();
            if (dataProp != null)
            {
                var keys   = dataProp.FindPropertyRelative("m_Keys");
                var values = dataProp.FindPropertyRelative("m_Values");
                if (keys != null && values != null && keys.isArray && values.isArray)
                {
                    int count = Mathf.Min(keys.arraySize, values.arraySize);
                    for (int i = 0; i < count; i++)
                    {
                        int id = keys.GetArrayElementAtIndex(i).intValue;
                        var item = values.GetArrayElementAtIndex(i);          // TTreeDataItem<Node>
                        var nodeValueProp = item != null ? item.FindPropertyRelative("m_Value") : null;

                        var nodeOut = new JObj();
                        nodeOut.Add("id", (long)id);
                        nodeOut.Add("parent", parentOf.TryGetValue(id, out var par) ? (object)(long)par : null);

                        var childArr = new JArr();
                        if (childrenOf.TryGetValue(id, out var kids))
                            foreach (var k in kids) childArr.Add((long)k);
                        nodeOut.Add("children", childArr);

                        nodeOut.Add("isRoot", IndexOfInt(rootIds, id) >= 0);

                        if (nodeValueProp != null)
                        {
                            nodeOut.Add("summary", BuildNodeSummary(nodeValueProp));
                            nodeOut.Add("value", DumpProperty(nodeValueProp, 1));
                        }
                        else
                        {
                            nodeOut.Add("nodeError", "TTreeDataItem.m_Value missing");
                        }
                        nodesOut.Add(nodeOut);
                    }
                }
            }
            result.Add("nodeCount", nodesOut.Items.Count);
            result.Add("nodes", nodesOut);
            return result;
        }

        /// <summary>
        /// Build a compact, human-readable summary of one GC2 Node — the
        /// fields a reader most wants at a glance. The full fidelity is in
        /// the node's <c>value</c> dump; this is just for convenience.
        /// </summary>
        private static JObj BuildNodeSummary(SerializedProperty nodeProp)
        {
            var summary = new JObj();

            // Node type (NodeTypeText / NodeTypeChoice / NodeTypeRandom).
            var typeProp = nodeProp.FindPropertyRelative("m_NodeType");
            if (typeProp != null && typeProp.propertyType == SerializedPropertyType.ManagedReference)
            {
                string nt = ShortType(typeProp.managedReferenceFullTypename);
                if (!string.IsNullOrEmpty(nt))
                {
                    summary.Add("nodeType", nt);
                    s_nodeTypes.Add(nt);
                }
            }

            // Literal line text.
            summary.Add("text", ResolveNodeText(nodeProp));

            // Speaker / expression / portrait.
            var acting = nodeProp.FindPropertyRelative("m_Acting");
            if (acting != null)
            {
                var actorProp = acting.FindPropertyRelative("m_Actor");
                if (actorProp != null && actorProp.propertyType == SerializedPropertyType.ObjectReference)
                    summary.Add("actor", actorProp.objectReferenceValue != null
                        ? actorProp.objectReferenceValue.name : null);

                var exprProp = acting.FindPropertyRelative("m_Expression");
                if (exprProp != null) summary.Add("expressionIndex", (long)exprProp.intValue);

                var portraitProp = acting.FindPropertyRelative("m_Portrait");
                if (portraitProp != null && portraitProp.propertyType == SerializedPropertyType.Enum)
                    summary.Add("portrait", EnumName(portraitProp));
            }

            // Tag (IdString).
            var tagProp = nodeProp.FindPropertyRelative("m_Tag");
            if (tagProp != null)
            {
                var tagStr = FindFirstRelative(tagProp, "m_String", "m_Name", "m_Id");
                if (tagStr != null && tagStr.propertyType == SerializedPropertyType.String &&
                    !string.IsNullOrEmpty(tagStr.stringValue))
                    summary.Add("tag", tagStr.stringValue);
            }

            // OnStart / OnFinish / condition type lists.
            summary.Add("onStartInstructions",
                CollectPolymorphicTypes(nodeProp, "m_OnStart", "m_Instructions", "m_Instructions", s_instructionTypes));
            summary.Add("onFinishInstructions",
                CollectPolymorphicTypes(nodeProp, "m_OnFinish", "m_Instructions", "m_Instructions", s_instructionTypes));
            summary.Add("conditions",
                CollectPolymorphicTypes(nodeProp, "m_Conditions", "m_Conditions", "m_Conditions", s_conditionTypes));

            return summary;
        }

        /// <summary>
        /// Resolve a Node's literal text. The chain is
        /// <c>m_Text (NodeText) → m_Text (PropertyGetString) → m_Property
        /// ([SerializeReference]) → m_Value (string)</c>. Returns null when
        /// the text is a dynamic property with no literal value.
        /// </summary>
        private static string ResolveNodeText(SerializedProperty nodeProp)
        {
            var nodeText = nodeProp.FindPropertyRelative("m_Text");
            if (nodeText == null) return null;
            var propGet = nodeText.FindPropertyRelative("m_Text");
            if (propGet == null) return null;
            var inner = propGet.FindPropertyRelative("m_Property");
            if (inner == null) return null;
            var value = inner.FindPropertyRelative("m_Value");
            if (value != null && value.propertyType == SerializedPropertyType.String)
                return value.stringValue;
            return null;
        }

        /// <summary>
        /// Walk a node's polymorphic list (OnStart / OnFinish / Conditions),
        /// add every element's short type name to <paramref name="sink"/>,
        /// and return them as a JArr. The path is
        /// <c>node → outer (RunInstructionsList / RunConditionsList) → mid
        /// (InstructionList / ConditionList) → inner (the array)</c>.
        /// </summary>
        private static JArr CollectPolymorphicTypes(SerializedProperty nodeProp,
            string outerName, string midName, string innerName, SortedSet<string> sink)
        {
            var result = new JArr();
            var outer = nodeProp.FindPropertyRelative(outerName);
            if (outer == null) return result;
            var mid = outer.FindPropertyRelative(midName);
            if (mid == null) return result;
            var inner = mid.FindPropertyRelative(innerName);
            if (inner == null || !inner.isArray) return result;

            for (int i = 0; i < inner.arraySize; i++)
            {
                var element = inner.GetArrayElementAtIndex(i);
                if (element == null) continue;
                if (element.propertyType == SerializedPropertyType.ManagedReference)
                {
                    string st = ShortType(element.managedReferenceFullTypename);
                    if (!string.IsNullOrEmpty(st))
                    {
                        result.Add(st);
                        if (sink != null) sink.Add(st);
                    }
                    else result.Add(null);
                }
            }
            return result;
        }

        // ── Generic recursive SerializedProperty dump ────────────────────

        /// <summary>
        /// Faithfully dump any SerializedProperty to a JSON-able value.
        /// Primitives become primitives; managed references become objects
        /// carrying <c>__type</c> + their fields; object references become
        /// a small descriptor; arrays become lists; nested structs/classes
        /// recurse. This is what guarantees every instruction / condition
        /// is captured regardless of its concrete type.
        /// </summary>
        private static object DumpProperty(SerializedProperty p, int depth)
        {
            if (p == null) return null;
            if (depth > MaxDepth) return "<max-depth-exceeded>";

            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:      return p.longValue;
                case SerializedPropertyType.Boolean:      return p.boolValue;
                case SerializedPropertyType.Float:        return p.doubleValue;
                case SerializedPropertyType.String:       return p.stringValue;
                case SerializedPropertyType.ArraySize:    return (long)p.intValue;
                case SerializedPropertyType.Character:    return (long)p.intValue;
                case SerializedPropertyType.LayerMask:    return (long)p.intValue;
                case SerializedPropertyType.Enum:         return EnumName(p);
                case SerializedPropertyType.ObjectReference:
                    return DumpObjectRef(p.objectReferenceValue);

                case SerializedPropertyType.Color:
                {
                    var c = p.colorValue;
                    var o = new JObj();
                    o.Add("r", (double)c.r); o.Add("g", (double)c.g);
                    o.Add("b", (double)c.b); o.Add("a", (double)c.a);
                    return o;
                }
                case SerializedPropertyType.Vector2:
                {
                    var v = p.vector2Value;
                    var o = new JObj(); o.Add("x", (double)v.x); o.Add("y", (double)v.y);
                    return o;
                }
                case SerializedPropertyType.Vector3:
                {
                    var v = p.vector3Value;
                    var o = new JObj();
                    o.Add("x", (double)v.x); o.Add("y", (double)v.y); o.Add("z", (double)v.z);
                    return o;
                }
                case SerializedPropertyType.Vector4:
                {
                    var v = p.vector4Value;
                    var o = new JObj();
                    o.Add("x", (double)v.x); o.Add("y", (double)v.y);
                    o.Add("z", (double)v.z); o.Add("w", (double)v.w);
                    return o;
                }
                case SerializedPropertyType.Vector2Int:
                {
                    var v = p.vector2IntValue;
                    var o = new JObj(); o.Add("x", (long)v.x); o.Add("y", (long)v.y);
                    return o;
                }
                case SerializedPropertyType.Vector3Int:
                {
                    var v = p.vector3IntValue;
                    var o = new JObj();
                    o.Add("x", (long)v.x); o.Add("y", (long)v.y); o.Add("z", (long)v.z);
                    return o;
                }
                case SerializedPropertyType.Quaternion:
                {
                    var q = p.quaternionValue;
                    var o = new JObj();
                    o.Add("x", (double)q.x); o.Add("y", (double)q.y);
                    o.Add("z", (double)q.z); o.Add("w", (double)q.w);
                    return o;
                }
                case SerializedPropertyType.Rect:
                {
                    var r = p.rectValue;
                    var o = new JObj();
                    o.Add("x", (double)r.x); o.Add("y", (double)r.y);
                    o.Add("width", (double)r.width); o.Add("height", (double)r.height);
                    return o;
                }
                case SerializedPropertyType.Bounds:
                {
                    var b = p.boundsValue;
                    var o = new JObj();
                    o.Add("centerX", (double)b.center.x); o.Add("centerY", (double)b.center.y);
                    o.Add("centerZ", (double)b.center.z);
                    o.Add("sizeX", (double)b.size.x); o.Add("sizeY", (double)b.size.y);
                    o.Add("sizeZ", (double)b.size.z);
                    return o;
                }
                case SerializedPropertyType.AnimationCurve: return "<AnimationCurve>";
                case SerializedPropertyType.Gradient:       return "<Gradient>";

                case SerializedPropertyType.ManagedReference:
                {
                    string typeName = p.managedReferenceFullTypename;
                    if (string.IsNullOrEmpty(typeName)) return null;   // null reference
                    string shortName = ShortType(typeName);
                    s_managedTypes.Add(shortName);
                    var o = new JObj();
                    o.Add("__type", shortName);
                    o.Add("__typeFull", typeName);
                    foreach (var child in DirectChildren(p))
                        o.Add(child.name, DumpProperty(child, depth + 1));
                    return o;
                }

                case SerializedPropertyType.Generic:
                {
                    if (p.isArray)
                    {
                        var arr = new JArr();
                        int n = p.arraySize;
                        for (int i = 0; i < n; i++)
                            arr.Add(DumpProperty(p.GetArrayElementAtIndex(i), depth + 1));
                        return arr;
                    }
                    var o = new JObj();
                    foreach (var child in DirectChildren(p))
                        o.Add(child.name, DumpProperty(child, depth + 1));
                    return o;
                }

                default:
                    return "<unhandled:" + p.propertyType + ">";
            }
        }

        /// <summary>
        /// Describe a UnityEngine.Object reference. References to objects
        /// living inside the prefab currently being extracted record their
        /// in-prefab transform path; everything else records its asset
        /// path. This is how a "Set Active" instruction pointing at a
        /// `Scene2` marker child stays traceable.
        /// </summary>
        private static object DumpObjectRef(UnityEngine.Object o)
        {
            if (o == null) return null;
            var jo = new JObj();
            jo.Add("name", o.name);
            jo.Add("type", o.GetType().Name);

            Transform tr = null;
            if (o is GameObject g) tr = g.transform;
            else if (o is Component c) tr = c.transform;

            if (tr != null && s_prefabRoot != null && IsDescendantOrSelf(tr, s_prefabRoot))
            {
                jo.Add("inPrefab", true);
                jo.Add("prefabPath", TransformPath(tr, s_prefabRoot));
                if (o is Component comp) jo.Add("componentType", comp.GetType().Name);
            }
            else
            {
                string assetPath = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(assetPath)) jo.Add("assetPath", assetPath);
            }
            return jo;
        }

        // ── SerializedProperty helpers ───────────────────────────────────

        /// <summary>
        /// Enumerate the immediate children of a property. Uses
        /// <see cref="SerializedProperty.Next"/> (not <c>NextVisible</c>)
        /// and depth tracking so that <c>[HideInInspector]</c> serialized
        /// fields are still captured — GC2 hides several fields that carry
        /// real data, e.g. a Condition's <c>m_Sign</c> (the If / Not
        /// negation flag) and <c>TPolymorphicItem</c>'s <c>m_IsEnabled</c>
        /// / <c>m_Breakpoint</c>. Missing those would silently drop
        /// meaning from the extracted graph.
        /// </summary>
        private static IEnumerable<SerializedProperty> DirectChildren(SerializedProperty parent)
        {
            if (parent == null) yield break;
            var it = parent.Copy();
            int parentDepth = it.depth;
            bool enterChildren = true;
            while (it.Next(enterChildren))
            {
                enterChildren = false;
                if (it.depth <= parentDepth) yield break;   // left the subtree
                if (it.depth == parentDepth + 1)             // a direct child
                    yield return it.Copy();
            }
        }

        /// <summary>Enumerate the top-level properties of a SerializedObject.</summary>
        private static IEnumerable<SerializedProperty> TopLevelProperties(SerializedObject so)
        {
            var it = so.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                yield return it.Copy();
            }
        }

        /// <summary>Return the first relative property found among several candidate names.</summary>
        private static SerializedProperty FindFirstRelative(SerializedProperty owner, params string[] names)
        {
            if (owner == null) return null;
            foreach (var n in names)
            {
                var p = owner.FindPropertyRelative(n);
                if (p != null) return p;
            }
            return null;
        }

        private static string EnumName(SerializedProperty p)
        {
            int idx = p.enumValueIndex;
            var names = p.enumNames;
            if (names != null && idx >= 0 && idx < names.Length) return names[idx];
            return p.intValue.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsDescendantOrSelf(Transform t, Transform root)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur == root) return true;
            return false;
        }

        /// <summary>Path of a transform relative to (and excluding) a root.</summary>
        private static string TransformPath(Transform t, Transform root)
        {
            if (t == root) return "";
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent)
                parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        /// <summary>
        /// Trim a <c>managedReferenceFullTypename</c> ("Assembly
        /// Namespace.Type" or "Assembly Namespace.Outer/Nested") down to
        /// the short type name.
        /// </summary>
        private static string ShortType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return fullTypeName;
            int space = fullTypeName.LastIndexOf(' ');
            string typePart = space >= 0 ? fullTypeName.Substring(space + 1) : fullTypeName;
            int slash = typePart.LastIndexOf('/');
            if (slash >= 0) typePart = typePart.Substring(slash + 1);
            int dot = typePart.LastIndexOf('.');
            return dot >= 0 ? typePart.Substring(dot + 1) : typePart;
        }

        private static int IndexOfInt(JArr arr, int value)
        {
            for (int i = 0; i < arr.Items.Count; i++)
                if (arr.Items[i] is long l && l == value) return i;
            return -1;
        }

        private static JArr ToJArr(IEnumerable<string> items)
        {
            var arr = new JArr();
            foreach (var s in items) arr.Add(s);
            return arr;
        }

        // ── Minimal ordered JSON model + writer (no external deps) ───────

        /// <summary>An ordered JSON object — preserves key insertion order.</summary>
        private sealed class JObj
        {
            public readonly List<KeyValuePair<string, object>> Items =
                new List<KeyValuePair<string, object>>();
            public void Add(string key, object value)
            {
                Items.Add(new KeyValuePair<string, object>(key, value));
            }
        }

        /// <summary>A JSON array.</summary>
        private sealed class JArr
        {
            public readonly List<object> Items = new List<object>();
            public void Add(object value) { Items.Add(value); }
        }

        private static void WriteValue(StringBuilder sb, object v, int indent)
        {
            if (v == null) { sb.Append("null"); return; }

            if (v is string s)      { WriteString(sb, s); return; }
            if (v is bool b)        { sb.Append(b ? "true" : "false"); return; }
            if (v is int i)         { sb.Append(i.ToString(CultureInfo.InvariantCulture)); return; }
            if (v is long l)        { sb.Append(l.ToString(CultureInfo.InvariantCulture)); return; }
            if (v is float f)       { WriteNumber(sb, f); return; }
            if (v is double d)      { WriteNumber(sb, d); return; }
            if (v is JObj o)        { WriteObject(sb, o, indent); return; }
            if (v is JArr a)        { WriteArray(sb, a, indent); return; }

            WriteString(sb, v.ToString());
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder sb, JObj o, int indent)
        {
            if (o.Items.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\n");
            string pad = new string(' ', (indent + 1) * 2);
            for (int i = 0; i < o.Items.Count; i++)
            {
                sb.Append(pad);
                WriteString(sb, o.Items[i].Key);
                sb.Append(": ");
                WriteValue(sb, o.Items[i].Value, indent + 1);
                if (i < o.Items.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(new string(' ', indent * 2));
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, JArr a, int indent)
        {
            if (a.Items.Count == 0) { sb.Append("[]"); return; }
            sb.Append("[\n");
            string pad = new string(' ', (indent + 1) * 2);
            for (int i = 0; i < a.Items.Count; i++)
            {
                sb.Append(pad);
                WriteValue(sb, a.Items[i], indent + 1);
                if (i < a.Items.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(new string(' ', indent * 2));
            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}

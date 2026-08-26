// SMSModForge — vanilla UI / layout extractor  (Unity Editor script)
//
// Companion to SMSModForgeLevelExtractor. That one answers "what is in each
// level"; this answers "how big is everything, and where exactly does it sit".
//
// The editor's Places preview reproduces the game's framing from hard-coded
// constants — a 2048x1136 canvas, level art at 70.32 px/unit, navigator buttons
// 150px on a 6-column strip whose row centre is at y=1052, label text at a fixed
// offset inside each button. Every one of those was measured by eye from
// screenshots. They are close, which is worse than being obviously wrong: the
// preview reads as trustworthy while positions drift by a few pixels, and there
// is no way to tell a mis-set constant from a mis-authored object.
//
// So measure them instead. Open the game's CoreGameScene and run
//   Tools > SMSModForge > Extract Vanilla UI Spec
// which writes vanilla_ui.json next to the level catalog.
//
// Nothing here modifies the project — it reads the open scene and writes one
// JSON file.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.EditorTools
{
    public static class SMSModForgeUiExtractor
    {
        private const string MenuPath = "Tools/SMSModForge/Extract Vanilla UI Spec…";
        private const string OutFileName = "vanilla_ui.json";

        /// <summary>Roots worth describing: the gameplay canvas and the level
        /// container. Missing ones are reported rather than aborting — a scene
        /// that lacks one still yields a useful file for the others.</summary>
        private static readonly string[] CanvasRoots = { "9_MainCanvas", "6_Effects" };

        [MenuItem(MenuPath)]
        public static void Run()
        {
            string defaultRoot = Path.GetDirectoryName(Application.dataPath) ?? "";
            string outDir = EditorUtility.SaveFolderPanel(
                "Choose output folder (the same one as the level catalog)",
                defaultRoot, "VanillaLevelArt");
            if (string.IsNullOrEmpty(outDir)) return;

            var sb = new StringBuilder();
            sb.Append("{\n");
            AppendCameras(sb);
            sb.Append(",\n");
            AppendCanvases(sb);
            sb.Append(",\n");
            AppendNavigator(sb);
            sb.Append("\n}\n");

            string path = Path.Combine(outDir, OutFileName);
            File.WriteAllText(path, sb.ToString());
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("SMSModForge — Extract Vanilla UI Spec",
                "Written:\n" + path +
                "\n\nCopy it next to vanilla_levels.json in the editor's " +
                "Resources/VanillaLevelArt folder.", "OK");
            Debug.Log("[SMSModForge] UI spec written to " + path);
        }

        // ── Cameras ──────────────────────────────────────────────────────
        //
        // Orthographic size is half the visible height in WORLD units, so it and
        // the aspect give the exact world rectangle on screen. That is what maps
        // a GameObject's authored position to a place on the preview canvas, and
        // it is the single most likely source of a positional drift.

        private static void AppendCameras(StringBuilder sb)
        {
            sb.Append("  \"cameras\": [");
            bool first = true;
            foreach (var cam in Object.FindObjectsOfType<Camera>(true))
            {
                if (cam == null) continue;
                if (!first) sb.Append(',');
                sb.Append("\n    { \"name\": ").Append(Quote(cam.name));
                sb.Append(", \"path\": ").Append(Quote(PathOf(cam.transform)));
                sb.Append(", \"tag\": ").Append(Quote(cam.tag));
                sb.Append(", \"enabled\": ").Append(cam.isActiveAndEnabled ? "true" : "false");
                sb.Append(", \"orthographic\": ").Append(cam.orthographic ? "true" : "false");
                sb.Append(", \"orthographicSize\": ").Append(F(cam.orthographicSize));
                sb.Append(", \"aspect\": ").Append(F(cam.aspect));
                sb.Append(", \"depth\": ").Append(F(cam.depth));
                sb.Append(", \"position\": ").Append(V3(cam.transform.position));
                // The world rectangle actually framed, spelled out so the editor
                // doesn't have to re-derive it.
                if (cam.orthographic)
                {
                    sb.Append(", \"worldHeight\": ").Append(F(cam.orthographicSize * 2f));
                    sb.Append(", \"worldWidth\": ").Append(F(cam.orthographicSize * 2f * cam.aspect));
                }
                sb.Append(" }");
                first = false;
            }
            sb.Append("\n  ]");
        }

        // ── Canvases ─────────────────────────────────────────────────────
        //
        // referenceResolution is the coordinate space the UI is authored in —
        // the number the preview's canvas size should equal. Guessing it from a
        // screenshot is what produced 2048x1136.

        private static void AppendCanvases(StringBuilder sb)
        {
            sb.Append("  \"canvases\": [");
            bool first = true;
            foreach (var canvas in Object.FindObjectsOfType<Canvas>(true))
            {
                if (canvas == null || !canvas.isRootCanvas) continue;
                var scaler = canvas.GetComponent<CanvasScaler>();
                var rt = canvas.transform as RectTransform;

                if (!first) sb.Append(',');
                sb.Append("\n    { \"name\": ").Append(Quote(canvas.name));
                sb.Append(", \"path\": ").Append(Quote(PathOf(canvas.transform)));
                sb.Append(", \"renderMode\": ").Append(Quote(canvas.renderMode.ToString()));
                sb.Append(", \"scaleFactor\": ").Append(F(canvas.scaleFactor));
                sb.Append(", \"referencePixelsPerUnit\": ").Append(F(canvas.referencePixelsPerUnit));
                if (rt != null)
                {
                    sb.Append(", \"rect\": [").Append(F(rt.rect.width)).Append(", ")
                      .Append(F(rt.rect.height)).Append(']');
                }
                if (scaler != null)
                {
                    sb.Append(", \"uiScaleMode\": ").Append(Quote(scaler.uiScaleMode.ToString()));
                    sb.Append(", \"referenceResolution\": [")
                      .Append(F(scaler.referenceResolution.x)).Append(", ")
                      .Append(F(scaler.referenceResolution.y)).Append(']');
                    sb.Append(", \"screenMatchMode\": ").Append(Quote(scaler.screenMatchMode.ToString()));
                    sb.Append(", \"matchWidthOrHeight\": ").Append(F(scaler.matchWidthOrHeight));
                }
                sb.Append(" }");
                first = false;
            }
            sb.Append("\n  ]");
        }

        // ── Navigator ────────────────────────────────────────────────────
        //
        // The whole map-button strip, described as a tree: every RectTransform's
        // anchors, pivot, size and position, plus the text settings on anything
        // that draws text. Button pitch, row height, label placement and font
        // size all fall out of this rather than being eyeballed.

        private static void AppendNavigator(StringBuilder sb)
        {
            sb.Append("  \"navigator\": ");
            Transform nav = null;
            foreach (var root in CanvasRoots)
            {
                var go = GameObject.Find(root) ?? FindInactiveByName(root);
                nav = go != null ? go.transform.Find("Navigator") : null;
                if (nav != null) break;
            }
            if (nav == null) { sb.Append("null"); return; }
            AppendUiNode(sb, nav, 2, 0);
        }

        private const int MaxUiDepth = 6;

        private static void AppendUiNode(StringBuilder sb, Transform t, int indent, int depth)
        {
            string pad = new string(' ', indent);
            sb.Append("{\n");
            sb.Append(pad).Append("  \"name\": ").Append(Quote(t.name)).Append(",\n");
            sb.Append(pad).Append("  \"activeSelf\": ").Append(t.gameObject.activeSelf ? "true" : "false").Append(",\n");

            if (t is RectTransform rt)
            {
                sb.Append(pad).Append("  \"anchorMin\": ").Append(V2(rt.anchorMin)).Append(",\n");
                sb.Append(pad).Append("  \"anchorMax\": ").Append(V2(rt.anchorMax)).Append(",\n");
                sb.Append(pad).Append("  \"pivot\": ").Append(V2(rt.pivot)).Append(",\n");
                sb.Append(pad).Append("  \"anchoredPosition\": ").Append(V2(rt.anchoredPosition)).Append(",\n");
                sb.Append(pad).Append("  \"sizeDelta\": ").Append(V2(rt.sizeDelta)).Append(",\n");
                sb.Append(pad).Append("  \"rect\": [").Append(F(rt.rect.width)).Append(", ")
                  .Append(F(rt.rect.height)).Append("],\n");
                sb.Append(pad).Append("  \"localScale\": ").Append(V3(rt.localScale)).Append(",\n");
            }

            AppendTextIfAny(sb, t, pad);
            AppendImageIfAny(sb, t, pad);
            AppendLayoutIfAny(sb, t, pad);

            sb.Append(pad).Append("  \"components\": [");
            bool firstC = true;
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                if (!firstC) sb.Append(", ");
                sb.Append(Quote(c.GetType().Name));
                firstC = false;
            }
            sb.Append("],\n");

            sb.Append(pad).Append("  \"children\": [");
            if (depth < MaxUiDepth && t.childCount > 0)
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('\n').Append(pad).Append("    ");
                    AppendUiNode(sb, t.GetChild(i), indent + 4, depth + 1);
                }
                sb.Append('\n').Append(pad).Append("  ");
            }
            sb.Append("]\n");
            sb.Append(pad).Append('}');
        }

        /// <summary>Text settings, for whichever text component the project uses.
        /// TextMeshPro is reached by reflection so this script compiles whether
        /// or not TMP is referenced.</summary>
        private static void AppendTextIfAny(StringBuilder sb, Transform t, string pad)
        {
            var uiText = t.GetComponent<Text>();
            if (uiText != null)
            {
                sb.Append(pad).Append("  \"text\": { \"kind\": \"UI.Text\"");
                sb.Append(", \"value\": ").Append(Quote(uiText.text));
                sb.Append(", \"font\": ").Append(Quote(uiText.font != null ? uiText.font.name : ""));
                sb.Append(", \"fontSize\": ").Append(uiText.fontSize.ToString(CultureInfo.InvariantCulture));
                sb.Append(", \"alignment\": ").Append(Quote(uiText.alignment.ToString()));
                sb.Append(", \"color\": ").Append(Quote(ColorUtility.ToHtmlStringRGBA(uiText.color)));
                sb.Append(" },\n");
                return;
            }

            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (tn != "TextMeshProUGUI" && tn != "TextMeshPro") continue;

                var ct = c.GetType();
                sb.Append(pad).Append("  \"text\": { \"kind\": ").Append(Quote(tn));
                sb.Append(", \"value\": ").Append(Quote(GetStr(c, ct, "text")));
                sb.Append(", \"font\": ").Append(Quote(GetObjName(c, ct, "font")));
                sb.Append(", \"fontSize\": ").Append(Quote(GetStr(c, ct, "fontSize")));
                sb.Append(", \"fontStyle\": ").Append(Quote(GetStr(c, ct, "fontStyle")));
                sb.Append(", \"alignment\": ").Append(Quote(GetStr(c, ct, "alignment")));
                sb.Append(", \"characterSpacing\": ").Append(Quote(GetStr(c, ct, "characterSpacing")));
                sb.Append(", \"lineSpacing\": ").Append(Quote(GetStr(c, ct, "lineSpacing")));
                sb.Append(", \"enableWordWrapping\": ").Append(Quote(GetStr(c, ct, "enableWordWrapping")));
                sb.Append(", \"margin\": ").Append(Quote(GetStr(c, ct, "margin")));
                sb.Append(", \"color\": ").Append(Quote(GetColorHex(c, ct, "color")));
                sb.Append(" },\n");
                return;
            }
        }

        private static void AppendImageIfAny(StringBuilder sb, Transform t, string pad)
        {
            var img = t.GetComponent<Image>();
            if (img == null) return;
            sb.Append(pad).Append("  \"image\": { \"sprite\": ")
              .Append(Quote(img.sprite != null ? img.sprite.name : ""));
            if (img.sprite != null)
            {
                sb.Append(", \"spritePixelSize\": [")
                  .Append(Mathf.RoundToInt(img.sprite.rect.width)).Append(", ")
                  .Append(Mathf.RoundToInt(img.sprite.rect.height)).Append(']');
                sb.Append(", \"pixelsPerUnit\": ").Append(F(img.sprite.pixelsPerUnit));
            }
            sb.Append(", \"color\": ").Append(Quote(ColorUtility.ToHtmlStringRGBA(img.color)));
            sb.Append(", \"type\": ").Append(Quote(img.type.ToString()));
            sb.Append(" },\n");
        }

        /// <summary>Grid / layout-group settings — the authoritative source for
        /// button pitch and column count, which the preview currently hard-codes.</summary>
        private static void AppendLayoutIfAny(StringBuilder sb, Transform t, string pad)
        {
            var grid = t.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                sb.Append(pad).Append("  \"gridLayout\": { \"cellSize\": ").Append(V2(grid.cellSize));
                sb.Append(", \"spacing\": ").Append(V2(grid.spacing));
                sb.Append(", \"startCorner\": ").Append(Quote(grid.startCorner.ToString()));
                sb.Append(", \"startAxis\": ").Append(Quote(grid.startAxis.ToString()));
                sb.Append(", \"childAlignment\": ").Append(Quote(grid.childAlignment.ToString()));
                sb.Append(", \"constraint\": ").Append(Quote(grid.constraint.ToString()));
                sb.Append(", \"constraintCount\": ").Append(grid.constraintCount.ToString(CultureInfo.InvariantCulture));
                sb.Append(", \"padding\": [").Append(grid.padding.left).Append(", ").Append(grid.padding.right)
                  .Append(", ").Append(grid.padding.top).Append(", ").Append(grid.padding.bottom).Append(']');
                sb.Append(" },\n");
                return;
            }

            var hv = t.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (hv != null)
            {
                sb.Append(pad).Append("  \"layoutGroup\": { \"kind\": ").Append(Quote(hv.GetType().Name));
                sb.Append(", \"spacing\": ").Append(F(hv.spacing));
                sb.Append(", \"childAlignment\": ").Append(Quote(hv.childAlignment.ToString()));
                sb.Append(", \"padding\": [").Append(hv.padding.left).Append(", ").Append(hv.padding.right)
                  .Append(", ").Append(hv.padding.top).Append(", ").Append(hv.padding.bottom).Append(']');
                sb.Append(" },\n");
            }
        }

        // ── Reflection helpers (TMP without a hard reference) ────────────

        private static string GetStr(object o, System.Type t, string prop)
        {
            var p = t.GetProperty(prop);
            if (p == null) return "";
            try
            {
                var v = p.GetValue(o, null);
                if (v is float f) return F(f);
                if (v is Vector4 v4) return "[" + F(v4.x) + ", " + F(v4.y) + ", " + F(v4.z) + ", " + F(v4.w) + "]";
                return v != null ? v.ToString() : "";
            }
            catch { return ""; }
        }

        private static string GetObjName(object o, System.Type t, string prop)
        {
            var p = t.GetProperty(prop);
            if (p == null) return "";
            try { return p.GetValue(o, null) is Object u && u != null ? u.name : ""; }
            catch { return ""; }
        }

        private static string GetColorHex(object o, System.Type t, string prop)
        {
            var p = t.GetProperty(prop);
            if (p == null) return "";
            try { return p.GetValue(o, null) is Color c ? ColorUtility.ToHtmlStringRGBA(c) : ""; }
            catch { return ""; }
        }

        // ── Small helpers ────────────────────────────────────────────────

        private static GameObject FindInactiveByName(string name)
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == name && go.hideFlags == HideFlags.None &&
                    go.scene.IsValid())
                    return go;
            return null;
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c != null; c = c.parent) parts.Insert(0, c.name);
            return string.Join("/", parts);
        }

        private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        private static string V2(Vector2 v) => "[" + F(v.x) + ", " + F(v.y) + "]";
        private static string V3(Vector3 v) => "[" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + "]";

        private static string Quote(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

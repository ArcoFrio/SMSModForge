// SMSModForge — vanilla UI surface extractor  (Unity Editor script)
//
// WHAT THIS IS FOR
//   The editor's preview has to show what the game shows. Not approximately:
//   a preview that is close is worse than one that is obviously wrong, because
//   it reads as trustworthy while positions drift and there is no way to tell a
//   mis-set constant from a mis-authored object.
//
//   So nothing here is measured by eye. This walks every Canvas in the open
//   scene and writes out what each object actually is - its resolved rectangle,
//   its sprite and that sprite's nine-slice border, its tint, its text settings,
//   its effects - together with the sprite images themselves, deduplicated.
//
// WHAT IT WRITES
//   <out>/index.json            every surface, its canvas and scaler settings
//   <out>/fonts.json            every font asset referenced, and by what
//   <out>/report.txt            self-checks, warnings, and what could not be read
//   <out>/Surfaces/<name>.json  one file per surface: the full object tree
//   <out>/Sprites/<key>.png     each distinct sprite crop, written once
//   <out>/Sprites/index.json    key -> border, rect, pixels-per-unit, source
//
//   One file per surface rather than one enormous one: they are loaded on
//   demand, they diff readably, and a single unreadable surface does not cost
//   the other fifty-four.
//
// BEFORE YOU RUN IT
//   Set the Game view to 1920x1080. With a CanvasScaler in ScaleWithScreenSize
//   mode a canvas's real rect is whatever the Game view is, NOT the reference
//   resolution - so resolved geometry silently depends on a window size. This
//   checks the two against each other and says so in the report when they
//   disagree, but it cannot fix it for you.
//
// TWO MENU ITEMS, AND THE DIFFERENCE BETWEEN THEM
//   Tools > SMSModForge > Extract Vanilla UI Surfaces
//       Reads the scene and touches nothing. Objects that are switched off have
//       never been through Unity's layout system, so anything of theirs that a
//       LayoutGroup or ContentSizeFitter would drive is marked "unverified"
//       rather than reported as fact.
//
//   Tools > SMSModForge > Extract Vanilla UI Surfaces (resolve dormant)
//       Additionally switches each dormant surface on, forces a layout rebuild,
//       reads it, and switches it back. That yields real geometry for the
//       thirty-five surfaces that are off at load - at the price of running
//       every OnEnable on the way, which is game code that may do anything.
//       The scene will be left marked as modified. Do not save it.
//
// HOW THE NUMBERS ARE CHECKED
//   A silent no-op reads exactly like success, so the run ends with two
//   controls whose results go in the report:
//     * A LIVE surface is read twice, once directly and once after a forced
//       rebuild. Those MUST agree. If a rebuild moves an already-correct
//       surface, either the rebuild is destructive or the direct read was
//       wrong, and both are worth knowing before trusting any of this.
//     * A DORMANT surface with layout groups is read before and after being
//       resolved. At least one node MUST differ. If nothing moves, the rebuild
//       did nothing and "unverified" is the honest label for the whole run.
//   A run where both controls pass is a run whose geometry can be relied on.
//   A run where either fails says so at the top of the report.
//
// NOTES
//   * TextMeshPro is reached by reflection, so this compiles whether or not the
//     project references TMP.
//   * TMP's outline and underlay live on the MATERIAL, not the component.
//     Extracting only the component drops them silently, so materials are read
//     too.
//   * Fonts are NOT exported - the font files are supplied separately. What is
//     written is the list of which font each text object asks for, so the list
//     of files needed is a fact rather than a guess.
//   * Supersedes SMSModForgeUiExtractor, which measured cameras, root canvases
//     and the navigator only. That one is kept because the constants presently
//     in PlacePreview came out of it and deleting it would strand their
//     provenance.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SMSModForge.EditorTools
{
    public static class SMSModForgeUiSurfaceExtractor
    {
        private const string MenuPlain =
            "Tools/SMSModForge/Extract Vanilla UI Surfaces…";
        private const string MenuResolve =
            "Tools/SMSModForge/Extract Vanilla UI Surfaces (resolve dormant)…";

        [MenuItem(MenuPlain)]
        public static void RunPlain() => Run(resolveDormant: false);

        [MenuItem(MenuResolve)]
        public static void RunResolving()
        {
            bool go = EditorUtility.DisplayDialog(
                "SMSModForge — resolve dormant surfaces",
                "This switches each dormant canvas on, forces a layout rebuild, " +
                "reads it, and switches it back.\n\n" +
                "Switching an object on runs its OnEnable, and that is game code " +
                "which may do anything - spawn objects, write files, change " +
                "state. The scene will be left marked as modified.\n\n" +
                "Do not save the scene afterwards.",
                "Run it", "Cancel");
            if (go) Run(resolveDormant: true);
        }

        // ── Entry point ──────────────────────────────────────────────────

        private static void Run(bool resolveDormant)
        {
            string defaultRoot = Path.GetDirectoryName(Application.dataPath) ?? "";
            string outDir = EditorUtility.SaveFolderPanel(
                "Choose output folder", defaultRoot, "VanillaUi");
            if (string.IsNullOrEmpty(outDir)) return;

            Directory.CreateDirectory(Path.Combine(outDir, "Surfaces"));
            Directory.CreateDirectory(Path.Combine(outDir, "Sprites"));

            var log = new Report();
            var sprites = new SpriteLibrary(Path.Combine(outDir, "Sprites"), log);
            var fonts = new FontLedger();

            var surfaces = FindSurfaces();
            log.Line("Surfaces found: " + surfaces.Count);

            // What space the numbers are in, and whether any canvas is
            // incapable of producing them at all.
            DescribeCanvasSpace(surfaces, log);

            var index = new Json();
            index.Object();
            index.Key("generatedUtc").Value(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            index.Key("resolvedDormant").Value(resolveDormant);
            index.Key("surfaces").Array();

            try
            {
                for (int i = 0; i < surfaces.Count; i++)
                {
                    var canvas = surfaces[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Extracting UI surfaces",
                            canvas.name + "  (" + (i + 1) + " of " + surfaces.Count + ")",
                            (i + 1) / (float)surfaces.Count))
                    {
                        log.Warn("Cancelled by the user after " + i + " surfaces.");
                        break;
                    }

                    WriteSurface(canvas, outDir, sprites, fonts, log, resolveDormant, index);
                }

                index.EndArray();
                index.EndObject();

                RunControls(surfaces, log);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            File.WriteAllText(Path.Combine(outDir, "index.json"), index.ToString());
            File.WriteAllText(Path.Combine(outDir, "fonts.json"), fonts.ToJson());
            File.WriteAllText(Path.Combine(outDir, "Sprites", "index.json"), sprites.ToJson());
            File.WriteAllText(Path.Combine(outDir, "report.txt"), log.ToString());
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("SMSModForge — Extract Vanilla UI Surfaces",
                surfaces.Count + " surfaces, " + sprites.Count + " distinct sprites, " +
                fonts.Count + " fonts referenced.\n\n" +
                (log.Problems == 0
                    ? "No problems reported."
                    : log.Problems + " problem(s) - read report.txt before trusting this.") +
                "\n\nWritten to:\n" + outDir,
                "OK");
            Debug.Log("[SMSModForge] UI surfaces written to " + outDir);
        }

        // ── Finding the surfaces ─────────────────────────────────────────

        /// <summary>Every canvas with no canvas above it.
        /// <para/>
        /// Computed by walking parents rather than by asking Canvas.isRootCanvas,
        /// so this and the offline census (Tools/Ui/CanvasCensus.py) mean the
        /// same thing by "surface" and their counts can be compared. If the two
        /// disagree, one of them is wrong and it is worth finding out which.</summary>
        private static List<Canvas> FindSurfaces()
        {
            var all = Object.FindObjectsOfType<Canvas>(true)
                            .Where(c => c != null && c.gameObject.scene.IsValid())
                            .ToList();
            var found = new List<Canvas>();
            foreach (var canvas in all)
            {
                bool nested = false;
                for (var p = canvas.transform.parent; p != null; p = p.parent)
                    if (p.GetComponent<Canvas>() != null) { nested = true; break; }
                if (!nested) found.Add(canvas);
            }
            return found.OrderBy(c => PathOf(c.transform), StringComparer.Ordinal).ToList();
        }

        /// <summary>Describe the space every resolved rectangle is expressed in,
        /// and complain only about what is actually wrong.
        /// <para/>
        /// This used to compare each canvas's rect against its reference
        /// resolution and call any difference an error. That was wrong twice. It
        /// bailed after the first canvas, so a single outlier was reported as if
        /// it were universal - and the premise was false anyway: with
        /// matchWidthOrHeight 0 the canvas locks its WIDTH to the reference and
        /// takes its height from the aspect, so a canvas 1920 wide and 1406 tall
        /// is not a mistake, it is what a player at that aspect actually gets.
        /// <para/>
        /// What IS worth stopping for is a canvas whose rect is zero. Everything
        /// under one of those resolves to a point, and a file full of zeroes reads
        /// as data rather than as the absence of it.</summary>
        private static void DescribeCanvasSpace(List<Canvas> surfaces, Report log)
        {
            log.Line("Game view: " + Screen.width + "x" + Screen.height);

            var references = new Dictionary<string, int>();
            var dead = new List<string>();

            foreach (var canvas in surfaces)
            {
                var rt = canvas.transform as RectTransform;
                if (rt == null) continue;

                if (rt.rect.width <= 0f || rt.rect.height <= 0f)
                {
                    dead.Add(PathOf(canvas.transform) +
                             (canvas.enabled ? "" : "  (Canvas component disabled)"));
                    continue;
                }

                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler == null ||
                    scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;

                string key = F(scaler.referenceResolution.x) + "x" +
                             F(scaler.referenceResolution.y) +
                             " (match " + F(scaler.matchWidthOrHeight) + ") -> rect " +
                             F(rt.rect.width) + "x" + F(rt.rect.height);
                references.TryGetValue(key, out int n);
                references[key] = n + 1;
            }

            foreach (var pair in references.OrderByDescending(kv => kv.Value))
                log.Line("  " + pair.Value + " canvas(es): reference " + pair.Key);

            if (dead.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append(dead.Count).Append(" canvas(es) have a zero-size rect, so every ")
                  .Append("rectangle under them resolved to a point. Their geometry in ")
                  .Append("this run is unusable - not wrong, absent:\n");
                foreach (var d in dead) sb.Append("         ").Append(d).Append('\n');
                sb.Append("         A Canvas whose component is disabled is never driven ")
                  .Append("by Unity, so its RectTransform stays at zero. Enable it, or ")
                  .Append("accept that these surfaces cannot be measured this way.");
                log.Warn(sb.ToString());
            }
        }

        // ── One surface ──────────────────────────────────────────────────

        private static void WriteSurface(Canvas canvas, string outDir,
                                         SpriteLibrary sprites, FontLedger fonts,
                                         Report log, bool resolveDormant, Json index)
        {
            string path = PathOf(canvas.transform);
            bool live = canvas.gameObject.activeInHierarchy;
            var restore = (ActivationScope)null;

            // Before anything is switched on. Resolving a dormant surface makes
            // its whole subtree read as active, so asking the GameObject later
            // returns the state this extractor created rather than the one the
            // game loads with - and a field that quietly means something else is
            // worse than a missing one.
            var atLoad = new Dictionary<Transform, bool>();
            foreach (var tr in canvas.GetComponentsInChildren<Transform>(true))
                atLoad[tr] = tr.gameObject.activeInHierarchy;

            try
            {
                if (!live && resolveDormant)
                {
                    restore = ActivationScope.Open(canvas.gameObject);
                    ForceLayout(canvas);
                }
                else if (live)
                {
                    ForceLayout(canvas);
                }

                var json = new Json();
                json.Object();
                json.Key("path").Value(path);
                json.Key("scene").Value(canvas.gameObject.scene.name);
                json.Key("liveAtLoad").Value(live);
                json.Key("resolved").Value(live || restore != null);
                WriteCanvasSettings(json, canvas);
                json.Key("root");
                WriteNode(json, canvas.transform, canvas, sprites, fonts, log,
                          resolvedHere: live || restore != null, atLoad: atLoad);
                json.EndObject();

                string file = SafeFileName(path) + ".json";
                File.WriteAllText(Path.Combine(outDir, "Surfaces", file), json.ToString());

                index.Object();
                index.Key("path").Value(path);
                index.Key("scene").Value(canvas.gameObject.scene.name);
                index.Key("file").Value("Surfaces/" + file);
                index.Key("liveAtLoad").Value(live);
                index.Key("resolved").Value(live || restore != null);
                index.Key("objects").Value(CountBelow(canvas.transform));
                var crt = canvas.transform as RectTransform;
                index.Key("usable").Value(crt != null && crt.rect.width > 0f && crt.rect.height > 0f);
                index.EndObject();
            }
            catch (Exception ex)
            {
                log.Problem("Surface '" + path + "' failed: " + ex.Message);
            }
            finally
            {
                if (restore != null) restore.Close();
            }
        }

        private static void WriteCanvasSettings(Json json, Canvas canvas)
        {
            var rt = canvas.transform as RectTransform;
            json.Key("canvas").Object();
            // Not the same question as activeSelf: a live GameObject with a
            // disabled Canvas draws nothing and is never given a size.
            json.Key("enabled").Value(canvas.enabled);
            json.Key("renderMode").Value(canvas.renderMode.ToString());
            json.Key("sortingOrder").Value(canvas.sortingOrder);
            json.Key("sortingLayer").Value(canvas.sortingLayerName);
            json.Key("overrideSorting").Value(canvas.overrideSorting);
            json.Key("scaleFactor").Value(canvas.scaleFactor);
            json.Key("referencePixelsPerUnit").Value(canvas.referencePixelsPerUnit);
            if (rt != null) json.Key("rect").Vector2(rt.rect.size);
            json.EndObject();

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                json.Key("scaler").Object();
                json.Key("uiScaleMode").Value(scaler.uiScaleMode.ToString());
                json.Key("referenceResolution").Vector2(scaler.referenceResolution);
                json.Key("screenMatchMode").Value(scaler.screenMatchMode.ToString());
                json.Key("matchWidthOrHeight").Value(scaler.matchWidthOrHeight);
                json.Key("referencePixelsPerUnit").Value(scaler.referencePixelsPerUnit);
                json.EndObject();
            }
        }

        // ── One object ───────────────────────────────────────────────────

        private static void WriteNode(Json json, Transform t, Canvas canvas,
                                      SpriteLibrary sprites, FontLedger fonts,
                                      Report log, bool resolvedHere,
                                      Dictionary<Transform, bool> atLoad)
        {
            bool liveAtLoad = atLoad.TryGetValue(t, out bool known)
                ? known : t.gameObject.activeInHierarchy;
            json.Object();
            json.Key("name").Value(t.name);
            json.Key("siblingIndex").Value(t.GetSiblingIndex());
            json.Key("activeSelf").Value(t.gameObject.activeSelf);
            json.Key("activeInHierarchy").Value(liveAtLoad);

            WriteRect(json, t, canvas, resolvedHere);

            json.Key("components").Array();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                json.Value(c.GetType().Name);
            }
            json.EndArray();

            WriteGraphics(json, t, sprites, fonts, log);
            WriteEffects(json, t, isSurfaceRoot: t == canvas.transform);
            WriteLayout(json, t);

            json.Key("children").Array();
            for (int i = 0; i < t.childCount; i++)
                WriteNode(json, t.GetChild(i), canvas, sprites, fonts, log,
                          resolvedHere, atLoad);
            json.EndArray();

            json.EndObject();
        }

        /// <summary>Geometry, both as authored and as resolved.
        /// <para/>
        /// Both are written because they answer different questions. The authored
        /// anchors are what a pack would set to make a new object; the resolved
        /// rectangle is where the object actually is, which for anything inside a
        /// LayoutGroup is a number Unity computed and the authored one does not
        /// predict. The preview needs the second to draw vanilla correctly and
        /// the first to author anything new.</summary>
        private static void WriteRect(Json json, Transform t, Canvas canvas,
                                      bool resolvedHere)
        {
            var rt = t as RectTransform;
            if (rt == null)
            {
                json.Key("transform").Object();
                json.Key("localPosition").Vector3(t.localPosition);
                json.Key("localScale").Vector3(t.localScale);
                json.Key("localEuler").Vector3(t.localEulerAngles);
                json.EndObject();
                return;
            }

            json.Key("rect").Object();
            json.Key("anchorMin").Vector2(rt.anchorMin);
            json.Key("anchorMax").Vector2(rt.anchorMax);
            json.Key("pivot").Vector2(rt.pivot);
            json.Key("anchoredPosition").Vector2(rt.anchoredPosition);
            json.Key("sizeDelta").Vector2(rt.sizeDelta);
            json.Key("offsetMin").Vector2(rt.offsetMin);
            json.Key("offsetMax").Vector2(rt.offsetMax);
            json.Key("localScale").Vector3(rt.localScale);
            json.Key("localEuler").Vector3(rt.localEulerAngles);
            json.Key("size").Vector2(rt.rect.size);

            // Resolved, in the canvas's own coordinates: the four world corners
            // pulled back through the canvas transform. That is the space the
            // reference resolution is expressed in, so these numbers are directly
            // comparable to the 1920x1080 the preview composes at.
            var canvasRt = canvas.transform as RectTransform;
            if (canvasRt != null)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                var toCanvas = canvasRt.worldToLocalMatrix;
                Vector3 bl = toCanvas.MultiplyPoint3x4(corners[0]);
                Vector3 tr = toCanvas.MultiplyPoint3x4(corners[2]);

                json.Key("resolved").Object();
                json.Key("min").Vector2(new Vector2(bl.x, bl.y));
                json.Key("max").Vector2(new Vector2(tr.x, tr.y));
                json.Key("size").Vector2(new Vector2(tr.x - bl.x, tr.y - bl.y));
                // Read live, NOT from the at-load snapshot: see TrustOf.
                json.Key("trust").Value(
                    TrustOf(rt, resolvedHere, rt.gameObject.activeInHierarchy));
                json.EndObject();
            }
            json.EndObject();
        }

        /// <summary>How much the resolved rectangle is worth.
        /// <para/>
        /// "direct" - anchors and sizeDelta fully determine it, so it is right
        /// whether or not this object has ever been switched on.
        /// "rebuilt" - driven by a layout group or fitter that has just run.
        /// "unverified" - driven, but not by anything that ran. Do not draw it.
        /// <para/>
        /// The inactive case is the one worth spelling out. Unity's layout groups
        /// filter their children by activeInHierarchy before they position any of
        /// them, so a switched-off child is not laid out at all: its rectangle is
        /// the authored one, sitting untouched. That is emphatically NOT where the
        /// game will draw it - the moment the game switches it on, the group
        /// places it somewhere else. In this scene 1728 of the 1864 children of
        /// layout groups are dark even with their surface switched on, so calling
        /// those "rebuilt" would have been a confident claim about the wrong
        /// number in almost every case.
        /// <para/>
        /// Resolving them would mean switching on each one individually, deep
        /// inside the tree, running whatever OnEnable the game has put there.
        /// That is a bigger intrusion than this tool should make by itself.
        /// <para/>
        /// The state that decides this is whether layout ran WHILE THE RECTANGLE
        /// WAS BEING READ - not whether the object is on when the game loads.
        /// Those are different questions, and the answers differ for 129 objects
        /// here: everything inside a dormant surface this tool switched on was
        /// genuinely laid out and its rectangle is real, even though the player
        /// does not see it until later. Using the at-load answer marked all 129
        /// unverified; using neither marked 1728 rebuilt. Both facts are in the
        /// JSON - activeInHierarchy is the at-load one - so a reader can tell
        /// "measured, shown later" from "never measured".</summary>
        private static string TrustOf(RectTransform rt, bool resolvedHere, bool activeWhenRead)
        {
            // Explicitly opted out of layout: the group skips it, so the authored
            // rectangle is the true one.
            var element = rt.GetComponent<LayoutElement>();
            if (element != null && element.ignoreLayout) return "direct";

            bool driven = rt.GetComponent<ContentSizeFitter>() != null ||
                          (rt.parent != null && rt.parent.GetComponent<LayoutGroup>() != null);
            if (!driven) return "direct";

            if (!activeWhenRead) return "unverified";
            return resolvedHere ? "rebuilt" : "unverified";
        }

        // ── Things that draw ─────────────────────────────────────────────

        private static void WriteGraphics(Json json, Transform t, SpriteLibrary sprites,
                                          FontLedger fonts, Report log)
        {
            var image = t.GetComponent<Image>();
            if (image != null)
            {
                json.Key("image").Object();
                json.Key("enabled").Value(image.enabled);
                json.Key("color").Value(Hex(image.color));
                json.Key("type").Value(image.type.ToString());
                json.Key("fillCenter").Value(image.fillCenter);
                json.Key("preserveAspect").Value(image.preserveAspect);
                Optional(json, image, "pixelsPerUnitMultiplier");
                json.Key("raycastTarget").Value(image.raycastTarget);
                json.Key("material").Value(image.material != null ? image.material.name : "");
                if (image.type == Image.Type.Filled)
                {
                    json.Key("fillMethod").Value(image.fillMethod.ToString());
                    json.Key("fillAmount").Value(image.fillAmount);
                    json.Key("fillOrigin").Value(image.fillOrigin);
                    json.Key("fillClockwise").Value(image.fillClockwise);
                }
                if (image.sprite != null)
                {
                    var sprite = image.sprite;
                    log.NoteSlice(image.type, sprite.border, PathOf(t), sprite.name);
                    json.Key("sprite").Value(sprite.name);
                    // The nine-slice, which is the whole point. Unity orders it
                    // left, bottom, right, top.
                    json.Key("border").Vector4(sprite.border);
                    json.Key("spritePixelsPerUnit").Value(sprite.pixelsPerUnit);
                    json.Key("spriteSize").Vector2(sprite.rect.size);
                    json.Key("spritePivot").Vector2(sprite.pivot);
                    json.Key("spriteKey").Value(sprites.Add(sprite));
                }
                else
                {
                    json.Key("sprite").Value("");
                }
                json.EndObject();
            }

            var raw = t.GetComponent<RawImage>();
            if (raw != null)
            {
                json.Key("rawImage").Object();
                json.Key("enabled").Value(raw.enabled);
                json.Key("texture").Value(raw.texture != null ? raw.texture.name : "");
                json.Key("uvRect").Vector4(new Vector4(
                    raw.uvRect.x, raw.uvRect.y, raw.uvRect.width, raw.uvRect.height));
                json.Key("color").Value(Hex(raw.color));
                json.EndObject();
            }

            var text = t.GetComponent<Text>();
            if (text != null)
            {
                fonts.Note(text.font != null ? text.font.name : "(none)", "UI.Text", t);
                json.Key("text").Object();
                json.Key("enabled").Value(text.enabled);
                json.Key("kind").Value("UI.Text");
                json.Key("value").Value(text.text);
                json.Key("font").Value(text.font != null ? text.font.name : "");
                json.Key("fontSize").Value(text.fontSize);
                json.Key("fontStyle").Value(text.fontStyle.ToString());
                json.Key("alignment").Value(text.alignment.ToString());
                json.Key("color").Value(Hex(text.color));
                json.Key("lineSpacing").Value(text.lineSpacing);
                json.Key("richText").Value(text.supportRichText);
                json.Key("horizontalOverflow").Value(text.horizontalOverflow.ToString());
                json.Key("verticalOverflow").Value(text.verticalOverflow.ToString());
                json.Key("bestFit").Value(text.resizeTextForBestFit);
                json.Key("bestFitMin").Value(text.resizeTextMinSize);
                json.Key("bestFitMax").Value(text.resizeTextMaxSize);
                json.EndObject();
            }

            WriteTmpIfAny(json, t, fonts);
        }

        /// <summary>TextMeshPro, through reflection so this file compiles with or
        /// without a TMP reference.
        /// <para/>
        /// The material half matters as much as the component half: TMP's outline
        /// and its underlay - what everything else calls a drop shadow - are
        /// shader properties, so a reader that took only the component would drop
        /// both without noticing.</summary>
        private static void WriteTmpIfAny(Json json, Transform t, FontLedger fonts)
        {
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                string kind = c.GetType().Name;
                if (kind != "TextMeshProUGUI" && kind != "TextMeshPro") continue;

                var type = c.GetType();
                string fontName = ObjName(c, type, "font");
                fonts.Note(fontName.Length > 0 ? fontName : "(none)", kind, t);

                json.Key("text").Object();
                json.Key("enabled").Value(c is Behaviour b && b.enabled);
                json.Key("kind").Value(kind);
                json.Key("value").Value(Str(c, type, "text"));
                json.Key("font").Value(fontName);
                json.Key("sourceFontFile").Value(SourceFontOf(c, type));
                json.Key("fontSize").Value(Str(c, type, "fontSize"));
                json.Key("autoSize").Value(Str(c, type, "enableAutoSizing"));
                json.Key("fontSizeMin").Value(Str(c, type, "fontSizeMin"));
                json.Key("fontSizeMax").Value(Str(c, type, "fontSizeMax"));
                json.Key("fontStyle").Value(Str(c, type, "fontStyle"));
                json.Key("fontWeight").Value(Str(c, type, "fontWeight"));
                json.Key("alignment").Value(Str(c, type, "alignment"));
                json.Key("color").Value(ColorHex(c, type, "color"));
                json.Key("characterSpacing").Value(Str(c, type, "characterSpacing"));
                json.Key("wordSpacing").Value(Str(c, type, "wordSpacing"));
                json.Key("lineSpacing").Value(Str(c, type, "lineSpacing"));
                json.Key("paragraphSpacing").Value(Str(c, type, "paragraphSpacing"));
                json.Key("margin").Value(Str(c, type, "margin"));
                json.Key("wordWrapping").Value(Str(c, type, "enableWordWrapping"));
                json.Key("overflowMode").Value(Str(c, type, "overflowMode"));
                json.Key("richText").Value(Str(c, type, "richText"));

                var gradient = Prop(c, type, "enableVertexGradient");
                if (gradient is bool on && on)
                {
                    json.Key("gradient").Object();
                    var grad = Prop(c, type, "colorGradient");
                    if (grad != null)
                    {
                        var gt = grad.GetType();
                        json.Key("topLeft").Value(ColorHex(grad, gt, "topLeft"));
                        json.Key("topRight").Value(ColorHex(grad, gt, "topRight"));
                        json.Key("bottomLeft").Value(ColorHex(grad, gt, "bottomLeft"));
                        json.Key("bottomRight").Value(ColorHex(grad, gt, "bottomRight"));
                    }
                    json.EndObject();
                }

                WriteTmpMaterial(json, Prop(c, type, "fontSharedMaterial") as Material);
                json.EndObject();
                return;
            }
        }

        /// <summary>The shader properties that decide whether TMP text has an
        /// outline or a shadow, and how thick.</summary>
        private static void WriteTmpMaterial(Json json, Material material)
        {
            if (material == null) return;
            json.Key("material").Object();
            json.Key("name").Value(material.name);
            json.Key("shader").Value(material.shader != null ? material.shader.name : "");

            var floats = new[]
            {
                "_OutlineWidth", "_OutlineSoftness", "_FaceDilate",
                "_UnderlayOffsetX", "_UnderlayOffsetY", "_UnderlayDilate",
                "_UnderlaySoftness", "_GlowPower", "_GlowOuter", "_GlowInner",
            };
            foreach (var name in floats)
                if (material.HasProperty(name))
                    json.Key(name.TrimStart('_')).Value(material.GetFloat(name));

            var colors = new[] { "_FaceColor", "_OutlineColor", "_UnderlayColor", "_GlowColor" };
            foreach (var name in colors)
                if (material.HasProperty(name))
                    json.Key(name.TrimStart('_')).Value(Hex(material.GetColor(name)));

            json.EndObject();
        }

        /// <summary>The TTF or OTF a TMP font asset was built from, when the asset
        /// still points at one. Written so the list of font files to supply is a
        /// fact rather than a guess - a name here is a file that is needed.</summary>
        private static string SourceFontOf(object tmp, Type type)
        {
            var font = Prop(tmp, type, "font");
            if (font == null) return "";
            var fontType = font.GetType();

            var source = Prop(font, fontType, "sourceFontFile") as Object;
            if (source != null) return source.name;

            // Fall back to the face the atlas was built from, which names the
            // family even when the source asset reference is gone.
            var face = Prop(font, fontType, "faceInfo");
            if (face != null)
            {
                var ft = face.GetType();
                string family = Str(face, ft, "familyName");
                string style = Str(face, ft, "styleName");
                if (family.Length > 0)
                    return style.Length > 0 ? family + " " + style : family;
            }
            return "";
        }

        // ── Effects, masks, layout ───────────────────────────────────────

        /// <summary>Shadow and Outline duplicate the graphic's mesh at an offset.
        /// There are roughly a thousand of them in the vanilla UI, so a preview
        /// that skipped them would be wrong on most of what it draws.</summary>
        private static void WriteEffects(Json json, Transform t, bool isSurfaceRoot)
        {
            // Outline derives from Shadow, so it has to be tested first or every
            // outline is recorded as a plain shadow.
            var outline = t.GetComponent<Outline>();
            if (outline != null)
            {
                json.Key("outline").Object();
                json.Key("color").Value(Hex(outline.effectColor));
                json.Key("distance").Vector2(outline.effectDistance);
                json.Key("useGraphicAlpha").Value(outline.useGraphicAlpha);
                json.EndObject();
            }
            else
            {
                var shadow = t.GetComponent<Shadow>();
                if (shadow != null)
                {
                    json.Key("shadow").Object();
                    json.Key("color").Value(Hex(shadow.effectColor));
                    json.Key("distance").Vector2(shadow.effectDistance);
                    json.Key("useGraphicAlpha").Value(shadow.useGraphicAlpha);
                    json.EndObject();
                }
            }

            var group = t.GetComponent<CanvasGroup>();
            if (group != null)
            {
                json.Key("canvasGroup").Object();
                json.Key("alpha").Value(group.alpha);
                json.Key("interactable").Value(group.interactable);
                json.Key("blocksRaycasts").Value(group.blocksRaycasts);
                json.Key("ignoreParentGroups").Value(group.ignoreParentGroups);
                json.EndObject();
            }

            var mask = t.GetComponent<Mask>();
            if (mask != null)
            {
                json.Key("mask").Object();
                json.Key("showMaskGraphic").Value(mask.showMaskGraphic);
                json.EndObject();
            }

            var rectMask = t.GetComponent<RectMask2D>();
            if (rectMask != null)
            {
                json.Key("rectMask").Object();
                json.Key("padding").Vector4(rectMask.padding);
                json.EndObject();
            }

            // A nested canvas re-sorts everything under it, so its order has to
            // travel with the tree or the draw order is wrong below this point.
            var nested = t.GetComponent<Canvas>();
            if (nested != null && !isSurfaceRoot)
            {
                json.Key("nestedCanvas").Object();
                json.Key("overrideSorting").Value(nested.overrideSorting);
                json.Key("sortingOrder").Value(nested.sortingOrder);
                json.EndObject();
            }
        }

        private static void WriteLayout(Json json, Transform t)
        {
            var grid = t.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                json.Key("gridLayout").Object();
                json.Key("cellSize").Vector2(grid.cellSize);
                json.Key("spacing").Vector2(grid.spacing);
                json.Key("startCorner").Value(grid.startCorner.ToString());
                json.Key("startAxis").Value(grid.startAxis.ToString());
                json.Key("childAlignment").Value(grid.childAlignment.ToString());
                json.Key("constraint").Value(grid.constraint.ToString());
                json.Key("constraintCount").Value(grid.constraintCount);
                json.Key("padding").Vector4(Pad(grid.padding));
                json.EndObject();
            }

            var hv = t.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (hv != null)
            {
                json.Key("layoutGroup").Object();
                json.Key("kind").Value(hv.GetType().Name);
                json.Key("spacing").Value(hv.spacing);
                json.Key("childAlignment").Value(hv.childAlignment.ToString());
                json.Key("padding").Vector4(Pad(hv.padding));
                json.Key("childControlWidth").Value(hv.childControlWidth);
                json.Key("childControlHeight").Value(hv.childControlHeight);
                json.Key("childForceExpandWidth").Value(hv.childForceExpandWidth);
                json.Key("childForceExpandHeight").Value(hv.childForceExpandHeight);
                Optional(json, hv, "childScaleWidth");
                Optional(json, hv, "childScaleHeight");
                Optional(json, hv, "reverseArrangement");
                json.EndObject();
            }

            var fitter = t.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                json.Key("sizeFitter").Object();
                json.Key("horizontalFit").Value(fitter.horizontalFit.ToString());
                json.Key("verticalFit").Value(fitter.verticalFit.ToString());
                json.EndObject();
            }

            var element = t.GetComponent<LayoutElement>();
            if (element != null)
            {
                json.Key("layoutElement").Object();
                json.Key("ignoreLayout").Value(element.ignoreLayout);
                json.Key("minWidth").Value(element.minWidth);
                json.Key("minHeight").Value(element.minHeight);
                json.Key("preferredWidth").Value(element.preferredWidth);
                json.Key("preferredHeight").Value(element.preferredHeight);
                json.Key("flexibleWidth").Value(element.flexibleWidth);
                json.Key("flexibleHeight").Value(element.flexibleHeight);
                json.EndObject();
            }
        }

        /// <summary>Write a property only if this Unity version has it.
        /// <para/>
        /// Image.pixelsPerUnitMultiplier and the layout groups' childScale and
        /// reverseArrangement were each added in a different Unity version. Naming
        /// them directly would make this whole file fail to compile on anything
        /// older, and the version in use is not something this script gets to
        /// choose - so they are asked for by name and left out when absent.</summary>
        private static void Optional(Json json, object target, string name)
        {
            var value = Prop(target, target.GetType(), name);
            if (value == null) return;
            if (value is bool b) json.Key(name).Value(b);
            else if (value is float f) json.Key(name).Value(f);
            else if (value is int i) json.Key(name).Value(i);
            else json.Key(name).Value(value.ToString());
        }

        private static Vector4 Pad(RectOffset p)
            => new Vector4(p.left, p.right, p.top, p.bottom);

        // ── Making layout real ───────────────────────────────────────────

        private static void ForceLayout(Canvas canvas)
        {
            Canvas.ForceUpdateCanvases();
            var rt = canvas.transform as RectTransform;
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>Switches a dormant object on, remembering every ancestor it
        /// had to switch on to get there, and puts all of them back.
        /// <para/>
        /// The restore runs in a finally, and in reverse order, because a partial
        /// restore leaves the author's scene quietly different from the one they
        /// opened.</summary>
        private sealed class ActivationScope
        {
            private readonly List<GameObject> _switched = new List<GameObject>();

            public static ActivationScope Open(GameObject target)
            {
                var scope = new ActivationScope();
                var chain = new List<GameObject>();
                for (var t = target.transform; t != null; t = t.parent)
                    chain.Add(t.gameObject);
                chain.Reverse();                     // root first

                foreach (var go in chain)
                {
                    if (go.activeSelf) continue;
                    go.SetActive(true);
                    scope._switched.Add(go);
                }
                return scope;
            }

            public void Close()
            {
                for (int i = _switched.Count - 1; i >= 0; i--)
                {
                    if (_switched[i] != null) _switched[i].SetActive(false);
                }
                _switched.Clear();
            }
        }

        // ── The controls ─────────────────────────────────────────────────

        /// <summary>Two checks whose only job is to fail when the extraction is
        /// lying. See the header for why they are shaped this way.</summary>
        private static void RunControls(List<Canvas> surfaces, Report log)
        {
            log.Line("");
            log.Line("── Controls ──");

            // 1. A live surface must read the same before and after a rebuild.
            var liveOne = surfaces.FirstOrDefault(
                c => c.gameObject.activeInHierarchy && HasLayoutGroup(c.transform));
            if (liveOne == null)
            {
                log.Warn("Control 1 skipped: no live surface with a layout group, so " +
                         "there was nothing to check the direct read against.");
            }
            else
            {
                var before = Snapshot(liveOne.transform);
                ForceLayout(liveOne);
                var after = Snapshot(liveOne.transform);
                int moved = CountDifferences(before, after);
                if (moved == 0)
                    log.Line("Control 1 PASSED: '" + PathOf(liveOne.transform) +
                             "' read identically before and after a rebuild (" +
                             before.Count + " objects).");
                else
                    log.Problem("Control 1 FAILED: rebuilding the live surface '" +
                                PathOf(liveOne.transform) + "' moved " + moved +
                                " object(s). Either the rebuild is destructive or the " +
                                "direct read was wrong; the resolved geometry in this " +
                                "run cannot be trusted until that is explained.");
            }

            // 2. Does a rebuild actually do anything?
            //
            //    The first version of this asked whether resolving a dormant
            //    surface changed it, and called "no" a failure. That cannot tell a
            //    broken rebuild from a scene whose layout results were already
            //    serialised correctly - and this scene's are, so it reported a
            //    problem that did not exist while proving nothing either way.
            //
            //    Displacing a child first makes the two distinguishable. A working
            //    rebuild MUST put it back; a no-op leaves it where it was pushed.
            RectTransform group = null, victim = null;
            Canvas host = null;
            foreach (var canvas in surfaces.OrderByDescending(c => c.gameObject.activeInHierarchy))
            {
                foreach (var candidate in canvas.GetComponentsInChildren<LayoutGroup>(true))
                {
                    var rt = candidate.transform as RectTransform;
                    if (rt == null || !candidate.gameObject.activeInHierarchy) continue;

                    // It has to be a child the group actually manages. A layout
                    // group ignores inactive children and ones that opt out, so
                    // displacing either proves nothing: it stays where it was put
                    // because that is correct, and the control reads as a failure.
                    // Choosing badly here is how this reported a broken rebuild
                    // twice over a scene that was fine.
                    for (int i = 0; i < rt.childCount; i++)
                    {
                        var child = rt.GetChild(i) as RectTransform;
                        if (child == null || !child.gameObject.activeInHierarchy) continue;
                        var opt = child.GetComponent<LayoutElement>();
                        if (opt != null && opt.ignoreLayout) continue;
                        group = rt; victim = child; host = canvas;
                        break;
                    }
                    if (victim != null) break;
                }
                if (victim != null) break;
            }

            if (victim == null)
            {
                log.Warn("Control 2 skipped: no active layout group with an active, " +
                         "non-opted-out child was found, so there was no way to test " +
                         "whether a rebuild does anything.");
                return;
            }

            Vector2 original = victim.anchoredPosition;
            Vector2 nudged = original + new Vector2(137f, 91f);
            var scope = host.gameObject.activeInHierarchy
                ? null : ActivationScope.Open(host.gameObject);
            Vector2 settled;
            try
            {
                victim.anchoredPosition = nudged;
                LayoutRebuilder.ForceRebuildLayoutImmediate(group);
                settled = victim.anchoredPosition;
            }
            finally
            {
                // Back where it was, whatever happened above. A control that
                // leaves the scene changed is not a control, it is damage.
                victim.anchoredPosition = original;
                if (scope != null) scope.Close();
            }

            string who = PathOf(victim);
            if ((settled - original).sqrMagnitude < 0.0001f)
                log.Line("Control 2 PASSED: '" + who + "' was displaced by (137, 91) " +
                         "and the rebuild put it back, so layout is really running. " +
                         "Where a rebuild changes nothing, the values were already right.");
            else if ((settled - nudged).sqrMagnitude < 0.0001f)
                log.Problem("Control 2 FAILED: '" + who + "' stayed where it was pushed. " +
                            "The rebuild is a no-op, so every rect marked 'rebuilt' in " +
                            "this run is really 'unverified'.");
            else
                log.Problem("Control 2 INCONCLUSIVE: '" + who + "' was displaced to " +
                            F(nudged.x) + "," + F(nudged.y) + " and settled at " +
                            F(settled.x) + "," + F(settled.y) + ", which is neither " +
                            "where it started nor where it was put. Something else is " +
                            "moving it and the resolved geometry needs explaining " +
                            "before it is used.");
        }

        private static bool HasLayoutGroup(Transform root)
            => root.GetComponentsInChildren<LayoutGroup>(true).Length > 0;

        private static List<Vector2> Snapshot(Transform root)
        {
            var sizes = new List<Vector2>();
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            {
                sizes.Add(rt.anchoredPosition);
                sizes.Add(rt.rect.size);
            }
            return sizes;
        }

        private static int CountDifferences(List<Vector2> a, List<Vector2> b)
        {
            int n = Mathf.Min(a.Count, b.Count), differences = 0;
            for (int i = 0; i < n; i++)
                if ((a[i] - b[i]).sqrMagnitude > 0.0001f) differences++;
            return differences + Mathf.Abs(a.Count - b.Count);
        }

        // ── Sprites, written once each ───────────────────────────────────

        /// <summary>Every distinct sprite crop, exported once and referred to by
        /// key. The vanilla UI reuses a handful of white shapes across thousands
        /// of Images and recolours them, so this collapses a great deal - and the
        /// count it ends on is the answer to how many shapes the UI really has.
        /// </summary>
        private sealed class SpriteLibrary
        {
            private readonly string _dir;
            private readonly Report _log;
            private readonly Dictionary<string, string> _keys =
                new Dictionary<string, string>();
            private readonly List<string> _entries = new List<string>();

            public SpriteLibrary(string dir, Report log) { _dir = dir; _log = log; }

            public int Count => _keys.Count;

            public string Add(Sprite sprite)
            {
                var texture = sprite.texture;
                if (texture == null) return "";

                var r = sprite.textureRect;
                string identity = texture.name + "|" +
                                  Mathf.RoundToInt(r.x) + "," + Mathf.RoundToInt(r.y) + "," +
                                  Mathf.RoundToInt(r.width) + "," + Mathf.RoundToInt(r.height);
                if (_keys.TryGetValue(identity, out string existing)) return existing;

                string key = SafeFileName(sprite.name) + "_" + (_keys.Count + 1);
                _keys[identity] = key;

                string path = Path.Combine(_dir, key + ".png");
                bool ok = TryExport(texture, r, path);
                if (!ok) _log.Problem("Sprite '" + sprite.name + "' could not be read " +
                                     "from texture '" + texture.name + "'.");

                var e = new Json();
                e.Object();
                e.Key("key").Value(key);
                e.Key("sprite").Value(sprite.name);
                e.Key("texture").Value(texture.name);
                e.Key("file").Value(ok ? key + ".png" : "");
                e.Key("textureRect").Vector4(new Vector4(r.x, r.y, r.width, r.height));
                e.Key("border").Vector4(sprite.border);
                e.Key("pivot").Vector2(sprite.pivot);
                e.Key("pixelsPerUnit").Value(sprite.pixelsPerUnit);
                e.EndObject();
                _entries.Add(e.ToString());
                return key;
            }

            /// <summary>Blit to a RenderTexture and read back, the same detour the
            /// art extractor uses: it works on compressed and non-readable
            /// textures without touching importer settings on the source asset.
            /// ReadPixels and textureRect share a bottom-left origin, so nothing
            /// is flipped.</summary>
            private static bool TryExport(Texture2D source, Rect rect, string outPath)
            {
                int x = Mathf.RoundToInt(rect.x), y = Mathf.RoundToInt(rect.y);
                int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
                if (w <= 0 || h <= 0) return false;

                var rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                                                    RenderTextureFormat.ARGB32,
                                                    RenderTextureReadWrite.Linear);
                var previous = RenderTexture.active;
                Texture2D readable = null;
                try
                {
                    Graphics.Blit(source, rt);
                    RenderTexture.active = rt;
                    readable = new Texture2D(w, h, TextureFormat.ARGB32, mipChain: false);
                    readable.ReadPixels(new Rect(x, y, w, h), 0, 0);
                    readable.Apply();

                    var bytes = readable.EncodeToPNG();
                    if (bytes == null || bytes.Length == 0) return false;
                    File.WriteAllBytes(outPath, bytes);
                    return true;
                }
                catch { return false; }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                    if (readable != null) Object.DestroyImmediate(readable);
                }
            }

            public string ToJson()
                => "[\n  " + string.Join(",\n  ", _entries) + "\n]\n";
        }

        // ── Which fonts are needed ───────────────────────────────────────

        /// <summary>Every font any text object asks for, and how many ask for it.
        /// The font files themselves are supplied separately, so this list is the
        /// specification of which ones.</summary>
        private sealed class FontLedger
        {
            private sealed class Use
            {
                public int Count;
                public readonly HashSet<string> Kinds = new HashSet<string>();
                public readonly List<string> Examples = new List<string>();
            }

            private readonly Dictionary<string, Use> _uses = new Dictionary<string, Use>();

            public int Count => _uses.Count;

            public void Note(string font, string kind, Transform where)
            {
                if (!_uses.TryGetValue(font, out var use))
                    _uses[font] = use = new Use();
                use.Count++;
                use.Kinds.Add(kind);
                if (use.Examples.Count < 5) use.Examples.Add(PathOf(where));
            }

            public string ToJson()
            {
                var json = new Json();
                json.Array();
                foreach (var pair in _uses.OrderByDescending(p => p.Value.Count))
                {
                    json.Object();
                    json.Key("font").Value(pair.Key);
                    json.Key("usedBy").Value(pair.Value.Count);
                    json.Key("kinds").Array();
                    foreach (var k in pair.Value.Kinds.OrderBy(k => k)) json.Value(k);
                    json.EndArray();
                    json.Key("examples").Array();
                    foreach (var e in pair.Value.Examples) json.Value(e);
                    json.EndArray();
                    json.EndObject();
                }
                json.EndArray();
                return json.ToString();
            }
        }

        // ── The report ───────────────────────────────────────────────────

        private sealed class Report
        {
            private readonly StringBuilder _sb = new StringBuilder();
            public int Problems { get; private set; }

            public Report()
            {
                _sb.Append("SMSModForge — vanilla UI surface extraction\n");
                _sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",
                                                 CultureInfo.InvariantCulture)).Append('\n');
                _sb.Append("Unity ").Append(Application.unityVersion).Append("\n\n");
            }

            private readonly List<string> _borderless = new List<string>();
            private readonly List<string> _wastedBorder = new List<string>();
            private int _borderlessTotal, _wastedBorderTotal;

            /// <summary>Watch for the two ways a nine-slice can be a lie.
            /// <para/>
            /// A Sliced image whose sprite has no border draws exactly as a Simple
            /// one, so a preview that faithfully reproduced "Sliced" would be right
            /// by accident and wrong the moment that sprite gained a border. The
            /// mirror case - a sprite that carries a border, used as Simple - means
            /// Unity is stretching the corners the artist drew to protect. Neither
            /// is an error here. Both are things the preview's author has to know
            /// before deciding what faithful means.</summary>
            public void NoteSlice(Image.Type type, Vector4 border, string path, string sprite)
            {
                bool hasBorder = border != Vector4.zero;
                if (type == Image.Type.Sliced && !hasBorder)
                {
                    if (_borderless.Count < 12) _borderless.Add(path + "  (" + sprite + ")");
                    _borderlessTotal++;
                }
                else if (type == Image.Type.Simple && hasBorder)
                {
                    if (_wastedBorder.Count < 12) _wastedBorder.Add(path + "  (" + sprite + ")");
                    _wastedBorderTotal++;
                }
            }

            private string SliceNotes()
            {
                if (_borderlessTotal == 0 && _wastedBorderTotal == 0) return "";
                var sb = new StringBuilder("\n\n-- Nine-slice notes --\n");
                if (_borderlessTotal > 0)
                {
                    sb.Append(_borderlessTotal)
                      .Append(" image(s) set to Sliced whose sprite has no border - ")
                      .Append("these draw as Simple:\n");
                    foreach (var e in _borderless) sb.Append("    ").Append(e).Append('\n');
                }
                if (_wastedBorderTotal > 0)
                {
                    sb.Append(_wastedBorderTotal)
                      .Append(" image(s) set to Simple whose sprite HAS a border - ")
                      .Append("the corners are being stretched:\n");
                    foreach (var e in _wastedBorder) sb.Append("    ").Append(e).Append('\n');
                }
                return sb.ToString();
            }

            public void Line(string text) => _sb.Append(text).Append('\n');
            public void Warn(string text) { Problems++; _sb.Append("WARNING: ").Append(text).Append('\n'); }
            public void Problem(string text) { Problems++; _sb.Append("PROBLEM: ").Append(text).Append('\n'); }

            public override string ToString()
            {
                string body = _sb.ToString();
                string head = Problems == 0
                    ? "No problems. The geometry in this run can be relied on.\n\n"
                    : Problems + " problem(s) below. Read them before trusting the " +
                      "output of this run.\n\n";
                return head + body + SliceNotes();
            }
        }

        // ── A very small JSON writer ─────────────────────────────────────
        //
        // Hand-rolled because the trees here are arbitrary and Unity's
        // JsonUtility only does fields on a serialisable class. It tracks
        // whether a comma is needed, which is the only part of writing JSON by
        // hand that is actually easy to get wrong.

        private sealed class Json
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly Stack<bool> _first = new Stack<bool>();
            private bool _afterKey;

            private void Separate()
            {
                if (_afterKey) { _afterKey = false; return; }
                if (_first.Count == 0) return;
                if (_first.Peek()) { _first.Pop(); _first.Push(false); }
                else _sb.Append(',');
            }

            public Json Object() { Separate(); _sb.Append('{'); _first.Push(true); return this; }
            public Json EndObject() { _sb.Append('}'); _first.Pop(); return this; }
            public Json Array() { Separate(); _sb.Append('['); _first.Push(true); return this; }
            public Json EndArray() { _sb.Append(']'); _first.Pop(); return this; }

            public Json Key(string name)
            {
                Separate();
                _sb.Append(Quote(name)).Append(':');
                _afterKey = true;
                return this;
            }

            public Json Value(string v) { Separate(); _sb.Append(Quote(v)); return this; }
            public Json Value(bool v) { Separate(); _sb.Append(v ? "true" : "false"); return this; }
            public Json Value(int v) { Separate(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
            public Json Value(float v) { Separate(); _sb.Append(F(v)); return this; }

            public Json Vector2(Vector2 v)
            { Separate(); _sb.Append('[').Append(F(v.x)).Append(',').Append(F(v.y)).Append(']'); return this; }

            public Json Vector3(Vector3 v)
            {
                Separate();
                _sb.Append('[').Append(F(v.x)).Append(',').Append(F(v.y)).Append(',')
                   .Append(F(v.z)).Append(']');
                return this;
            }

            public Json Vector4(Vector4 v)
            {
                Separate();
                _sb.Append('[').Append(F(v.x)).Append(',').Append(F(v.y)).Append(',')
                   .Append(F(v.z)).Append(',').Append(F(v.w)).Append(']');
                return this;
            }

            public override string ToString() => _sb.ToString();
        }

        // ── Reflection helpers (TMP without a hard reference) ────────────

        private static object Prop(object o, Type type, string name)
        {
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null) { try { return p.GetValue(o, null); } catch { return null; } }
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) { try { return f.GetValue(o); } catch { return null; } }
            return null;
        }

        private static string Str(object o, Type type, string name)
        {
            var v = Prop(o, type, name);
            if (v == null) return "";
            if (v is float f) return F(f);
            if (v is Vector4 v4)
                return "[" + F(v4.x) + "," + F(v4.y) + "," + F(v4.z) + "," + F(v4.w) + "]";
            return v.ToString();
        }

        private static string ObjName(object o, Type type, string name)
            => Prop(o, type, name) is Object u && u != null ? u.name : "";

        private static string ColorHex(object o, Type type, string name)
            => Prop(o, type, name) is Color c ? Hex(c) : "";

        // ── Small helpers ────────────────────────────────────────────────

        private static int CountBelow(Transform t)
            => t.GetComponentsInChildren<Transform>(true).Length - 1;

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c != null; c = c.parent) parts.Insert(0, c.name);
            return string.Join("/", parts);
        }

        private static string SafeFileName(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
                sb.Append(Path.GetInvalidFileNameChars().Contains(ch) || ch == '/' ? '_' : ch);
            string name = sb.ToString().Trim();
            return name.Length == 0 ? "unnamed" : name;
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

        private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

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
                        if (ch < 0x20)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

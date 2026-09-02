// SMSModForge — TextMeshPro font asset exporter  (Unity Editor script)
//
// WHY THIS EXISTS
//   Hunting for the TTFs behind the game's text runs into a wall: most of them
//   are not in the project and never were. A TMP font asset is self-contained
//   once baked - an SDF atlas texture, a glyph table, kerning, and a material -
//   and the source font is only needed to bake it. A ripped project keeps what
//   shipped, and what shipped is the asset.
//
//   That is not a loss, it is the better source. TMP does not read a TTF at
//   runtime either, so drawing from the atlas reproduces what the game draws,
//   while drawing from a TTF through WPF's text stack reproduces something close
//   to it. The atlas gives exact glyph images and exact advances.
//
// WHAT IT WRITES, per font asset
//   <out>/Fonts/<name>.json     face metrics, glyph table, character map,
//                               kerning, atlas parameters, material SDF settings
//   <out>/Fonts/<name>_<n>.png  the SDF atlas page(s)
//   <out>/Fonts/report.txt      coverage, and every property that could not be
//                               read, by name
//
// THE FLIP THAT WILL BITE IF IT IS NOT SAID
//   A glyph's rect in the asset is in atlas pixels with a BOTTOM-LEFT origin,
//   because that is how Unity addresses textures. The PNG written beside it has
//   a top-left origin, because that is how PNG works. Reading one with the
//   other's convention puts every glyph in the wrong row - and it still looks
//   like text, which is what makes it dangerous. So both are written: "rect" is
//   the asset's own bottom-up rectangle and "rectTopLeft" is the same rectangle
//   ready to crop out of the PNG. Use the second unless you know why you want
//   the first.
//
// EVERYTHING IS REFLECTION
//   So this compiles with or without a TMP reference, and so a TMP version that
//   renamed a property produces a named gap in the report rather than a zero
//   that reads like a measurement.
//
// CONTROLS
//   A font that yields no glyphs, or an atlas that writes zero bytes, is
//   reported as a problem rather than written as an empty file that looks
//   finished. Coverage of printable ASCII is reported per font, because an atlas
//   holds only the characters baked into it - which is what decides whether a
//   pack author can type something the vanilla UI never displayed.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SMSModForge.EditorTools
{
    public static class SMSModForgeTmpFontExporter
    {
        private const string MenuPath = "Tools/SMSModForge/Export TMP Font Assets…";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            string defaultRoot = Path.GetDirectoryName(Application.dataPath) ?? "";
            string outDir = EditorUtility.SaveFolderPanel(
                "Choose output folder (the same one as the UI surfaces)",
                defaultRoot, "VanillaUi");
            if (string.IsNullOrEmpty(outDir)) return;

            string fontDir = Path.Combine(outDir, "Fonts");
            Directory.CreateDirectory(fontDir);

            var log = new StringBuilder();
            log.Append("SMSModForge — TMP font asset export\n")
               .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
               .Append("\nUnity ").Append(Application.unityVersion).Append("\n\n");

            var fonts = CollectFonts(log);
            log.Append("Font assets referenced by this scene: ").Append(fonts.Count).Append("\n\n");

            int failures = 0;
            try
            {
                int done = 0;
                foreach (var font in fonts)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Exporting TMP fonts", font.name,
                            ++done / (float)Math.Max(fonts.Count, 1)))
                    {
                        log.Append("CANCELLED after ").Append(done - 1).Append(" fonts.\n");
                        break;
                    }
                    if (!Export(font, fontDir, log)) failures++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            File.WriteAllText(Path.Combine(fontDir, "report.txt"), log.ToString());
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("SMSModForge — Export TMP Font Assets",
                fonts.Count + " font asset(s)" +
                (failures > 0 ? ", " + failures + " with problems" : ", no problems") +
                ".\n\nRead Fonts/report.txt for per-font coverage and anything that " +
                "could not be read.\n\nWritten to:\n" + fontDir, "OK");
            Debug.Log("[SMSModForge] TMP fonts written to " + fontDir);
        }

        // ── Which fonts ──────────────────────────────────────────────────

        /// <summary>Every font asset a text object in the scene asks for, plus the
        /// fallbacks those name.
        /// <para/>
        /// The fallback chain matters: this scene shows a "LiberationSans SDF -
        /// Fallback" in use, which is TMP reaching past a font that lacked a
        /// glyph. Exporting the head of a chain and not the rest would reproduce
        /// the gap without the thing that fills it.</summary>
        private static List<Object> CollectFonts(StringBuilder log)
        {
            var found = new List<Object>();
            var seen = new HashSet<int>();

            void Add(Object font)
            {
                if (font == null || !seen.Add(font.GetInstanceID())) return;
                found.Add(font);
                if (Prop(font, font.GetType(), "fallbackFontAssetTable") is IEnumerable list)
                    foreach (var fallback in list) Add(fallback as Object);
            }

            foreach (var component in Object.FindObjectsOfType<Component>(true))
            {
                if (component == null) continue;
                string kind = component.GetType().Name;
                if (kind != "TextMeshProUGUI" && kind != "TextMeshPro") continue;
                Add(Prop(component, component.GetType(), "font") as Object);
            }

            if (found.Count == 0)
                log.Append("WARNING: no TextMeshPro components were found in the open " +
                           "scene, so there was nothing to export.\n\n");
            return found.OrderBy(f => f.name, StringComparer.Ordinal).ToList();
        }

        // ── One font ─────────────────────────────────────────────────────

        private static bool Export(Object font, string dir, StringBuilder log)
        {
            var type = font.GetType();
            string safe = SafeName(font.name);
            var missing = new List<string>();
            bool ok = true;

            var json = new Json();
            json.Object();
            json.Key("name").Value(font.name);
            json.Key("sourceFontFile").Value(ObjName(font, type, "sourceFontFile"));

            // Static means every glyph is baked into the atlas and the asset is
            // self-contained. Dynamic means the atlas starts empty and TMP
            // rasterises glyphs on demand from the source font - so a dynamic
            // asset needs that font file after all, and is the one case where
            // the atlas route does not save us the hunt.
            string population = Str(font, type, "atlasPopulationMode");
            json.Key("atlasPopulationMode").Value(population);
            bool dynamic = population.IndexOf("Dynamic", StringComparison.OrdinalIgnoreCase) >= 0;
            json.Key("needsSourceFont").Value(dynamic);

            // ── Atlas pages ──────────────────────────────────────────────
            var pages = new List<Texture2D>();
            if (Prop(font, type, "atlasTextures") is Array array)
                foreach (var page in array) if (page is Texture2D t) pages.Add(t);
            if (pages.Count == 0 && Prop(font, type, "atlasTexture") is Texture2D single)
                pages.Add(single);

            int atlasWidth = Int(font, type, "atlasWidth", missing);
            int atlasHeight = Int(font, type, "atlasHeight", missing);

            // The configured size and the texture that exists are different
            // questions: an unpopulated dynamic atlas declares 1024x1024 and
            // holds a 1x1 texture. rectTopLeft is computed from a height, and
            // computing it from the declared one would put every glyph of such
            // a font somewhere it is not.
            int textureWidth = pages.Count > 0 ? pages[0].width : 0;
            int textureHeight = pages.Count > 0 ? pages[0].height : 0;
            if (atlasWidth <= 0) atlasWidth = textureWidth;
            if (atlasHeight <= 0) atlasHeight = textureHeight;
            int flipHeight = textureHeight > 0 ? textureHeight : atlasHeight;

            json.Key("atlas").Object();
            json.Key("width").Value(atlasWidth);
            json.Key("height").Value(atlasHeight);
            json.Key("textureWidth").Value(textureWidth);
            json.Key("textureHeight").Value(textureHeight);
            json.Key("padding").Value(Int(font, type, "atlasPadding", missing));
            json.Key("renderMode").Value(Str(font, type, "atlasRenderMode"));
            json.Key("pages").Array();
            for (int p = 0; p < pages.Count; p++)
            {
                string file = safe + "_" + p + ".png";
                if (TryWritePng(pages[p], Path.Combine(dir, file)))
                {
                    json.Value(file);
                }
                else
                {
                    ok = false;
                    json.Value("");
                    log.Append("PROBLEM: ").Append(font.name).Append(" — atlas page ")
                       .Append(p).Append(" could not be read.\n");
                }
            }
            json.EndArray();
            json.EndObject();

            if (pages.Count == 0)
            {
                ok = false;
                log.Append("PROBLEM: ").Append(font.name)
                   .Append(" — no atlas texture on this asset. The glyph metrics are " +
                           "still written, but there is no image to draw them from.\n");
            }

            // ── Face metrics ─────────────────────────────────────────────
            //
            // What TMP lays a line out with: where the baseline sits, how tall a
            // line is, how the point size relates to the atlas. Without these the
            // glyphs are just a pile of pictures.
            AppendFace(json, font, type, missing);

            int glyphs = AppendGlyphs(json, font, type, flipHeight, missing);
            if (glyphs == 0 && dynamic)
            {
                // Expected, not broken. A dynamic asset ships empty and fills
                // itself from the source font as the game asks for characters,
                // so this is the one kind of font that does still need its TTF.
                log.Append("NEEDS FONT: ").Append(font.name)
                   .Append(" — dynamic atlas, empty until runtime. Rendering it " +
                           "needs the source font: ")
                   .Append(ObjName(font, type, "sourceFontFile")).Append('\n');
            }
            else if (glyphs == 0)
            {
                ok = false;
                log.Append("PROBLEM: ").Append(font.name)
                   .Append(" — static atlas with an empty glyph table, so nothing " +
                           "can be drawn from this asset at all.\n");
            }

            int characters = AppendCharacters(json, font, type, out var codes, missing);
            AppendKerning(json, font, type, missing);
            AppendMaterial(json, Prop(font, type, "material") as Material);
            json.EndObject();

            File.WriteAllText(Path.Combine(dir, safe + ".json"), json.ToString());

            // ── Coverage: what a pack author is allowed to type ──────────
            var absent = new List<int>();
            for (int c = 32; c <= 126; c++) if (!codes.Contains(c)) absent.Add(c);

            log.Append(!ok ? "PROBLEM " : dynamic ? "DYNAMIC " : "OK      ")
               .Append(font.name.PadRight(32))
               .Append(glyphs.ToString().PadLeft(5)).Append(" glyphs, ")
               .Append(characters.ToString().PadLeft(5)).Append(" chars, ")
               .Append(pages.Count).Append(" page(s), ");
            if (dynamic && characters == 0)
                log.Append("populated at runtime, coverage decided by the source font");
            else if (absent.Count == 0) log.Append("printable ASCII complete");
            else
            {
                log.Append(absent.Count).Append(" of 95 printable ASCII missing: ");
                foreach (int c in absent.Take(24))
                    log.Append(c == 32 ? "space" : ((char)c).ToString()).Append(' ');
                if (absent.Count > 24) log.Append("…");
            }
            log.Append('\n');

            if (missing.Count > 0)
                log.Append("        could not read: ")
                   .Append(string.Join(", ", missing.Distinct())).Append('\n');
            return ok;
        }

        private static void AppendFace(Json json, Object font, Type type, List<string> missing)
        {
            var face = Prop(font, type, "faceInfo");
            json.Key("face");
            if (face == null) { missing.Add("faceInfo"); json.Object().EndObject(); return; }

            var ft = face.GetType();
            json.Object();
            json.Key("familyName").Value(Str(face, ft, "familyName"));
            json.Key("styleName").Value(Str(face, ft, "styleName"));
            // "unitsPerEm" is spelled unitsPerEM in TextCore's FaceInfo. All 32
            // fonts reporting it unreadable was one wrong capital, not 32 broken
            // assets - so the spellings worth trying are tried, and only a name
            // matching none of them is reported as a gap.
            AppendFirstOf(json, face, ft, "unitsPerEm",
                          new[] { "unitsPerEM", "unitsPerEm" }, missing);

            string[] numbers =
            {
                "pointSize", "scale", "lineHeight", "ascentLine",
                "baseline", "descentLine", "capLine", "meanLine",
                "superscriptOffset", "superscriptSize", "subscriptOffset",
                "subscriptSize", "underlineOffset", "underlineThickness",
                "strikethroughOffset", "strikethroughThickness", "tabWidth",
            };
            foreach (var name in numbers)
            {
                object v = Prop(face, ft, name);
                if (v == null) { missing.Add("faceInfo." + name); continue; }
                json.Key(name).Value(ToFloat(v));
            }
            json.EndObject();
        }

        /// <summary>Write the first of several spellings that resolves, under one
        /// agreed key. Reflection turns a renamed property into silence, and
        /// silence across every font reads like a broken export rather than the
        /// typo it usually is.</summary>
        private static void AppendFirstOf(Json json, object owner, Type type,
                                          string key, string[] candidates,
                                          List<string> missing)
        {
            foreach (var name in candidates)
            {
                object v = Prop(owner, type, name);
                if (v == null) continue;
                json.Key(key).Value(ToFloat(v));
                return;
            }
            missing.Add("faceInfo." + string.Join("/", candidates));
        }

        private static int AppendGlyphs(Json json, Object font, Type type,
                                        int atlasHeight, List<string> missing)
        {
            json.Key("glyphs").Array();
            if (!(Prop(font, type, "glyphTable") is IEnumerable table))
            {
                missing.Add("glyphTable");
                json.EndArray();
                return 0;
            }

            int count = 0;
            foreach (var glyph in table)
            {
                if (glyph == null) continue;
                var gt = glyph.GetType();
                json.Object();
                json.Key("index").Value(Int(glyph, gt, "index", missing));

                var rect = Prop(glyph, gt, "glyphRect");
                if (rect == null) missing.Add("glyphRect");
                else
                {
                    var rt = rect.GetType();
                    int x = Int(rect, rt, "x", missing), y = Int(rect, rt, "y", missing);
                    int w = Int(rect, rt, "width", missing), h = Int(rect, rt, "height", missing);
                    json.Key("rect").Vector4(new Vector4(x, y, w, h));
                    // The same rectangle with the rows counted from the top, ready
                    // to crop from the PNG. See this file's header: getting it
                    // wrong still looks like text.
                    json.Key("rectTopLeft").Vector4(
                        new Vector4(x, Mathf.Max(0, atlasHeight - y - h), w, h));
                }

                var metrics = Prop(glyph, gt, "metrics");
                if (metrics == null) missing.Add("metrics");
                else
                {
                    var mt = metrics.GetType();
                    json.Key("metrics").Object();
                    json.Key("width").Value(Float(metrics, mt, "width", missing));
                    json.Key("height").Value(Float(metrics, mt, "height", missing));
                    json.Key("bearingX").Value(Float(metrics, mt, "horizontalBearingX", missing));
                    json.Key("bearingY").Value(Float(metrics, mt, "horizontalBearingY", missing));
                    json.Key("advance").Value(Float(metrics, mt, "horizontalAdvance", missing));
                    json.EndObject();
                }

                object scale = Prop(glyph, gt, "scale");
                if (scale != null) json.Key("scale").Value(ToFloat(scale));
                object page = Prop(glyph, gt, "atlasIndex");
                if (page != null) json.Key("page").Value(ToInt(page));

                json.EndObject();
                count++;
            }
            json.EndArray();
            return count;
        }

        private static int AppendCharacters(Json json, Object font, Type type,
                                            out HashSet<int> codes, List<string> missing)
        {
            codes = new HashSet<int>();
            json.Key("characters").Array();
            if (!(Prop(font, type, "characterTable") is IEnumerable table))
            {
                missing.Add("characterTable");
                json.EndArray();
                return 0;
            }

            int count = 0;
            foreach (var character in table)
            {
                if (character == null) continue;
                var ct = character.GetType();
                int unicode = Int(character, ct, "unicode", missing);
                codes.Add(unicode);
                json.Object();
                json.Key("unicode").Value(unicode);
                json.Key("glyphIndex").Value(Int(character, ct, "glyphIndex", missing));
                json.EndObject();
                count++;
            }
            json.EndArray();
            return count;
        }

        /// <summary>Kerning. Without it, every pair the designer tightened draws a
        /// little too wide, and across a line that accumulates into a visible
        /// difference from the game.</summary>
        private static void AppendKerning(Json json, Object font, Type type, List<string> missing)
        {
            json.Key("kerning").Array();
            var featureTable = Prop(font, type, "fontFeatureTable");
            var records = featureTable == null ? null
                : Prop(featureTable, featureTable.GetType(),
                       "glyphPairAdjustmentRecords") as IEnumerable;
            if (records == null)
            {
                missing.Add("fontFeatureTable.glyphPairAdjustmentRecords");
                json.EndArray();
                return;
            }

            foreach (var record in records)
            {
                if (record == null) continue;
                var rt = record.GetType();
                var first = Prop(record, rt, "firstAdjustmentRecord");
                var second = Prop(record, rt, "secondAdjustmentRecord");
                if (first == null || second == null) continue;

                json.Object();
                json.Key("first").Value(ToInt(Prop(first, first.GetType(), "glyphIndex")));
                json.Key("second").Value(ToInt(Prop(second, second.GetType(), "glyphIndex")));
                json.Key("firstAdvance").Value(AdvanceOf(first));
                json.Key("secondAdvance").Value(AdvanceOf(second));
                json.EndObject();
            }
            json.EndArray();
        }

        private static float AdvanceOf(object record)
        {
            var values = Prop(record, record.GetType(), "glyphValueRecord");
            return values == null ? 0f
                : ToFloat(Prop(values, values.GetType(), "xAdvance"));
        }

        /// <summary>The material is where TMP keeps its outline and its underlay -
        /// what everything else calls a drop shadow. Several of this game's font
        /// assets differ from one another only here, which is why "Acme-Shadow"
        /// turns out to be plain Acme with a material on it.</summary>
        private static void AppendMaterial(Json json, Material material)
        {
            json.Key("material");
            if (material == null) { json.Object().EndObject(); return; }

            json.Object();
            json.Key("name").Value(material.name);
            json.Key("shader").Value(material.shader != null ? material.shader.name : "");

            string[] floats =
            {
                "_OutlineWidth", "_OutlineSoftness", "_FaceDilate", "_Sharpness",
                "_UnderlayOffsetX", "_UnderlayOffsetY", "_UnderlayDilate",
                "_UnderlaySoftness", "_GradientScale", "_ScaleRatioA",
                "_ScaleRatioB", "_ScaleRatioC", "_WeightNormal", "_WeightBold",
                "_TextureWidth", "_TextureHeight",
            };
            foreach (var name in floats)
                if (material.HasProperty(name))
                    json.Key(name.TrimStart('_')).Value(material.GetFloat(name));

            string[] colors = { "_FaceColor", "_OutlineColor", "_UnderlayColor" };
            foreach (var name in colors)
                if (material.HasProperty(name))
                    json.Key(name.TrimStart('_'))
                        .Value("#" + ColorUtility.ToHtmlStringRGBA(material.GetColor(name)));

            json.EndObject();
        }

        // ── Writing a texture out ────────────────────────────────────────

        /// <summary>The blit-and-read-back detour the other extractors use, so a
        /// compressed or non-readable atlas works without touching importer
        /// settings on the source asset.</summary>
        private static bool TryWritePng(Texture2D source, string outPath)
        {
            if (source == null || source.width <= 0 || source.height <= 0) return false;

            var rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                                                RenderTextureFormat.ARGB32,
                                                RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                readable = new Texture2D(source.width, source.height,
                                         TextureFormat.ARGB32, mipChain: false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
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

        // ── Reflection ───────────────────────────────────────────────────

        private static object Prop(object o, Type type, string name)
        {
            if (o == null) return null;
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null) { try { return p.GetValue(o, null); } catch { return null; } }
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) { try { return f.GetValue(o); } catch { return null; } }
            return null;
        }

        private static string Str(object o, Type type, string name)
        {
            var v = Prop(o, type, name);
            return v == null ? "" : v.ToString();
        }

        private static string ObjName(object o, Type type, string name)
            => Prop(o, type, name) is Object u && u != null ? u.name : "";

        private static int Int(object o, Type type, string name, List<string> missing)
        {
            var v = Prop(o, type, name);
            if (v == null) { missing.Add(name); return 0; }
            return ToInt(v);
        }

        private static float Float(object o, Type type, string name, List<string> missing)
        {
            var v = Prop(o, type, name);
            if (v == null) { missing.Add(name); return 0f; }
            return ToFloat(v);
        }

        private static int ToInt(object v)
        {
            if (v == null) return 0;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static float ToFloat(object v)
        {
            if (v == null) return 0f;
            if (v is float f) return f;
            try { return Convert.ToSingle(v, CultureInfo.InvariantCulture); }
            catch { return 0f; }
        }

        private static string SafeName(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
                sb.Append(Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch);
            string name = sb.ToString().Trim();
            return name.Length == 0 ? "unnamed" : name;
        }
    }
}

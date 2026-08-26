// SMSModForge — Vanilla Bust Art Extractor (Unity Editor script)
//
// HOW TO USE
//   1. Copy this file into your Starmaker Story Unity project under any
//      `Assets/Editor/` folder (create one if it doesn't exist — Unity
//      treats anything inside an `Editor` folder as editor-only code).
//   2. Open the `CoreGameScene` scene (the file containing the
//      `2_Bust_Manager` GameObject — the same scene the runtime mod
//      hooks). Without this scene loaded the extractor has nothing to
//      walk.
//   3. From Unity's main menu pick:  Tools › SMSModForge › Extract Bust
//      Art…   You'll be prompted for an output folder. The recommended
//      target is `<SMSModForge repo>/SMSModForge/Resources/VanillaBustArt/`
//      — the editor's csproj ships the contents of that folder so the
//      dialogue-node preview can find them at run-time without any
//      asset extraction inside the editor itself.
//   4. Re-build the editor after the extraction finishes. The PNGs get
//      copied to the build output's `VanillaBustArt/` folder, next to
//      `SMSModForge.exe`.
//
// WHAT GETS EXTRACTED
//   For every direct child of `2_Bust_Manager` the extractor recursively
//   collects EVERY `SpriteRenderer` in that bust's hierarchy and writes
//   one PNG per sprite into `VanillaBustArt/<BustGoName>/`.
//
//   The bust GameObject itself is only a "CharacterBox" container — the
//   real body art lives on its first child (`MBase1`, `D1Base`, …), which
//   is how SMSAndroids reaches it too (`newBust.transform.GetChild(0)`).
//   Renderers under that body are classified by structural *pattern*, not
//   by exact GameObject names, so a `D1Base` bust is handled like an
//   `MBase1` one:
//     * Base.PNG               ← the body GameObject's own sprite
//     * Blink.PNG              ← a renderer whose name/ancestry says "blink"
//     * Mouth1.PNG … MouthN.PNG← renderers under a "mouth" group
//     * Expression<Name>.PNG   ← renderers under an "expression" group,
//                                named after the renderer's GameObject
//     * _extra/<path>.PNG      ← anything else, so nothing is ever lost
//   Each bust folder also gets a `_contents.txt` mapping every PNG back to
//   its source GameObject path, sprite name and pixel size.
//
// NOTES
//   * Pixels are read via the standard RenderTexture blit + ReadPixels
//     detour, so non-readable / GPU-compressed source textures are handled
//     without flipping importer settings on the source assets.
//   * Sprites are cropped with `Sprite.textureRect` (correct whether or
//     not the sprite is atlas-packed) and the sub-rect is read straight
//     off the blitted RenderTexture — ReadPixels and `textureRect` share
//     the same bottom-left origin, so no vertical inversion is applied.
//     (A spurious `height - y - h` inversion in the previous version is
//     what made every non-full-texture overlay come out blank/white.)
//   * Re-running the extractor overwrites existing PNGs in the
//     destination, so it doubles as the "refresh" path.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SMSModForge.UnityTools
{
    public static class SMSModForgeArtExtractor
    {
        private const string MenuPath = "Tools/SMSModForge/Extract Bust Art...";
        private const string BustManagerName = "2_Bust_Manager";

        private enum Role { Base, Blink, Mouth, Expression, Extra }

        private sealed class RendererInfo
        {
            public SpriteRenderer Renderer;
            public string Path;   // hierarchy path within the bust
            public Role Role;
        }

        // ── Entry point ──────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var bustManager = GameObject.Find(BustManagerName)
                              ?? FindInactiveByName(BustManagerName);
            if (bustManager == null)
            {
                EditorUtility.DisplayDialog(
                    "SMSModForge — Extract Bust Art",
                    "Could not find a GameObject named '" + BustManagerName +
                    "' in the currently open scene. Open the game's CoreGameScene " +
                    "before running this command.",
                    "OK");
                return;
            }

            string defaultRoot = Path.GetDirectoryName(Application.dataPath) ?? "";
            string outDir = EditorUtility.SaveFolderPanel(
                "Choose output folder (recommended: SMSModForge/Resources/VanillaBustArt)",
                defaultRoot, "VanillaBustArt");
            if (string.IsNullOrEmpty(outDir)) return;

            int busts, sprites;
            Extract(bustManager.transform, outDir, out busts, out sprites);

            EditorUtility.DisplayDialog(
                "SMSModForge — Extract Bust Art",
                "Extracted " + sprites + " sprite(s) across " + busts + " bust(s) to:\n\n" +
                outDir + "\n\nCopy this folder into <SMSModForge repo>/SMSModForge/" +
                "Resources/VanillaBustArt/ and rebuild the editor so the dialogue-node " +
                "previews can find the art.",
                "OK");
            Debug.Log("[SMSModForge] Extracted " + sprites + " sprite(s) across " +
                      busts + " bust(s) to " + outDir);
        }

        // ── Implementation ───────────────────────────────────────────────

        private static void Extract(Transform bustManager, string outDir,
                                     out int bustCount, out int spriteCount)
        {
            bustCount = 0;
            spriteCount = 0;
            int total = bustManager.childCount;
            try
            {
                for (int i = 0; i < total; i++)
                {
                    var bust = bustManager.GetChild(i);
                    bool cancel = EditorUtility.DisplayCancelableProgressBar(
                        "Extracting vanilla bust art",
                        bust.name + " (" + (i + 1) + "/" + total + ")",
                        (float)i / Mathf.Max(1, total));
                    if (cancel) break;

                    try
                    {
                        int n = ExtractBust(bust, Path.Combine(outDir, Sanitize(bust.name)));
                        if (n > 0) { bustCount++; spriteCount += n; }
                        else Debug.LogWarning("[SMSModForge] No sprites found on bust '" +
                                              bust.name + "'");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[SMSModForge] Bust extract failed for '" +
                                         bust.name + "': " + ex.Message);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Extract every sprite under one bust. Returns the number of PNGs
        /// written.
        /// </summary>
        private static int ExtractBust(Transform bustRoot, string outDir)
        {
            // The real bust art lives on the bust GO's first child —
            // MBase1 / D1Base / etc. The bust GO itself only carries a
            // "CharacterBox" placeholder sprite, which is never the art.
            // (SMSAndroids reaches the body the same way, via
            // newBust.transform.GetChild(0).)
            if (bustRoot.childCount == 0) return 0;
            Transform body = bustRoot.GetChild(0);

            // The base sprite is the body GO's own renderer; fall back to
            // the first renderer in its subtree if it carries none.
            SpriteRenderer baseRenderer = body.GetComponent<SpriteRenderer>();
            if (baseRenderer == null || baseRenderer.sprite == null ||
                baseRenderer.sprite.texture == null)
            {
                baseRenderer = null;
                foreach (var sr in body.GetComponentsInChildren<SpriteRenderer>(true))
                    if (sr != null && sr.sprite != null && sr.sprite.texture != null)
                    { baseRenderer = sr; break; }
            }

            var infos = new List<RendererInfo>();
            RendererInfo baseInfo = null;

            // Base + overlays: every sprite in the body subtree (including
            // inactive ones — blink / mouth / expression are toggled off in
            // the saved scene).
            foreach (var sr in body.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null || sr.sprite.texture == null) continue;
                Role role = (sr == baseRenderer) ? Role.Base : ClassifyOverlay(sr.transform, body);
                var info = new RendererInfo
                {
                    Renderer = sr,
                    Path = TransformPath(sr.transform, bustRoot),
                    Role = role,
                };
                if (role == Role.Base) baseInfo = info;
                infos.Add(info);
            }

            // Anything outside the body subtree — the CharacterBox on the
            // bust root, stray children — is kept under _extra/ so nothing
            // is silently lost.
            foreach (var sr in bustRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null || sr.sprite.texture == null) continue;
                if (IsUnder(sr.transform, body)) continue;
                infos.Add(new RendererInfo
                {
                    Renderer = sr,
                    Path = TransformPath(sr.transform, bustRoot),
                    Role = Role.Extra,
                });
            }
            if (infos.Count == 0) return 0;

            Directory.CreateDirectory(outDir);

            int written = 0;
            int mouthSeq = 0;
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var contents = new StringBuilder();
            contents.AppendLine("# SMSModForge bust art — " + bustRoot.name);
            contents.AppendLine("# file <- sourcePath | sprite | WxH");

            // Stable ordering: body first, then by hierarchy path.
            infos.Sort((a, b) =>
            {
                if (a == baseInfo) return -1;
                if (b == baseInfo) return 1;
                return string.CompareOrdinal(a.Path, b.Path);
            });

            foreach (var info in infos)
            {
                string fileName;
                switch (info.Role)
                {
                    case Role.Base:
                        fileName = "Base.PNG";
                        break;
                    case Role.Blink:
                        fileName = UniqueName("Blink", usedNames) + ".PNG";
                        break;
                    case Role.Mouth:
                        mouthSeq++;
                        int idx = NumericName(info.Renderer.transform.name, mouthSeq);
                        fileName = UniqueName("Mouth" + idx, usedNames) + ".PNG";
                        break;
                    case Role.Expression:
                        fileName = UniqueName("Expression" + Sanitize(info.Renderer.transform.name),
                                              usedNames) + ".PNG";
                        break;
                    default:
                        fileName = Path.Combine("_extra", Sanitize(info.Path) + ".PNG");
                        break;
                }

                string outPath = Path.Combine(outDir, fileName);
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var sprite = info.Renderer.sprite;
                // Overlays (Blink/Mouth/Expression) are composited onto a
                // canvas sized to the base sprite's texture. Base and _extra
                // sprites use their own texture as the canvas.
                SpriteRenderer baseSR =
                    (info.Role != Role.Base && info.Role != Role.Extra && baseInfo != null)
                    ? baseInfo.Renderer : null;
                if (TryDumpSprite(info.Renderer, outPath, baseSR))
                {
                    written++;
                    int outW, outH;
                    if (baseSR != null && baseSR.sprite != null && baseSR.sprite.texture != null)
                    { outW = baseSR.sprite.texture.width; outH = baseSR.sprite.texture.height; }
                    else
                    { outW = sprite.texture.width; outH = sprite.texture.height; }
                    contents.AppendLine(fileName.Replace('\\', '/') + "  <-  " + info.Path +
                                        "  |  " + sprite.name + "  |  " +
                                        outW + "x" + outH);
                }
            }

            // ── Jiggle mask ─────────────────────────────────────────────
            //
            // Not a sprite, which is why it was never picked up by the sweep
            // above: it is a TEXTURE bound to the body's material as _MaskTex,
            // and it is what tells the shader where the bust may deform. The
            // preview can blink and mouth a vanilla bust without it, but it
            // cannot jiggle one, which is the last thing missing.
            //
            // Taken from the BODY renderer specifically — overlays either share
            // that material or carry a plain one, so any other renderer would
            // give the same texture or none.
            if (baseInfo != null && baseInfo.Renderer != null)
            {
                var mat = baseInfo.Renderer.sharedMaterial;
                if (mat != null && mat.HasProperty(MaskTexProperty))
                {
                    var maskTex = mat.GetTexture(MaskTexProperty) as Texture2D;
                    if (maskTex != null && TryDumpTexture(maskTex, Path.Combine(outDir, "Mask.PNG")))
                    {
                        written++;
                        contents.AppendLine("Mask.PNG  <-  " + baseInfo.Path + " [material " +
                                            mat.name + "]  |  " + maskTex.name + "  |  " +
                                            maskTex.width + "x" + maskTex.height);
                    }

                    // The uniforms alongside it. A mask says WHERE a bust may
                    // deform; these say how fast and how far. Without them a
                    // borrowed bust would jiggle to whatever defaults the
                    // editor happens to hold, which is motion the game never
                    // gives it — a subtler kind of wrong than not moving.
                    WriteJiggleSettings(mat, Path.Combine(outDir, "Jiggle.txt"));
                }
            }

            File.WriteAllText(Path.Combine(outDir, "_contents.txt"), contents.ToString());
            return written;
        }

        private static readonly int MaskTexProperty = Shader.PropertyToID("_MaskTex");

        /// <summary>
        /// Dump the body material's jiggle uniforms as <c>key=value</c> lines.
        /// <para/>
        /// Plain text rather than JSON to match <c>_contents.txt</c> beside it,
        /// and because this is a flat list of floats that a human may well want
        /// to read. Names are the shader's, minus the leading underscore, so
        /// they line up one-to-one with the editor's JiggleParams. A property
        /// the material does not carry is skipped rather than guessed, leaving
        /// the editor's own default to stand.
        /// </summary>
        private static void WriteJiggleSettings(Material mat, string outPath)
        {
            var names = new[]
            {
                "_JiggleSpeed", "_JiggleStrength", "_JiggleFrequency",
                "_NoiseScale", "_NoiseSpeed", "_NoiseStrength",
            };
            var sb = new StringBuilder();
            sb.AppendLine("# SMSModForge jiggle uniforms <- material " + mat.name);
            foreach (var n in names)
                if (mat.HasProperty(n))
                    sb.AppendLine(n.Substring(1) + "=" +
                                  mat.GetFloat(n).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (mat.HasProperty("_Color"))
            {
                var c = mat.GetColor("_Color");
                sb.AppendLine("Tint=#" +
                    Mathf.RoundToInt(c.r * 255).ToString("X2") +
                    Mathf.RoundToInt(c.g * 255).ToString("X2") +
                    Mathf.RoundToInt(c.b * 255).ToString("X2") +
                    Mathf.RoundToInt(c.a * 255).ToString("X2"));
            }
            try { File.WriteAllText(outPath, sb.ToString()); }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[SMSModForge] Could not write " + outPath + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Write a plain texture out whole, with no sprite rect or compositing.
        /// <para/>
        /// Separate from <c>TryDumpSprite</c> because a mask is not a sprite:
        /// there is no textureRect to crop to and no base canvas to place it
        /// on. It is read through the same blit, since a mask is no likelier
        /// than a sprite atlas to be marked readable.
        /// <para/>
        /// Written as straight (non-premultiplied) RGBA. The mask carries
        /// per-channel amounts rather than colour, so nothing here may treat
        /// its alpha as transparency.
        /// </summary>
        private static bool TryDumpTexture(Texture2D src, string outPath)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32,
                                                RenderTextureReadWrite.Linear);
            var prevActive = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                readable = new Texture2D(src.width, src.height, TextureFormat.ARGB32, mipChain: false);
                readable.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                readable.Apply();

                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(outPath, readable.EncodeToPNG());
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[SMSModForge] Could not export mask " + src.name + ": " + ex.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                // Fully qualified: `using System;` above makes a bare Object
                // ambiguous, which is why the sprite dump does the same.
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        // ── Classification ───────────────────────────────────────────────

        /// <summary>
        /// Classify an overlay sprite from its own name and its ancestor
        /// chain up to (and including) the body GO — case-insensitive
        /// substring match, so it is robust to body objects called
        /// <c>MBase1</c>, <c>D1Base</c>, etc. A sprite under the body with
        /// no blink / mouth / expression cue is treated as an extra rather
        /// than a competing base.
        /// </summary>
        private static Role ClassifyOverlay(Transform t, Transform body)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                string n = cur.name.ToLowerInvariant();
                if (n.Contains("blink")) return Role.Blink;
                if (n.Contains("mouth")) return Role.Mouth;
                if (n.Contains("expression")) return Role.Expression;
                if (cur == body) break;
            }
            return Role.Extra;
        }

        /// <summary>True if <paramref name="t"/> is <paramref name="ancestor"/> or a descendant of it.</summary>
        private static bool IsUnder(Transform t, Transform ancestor)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur == ancestor) return true;
            return false;
        }

        // ── Texture export ───────────────────────────────────────────────

        /// <summary>
        /// Write one sprite to a PNG on a correctly-sized canvas so that
        /// all layers (base, blink, mouth, expression) share the same
        /// pixel dimensions and composite by simple stacking.
        ///
        /// <para><b>How sprites are stored in this game:</b> each overlay
        /// (blink, mouth, expression) has its own dedicated texture file
        /// of the same dimensions as the base body texture (e.g. 256×256).
        /// The overlay art is drawn at the correct composite position
        /// within that texture — the rest is transparent. The
        /// <c>Sprite</c> asset references only the tight sub-rect that
        /// contains visible pixels (<see cref="Sprite.textureRect"/>),
        /// and a custom <see cref="Sprite.pivot"/> handles the rendering
        /// offset. So <c>textureRect.x / .y</c> already encode the
        /// correct canvas position.</para>
        ///
        /// <para><b>Non-atlas sprites (common path):</b> the overlay's
        /// source texture is the same size as the base's. We create a
        /// transparent canvas of that size and stamp the sprite's tight
        /// pixels at their <c>textureRect</c> position — which IS where
        /// the artist placed them.</para>
        ///
        /// <para><b>Atlas-packed sprites (fallback):</b> <c>textureRect</c>
        /// positions in an atlas have no spatial relationship to the
        /// base, so we fall back to pivot-based compositing — computing
        /// placement from the pivot delta + Transform offset.</para>
        /// </summary>
        /// <param name="baseSR">
        /// The base (body) <see cref="SpriteRenderer"/> — determines the
        /// canvas size and is needed for atlas pivot math. Pass
        /// <c>null</c> for the base sprite itself and for <c>_extra</c>
        /// sprites (they use their own source texture as the canvas).
        /// </param>
        private static bool TryDumpSprite(SpriteRenderer sr, string outPath,
                                           SpriteRenderer baseSR)
        {
            var sprite = sr.sprite;
            var src = sprite.texture;
            if (src == null) return false;

            // ── Canvas size ─────────────────────────────────────────
            // Overlays match the base texture's pixel dimensions;
            // base / _extra use their own source texture dimensions.
            int canvasW, canvasH;
            if (baseSR != null && baseSR.sprite != null && baseSR.sprite.texture != null)
            {
                canvasW = baseSR.sprite.texture.width;
                canvasH = baseSR.sprite.texture.height;
            }
            else
            {
                canvasW = src.width;
                canvasH = src.height;
            }
            if (canvasW <= 0 || canvasH <= 0) return false;

            // ── Tight rect in the source texture ────────────────────
            Rect tr = sprite.textureRect;
            int tx = Mathf.Clamp(Mathf.RoundToInt(tr.x), 0, Mathf.Max(0, src.width - 1));
            int ty = Mathf.Clamp(Mathf.RoundToInt(tr.y), 0, Mathf.Max(0, src.height - 1));
            int tw = Mathf.Clamp(Mathf.RoundToInt(tr.width), 1, src.width - tx);
            int th = Mathf.Clamp(Mathf.RoundToInt(tr.height), 1, src.height - ty);

            // ── Placement on the canvas ─────────────────────────────
            int placeX, placeY;
            if (baseSR == null ||
                (!sprite.packed && src.width == canvasW && src.height == canvasH))
            {
                // Non-atlas sprite whose texture matches the canvas —
                // textureRect.xy IS the correct position because the
                // artist drew the art there in the source PNG.
                placeX = tx;
                placeY = ty;
            }
            else
            {
                // Atlas-packed or mismatched-size overlay — derive
                // placement from pivot difference + Transform delta.
                float bPPU = baseSR.sprite.pixelsPerUnit;
                float oPPU = sprite.pixelsPerUnit;
                float s    = bPPU / Mathf.Max(oPPU, 0.001f);
                Vector3 bPos  = baseSR.transform.position;
                Vector3 oPos  = sr.transform.position;
                Vector2 bPiv  = baseSR.sprite.pivot;
                Vector2 oPiv  = sprite.pivot;
                Vector2 tro   = sprite.textureRectOffset;
                placeX = Mathf.RoundToInt(
                    (oPos.x - bPos.x) * bPPU + bPiv.x - oPiv.x * s + tro.x * s);
                placeY = Mathf.RoundToInt(
                    (oPos.y - bPos.y) * bPPU + bPiv.y - oPiv.y * s + tro.y * s);
            }

            // ── Blit source texture → RenderTexture → read pixels ───
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;

                readable = new Texture2D(canvasW, canvasH, TextureFormat.ARGB32, mipChain: false);

                // Fast path: tight rect fills the entire canvas.
                if (placeX == 0 && placeY == 0 && tw == canvasW && th == canvasH)
                {
                    readable.ReadPixels(new Rect(tx, ty, tw, th), 0, 0);
                }
                else
                {
                    // Clear to transparent, then stamp the tight pixels
                    // at the computed position (clamped to canvas bounds).
                    var clear = new Color32[canvasW * canvasH]; // (0,0,0,0)
                    readable.SetPixels32(clear);

                    int srcSkipX = Mathf.Max(0, -placeX);
                    int srcSkipY = Mathf.Max(0, -placeY);
                    int dstX     = Mathf.Max(0, placeX);
                    int dstY     = Mathf.Max(0, placeY);
                    int copyW    = Mathf.Min(tw - srcSkipX, canvasW - dstX);
                    int copyH    = Mathf.Min(th - srcSkipY, canvasH - dstY);
                    copyW = Mathf.Min(copyW, src.width  - (tx + srcSkipX));
                    copyH = Mathf.Min(copyH, src.height - (ty + srcSkipY));
                    if (copyW > 0 && copyH > 0)
                        readable.ReadPixels(
                            new Rect(tx + srcSkipX, ty + srcSkipY, copyW, copyH),
                            dstX, dstY);
                }
                readable.Apply();

                var bytes = readable.EncodeToPNG();
                if (bytes == null || bytes.Length == 0) return false;
                File.WriteAllBytes(outPath, bytes);
                return true;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static string TransformPath(Transform t, Transform root)
        {
            if (t == root) return t.name;
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent)
                parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        /// <summary>Parse a GameObject name as an integer, else use a fallback.</summary>
        private static int NumericName(string name, int fallback)
        {
            int n;
            return int.TryParse(name, out n) ? n : fallback;
        }

        private static string UniqueName(string baseName, HashSet<string> used)
        {
            string name = baseName;
            int n = 2;
            while (used.Contains(name)) name = baseName + "_" + n++;
            used.Add(name);
            return name;
        }

        /// <summary>Make a string safe to use as a file / folder name.</summary>
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append((char.IsLetterOrDigit(c) || c == '_' || c == '-' ||
                           c == '/' || c == '.') ? c : '_');
            return sb.ToString();
        }

        /// <summary>
        /// `GameObject.Find` only sees active GameObjects. Walk every root
        /// in the open scene (with includeInactive) so a disabled
        /// `2_Bust_Manager` is still found.
        /// </summary>
        private static GameObject FindInactiveByName(string name)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
            }
            return null;
        }
    }
}

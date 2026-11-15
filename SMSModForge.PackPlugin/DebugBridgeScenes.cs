using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The game's scenes, read for the editor's catalogue of them (1.9.0, extending the
    /// game's own scenes): every child of a <c>4_CG_Manager-*</c> root, with its art's
    /// sprite and sizes, its frame's and its background's, and the art itself written
    /// small. Debug only, like the rest of the bridge.
    /// <para/>
    /// One scene at a time and nothing kept: the art is cut out of its texture and halved
    /// on the graphics card down to the size asked for, and only that small copy is read
    /// back, written and destroyed before the next scene. A full-size readable copy of
    /// every texture (as <c>sprite-dump</c> keeps) would be gigabytes over 1335 scenes.
    /// A few scenes a frame, so the game keeps drawing.
    /// </summary>
    internal sealed partial class DebugBridge
    {
        /// <summary>
        /// game-scene &lt;path&gt;: one of the game's scenes as it stands now, for
        /// checking a pack's change to it (1.9.0) - shown or not, its art (sprite,
        /// whether the texture can be read, and its middle texel when it can: a
        /// pack's picture is read from a file and can be, the game's own cannot),
        /// the art object's scale, its frame, the signals its trigger raises and
        /// whether each is on, and its components by type.
        /// </summary>
        private void GameSceneState(string path)
        {
            var go = VanillaSceneExtensions.Find(path);
            if (go == null) { Say("error game-scene: no " + path); return; }
            var parts = new List<string> { "name=" + go.name, "active=" + go.activeSelf };
            var core = go.transform.Find("Core");
            var art = core == null ? null : core.Find("Art");
            var artSr = art == null ? null : art.GetComponent<SpriteRenderer>();
            var sprite = artSr == null ? null : artSr.sprite;
            parts.Add("art=" + (sprite == null ? "none" : sprite.name));
            if (sprite != null)
            {
                var tex = sprite.texture;
                parts.Add("artSize=" + N(sprite.bounds.size.x, "0.###") + "x" + N(sprite.bounds.size.y, "0.###"));
                parts.Add("readable=" + (tex != null && tex.isReadable));
                if (tex != null && tex.isReadable)
                {
                    var r = sprite.textureRect;
                    var c = tex.GetPixel((int)(r.x + r.width / 2), (int)(r.y + r.height / 2));
                    parts.Add("texel=" + Hex(c));
                }
            }
            if (art != null) parts.Add("artScale=" + N(art.localScale.x, "0.###"));
            var frameSr = core == null ? null : core.GetComponent<SpriteRenderer>();
            parts.Add("frame=" + (frameSr == null || frameSr.sprite == null ? "none" : frameSr.sprite.name
                                  + " " + N(frameSr.sprite.rect.width, "0") + "x" + N(frameSr.sprite.rect.height, "0")));
            var signals = new List<string>();
            foreach (var ins in VanillaSceneExtensions.InstructionsOf(go.GetComponent<GameCreator.Runtime.VisualScripting.Trigger>()))
                if (ins is GameCreator.Runtime.VisualScripting.InstructionLogicRaiseSignal)
                    signals.Add(VanillaSceneExtensions.SignalOf(ins) + (VanillaSceneExtensions.IsOn(ins) ? "" : " off"));
            parts.Add("signals=[" + string.Join(",", signals.ToArray()) + "]");
            parts.Add("components=" + string.Join(",", go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToArray()));
            Say("ok game-scene " + path + ": " + string.Join("; ", parts.ToArray()));
        }

        /// <summary>The signals listened for: a scene's sounds, and the screen
        /// transitions the game's scripts raise (which tell which of them ran).</summary>
        private static readonly string[] HeardNames =
            { "kiss", "flash", "Blink", "FadeInBlack", "FadeOutBlack", "FadeUI", "DialogueStart", "DialogueEnd" };

        /// <summary>The signals heard since the last <c>signals</c> command, each
        /// with the object that raised it.</summary>
        private sealed class SignalEar : GameCreator.Runtime.Common.ISignalReceiver
        {
            public readonly List<string> Heard = new List<string>();
            public void OnReceiveSignal(GameCreator.Runtime.Common.SignalArgs args)
            {
                string name = "?";
                foreach (var n in HeardNames)
                    if (args.signal == new PropertyName(n)) { name = n; break; }
                Heard.Add(name + "(" + (args.invoker == null ? "-" : args.invoker.name) + ")");
            }
        }

        private SignalEar _ear;

        /// <summary>signals: the kiss and flash signals raised since the last time
        /// this was asked, then forgets them. Listening starts at the first ask.</summary>
        private void HeardSignals()
        {
            if (_ear == null)
            {
                _ear = new SignalEar();
                foreach (var n in HeardNames)
                    GameCreator.Runtime.Common.Signals.Subscribe(_ear, new PropertyName(n));
                Say("ok signals (listening from now)");
                return;
            }
            Say("ok signals [" + string.Join(",", _ear.Heard.ToArray()) + "]");
            _ear.Heard.Clear();
        }

        /// <summary>scene-catalog [max side]: writes DebugBridge/&lt;run&gt;/scenes/scenes.tsv
        /// and one PNG per scene's art, then answers.</summary>
        private bool SceneCatalog(string arg)
        {
            int maxSide;
            if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxSide) || maxSide < 16) maxSide = 512;
            string into = Path.Combine(_dir, "scenes");
            if (Directory.Exists(into)) Directory.Delete(into, true);
            Directory.CreateDirectory(into);
            _waitingFor = "scene-catalog";
            _waitDeadline = Time.unscaledTime + 3600f;
            StartCoroutine(DumpScenes(into, maxSide));
            return true;
        }

        private IEnumerator DumpScenes(string into, int maxSide)
        {
            var table = new List<string>
            {
                "index\tpath\tartSprite\tartTexture\tartW\tartH\tartPpu\tartPivotX\tartPivotY\tartPosX\tartPosY\tartScaleX\tartScaleY"
                + "\tframeSprite\tframeW\tframeH\tframePpu\tcoreScaleX\tcoreScaleY\tbackSprite\tbackW\tbackH\trootSprite\tfile",
            };
            int index = 0, written = 0, failed = 0;
            var roots = SceneManager.GetActiveScene().GetRootGameObjects()
                .Where(g => g != null && g.name.StartsWith("4_CG_Manager-", StringComparison.Ordinal))
                .OrderBy(g => g.name, StringComparer.Ordinal).ToList();
            foreach (var root in roots)
            {
                for (int c = 0; c < root.transform.childCount; c++)
                {
                    var scene = root.transform.GetChild(c);
                    var core = scene.Find("Core");
                    var art = core == null ? null : core.Find("Art");
                    var back = core == null ? null : core.Find("Background");
                    var artSr = art == null ? null : art.GetComponent<SpriteRenderer>();
                    var frameSr = core == null ? null : core.GetComponent<SpriteRenderer>();
                    var backSr = back == null ? null : back.GetComponent<SpriteRenderer>();
                    var rootSr = scene.GetComponent<SpriteRenderer>();

                    string file = "";
                    if (artSr != null && artSr.sprite != null)
                    {
                        try
                        {
                            file = index.ToString(CultureInfo.InvariantCulture) + ".png";
                            WriteSmall(artSr.sprite, maxSide, Path.Combine(into, file));
                            written++;
                        }
                        catch (Exception ex)
                        {
                            file = "error: " + ex.Message.Replace('\t', ' ');
                            failed++;
                        }
                    }

                    var s = artSr != null ? artSr.sprite : null;
                    var f = frameSr != null ? frameSr.sprite : null;
                    var b = backSr != null ? backSr.sprite : null;
                    table.Add(string.Join("\t", new[]
                    {
                        index.ToString(CultureInfo.InvariantCulture), root.name + "/" + scene.name,
                        s != null ? s.name : "", s != null && s.texture != null ? s.texture.name : "",
                        N(s != null ? s.textureRect.width : 0), N(s != null ? s.textureRect.height : 0), N(s != null ? s.pixelsPerUnit : 0),
                        N(s != null ? s.pivot.x : 0), N(s != null ? s.pivot.y : 0),
                        N(art != null ? art.localPosition.x : 0), N(art != null ? art.localPosition.y : 0),
                        N(art != null ? art.localScale.x : 0), N(art != null ? art.localScale.y : 0),
                        f != null ? f.name : "", N(f != null ? f.textureRect.width : 0), N(f != null ? f.textureRect.height : 0),
                        N(f != null ? f.pixelsPerUnit : 0),
                        N(core != null ? core.localScale.x : 0), N(core != null ? core.localScale.y : 0),
                        b != null ? b.name : "", N(b != null ? b.textureRect.width : 0), N(b != null ? b.textureRect.height : 0),
                        rootSr != null && rootSr.sprite != null ? rootSr.sprite.name : "",
                        file,
                    }));
                    index++;
                    if (index % 4 == 0) yield return null;
                }
            }
            File.WriteAllLines(Path.Combine(into, "scenes.tsv"), table.ToArray());
            Say("ok scene-catalog " + index + " scenes, " + written + " pictures, " + failed + " failed -> " + into);
            _waitingFor = null;
        }

        private static string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>A sprite's own rectangle of its texture, no larger than
        /// <paramref name="maxSide"/>, as a PNG - halved on the graphics card, so a
        /// smoothing filter acts at every step and nothing full size is read back.</summary>
        private static void WriteSmall(Sprite sprite, int maxSide, string file)
        {
            var tex = sprite.texture;
            var r = sprite.textureRect;
            int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));
            var current = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            current.filterMode = FilterMode.Bilinear;
            Graphics.Blit(tex, current, new Vector2(r.width / tex.width, r.height / tex.height),
                          new Vector2(r.x / tex.width, r.y / tex.height));
            while (Mathf.Max(w, h) > maxSide)
            {
                int nw = Mathf.Max(1, w / 2), nh = Mathf.Max(1, h / 2);
                if (Mathf.Max(nw, nh) < maxSide)
                {
                    float k = (float)maxSide / Mathf.Max(w, h);
                    nw = Mathf.Max(1, Mathf.RoundToInt(w * k));
                    nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
                }
                var next = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                next.filterMode = FilterMode.Bilinear;
                Graphics.Blit(current, next);
                RenderTexture.ReleaseTemporary(current);
                current = next;
                w = nw;
                h = nh;
            }
            var before = RenderTexture.active;
            RenderTexture.active = current;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(current);
            try { File.WriteAllBytes(file, ImageConversion.EncodeToPNG(read)); }
            finally { UnityEngine.Object.Destroy(read); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The bridge's commands for the game's effects (1.10.0's FX research: which of the game's effects can be a
    /// template, what each one looks like, and what an author would set on it).
    /// <list type="bullet">
    /// <item><c>fx-survey</c>: every particle system in the scene, on or off, one line each - where, the texture
    /// it draws, its shader, whether it loops - to a file beside the results.</item>
    /// <item><c>particles &lt;path&gt;</c>: every setting of every particle system on the object and under it, to
    /// a file - what an author would change on one made from it.</item>
    /// <item><c>fx-show &lt;path&gt;</c>: a copy of the object played in the middle of the screen, wherever the
    /// original sits (most are under levels nobody is in) - to see it; <c>fx-clear</c> takes the copies away.</item>
    /// <item><c>png-save &lt;texture&gt; &lt;file&gt;</c>: a loaded texture as a picture, to look at.</item>
    /// </list>
    /// Debug builds only, like the rest of the bridge; nothing here changes the game's own objects.
    /// </summary>
    internal sealed partial class DebugBridge
    {
        private readonly List<GameObject> _fxShown = new List<GameObject>();
        private PackContext _fxContext;

        private bool RunFx(string verb, string a, string rest)
        {
            string all = (a + " " + rest).Trim();
            switch (verb)
            {
                case "fx-survey":
                {
                    var lines = new List<string>();
                    foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
                    {
                        if (ps == null || !ps.gameObject.scene.IsValid()) continue;
                        var r = ps.GetComponent<ParticleSystemRenderer>();
                        var main = ps.main;
                        lines.Add(FullPath(ps.transform) + "\t" + (ps.gameObject.activeInHierarchy ? "on" : "off")
                                  + "\tloop=" + main.loop + "\tmax=" + main.maxParticles
                                  + "\tlayer=" + (r != null ? r.sortingLayerName + "/" + r.sortingOrder : "-")
                                  + "\t" + MaterialSummary(r != null ? r.sharedMaterial : null));
                    }
                    lines.Sort(StringComparer.Ordinal);
                    string file = Path.Combine(_dir, "fx-survey.txt");
                    File.WriteAllLines(file, lines.ToArray());
                    Say("ok fx-survey " + lines.Count + " particle systems, " + file);
                    return true;
                }
                case "particles":
                {
                    var target = FindPath(all);
                    if (target == null) { Say("error particles: no " + all); return true; }
                    var w = new StringBuilder();
                    foreach (var ps in target.GetComponentsInChildren<ParticleSystem>(true))
                        Describe(w, ps);
                    string label = new string(target.name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                    string file = Path.Combine(_dir, "particles-" + label + ".txt");
                    File.WriteAllText(file, w.ToString());
                    Say("ok particles " + file);
                    return true;
                }
                case "fx-show":
                case "fx-show-centre":
                {
                    var source = FindPath(all);
                    if (source == null) { Say("error fx-show: no " + all); return true; }
                    var cam = Camera.main;
                    if (cam == null) { Say("error fx-show: no camera"); return true; }
                    // At the root, at the scale it has where it is - and where it is (every level shares one place,
                    // so an effect of another level's is where it would be there: rain above the top, falling in),
                    // or, fx-show-centre, in the middle of the view.
                    var copy = UnityEngine.Object.Instantiate(source);
                    copy.name = "SMSModForge fx-show " + source.name;
                    copy.transform.SetParent(null, false);
                    copy.transform.localScale = source.transform.lossyScale;
                    copy.transform.rotation = source.transform.rotation;
                    var at = verb == "fx-show" ? source.transform.position : cam.transform.position;
                    copy.transform.position = new Vector3(at.x, at.y, source.transform.position.z);
                    copy.SetActive(true);
                    foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        ps.gameObject.SetActive(true);
                        ps.Clear(false);
                        ps.Play(false);
                    }
                    _fxShown.Add(copy);
                    Say("ok fx-show " + FullPath(source.transform) + " at " + N(at.x, "0.##") + "," + N(at.y, "0.##")
                        + " scale " + N(source.transform.lossyScale.x, "0.###"));
                    return true;
                }
                case "fx-state":
                {
                    // fx-state: each copy fx-show made - still there, on, and each of its systems playing, how many
                    // particles, where, drawn on which layer, and the scripts it carries.
                    var parts = new List<string>();
                    foreach (var go in _fxShown)
                    {
                        if (go == null) { parts.Add("(destroyed)"); continue; }
                        var scripts = go.GetComponentsInChildren<MonoBehaviour>(true).Where(b => b != null)
                                        .Select(b => b.GetType().Name).Distinct().ToArray();
                        parts.Add(go.name + " active=" + go.activeInHierarchy + " at " + V3(go.transform.position)
                                  + " scripts=[" + string.Join(",", scripts) + "]");
                        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var r = ps.GetComponent<ParticleSystemRenderer>();
                            parts.Add("  " + ps.name + " on=" + ps.gameObject.activeInHierarchy + " playing=" + ps.isPlaying
                                      + " n=" + ps.particleCount + " time=" + N(ps.time, "0.##")
                                      + (r != null ? " visible=" + r.isVisible + " layer=" + r.sortingLayerName + "/" + r.sortingOrder
                                                      + " bounds=" + V3(r.bounds.center) + "~" + V3(r.bounds.size) : ""));
                        }
                    }
                    Say("ok fx-state " + (parts.Count == 0 ? "(nothing shown)" : string.Join(" | ", parts.ToArray())));
                    return true;
                }
                case "fx-sort":
                {
                    // fx-sort <layer> <order>: every copy fx-show made drawn on this sorting layer, at this order -
                    // in front of the room, or behind its characters.
                    int order;
                    if (!int.TryParse(rest.Trim(), out order)) { Say("error fx-sort: fx-sort <layer> <order>"); return true; }
                    int n = 0;
                    foreach (var go in _fxShown)
                        if (go != null)
                            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.sortingLayerName = a; r.sortingOrder = order; n++; }
                    Say("ok fx-sort " + n + " renderers on " + a + "/" + order);
                    return true;
                }
                case "fx-alive":
                {
                    // fx-alive <path>: how many particles are out under the object, on or off.
                    var target = FindPath(all);
                    if (target == null) { Say("error fx-alive: no " + all); return true; }
                    int n = target.GetComponentsInChildren<ParticleSystem>(true).Sum(ps => ps.particleCount);
                    Say("ok fx-alive " + n);
                    return true;
                }
                case "fx-layers":
                {
                    // fx-layers: the sorting layers, back to front - where an effect can be drawn - and what is
                    // drawn on each now (renderers switched on: how many, the orders they use), and every canvas
                    // switched on: how it is drawn, on which layer, at which order.
                    var parts = new List<string> { string.Join(", ", SortingLayer.layers.Select(l => l.name + "(" + l.value + ")").ToArray()) };
                    foreach (var g in Resources.FindObjectsOfTypeAll<Renderer>()
                                 .Where(r => r != null && r.gameObject.scene.IsValid() && r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer))
                                 .GroupBy(r => r.sortingLayerName))
                    {
                        var orders = g.Select(r => r.sortingOrder).ToList();
                        var sample = g.OrderByDescending(r => r.sortingOrder).Take(3).Select(r => FullPath(r.transform) + "@" + r.sortingOrder);
                        parts.Add("layer " + g.Key + ": " + orders.Count + " renderers, orders " + orders.Min() + ".." + orders.Max()
                                  + " top: " + string.Join("; ", sample.ToArray()));
                    }
                    foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>()
                                 .Where(c => c != null && c.gameObject.scene.IsValid() && c.isRootCanvas && c.gameObject.activeInHierarchy))
                        parts.Add("canvas " + FullPath(c.transform) + " " + c.renderMode + " layer=" + c.sortingLayerName + "/" + c.sortingOrder
                                  + " plane=" + N(c.planeDistance, "0.##") + " camera=" + (c.worldCamera != null ? c.worldCamera.name : "-"));
                    var cam = Camera.main;
                    if (cam != null)
                        parts.Add("camera " + cam.name + " ortho=" + cam.orthographic + " size=" + N(cam.orthographicSize, "0.###")
                                  + " at " + V3(cam.transform.position) + " near=" + N(cam.nearClipPlane, "0.##") + " far=" + N(cam.farClipPlane, "0.##"));
                    Say("ok fx-layers " + string.Join(" | ", parts.ToArray()));
                    return true;
                }
                case "fx-play":
                {
                    // fx-play <effect as the manifest writes it, JSON>: played through the pack runtime itself, as if
                    // a pack named "bridge" had it - the FX tab's templates tried without making a pack.
                    Newtonsoft.Json.Linq.JObject def;
                    try { def = Newtonsoft.Json.Linq.JObject.Parse(all); }
                    catch (Exception ex) { Say("error fx-play: " + ex.Message); return true; }
                    if (_fxContext == null)
                        _fxContext = new PackContext { PackId = "bridge", Log = Plugin.Log, Effects = new EffectsRegistry() };
                    string key = (string)def[SMSModForge.Shared.Effects.KeyField] ?? "test";
                    def[SMSModForge.Shared.Effects.KeyField] = key;
                    var template = SMSModForge.Shared.Effects.Find((string)def[SMSModForge.Shared.Effects.TemplateField]);
                    if (template == null) { Say("error fx-play: no template " + (string)def[SMSModForge.Shared.Effects.TemplateField]); return true; }
                    _fxContext.Effects.ByKey[key] = new EffectsRegistry.Entry { Key = key, Def = def, Template = template };
                    bool played = EffectRuntime.Play(_fxContext, key);
                    Say((played ? "ok" : "error") + " fx-play " + key + " " + template.Id);
                    return true;
                }
                case "fx-tick":
                {
                    // fx-tick: the bridge's effects swept (what the plugin's Tick does for a pack's).
                    if (_fxContext != null) EffectRuntime.Tick(_fxContext);
                    int live = _fxContext == null ? 0 : _fxContext.Effects.ByKey.Values.Sum(en => en.Live.Count);
                    Say("ok fx-tick live=" + live);
                    return true;
                }
                case "fx-clear":
                {
                    int n = 0;
                    foreach (var go in _fxShown)
                        if (go != null) { UnityEngine.Object.Destroy(go); n++; }
                    _fxShown.Clear();
                    // ...and what fx-play played, still going or not.
                    if (_fxContext != null)
                        foreach (var en in _fxContext.Effects.ByKey.Values)
                        {
                            foreach (var p in en.Live)
                                if (p.Root != null) { UnityEngine.Object.Destroy(p.Root); n++; }
                            en.Live.Clear();
                        }
                    Say("ok fx-clear " + n);
                    return true;
                }
                case "particles-live":
                {
                    // particles-live <path>: every live particle under the object - how old, where (as the system
                    // keeps it, and in the world with the system's own scale only or with its parents' too), how
                    // big, what colour - to a file. What the editor's wet drops are held to (2026-11-27).
                    var target = FindPath(all);
                    if (target == null) { Say("error particles-live: no " + all); return true; }
                    var cam = Camera.main;
                    var w = new StringBuilder();
                    if (cam != null)
                        w.AppendLine("camera " + V3p(cam.transform.position) + " ortho " + N(cam.orthographicSize, "0.#####")
                                     + " pixels " + cam.pixelWidth + "x" + cam.pixelHeight + " screen " + Screen.width + "x" + Screen.height);
                    w.AppendLine("gravity " + V3p(Gravity()));
                    int total = 0;
                    foreach (var ps in target.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = ps.main;
                        var buf = new ParticleSystem.Particle[Math.Max(1, main.maxParticles)];
                        int n = ps.GetParticles(buf);
                        total += n;
                        var tr = ps.transform;
                        bool world = main.simulationSpace == ParticleSystemSimulationSpace.World;
                        w.AppendLine("== " + FullPath(tr) + " n=" + n + " playing=" + ps.isPlaying + " paused=" + ps.isPaused
                                     + " time=" + N(ps.time, "0.####") + " at " + V3p(tr.position) + " rot " + V3p(tr.eulerAngles)
                                     + " scale " + V3p(tr.localScale) + " lossy " + V3p(tr.lossyScale)
                                     + " space=" + main.simulationSpace + " scaling=" + main.scalingMode
                                     + " shape " + V3p(ps.shape.scale));
                        for (int i = 0; i < n; i++)
                        {
                            var p = buf[i];
                            Vector3 own = world ? p.position : tr.position + tr.rotation * Vector3.Scale(tr.localScale, p.position);
                            Vector3 hier = world ? p.position : tr.TransformPoint(p.position);
                            w.AppendLine("p age=" + N(p.startLifetime - p.remainingLifetime, "0.#####") + " life=" + N(p.startLifetime, "0.###")
                                         + " pos=" + V3p(p.position) + " worldOwn=" + V3p(own) + " worldHier=" + V3p(hier)
                                         + " size=" + V3p(p.GetCurrentSize3D(ps)) + " start=" + V3p(p.startSize3D)
                                         + " colour=" + Rgba(p.GetCurrentColor(ps)) + " vel=" + V3p(p.velocity)
                                         + " rot=" + N(p.rotation, "0.##") + " seed=" + p.randomSeed);
                        }
                    }
                    string label = new string(target.name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                    string file = Path.Combine(_dir, "particles-live-" + label + ".txt");
                    File.WriteAllText(file, w.ToString());
                    Say("ok particles-live " + total + " " + file);
                    return true;
                }
                case "particles-pause":
                case "particles-play":
                {
                    // Every system under the object held where it is (to photograph its particles and read them at
                    // the same moment), or let go again.
                    var target = FindPath(all);
                    if (target == null) { Say("error " + verb + ": no " + all); return true; }
                    int n = 0;
                    foreach (var ps in target.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (verb == "particles-pause") ps.Pause(false); else ps.Play(false);
                        n++;
                    }
                    Say("ok " + verb + " " + n);
                    return true;
                }
                case "material":
                {
                    // material <path>: the object's sprite renderer's material - its name, shader, the mask it reads
                    // (_MaskTex) - and where it is drawn on the screen (pixels, rows from the top): how an object's
                    // mask is set up in the game (after 1.10.0, objects' masks).
                    var target = FindPath(all);
                    var sr = target != null ? target.GetComponent<SpriteRenderer>() : null;
                    if (sr == null) { Say("error material: no sprite renderer at " + all); return true; }
                    var m = sr.sharedMaterial;
                    string mask = m == null || !m.HasProperty("_MaskTex") ? "(no _MaskTex)"
                                : m.GetTexture("_MaskTex") is Texture t && t != null ? t.name + " " + t.width + "x" + t.height : "(none bound)";
                    var cam = Camera.main;
                    string rect = "";
                    if (cam != null)
                    {
                        var b = sr.bounds;
                        var lo = cam.WorldToViewportPoint(b.min);
                        var hi = cam.WorldToViewportPoint(b.max);
                        rect = " screen " + N(lo.x * Screen.width, "0") + "," + N((1 - hi.y) * Screen.height, "0") + " to "
                               + N(hi.x * Screen.width, "0") + "," + N((1 - lo.y) * Screen.height, "0");
                    }
                    Say("ok material " + FullPath(target.transform) + ": " + (m != null ? m.name : "(none)") + " shader="
                        + (m != null && m.shader != null ? m.shader.name : "?") + " mask=" + mask + rect);
                    return true;
                }
                case "legacy-text":
                {
                    // legacy-text <path>: every old-style UI Text under the object (the HUD's money and day are Game
                    // Creator's TextPropertyString, one of them) - its words and how they are set: font, size, style,
                    // alignment, best fit, line spacing, colour, rectangle, whether shown.
                    var target = FindPath(all);
                    if (target == null) { Say("error legacy-text: no " + all); return true; }
                    var found = new List<string>();
                    foreach (var t in target.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                    {
                        var rt = t.rectTransform;
                        found.Add(FullPath(t.transform) + " [" + t.GetType().Name + (t.gameObject.activeInHierarchy ? "" : ", off") + "] '"
                                  + t.text.Replace("\n", "\\n") + "' font=" + (t.font != null ? t.font.name : "(none)") + " size=" + t.fontSize
                                  + " style=" + t.fontStyle + " align=" + t.alignment + " bestFit=" + t.resizeTextForBestFit
                                  + (t.resizeTextForBestFit ? "(" + t.resizeTextMinSize + ".." + t.resizeTextMaxSize + ")" : "")
                                  + " line=" + N(t.lineSpacing, "0.##") + " colour=" + Rgba(t.color) + " rect " + N(rt.rect.width, "0.##") + "x"
                                  + N(rt.rect.height, "0.##") + " anchored " + N(rt.anchoredPosition.x, "0.##") + "," + N(rt.anchoredPosition.y, "0.##")
                                  + " scale " + N(rt.localScale.x, "0.##") + " richText=" + t.supportRichText);
                    }
                    Say("ok legacy-text " + found.Count + ": " + string.Join(" | ", found.ToArray()));
                    return true;
                }
                case "stat":
                {
                    // stat <attribute> [value]: one of the player's Game Creator Stats attributes (11_PlayerData's
                    // Traits), read - or set, then read back. The navigator's pink bar is PhotoXPCurrent.
                    var player = FindPath("11_PlayerData");
                    var traits = player == null ? null
                        : player.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "Traits");
                    if (traits == null) { Say("error stat: no Traits on 11_PlayerData"); return true; }
                    var attributes = traits.GetType().GetProperty("RuntimeAttributes")?.GetValue(traits, null);
                    var get = attributes?.GetType().GetMethod("Get", new[] { typeof(string) });
                    // By its id, or by its asset's name (what AttributeUI shows: PhotoXPCurrent), whose id is asked.
                    string id = a;
                    var asset = Resources.FindObjectsOfTypeAll<ScriptableObject>()
                        .FirstOrDefault(o => o != null && o.name == a && o.GetType().Name == "Attribute");
                    var idValue = asset?.GetType().GetProperty("ID")?.GetValue(asset, null);
                    if (idValue != null)
                        id = (idValue.GetType().GetProperty("String")?.GetValue(idValue, null) as string) ?? idValue.ToString();
                    object data;
                    try { data = get?.Invoke(attributes, new object[] { id }); }
                    catch (Exception e) { Say("error stat: " + (e.InnerException ?? e).Message + " (id '" + id + "')"); return true; }
                    if (data == null) { Say("error stat: no attribute " + a); return true; }
                    if (rest.Trim().Length > 0)
                    {
                        var value = data.GetType().GetProperty("Value");
                        if (value == null || !value.CanWrite) { Say("error stat: " + a + " has no Value to set"); return true; }
                        value.SetValue(data, Convert.ChangeType(rest.Trim(), value.PropertyType, CultureInfo.InvariantCulture), null);
                    }
                    var shown = data.GetType().GetProperties()
                        .Where(p => p.GetIndexParameters().Length == 0 && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string)))
                        .Select(p => { object v; try { v = p.GetValue(data, null); } catch (Exception) { v = "?"; } return p.Name + "=" + Show(v); });
                    Say("ok stat " + a + ": " + string.Join("; ", shown.ToArray()));
                    return true;
                }
                case "png-save":
                {
                    // png-save <texture name, spaces and all> <file>
                    int last = all.LastIndexOf(' ');
                    string wanted = last > 0 ? all.Substring(0, last) : all;
                    var tex = Resources.FindObjectsOfTypeAll<Texture>().FirstOrDefault(x => x != null && x.name == wanted);
                    if (tex == null) { Say("error png-save: no texture " + wanted); return true; }
                    var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(tex, rt);
                    var before = RenderTexture.active;
                    RenderTexture.active = rt;
                    var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                    read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    read.Apply();
                    RenderTexture.active = before;
                    RenderTexture.ReleaseTemporary(rt);
                    byte[] png = read.EncodeToPNG();
                    UnityEngine.Object.Destroy(read);
                    string file = Path.Combine(_dir, "shots", all.Substring(last + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, png);
                    Say("ok png-save " + tex.name + " " + tex.width + "x" + tex.height + " " + tex.graphicsFormat + " " + file);
                    return true;
                }
                case "fx-icon":
                {
                    // fx-icon <attractor path> <file>: the game's points icon (the attractor's particle prefab's
                    // picture), its sprite cut out of its texture, as a picture - and its colour and size.
                    int cut = all.LastIndexOf(' ');
                    var target = cut > 0 ? FindPath(all.Substring(0, cut)) : null;
                    var attractor = target != null ? target.GetComponent<ExplosiveUIParticleAttractor>() : null;
                    var image = attractor != null && attractor.particlePrefab != null ? attractor.particlePrefab.GetComponentInChildren<UnityEngine.UI.Image>(true) : null;
                    if (image == null || image.sprite == null) { Say("error fx-icon: no icon at " + all); return true; }
                    var sprite = image.sprite;
                    var tex = sprite.texture;
                    var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(tex, rt);
                    var before = RenderTexture.active;
                    RenderTexture.active = rt;
                    var r = sprite.textureRect;
                    var read = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                    read.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
                    read.Apply();
                    RenderTexture.active = before;
                    RenderTexture.ReleaseTemporary(rt);
                    string file = Path.Combine(_dir, "shots", all.Substring(cut + 1));
                    File.WriteAllBytes(file, read.EncodeToPNG());
                    UnityEngine.Object.Destroy(read);
                    Say("ok fx-icon " + sprite.name + " of " + tex.name + " " + r.width + "x" + r.height + " colour " + Rgba(image.color)
                        + " size " + V3(image.rectTransform.sizeDelta) + " scale " + V3(attractor.particlePrefab.localScale) + " " + file);
                    return true;
                }
            }
            return false;
        }

        private static string MaterialSummary(Material m)
        {
            if (m == null) return "material=(none)";
            var s = new StringBuilder("material=" + m.name + " shader=" + (m.shader != null ? m.shader.name : "?"));
            foreach (var prop in m.GetTexturePropertyNames())
            {
                Texture t;
                try { t = m.GetTexture(prop); } catch (Exception) { continue; }
                if (t != null) s.Append(" " + prop + "=" + t.name + "(" + t.width + "x" + t.height + ")");
            }
            foreach (var prop in new[] { "_Color", "_BaseColor", "_TintColor" })
                if (m.HasProperty(prop)) s.Append(" " + prop + "=" + Rgba(m.GetColor(prop)));
            return s.ToString();
        }

        /// <summary>The 3D physics' gravity, which particles fall by (their gravity modifier times it) - read through
        /// reflection, the physics module not being one the plugin is built against.</summary>
        private static Vector3 Gravity()
        {
            var physics = Type.GetType("UnityEngine.Physics, UnityEngine.PhysicsModule");
            var g = physics?.GetProperty("gravity", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            return g != null ? (Vector3)g.GetValue(null, null) : new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static string V3p(Vector3 v) => N(v.x, "0.#####") + "," + N(v.y, "0.#####") + "," + N(v.z, "0.#####");

        /// <summary>How a material blends and what it switches on: what decides how a particle is laid over what is
        /// behind it.</summary>
        private static string MaterialDetail(Material m)
        {
            if (m == null) return "";
            var s = new StringBuilder(" queue=" + m.renderQueue + " keywords=[" + string.Join(",", m.shaderKeywords) + "]");
            foreach (var prop in new[] { "_Mode", "_Surface", "_Blend", "_BlendOp", "_SrcBlend", "_DstBlend", "_ZWrite", "_Cull",
                                         "_ColorMode", "_SoftParticlesEnabled", "_CameraFadingEnabled", "_DistortionEnabled",
                                         "_FlipbookMode", "_LightingEnabled", "_EmissionEnabled", "_Cutoff" })
                if (m.HasProperty(prop)) s.Append(" " + prop + "=" + N(m.GetFloat(prop), "0.###"));
            if (m.shader != null) s.Append(" passes=" + m.passCount + " tag=" + m.GetTag("RenderType", false, "-"));
            return s.ToString();
        }

        private static void Describe(StringBuilder w, ParticleSystem ps)
        {
            var main = ps.main;
            w.AppendLine("== " + FullPath(ps.transform) + (ps.gameObject.activeInHierarchy ? "" : " (off)"));
            if (main.startSize3D)
                w.AppendLine("  size3D x=" + Curve(main.startSizeX) + " y=" + Curve(main.startSizeY) + " z=" + Curve(main.startSizeZ)
                             + " gravity " + V3p(Gravity()) + " seed=" + (ps.useAutoRandomSeed ? "auto" : ps.randomSeed.ToString()));
            else
                w.AppendLine("  gravity " + V3p(Gravity()) + " seed=" + (ps.useAutoRandomSeed ? "auto" : ps.randomSeed.ToString()));
            w.AppendLine("  transform local " + V3(ps.transform.localPosition) + " scale " + V3(ps.transform.localScale)
                         + " lossy " + V3(ps.transform.lossyScale) + " rot " + V3(ps.transform.localEulerAngles));
            w.AppendLine("  main duration=" + N(main.duration, "0.###") + " loop=" + main.loop + " prewarm=" + main.prewarm
                         + " delay=" + Curve(main.startDelay) + " lifetime=" + Curve(main.startLifetime)
                         + " speed=" + Curve(main.startSpeed) + " size=" + Curve(main.startSize)
                         + (main.startSize3D ? " size3D" : "") + " rotation=" + Curve(main.startRotation)
                         + " colour=" + Gradient(main.startColor) + " gravity=" + Curve(main.gravityModifier)
                         + " space=" + main.simulationSpace + " simSpeed=" + N(main.simulationSpeed, "0.###")
                         + " scaling=" + main.scalingMode + " playOnAwake=" + main.playOnAwake
                         + " max=" + main.maxParticles + " stop=" + main.stopAction);
            var em = ps.emission;
            var bursts = new List<string>();
            for (int i = 0; i < em.burstCount; i++)
            {
                var b = em.GetBurst(i);
                bursts.Add("t" + N(b.time, "0.###") + " n" + Curve(b.count) + " x" + b.cycleCount + " every" + N(b.repeatInterval, "0.###")
                           + " p" + N(b.probability, "0.##"));
            }
            w.AppendLine("  emission " + On(em.enabled) + " rate=" + Curve(em.rateOverTime) + " perDistance=" + Curve(em.rateOverDistance)
                         + " bursts=[" + string.Join("; ", bursts.ToArray()) + "]");
            var sh = ps.shape;
            w.AppendLine("  shape " + On(sh.enabled) + " " + sh.shapeType + " radius=" + N(sh.radius, "0.###")
                         + " thickness=" + N(sh.radiusThickness, "0.###") + " angle=" + N(sh.angle, "0.#") + " arc=" + N(sh.arc, "0.#")
                         + " pos=" + V3(sh.position) + " rot=" + V3(sh.rotation) + " scale=" + V3(sh.scale)
                         + (sh.texture != null ? " texture=" + sh.texture.name : "") + (sh.sprite != null ? " sprite=" + sh.sprite.name : ""));
            var vel = ps.velocityOverLifetime;
            if (vel.enabled)
                w.AppendLine("  velocity x=" + Curve(vel.x) + " y=" + Curve(vel.y) + " z=" + Curve(vel.z) + " space=" + vel.space
                             + " orbital=" + Curve(vel.orbitalZ) + " radial=" + Curve(vel.radial) + " speedMul=" + Curve(vel.speedModifier));
            var lim = ps.limitVelocityOverLifetime;
            if (lim.enabled) w.AppendLine("  limitVelocity limit=" + Curve(lim.limit) + " dampen=" + N(lim.dampen, "0.###") + " drag=" + Curve(lim.drag));
            var force = ps.forceOverLifetime;
            if (force.enabled) w.AppendLine("  force x=" + Curve(force.x) + " y=" + Curve(force.y) + " space=" + force.space);
            var col = ps.colorOverLifetime;
            if (col.enabled) w.AppendLine("  colourOverLife " + Gradient(col.color));
            var size = ps.sizeOverLifetime;
            if (size.enabled) w.AppendLine("  sizeOverLife " + Curve(size.size) + (size.separateAxes ? " (axes)" : ""));
            var rot = ps.rotationOverLifetime;
            if (rot.enabled) w.AppendLine("  rotationOverLife z=" + Curve(rot.z));
            var noise = ps.noise;
            if (noise.enabled) w.AppendLine("  noise strength=" + Curve(noise.strengthX) + " frequency=" + N(noise.frequency, "0.###")
                                            + " scroll=" + Curve(noise.scrollSpeed) + " octaves=" + noise.octaveCount);
            var sheet = ps.textureSheetAnimation;
            if (sheet.enabled)
            {
                var sprites = new List<string>();
                if (sheet.mode == ParticleSystemAnimationMode.Sprites)
                    for (int i = 0; i < sheet.spriteCount; i++) { var sp = sheet.GetSprite(i); sprites.Add(sp != null ? sp.name : "-"); }
                w.AppendLine("  sheet " + sheet.mode + " tiles=" + sheet.numTilesX + "x" + sheet.numTilesY + " " + sheet.animation
                             + " frame=" + Curve(sheet.frameOverTime) + " start=" + Curve(sheet.startFrame) + " cycles=" + sheet.cycleCount
                             + (sprites.Count > 0 ? " sprites=[" + string.Join(",", sprites.ToArray()) + "]" : ""));
            }
            if (ps.trails.enabled) w.AppendLine("  trails ratio=" + N(ps.trails.ratio, "0.##") + " lifetime=" + Curve(ps.trails.lifetime));
            if (ps.subEmitters.enabled) w.AppendLine("  subEmitters " + ps.subEmitters.subEmittersCount);
            if (ps.collision.enabled) w.AppendLine("  collision " + ps.collision.type);
            if (ps.lights.enabled) w.AppendLine("  lights");
            if (ps.externalForces.enabled) w.AppendLine("  externalForces");
            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (r != null)
                w.AppendLine("  renderer " + On(r.enabled) + " " + r.renderMode + " align=" + r.alignment + " sort=" + r.sortMode
                             + " layer=" + r.sortingLayerName + "/" + r.sortingOrder + " minSize=" + N(r.minParticleSize, "0.###")
                             + " maxSize=" + N(r.maxParticleSize, "0.###") + " pivot=" + V3p(r.pivot) + " flip=" + V3p(r.flip)
                             + " " + MaterialSummary(r.sharedMaterial) + MaterialDetail(r.sharedMaterial)
                             + (ps.trails.enabled && r.trailMaterial != null ? " trail " + MaterialSummary(r.trailMaterial) : ""));
        }

        private static string On(bool on) => on ? "on" : "off";

        private static string V3(Vector3 v) => N(v.x, "0.###") + "," + N(v.y, "0.###") + "," + N(v.z, "0.###");

        private static string Rgba(Color c)
            => "#" + ((Color32)c).r.ToString("X2") + ((Color32)c).g.ToString("X2") + ((Color32)c).b.ToString("X2") + ((Color32)c).a.ToString("X2");

        private static string Curve(ParticleSystem.MinMaxCurve c)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant: return N(c.constant, "0.###");
                case ParticleSystemCurveMode.TwoConstants: return N(c.constantMin, "0.###") + ".." + N(c.constantMax, "0.###");
                case ParticleSystemCurveMode.Curve: return "curve*" + N(c.curveMultiplier, "0.###") + Keys(c.curve);
                default: return "curves*" + N(c.curveMultiplier, "0.###") + Keys(c.curveMin) + Keys(c.curveMax);
            }
        }

        private static string Keys(AnimationCurve curve)
        {
            if (curve == null) return "()";
            return "(" + string.Join(" ", curve.keys.Select(k => N(k.time, "0.##") + ":" + N(k.value, "0.##")).ToArray()) + ")";
        }

        private static string Gradient(ParticleSystem.MinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return Rgba(g.color);
                case ParticleSystemGradientMode.TwoColors: return Rgba(g.colorMin) + ".." + Rgba(g.colorMax);
                case ParticleSystemGradientMode.Gradient: return "gradient" + Stops(g.gradient);
                case ParticleSystemGradientMode.TwoGradients: return "gradients" + Stops(g.gradientMin) + Stops(g.gradientMax);
                default: return "random" + Stops(g.gradient);
            }
        }

        private static string Stops(UnityEngine.Gradient g)
        {
            if (g == null) return "()";
            return "(" + string.Join(" ", g.colorKeys.Select(k => N(k.time, "0.##") + ":" + Rgba(k.color).Substring(0, 7)).ToArray())
                   + " | " + string.Join(" ", g.alphaKeys.Select(k => N(k.time, "0.##") + ":" + N(k.alpha, "0.##")).ToArray()) + ")";
        }
    }
}

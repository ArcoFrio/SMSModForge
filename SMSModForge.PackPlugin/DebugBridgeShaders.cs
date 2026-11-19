using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The bridge's commands for checking the editor's preview against what the
    /// game draws (1.9.1, the author: "a full shader check ... to make sure our
    /// editor implementation matches exactly"). Debug only, like the bridge.
    /// <list type="bullet">
    /// <item><c>freeze on|off</c>: the game's clock stopped (Time.timeScale 0) -
    /// the shaders' _Time with it - so a frame can be read at a known moment.</item>
    /// <item><c>bust-look &lt;path&gt; &lt;name&gt;</c>: at the end of the frame, the
    /// screen as drawn, the background the bust shader's fake GI reads
    /// (_CameraSortingLayerTexture), and beside them in info.txt: the shader's
    /// time, where the body's art lies on screen (its texture's four corners),
    /// its material's values, the fake GI's globals, and every 2D light that is
    /// on, whether it lights the body's sorting layer.</item>
    /// <item><c>sprite-textures &lt;path&gt;</c>: every sprite renderer at and under
    /// the object, active or not, with its texture's name (quoted; a pack's own art
    /// has none), size, filter and wrap.</item>
    /// <item><c>parallax rest|follow</c>: the game's levels held where they rest,
    /// not following the mouse (see ParallaxTick), or let go again.</item>
    /// <item><c>screen-look &lt;name&gt;</c>: at the end of the frame, the screen
    /// as drawn, and in info.txt every 2D light that is on (whether it lights
    /// the Bust layer) and what each sorting layer draws on screen.</item>
    /// </list>
    /// </summary>
    internal sealed partial class DebugBridge
    {
        private float _timeScaleBefore = 1f;

        // parallax rest: every level held where it rests, whatever the mouse does (1.9.2).
        private bool _parallaxRest;
        private float _parallaxScanAt;
        private readonly List<Behaviour> _parallaxHeld = new List<Behaviour>();

        /// <summary>
        /// Holds the game's levels where they rest while <c>parallax rest</c> is on.
        /// A level follows the mouse (the game's ParallaxMouseEffect: half its
        /// strength in local units at the screen's edge), and on the run's desktop
        /// the mouse is wherever the author's is: the backdrops were captured with
        /// every room 19 game pixels right of where it rests. Each effect found on
        /// is switched off and its level put back to where it started; one not
        /// started yet never moved. Looked for twice a second, as levels come up.
        /// </summary>
        private void ParallaxTick()
        {
            if (!_parallaxRest || Time.unscaledTime < _parallaxScanAt) return;
            _parallaxScanAt = Time.unscaledTime + 0.5f;
            foreach (var b in Resources.FindObjectsOfTypeAll<Behaviour>())
            {
                if (b == null || !b.enabled || b.GetType().Name != "ParallaxMouseEffect" || !b.gameObject.scene.IsValid()) continue;
                var start = b.GetType().GetField("startPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                b.enabled = false;
                _parallaxHeld.Add(b);
                if (start != null && (Vector3)start.GetValue(b) != Vector3.zero)
                    b.GetType().GetMethod("ResetToStartPosition")?.Invoke(b, null);
            }
        }

        private bool RunShaders(string verb, string a, string rest)
        {
            switch (verb)
            {
                case "parallax":
                {
                    if (a == "rest") { _parallaxRest = true; _parallaxScanAt = 0f; ParallaxTick(); }
                    else
                    {
                        _parallaxRest = false;
                        foreach (var b in _parallaxHeld) if (b != null) b.enabled = true;
                        _parallaxHeld.Clear();
                    }
                    Say("ok parallax " + a + " held=" + _parallaxHeld.Count.ToString(CultureInfo.InvariantCulture));
                    return true;
                }
                case "freeze":
                {
                    if (a == "on")
                    {
                        if (Time.timeScale != 0f) _timeScaleBefore = Time.timeScale;
                        Time.timeScale = 0f;
                    }
                    else Time.timeScale = _timeScaleBefore;
                    Say("ok freeze " + a + " timeScale=" + Time.timeScale.ToString(CultureInfo.InvariantCulture));
                    return true;
                }
                case "sprite-textures":
                {
                    var root = FindPath((a + " " + rest).Trim());
                    if (root == null) { Say("error sprite-textures: nothing at " + (a + " " + rest).Trim()); return true; }
                    var found = root.GetComponentsInChildren<SpriteRenderer>(true)
                        .Where(sr => sr.sprite != null && sr.sprite.texture != null)
                        .Select(sr => FullPath(sr.transform) + " = '" + sr.sprite.texture.name + "' " + sr.sprite.texture.width + "x" + sr.sprite.texture.height
                                      + " filter=" + sr.sprite.texture.filterMode + " wrap=" + sr.sprite.texture.wrapMode).ToArray();
                    Say("ok sprite-textures " + found.Length + ": " + string.Join(" | ", found));
                    return true;
                }
                case "material-dump":
                {
                    // material-dump <shader name>: every material drawing with it,
                    // its keywords and every property's value, and the global
                    // keywords - what a post-processing pass really runs with.
                    string shaderName = (a + " " + rest).Trim();
                    var lines = new List<string> { "global keywords: " + string.Join(" ", Shader.enabledGlobalKeywords.Select(k => k.name).ToArray()) };
                    foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    {
                        if (m == null || m.shader == null || m.shader.name != shaderName) continue;
                        lines.Add("material " + m.name + " keywords: " + string.Join(" ", m.shaderKeywords));
                        foreach (var kind in new[] { MaterialPropertyType.Float, MaterialPropertyType.Int, MaterialPropertyType.Vector, MaterialPropertyType.Matrix, MaterialPropertyType.Texture })
                            foreach (var n in m.GetPropertyNames(kind))
                            {
                                string v;
                                switch (kind)
                                {
                                    case MaterialPropertyType.Float: v = Lf(m.GetFloat(n)); break;
                                    case MaterialPropertyType.Int: v = m.GetInteger(n).ToString(CultureInfo.InvariantCulture); break;
                                    case MaterialPropertyType.Vector: v = Lv4(m.GetVector(n)); break;
                                    case MaterialPropertyType.Matrix: v = m.GetMatrix(n).ToString(); break;
                                    default: var t = m.GetTexture(n); v = t == null ? "-" : t.name + " " + t.width + "x" + t.height; break;
                                }
                                lines.Add("  " + kind + " " + n + " = " + v);
                            }
                    }
                    File.WriteAllLines(Path.Combine(_dir, "material-dump.txt"), lines.ToArray());
                    Say("ok material-dump " + (lines.Count - 1) + " lines");
                    return true;
                }
                case "render-textures":
                {
                    // Every render texture alive: name, size, format - to find the
                    // camera's own HDR target.
                    var found = Resources.FindObjectsOfTypeAll<RenderTexture>().Where(r => r != null)
                        .Select(r => r.name + " " + r.width + "x" + r.height + " " + r.graphicsFormat + (r.IsCreated() ? "" : " (not created)")).ToArray();
                    File.WriteAllLines(Path.Combine(_dir, "render-textures.txt"), found);
                    Say("ok render-textures " + found.Length);
                    return true;
                }
                case "rt-save":
                {
                    // rt-save <name start> <file>: at the end of the frame, that
                    // render texture as it is, as raw floats (int32 w, h, then RGBA
                    // float32, rows from the bottom).
                    _waitingFor = "frame";
                    _waitDeadline = Time.unscaledTime + 10f;
                    StartCoroutine(SaveRenderTexture(a, rest.Trim()));
                    return true;
                }
                case "shadow-mesh":
                {
                    // shadow-mesh <path>: the ShadowCaster2D's mesh (what it casts) and
                    // the sprite's own mesh, in screen pixels, to shadow-mesh.txt.
                    var target = FindPath((a + " " + rest).Trim());
                    var caster = target != null ? target.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "ShadowCaster2D") : null;
                    var sr = target != null ? target.GetComponent<SpriteRenderer>() : null;
                    var cam = Camera.main;
                    if (caster == null || sr == null || cam == null) { Say("error shadow-mesh: no ShadowCaster2D with a sprite at " + (a + " " + rest).Trim()); return true; }
                    var lines = new List<string>();
                    float cx0 = float.MaxValue, cy0 = float.MaxValue, cx1 = float.MinValue, cy1 = float.MinValue;
                    float sx0 = float.MaxValue, sy0 = float.MaxValue, sx1 = float.MinValue, sy1 = float.MinValue;
                    var mesh = caster.GetType().GetProperty("mesh")?.GetValue(caster, null) as Mesh;
                    if (mesh != null)
                    {
                        lines.Add("caster mesh: " + mesh.vertexCount + " vertices, " + mesh.triangles.Length / 3 + " triangles, uv channels " + mesh.uv.Length);
                        var uv = mesh.uv; var tangents = mesh.tangents;
                        for (int i = 0; i < mesh.vertexCount; i++)
                        {
                            var w = target.transform.TransformPoint(mesh.vertices[i]);
                            var sc = cam.WorldToScreenPoint(w);
                            lines.Add("cv " + Lf(sc.x) + "," + Lf(sc.y) + (i < tangents.Length ? " t " + Lv4(tangents[i]) : ""));
                            cx0 = Mathf.Min(cx0, sc.x); cy0 = Mathf.Min(cy0, sc.y); cx1 = Mathf.Max(cx1, sc.x); cy1 = Mathf.Max(cy1, sc.y);
                        }
                    }
                    // Model space, exactly as URP holds it: the caster mesh (positions,
                    // tangents, triangles), the sprite mesh (positions, uvs, triangles),
                    // the shadow shape's settings, the transform and the camera.
                    if (mesh != null)
                    {
                        var mv = mesh.vertices; var mt = mesh.tangents; var mi = mesh.triangles;
                        for (int i = 0; i < mv.Length; i++) lines.Add("cl " + Lf(mv[i].x) + "," + Lf(mv[i].y) + " t " + Lv4(mt[i]));
                        for (int i = 0; i + 2 < mi.Length; i += 3) lines.Add("ct " + mi[i] + "," + mi[i + 1] + "," + mi[i + 2]);
                    }
                    var shape = caster.GetType().GetField("m_ShadowMesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(caster);
                    if (shape != null)
                        foreach (var f in shape.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                        {
                            var v = f.GetValue(shape);
                            if (v is Mesh || v == null) continue;
                            lines.Add("shape " + f.Name + "=" + (v is float fv ? Lf(fv) : Convert.ToString(v, CultureInfo.InvariantCulture)));
                        }
                    {
                        var spv = sr.sprite.vertices; var spu = sr.sprite.uv; var spt = sr.sprite.triangles;
                        for (int i = 0; i < spv.Length; i++) lines.Add("sl " + Lf(spv[i].x) + "," + Lf(spv[i].y) + " uv " + Lf(spu[i].x) + "," + Lf(spu[i].y));
                        for (int i = 0; i + 2 < spt.Length; i += 3) lines.Add("st " + spt[i] + "," + spt[i + 1] + "," + spt[i + 2]);
                        lines.Add("sprite rect " + Lf(sr.sprite.rect.x) + "," + Lf(sr.sprite.rect.y) + "," + Lf(sr.sprite.rect.width) + "," + Lf(sr.sprite.rect.height)
                                  + " ppu " + Lf(sr.sprite.pixelsPerUnit) + " pivot " + Lf(sr.sprite.pivot.x) + "," + Lf(sr.sprite.pivot.y)
                                  + " texture " + sr.sprite.texture.width + "x" + sr.sprite.texture.height);
                        var tr = target.transform;
                        lines.Add("transform position " + Lf(tr.position.x) + "," + Lf(tr.position.y) + "," + Lf(tr.position.z)
                                  + " lossyScale " + Lf(tr.lossyScale.x) + "," + Lf(tr.lossyScale.y) + " rotationZ " + Lf(tr.eulerAngles.z)
                                  + " bounds " + Lf(sr.bounds.min.x) + "," + Lf(sr.bounds.min.y) + ".." + Lf(sr.bounds.max.x) + "," + Lf(sr.bounds.max.y));
                        lines.Add("camera position " + Lf(cam.transform.position.x) + "," + Lf(cam.transform.position.y) + " orthographicSize " + Lf(cam.orthographicSize)
                                  + " pixels " + cam.pixelWidth + "x" + cam.pixelHeight + " world00 " + Lf(cam.ScreenToWorldPoint(Vector3.zero).x) + "," + Lf(cam.ScreenToWorldPoint(Vector3.zero).y)
                                  + " world11 " + Lf(cam.ScreenToWorldPoint(new Vector3(cam.pixelWidth, cam.pixelHeight, 0f)).x) + "," + Lf(cam.ScreenToWorldPoint(new Vector3(cam.pixelWidth, cam.pixelHeight, 0f)).y));
                    }
                    var sv = sr.sprite.vertices; var tri = sr.sprite.triangles;
                    lines.Add("sprite mesh: " + sr.sprite.name + " " + sv.Length + " vertices, " + tri.Length / 3 + " triangles");
                    foreach (var v in sv)
                    {
                        var sc = cam.WorldToScreenPoint(target.transform.TransformPoint(v));
                        lines.Add("sv " + Lf(sc.x) + "," + Lf(sc.y));
                        sx0 = Mathf.Min(sx0, sc.x); sy0 = Mathf.Min(sy0, sc.y); sx1 = Mathf.Max(sx1, sc.x); sy1 = Mathf.Max(sy1, sc.y);
                    }
                    // Every caster under the bust's root, so a test can see which
                    // of its parts throw a shadow at all.
                    var bustRoot = target.transform.parent != null ? target.transform.parent : target.transform;
                    var casters = bustRoot.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "ShadowCaster2D")
                        .Select(c => c.transform == bustRoot ? "." : c.transform.name).ToArray();
                    File.WriteAllLines(Path.Combine(_dir, "shadow-mesh.txt"), lines.ToArray());
                    // Bounds on screen, in pixels: what the caster casts, and the sprite.
                    Say("ok shadow-mesh " + (mesh != null ? mesh.vertexCount : 0) + " caster vertices, " + sv.Length + " sprite vertices"
                        + "; casterBounds=" + Lf(cx0) + "," + Lf(cy0) + "," + Lf(cx1) + "," + Lf(cy1)
                        + "; spriteBounds=" + Lf(sx0) + "," + Lf(sy0) + "," + Lf(sx1) + "," + Lf(sy1)
                        + "; casters=" + string.Join("|", casters)
                        + "; origin=" + Lf(cam.WorldToScreenPoint(target.transform.position).x) + "," + Lf(cam.WorldToScreenPoint(target.transform.position).y));
                    return true;
                }
                case "sorting":
                {
                    // sorting <path>: every renderer under it - sorting layer (name,
                    // id) and order - and the project's sorting layers in order.
                    var target = FindPath((a + " " + rest).Trim());
                    if (target == null) { Say("error sorting: no " + (a + " " + rest).Trim()); return true; }
                    var renderers = target.GetComponentsInChildren<Renderer>(true)
                        .Select(r => r.transform.name + ":" + r.sortingLayerName + "(" + r.sortingLayerID + ")#" + r.sortingOrder + "[" + (r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name.Replace(" ", "_") : "-") + (r.gameObject.activeInHierarchy && r.enabled ? "" : ",off") + "]").ToArray();
                    var layers = SortingLayer.layers.Select(l => l.name + "(" + l.id + ")=" + l.value).ToArray();
                    Say("ok sorting " + string.Join(" ", renderers) + "; layers " + string.Join(" ", layers));
                    return true;
                }
                case "shadow-light":
                {
                    // shadow-light <path>: a 2D light's place and the numbers URP
                    // projects shadows with (_LightPos, _ShadowRadius, the soft angle).
                    var target = FindPath((a + " " + rest).Trim());
                    var light = target != null ? target.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "Light2D") : null;
                    var cam = Camera.main;
                    if (light == null || cam == null) { Say("error shadow-light: no Light2D at " + (a + " " + rest).Trim()); return true; }
                    var t = light.GetType();
                    object P(string n) => t.GetProperty(n, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(light, null);
                    var sphere = P("boundingSphere") is BoundingSphere bs ? bs : default(BoundingSphere);
                    var pos = light.transform.position;
                    float radius = sphere.radius + (pos - sphere.position).magnitude;
                    var sc = cam.WorldToScreenPoint(pos);
                    Say("ok shadow-light world " + Lf(pos.x) + "," + Lf(pos.y) + "," + Lf(pos.z) + " screen " + Lf(sc.x) + "," + Lf(sc.y)
                        + "; sphere " + Lf(sphere.position.x) + "," + Lf(sphere.position.y) + " r " + Lf(sphere.radius) + "; shadowRadius " + Lf(radius)
                        + "; outerRadius " + Convert.ToString(P("pointLightOuterRadius"), CultureInfo.InvariantCulture)
                        + "; pixelsPerUnit " + Lf(cam.pixelHeight / (2f * cam.orthographicSize))
                        + "; shadowSoftness " + Convert.ToString(P("shadowSoftness"), CultureInfo.InvariantCulture)
                        + "; shadowSoftnessFalloffIntensity " + Convert.ToString(P("shadowSoftnessFalloffIntensity"), CultureInfo.InvariantCulture)
                        + "; shadowIntensity " + Convert.ToString(P("shadowIntensity"), CultureInfo.InvariantCulture)
                        + "; shadowVolumeIntensity " + Convert.ToString(P("shadowVolumeIntensity"), CultureInfo.InvariantCulture));
                    return true;
                }
                case "texture-save":
                {
                    // texture-save <name> <file>: a loaded texture as the shaders read it
                    // (sRGB decoded), raw floats like rt-save.
                    var tex = Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(x => x != null && x.name == a);
                    if (tex == null) { Say("error texture-save: no texture " + a); return true; }
                    var full = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    Graphics.Blit(tex, full);
                    var before = RenderTexture.active;
                    RenderTexture.active = full;
                    var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBAFloat, false, true);
                    read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    read.Apply();
                    RenderTexture.active = before;
                    RenderTexture.ReleaseTemporary(full);
                    var px = read.GetPixels();
                    UnityEngine.Object.Destroy(read);
                    using (var w = new BinaryWriter(File.Create(Path.Combine(_dir, rest.Trim()))))
                    {
                        w.Write(tex.width); w.Write(tex.height);
                        foreach (var c in px) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
                    }
                    Say("ok texture-save " + tex.name + " " + tex.width + "x" + tex.height + " " + tex.graphicsFormat + " readable " + tex.isReadable);
                    return true;
                }
                case "lights-list":
                {
                    // Every 2D light switched on: kind, colour, intensity, the
                    // sorting layers it lights, its blend style.
                    var type = Light2DType();
                    if (type == null) { Say("error lights-list: no Light2D type"); return true; }
                    var said = new List<string>();
                    foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
                    {
                        var light = obj as Behaviour;
                        if (light == null || !light.isActiveAndEnabled) continue;
                        object P(string n) => type.GetProperty(n)?.GetValue(light, null);
                        var layers = type.GetField("m_ApplyToSortingLayers", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(light) as int[];
                        var c = (Color)P("color");
                        said.Add(FullPath(light.transform) + " " + P("lightType") + " colour " + Lf(c.r) + "," + Lf(c.g) + "," + Lf(c.b) + "," + Lf(c.a)
                                 + " intensity " + Convert.ToString(P("intensity"), CultureInfo.InvariantCulture) + " blendStyle " + Convert.ToString(P("blendStyleIndex"), CultureInfo.InvariantCulture)
                                 + " layers " + (layers == null ? "-" : string.Join(",", layers.Select(l => SortingLayer.IDToName(l)).ToArray()))
                                 // what decides whether a bust casts a shadow there, and the light's glow and reach
                                 + " shadows " + Convert.ToString(P("shadowsEnabled") ?? "-", CultureInfo.InvariantCulture)
                                 + " shadowIntensity " + Convert.ToString(P("shadowIntensity") ?? "-", CultureInfo.InvariantCulture)
                                 + " volume " + Convert.ToString(P("volumeIntensity") ?? P("volumeOpacity") ?? "-", CultureInfo.InvariantCulture)
                                 + " outer " + Convert.ToString(P("pointLightOuterRadius") ?? "-", CultureInfo.InvariantCulture)
                                 + " screen " + ScreenOf(light.transform.position));
                    }
                    Say("ok lights-list " + string.Join(" | ", said.ToArray()));
                    return true;
                }
                case "set-field":
                {
                    // set-field <component type> <field> <value> <path>: one bool or
                    // float field of a component, its private ones too (a light's
                    // glow switched off, to measure what it adds).
                    var words = (a + " " + rest).Trim().Split(new[] { ' ' }, 4);
                    if (words.Length < 4) { Say("error set-field: set-field <type> <field> <value> <path>"); return true; }
                    var target = FindPath(words[3]);
                    var comp = target != null ? target.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == words[0]) : null;
                    var field = comp?.GetType().GetField(words[1], BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (field == null) { Say("error set-field: no " + words[0] + "." + words[1] + " at " + words[3]); return true; }
                    object value = field.FieldType == typeof(bool) ? (object)bool.Parse(words[2]) : float.Parse(words[2], CultureInfo.InvariantCulture);
                    field.SetValue(comp, value);
                    Say("ok set-field " + words[0] + "." + words[1] + " = " + Convert.ToString(field.GetValue(comp), CultureInfo.InvariantCulture));
                    return true;
                }
                case "shadow-casters":
                {
                    // Every ShadowCaster2D in the scene, switched on or not.
                    var all = Resources.FindObjectsOfTypeAll<Component>().Where(c => c != null && c.GetType().Name == "ShadowCaster2D" && c.gameObject.scene.IsValid())
                        .Select(c => FullPath(c.transform) + (c.gameObject.activeInHierarchy && ((Behaviour)c).enabled ? "" : " (off)")).ToArray();
                    Say("ok shadow-casters " + all.Length + ": " + string.Join(" | ", all));
                    return true;
                }
                case "asset-fields":
                {
                    // asset-fields <type name>: every field of every loaded object of
                    // that type (a ScriptableObject, such as the 2D renderer's data).
                    var found = Resources.FindObjectsOfTypeAll<ScriptableObject>().Where(o => o != null && o.GetType().Name == a).ToArray();
                    var said = new List<string>();
                    foreach (var o in found)
                    {
                        var values = new List<string>();
                        for (var type = o.GetType(); type != null && type != typeof(ScriptableObject); type = type.BaseType)
                            foreach (var f in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                                             | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                            {
                                object v;
                                try { v = f.GetValue(o); } catch (Exception) { continue; }
                                values.Add(f.Name + "=" + (v == null ? "null" : v is UnityEngine.Object uo ? (uo == null ? "null" : uo.name + " (" + uo.GetType().Name + ")") : Convert.ToString(v, CultureInfo.InvariantCulture)));
                            }
                        said.Add(o.name + ": " + string.Join("; ", values.ToArray()));
                    }
                    File.WriteAllLines(Path.Combine(_dir, "asset-fields.txt"), said.ToArray());
                    Say("ok asset-fields " + found.Length);
                    return true;
                }
                case "frames":
                {
                    // How many frames the game has drawn, and what paces it.
                    Say("ok frames " + Time.frameCount + " targetFrameRate=" + Application.targetFrameRate + " vSyncCount=" + QualitySettings.vSyncCount);
                    return true;
                }
                case "screen-look":
                {
                    _waitingFor = "frame";
                    _waitDeadline = Time.unscaledTime + 10f;
                    StartCoroutine(ScreenLook((a + " " + rest).Trim()));
                    return true;
                }
                case "bust-look":
                {
                    _waitingFor = "frame";
                    _waitDeadline = Time.unscaledTime + 10f;
                    StartCoroutine(BustLook(a, rest.Trim()));
                    return true;
                }
                case "lights":
                {
                    // lights neutral|restore: every 2D light but the global ones
                    // off, and those white at 1 - colours drawn as the art has them.
                    Say("ok lights " + a + ": " + (a == "neutral" ? NeutralLights() : RestoreLights()));
                    return true;
                }
                case "postfx":
                {
                    // postfx off|on: the main camera's post-processing (URP's
                    // renderPostProcessing on its additional camera data).
                    var data = CameraData();
                    var flag = data != null ? data.GetType().GetProperty("renderPostProcessing") : null;
                    if (flag == null) { Say("error postfx: no camera data"); return true; }
                    if (a == "off") { _postFxBefore = (bool)flag.GetValue(data, null); flag.SetValue(data, false, null); }
                    else flag.SetValue(data, _postFxBefore, null);
                    Say("ok postfx " + a + " renderPostProcessing=" + flag.GetValue(data, null));
                    return true;
                }
                case "volume-settings":
                {
                    // volume-settings: every active post-processing volume - profile,
                    // global or not, priority, weight - and each effect in it
                    // that is on, with every setting it overrides. Written to
                    // volumes.txt beside the results; the answer counts them.
                    var lines = Volumes();
                    File.WriteAllLines(Path.Combine(_dir, "volumes.txt"), lines.ToArray());
                    Say("ok volume-settings " + lines.Count(l => l.StartsWith("volume ")) + " active, written to " + Path.Combine(_dir, "volumes.txt"));
                    return true;
                }
                case "fake-gi":
                {
                    // fake-gi off|on: the bust shader's fake GI, by its global switch.
                    if (a == "off") { _giBefore = Shader.GetGlobalFloat("_Global_GIEnabled"); Shader.SetGlobalFloat("_Global_GIEnabled", 0f); }
                    else Shader.SetGlobalFloat("_Global_GIEnabled", _giBefore);
                    Say("ok fake-gi " + a + " enabled=" + Lf(Shader.GetGlobalFloat("_Global_GIEnabled")));
                    return true;
                }
                default:
                    return false;
            }
        }

        private float _giBefore = 1f;
        private bool _postFxBefore = true;

        private static Component CameraData()
        {
            var cam = Camera.main;
            if (cam == null) return null;
            return cam.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "UniversalAdditionalCameraData");
        }

        /// <summary>Every active Volume, and the effects of its profile that are on,
        /// with the settings they override (by reflection: the plugin does not
        /// reference the render pipeline).</summary>
        private static List<string> Volumes()
        {
            var lines = new List<string>();
            var volumeType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(asm => { try { return asm.GetType("UnityEngine.Rendering.Volume"); } catch { return null; } })
                .FirstOrDefault(t => t != null);
            if (volumeType == null) { lines.Add("no Volume type"); return lines; }
            var cam = Camera.main;
            var data = CameraData();
            if (data != null)
                foreach (var p in new[] { "renderPostProcessing", "antialiasing", "volumeLayerMask", "volumeTrigger", "stopNaN", "dithering" })
                {
                    var prop = data.GetType().GetProperty(p);
                    if (prop != null) lines.Add("camera " + p + "=" + prop.GetValue(data, null));
                }
            foreach (var obj in UnityEngine.Object.FindObjectsOfType(volumeType))
            {
                var v = obj as Behaviour;
                if (v == null || !v.isActiveAndEnabled) continue;
                object Field(string name)
                {
                    var f = volumeType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                    if (f != null) return f.GetValue(v);
                    var p = volumeType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    try { return p != null ? p.GetValue(v, null) : null; } catch { return null; }
                }
                var profile = Field("sharedProfile") as ScriptableObject;
                lines.Add("volume " + FullPath(v.transform) + " layer=" + LayerMask.LayerToName(v.gameObject.layer) + " global=" + Field("isGlobal")
                          + " priority=" + Field("priority") + " weight=" + Field("weight") + " blendDistance=" + Field("blendDistance")
                          + " profile=" + (profile != null ? profile.name : "-"));
                if (profile == null) continue;
                var components = profile.GetType().GetField("components", BindingFlags.Public | BindingFlags.Instance)?.GetValue(profile) as System.Collections.IEnumerable;
                if (components == null) continue;
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var ct = comp.GetType();
                    bool active = (bool)(ct.GetField("active", BindingFlags.Public | BindingFlags.Instance)?.GetValue(comp) ?? true);
                    var settings = new List<string>();
                    foreach (var f in ct.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var param = f.GetValue(comp);
                        if (param == null) continue;
                        var pt = param.GetType();
                        var overridden = pt.GetProperty("overrideState");
                        var value = pt.GetProperty("value");
                        if (overridden == null || value == null) continue;
                        if (!(bool)overridden.GetValue(param, null)) continue;
                        object val;
                        try { val = value.GetValue(param, null); } catch { val = "?"; }
                        string text = val is Color c ? Lc(c) : val is float fl ? Lf(fl) : val is Vector2 v2 ? Lv(v2) : val is Vector4 v4 ? Lf(v4.x) + "," + Lf(v4.y) + "," + Lf(v4.z) + "," + Lf(v4.w) : val is UnityEngine.Object o ? (o != null ? o.name : "null") : Convert.ToString(val, CultureInfo.InvariantCulture);
                        settings.Add(f.Name + "=" + text);
                    }
                    lines.Add("  effect " + ct.Name + (active ? "" : " (off)") + ": " + string.Join("; ", settings.ToArray()));
                }
            }
            return lines;
        }
        private readonly List<(Behaviour light, bool enabled, Color colour, float intensity)> _lightsBefore = new List<(Behaviour, bool, Color, float)>();

        private static Type Light2DType()
            => AppDomain.CurrentDomain.GetAssemblies()
                .Select(asm => { try { return asm.GetType("UnityEngine.Rendering.Universal.Light2D"); } catch { return null; } })
                .FirstOrDefault(t => t != null);

        private string NeutralLights()
        {
            var type = Light2DType();
            if (type == null) return "no Light2D type";
            var lightType = type.GetProperty("lightType");
            var colour = type.GetProperty("color");
            var intensity = type.GetProperty("intensity");
            int off = 0, white = 0;
            foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
            {
                var light = obj as Behaviour;
                if (light == null || !light.isActiveAndEnabled) continue;
                _lightsBefore.Add((light, light.enabled, (Color)colour.GetValue(light, null), (float)intensity.GetValue(light, null)));
                if (lightType.GetValue(light, null).ToString() == "Global")
                {
                    colour.SetValue(light, Color.white, null);
                    intensity.SetValue(light, 1f, null);
                    white++;
                }
                else { light.enabled = false; off++; }
            }
            return off + " off, " + white + " global white at 1";
        }

        private string RestoreLights()
        {
            var type = Light2DType();
            int n = 0;
            foreach (var (light, enabled, colour, intensity) in _lightsBefore)
            {
                if (light == null) continue;
                light.enabled = enabled;
                type.GetProperty("color").SetValue(light, colour, null);
                type.GetProperty("intensity").SetValue(light, intensity, null);
                n++;
            }
            _lightsBefore.Clear();
            return n + " put back";
        }

        /// <summary>The background the fake GI reads lives only while the camera
        /// draws: copied as it finishes, into a texture of our own.</summary>
        private RenderTexture _background;
        private string _backgroundFormat = "";

        private string _backgroundSeen = "";

        /// <summary>The engine's sprite snapping while the main camera draws: its
        /// Pixel Perfect Camera sets PixelPerfectRendering.pixelSnapSpacing as the
        /// camera begins and puts 0 back as it ends, so it is read in between.</summary>
        private float _snapWhileDrawing = -1f;

        private void ReadSnap(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        {
            if (camera == Camera.main) _snapWhileDrawing = UnityEngine.U2D.PixelPerfectRendering.pixelSnapSpacing;
        }

        private void CopyBackground(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        {
            var source = Shader.GetGlobalTexture("_CameraSortingLayerTexture");
            _backgroundSeen += (camera != null ? camera.name : "null") + ":" + (source == null ? "none" : source.width + "x" + source.height) + " ";
            if (camera != Camera.main) return;
            // Nothing copied this frame (no bust drawn: an NPC alone, 1.9.1): the
            // flat stand-in bound instead, which the fake GI then reads.
            if (source != null && source.width <= 4) _flatBackground = source;
            if (source == null || source.width <= 4) return;
            if (_background == null || _background.width != source.width || _background.height != source.height)
            {
                if (_background != null) _background.Release();
                _background = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            }
            var cmd = new UnityEngine.Rendering.CommandBuffer { name = "SMSModForge bust-look background" };
            cmd.Blit(source, _background);
            context.ExecuteCommandBuffer(cmd);
            context.Submit();
            cmd.Release();
            var rt = source as RenderTexture;
            _backgroundFormat = source.width + "x" + source.height + " filter=" + source.filterMode + (rt != null ? " format=" + rt.format : "");
        }

        private Texture _flatBackground;

        /// <summary>A small texture's texels as the shaders read them, "r,g,b,a" each.</summary>
        private static string Texels(Texture tex)
        {
            var full = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, full);
            var before = RenderTexture.active;
            RenderTexture.active = full;
            var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            read.Apply();
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(full);
            var px = read.GetPixels();
            UnityEngine.Object.Destroy(read);
            return string.Join(" ", px.Select(c => Lf(c.r) + "," + Lf(c.g) + "," + Lf(c.b) + "," + Lf(c.a)).Distinct().ToArray());
        }

        private System.Collections.IEnumerator BustLook(string path, string name)
        {
            _flatBackground = null;
            _backgroundFormat = "";
            _backgroundSeen = "";
            _snapWhileDrawing = -1f;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += CopyBackground;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += ReadSnap;
            yield return null;
            yield return new WaitForEndOfFrame();
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= CopyBackground;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= ReadSnap;
            _waitingFor = null;
            string fail = null;
            var lines = new List<string>();
            string folder = Path.Combine(_dir, "looks", name);
            try
            {
                Directory.CreateDirectory(folder);
                var body = FindPath(path);
                var sr = body != null ? body.GetComponent<SpriteRenderer>() : null;
                if (sr == null || sr.sprite == null) fail = "no sprite renderer with a sprite at " + path;
                else
                {
                    Vector4 t = Shader.GetGlobalVector("_Time");
                    lines.Add("time=" + Lf(t.y));
                    lines.Add("timeVector=" + Lf(t.x) + "," + Lf(t.y) + "," + Lf(t.z) + "," + Lf(t.w));
                    lines.Add("timeSinceLevelLoad=" + Lf(Time.timeSinceLevelLoad));
                    lines.Add("timeScale=" + Lf(Time.timeScale));
                    lines.Add("screen=" + Screen.width + "x" + Screen.height);
                    var cam = Camera.main;
                    if (cam != null)
                        lines.Add("camera=" + cam.name + " ortho=" + cam.orthographic + " size=" + Lf(cam.orthographicSize)
                                  + " pos=" + Lv(cam.transform.position) + " pixelRect=" + Lf(cam.pixelRect.x) + "," + Lf(cam.pixelRect.y) + "," + Lf(cam.pixelRect.width) + "," + Lf(cam.pixelRect.height));

                    var sprite = sr.sprite;
                    lines.Add("body=" + FullPath(body.transform) + " active=" + body.activeInHierarchy);
                    lines.Add("sprite=" + sprite.name + " texture=" + (sprite.texture != null ? sprite.texture.name + " " + sprite.texture.width + "x" + sprite.texture.height + " filter=" + sprite.texture.filterMode + " wrap=" + sprite.texture.wrapMode : "-"));
                    lines.Add("spriteRect=" + Lf(sprite.rect.x) + "," + Lf(sprite.rect.y) + "," + Lf(sprite.rect.width) + "," + Lf(sprite.rect.height)
                              + " textureRect=" + Lf(sprite.textureRect.x) + "," + Lf(sprite.textureRect.y) + "," + Lf(sprite.textureRect.width) + "," + Lf(sprite.textureRect.height)
                              + " pivot=" + Lf(sprite.pivot.x) + "," + Lf(sprite.pivot.y) + " ppu=" + Lf(sprite.pixelsPerUnit));
                    lines.Add("renderer=layer " + sr.sortingLayerName + " (" + sr.sortingLayerID + ") order " + sr.sortingOrder + " colour " + Lc(sr.color) + " flipX=" + sr.flipX + " flipY=" + sr.flipY);

                    // The art's four corners on screen: the sprite's rect, about
                    // its pivot, in its own units, through the object and camera.
                    if (cam != null)
                    {
                        float ppu = sprite.pixelsPerUnit;
                        Vector2 min = -sprite.pivot / ppu, max = (sprite.rect.size - sprite.pivot) / ppu;
                        var corners = new[] { new Vector3(min.x, min.y), new Vector3(max.x, min.y), new Vector3(min.x, max.y), new Vector3(max.x, max.y) }
                            .Select(c => cam.WorldToScreenPoint(body.transform.TransformPoint(c))).ToArray();
                        lines.Add("corners=" + string.Join(";", corners.Select(c => Lf(c.x) + "," + Lf(c.y)).ToArray()) + "  (uv 00, 10, 01, 11; screen pixels from the bottom left)");
                        // Where it is drawn: the main camera's Pixel Perfect Camera
                        // has the engine move every sprite, at render time, to the
                        // nearest multiple of this spacing (world units); the
                        // transform keeps its own position, so corners above miss it.
                        float snap = _snapWhileDrawing;
                        Vector3 at = body.transform.position, moved = Vector3.zero;
                        if (snap > 0f)
                            moved = new Vector3(Mathf.Round(at.x / snap) * snap - at.x, Mathf.Round(at.y / snap) * snap - at.y, 0f);
                        var drawn = new[] { new Vector3(min.x, min.y), new Vector3(max.x, min.y), new Vector3(min.x, max.y), new Vector3(max.x, max.y) }
                            .Select(c => cam.WorldToScreenPoint(body.transform.TransformPoint(c) + moved)).ToArray();
                        lines.Add("pixelSnapSpacing=" + Lf(snap) + " (while the main camera drew; after it: " + Lf(UnityEngine.U2D.PixelPerfectRendering.pixelSnapSpacing)
                                  + ") position=" + Lv(at) + " worldPerScreenPixel=" + Lf(2f * cam.orthographicSize / cam.pixelHeight));
                        lines.Add("drawnCorners=" + string.Join(";", drawn.Select(c => Lf(c.x) + "," + Lf(c.y)).ToArray()) + "  (corners as snapped to the spacing)");
                    }

                    var mat = sr.sharedMaterial;
                    if (mat != null)
                    {
                        lines.Add("material=" + mat.name + " shader=" + (mat.shader != null ? mat.shader.name : "-") + " keywords=" + string.Join(" ", mat.shaderKeywords));
                        foreach (var p in new[] { "_JiggleSpeed", "_JiggleStrength", "_JiggleFrequency", "_NoiseScale", "_NoiseSpeed", "_NoiseStrength", "_PixelSnap" })
                            if (mat.HasProperty(p)) lines.Add("mat" + p + "=" + Lf(mat.GetFloat(p)));
                        // A level's material (Sprite-Lit-Jiggle, 1.9.1: S10) has others: its
                        // jiggle's phase, its milking wave's count, direction and compression.
                        foreach (var p in new[] { "_JigglePhaseOffset", "_JiggleAmplitudeVariation", "_MilkingWaveCount", "_MilkingCompressionRatio" })
                            if (mat.HasProperty(p)) lines.Add("mat" + p + "=" + Lf(mat.GetFloat(p)));
                        foreach (var p in new[] { "_MilkingDirection", "_JiggleDirection" })
                            if (mat.HasProperty(p)) { var d = mat.GetVector(p); lines.Add("mat" + p + "=" + Lf(d.x) + "," + Lf(d.y) + "," + Lf(d.z) + "," + Lf(d.w)); }
                        foreach (var p in new[] { "_Color", "_RendererColor" })
                            if (mat.HasProperty(p)) lines.Add("mat" + p + "=" + Lc(mat.GetColor(p)));
                        if (mat.HasProperty("_MaskTex"))
                        {
                            var mask = mat.GetTexture("_MaskTex");
                            lines.Add("mask=" + (mask != null ? mask.name + " " + mask.width + "x" + mask.height + " filter=" + mask.filterMode : "-")
                                      + " st=" + Lv(mat.GetTextureScale("_MaskTex")) + "," + Lv(mat.GetTextureOffset("_MaskTex")));
                        }
                    }
                    foreach (var g in new[] { "_Global_GIEnabled", "_Global_GIIntensity", "_Global_GIBlurRadius", "_Global_GIBlurAmount", "_Global_GIEdgeWidth",
                                              "_Global_GISaturation", "_Global_GIBrightness", "_Global_RimPower", "_Global_RimSoftness" })
                        lines.Add(g + "=" + Lf(Shader.GetGlobalFloat(g)));
                    lines.Add("_Global_GITint=" + Lc(Shader.GetGlobalColor("_Global_GITint")));

                    lines.AddRange(Lights(sr.sortingLayerID, cam));
                    lines.Add(SaveScreen(Path.Combine(folder, "screen.png")));
                    lines.Add(_backgroundFormat.Length > 0 && _background != null
                        ? SaveTexture(_background, Path.Combine(folder, "background.png"), "background=" + _backgroundFormat)
                        : "background=(not copied while the camera drew; seen " + _backgroundSeen.Trim() + ")");
                    if (_flatBackground != null)
                        lines.Add("backgroundFlat=" + _flatBackground.name + " " + _flatBackground.width + "x" + _flatBackground.height
                                  + " " + _flatBackground.GetType().Name + " texels " + Texels(_flatBackground));
                }
            }
            catch (Exception e) { fail = e.GetType().Name + ": " + e.Message; }
            if (fail != null) { Say("error bust-look: " + fail); yield break; }
            File.WriteAllLines(Path.Combine(folder, "info.txt"), lines.ToArray());
            Say("ok bust-look " + name + " " + folder);
        }

        /// <summary>Renderer2D.m_CurrentColorHandle: which of _CameraTargetAttachmentA
        /// and B the camera draws its scene into now (-1 when it cannot be read).</summary>
        private static int CurrentColorHandle()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("UnityEngine.Rendering.Universal.Renderer2D", false)).FirstOrDefault(t => t != null);
            var field = type?.GetField("m_CurrentColorHandle", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return field != null ? (int)field.GetValue(null) : -1;
        }

        private System.Collections.IEnumerator SaveRenderTexture(string nameStart, string file)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            _waitingFor = null;
            // "_CameraTarget": the one of the camera's two HDR targets (A, B) it
            // drew into last. URP's 2D renderer takes turns between them - every
            // frame while post-processing runs, which writes into the other -
            // so a fixed name read a stale frame about half the time.
            if (nameStart == "_CameraTarget") nameStart = "_CameraTargetAttachment" + (CurrentColorHandle() == 1 ? "B" : "A");
            var rt = Resources.FindObjectsOfTypeAll<RenderTexture>().FirstOrDefault(r => r != null && r.IsCreated() && r.name.StartsWith(nameStart, StringComparison.Ordinal));
            if (rt == null) { Say("error rt-save: no render texture named " + nameStart + "*"); yield break; }
            var full = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(rt, full);
            var before = RenderTexture.active;
            RenderTexture.active = full;
            var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            read.Apply();
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(full);
            var px = read.GetPixels();
            UnityEngine.Object.Destroy(read);
            using (var w = new BinaryWriter(File.Create(Path.Combine(_dir, file))))
            {
                w.Write(rt.width); w.Write(rt.height);
                foreach (var c in px) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
            }
            Say("ok rt-save " + rt.name + " " + rt.width + "x" + rt.height + " " + rt.graphicsFormat);
        }

        private System.Collections.IEnumerator ScreenLook(string name)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            _waitingFor = null;
            string folder = Path.Combine(_dir, "looks", name);
            var lines = new List<string>();
            string fail = null;
            try
            {
                Directory.CreateDirectory(folder);
                var cam = Camera.main;
                lines.Add("screen=" + Screen.width + "x" + Screen.height);
                if (cam != null)
                    lines.Add("camera=" + cam.name + " ortho=" + cam.orthographic + " size=" + Lf(cam.orthographicSize) + " pos=" + Lv(cam.transform.position));
                foreach (var g in new[] { "_Global_GIEnabled", "_Global_GIIntensity", "_Global_GIBlurRadius", "_Global_GIBlurAmount", "_Global_GIEdgeWidth",
                                          "_Global_GISaturation", "_Global_GIBrightness", "_Global_RimPower", "_Global_RimSoftness" })
                    lines.Add(g + "=" + Lf(Shader.GetGlobalFloat(g)));
                lines.AddRange(Lights(SortingLayer.NameToID("Bust"), cam));
                // What draws, by sorting layer, in the layers' own order.
                var shown = UnityEngine.Object.FindObjectsOfType<Renderer>()
                    .Where(r => r != null && r.isVisible && r.enabled && r.gameObject.activeInHierarchy).ToList();
                foreach (var layer in SortingLayer.layers)
                {
                    var on = shown.Where(r => r.sortingLayerID == layer.id).OrderBy(r => r.sortingOrder).ToList();
                    if (on.Count == 0) continue;
                    lines.Add("layer=" + layer.name + " value " + layer.value + " id " + layer.id + ": " + on.Count + " - "
                              + string.Join(", ", on.Take(12).Select(r => FullPath(r.transform) + " [" + r.sortingOrder + (r.sharedMaterial != null ? " " + r.sharedMaterial.name : "") + "]").ToArray()));
                }
                lines.Add(SaveScreen(Path.Combine(folder, "screen.png")));
            }
            catch (Exception e) { fail = e.GetType().Name + ": " + e.Message; }
            if (fail != null) { Say("error screen-look: " + fail); yield break; }
            File.WriteAllLines(Path.Combine(folder, "info.txt"), lines.ToArray());
            Say("ok screen-look " + name + " " + folder);
        }

        /// <summary>Every 2D light that is on: what it is, where it is on screen,
        /// and whether it lights the given sorting layer.</summary>
        private static IEnumerable<string> Lights(int layerId, Camera cam)
        {
            var type = Light2DType();
            if (type == null) { yield return "lights=? (no Light2D type)"; yield break; }
            var layersField = type.GetField("m_ApplyToSortingLayers", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
            {
                var light = obj as Behaviour;
                if (light == null || !light.isActiveAndEnabled) continue;
                var layers = layersField != null ? layersField.GetValue(light) as int[] : null;
                bool lights = layers != null && layers.Contains(layerId);
                object Get(string member)
                {
                    var p = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
                    try { return p != null ? p.GetValue(light, null) : null; } catch { return null; }
                }
                var colour = Get("color");
                var pos = cam != null ? cam.WorldToScreenPoint(light.transform.position) : Vector3.zero;
                yield return "light=" + FullPath(light.transform) + " type=" + Get("lightType") + " lightsBody=" + lights
                             + " colour=" + (colour is Color c ? Lc(c) : "?") + " intensity=" + Get("intensity") + " blendStyle=" + Get("blendStyleIndex")
                             + " outer=" + Get("pointLightOuterRadius") + " inner=" + Get("pointLightInnerRadius") + " falloff=" + Get("falloffIntensity")
                             + " screen=" + Lf(pos.x) + "," + Lf(pos.y);
            }
        }

        private static string SaveScreen(string file)
        {
            var capture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
            var method = capture != null ? capture.GetMethod("CaptureScreenshotAsTexture", Type.EmptyTypes) : null;
            var texture = method != null ? method.Invoke(null, null) as Texture2D : null;
            if (texture == null) return "screenPng=(not captured)";
            try { File.WriteAllBytes(file, ImageConversion.EncodeToPNG(texture)); return "screenPng=" + file + " " + texture.width + "x" + texture.height; }
            finally { UnityEngine.Object.Destroy(texture); }
        }

        /// <summary>A render texture's pixels, as a screen shows their numbers, to a PNG.</summary>
        private static string SaveTexture(RenderTexture rt, string file, string label)
        {
            var before = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                read.Apply();
                try { File.WriteAllBytes(file, ImageConversion.EncodeToPNG(read)); }
                finally { UnityEngine.Object.Destroy(read); }
                return label + " png=" + file;
            }
            finally { RenderTexture.active = before; }
        }

        private static string Lf(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>A world point on the main camera's screen, pixels from the bottom left.</summary>
        private static string ScreenOf(Vector3 world)
        {
            var cam = Camera.main;
            if (cam == null) return "-";
            var sc = cam.WorldToScreenPoint(world);
            return Lf(sc.x) + "," + Lf(sc.y);
        }
        private static string Lv(Vector3 v) => Lf(v.x) + "," + Lf(v.y) + "," + Lf(v.z);
        private static string Lv(Vector2 v) => Lf(v.x) + "," + Lf(v.y);
        private static string Lv4(Vector4 v) => Lf(v.x) + "," + Lf(v.y) + "," + Lf(v.z) + "," + Lf(v.w);
        private static string Lc(Color c) => Lf(c.r) + "," + Lf(c.g) + "," + Lf(c.b) + "," + Lf(c.a);
    }
}

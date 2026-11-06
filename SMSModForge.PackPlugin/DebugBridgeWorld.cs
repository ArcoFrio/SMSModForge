using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The bridge's read-back commands for the in-game suite (1.8.0, 12.13):
    /// what a pack put in the game, read from the game itself - objects active
    /// or not, where they are and what they draw, the pack's variables, which
    /// map is up - and the two ways in a player has, a click and a journey.
    /// A check judges what these say, never a picture alone.
    /// <list type="bullet">
    /// <item><c>find &lt;path&gt;</c>: one object, inactive ones too. The path
    /// starts at a root object; a segment may start or end with <c>*</c>
    /// (<c>5_Levels/*_CheckRoom</c>: a pack place's index is given out at
    /// load).</item>
    /// <item><c>find-all &lt;name&gt;</c>: every object of that name, as paths.</item>
    /// <item><c>pvar &lt;pack&gt; &lt;name&gt;</c> / <c>pset &lt;pack&gt; &lt;name&gt; &lt;value&gt;</c>:
    /// a pack's variable.</item>
    /// <item><c>level</c>: the maps that are up.</item>
    /// <item><c>travel &lt;place token&gt;</c>: the journey a navigator button
    /// starts (<c>vanilla:14_Beach</c>, <c>pack:&lt;pack&gt;.&lt;key&gt;</c>).</item>
    /// <item><c>press &lt;path&gt;</c>: a player's click on the object - the
    /// pointer's enter, down, up and click, to whatever on it listens.</item>
    /// <item><c>quest &lt;pack&gt; &lt;quest&gt; [task,task]</c>: where the journal
    /// has a pack's quest and its tasks, and their counts. <c>gquest &lt;pack&gt;
    /// &lt;task,task&gt; &lt;game quest&gt;</c>: the tasks a pack adds to one of the
    /// game's quests.</item>
    /// <item><c>set-active &lt;path&gt; true|false</c>: one of the game's screens
    /// opened or closed, when how a player gets there is not what is checked.</item>
    /// <item><c>line</c> / <c>next</c>: the conversation on screen - speaker,
    /// line, choices - and a player's click to go on. <c>warning</c>:
    /// ModForge's save warning, if it is up, and its words.</item>
    /// <item><c>members &lt;type&gt;</c>: what one of the game's types offers.</item>
    /// <item><c>fields &lt;component&gt; &lt;path&gt;</c>: every field a component on
    /// the object holds, private ones too.</item>
    /// <item><c>find-text &lt;words&gt;</c> / <c>press-text &lt;words&gt;</c>: the
    /// same, for the object those words are written on - a button by what it
    /// says, as a player finds it (two of a pack's navigator buttons to one
    /// place share an object name).</item>
    /// </list>
    /// </summary>
    internal sealed partial class DebugBridge
    {
        /// <summary>The commands in this file; false when the verb is none of them.</summary>
        private bool RunWorld(string verb, string a, string rest)
        {
            string all = (a + " " + rest).Trim();
            switch (verb)
            {
                case "find":
                {
                    // Read as drawn - after every LateUpdate, where a moved
                    // object is held (PackMover) - and nothing else runs first.
                    _waitingFor = "frame";
                    _waitDeadline = Time.unscaledTime + 5f;
                    StartCoroutine(FindAsDrawn(all));
                    return true;
                }
                case "find-all":
                {
                    var paths = SceneRoots().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                        .Where(t => Matches(t.name, all)).Take(20).Select(FullPath).ToArray();
                    Say("ok find-all " + all + ": " + (paths.Length == 0 ? "(none)" : string.Join(" | ", paths)));
                    return true;
                }
                case "pvar":
                {
                    var ctx = Plugin.FindContext(a);
                    string name = rest.Trim();
                    if (ctx == null || ctx.Vars == null) { Say("error pvar: no pack " + a); return true; }
                    if (!ctx.Vars.Exists(name)) { Say("error pvar: " + a + " has no variable " + name); return true; }
                    Say("ok pvar " + a + "." + name + " = " + ctx.Vars.GetString(name));
                    return true;
                }
                case "pset":
                {
                    var ctx = Plugin.FindContext(a);
                    var words = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (ctx == null || ctx.Vars == null || words.Length == 0) { Say("error pset: no pack " + a); return true; }
                    if (!ctx.Vars.Exists(words[0])) { Say("error pset: " + a + " has no variable " + words[0]); return true; }
                    // Set answers whether the value is one a save keeps, not whether it was set.
                    ctx.Vars.Set(words[0], words.Length > 1 ? words[1] : "");
                    Say("ok pset " + a + "." + words[0] + " = " + ctx.Vars.GetString(words[0]));
                    return true;
                }
                case "font-atlas":
                {
                    // font-atlas <font name>: what the game really draws this font
                    // from - its shader, its textures and their pixels as a PNG, and
                    // the glyph table - for comparing with the editor's copy.
                    string fontName = (a + " " + rest).Trim();
                    if (fontName == "*")
                    {
                        // Every font that draws from a texture other than its atlas:
                        // an RGBA picture of its letters, outline and shadow baked in.
                        var coloured = new List<string>();
                        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                        {
                            if (f == null || f.material == null || !f.material.HasProperty("_MainTex")) continue;
                            var drawnFrom = f.material.GetTexture("_MainTex");
                            if (drawnFrom == null || drawnFrom == f.atlasTexture) continue;
                            var rt = RenderTexture.GetTemporary(drawnFrom.width, drawnFrom.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                            var before = RenderTexture.active;
                            Graphics.Blit(drawnFrom, rt);
                            RenderTexture.active = rt;
                            var read = new Texture2D(drawnFrom.width, drawnFrom.height, TextureFormat.RGBA32, false);
                            read.ReadPixels(new Rect(0, 0, drawnFrom.width, drawnFrom.height), 0, 0);
                            read.Apply();
                            RenderTexture.active = before;
                            RenderTexture.ReleaseTemporary(rt);
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(_dir, "shots", "colour-" + f.name + ".png"), ImageConversion.EncodeToPNG(read));
                            Destroy(read);
                            coloured.Add(f.name + " [" + f.material.shader.name + "] " + drawnFrom.width + "x" + drawnFrom.height);
                        }
                        Say("ok font-atlas colour: " + string.Join("; ", coloured.ToArray()));
                        return true;
                    }
                    var font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name == fontName);
                    if (font == null) { Say("error font-atlas: not loaded: " + fontName); return true; }
                    var material = font.material;
                    var parts = new List<string>
                    {
                        "shader " + (material != null && material.shader != null ? material.shader.name : "(none)"),
                        "material " + (material != null ? material.name : "(none)"),
                        "mode " + font.atlasPopulationMode,
                        "padding " + font.atlasPadding,
                    };
                    var seen = new HashSet<Texture>();
                    void Dump(string slot, Texture texture)
                    {
                        if (texture == null || !seen.Add(texture)) return;
                        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        var before = RenderTexture.active;
                        Graphics.Blit(texture, rt);
                        RenderTexture.active = rt;
                        var read = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                        read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                        read.Apply();
                        RenderTexture.active = before;
                        RenderTexture.ReleaseTemporary(rt);
                        string file = "atlas-" + fontName + "-" + slot + ".png";
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(_dir, "shots", file), ImageConversion.EncodeToPNG(read));
                        Destroy(read);
                        parts.Add(slot + " " + texture.name + " " + texture.width + "x" + texture.height + " -> " + file);
                    }
                    Dump("atlas", font.atlasTexture);
                    if (material != null)
                        foreach (string slot in material.GetTexturePropertyNames())
                            Dump(slot, material.GetTexture(slot));
                    Say("ok font-atlas " + string.Join("; ", parts.ToArray()));
                    return true;
                }
                case "text-info":
                {
                    // text-info <path>: how a text object is really being drawn.
                    string where = (a + " " + rest).Trim();
                    GameObject go;
                    if (where.StartsWith("says:", StringComparison.Ordinal))
                    {
                        string says = where.Substring(5);
                        var saying = FindObjectsOfType<TMP_Text>().FirstOrDefault(t => t.text != null && t.text.Contains(says));
                        go = saying != null ? saying.gameObject : null;
                    }
                    else go = TransformExtensions.ResolveGameObject(where);
                    var tmp = go != null ? go.GetComponent<TMP_Text>() : null;
                    if (tmp == null) { Say("error text-info: no text at " + where); return true; }
                    tmp.ForceMeshUpdate();
                    var info = tmp.textInfo;
                    var used = new List<string>();
                    var fontsSeen = new HashSet<string>();
                    if (info != null)
                        for (int i = 0; i < info.characterCount; i++)
                        {
                            var c = info.characterInfo[i];
                            string asset = c.fontAsset != null ? c.fontAsset.name : "(none)";
                            if (!fontsSeen.Add(asset) && i > 0 && c.character != '’') continue;
                            used.Add("'" + c.character + "' " + asset + " scale " + N(c.scale, "0.###")
                                     + " from " + N(c.origin, "0.##") + " to " + N(c.xAdvance, "0.##"));
                        }
                    if (info != null)
                        for (int i = 0; i < info.lineCount; i++)
                            used.Add("line " + i + " " + info.lineInfo[i].firstCharacterIndex + "-" + info.lineInfo[i].lastCharacterIndex
                                     + " advance " + N(info.characterInfo[info.lineInfo[i].lastVisibleCharacterIndex].xAdvance
                                                       - info.characterInfo[info.lineInfo[i].firstCharacterIndex].origin, "0.##"));
                    var mat = tmp.fontSharedMaterial;
                    var rendering = tmp.materialForRendering;
                    var props = new List<string>();
                    foreach (var m in new[] { mat, rendering })
                    {
                        if (m == null) continue;
                        props.Add(m.name + " [" + (m.shader != null ? m.shader.name : "-") + "] keywords " + string.Join(",", m.shaderKeywords));
                        foreach (string f in new[] { "_OutlineWidth", "_FaceDilate", "_UnderlayDilate", "_UnderlayOffsetX", "_UnderlayOffsetY", "_UnderlaySoftness", "_WeightNormal", "_GradientScale" })
                            if (m.HasProperty(f)) props.Add(f + "=" + N(m.GetFloat(f), "0.###"));
                        foreach (string t in m.GetTexturePropertyNames())
                            if (m.GetTexture(t) != null) props.Add(t + "=" + m.GetTexture(t).name);
                    }
                    Say("ok text-info font " + (tmp.font != null ? tmp.font.name : "(none)")
                        + "; atlas " + (tmp.font != null && tmp.font.atlasTexture != null ? tmp.font.atlasTexture.name : "-")
                        + "; size " + N(tmp.fontSize, "0.##") + "; style " + tmp.fontStyle + "; auto " + tmp.enableAutoSizing
                        + "; effects " + string.Join(",", go.GetComponents<UnityEngine.UI.BaseMeshEffect>().Select(e => e.GetType().Name).ToArray())
                        + "; used " + string.Join(" | ", used.ToArray())
                        + "; material " + string.Join(" ", props.ToArray()));
                    return true;
                }
                case "image-info":
                {
                    // image-info <path>: how a picture is really being drawn.
                    string where = (a + " " + rest).Trim();
                    var go = FindPath(where);
                    var image = go != null ? go.GetComponent<UnityEngine.UI.Image>() : null;
                    if (image == null) { Say("error image-info: no picture at " + where); return true; }
                    var s = image.overrideSprite ?? image.sprite;
                    var parts = new List<string>
                    {
                        "type " + image.type,
                        "colour " + Hex(image.color),
                        "rendered " + Hex(image.canvasRenderer != null ? image.canvasRenderer.GetColor() : Color.white),
                        "aspect " + image.preserveAspect,
                        "ppuMul " + N(image.pixelsPerUnitMultiplier, "0.##"),
                        "size " + N(((RectTransform)image.transform).rect.width, "0.#") + "x" + N(((RectTransform)image.transform).rect.height, "0.#"),
                        "material " + (image.material != null ? image.material.name : "-"),
                    };
                    if (s != null)
                    {
                        var p = UnityEngine.Sprites.DataUtility.GetPadding(s);
                        parts.Add("sprite " + s.name + " rect " + s.rect.width + "x" + s.rect.height + " at " + s.rect.x + "," + s.rect.y
                                  + " texture " + (s.texture != null ? s.texture.name : "-")
                                  + " border " + s.border.x + "," + s.border.y + "," + s.border.z + "," + s.border.w
                                  + " ppu " + N(s.pixelsPerUnit, "0.##") + " padding " + p.x + "," + p.y + "," + p.z + "," + p.w + " packed " + s.packed);
                    }
                    foreach (var selectable in go.GetComponentsInParent<UnityEngine.UI.Selectable>(true))
                        if (selectable.targetGraphic == image)
                            parts.Add("target of " + selectable.name + " " + selectable.transition + " normal " + Hex(selectable.colors.normalColor)
                                      + " disabled " + Hex(selectable.colors.disabledColor) + " x" + N(selectable.colors.colorMultiplier, "0.##")
                                      + " interactable " + selectable.interactable);
                    Say("ok image-info " + string.Join("; ", parts.ToArray()));
                    return true;
                }
                case "sprite-dump":
                {
                    // sprite-dump: every sprite named in sprites-wanted.txt beside the
                    // results, its pixels at full quality into shots/sprites, and its
                    // sizes into shots/sprites.tsv - what the game really draws from.
                    string wantedFile = System.IO.Path.Combine(_dir, "sprites-wanted.txt");
                    if (!System.IO.File.Exists(wantedFile)) { Say("error sprite-dump: no " + wantedFile); return true; }
                    var wanted = new HashSet<string>(System.IO.File.ReadAllLines(wantedFile));
                    string into = System.IO.Path.Combine(_dir, "shots", "sprites");
                    System.IO.Directory.CreateDirectory(into);
                    var table = new List<string>();
                    var readable = new Dictionary<Texture2D, Texture2D>();
                    int written = 0, failed = 0;
                    foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                    {
                        if (s == null || s.texture == null || !wanted.Contains(s.name)) continue;
                        try
                        {
                            Texture2D whole;
                            if (!readable.TryGetValue(s.texture, out whole))
                            {
                                var rt = RenderTexture.GetTemporary(s.texture.width, s.texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                                var before = RenderTexture.active;
                                Graphics.Blit(s.texture, rt);
                                RenderTexture.active = rt;
                                whole = new Texture2D(s.texture.width, s.texture.height, TextureFormat.RGBA32, false);
                                whole.ReadPixels(new Rect(0, 0, s.texture.width, s.texture.height), 0, 0);
                                whole.Apply();
                                RenderTexture.active = before;
                                RenderTexture.ReleaseTemporary(rt);
                                readable[s.texture] = whole;
                            }
                            var r = s.textureRect;
                            int x = Mathf.FloorToInt(r.x), y = Mathf.FloorToInt(r.y);
                            int w = Mathf.Clamp(Mathf.CeilToInt(r.xMax) - x, 1, whole.width - x);
                            int h = Mathf.Clamp(Mathf.CeilToInt(r.yMax) - y, 1, whole.height - y);
                            var crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
                            crop.SetPixels(whole.GetPixels(x, y, w, h));
                            crop.Apply();
                            string file = written + ".png";
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(into, file), ImageConversion.EncodeToPNG(crop));
                            Destroy(crop);
                            var p = UnityEngine.Sprites.DataUtility.GetPadding(s);
                            table.Add(string.Join("\t", new[]
                            {
                                s.name, s.texture.name, file,
                                N(r.x, "0.#####"), N(r.y, "0.#####"), N(r.width, "0.#####"), N(r.height, "0.#####"),
                                N(s.rect.width, "0.#####"), N(s.rect.height, "0.#####"),
                                N(p.x, "0.#####"), N(p.y, "0.#####"), N(p.z, "0.#####"), N(p.w, "0.#####"),
                                N(s.pixelsPerUnit, "0.###"),
                            }));
                            written++;
                        }
                        catch (Exception e) { failed++; table.Add("# " + s.name + ": " + e.Message); }
                    }
                    foreach (var t in readable.Values) Destroy(t);
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(_dir, "shots", "sprites.tsv"), table.ToArray());
                    Say("ok sprite-dump " + written + " written, " + failed + " failed, of " + wanted.Count + " names");
                    return true;
                }
                case "ui-objects":
                {
                    // ui-objects: every UI object in the scene as the game has it right
                    // now, one line each, into shots/ui-objects.tsv - to compare with
                    // the editor's extraction.
                    var rows = new List<string>();
                    foreach (var rt in Resources.FindObjectsOfTypeAll<RectTransform>())
                    {
                        if (rt == null || !rt.gameObject.scene.IsValid()) continue;
                        var img = rt.GetComponent<UnityEngine.UI.Image>();
                        var label = rt.GetComponent<TMP_Text>();
                        string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
                        rows.Add(string.Join("\t", new[]
                        {
                            FullPath(rt),
                            rt.GetSiblingIndex().ToString(),
                            rt.gameObject.activeSelf ? "1" : "0",
                            F(rt.anchorMin.x), F(rt.anchorMin.y), F(rt.anchorMax.x), F(rt.anchorMax.y),
                            F(rt.pivot.x), F(rt.pivot.y),
                            F(rt.anchoredPosition.x), F(rt.anchoredPosition.y),
                            F(rt.sizeDelta.x), F(rt.sizeDelta.y),
                            F(rt.localScale.x), F(rt.localScale.y),
                            F(rt.localEulerAngles.z),
                            img != null && img.sprite != null ? img.sprite.name : "",
                            label != null ? label.font != null ? label.font.name : "-" : "",
                            label != null ? F(label.fontSize) : "",
                            label != null ? (label.text ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ") : "",
                        }));
                    }
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(_dir, "shots", "ui-objects.tsv"), rows.ToArray());
                    Say("ok ui-objects " + rows.Count);
                    return true;
                }
                case "font-spacing":
                {
                    // font-spacing: each font's own letter spacing, into
                    // shots/font-spacing.tsv - the export does not carry it.
                    var rows = new List<string>();
                    foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                        if (f != null)
                            rows.Add(f.name + "\t" + f.normalSpacingOffset.ToString(CultureInfo.InvariantCulture)
                                     + "\t" + f.boldSpacing.ToString(CultureInfo.InvariantCulture));
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(_dir, "shots", "font-spacing.tsv"), rows.ToArray());
                    Say("ok font-spacing " + rows.Count);
                    return true;
                }
                case "selectable-tints":
                {
                    // Every Selectable whose resting colour is not plain white: what
                    // the game multiplies its picture by, which the picture's own
                    // colour does not say.
                    var seenColours = new Dictionary<string, int>();
                    var lines = new List<string>();
                    int total = 0;
                    foreach (var selectable in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Selectable>())
                    {
                        if (selectable == null || !selectable.gameObject.scene.IsValid()) continue;
                        total++;
                        if (selectable.transition != UnityEngine.UI.Selectable.Transition.ColorTint || selectable.targetGraphic == null) continue;
                        var c = selectable.interactable ? selectable.colors.normalColor : selectable.colors.disabledColor;
                        c *= selectable.colors.colorMultiplier;
                        Color32 c32 = c;
                        string hex = "#" + c32.r.ToString("X2") + c32.g.ToString("X2") + c32.b.ToString("X2") + c32.a.ToString("X2");
                        int had;
                        seenColours.TryGetValue(hex, out had);
                        seenColours[hex] = had + 1;
                        if (hex != "#FFFFFFFF")
                        {
                            var down = new List<string>();
                            for (var t = selectable.targetGraphic.transform; t != null; t = t.parent)
                                down.Insert(0, t.GetSiblingIndex().ToString());
                            lines.Add(FullPath(selectable.targetGraphic.transform) + "\t" + string.Join("/", down.ToArray()) + "\t" + hex);
                        }
                    }
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(_dir, "shots", "selectable-tints.txt"), lines.ToArray());
                    Say("ok selectable-tints of " + total + ": " + string.Join(", ", seenColours.Select(kv => kv.Key + " x" + kv.Value).ToArray()));
                    return true;
                }
                case "resolution":
                {
                    // resolution <width> <height>: the window's size, for a check that
                    // compares pictures at one canvas pixel to one screen pixel.
                    int width, height;
                    if (!int.TryParse(a, out width) || !int.TryParse(rest.Trim(), out height)) { Say("error resolution: resolution <width> <height>"); return true; }
                    Screen.SetResolution(width, height, false);
                    Say("ok resolution " + width + "x" + height);
                    return true;
                }
                case "level":
                {
                    var levels = GameObject.Find("5_Levels");
                    if (levels == null) { Say("error level: no 5_Levels"); return true; }
                    var up = new List<string>();
                    foreach (Transform child in levels.transform)
                        if (child.gameObject.activeSelf) up.Add(child.name);
                    Say("ok level " + (up.Count == 0 ? "(none)" : string.Join(", ", up.ToArray())));
                    return true;
                }
                case "travel":
                {
                    var level5 = GameObject.Find("5_Levels");
                    var entry = PlaceRegistry.Resolve(all, null, level5 != null ? level5.transform : null);
                    if (entry == null) { Say("error travel: no place " + all); return true; }
                    if (!NavigatorRuntime.Travel(entry.AbsoluteSiblingIndex, entry.RoomTalk, null))
                    { Say("error travel: the game is locked (Lock-Game)"); return true; }
                    Say("ok travel " + all + " (map " + entry.AbsoluteSiblingIndex + ")");
                    return true;
                }
                case "press":
                {
                    var target = FindPath(all);
                    if (target == null) { Say("error press: no " + all); return true; }
                    Press(target, "press " + all);
                    return true;
                }
                case "quest":
                {
                    // quest <pack> <quest key> [task,task]: where the journal has
                    // a pack's quest and its tasks - state, and count.
                    var words = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    var quest = words.Length > 0 ? QuestRegistry.Find(a, words[0]) : null;
                    var journal = QuestRegistry.FindJournal();
                    if (quest == null || journal == null) { Say("error quest: no quest " + a + "." + rest + (journal == null ? " (no journal)" : "")); return true; }
                    var parts = new List<string> { "quest=" + journal.GetQuestState(quest) };
                    if (words.Length > 1)
                        foreach (var task in words[1].Split(',').Select(t => t.Trim()).Where(t => t.Length > 0))
                        {
                            int id = QuestRegistry.TaskId(a, words[0], task);
                            parts.Add(task + "=" + journal.GetTaskState(quest, id) + " " + N((float)journal.GetTaskValue(quest, id), "0.##"));
                        }
                    Say("ok quest " + a + "." + words[0] + ": " + string.Join("; ", parts.ToArray()));
                    return true;
                }
                case "gquest":
                {
                    // gquest <pack> <task key,task key> <the game's quest>: tasks a
                    // pack adds to one of the game's quests - there or not, and
                    // where the journal has them.
                    var words = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    var quest = words.Length > 1 ? QuestRegistry.FindVanilla(words[1].Trim()) : null;
                    var journal = QuestRegistry.FindJournal();
                    if (quest == null || journal == null) { Say("error gquest: no game quest " + rest); return true; }
                    var parts = new List<string> { "quest=" + journal.GetQuestState(quest) };
                    foreach (var key in words[0].Split(',').Select(k => k.Trim()).Where(k => k.Length > 0))
                    {
                        int id = SMSModForge.Shared.QuestIds.AddedTaskId(a, quest.name, key);
                        parts.Add(key + (quest.GetTask(id) == null ? "=(none)" : "=" + journal.GetTaskState(quest, id)));
                    }
                    Say("ok gquest " + a + " " + quest.name + ": " + string.Join("; ", parts.ToArray()));
                    return true;
                }
                case "set-active":
                {
                    // set-active <path> true|false: one of the game's screens
                    // opened or closed, where getting there is not what is checked.
                    int cut = all.LastIndexOf(' ');
                    var target = cut > 0 ? FindPath(all.Substring(0, cut)) : null;
                    bool on;
                    if (target == null || !bool.TryParse(all.Substring(cut + 1), out on)) { Say("error set-active: " + all); return true; }
                    target.SetActive(on);
                    Say("ok set-active " + FullPath(target.transform) + " " + on);
                    return true;
                }
                case "fields":
                {
                    // fields <component type> <path>: every field the component
                    // holds, its private ones too - the game's own components
                    // keep what a check needs (a parallax's home) to themselves.
                    var target = FindPath(rest);
                    if (target == null) { Say("error fields: no " + rest); return true; }
                    var component = target.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == a);
                    if (component == null) { Say("error fields: no " + a + " on " + rest); return true; }
                    var values = new List<string>();
                    for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(Component); type = type.BaseType)
                        foreach (var f in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                                         | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                        {
                            object v;
                            try { v = f.GetValue(component); } catch (Exception) { continue; }
                            values.Add(f.Name + "=" + Show(v));
                        }
                    var behaviour = component as Behaviour;
                    Say("ok fields " + a + " " + rest + ": enabled=" + (behaviour == null || behaviour.enabled) + "; " + string.Join("; ", values.ToArray()));
                    return true;
                }
                case "line":
                {
                    // The conversation on screen: which, at which node, and every
                    // word its box shows - speaker, line, choices.
                    var d = GameCreator.Runtime.Dialogue.Dialogue.Current;
                    if (d == null) { Say("ok line (none)"); return true; }
                    int node = -1;
                    var story = d.Story;
                    var id = story == null ? null : typeof(GameCreator.Runtime.Dialogue.Story).GetField("m_CurrentId",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (id != null) node = (int)id.GetValue(story);
                    // The skin's box is a root object of its own ("Dialogue_..."),
                    // its speech and its choices beside each other in it.
                    var words = new List<string>();
                    foreach (var root in SceneRoots().Where(r => r.gameObject.activeInHierarchy && r.name.StartsWith("Dialogue_", StringComparison.Ordinal)))
                    {
                        // Where the box sorts among the canvases: what a transition
                        // has to be drawn above.
                        var canvas = root.GetComponentInChildren<Canvas>();
                        if (canvas != null) words.Add("box: order " + canvas.rootCanvas.sortingOrder);
                        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
                        {
                            if (string.IsNullOrEmpty(t.text) || t.text.Trim().Length == 0) continue;
                            string path = FullPath(t.transform);
                            string kind = path.Contains("/Choices/") ? "choice" : t.name == "Actor" ? "speaker"
                                        : path.Contains("/Speech/") ? "line" : "other";
                            words.Add(kind + ": " + t.text.Replace("\n", "\\n"));
                        }
                    }
                    Say("ok line " + FullPath(d.transform) + " node " + node + " || " + string.Join(" | ", words.ToArray()));
                    return true;
                }
                case "next":
                {
                    // What a player's click on the box does: the line goes on.
                    var d = GameCreator.Runtime.Dialogue.Dialogue.Current;
                    if (d == null || d.Story == null) { Say("error next: no conversation"); return true; }
                    d.Story.Continue();
                    Say("ok next");
                    return true;
                }
                case "notice":
                {
                    // ModForge's notice on the main menu, if it is up, and its words.
                    var notice = SceneRoots().FirstOrDefault(r => r.name == "ModNotice ModForge" && r.gameObject.activeInHierarchy);
                    if (notice == null) { Say("ok notice (none)"); return true; }
                    Say("ok notice open: " + string.Join(" | ", notice.GetComponentsInChildren<TMP_Text>(false)
                        .Select(t => t.text.Replace("\n", "\\n")).Where(t => t.Trim().Length > 0).ToArray()));
                    return true;
                }
                case "drop-pack":
                {
                    // drop-pack <pack> <gap>: the pack let go of at that gap in
                    // the main menu's list, as a player's drag ends.
                    int gap;
                    if (Plugin.Instance == null || !int.TryParse(rest.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out gap)
                        || !Plugin.Instance.DropPack(a, gap))
                    { Say("error drop-pack: " + all); return true; }
                    Say("ok drop-pack " + a + " at " + gap + ": order now " + PackOrderSetting.Setting);
                    return true;
                }
                case "lang":
                {
                    // lang <code>: the language chosen on the main menu.
                    PluginLanguage.Switch(all, Plugin.Log);
                    Say("ok lang " + PluginLanguage.Code);
                    return true;
                }
                case "warning":
                {
                    if (a == "go-on")
                    {
                        // Continue, in whatever language ModForge speaks.
                        var go = SceneRoots().Where(r => r.name.StartsWith("SMSModForge_", StringComparison.Ordinal) && r.gameObject.activeInHierarchy)
                            .SelectMany(r => r.GetComponentsInChildren<ForgeButton>(false))
                            .FirstOrDefault(b => b.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Trim() == SMSModForge.Shared.SaveWarningText.ContinueLabel));
                        if (go == null) { Say("error warning: no Continue on screen"); return true; }
                        go.Press();
                        Say("ok warning go-on");
                        return true;
                    }
                    // ModForge's save warning, if it is up, and what it says.
                    if (!SaveLoadWarning.IsOpen) { Say("ok warning " + (SaveLoadWarning.IsWaiting ? "(waiting)" : "(none)")); return true; }
                    var window = SceneRoots().FirstOrDefault(r => r.GetComponentsInChildren<ForgeButton>(false).Any()
                                                                  && r.name.StartsWith("SMSModForge", StringComparison.Ordinal));
                    var texts = window == null ? new string[0] : window.GetComponentsInChildren<TMP_Text>(false)
                        .Select(t => t.text.Replace("\n", "\\n")).Where(t => t.Trim().Length > 0).ToArray();
                    Say("ok warning open: " + string.Join(" | ", texts));
                    return true;
                }
                case "scripts":
                {
                    // scripts <path>: every script on an object and under it,
                    // switched on or not, written to a file in the BepInEx folder
                    // (ScriptDump) - what one of the game's scripts does.
                    var target = FindPath(all);
                    if (target == null) { Say("error scripts: no " + all); return true; }
                    // Named after the object: several asked for in one second
                    // would otherwise write over each other.
                    string label = "scripts-" + new string(target.name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                    Say("ok scripts " + ScriptDump.WriteUnder(target, Plugin.Log, label));
                    return true;
                }
                case "members":
                {
                    // members <type's full name>: what a type of the game's offers
                    // - for finding the way in to something a check needs.
                    var type = AppDomain.CurrentDomain.GetAssemblies().Select(asm => asm.GetType(all, false)).FirstOrDefault(t => t != null);
                    if (type == null) { Say("error members: no type " + all); return true; }
                    var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                              | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
                    Say("ok members " + all + " (base " + type.BaseType + "): "
                        + string.Join(" | ", type.GetMembers(flags).Select(m => m.MemberType + " " + m).ToArray()));
                    return true;
                }
                case "find-text":
                {
                    // The object a piece of text belongs to - a button by its words.
                    var texts = Texts(all, onScreenOnly: false);
                    if (texts.Count == 0) { Say("error find-text: no text '" + all + "'"); return true; }
                    var owner = texts[0].transform.parent != null ? texts[0].transform.parent.gameObject : texts[0].gameObject;
                    Say("ok find-text " + all + ": " + Describe(owner) + "; matches " + texts.Count);
                    return true;
                }
                case "pointer":
                {
                    // pointer <enter|down|up|click|exit> <words>: one of a click's
                    // events alone, on what a player sees with those words - a
                    // press held between its down and its up, a hover left on.
                    var text = Texts(rest, onScreenOnly: true).FirstOrDefault();
                    if (text == null) { Say("error pointer: no text '" + rest + "' on screen"); return true; }
                    var owner = text.GetComponentsInParent<MonoBehaviour>()
                        .FirstOrDefault(b => b is IPointerClickHandler || b is IPointerDownHandler || b is IPointerEnterHandler);
                    var target = owner != null ? owner.gameObject : text.gameObject;
                    var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                    bool heard;
                    switch (a)
                    {
                        case "enter": heard = ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler); break;
                        case "down": heard = ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler); break;
                        case "up": heard = ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler); break;
                        case "click": heard = ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler); break;
                        case "exit": heard = ExecuteEvents.Execute(target, data, ExecuteEvents.pointerExitHandler); break;
                        default: Say("error pointer: no event '" + a + "'"); return true;
                    }
                    Say("ok pointer " + a + " (" + FullPath(target.transform) + (heard ? ")" : ": nothing listens)"));
                    return true;
                }
                case "press-text":
                {
                    // A click on what a player sees with those words on it.
                    var text = Texts(all, onScreenOnly: true).FirstOrDefault();
                    if (text == null) { Say("error press-text: no text '" + all + "' on screen"); return true; }
                    var clickable = text.GetComponentsInParent<MonoBehaviour>().FirstOrDefault(b => b is IPointerClickHandler);
                    Press(clickable != null ? clickable.gameObject : text.gameObject, "press-text " + all);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>The pointer's enter, down, up and click on an object, to
        /// whatever on it listens - what a player's click does.</summary>
        private void Press(GameObject target, string said)
        {
            if (!target.activeInHierarchy) { Say("error " + said + ": not on screen"); return; }
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var rect = target.transform as RectTransform;
            if (rect != null) data.position = RectTransformUtility.WorldToScreenPoint(null, rect.position);
            var heard = new List<string>();
            if (ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler)) heard.Add("enter");
            if (ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler)) heard.Add("down");
            if (ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler)) heard.Add("up");
            if (ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler)) heard.Add("click");
            Say(heard.Count == 0 ? "error " + said + ": nothing on " + FullPath(target.transform) + " listens for a click"
                                 : "ok " + said + " (" + FullPath(target.transform) + ": " + string.Join(", ", heard.ToArray()) + ")");
        }

        /// <summary>Text objects saying exactly these words, on screen first.</summary>
        private static List<TMP_Text> Texts(string words, bool onScreenOnly)
            => Resources.FindObjectsOfTypeAll<TMP_Text>()
                .Where(t => t != null && t.gameObject.scene.IsValid() && (!onScreenOnly || t.gameObject.activeInHierarchy)
                            && string.Equals((t.text ?? "").Trim(), words, StringComparison.Ordinal))
                .OrderByDescending(t => t.gameObject.activeInHierarchy).ToList();

        private System.Collections.IEnumerator FindAsDrawn(string path)
        {
            yield return new WaitForEndOfFrame();
            var found = FindPath(path);
            Say(found == null ? "error find: no " + path : "ok find " + path + ": " + Describe(found));
            _waitingFor = null;
        }

        // ── Finding things ───────────────────────────────────────────────

        /// <summary>Every root object in a scene, inactive ones too - the scene
        /// kept across loads included, where a mod's window may live (a loaded
        /// scene's own list leaves that one out). Assets are in no scene.</summary>
        private static IEnumerable<Transform> SceneRoots()
            => Resources.FindObjectsOfTypeAll<Transform>().Where(t => t != null && t.parent == null && t.gameObject.scene.IsValid());

        /// <summary>A path from a root object down, a <c>*</c> at either end of
        /// a segment matching any start or end.</summary>
        private static GameObject FindPath(string path)
        {
            var segments = path.Trim().Trim('/').Split('/');
            if (segments.Length == 0 || segments[0].Length == 0) return null;
            var at = SceneRoots().Where(r => Matches(r.name, segments[0])).ToList();
            for (int i = 1; i < segments.Length && at.Count > 0; i++)
            {
                var next = new List<Transform>();
                foreach (var t in at)
                    foreach (Transform child in t)
                        if (Matches(child.name, segments[i])) next.Add(child);
                at = next;
            }
            return at.Count > 0 ? at[0].gameObject : null;
        }

        private static bool Matches(string name, string pattern)
        {
            bool starts = pattern.StartsWith("*"), ends = pattern.EndsWith("*") && pattern.Length > 1;
            string core = pattern.Trim('*');
            if (starts && ends) return name.IndexOf(core, StringComparison.Ordinal) >= 0;
            if (starts) return name.EndsWith(core, StringComparison.Ordinal);
            if (ends) return name.StartsWith(core, StringComparison.Ordinal);
            return name == pattern;
        }

        private static string FullPath(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>What a check needs to know of an object, on one line:
        /// <c>key value</c> pairs separated by <c>; </c>.</summary>
        private static string Describe(GameObject go)
        {
            var t = go.transform;
            var parts = new List<string>
            {
                "name " + go.name,
                "active " + go.activeSelf + "/" + go.activeInHierarchy,
                "local " + V(t.localPosition),
                "world " + V(t.position),
                "home " + V(t.position - LevelDrift(t)),
                "rotz " + N(t.localEulerAngles.z, "0.##"),
                "worldrotz " + N(t.eulerAngles.z, "0.##"),
                "scale " + N(t.localScale.x, "0.###") + "," + N(t.localScale.y, "0.###"),
            };
            if (t is RectTransform rt)
            {
                // A UI object's own size, and where it is drawn: left, bottom,
                // right, top as fractions of the screen, whatever its size.
                parts.Add("size " + N(rt.rect.width, "0.#") + "," + N(rt.rect.height, "0.#"));
                var canvas = go.GetComponentInParent<Canvas>();
                var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                var lo = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                var hi = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                parts.Add("screen " + N(lo.x / Screen.width, "0.###") + "," + N(lo.y / Screen.height, "0.###")
                          + "," + N(hi.x / Screen.width, "0.###") + "," + N(hi.y / Screen.height, "0.###"));
            }
            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null) parts.Add("particles " + Hex(ps.main.startColor.color));
            var group = go.GetComponent<CanvasGroup>();
            if (group != null) parts.Add("group " + N(group.alpha, "0.###"));
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                parts.Add("sprite " + (sr.sprite != null ? sr.sprite.name + " " + sr.sprite.rect.width + "x" + sr.sprite.rect.height : "(none)")
                          + " colour " + Hex(sr.color) + " alpha " + N(sr.color.a, "0.##") + " order " + sr.sortingOrder
                          + " enabled " + sr.enabled);
                parts.Add("texel " + Texel(sr.sprite));
            }
            var image = go.GetComponent<UnityEngine.UI.Image>();
            if (image != null)
            {
                parts.Add("image " + (image.sprite != null ? image.sprite.name : "(none)") + " colour " + Hex(image.color)
                          + " alpha " + N(image.color.a, "0.##") + " enabled " + image.enabled);
                parts.Add("texel " + Texel(image.sprite));
                if (image.sprite != null && image.sprite.texture != null)
                    parts.Add("wrap " + image.sprite.texture.wrapMode + " " + image.type);
            }
            var tmp = go.GetComponent<TMP_Text>();
            if (tmp != null) parts.Add("text '" + tmp.text.Replace("\n", "\\n") + "' colour " + Hex(tmp.color));
            var text = go.GetComponent<UnityEngine.UI.Text>();
            if (text != null) parts.Add("text '" + text.text.Replace("\n", "\\n") + "'");
            var source = go.GetComponent<AudioSource>();
            if (source != null)
                parts.Add("audio " + (source.clip != null ? source.clip.name : "(none)") + " playing " + source.isPlaying
                          + " volume " + N(source.volume, "0.###") + " pitch " + N(source.pitch, "0.###"));
            var names = new List<string>();
            foreach (Transform child in t) names.Add(child.name + (child.gameObject.activeSelf ? "" : "(off)"));
            parts.Add("children " + t.childCount + (names.Count > 0 ? " [" + string.Join(", ", names.Take(24).ToArray()) + "]" : ""));
            parts.Add("components [" + string.Join(", ", go.GetComponents<Component>().Where(c => c != null)
                                                   .Select(c => c.GetType().Name).ToArray()) + "]");
            parts.Add("path " + FullPath(t));
            return string.Join("; ", parts.ToArray());
        }

        private static string V(Vector3 v) => N(v.x, "0.###") + "," + N(v.y, "0.###") + "," + N(v.z, "0.###");

        /// <summary>
        /// How far the map an object is on has drifted from its own place: a
        /// level follows the mouse (the game's ParallaxMouseEffect, which keeps
        /// where it started in <c>startPosition</c>), and everything on it rides
        /// along. Taken away, what is left is where the pack put the object -
        /// whatever the mouse is doing on the game's desktop. Zero off a map, or
        /// on one whose effect has not started.
        /// </summary>
        private static Vector3 LevelDrift(Transform t)
        {
            Transform level = t;
            while (level != null && (level.parent == null || level.parent.name != "5_Levels")) level = level.parent;
            if (level == null) return Vector3.zero;
            var effect = level.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "ParallaxMouseEffect");
            var start = effect == null ? null : effect.GetType().GetField("startPosition",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (start == null) return Vector3.zero;
            var home = (Vector3)start.GetValue(effect);
            if (home == Vector3.zero) return Vector3.zero;   // not started: it has not moved either
            return level.position - level.parent.TransformPoint(home);
        }

        /// <summary>A field's value on one line: numbers and vectors as
        /// numbers, objects by name, lists by count.</summary>
        private static string Show(object v)
        {
            if (v == null) return "null";
            if (v is float f) return N(f, "0.####");
            if (v is double d) return d.ToString("0.####", CultureInfo.InvariantCulture);
            if (v is Vector3 v3) return V(v3);
            if (v is Vector2 v2) return N(v2.x, "0.###") + "," + N(v2.y, "0.###");
            if (v is Color c) return Hex(c) + " a" + N(c.a, "0.##");
            if (v is UnityEngine.Object o) return o == null ? "null" : o.name + " (" + o.GetType().Name + ")";
            if (v is string s) return "'" + s + "'";
            if (v is System.Collections.ICollection list) return v.GetType().Name + "[" + list.Count + "]";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>The colour at the middle of a sprite's own picture - which
        /// picture it is, whatever it is called (a sprite made from a pack's
        /// file has no name). "-" when the texture cannot be read.</summary>
        private static string Texel(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || !sprite.texture.isReadable) return "-";
            var r = sprite.textureRect;
            var c = (Color32)sprite.texture.GetPixel((int)(r.x + r.width / 2), (int)(r.y + r.height / 2));
            return "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2") + c.a.ToString("X2");
        }
    }
}

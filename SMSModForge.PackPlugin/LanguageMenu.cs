using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using SMSModForge.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Where a player chooses the language, on the game's main menu - and only
    /// there, because packs are read when a game starts or loads: a choice made
    /// on the menu is in place before any pack is read, so nothing is ever read
    /// in one language and played in another.
    /// A flag for each language above the Exit button, bottom-right, stacked
    /// upward, each with the language's own name beside it - a flag alone does
    /// not say which language it means. (There was a Language row in the
    /// Settings panel as well, and one for the game's lines under it; the
    /// author had them taken out, 2026-09-27. The plugin's settings file still
    /// has both choices.)
    /// A choice switches ModForge's own words at once and warns about packs
    /// less than half translated into the language (<see cref="LanguageChoice"/>).
    /// <para/>
    /// Where the Exit button is was read off a running
    /// game (ROADMAP, step 2): nothing here guesses at the menu's layout, and
    /// anything not found is logged and left alone rather than half-built.
    /// </summary>
    internal static class LanguageMenu
    {
        private const string Tag = "[SMSModForge.PackPlugin] Language menu: ";

        private const string FlagsName = "SMSModForge_LanguageFlags";
        private const string WarningName = "SMSModForge_LanguageWarning";

        /// <summary>The Exit button, under the main menu.</summary>
        private const string ExitPath = "Bar/Button (Legacy) (3)";

        /// <summary>A flag is as wide as the Exit button under it (55), at a flag's usual 3:2.</summary>
        private const float FlagWidth = 55f;
        private const float FlagHeight = 37f;
        private const float NameSize = 20f;

        /// <summary>Between the Exit button and the lowest flag.</summary>
        private const float FlagsGap = 12f;

        /// <summary>What marks the language in use: the warning window's orange,
        /// behind the whole row, with the flag outlined in white and the name
        /// in bold white - seen at a glance, not just on a second look.</summary>
        private static readonly Color InUseBack = new Color(0.85f, 0.55f, 0.15f, 0.6f);
        private static readonly Color InUseHover = new Color(0.9f, 0.6f, 0.2f, 0.85f);
        private static readonly Color NameColour = new Color(0.82f, 0.82f, 0.82f, 1f);

        /// <summary>Every other row: dark, lighter under the mouse.</summary>
        private static readonly Color RowBack = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color RowHover = new Color(0.22f, 0.22f, 0.22f, 0.8f);
        private static readonly Color RowPressed = new Color(0.35f, 0.35f, 0.35f, 0.9f);

        /// <summary>The line over the flags saying what they choose: a little
        /// smaller than the languages' names, and on nothing, so it names the
        /// column without weighing on the menu.</summary>
        private const float HeadingSize = 18f;
        private static readonly Color HeadingColour = new Color(0.85f, 0.85f, 0.85f, 1f);

        public static ManualLogSource Log;

        /// <summary>Every language an installed pack is written in or
        /// translated into. Set by the menu banner, which reads the packs.</summary>
        public static List<string> PackLanguages = new List<string>();

        /// <summary>How much of each installed pack is in a language.</summary>
        public static Func<string, List<LanguageChoice.Coverage>> CoverageOf;

        /// <summary>After a language is chosen: the pack list redraws in it.</summary>
        public static Action Changed;

        private sealed class FlagRow
        {
            public string Code;
            public TextMeshProUGUI Name;
            public Outline Mark;
            public Button Button;
        }

        private static TextMeshProUGUI _heading;

        private static TMP_FontAsset _font;
        private static List<string> _offered;
        private static RectTransform _flags;
        private static readonly List<FlagRow> _flagRows = new List<FlagRow>();
        private static bool _flagsDone;

        private static GameObject _warning;

        /// <summary>Textures made from the flags built into the plugin, kept
        /// for the session: the menu is built again every time it is shown.</summary>
        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A new scene: whatever was built went with the old one.</summary>
        public static void Reset()
        {
            _offered = null;
            _flags = null;
            _flagRows.Clear();
            _flagsDone = false;
            _heading = null;
            _warning = null;
        }

        /// <summary>
        /// Build the flags if they are not built yet. Called every frame on the
        /// main menu once the pack list is up; does nothing once they are.
        /// </summary>
        public static void Update(Transform menuRoot, TMP_FontAsset font)
        {
            if (menuRoot == null || _flagsDone) return;
            try
            {
                if (_offered == null)
                {
                    _font = font;
                    _offered = OfferedNow();
                    // Every name is written in its own letters - 中文, 日本語,
                    // 한국어, Русский - whatever the player reads the rest in.
                    PluginFonts.AddForText(string.Join(" ", _offered.Select(PluginLanguage.NameOf).ToArray()),
                                           "the language menu", Log);
                    Log?.LogInfo(Tag + "offering " + string.Join(", ", _offered.ToArray()) + ".");
                }
                if (BuildFlags(menuRoot))
                {
                    _flagsDone = true;
                    Refresh();
                }
            }
            catch (Exception e)
            {
                Log?.LogError(Tag + "could not be built: " + e);
                _flagsDone = true;
            }
        }

        /// <summary>
        /// The languages to offer: ModForge's own, the switched-on packs'
        /// (<see cref="PackLanguages"/>), XUnity.AutoTranslator's, and the one
        /// in use - which stays on offer when the only pack in it has been
        /// switched off, so the list never shows a language other than the one
        /// being read.
        /// </summary>
        private static List<string> OfferedNow()
        {
            var packs = new List<string>(PackLanguages ?? new List<string>());
            if (!string.Equals(PluginLanguage.Setting, PluginLanguage.Auto, StringComparison.OrdinalIgnoreCase))
                packs.Add(PluginLanguage.Setting);
            // And the one XUnity.AutoTranslator translates the game into.
            if (XUnityLink.Loaded && !string.IsNullOrEmpty(XUnityLink.Language)) packs.Add(XUnityLink.Language);
            return LanguageChoice.Offered(PluginLanguage.OwnLanguages(), packs, PluginLanguage.NameOf);
        }

        /// <summary>
        /// A pack was switched on or off on the menu, and <see cref="PackLanguages"/>
        /// read again. When that changes the languages on offer, the flags are
        /// built again on the next frame.
        /// </summary>
        public static void PacksChanged()
        {
            if (_offered == null) return;   // not built yet: it reads them fresh
            try
            {
                var now = OfferedNow();
                if (now.SequenceEqual(_offered, StringComparer.OrdinalIgnoreCase)) return;
                _offered = now;
                PluginFonts.AddForText(string.Join(" ", _offered.Select(PluginLanguage.NameOf).ToArray()),
                                       "the language menu", Log);
                Log?.LogInfo(Tag + "now offering " + string.Join(", ", _offered.ToArray()) + ".");

                if (_flags != null) UnityEngine.Object.Destroy(_flags.gameObject);
                _flags = null;
                _flagRows.Clear();
                _heading = null;
                _flagsDone = false;
                Refresh();
            }
            catch (Exception e)
            {
                Log?.LogWarning(Tag + "the languages on offer could not be brought up to date: " + e.Message);
            }
        }

        // ── Flags above Exit ─────────────────────────────────────────────

        /// <returns>False while the Exit button is not there yet.</returns>
        private static bool BuildFlags(Transform menuRoot)
        {
            var exit = menuRoot.Find(ExitPath) as RectTransform;
            if (exit == null) return false;

            var codes = _offered.Where(c => LanguageChoice.FlagOf(c) != null && Flag(c) != null).ToList();
            if (codes.Count < 2)
            {
                Log?.LogInfo(Tag + "no flags: fewer than two languages have one.");
                return true;
            }

            var host = new GameObject(FlagsName, typeof(RectTransform), typeof(CanvasGroup));
            _flags = (RectTransform)host.transform;
            _flags.SetParent(menuRoot, false);
            // Drawn just after the bar Exit is on, so a panel that covers the
            // Exit button covers the flags too.
            if (exit.parent != null && exit.parent.parent == menuRoot)
                _flags.SetSiblingIndex(exit.parent.GetSiblingIndex() + 1);
            _flags.anchorMin = _flags.anchorMax = new Vector2(0.5f, 0.5f);
            // Its bottom-right corner sits on the Exit button's top-right one,
            // so the flags are centred over Exit and the names reach left.
            _flags.pivot = new Vector2(1f, 0f);

            var column = host.AddComponent<VerticalLayoutGroup>();
            column.spacing = 6;
            column.childAlignment = TextAnchor.LowerRight;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            var fit = host.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var follow = host.AddComponent<LanguageFlagsFollow>();
            follow.Target = exit;
            follow.Gap = FlagsGap;

            // The heading on top, then the flags stacked from the bottom:
            // English sits on Exit and the column grows upward, so the first
            // language is added last.
            AddHeading(_flags);
            for (int i = codes.Count - 1; i >= 0; i--) AddFlag(_flags, codes[i]);

            Log?.LogInfo(Tag + codes.Count + " flags above Exit (" + string.Join(", ", codes.ToArray()) + ").");
            return true;
        }

        /// <summary>
        /// "Mod language", over the flags: they choose the language of the mods
        /// - ModForge's words and the packs - and not of the game, which a
        /// flag on the main menu would otherwise be taken to mean.
        /// </summary>
        private static void AddHeading(RectTransform column)
        {
            var rect = Child(column, "Heading");
            var heading = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) heading.font = _font;
            heading.fontSize = HeadingSize;
            heading.color = HeadingColour;
            heading.alignment = TextAlignmentOptions.BottomRight;
#pragma warning disable 0618
            heading.enableWordWrapping = false;
#pragma warning restore 0618
            heading.raycastTarget = false;
            heading.text = GameTexts.T("game.language.flagsHeading");
            _heading = heading;
        }

        private static void AddFlag(RectTransform column, string code)
        {
            var row = Child(column, "Flag " + code);
            var back = row.gameObject.AddComponent<Image>();
            back.color = Color.white;   // the button's colours below are what shows

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 4, 3, 3);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var nameRect = Child(row, "Name");
            var name = nameRect.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) name.font = _font;
            name.text = PluginLanguage.NameOf(code);
            name.fontSize = NameSize;
            name.color = NameColour;
            name.alignment = TextAlignmentOptions.MidlineRight;
#pragma warning disable 0618
            name.enableWordWrapping = false;
#pragma warning restore 0618
            name.raycastTarget = false;

            var flagRect = Child(row, "Flag");
            var flag = flagRect.gameObject.AddComponent<Image>();
            flag.sprite = Flag(code);
            flag.preserveAspect = true;
            flag.raycastTarget = false;
            var size = flagRect.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = FlagWidth;
            size.minHeight = size.preferredHeight = FlagHeight;
            var mark = flagRect.gameObject.AddComponent<Outline>();
            mark.effectColor = Color.white;
            mark.effectDistance = new Vector2(2, -2);
            mark.enabled = false;

            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            var colours = button.colors;
            colours.normalColor = RowBack;
            colours.highlightedColor = RowHover;
            colours.pressedColor = RowPressed;
            colours.selectedColor = colours.normalColor;
            colours.colorMultiplier = 1f;
            colours.fadeDuration = 0.1f;
            button.colors = colours;
            // Not reachable by the keys the game's menu moves between its own
            // buttons with: a flag is picked with the mouse.
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            string chosen = code;
            button.onClick.AddListener(() => Choose(chosen));

            _flagRows.Add(new FlagRow { Code = code, Name = name, Mark = mark, Button = button });
        }

        /// <summary>A flag built into the plugin, or null when there is none
        /// for <paramref name="code"/>.</summary>
        private static Sprite Flag(string code)
        {
            string flagged = LanguageChoice.FlagOf(code);
            if (flagged == null) return null;
            Sprite sprite;
            if (_sprites.TryGetValue(flagged, out sprite) && sprite != null) return sprite;

            try
            {
                using (var stream = typeof(LanguageMenu).Assembly.GetManifestResourceStream("SMSModForge.Flags." + flagged + ".png"))
                {
                    if (stream == null)
                    {
                        Log?.LogWarning(Tag + "the flag for " + flagged + " is not built into the plugin.");
                        return null;
                    }
                    var bytes = new MemoryStream();
                    stream.CopyTo(bytes);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!texture.LoadImage(bytes.ToArray())) return null;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    sprite.name = "SMSModForge flag " + flagged;
                    _sprites[flagged] = sprite;
                    return sprite;
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning(Tag + "the flag for " + flagged + " could not be read: " + e.Message);
                return null;
            }
        }

        // ── Choosing ─────────────────────────────────────────────────────

        private static void Choose(string setting)
        {
            if (string.Equals(setting, PluginLanguage.Setting, StringComparison.OrdinalIgnoreCase)) return;
            string before = PluginLanguage.Setting;
            SaveLoadWarning.Click();
            Switch(setting);
            WarnIfNotEnough(before);
        }

        private static void Switch(string setting)
        {
            PluginLanguage.Switch(setting, Log);
            Refresh();
            try { Changed?.Invoke(); }
            catch (Exception e) { Log?.LogWarning(Tag + "the pack list could not be redrawn: " + e.Message); }
        }

        /// <summary>Show what is in use: the flag marked, the row's words and
        /// the list's choice.</summary>
        private static void Refresh()
        {
            string inUse = LanguageMatch.Best(PluginLanguage.Code, _flagRows.Select(r => r.Code).ToList());
            foreach (var row in _flagRows)
            {
                if (row.Name == null) continue;
                bool on = string.Equals(row.Code, inUse, StringComparison.OrdinalIgnoreCase);
                row.Mark.enabled = on;
                row.Name.color = on ? Color.white : NameColour;
                row.Name.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
                if (row.Button != null)
                {
                    var colours = row.Button.colors;
                    colours.normalColor = on ? InUseBack : RowBack;
                    colours.highlightedColor = on ? InUseHover : RowHover;
                    colours.selectedColor = colours.normalColor;
                    row.Button.colors = colours;
                }
            }
            if (_heading != null) _heading.text = GameTexts.T("game.language.flagsHeading");
        }

        /// <summary>
        /// Tell the player which packs are less than half in the language just
        /// chosen - listed the way the warning before a save lists packs - and
        /// let them keep it or go back.
        /// </summary>
        private static void WarnIfNotEnough(string before)
        {
            if (CoverageOf == null) return;
            List<LanguageChoice.Coverage> packs;
            try { packs = CoverageOf(PluginLanguage.Code) ?? new List<LanguageChoice.Coverage>(); }
            catch (Exception e)
            {
                Log?.LogWarning(Tag + "could not count how much of each pack is in " + PluginLanguage.Code + ": " + e.Message);
                return;
            }
            foreach (var p in packs)
                Log?.LogInfo(Tag + p.Pack + " in " + PluginLanguage.Code + ": "
                             + (p.Own ? "written in it" : p.Translated + " of " + p.Total + " texts (" + p.Percent + "%)"
                                                         + (p.HasTranslation ? "" : ", no translation")) + ".");

            string language = PluginLanguage.NameOf(PluginLanguage.Code);
            var text = LanguageChoice.Warning(language, packs);
            if (text == null) return;

            if (_warning != null) UnityEngine.Object.Destroy(_warning);
            string beforeName = PluginLanguage.NameOf(
                string.Equals(before, PluginLanguage.Auto, StringComparison.OrdinalIgnoreCase) ? PluginLanguage.AutoLanguage : before);
            try
            {
                _warning = SaveLoadWarning.Window(WarningName, text, new[]
                {
                    new SaveLoadWarning.WindowButton(GameTexts.F("game.language.keep", "language", language),
                                                     SaveLoadWarning.GoOnColour, Keep),
                    new SaveLoadWarning.WindowButton(GameTexts.F("game.language.back", "language", beforeName),
                                                     SaveLoadWarning.OtherColour, () => Back(before)),
                }, _font);
            }
            catch (Exception e)
            {
                Log?.LogWarning(Tag + "the warning could not be shown: " + e.Message + " - " + text.Plain());
            }
        }

        private static void Keep()
        {
            SaveLoadWarning.Click();
            var window = _warning;
            _warning = null;
            SaveLoadWarning.Fold(window, null);
        }

        private static void Back(string before)
        {
            SaveLoadWarning.Click();
            var window = _warning;
            _warning = null;
            SaveLoadWarning.Fold(window, null);
            Switch(before);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static RectTransform Child(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

    }

    /// <summary>
    /// Keeps the flags on top of the Exit button, and shows them only while it
    /// shows: wherever the menu moves it, whatever fades it in, and whatever
    /// size the screen is.
    /// </summary>
    internal sealed class LanguageFlagsFollow : MonoBehaviour
    {
        public RectTransform Target;
        public float Gap;
        private CanvasGroup _group;

        private void LateUpdate()
        {
            if (Target == null) return;
            var rect = (RectTransform)transform;
            if (_group == null) _group = GetComponent<CanvasGroup>();

            // The button's top-right corner, from its place in its parent
            // rather than as drawn: a button that grows under the mouse would
            // otherwise push the flags about.
            Vector2 size = Target.rect.size;
            Vector2 pivot = Target.pivot;
            Vector3 corner = Target.localPosition + new Vector3((1f - pivot.x) * size.x, (1f - pivot.y) * size.y, 0f);
            Vector3 world = Target.parent != null ? Target.parent.TransformPoint(corner) : corner;
            rect.position = world + Vector3.up * (Gap * rect.lossyScale.y);

            float alpha = Target.gameObject.activeInHierarchy ? AlphaBetween(Target, rect.parent) : 0f;
            if (_group != null)
            {
                _group.alpha = alpha;
                bool usable = alpha > 0.5f;
                _group.interactable = usable;
                _group.blocksRaycasts = usable;
            }
        }

        /// <summary>How see-through the fades between the button and the
        /// flags' own parent make it; the parent's own fade reaches the flags
        /// by itself.</summary>
        private static float AlphaBetween(Transform from, Transform stop)
        {
            float alpha = 1f;
            for (var t = from; t != null && t != stop; t = t.parent)
            {
                var group = t.GetComponent<CanvasGroup>();
                if (group == null || !group.enabled) continue;
                alpha *= group.alpha;
                if (group.ignoreParentGroups) break;
            }
            return alpha;
        }
    }
}

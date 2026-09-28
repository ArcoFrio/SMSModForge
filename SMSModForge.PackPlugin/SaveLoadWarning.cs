using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using GameCreator.Runtime.Common;
using SMSModForge.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// What a player is told when a save is loaded that the installed packs
    /// might not get along with.
    /// <para/>
    /// Two reasons, said together when both apply:
    /// <list type="bullet">
    ///   <item>A pack that changes the game's own quests or conversations
    ///   (<see cref="SaveLoadChecks.Of"/>) meets an EXISTING save that
    ///   has not been played with those changes. A save part-way through the
    ///   game's story can be left unable to go on. Only on loading a
    ///   save: a new game never reports a save slot (the game's Restart sets
    ///   it to none, and only loading one sets it), which is also what the
    ///   manual-save copy treats as "new game", so a new game is never warned
    ///   - it starts with the changes in place.</item>
    ///   <item>The save's folder holds data for a pack that is not running.
    ///   Said once for each save and each pack, not on every load
    ///   (<see cref="SaveLoadChecks.NotYetTold"/>); a pack that runs with the
    ///   save again is said again the next time it has gone.</item>
    /// </list>
    /// The decision and the wording are <see cref="SaveLoadChecks.Warning"/>,
    /// shared with the editor's tests; this is the window.
    /// </summary>
    internal static class SaveLoadWarning
    {
        private const string Tag = "[SMSModForge.PackPlugin] Save check: ";

        /// <summary>What was found when the save was bound, said once the
        /// game has settled: the quests are only read then, when the journal
        /// holds the save's own progress.</summary>
        private sealed class Pending
        {
            public List<SaveLoadChecks.PackChanges> Unseen;
            public List<SaveLoadChecks.AbsentPack> Absent;

            /// <summary>Every pack the save has data for that is not running,
            /// told before or not: what the told list is kept to.</summary>
            public List<string> AllAbsent;
            public int Slot;
        }

        private static Pending _pending;
        private static List<PackContext> _toAcknowledge = new List<PackContext>();
        private static GameObject _window;
        private static ManualLogSource _log;

        /// <summary>A button has been pressed and the window is on its way
        /// out; a second press does nothing.</summary>
        private static bool _answered;

        /// <summary>The game's title scene, as its build settings list it.</summary>
        private const string TitleScenePath = "Assets/Scenes/GameStart.unity";

        /// <summary>
        /// The global the game sets before it resets to the title. Its own
        /// "back to the title" after an ending, read out of CoreGameScene: set
        /// this to true, then Reset Game to GameStart - Game Creator's
        /// SaveLoadManager.Restart, which clears the game's state and replaces
        /// CoreGameScene with the title.
        /// </summary>
        private const string RestartFlag = "RESTART-GAME-INCOMING25";

        /// <summary>Whether the window is up. Quests wait while it is: the
        /// player has not said yet whether they are going on with this save.</summary>
        public static bool IsOpen => _window != null;

        /// <summary>Whether anything is still to be shown or answered.</summary>
        public static bool IsWaiting => _pending != null || _linesOnly || IsOpen;

        // ── The game's own lines, in the packs' translations ────────────
        //
        // Told once, the first time a loaded save has a pack that translates
        // them into the player's language: inside the warning when there is
        // one, on its own otherwise. See GameLineNotice.

        /// <summary>The packs that show the game's lines translated, when the
        /// player has not been told yet.</summary>
        private static List<string> _linePacks = new List<string>();

        /// <summary>Nothing to warn about, only the notice to give.</summary>
        private static bool _linesOnly;

        public static void Reset()
        {
            _pending = null;
            _toAcknowledge = new List<PackContext>();
            if (_window != null) UnityEngine.Object.Destroy(_window);
            _window = null;
            _answered = false;
            _linePacks = new List<string>();
            _linesOnly = false;
        }

        /// <summary>
        /// Decide, for the save slot just bound, whether there is anything to
        /// say. Called once per load, the moment the plugin learns which save
        /// was loaded.
        /// </summary>
        public static void Check(int slot, IReadOnlyList<PackContext> contexts, ManualLogSource log)
        {
            if (slot < 1) return;

            var running = contexts.Where(c => c?.Vars != null).ToList();

            // The packs the save has data for that are not running, told apart:
            // one switched off is a tick away on the main menu, one not
            // installed is not - and each is named at the version the save
            // recorded, when it has a record (SaveCarry read both at binding).
            var absent = SaveLoadChecks.Absent(SaveCarry.LoadedFiles, running.Select(c => c.PackId).ToList(),
                                               SaveCarry.Installed, PackSwitchSetting.Off, SaveCarry.Loaded);

            // Told once for each save, each pack on its own - not on every
            // load. A pack running with the save again comes off the list, and
            // once the autosave commits that, taking it away tells them again.
            var allAbsent = absent.Select(a => a.Id).ToList();
            var toldBefore = SaveCarry.LiveTold;
            SaveCarry.NoteTold(SaveLoadChecks.Told(toldBefore, allAbsent, null));
            var untold = SaveLoadChecks.NotYetTold(absent, toldBefore);
            if (untold.Count < absent.Count)
                log?.LogInfo(Tag + "slot " + slot + " also has data from "
                             + string.Join(", ", allAbsent.Where(id => !untold.Any(a => a.Id == id)).ToArray())
                             + ", not running now - the player was told when it was first found, so not again.");
            absent = untold;
            var unseen = running
                .Select(c => new SaveLoadChecks.PackChanges(
                    c.PackId, SaveLoadChecks.NotYetSeen(c.GameChanges, c.Vars.GameChangesSeen), c.ChangeWords)
                {
                    SaveBeforeMarks = c.Vars.WrittenBeforeChangeMarks,
                })
                .Where(p => p.Kinds.Count > 0)
                .ToList();
            var risky = running.Where(c => unseen.Any(p => p.PackId == c.PackId)).ToList();

            _linePacks = new List<string>();
            if (!PluginLanguage.GameLinesNoticeShown)
                foreach (var c in running)
                {
                    try { if (GameLineTranslations.Usable(c) > 0) _linePacks.Add(c.PackId); }
                    catch (Exception e)
                    {
                        log?.LogWarning(Tag + c.PackId + ": could not count its translation of the game's lines: " + e.Message);
                    }
                }

            if (SaveLoadChecks.WarningFor(unseen, absent) == null)
            {
                _linesOnly = _linePacks.Count > 0;
                return;
            }

            _pending = new Pending { Unseen = unseen, Absent = absent, AllAbsent = allAbsent, Slot = slot };
            _toAcknowledge = risky;
        }

        /// <summary>
        /// The game's quests whose tasks the packs change in ways this save has
        /// not been played with, and where the save stands with each.
        /// </summary>
        private static List<SaveLoadChecks.ChangedQuest> ChangedQuests(IEnumerable<SaveLoadChecks.PackChanges> unseen,
                                                                       ManualLogSource log)
        {
            var found = new List<SaveLoadChecks.ChangedQuest>();
            foreach (var pack in unseen)
            {
                // Whatever the kind: a quest whose start this pack has taken
                // over is as worth naming as one whose tasks it changes.
                var ctx = _toAcknowledge.FirstOrDefault(c => c.PackId == pack.PackId);
                if (ctx == null) continue;
                foreach (var change in ctx.ChangedQuests)
                {
                    try
                    {
                        var quest = QuestRegistry.FindVanilla(change.Key);
                        if (quest == null) continue;
                        // The ids the tasks were added under - the same as
                        // VanillaQuestEdits gives them.
                        var added = change.Value.Select(k => QuestIds.AddedTaskId(pack.PackId, quest.name, k)).ToList();
                        var progress = VanillaQuestEdits.ProgressOf(quest, added);
                        if (progress != null) found.Add(new SaveLoadChecks.ChangedQuest(change.Key, progress.Value));
                    }
                    catch (Exception e)
                    {
                        log?.LogWarning(Tag + "could not read where this save is with '" + change.Key + "': " + e.Message);
                    }
                }
            }
            return found;
        }

        /// <summary>Put the window up, if there is one to show. Called once the
        /// loaded game has settled, so it lands over the game rather than over
        /// the loading screen.</summary>
        public static void ShowIfPending(ManualLogSource log)
        {
            if (_window != null) return;
            if (_pending == null)
            {
                if (_linesOnly)
                {
                    _linesOnly = false;
                    TellAboutGameLines(log);
                }
                return;
            }
            var pending = _pending;
            _pending = null;
            _log = log;
            _answered = false;

            var text = SaveLoadChecks.WarningFor(pending.Unseen, pending.Absent, ChangedQuests(pending.Unseen, log));
            if (text == null) return;
            log?.LogWarning(Tag + "slot " + pending.Slot + ": " + text.Plain());

            // Without an event system nothing on the window could be clicked,
            // and a window nobody can close is worse than none: the warning is
            // already in the log, so the game goes on as if Continue was chosen.
            // FindObjectOfType: its replacement is newer than some Unity builds
            // this plugin has been run on.
#pragma warning disable 0618
            bool clickable = EventSystem.current != null || UnityEngine.Object.FindObjectOfType<EventSystem>() != null;
#pragma warning restore 0618
            if (!clickable)
            {
                log?.LogWarning(Tag + "the scene has no event system, so the warning could not be shown; "
                                + "going on with this save.");
                Acknowledge();
                return;
            }

            // The notice about the game's lines rides along, rather than
            // being a second window after this one.
            if (_linePacks.Count > 0)
                text.After.Add(GameLineNotice.Body(_linePacks, PluginLanguage.NameOf(PluginLanguage.Code)));

            try { _window = Build(text); }
            catch (Exception e)
            {
                log?.LogError(Tag + "the warning could not be shown: " + e);
                Acknowledge();
            }
            if (_window != null && _linePacks.Count > 0) PluginLanguage.NoteGameLinesNoticeShown(log);

            // On screen: the packs it names have been told about. Noted for the
            // autosave to commit, like any other change - quit before it and
            // the save still has not told them, so it says it again. Not
            // before the notice appeared, which told nobody.
            if (_window != null && pending.Absent.Count > 0)
                SaveCarry.NoteTold(SaveLoadChecks.Told(SaveCarry.LiveTold, pending.AllAbsent,
                                                       pending.Absent.Select(a => a.Id)));
        }

        /// <summary>
        /// The notice about the game's lines on its own, for a save with
        /// nothing else to say about it. Not given where nothing could be
        /// clicked - it waits for a load where it can be.
        /// </summary>
        private static void TellAboutGameLines(ManualLogSource log)
        {
            _log = log;
            _answered = false;
            var text = GameLineNotice.Notice(_linePacks, PluginLanguage.NameOf(PluginLanguage.Code));
            log?.LogInfo(Tag + text.Plain());

#pragma warning disable 0618
            bool clickable = EventSystem.current != null || UnityEngine.Object.FindObjectOfType<EventSystem>() != null;
#pragma warning restore 0618
            if (!clickable) return;

            try
            {
                _window = Window("SMSModForge_GameLines", text, new[]
                {
                    new WindowButton(SaveWarningText.ContinueLabel, GoOnColour, CloseNotice),
                }, GameFont());
                PluginLanguage.NoteGameLinesNoticeShown(log);
            }
            catch (Exception e)
            {
                log?.LogError(Tag + "the notice about the game's lines could not be shown: " + e);
            }
        }

        private static void CloseNotice()
        {
            if (_answered || _window == null) return;
            _answered = true;
            Click();
            var window = _window;
            Fold(window, () => { if (ReferenceEquals(_window, window)) _window = null; _answered = false; });
        }

        private static void Acknowledge()
        {
            foreach (var c in _toAcknowledge) c.Vars?.AcknowledgeGameChanges(c.GameChanges);
            _toAcknowledge = new List<PackContext>();
        }

        /// <summary>The game's own button click, the one its menus make.</summary>
        internal static void Click()
        {
            var clip = UiAssets.Sound(UiFactory.DefaultButtonSound);
            if (clip != null) GameAudio.PlayUi(clip, UiButtonClick.DefaultClickVolume);
        }

        /// <summary>Go on with this save: the window folds away the way the
        /// game's own panels do, then goes.</summary>
        private static void Continue()
        {
            if (_answered || _window == null) return;
            _answered = true;
            Click();
            Acknowledge();

            var window = _window;
            var closer = window.GetComponent<SaveWarningCloser>();
            if (closer != null) closer.Close(() => Gone(window));
            else Gone(window);
        }

        private static void Gone(GameObject window)
        {
            if (window != null) UnityEngine.Object.Destroy(window);
            if (ReferenceEquals(_window, window)) _window = null;
            _answered = false;
        }

        /// <summary>
        /// Leave this save for the title screen, the way the game itself goes
        /// back to it. Nothing is acknowledged: the save was not played on.
        /// The window goes with the scene.
        /// </summary>
        private static void ReturnToMainMenu()
        {
            if (_answered) return;
            _answered = true;
            Click();

            int title = SceneUtility.GetBuildIndexByScenePath(TitleScenePath);
            var manager = Singleton<SaveLoadManager>.Instance;
            if (title < 0 || manager == null)
            {
                _log?.LogError(Tag + "the title scene (" + TitleScenePath + ") or the game's save manager is not "
                               + "there, so the game cannot go back to the main menu from here. Continuing with "
                               + "this save instead.");
                _answered = false;
                Continue();
                return;
            }

            GameVariableBridge.SetBool(RestartFlag, true);
            _log?.LogInfo(Tag + "returning to the main menu.");
            _ = manager.Restart(title);
        }

        // ── The window ───────────────────────────────────────────────────

        /// <summary>The warning's own sentences: three quarters of the size
        /// they were, so the window has room for the list.</summary>
        private const float BodySize = 30f * 0.75f;

        /// <summary>The list of what changed - half the size the warning used
        /// to be. It is the part that grows with every pack and every quest.</summary>
        private const float DetailSize = 30f * 0.5f;

        /// <summary>The tallest the list's box grows (in the window's 1080-high
        /// reference) before it scrolls instead.</summary>
        private const float DetailsMaxHeight = 300f;

        private const float ScrollbarWidth = 14f;

        /// <summary>Space between the list's box and the list inside it.</summary>
        private const float DetailsInset = 12f;

        /// <summary>The button that goes on, and the one that does not.</summary>
        internal static readonly Color GoOnColour = new Color(0.22f, 0.45f, 0.78f);
        internal static readonly Color OtherColour = new Color(0.32f, 0.32f, 0.38f);

        /// <summary>One of a window's buttons, left to right.</summary>
        internal struct WindowButton
        {
            public readonly string Label;
            public readonly Color Colour;
            public readonly Action OnClick;

            public WindowButton(string label, Color colour, Action onClick)
            {
                Label = label;
                Colour = colour;
                OnClick = onClick;
            }
        }

        private static GameObject Build(SaveWarningText text)
            => Window("SMSModForge_SaveWarning", text, new[]
            {
                new WindowButton(SaveWarningText.ContinueLabel, GoOnColour, Continue),
                new WindowButton(SaveWarningText.ReturnLabel, OtherColour, ReturnToMainMenu),
            }, GameFont());

        /// <summary>
        /// A warning window over everything: its title, what it says, a list
        /// in a box of its own, and buttons. This one's look, shared by every
        /// warning ModForge puts in front of a player, so they read as the
        /// same kind of thing. <see cref="Fold"/> takes it away.
        /// </summary>
        internal static GameObject Window(string name, SaveWarningText text, IList<WindowButton> buttonsLeftToRight,
                                          TMP_FontAsset font)
        {
            var root = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
                                      typeof(CanvasGroup));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // Everything behind it is dimmed, and takes no clicks.
            var dim = Child(root.transform, "Dim");
            Stretch(dim);
            var dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.7f);
            dimImage.raycastTarget = true;

            var panel = Child(root.transform, "Panel");
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1100, 0);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.09f, 0.09f, 0.13f, 0.98f);
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.85f, 0.55f, 0.15f, 1f);
            outline.effectDistance = new Vector2(3, -3);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 40, 40);
            layout.spacing = 22;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddLabel(panel, "Title", text.Title, 46, FontStyles.Bold, new Color(1f, 0.78f, 0.35f), font,
                     TextAlignmentOptions.Center);
            foreach (var paragraph in text.Paragraphs)
                AddLabel(panel, "Paragraph", paragraph, BodySize, FontStyles.Normal, Color.white, font,
                         TextAlignmentOptions.Left);
            // What changed, in a box of its own that scrolls once it is taller
            // than the window can spare.
            var details = text.Details.Count > 0 ? AddDetails(panel, text.Details, font) : null;
            foreach (var paragraph in text.After)
                AddLabel(panel, "After", paragraph, BodySize, FontStyles.Normal, Color.white, font,
                         TextAlignmentOptions.Left);

            var buttons = Child(panel, "Buttons");
            var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 30;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            var rowSize = buttons.gameObject.AddComponent<LayoutElement>();
            rowSize.preferredHeight = 76;
            rowSize.minHeight = 76;

            foreach (var button in buttonsLeftToRight)
                AddButton(buttons, button.Label, button.Colour, font, button.OnClick);

            var closer = root.AddComponent<SaveWarningCloser>();
            closer.Panel = panel;
            closer.Dim = dimImage;

            if (details != null) FitDetails(panel, details);
            return root;
        }

        /// <summary>Take a <see cref="Window"/> away the way the game's own
        /// panels go - folding flat - then destroy it and call
        /// <paramref name="gone"/>.</summary>
        internal static void Fold(GameObject window, Action gone)
        {
            if (window == null) { gone?.Invoke(); return; }
            Action destroy = () =>
            {
                if (window != null) UnityEngine.Object.Destroy(window);
                gone?.Invoke();
            };
            var closer = window.GetComponent<SaveWarningCloser>();
            if (closer != null) closer.Close(destroy);
            else destroy();
        }

        /// <summary>
        /// The list of what changed: a box inside the window, with the list in
        /// it at <see cref="DetailSize"/>, a scrollbar down its right side, and
        /// the mouse wheel scrolling it.
        /// </summary>
        private static ScrollRect AddDetails(RectTransform parent, IList<SaveWarningSection> sections,
                                             TMP_FontAsset font)
        {
            var box = Child(parent, "Details");
            // A background is what the wheel and a drag land on.
            var background = box.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.35f);
            var border = box.gameObject.AddComponent<Outline>();
            border.effectColor = new Color(1f, 1f, 1f, 0.18f);
            border.effectDistance = new Vector2(1, -1);
            box.gameObject.AddComponent<LayoutElement>();

            var viewport = Child(box, "Viewport");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(DetailsInset + 2, DetailsInset);
            viewport.offsetMax = new Vector2(-(DetailsInset + ScrollbarWidth + 8), -DetailsInset);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = Child(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var list = content.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 10;
            list.childControlWidth = true;
            list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // One block per heading. The names are a pack's and the game's, so
            // they are kept out of the text engine's tags.
            foreach (var section in sections)
                AddLabel(content, "Section",
                         "<b>" + section.Heading + "</b>"
                         + string.Concat(section.Items.Select(i => "\n" + SaveWarningText.Bullet
                                                                   + "<noparse>" + i + "</noparse>").ToArray()),
                         DetailSize, FontStyles.Normal, Color.white, font, TextAlignmentOptions.Left);

            var bar = Child(box, "Scrollbar");
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(ScrollbarWidth, -2 * DetailsInset);
            bar.anchoredPosition = new Vector2(-DetailsInset + 4, 0f);
            var track = bar.gameObject.AddComponent<Image>();
            track.color = new Color(1f, 1f, 1f, 0.08f);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slide = Child(bar, "Sliding Area");
            Stretch(slide);
            var handle = Child(slide, "Handle");
            Stretch(handle);
            var grip = handle.gameObject.AddComponent<Image>();
            grip.color = new Color(0.85f, 0.55f, 0.15f, 0.9f);   // the window's own orange
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = grip;

            var scroll = box.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.verticalScrollbar = scrollbar;
            // Shown when there is something to scroll: a bar beside a list that
            // fits would be a handle with nowhere to go.
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        /// <summary>
        /// Make the list's box as tall as the list, up to
        /// <see cref="DetailsMaxHeight"/>. The list's height depends on its
        /// width, which only exists once the window has been laid out - and the
        /// list is not laid out with the window, because the scroll view is
        /// where a layout pass stops.
        /// </summary>
        private static void FitDetails(RectTransform panel, ScrollRect details)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            LayoutRebuilder.ForceRebuildLayoutImmediate(details.content);
            float needed = details.content.rect.height + 2 * DetailsInset;
            var size = details.GetComponent<LayoutElement>();
            size.preferredHeight = size.minHeight = Mathf.Min(needed, DetailsMaxHeight);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            details.verticalNormalizedPosition = 1f;   // the top of the list
        }

        private static RectTransform Child(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void AddLabel(RectTransform parent, string name, string value, float size, FontStyles style,
                                  Color color, TMP_FontAsset font, TextAlignmentOptions alignment)
        {
            var rect = Child(parent, name);
            var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = value;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            // Obsolete in newer TextMesh Pro builds, and still what this one
            // wraps by: the window has always been drawn with it.
#pragma warning disable 0618
            tmp.enableWordWrapping = true;
#pragma warning restore 0618
            tmp.raycastTarget = false;
        }

        private static void AddButton(RectTransform parent, string label, Color color, TMP_FontAsset font, Action onClick)
        {
            var rect = Child(parent, label);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = 340;
            size.preferredHeight = 76;

            var text = Child(rect, "Label");
            Stretch(text);
            var tmp = text.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = label;
            tmp.fontSize = 32;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }

        /// <summary>The font the game's own interface is written in, so the
        /// window reads as part of the game. TextMesh Pro's default if the
        /// game's canvas has no text to borrow from.</summary>
        private static TMP_FontAsset GameFont()
        {
            var canvas = TransformExtensions.FindGlobalIncludingInactive("9_MainCanvas");
            if (canvas != null)
                foreach (var t in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
                    if (t != null && t.font != null) return t.font;
            if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;
            return Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
        }
    }

    /// <summary>
    /// The warning window's way out: the panel folds flat and the dimming
    /// fades, then the window goes. The fold is the game's own - its shop's
    /// close button scales the panel to (1, 0, 1) over 0.3s on QuadInOut
    /// before switching it off - so the window leaves the way the game's panels
    /// do.
    /// </summary>
    internal sealed class SaveWarningCloser : MonoBehaviour
    {
        public RectTransform Panel;
        public Image Dim;

        private const float Duration = 0.3f;
        private static readonly Vector3 Folded = new Vector3(1f, 0f, 1f);

        public void Close(Action done)
        {
            // Nothing more can be pressed on the way out, but what is behind
            // stays covered until the window has gone.
            var group = GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.interactable = false;
                group.blocksRaycasts = true;
            }
            if (!isActiveAndEnabled) { done?.Invoke(); return; }
            StartCoroutine(Fold(done));
        }

        private IEnumerator Fold(Action done)
        {
            Vector3 from = Panel != null ? Panel.localScale : Vector3.one;
            float dimFrom = Dim != null ? Dim.color.a : 0f;
            for (float t = 0f; t < Duration; t += Time.unscaledDeltaTime)
            {
                float k = UiOpenAnimation.Ease(t / Duration, "QuadInOut");
                if (Panel != null) Panel.localScale = Vector3.LerpUnclamped(from, Vector3.Scale(from, Folded), k);
                if (Dim != null)
                {
                    var c = Dim.color;
                    c.a = Mathf.Lerp(dimFrom, 0f, k);
                    Dim.color = c;
                }
                yield return null;
            }
            done?.Invoke();
        }
    }
}

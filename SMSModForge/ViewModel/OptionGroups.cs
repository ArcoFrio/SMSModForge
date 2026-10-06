using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// The headings a dropdown's items go under, and the views that put them there.
/// <para/>
/// Three questions cover every list in the editor that has more than a glance's
/// worth in it. Whose is it - the pack's or the game's - wherever the two share
/// a list, since either can be named and the name alone does not say. Which of
/// the author's folders is it in, for a list that is all the pack's: whoever
/// filed their sounds in folders has already said how they think of them. And
/// what is it for, for the action and condition types.
/// </summary>
public static class OptionGroups
{
    public static string ThisPack => Loc.T("common.group.thisPack");
    public static string GamesOwn => Loc.T("common.group.gamesOwn");

    /// <summary>The heading for whatever the author has not put in a folder.</summary>
    public static string NotInFolder => Loc.T("common.group.notInFolder");

    /// <summary>
    /// A dropdown's items under headings, live over <paramref name="source"/>:
    /// a heading appears the moment the first thing that belongs under it does.
    /// A fresh view each call, so two boxes never share one's current item.
    /// </summary>
    /// <param name="sortHeadings">False for a list whose items already come
    /// in the order its headings should - the action types under their
    /// topics.</param>
    public static ICollectionView Under(System.Collections.IEnumerable source, Func<object?, string> heading,
                                        bool sortHeadings = true)
    {
        var groups = new System.Windows.Data.PropertyGroupDescription(null, new View.Converters.HeadingConverter(heading));
        if (sortHeadings) groups.CustomSort = HeadingOrder.Instance;

        // A view made directly, not through a CollectionViewSource: that one
        // files every view it makes with WPF's binding engine, keyed to its
        // list, to be purged when the list is collected - and many of these
        // lists are made for one reading and dropped, a row's levels or a
        // picker's types. The purge (DataBindEngine.DoCleanup) was seen to
        // throw inside WPF while such views piled up, and nothing here needs
        // the engine to know about them. A ListCollectionView follows its list
        // as it changes all the same.
        ICollectionView made = source is System.Collections.IList list
            ? new System.Windows.Data.ListCollectionView(list)
            : new System.Windows.Data.CollectionViewSource { Source = source }.View;
        made.GroupDescriptions.Add(groups);
        return made;
    }

    /// <summary>
    /// The order the headings stand in: the pack's own over the game's, as the
    /// music pickers have always had them - the ones an author wrote are the
    /// ones they are usually after - and whatever is in no folder under the
    /// folders, which stand in alphabetical order.
    /// </summary>
    private sealed class HeadingOrder : System.Collections.IComparer
    {
        public static readonly HeadingOrder Instance = new();

        private static string Name(object? group) => (group as System.Windows.Data.CollectionViewGroup)?.Name as string ?? "";

        private static int Rank(string name)
            => name == ThisPack ? 0 : name == GamesOwn ? 2 : name == NotInFolder ? 3 : 1;

        public int Compare(object? x, object? y)
        {
            string a = Name(x), b = Name(y);
            int byRank = Rank(a).CompareTo(Rank(b));
            return byRank != 0 ? byRank : StringComparer.CurrentCultureIgnoreCase.Compare(a, b);
        }
    }

    /// <summary>
    /// A grouped view that is told when its items may have changed heading.
    /// <para/>
    /// A view files each item when it arrives and never looks again, so a sound
    /// moved into a folder stayed under its old heading for as long as the view
    /// lived. <see cref="Regroup"/> files them again when, and only when, a
    /// heading has changed: it runs on every node selection, and a view reset
    /// for nothing is churn under every open picker - an editable box's text
    /// goes down with it.
    /// </summary>
    public sealed class Live
    {
        private readonly System.Collections.IEnumerable _source;
        private readonly Func<object?, string> _heading;
        private string _filing = "";

        public Live(System.Collections.IEnumerable source, Func<object?, string> heading)
        {
            _source = source;
            _heading = heading;
            View = Under(source, heading);
            _filing = Filing();
        }

        public ICollectionView View { get; }

        public void Regroup()
        {
            string filing = Filing();
            if (filing == _filing) return;
            _filing = filing;
            View.Refresh();
        }

        private string Filing() => string.Join("\n", _source.Cast<object>().Select(o => o + "\t" + _heading(o)));
    }

    // ── Whose is it ──────────────────────────────────────────────────────

    /// <summary>A level or a place by its token: <c>place:</c> and
    /// <c>self:</c> are the pack's, anything else is one of the game's.</summary>
    public static string OfLevel(object? item)
    {
        string token = item is NavigatorTargetOption o ? o.Token : item as string ?? "";
        return token.StartsWith("place:", StringComparison.OrdinalIgnoreCase)
               || token.StartsWith("self:", StringComparison.OrdinalIgnoreCase)
            ? ThisPack : GamesOwn;
    }

    public static ICollectionView Levels(IEnumerable<NavigatorTargetOption> levels) => Under(levels, OfLevel);

    /// <summary>A bust by its GameObject name: one of the game's when the game
    /// has a bust of that name, the pack's otherwise.</summary>
    public static string OfBust(object? item)
    {
        string name = item is NavigatorTargetOption o ? o.Token : item as string ?? "";
        return VanillaBusts.FindByGoName(name) != null ? GamesOwn : ThisPack;
    }

    /// <summary>The four faces every bust in the game has, and the ones a pack
    /// adds.</summary>
    public static string OfExpression(object? item)
        => item is string s && GameExpressions.Contains(s) ? GamesOwn : ThisPack;

    private static readonly HashSet<string> GameExpressions =
        new(StringComparer.OrdinalIgnoreCase) { "Happy", "Angry", "Sad", "Flirty" };

    /// <summary>Under whose heading, from a set of the names that are the
    /// game's - for a list whose items do not say so themselves.</summary>
    public static ICollectionView ByOrigin(IEnumerable<string> items, ICollection<string> games)
        => Under(items, o => o is string s && games.Contains(s) ? GamesOwn : ThisPack);

    // ── Which folder ─────────────────────────────────────────────────────

    /// <summary>The folder a unit's key is filed in, as a path ("Night /
    /// Bar"), or <see cref="NotInFolder"/>.</summary>
    public static string FolderOf(IEnumerable<UnitFolderDef>? folders, string? key)
    {
        if (string.IsNullOrEmpty(key) || folders == null) return NotInFolder;

        string? Search(IEnumerable<UnitFolderDef> level, string trail)
        {
            foreach (var folder in level)
            {
                string here = trail.Length == 0 ? folder.Name : trail + " / " + folder.Name;
                if (folder.Items.Contains(key!, StringComparer.OrdinalIgnoreCase)) return here;
                string? deeper = Search(folder.Folders ?? new List<UnitFolderDef>(), here);
                if (deeper != null) return deeper;
            }
            return null;
        }

        return Search(folders, "") ?? NotInFolder;
    }

    public static ICollectionView ByFolder<T>(IEnumerable<T> source, Func<IEnumerable<UnitFolderDef>?> folders)
        => Under(source, o => FolderOf(folders(), o is NavigatorTargetOption n ? n.Token : o as string));

    // ── What is it for ───────────────────────────────────────────────────

    /// <summary>The action types under what they do, in an order that reads
    /// from who is on screen out to the rest of the game.</summary>
    private static readonly (string Heading, string[] Types)[] ActionTopics =
    {
        ("typeGroup.characters", new[] { NodeActionTypes.CharacterFocus, NodeActionTypes.LeaveBust }),
        ("typeGroup.objects", new[]
        {
            NodeActionTypes.SetGameObjectActive, NodeActionTypes.SetSprite, NodeActionTypes.FadeSprite,
            NodeActionTypes.MoveGameObject, NodeActionTypes.SpinGameObject, NodeActionTypes.SetComponentProperty,
            NodeActionTypes.DeactivateAllScenes,
        }),
        ("typeGroup.screen", new[]
        {
            NodeActionTypes.TransitionLevels, NodeActionTypes.Transitions, NodeActionTypes.LeaveUiFaded,
            NodeActionTypes.EmitSignal, NodeActionTypes.SetWeather,
        }),
        ("typeGroup.sound", new[] { NodeActionTypes.SwitchMusic, NodeActionTypes.PlaySFX }),
        ("typeGroup.variables", new[]
        {
            NodeActionViewModel.VariableFamilyType, NodeActionTypes.AddToList,
            NodeActionTypes.RemoveFromList, NodeActionTypes.ClearList,
        }),
        ("typeGroup.timing", new[] { NodeActionTypes.Wait, NodeActionTypes.DiceRoll }),
        ("typeGroup.quests", new[] { NodeActionTypes.Quest }),
    };

    private static readonly (string Heading, string[] Types)[] ConditionTopics =
    {
        ("typeGroup.variables", new[]
        {
            NodeConditionViewModel.VariableFamilyType, NodeConditionTypes.VariableExists,
            NodeConditionTypes.VariableStartsWith, NodeConditionTypes.ListContains, NodeConditionTypes.ListCount,
        }),
        ("typeGroup.world", new[]
        {
            NodeConditionTypes.LevelActive, NodeConditionTypes.GameObjectActive, NodeConditionTypes.Weather,
        }),
        ("typeGroup.quests", new[] { NodeConditionTypes.QuestState, NodeConditionTypes.QuestCounter }),
        ("typeGroup.chance", new[]
        {
            NodeConditionTypes.DailyChance, NodeConditionTypes.Random, NodeConditionTypes.Timer,
            NodeConditionTypes.InputKey, NodeConditionTypes.AlwaysTrue,
        }),
    };

    /// <summary>The topic an action type is listed under; one nobody filed
    /// goes last, under "Other", rather than nowhere.</summary>
    public static string ActionTopic(string type) => Topic(ActionTopics, type);

    public static string ConditionTopic(string type) => Topic(ConditionTopics, type);

    private static string Topic((string Heading, string[] Types)[] topics, string type)
    {
        foreach (var (heading, types) in topics)
            if (types.Contains(type, StringComparer.Ordinal)) return Loc.T(heading);
        return Loc.T("common.group.misc");
    }

    private static int Order((string Heading, string[] Types)[] topics, string type)
    {
        for (int i = 0; i < topics.Length; i++)
            if (topics[i].Types.Contains(type, StringComparer.Ordinal)) return i;
        return topics.Length;
    }

    /// <summary>The types a picker offers, under their topics - in the topics'
    /// order, alphabetical inside each.</summary>
    public static ICollectionView ActionTypes(IEnumerable<string> types)
        => Under(types.OrderBy(t => Order(ActionTopics, t)).ThenBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
                 o => ActionTopic(o as string ?? ""), sortHeadings: false);

    public static ICollectionView ConditionTypes(IEnumerable<string> types)
        => Under(types.OrderBy(t => Order(ConditionTopics, t)).ThenBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
                 o => ConditionTopic(o as string ?? ""), sortHeadings: false);
}

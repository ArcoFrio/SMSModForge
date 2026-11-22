using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using SMSModForge.Documentation;
using SMSModForge.Localization;

namespace SMSModForge.ViewModel;

/// <summary>The ModForge tab's pages (1.10.0 rework): one page at a time, chosen in the strip at its top.</summary>
public enum HomePage { Home, Tutorials, Guide, Issues }

/// <summary>A topic of the guide as the reader's sidebar lists it: which part it is in, and where.</summary>
public sealed record GuideEntry(DocPart Part, DocTopic Topic, int Index)
{
    public string PartName => Part.Name;
    public string Title => Topic.Title;
}

/// <summary>
/// The ModForge tab, reworked for 1.10.0 (the author: "make it look much cleaner, and make it so an entry level user
/// is able to get the information they need"). It was the oldest tab: a column of fold-outs for help and a column
/// of issues, side by side. Now it is pages - Home (the pack, what to learn next, how to start), Tutorials, the
/// Guide as a reader with its topics down the side, and Issues - with one search over the tutorials and the guide
/// whose results take the page's place while it has words in it.
/// </summary>
public sealed partial class MainViewModel
{
    private HomePage _homePage = HomePage.Home;

    /// <summary>The page the ModForge tab shows.</summary>
    public HomePage HomePage
    {
        get => _homePage;
        set
        {
            if (!SetField(ref _homePage, value)) return;
            RaiseHomePages();
        }
    }

    // One for each button of the strip: RadioButtons bind both ways to these.
    // None is lit while the search's results are the page (the review: Home stayed lit over them).
    public bool IsHomeHome { get => _homePage == HomePage.Home && !IsDocSearchActive; set { if (value) GoHome(HomePage.Home); } }
    public bool IsHomeTutorials { get => _homePage == HomePage.Tutorials && !IsDocSearchActive; set { if (value) GoHome(HomePage.Tutorials); } }
    public bool IsHomeGuide { get => _homePage == HomePage.Guide && !IsDocSearchActive; set { if (value) GoHome(HomePage.Guide); } }
    public bool IsHomeIssues { get => _homePage == HomePage.Issues && !IsDocSearchActive; set { if (value) GoHome(HomePage.Issues); } }

    // What is drawn: the search's results take the page's place while there is a search.
    public bool ShowHomeHome => IsHomeHome;
    public bool ShowHomeTutorials => IsHomeTutorials;
    public bool ShowHomeGuide => IsHomeGuide;
    public bool ShowHomeIssues => IsHomeIssues;
    public bool ShowSearchResults => IsDocSearchActive;

    /// <summary>A page chosen: a search in the box is put away, since its results would cover the page.</summary>
    private void GoHome(HomePage page)
    {
        if (IsDocSearchActive) DocQuery = "";
        HomePage = page;
        RaiseHomePages();
    }

    private void RaiseHomePages()
    {
        foreach (var name in new[]
                 {
                     nameof(IsHomeHome), nameof(IsHomeTutorials), nameof(IsHomeGuide), nameof(IsHomeIssues),
                     nameof(ShowHomeHome), nameof(ShowHomeTutorials), nameof(ShowHomeGuide), nameof(ShowHomeIssues),
                     nameof(ShowSearchResults), nameof(DocSearchHeading),
                 })
            OnPropertyChanged(name);
    }

    /// <summary>Shows a page of the ModForge tab (parameter: a <see cref="ViewModel.HomePage"/> or its name).</summary>
    public RelayCommand ShowHomePageCommand => _showHomePage ??= new RelayCommand(p =>
    {
        if (p is HomePage page) GoHome(page);
        else if (p is string s && Enum.TryParse<HomePage>(s, out var named)) GoHome(named);
    });
    private RelayCommand? _showHomePage;

    // ── Home: what to learn next ───────────────────────────────────────────

    // The story's order, not the list's: a deeper tutorial is listed under the one it goes with, but builds on
    // the whole first run.
    private IEnumerable<TutorialListItem> Ladder => Tutorials.Where(t => t.Def.IsOnLadder).OrderBy(t => t.Def.Level);

    /// <summary>The first tutorial on the ladder not finished (or finished in an older version), or null when
    /// every one is done.</summary>
    public TutorialListItem? NextTutorialItem => Ladder.FirstOrDefault(t => !t.IsComplete || t.IsOutdated);

    public bool HasNextTutorial => NextTutorialItem != null;
    public bool AllTutorialsDone => NextTutorialItem == null;

    public int TutorialsDone => Ladder.Count(t => t.IsComplete && !t.IsOutdated);
    public int TutorialsTotal => Ladder.Count();

    /// <summary>How far through the tutorials, 0 to 1, for the bar on Home.</summary>
    public double TutorialsFraction => TutorialsTotal == 0 ? 0 : (double)TutorialsDone / TutorialsTotal;

    public string TutorialsProgressText => Loc.F("home.learn.progress", "done", TutorialsDone, "total", TutorialsTotal);

    /// <summary>The next tutorial's picture (its own or its group's), for its card on Home.</summary>
    public ImageSource? NextTutorialArt => NextTutorialItem is { } t ? TutorialRunner.HeaderArtFor(t.Def) : null;

    /// <summary>Starts (or takes again) the next tutorial.</summary>
    public RelayCommand ContinueLearningCommand => _continueLearning ??= new RelayCommand(() =>
    {
        if (NextTutorialItem is { } t) TutorialRunner.Start(t.Def);
    });
    private RelayCommand? _continueLearning;

    /// <summary>The Home page's numbers read again: a tutorial finished, a pack opened.</summary>
    internal void RaiseHomeLearning()
    {
        foreach (var name in new[]
                 {
                     nameof(NextTutorialItem), nameof(HasNextTutorial), nameof(AllTutorialsDone), nameof(TutorialsDone),
                     nameof(TutorialsTotal), nameof(TutorialsFraction), nameof(TutorialsProgressText), nameof(NextTutorialArt),
                 })
            OnPropertyChanged(name);
    }

    // ── Home: the pack ─────────────────────────────────────────────────────

    /// <summary>The issues in a sentence, for the pack's card on Home.</summary>
    public string IssuesSummary => Issues.Count == 0
        ? Loc.T("home.issues.none")
        : Loc.P("home.issues.some", Issues.Count);

    public bool HasIssues => Issues.Count > 0;

    /// <summary>
    /// How serious the worst issue is, for the issue count's notification pill: "error" (red), "warning" (amber),
    /// "info" (muted). The author (1.10.0): "can we have some notification-like highlighting to the number of issues
    /// if there are any? Just to bring some more attention to them." A pack with only notes stays quiet.
    /// </summary>
    public string IssueLevel => Issues.Any(i => i.Severity == Validation.Severity.Error) ? "error"
                              : Issues.Any(i => i.Severity == Validation.Severity.Warning) ? "warning"
                              : "info";

    /// <summary>The search's page's heading: what was looked for.</summary>
    public string DocSearchHeading => IsDocSearchActive ? Loc.F("home.search.results", "query", _docQuery.Trim()) : "";

    // ── The guide, as a reader ─────────────────────────────────────────────

    private IReadOnlyList<GuideEntry>? _guideEntries;

    /// <summary>Every topic of the guide in order, each with its part: the reader's sidebar.</summary>
    public IReadOnlyList<GuideEntry> GuideEntries => _guideEntries ??=
        DocTopics.Parts.SelectMany(p => p.Topics.Select(t => (p, t)))
                 .Select((x, i) => new GuideEntry(x.p, x.t, i)).ToList();

    /// <summary>The sidebar's list, under a heading for each part.</summary>
    public System.ComponentModel.ICollectionView GuideView
    {
        get
        {
            if (_guideView != null) return _guideView;
            _guideView = new System.Windows.Data.CollectionViewSource { Source = GuideEntries }.View;
            _guideView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(GuideEntry.PartName)));
            return _guideView;
        }
    }
    private System.ComponentModel.ICollectionView? _guideView;

    private GuideEntry? _guideEntry;

    /// <summary>The topic the reader shows. The first one until another is picked.</summary>
    public GuideEntry? SelectedGuideEntry
    {
        get => _guideEntry ??= GuideEntries.FirstOrDefault();
        set
        {
            if (value == null || ReferenceEquals(value, _guideEntry)) return;
            _guideEntry = value;
            OnPropertyChanged();
            foreach (var name in new[]
                     {
                         nameof(GuideTopic), nameof(GuidePicture), nameof(HasGuidePicture), nameof(GuidePartName),
                         nameof(GuidePrevious), nameof(GuideNext), nameof(HasGuidePrevious), nameof(HasGuideNext),
                         nameof(GuidePosition),
                     })
                OnPropertyChanged(name);
        }
    }

    public DocTopic? GuideTopic => SelectedGuideEntry?.Topic;
    public string GuidePartName => SelectedGuideEntry?.PartName ?? "";

    public GuideEntry? GuidePrevious => SelectedGuideEntry is { Index: > 0 } e ? GuideEntries[e.Index - 1] : null;
    public GuideEntry? GuideNext => SelectedGuideEntry is { } e && e.Index + 1 < GuideEntries.Count ? GuideEntries[e.Index + 1] : null;
    public bool HasGuidePrevious => GuidePrevious != null;
    public bool HasGuideNext => GuideNext != null;

    /// <summary>"Topic 4 of 36", under the reader's title.</summary>
    public string GuidePosition => SelectedGuideEntry is { } e
        ? Loc.F("home.guide.position", "number", e.Index + 1, "count", GuideEntries.Count)
        : "";

    public RelayCommand GuidePreviousCommand => _guidePrevious ??= new RelayCommand(() => { if (GuidePrevious is { } e) SelectedGuideEntry = e; });
    public RelayCommand GuideNextCommand => _guideNext ??= new RelayCommand(() => { if (GuideNext is { } e) SelectedGuideEntry = e; });
    private RelayCommand? _guidePrevious, _guideNext;

    /// <summary>Opens a topic in the reader (parameter: a <see cref="DocTopic"/> - from the search's results, a copy
    /// of the catalog's, found again by its title's key - or a <see cref="GuideEntry"/>).</summary>
    public RelayCommand OpenGuideTopicCommand => _openGuideTopic ??= new RelayCommand(p =>
    {
        GuideEntry? entry = p switch
        {
            GuideEntry g => g,
            DocTopic t => GuideEntries.FirstOrDefault(e => e.Topic.TitleKey == t.TitleKey),
            _ => null,
        };
        if (entry == null) return;
        SelectedGuideEntry = entry;
        GoHome(HomePage.Guide);
    });
    private RelayCommand? _openGuideTopic;

    /// <summary>
    /// The picture of the part of the editor a topic is about (Resources/GuidePictures/&lt;topic&gt;.jpg, the topic
    /// named by its title's key: docs.places.title is places.jpg), or null when it has none. Taken in the look the
    /// editor is in when there is one for it (places.EX.jpg).
    /// </summary>
    public ImageSource? GuidePicture
        => SelectedGuideEntry is { } e ? GuidePictures.Load(e.Topic.PictureName) : null;

    public bool HasGuidePicture => GuidePicture != null;

    /// <summary>The hero picture at the top of Home (Resources/TutorialHeaders/home.jpg), or null for the accent.</summary>
    public ImageSource? HomeHeroArt => TutorialRunner.ArtNamed("home");
}

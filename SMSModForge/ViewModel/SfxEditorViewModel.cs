using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Audio;

namespace SMSModForge.ViewModel;

/// <summary>
/// The SFX tab's sound editor (the author, 1.7.0): the sound as a strip, like
/// Audacity's but one channel tall, cut into pieces at a spot picked by
/// clicking, pieces taken out, and the whole pitched, sped up, echoed and given
/// a room.
/// <para/>
/// Works on the sound's <see cref="SfxDef.Edit"/> and nothing else: every cut
/// and every slider is written there as it happens, so undo, the list of
/// changes before saving and saving itself see an edit like any other change.
/// The recording is only ever read; the file the game plays is made from it
/// when the pack is saved (<see cref="SfxEdits"/>).
/// </summary>
public sealed class SfxEditorViewModel : ObservableObject
{
    private readonly EditableSound _sound;
    private readonly Func<string?> _packRoot;
    private readonly Action _edited;
    private readonly Services.SfxPreviewPlayer _player;

    /// <param name="edited">Called after each change, so it is an undo step of
    /// its own.</param>
    public SfxEditorViewModel(EditableSound sound, Func<string?> packRoot, Action edited,
                              Services.SfxPreviewPlayer player)
    {
        _sound = sound;
        _packRoot = packRoot;
        _edited = edited;
        _player = player;

        PlayCommand = new RelayCommand(Play, () => Current != null && !IsPreparing);
        StopCommand = new RelayCommand(() => _player.Stop());
        SplitCommand = new RelayCommand(Split, CanSplit);
        DeleteCommand = new RelayCommand(DeletePiece, () => Highlighted >= 0 && Pieces.Count > 1);
        ResetCommand = new RelayCommand(Reset, () => Model.Edit != null);
        ResetEffectsCommand = new RelayCommand(ResetEffects, () => HasEffects);
        ZoomInCommand = new RelayCommand(() => ZoomAround(Zoom * 1.5, ZoomAnchor()), () => Current != null);
        ZoomOutCommand = new RelayCommand(() => ZoomAround(Zoom / 1.5, ZoomAnchor()), () => Current != null && Zoom > 1);
        FitCommand = new RelayCommand(() => ZoomAround(1, 0), () => Current != null && Zoom > 1);

        Reload();
    }

    /// <summary>The sound's record, whichever tab it is from.</summary>
    private EditableSound Model => _sound;

    // ── The recordings ───────────────────────────────────────────────────

    /// <summary>One recording: the sound, or one of its variants.</summary>
    public sealed class Recording
    {
        public string File { get; init; } = "";
        public string Name => Path.GetFileName(File);
        public AudioData Data { get; init; } = null!;
        public WavePeaks Peaks { get; init; } = null!;
        public override string ToString() => Name;
    }

    /// <summary>The recording and its variants, once read.</summary>
    public ObservableCollection<Recording> Recordings { get; } = new();

    /// <summary>Whether there are variants to choose between - the picker
    /// above the strip shows only then.</summary>
    public bool HasVariants => Recordings.Count > 1;

    private Recording? _current;

    /// <summary>The recording on the strip. Each is cut on its own.</summary>
    public Recording? Current
    {
        get => _current;
        set
        {
            if (ReferenceEquals(_current, value)) return;
            _current = value;
            _cursor = 0;
            _highlighted = -1;
            _zoom = 1;
            _viewStart = 0;
            OnPropertyChanged();
            Changed();
        }
    }

    /// <summary>What reading the recordings came to: empty while there is
    /// nothing to read, or a sentence saying why there is no strip.</summary>
    public string Status { get; private set; } = "";

    /// <summary>The reading in progress, for whoever has to wait for it.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The recording the sound is made from: the edit's when it has
    /// one, otherwise the file it plays.</summary>
    public string Source => Model.Edit?.Source is { Length: > 0 } s ? s : Model.AudioPath;

    /// <summary>Read the recordings again - the sound was pointed at a
    /// different one.</summary>
    public void Reload()
    {
        string? root = _packRoot();
        string source = Source;
        Recordings.Clear();
        _current = null;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(HasVariants));

        if (string.IsNullOrWhiteSpace(source)) { SetStatus(Loc.T("sfxEdit.noFile")); return; }
        if (root == null) { SetStatus(Loc.T("sfxEdit.saveFirst")); return; }
        // Music has no variants: the game looks for them only beside a sound.
        var files = Model.HasVariants ? SfxEdits.Files(root, source) : new List<string> { source };
        if (files.Count == 0 || !SfxEdits.RecordingExists(root, files[0]))
        {
            SetStatus(Loc.F("sfxEdit.missing", "file", source));
            return;
        }

        SetStatus(Loc.T("sfxEdit.reading"));
        var scheduler = System.Threading.SynchronizationContext.Current != null
            ? TaskScheduler.FromCurrentSynchronizationContext()
            : TaskScheduler.Default;
        Loading = Task.Run(() => files.Select(f =>
            {
                var data = Read(SfxEdits.RecordingAbs(root, f));
                return data == null ? null : new Recording { File = f, Data = data, Peaks = new WavePeaks(data) };
            }).Where(r => r != null).ToList())
            .ContinueWith(t =>
            {
                if (!string.Equals(Source, source, StringComparison.OrdinalIgnoreCase)) return;   // moved on meanwhile
                var read = t.IsCompletedSuccessfully ? t.Result : new List<Recording?>();
                foreach (var r in read) Recordings.Add(r!);
                OnPropertyChanged(nameof(HasVariants));
                if (Recordings.Count == 0) { SetStatus(Loc.F("sfxEdit.unreadable", "file", source)); return; }
                SetStatus("");
                Current = Recordings[0];
            }, scheduler);
    }

    private void SetStatus(string status)
    {
        Status = status;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasStatus));
    }

    public bool HasStatus => Status.Length > 0;

    /// <summary>Recordings already read, by path and the file's size and time:
    /// coming back to a sound, or undoing a cut, does not read it again.</summary>
    private static readonly Dictionary<string, AudioData> _read = new(StringComparer.OrdinalIgnoreCase);

    private static AudioData? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            string id = path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            lock (_read)
                if (_read.TryGetValue(id, out var known)) return known;
            var data = AudioData.Load(path);
            lock (_read)
            {
                if (_read.Count > 64) _read.Clear();
                _read[id] = data;
            }
            return data;
        }
        catch (Exception) { return null; }
    }

    // ── The pieces ───────────────────────────────────────────────────────

    /// <summary>The current recording's pieces, in the order they play.
    /// Uncut, the whole recording is one piece.</summary>
    public IReadOnlyList<SfxPieceDef> Pieces
    {
        get
        {
            if (Current == null) return Array.Empty<SfxPieceDef>();
            var cut = Model.Edit?.PiecesOf(Current.Name);
            return cut is { Count: > 0 } ? cut : new[] { new SfxPieceDef { From = 0, To = Current.Data.Seconds } };
        }
    }

    /// <summary>How long the pieces last, one after another.</summary>
    public double Length => Pieces.Sum(p => p.Length);

    /// <summary>Where piece <paramref name="index"/> starts on the strip.</summary>
    public double StartOf(int index) => Pieces.Take(index).Sum(p => p.Length);

    /// <summary>The piece at a point on the strip, or -1 past the end.</summary>
    public int PieceAt(double time)
    {
        var pieces = Pieces;
        double at = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            if (time < at + pieces[i].Length || (i == pieces.Count - 1 && time <= at + pieces[i].Length)) return i;
            at += pieces[i].Length;
        }
        return -1;
    }

    private double _cursor;

    /// <summary>The spot picked on the strip, in seconds from its start.</summary>
    public double Cursor
    {
        get => _cursor;
        set
        {
            _cursor = Math.Clamp(value, 0, Length);
            _highlighted = PieceAt(_cursor);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Highlighted));
            Changed();
        }
    }

    private int _highlighted = -1;

    /// <summary>The piece the cursor is in, highlighted; -1 for none.</summary>
    public int Highlighted => _highlighted;

    /// <summary>Not too near a piece's edge to cut there: a sliver a few
    /// samples long is no piece anybody meant to make.</summary>
    private bool CanSplit()
    {
        int i = PieceAt(_cursor);
        if (i < 0 || Current == null) return false;
        double into = _cursor - StartOf(i);
        double min = 0.005;
        return into > min && into < Pieces[i].Length - min;
    }

    /// <summary>Cut the piece under the cursor in two, at the cursor.</summary>
    public void Split()
    {
        if (!CanSplit()) return;
        int i = PieceAt(_cursor);
        var pieces = Pieces.Select(Copy).ToList();
        var p = pieces[i];
        double at = p.From + (_cursor - StartOf(i));
        pieces[i] = new SfxPieceDef { From = p.From, To = Round(at) };
        pieces.Insert(i + 1, new SfxPieceDef { From = Round(at), To = p.To });
        Store(pieces);
        _highlighted = i + 1;   // the half after the cut, as a new strip
        Done();
    }

    /// <summary>Take the highlighted piece out; the rest close up behind it.</summary>
    public void DeletePiece()
    {
        if (_highlighted < 0 || Pieces.Count <= 1) return;
        var pieces = Pieces.Select(Copy).ToList();
        double start = StartOf(_highlighted);
        pieces.RemoveAt(_highlighted);
        Store(pieces);
        _cursor = Math.Min(start, Length);
        _highlighted = Math.Min(_highlighted, Pieces.Count - 1);
        Done();
    }

    private static SfxPieceDef Copy(SfxPieceDef p) => new() { From = p.From, To = p.To };

    private static double Round(double seconds) => Math.Round(seconds, 5);

    /// <summary>Write the current recording's pieces into the edit - or take
    /// them out, when they are the whole recording again.</summary>
    private void Store(List<SfxPieceDef> pieces)
    {
        if (Current == null) return;
        var edit = EnsureEdit();
        edit.Files ??= new List<SfxFileEditDef>();
        edit.Files.RemoveAll(f => string.Equals(f.File, Current.Name, StringComparison.OrdinalIgnoreCase));
        bool whole = pieces.Count == 1 && pieces[0].From <= 1e-6
                     && Math.Abs(pieces[0].To - Current.Data.Seconds) <= 1e-4;
        if (!whole) edit.Files.Add(new SfxFileEditDef { File = Current.Name, Pieces = pieces });
        if (edit.Files.Count == 0) edit.Files = null;
    }

    // ── Effects ──────────────────────────────────────────────────────────

    /// <summary>Semitones, -12 to 12.</summary>
    public double Pitch
    {
        get => Model.Edit?.Pitch ?? 0;
        set { if (Math.Abs(value - Pitch) < 1e-9) return; EnsureEdit().Pitch = Math.Round(Math.Clamp(value, -12, 12), 2); Effected(); }
    }

    /// <summary>Times as fast, 0.5 to 2.</summary>
    public double Speed
    {
        get => Model.Edit?.Speed ?? 1;
        set { if (Math.Abs(value - Speed) < 1e-9) return; EnsureEdit().Speed = Math.Round(Math.Clamp(value, 0.5, 2), 3); Effected(); }
    }

    /// <summary>Seconds between the sound and its echo.</summary>
    public double EchoDelay
    {
        get => Model.Edit?.Echo?.Delay ?? 0.25;
        set { if (Math.Abs(value - EchoDelay) < 1e-9) return; (EnsureEdit().Echo ??= new SfxEchoDef()).Delay = Math.Round(Math.Clamp(value, 0.05, 1), 3); Effected(); }
    }

    /// <summary>How loud each echo is, 0 (none) to 0.9.</summary>
    public double EchoAmount
    {
        get => Model.Edit?.Echo?.Amount ?? 0;
        set { if (Math.Abs(value - EchoAmount) < 1e-9) return; (EnsureEdit().Echo ??= new SfxEchoDef()).Amount = Math.Round(Math.Clamp(value, 0, 0.9), 3); Effected(); }
    }

    /// <summary>How much of the room is heard, 0 (none) to 1.</summary>
    public double ReverbAmount
    {
        get => Model.Edit?.Reverb?.Amount ?? 0;
        set { if (Math.Abs(value - ReverbAmount) < 1e-9) return; (EnsureEdit().Reverb ??= new SfxReverbDef()).Amount = Math.Round(Math.Clamp(value, 0, 1), 3); Effected(); }
    }

    /// <summary>How big the room is, 0 to 1.</summary>
    public double ReverbRoom
    {
        get => Model.Edit?.Reverb?.Room ?? 0.5;
        set { if (Math.Abs(value - ReverbRoom) < 1e-9) return; (EnsureEdit().Reverb ??= new SfxReverbDef()).Room = Math.Round(Math.Clamp(value, 0, 1), 3); Effected(); }
    }

    public string PitchText => Pitch == 0 ? Loc.T("sfxEdit.asRecorded")
        : Loc.F(Math.Abs(Pitch) == 1 ? "sfxEdit.semitone" : "sfxEdit.semitones",
                "n", (Pitch > 0 ? "+" : "") + Pitch.ToString("0.#", CultureInfo.CurrentCulture));
    public string SpeedText => Speed == 1 ? Loc.T("sfxEdit.asRecorded")
        : Speed.ToString("0.##", CultureInfo.CurrentCulture) + "×";
    public string EchoText => EchoAmount <= 0 ? Loc.T("sfxEdit.off") : Percent(EchoAmount);
    public string EchoDelayText => Loc.F("sfxEdit.seconds", "n", EchoDelay.ToString("0.00", CultureInfo.CurrentCulture));
    public string ReverbText => ReverbAmount <= 0 ? Loc.T("sfxEdit.off") : Percent(ReverbAmount);
    public string RoomText => Percent(ReverbRoom);

    private static string Percent(double fraction)
        => Loc.F("sfxEdit.percent", "n", (fraction * 100).ToString("0", CultureInfo.CurrentCulture));

    private bool HasEffects => Model.Edit is { } e
        && (e.Pitch != 0 || e.Speed != 1 || e.Echo != null || e.Reverb != null);

    private void Effected()
    {
        foreach (string p in new[] { nameof(Pitch), nameof(Speed), nameof(EchoDelay), nameof(EchoAmount),
                                     nameof(ReverbAmount), nameof(ReverbRoom), nameof(PitchText),
                                     nameof(SpeedText), nameof(EchoText), nameof(ReverbText),
                                     nameof(EchoDelayText), nameof(RoomText),
                                     nameof(HasEcho), nameof(HasReverb) })
            OnPropertyChanged(p);
        // A slider moves through many values on the way to the one meant: the
        // step is taken when it is let go (the window does that), not at each.
        Done(step: false);
    }

    /// <summary>Whether there is an echo to set the delay of.</summary>
    public bool HasEcho => EchoAmount > 0;

    /// <summary>Whether there is a room to set the size of.</summary>
    public bool HasReverb => ReverbAmount > 0;

    /// <summary>Put pitch, speed, echo and reverb back as recorded, keeping
    /// the cuts.</summary>
    public void ResetEffects()
    {
        if (Model.Edit == null) return;
        Model.Edit.Pitch = 0;
        Model.Edit.Speed = 1;
        Model.Edit.Echo = null;
        Model.Edit.Reverb = null;
        Effected();
        _edited();
    }

    /// <summary>Put the sound back as recorded: no cuts in any of its files,
    /// no effects, and the recording itself played again.</summary>
    public void Reset()
    {
        if (Model.Edit == null) return;
        _player.Stop();
        string source = Model.Edit.Source;
        Model.Edit = null;
        if (!string.IsNullOrWhiteSpace(source)) Model.AudioPath = source;
        _cursor = 0;
        _highlighted = -1;
        Effected();
        Changed();
        _edited();
    }

    // ── Keeping the edit ─────────────────────────────────────────────────

    private SfxEditDef EnsureEdit()
        => Model.Edit ??= new SfxEditDef { Source = Model.AudioPath };

    /// <summary>
    /// After every change: an edit that no longer changes anything is no edit
    /// - the sound plays its recording again, rather than a file made to sound
    /// exactly like it - and the change is an undo step of its own.
    /// </summary>
    private void Done(bool step = true)
    {
        if (Model.Edit is { } edit && !edit.ChangesAnything)
        {
            Model.Edit = null;
            if (!string.IsNullOrWhiteSpace(edit.Source)) Model.AudioPath = edit.Source;
        }
        _rendered = null;
        Changed();
        if (step) _edited();
    }

    /// <summary>Raised whenever the strip has something new to draw.</summary>
    public event Action? Redraw;

    private void Changed()
    {
        _cursor = Math.Clamp(_cursor, 0, Length);
        if (_highlighted >= Pieces.Count) _highlighted = Pieces.Count - 1;
        _viewStart = Math.Clamp(_viewStart, 0, Math.Max(0, Length - VisibleSeconds));
        OnPropertyChanged(nameof(Cursor));
        OnPropertyChanged(nameof(Highlighted));
        OnPropertyChanged(nameof(Pieces));
        OnPropertyChanged(nameof(Length));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsEdited));
        RaiseView();
        PlayCommand.Raise();
        SplitCommand.Raise();
        DeleteCommand.Raise();
        ResetCommand.Raise();
        ResetEffectsCommand.Raise();
        ZoomInCommand.Raise();
        ZoomOutCommand.Raise();
        FitCommand.Raise();
        Redraw?.Invoke();
    }

    /// <summary>Whether the sound is edited at all.</summary>
    public bool IsEdited => Model.Edit != null;

    /// <summary>A line under the strip: where the cursor is, which piece, and
    /// how long the sound is now against how long it was.</summary>
    public string Summary
    {
        get
        {
            if (Current == null) return "";
            string F(double s) => s.ToString("0.000", CultureInfo.CurrentCulture);
            return Loc.F("sfxEdit.summary",
                "cursor", F(_cursor),
                "piece", Math.Max(0, _highlighted) + 1, "pieces", Pieces.Count,
                "length", F(Length), "was", F(Current.Data.Seconds));
        }
    }

    // ── Zoom ─────────────────────────────────────────────────────────────

    private double _zoom = 1;

    /// <summary>How far in: 1 shows the whole sound across the strip, 2 half
    /// of it, and so on.</summary>
    public double Zoom => _zoom;

    private double _viewStart;

    /// <summary>The time at the strip's left edge.</summary>
    public double ViewStart
    {
        get => _viewStart;
        set
        {
            _viewStart = Math.Clamp(value, 0, Math.Max(0, Length - VisibleSeconds));
            RaiseView();
            Redraw?.Invoke();
        }
    }

    /// <summary>How much of the sound the strip shows at once.</summary>
    public double VisibleSeconds => Length / _zoom;

    /// <summary>How far the view can be scrolled.</summary>
    public double ScrollMax => Math.Max(0, Length - VisibleSeconds);

    public bool CanScroll => ScrollMax > 0;

    /// <summary>The most a strip can zoom in to: about four samples to a pixel
    /// on a strip a thousand wide.</summary>
    private double MaxZoom => Current == null ? 1 : Math.Max(1, Length * Current.Data.SampleRate / 4000.0);

    /// <summary>Zoom to <paramref name="zoom"/>, keeping <paramref name="anchor"/>
    /// - a time on the strip - where it is on screen.</summary>
    public void ZoomAround(double zoom, double anchor)
    {
        if (Current == null || Length <= 0) return;
        double before = VisibleSeconds;
        double fraction = before > 0 ? (anchor - _viewStart) / before : 0;
        _zoom = Math.Clamp(zoom, 1, MaxZoom);
        _viewStart = Math.Clamp(anchor - fraction * VisibleSeconds, 0, ScrollMax);
        RaiseView();
        ZoomOutCommand.Raise();
        FitCommand.Raise();
        Redraw?.Invoke();
    }

    /// <summary>What the zoom buttons keep in place: the cursor when it is in
    /// view, the middle otherwise.</summary>
    private double ZoomAnchor()
        => _cursor >= _viewStart && _cursor <= _viewStart + VisibleSeconds ? _cursor : _viewStart + VisibleSeconds / 2;

    private void RaiseView()
    {
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(ViewStart));
        OnPropertyChanged(nameof(VisibleSeconds));
        OnPropertyChanged(nameof(ScrollMax));
        OnPropertyChanged(nameof(CanScroll));
    }

    // ── Playing ──────────────────────────────────────────────────────────

    private (string Key, AudioData Sound)? _rendered;

    private string RenderKey()
        => Current!.File + "|" + Newtonsoft.Json.JsonConvert.SerializeObject(Model.Edit);

    /// <summary>The current recording as edited: pieces, then effects.
    /// Made once per edit and kept until the next.</summary>
    public AudioData? Rendered()
    {
        if (Current == null) return null;
        string key = RenderKey();
        if (_rendered is { } r && r.Key == key) return r.Sound;
        var made = SfxRenderer.Render(Current.Data, Model.Edit?.PiecesOf(Current.Name), Model.Edit);
        _rendered = (key, made);
        return made;
    }

    /// <summary>Longest a sound is made on the spot when Play is pressed; a
    /// longer one - a music track - is made in the background, so the window
    /// does not freeze while pitch or speed works through minutes of it.</summary>
    private const double MadeOnTheSpot = 15;

    /// <summary>Whether a long sound is being made, to be played when it is.</summary>
    public bool IsPreparing { get; private set; }

    /// <summary>Play the sound as edited, from the cursor.</summary>
    public void Play() => PlayFrom(_cursor >= Length - 1e-6 ? 0 : _cursor);

    /// <summary>Play the sound as edited, from its start - the tab's own Play
    /// button, which plays what the game will.</summary>
    public void PlayFromStart() => PlayFrom(0);

    private void PlayFrom(double spot)
    {
        if (Current == null || IsPreparing) return;
        string key = RenderKey();
        if ((_rendered is { } r && r.Key == key) || Current.Data.Seconds <= MadeOnTheSpot)
        {
            Start(Rendered()!, spot);
            return;
        }

        // Made from copies: the sliders can move while it is being made.
        var data = Current.Data;
        var pieces = Model.Edit?.PiecesOf(Current.Name)?.Select(Copy).ToList();
        var edit = Model.Edit == null ? null
            : Newtonsoft.Json.JsonConvert.DeserializeObject<SfxEditDef>(Newtonsoft.Json.JsonConvert.SerializeObject(Model.Edit));
        SetPreparing(true);
        var scheduler = System.Threading.SynchronizationContext.Current != null
            ? TaskScheduler.FromCurrentSynchronizationContext()
            : TaskScheduler.Default;
        Task.Run(() => SfxRenderer.Render(data, pieces, edit)).ContinueWith(t =>
        {
            SetPreparing(false);
            if (!t.IsCompletedSuccessfully) return;
            _rendered = (key, t.Result);
            // Moved on meanwhile - another recording, another edit: not this one.
            if (Current != null && RenderKey() == key) Start(t.Result, spot);
        }, scheduler);
    }

    private void SetPreparing(bool on)
    {
        IsPreparing = on;
        OnPropertyChanged(nameof(IsPreparing));
        PlayCommand.Raise();
    }

    private void Start(AudioData sound, double spot)
    {
        _player.Play(sound, Model.Volume, spot * SfxRenderer.TimeScale(Model.Edit));
        Started?.Invoke();
    }

    /// <summary>Raised when the sound starts playing, so the strip can follow
    /// it with a line.</summary>
    public event Action? Started;

    /// <summary>Where the playing has got to on the strip, or null when
    /// nothing of this sound is playing or it is past the pieces, in an echo's
    /// or a room's tail.</summary>
    public double? Playhead
    {
        get
        {
            if (_player.Position is not { } at) return null;
            double t = at / SfxRenderer.TimeScale(Model.Edit);
            return t <= Length ? t : null;
        }
    }

    public RelayCommand PlayCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand SplitCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ResetEffectsCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand FitCommand { get; }
}

/// <summary>
/// A sound the editor can cut and give effects - an SFX or a music track: what
/// it plays, how it was edited, how loud it is, and whether variants beside it
/// play in its place.
/// </summary>
public sealed class EditableSound
{
    private readonly Func<string> _getPath;
    private readonly Action<string> _setPath;
    private readonly Func<SfxEditDef?> _getEdit;
    private readonly Action<SfxEditDef?> _setEdit;
    private readonly Func<float> _volume;

    public EditableSound(Func<string> getPath, Action<string> setPath,
                         Func<SfxEditDef?> getEdit, Action<SfxEditDef?> setEdit,
                         Func<float> volume, bool hasVariants)
    {
        _getPath = getPath;
        _setPath = setPath;
        _getEdit = getEdit;
        _setEdit = setEdit;
        _volume = volume;
        HasVariants = hasVariants;
    }

    public static EditableSound Of(SfxDef sfx)
        => new(() => sfx.AudioPath, p => sfx.AudioPath = p, () => sfx.Edit, e => sfx.Edit = e,
               () => GameLoudness.Sfx(sfx.DefaultVolume), hasVariants: true);

    public static EditableSound Of(MusicDef music)
        => new(() => music.AudioPath, p => music.AudioPath = p, () => music.Edit, e => music.Edit = e,
               () => GameLoudness.Music(music.Volume), hasVariants: false);

    /// <summary>What the game plays: the recording, or the file made from it.</summary>
    public string AudioPath { get => _getPath(); set => _setPath(value ?? ""); }

    public SfxEditDef? Edit { get => _getEdit(); set => _setEdit(value); }

    /// <summary>The gain the game plays it at - see <see cref="GameLoudness"/>.</summary>
    public float Volume => _volume();

    public bool HasVariants { get; }
}

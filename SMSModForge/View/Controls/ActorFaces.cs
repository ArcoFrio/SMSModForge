using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Rendering;

namespace SMSModForge.View.Controls;

/// <summary>
/// The faces beside the names in the Actor picker (<see cref="CharacterFace"/>).
/// <para/>
/// A picker of the game's cast lists a hundred and more names, and cutting a
/// hundred faces as it opens would be felt. So each name gets a
/// <see cref="Slot"/> at once - empty, drawn as the neutral face - and its face
/// is cut on a worker thread and arrives a moment later; once cut, a face is
/// kept until the characters change.
/// </summary>
public static class ActorFaces
{
    private static readonly Dictionary<string, Slot> Slots = new(StringComparer.OrdinalIgnoreCase);
    private static Func<string, (string Base, string? Blink)?>? _filesOf;

    /// <summary>Where a speaker's bust files are; given by the window's view
    /// model whenever its characters change. Every face is cut again.</summary>
    public static void Use(Func<string, (string Base, string? Blink)?> filesOf)
    {
        _filesOf = filesOf;
        foreach (var slot in Slots.Values) slot.Refresh(filesOf);
    }

    /// <summary>The slot for <paramref name="actorKey"/>'s face.</summary>
    public static Slot For(string actorKey)
    {
        if (!Slots.TryGetValue(actorKey ?? "", out var slot))
        {
            slot = new Slot(actorKey ?? "");
            Slots[slot.Key] = slot;
            if (_filesOf != null) slot.Refresh(_filesOf);
        }
        return slot;
    }

    /// <summary>One name's face, filled in when it has been cut.</summary>
    public sealed class Slot : INotifyPropertyChanged
    {
        private ImageSource? _face;
        private bool _known;
        private int _asked;

        internal Slot(string key) => Key = key;

        public string Key { get; }

        /// <summary>The face, or null while it is being cut and when there is none.</summary>
        public ImageSource? Face
        {
            get => _face;
            private set { _face = value; Changed(nameof(Face)); Changed(nameof(ShowsNeutral)); }
        }

        /// <summary>The neutral face is drawn instead: none could be cut, or
        /// none yet.</summary>
        public bool ShowsNeutral => _face == null;

        /// <summary>Whether the face has been looked for and the answer is in.</summary>
        public bool Known => _known;

        internal void Refresh(Func<string, (string Base, string? Blink)?> filesOf)
        {
            int ask = ++_asked;
            (string Base, string? Blink)? files = null;
            try { files = string.IsNullOrWhiteSpace(Key) ? null : filesOf(Key); }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException) { }

            if (files is not { } f)
            {
                Settle(ask, null);
                return;
            }
            var dispatcher = Application.Current?.Dispatcher;
            Task.Run(() => CharacterFace.Cut(f.Base, f.Blink)).ContinueWith(t =>
            {
                var face = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(() => Settle(ask, face));
                else Settle(ask, face);
            }, TaskScheduler.Default);
        }

        private void Settle(int ask, ImageSource? face)
        {
            if (ask != _asked) return;   // the characters changed while it was being cut
            _known = true;
            Face = face;
            Changed(nameof(Known));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>A name in the Actor picker to its face's slot, for the picker's
/// rows to bind the face to.</summary>
public sealed class ActorFaceConverter : IValueConverter
{
    public static readonly ActorFaceConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key ? ActorFaces.For(key) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SMSModForge.Tutorials;

/// <summary>
/// The maths behind the tutorial overlay: the dimming shape with a hole cut in
/// it, and where the instruction callout sits relative to that hole.
/// <para/>
/// Pure, and separate from the control that draws it, because this is where
/// the bugs would be. Everything is in device-independent units taken from
/// <c>TransformToVisual</c> — never physical pixels — so display scaling and
/// resolution need no special handling: WPF has already converted by the time
/// a rectangle reaches here.
/// </summary>
public static class SpotlightGeometry
{
    /// <summary>Breathing room left around the highlighted control.</summary>
    public const double Padding = 6;

    /// <summary>Corner rounding on the hole.</summary>
    public const double CornerRadius = 4;

    /// <summary>Gap between the hole and the callout beside it.</summary>
    public const double Gap = 12;

    /// <summary>How close to the window edge the callout may sit.</summary>
    public const double Margin = 12;

    /// <summary>
    /// The dimming geometry: the whole window with each hole cut out of it.
    /// Built as one figure with <see cref="FillRule.EvenOdd"/>, so the holes
    /// are genuinely absent rather than painted over — which is what lets
    /// clicks inside them reach the controls underneath, and what keeps the
    /// dimmed area a single hit-testable shape that swallows clicks outside.
    /// <para/>
    /// Several holes, because the lit area is a permission rather than only an
    /// emphasis: a step that says "add a node, then set its actor" has to light
    /// both, or its second half cannot be done.
    /// <para/>
    /// No holes gives a plain full-window dim, which is the right answer for a
    /// step that highlights nothing.
    /// </summary>
    public static Geometry BuildMask(Size window, IReadOnlyList<Rect> holes)
    {
        var bounds = new Rect(0, 0, window.Width, window.Height);
        var outer = new RectangleGeometry(bounds);

        var cut = Prepare(holes, bounds);
        if (cut.Count == 0) return outer;

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(outer);
        foreach (var r in cut)
            group.Children.Add(new RectangleGeometry(r, CornerRadius, CornerRadius));
        return group;
    }

    /// <summary>Single-hole convenience, for the common case.</summary>
    public static Geometry BuildMask(Size window, Rect hole)
        => BuildMask(window, new[] { hole });

    /// <summary>
    /// The holes to actually cut: padded, clipped to the window, empties
    /// dropped, and — the part that matters — any that touch each other merged
    /// into one.
    /// <para/>
    /// Merging is not tidiness. Under <see cref="FillRule.EvenOdd"/> a region
    /// crossed twice fills back in, so two overlapping holes would paint a dark
    /// patch exactly where they meet, and that patch would swallow clicks.
    /// Anchors that sit near each other — a toolbar and the pane below it —
    /// overlap as soon as padding is added, so this is the normal case rather
    /// than a corner one.
    /// </summary>
    public static IReadOnlyList<Rect> Prepare(IReadOnlyList<Rect> holes, Rect window)
    {
        var list = new List<Rect>();
        if (holes == null) return list;

        foreach (var h in holes)
        {
            if (h.IsEmpty || h.Width <= 0 || h.Height <= 0) continue;
            var r = Inflate(h, Padding);
            r.Intersect(window);
            if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) continue;
            list.Add(r);
        }

        // Repeated until nothing merges: joining two rectangles can bring the
        // result into contact with a third.
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < list.Count && !merged; i++)
            for (int j = i + 1; j < list.Count && !merged; j++)
            {
                if (!list[i].IntersectsWith(list[j])) continue;
                list[i] = Rect.Union(list[i], list[j]);
                list.RemoveAt(j);
                merged = true;
            }
        }
        return list;
    }

    /// <summary>The padded hole, for drawing the ring around the spotlight.</summary>
    public static Rect SpotlightRect(Size window, Rect hole)
    {
        if (hole.IsEmpty || hole.Width <= 0 || hole.Height <= 0) return Rect.Empty;
        var padded = Inflate(hole, Padding);
        padded.Intersect(new Rect(0, 0, window.Width, window.Height));
        return padded;
    }

    /// <summary>
    /// Where to put the callout.
    /// <para/>
    /// Four fixed corners, tried in a fixed order, and the first that does not
    /// touch the spotlight wins. Earlier this followed the highlighted control
    /// around — which is what most guided tours do, and which put the
    /// instructions somewhere different on every step, sometimes over a list or
    /// a preview the author was about to need. Landing in one of four known
    /// places instead means the reader learns where to look once, and the
    /// corners are the least valuable ground on screen.
    /// <para/>
    /// The connection between instruction and control is carried by the
    /// spotlight and its ring, which is what that job is for.
    /// <para/>
    /// If every corner is blocked — a spotlight covering most of the window —
    /// the least-overlapped corner is used, on the grounds that unreadable
    /// instructions are worse than a partly covered highlight.
    /// </summary>
    public static (Point At, int Corner) PlaceCallout(
        Rect hole, Size callout, Size window, int sticky = -1)
        => PlaceCallout(new[] { hole }, callout, window, sticky);

    /// <inheritdoc cref="PlaceCallout(Rect, Size, Size, int)"/>
    public static (Point At, int Corner) PlaceCallout(
        IReadOnlyList<Rect> holes, Size callout, Size window, int sticky = -1)
    {
        double right  = Math.Max(Margin, window.Width  - callout.Width  - Margin);
        double bottom = Math.Max(Margin, window.Height - callout.Height - Margin);

        // Bottom-right first: editors put their lists and trees on the left, so
        // the bottom-right corner is the one least likely to be in the way.
        var corners = new[]
        {
            new Point(right,  bottom),
            new Point(Margin, bottom),
            new Point(right,  Margin),
            new Point(Margin, Margin),
        };

        // Every lit region, with the callout clearance added, so the
        // instructions do not come to rest on anything the step needs.
        var spots = new List<Rect>();
        foreach (var h in holes)
        {
            if (h.IsEmpty || h.Width <= 0 || h.Height <= 0) continue;
            spots.Add(Inflate(h, Padding + Gap));
        }

        if (spots.Count == 0)
            return (corners[sticky >= 0 ? sticky : 0], sticky >= 0 ? sticky : 0);

        // Keep the corner already in use if it still works.
        //
        // Without this the callout hops about on any step whose control is
        // animated or re-lays-out: the measured rectangle shifts by a fraction
        // of a pixel, a marginal corner tips over the overlap test, and the
        // instructions jump corner to corner every frame. Re-picking is only
        // worth it when the corner in use has actually become unusable.
        if (sticky >= 0 && sticky < corners.Length && OverlapArea(corners[sticky], callout, spots) <= 0)
            return (corners[sticky], sticky);

        int best = 0;
        double bestOverlap = double.MaxValue;
        for (int i = 0; i < corners.Length; i++)
        {
            double overlap = OverlapArea(corners[i], callout, spots);
            if (overlap <= 0) return (corners[i], i);
            if (overlap < bestOverlap) { bestOverlap = overlap; best = i; }
        }
        return (corners[best], best);
    }

    /// <summary>How much of the callout would sit over lit ground. Summed
    /// across regions, which can double-count where two overlap — that only
    /// makes a crowded corner rank worse, which is the right direction.</summary>
    private static double OverlapArea(Point at, Size callout, IReadOnlyList<Rect> spots)
    {
        var box = new Rect(at, callout);
        double total = 0;
        foreach (var s in spots)
        {
            var hit = Rect.Intersect(box, s);
            if (!hit.IsEmpty) total += hit.Width * hit.Height;
        }
        return total;
    }

    /// <summary>
    /// Rounds a measured rectangle to whole units, so the sub-pixel drift an
    /// animated control produces does not read as movement worth redrawing for.
    /// </summary>
    public static Rect Quantize(Rect r)
        => r.IsEmpty ? r : new Rect(Math.Floor(r.X), Math.Floor(r.Y),
                                    Math.Ceiling(r.Width), Math.Ceiling(r.Height));

    private static Rect Inflate(Rect r, double by)
        => new Rect(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);
}

using System;
using System.Collections.Generic;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The outline a pack bust casts its 2D shadow from (1.9.1).
    /// <para/>
    /// The room's lamps throw a bust's shadow on the wall behind it. URP takes
    /// that shape from the sprite's mesh, which Unity makes itself from the
    /// picture (a "tight" mesh, coarse: 13 points for a whole person), then
    /// trims it. Both happen in the engine's own code, which the editor cannot
    /// run, so its preview could only ever guess at the shadow. Instead the
    /// plugin hands the caster this outline - the art's own edge, pulled in by
    /// <see cref="InsetUnits"/> as URP's trim pulls in the game's busts - and
    /// the editor's preview casts the same one. Compiled into both from one
    /// file, so they cannot drift.
    /// <para/>
    /// Every number in it is a whole texel: the points are corners of the
    /// picture's texel grid, so the plugin and the editor get the same points
    /// whatever their floating point does.
    /// </summary>
    public static class ShadowOutline
    {
        /// <summary>How far inside the art's edge the outline runs, in the
        /// sprite's own units (0.04 = 4 texels of a 256 bust at 100 texels a
        /// unit): where the game's own busts' trimmed shapes sit.</summary>
        public const float InsetUnits = 0.04f;

        /// <summary>How far a simplified edge may stray from the texel edges it
        /// stands for, in texels.</summary>
        public const double Tolerance = 0.75;

        /// <summary>
        /// The outline of the art's largest solid part, counter-clockwise, as
        /// texel-grid corners (x right, y UP from the picture's bottom-left
        /// corner): x0, y0, x1, y1, ... Empty when nothing is left once the
        /// edge is pulled in.
        /// </summary>
        /// <param name="alpha">One value per texel, rows from the BOTTOM.</param>
        /// <param name="texelsPerUnit">The sprite's pixels per unit.</param>
        public static int[] Of(byte[] alpha, int width, int height, float texelsPerUnit)
        {
            int inset = Math.Max(0, (int)Math.Round(InsetUnits * texelsPerUnit));
            var solid = Erode(alpha, width, height, inset);
            var corners = LargestOutline(solid, width, height);
            if (corners.Count < 3) return new int[0];
            var kept = Simplify(corners);
            var result = new int[kept.Count * 2];
            for (int i = 0; i < kept.Count; i++) { result[2 * i] = kept[i].X; result[2 * i + 1] = kept[i].Y; }
            return result;
        }

        /// <summary>The texels at least <paramref name="inset"/> texels (centre
        /// to centre) from any see-through texel or the picture's edge.</summary>
        private static bool[] Erode(byte[] alpha, int w, int h, int inset)
        {
            // How many solid texels run left and right from each (itself
            // included): one row of the disc fits where both reach its half-width.
            var left = new int[w * h];
            var right = new int[w * h];
            for (int y = 0; y < h; y++)
            {
                int run = 0;
                for (int x = 0; x < w; x++) { run = alpha[y * w + x] != 0 ? run + 1 : 0; left[y * w + x] = run; }
                run = 0;
                for (int x = w - 1; x >= 0; x--) { run = alpha[y * w + x] != 0 ? run + 1 : 0; right[y * w + x] = run; }
            }
            // The disc: every texel whose centre is within inset of this one's.
            var half = new int[inset + 1];
            for (int dy = 0; dy <= inset; dy++)
            {
                int k = 0;
                while ((k + 1) * (k + 1) + dy * dy <= inset * inset) k++;
                half[dy] = k;
            }
            var solid = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool keep = alpha[y * w + x] != 0;
                    for (int dy = -inset; keep && dy <= inset; dy++)
                    {
                        int ny = y + dy;
                        int need = half[dy < 0 ? -dy : dy] + 1;
                        keep = ny >= 0 && ny < h && left[ny * w + x] >= need && right[ny * w + x] >= need;
                    }
                    solid[y * w + x] = keep;
                }
            return solid;
        }

        private struct Corner
        {
            public int X, Y;
            public Corner(int x, int y) { X = x; Y = y; }
        }

        /// <summary>The outer edge of the largest 4-connected part, along texel
        /// edges, with the part on the left (counter-clockwise).</summary>
        private static List<Corner> LargestOutline(bool[] solid, int w, int h)
        {
            var label = new int[w * h];
            int parts = 0, best = 0, bestCount = 0, bestStart = -1;
            var stack = new Stack<int>();
            for (int i = 0; i < solid.Length; i++)
            {
                if (!solid[i] || label[i] != 0) continue;
                parts++;
                int count = 0;
                label[i] = parts;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    count++;
                    int px = p % w, py = p / w;
                    if (px > 0) Visit(p - 1);
                    if (px < w - 1) Visit(p + 1);
                    if (py > 0) Visit(p - w);
                    if (py < h - 1) Visit(p + w);
                }
                if (count > bestCount) { bestCount = count; best = parts; bestStart = i; }
            }
            var corners = new List<Corner>();
            if (bestStart < 0) return corners;

            // Scan order puts the part's first texel lowest, then leftmost: its
            // bottom edge is outside, so the walk starts there going right.
            bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && label[y * w + x] == best;
            int sx = bestStart % w, sy = bestStart / w;
            int cx = sx, cy = sy, dir = 0; // 0 right, 1 up, 2 left, 3 down
            int[] stepX = { 1, 0, -1, 0 }, stepY = { 0, 1, 0, -1 };
            do
            {
                corners.Add(new Corner(cx, cy));
                cx += stepX[dir]; cy += stepY[dir];
                // The two texels ahead of the corner just reached, left and right
                // of the way on.
                int lx, ly, rx, ry;
                switch (dir)
                {
                    case 0: lx = cx; ly = cy; rx = cx; ry = cy - 1; break;
                    case 1: lx = cx - 1; ly = cy; rx = cx; ry = cy; break;
                    case 2: lx = cx - 1; ly = cy - 1; rx = cx - 1; ry = cy; break;
                    default: lx = cx; ly = cy - 1; rx = cx - 1; ry = cy - 1; break;
                }
                if (!In(lx, ly)) dir = (dir + 1) % 4;        // turn left
                else if (In(rx, ry)) dir = (dir + 3) % 4;    // turn right
            }
            while (cx != sx || cy != sy || dir != 0);
            return corners;

            void Visit(int q)
            {
                if (solid[q] && label[q] == 0) { label[q] = parts; stack.Push(q); }
            }
        }

        /// <summary>Douglas-Peucker over the closed walk, from its first corner
        /// and the corner farthest from it; corners only, so whole texels.</summary>
        private static List<Corner> Simplify(List<Corner> walk)
        {
            // Straight runs first: only the corners where the walk turns.
            var turns = new List<Corner>();
            int n = walk.Count;
            for (int i = 0; i < n; i++)
            {
                var a = walk[(i + n - 1) % n]; var b = walk[i]; var c = walk[(i + 1) % n];
                if ((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) != 0) turns.Add(b);
            }
            if (turns.Count < 4) return turns;
            int far = 0;
            long farD = -1;
            for (int i = 1; i < turns.Count; i++)
            {
                long dx = turns[i].X - turns[0].X, dy = turns[i].Y - turns[0].Y, d = dx * dx + dy * dy;
                if (d > farD) { farD = d; far = i; }
            }
            var keep = new bool[turns.Count];
            keep[0] = keep[far] = true;
            Reduce(turns, 0, far, keep);
            Reduce(turns, far, turns.Count, keep);
            var result = new List<Corner>();
            for (int i = 0; i < turns.Count; i++) if (keep[i]) result.Add(turns[i]);
            return result;
        }

        /// <summary>Keeps what the stretch from <paramref name="from"/> to
        /// <paramref name="to"/> (wrapping) needs within <see cref="Tolerance"/>.</summary>
        private static void Reduce(List<Corner> pts, int from, int to, bool[] keep)
        {
            var spans = new Stack<int[]>();
            spans.Push(new[] { from, to });
            int n = pts.Count;
            while (spans.Count > 0)
            {
                var s = spans.Pop();
                int a = s[0], b = s[1];
                if (b - a < 2) continue;
                var pa = pts[a % n]; var pb = pts[b % n];
                long ex = pb.X - pa.X, ey = pb.Y - pa.Y;
                double len = Math.Sqrt(ex * ex + ey * ey);
                int worst = -1;
                double worstD = -1;
                for (int i = a + 1; i < b; i++)
                {
                    var p = pts[i % n];
                    double d = len == 0
                        ? Math.Sqrt((double)(p.X - pa.X) * (p.X - pa.X) + (double)(p.Y - pa.Y) * (p.Y - pa.Y))
                        : Math.Abs(ex * (p.Y - pa.Y) - ey * (p.X - pa.X)) / len;
                    if (d > worstD) { worstD = d; worst = i; }
                }
                if (worstD > Tolerance)
                {
                    keep[worst % n] = true;
                    spans.Push(new[] { a, worst });
                    spans.Push(new[] { worst, b });
                }
            }
        }
    }
}

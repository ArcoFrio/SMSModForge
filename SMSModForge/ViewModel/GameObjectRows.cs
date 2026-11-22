using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// The objects of a place, or of a game's level, made what an undo step has -
/// where they are (1.10.0). A step keeps a place object by object
/// (<see cref="SplitPiece"/>), so two steps say which objects differ; this puts
/// those back and leaves every other row the row it was:
/// <list type="bullet">
/// <item>an object the step has unchanged keeps its row, moved among its
/// siblings if the step has it elsewhere;</item>
/// <item>an object the step has with other values is given them and its row
/// reads it again - the object and its row stay the ones on screen, and so
/// does its light (the preview's panel holds on to it);</item>
/// <item>an object only the step has gets a row, with everything under it;
/// one the step has not loses its row.</item>
/// </list>
/// Until this, anything but a move put back the whole place: its view made
/// again, every row and the preview with it - close to a second on a game's
/// level for one object added, renamed or given another picture.
/// <para/>
/// Nothing is changed until everything the step needs has been read, and the
/// caller holds the result to the step: where this is not sure of itself it
/// says so, and the place is put back whole as before.
/// </summary>
internal static class GameObjectRows
{
    /// <summary>What an object is, whatever it says: the object of the game's it was filled in from, the one
    /// it is a copy of, the forced one - or nothing in particular, one of the pack's own.</summary>
    private static string IdentityOf(GameObjectDef def)
        => (def.Role ?? "") + "|"
           + (!string.IsNullOrEmpty(def.BaselineName) ? "from:" + def.BaselineName
              : def.Bind ? "bound:" + def.Name
              : def.IsCopy ? "copy:" + def.CopyOf : "");

    private static bool SameObject(GameObjectDef a, GameObjectDef b) => IdentityOf(a) == IdentityOf(b);

    private sealed class Plan
    {
        public int To;                              // the step's part
        public GameObjectViewModel? Row;            // the row that shows it; null: a new one
        public bool Given;                          // the row's object is given the step's values
        public GameObjectDef? Read;                 // what was read for it: its values (given), or it and all under it (new)
        public List<Plan> Kids = new();
    }

    /// <summary>
    /// False, with nothing changed, when the rows are not the tree <paramref name="from"/> says, an object could
    /// not be read, or <paramref name="mayBeNew"/> says a new object is not one this can make a row for.
    /// <paramref name="made"/>: whether a row was made.
    /// </summary>
    public static bool ReadAgain(SplitPiece from, SplitPiece to,
                                 ObservableCollection<GameObjectViewModel> top, List<GameObjectDef> models,
                                 Func<GameObjectDef, GameObjectViewModel> rowFor,
                                 Func<GameObjectDef, bool> mayBeNew, out bool made)
    {
        made = false;
        var fromKids = from.ChildParts();
        var toKids = to.ChildParts();

        bool PlanFor(IList<GameObjectViewModel> rows, int[] f, int[] t, List<Plan> into)
        {
            if (rows.Count != f.Length) return false;       // not the tree the step before says
            var match = new int[t.Length];
            Array.Fill(match, -1);
            var used = new bool[f.Length];
            // The same object where it was, then anywhere among its siblings.
            for (int i = 0; i < t.Length && i < f.Length; i++)
                if (PackPieces.SaysTheSame(from.Parts[f[i]], to.Parts[t[i]])) { match[i] = i; used[i] = true; }
            for (int i = 0; i < t.Length; i++)
            {
                if (match[i] >= 0) continue;
                for (int j = 0; j < f.Length; j++)
                {
                    if (used[j] || !PackPieces.SaysTheSame(from.Parts[f[j]], to.Parts[t[i]])) continue;
                    match[i] = j; used[j] = true;
                    break;
                }
            }
            // As many left on each side: each is that object, saying something else. Any other number: the
            // step's are new and the rows left over go.
            var left = new Queue<int>(Enumerable.Range(0, f.Length).Where(j => !used[j]));
            bool changed = left.Count == match.Count(m => m < 0);
            for (int i = 0; i < t.Length; i++)
            {
                var plan = new Plan { To = t[i] };
                int j = match[i];
                if (j < 0 && changed) { j = left.Dequeue(); plan.Given = true; }
                if (j >= 0)
                {
                    plan.Row = rows[j];
                    if (!PlanFor(plan.Row.Children, fromKids[f[j]], toKids[t[i]], plan.Kids)) return false;
                }
                into.Add(plan);
            }
            return true;
        }

        var plans = new List<Plan>();
        if (!PlanFor(top, fromKids[0], toKids[0], plans)) return false;

        // Everything read before anything changes.
        bool anyNew = false;
        bool Read(List<Plan> list)
        {
            foreach (var plan in list)
            {
                if (plan.Row == null)
                {
                    plan.Read = (GameObjectDef)to.BuildNode(plan.To);
                    if (!mayBeNew(plan.Read)) return false;
                    anyNew = true;
                }
                else
                {
                    if (plan.Given)
                    {
                        plan.Read = (GameObjectDef)to.ReadNode(plan.To);
                        // Another object altogether where one was (one taken out and one put in, in a step):
                        // not that row's object with other values - what the row's object keeps of the game's
                        // would be the wrong object's. A new row, and the old one goes.
                        if (!SameObject(plan.Row.Model, plan.Read))
                        {
                            plan.Row = null;
                            plan.Given = false;
                            plan.Kids.Clear();
                            plan.Read = (GameObjectDef)to.BuildNode(plan.To);
                            if (!mayBeNew(plan.Read)) return false;
                            anyNew = true;
                            continue;
                        }
                    }
                    if (!Read(plan.Kids)) return false;
                }
            }
            return true;
        }
        try { if (!Read(plans)) return false; }
        catch (Newtonsoft.Json.JsonException) { return false; }

        void Apply(ObservableCollection<GameObjectViewModel> rows, List<GameObjectDef> defs, Func<GameObjectDef, GameObjectViewModel> row, List<Plan> list)
        {
            var want = new List<GameObjectViewModel>(list.Count);
            foreach (var plan in list)
            {
                if (plan.Row == null) plan.Row = row(plan.Read!);
                else if (plan.Given) plan.Row.TakeValues(plan.Read!);
                want.Add(plan.Row);
            }
            bool same = want.Count == rows.Count;
            for (int i = 0; same && i < want.Count; i++) same = ReferenceEquals(want[i], rows[i]);
            if (!same)
            {
                var keep = new HashSet<GameObjectViewModel>(want);
                for (int i = rows.Count - 1; i >= 0; i--)
                    if (!keep.Contains(rows[i])) rows.RemoveAt(i);
                for (int i = 0; i < want.Count; i++)
                {
                    int at = rows.IndexOf(want[i]);
                    if (at == i) continue;
                    if (at >= 0) rows.Move(at, i);
                    else rows.Insert(i, want[i]);
                }
                // The very list: the rows hold on to it (a copy pasted beside one goes into it).
                defs.Clear();
                foreach (var r in want) defs.Add(r.Model);
            }
            foreach (var plan in list)
                if (plan.Read == null || plan.Given) Apply(plan.Row!.Children, plan.Row.Model.Children, plan.Row.RowFor, plan.Kids);
        }
        Apply(top, models, rowFor, plans);
        made = anyNew;
        return true;
    }
}

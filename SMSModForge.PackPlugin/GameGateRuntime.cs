using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using GameCreator.Runtime.VisualScripting;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Conditions packs take out of the game's own scripts - a room's branch
    /// that plays a conversation, most often (<see cref="GameConditionEdits"/>).
    /// <para/>
    /// The list is rebuilt without them, from the game's own list as it was
    /// before any pack touched it: packs that take conditions out of the same
    /// list are applied together, and a pack loaded again in the same scene
    /// does not take out the condition that has since moved into a removed
    /// one's place. A removal whose position no longer holds a condition of the
    /// recorded type is refused and said, so a game update that reorders a list
    /// leaves every condition in it rather than taking out the wrong one.
    /// <para/>
    /// A branch with its conditions taken out still waits behind the branches
    /// before it: Game Creator runs the first branch of a list that runs. So
    /// each time the game goes through a list a pack changed, the log says
    /// which branch ran - the one answer to "I took them all out and it still
    /// did not play".
    /// </summary>
    internal static class GameGateRuntime
    {
        private const string Tag = "[SMSModForge.PackPlugin] Game conditions: ";

        private static readonly FieldInfo ConditionsField =
            typeof(ConditionList).GetField("m_Conditions", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Each list changed, and what it held before.</summary>
        private static readonly Dictionary<ConditionList, Condition[]> Originals =
            new Dictionary<ConditionList, Condition[]>();

        /// <summary>Put every list back. The scene's scripts are usually new
        /// after a load, and then this has nothing to do; when they are not,
        /// the packs installed now start from the game's lists again.</summary>
        public static void RestoreAll(ManualLogSource log)
        {
            foreach (var pair in Originals)
            {
                try { ConditionsField?.SetValue(pair.Key, pair.Value); }
                catch (System.Exception ex) { log?.LogWarning(Tag + "could not put a list back: " + ex.Message); }
            }
            Originals.Clear();
            foreach (var watch in Watches.Values) watch.Unhook();
            Watches.Clear();
        }

        private static readonly FieldInfo InstructionsField = FieldOf(typeof(Branch), "m_InstructionList");
        private static readonly FieldInfo BranchesField = FieldOf(typeof(BranchList), "m_Branches");

        private static readonly Dictionary<BranchList, Watch> Watches = new Dictionary<BranchList, Watch>();

        /// <summary>A branch list some pack changed a branch of, and what the
        /// game did the last time it went through it.</summary>
        private sealed class Watch
        {
            public BranchList List;
            public string Where;
            public ManualLogSource Log;
            private readonly Dictionary<int, Changed> _changed = new Dictionary<int, Changed>();
            private int _tried = -1;
            private System.Action<int> _onTry;
            private System.Action _onStart;
            private System.Action _onEnd;

            private sealed class Changed
            {
                public Branch Branch;
                public InstructionList Instructions;
                public string Packs;
                public int ConditionsLeft;
                public bool Ran;
                public System.Action OnRun;
            }

            public void Hook()
            {
                _onTry = i => _tried = i;
                _onStart = () =>
                {
                    _tried = -1;
                    foreach (var c in _changed.Values) c.Ran = false;
                };
                _onEnd = Report;
                List.EventRunBranch += _onTry;
                List.EventStartRunning += _onStart;
                List.EventEndRunning += _onEnd;
            }

            public void Add(int index, Branch branch, string packs, int conditionsLeft)
            {
                if (_changed.ContainsKey(index)) return;
                var changed = new Changed
                {
                    Branch = branch,
                    Packs = packs,
                    ConditionsLeft = conditionsLeft,
                    Instructions = InstructionsField?.GetValue(branch) as InstructionList,
                };
                changed.OnRun = () => changed.Ran = true;
                if (changed.Instructions != null) changed.Instructions.EventStartRunning += changed.OnRun;
                _changed[index] = changed;
            }

            public void Unhook()
            {
                if (_onTry != null) List.EventRunBranch -= _onTry;
                if (_onStart != null) List.EventStartRunning -= _onStart;
                if (_onEnd != null) List.EventEndRunning -= _onEnd;
                foreach (var c in _changed.Values)
                    if (c.Instructions != null) c.Instructions.EventStartRunning -= c.OnRun;
            }

            private void Report()
            {
                try
                {
                    foreach (var pair in _changed)
                    {
                        var c = pair.Value;
                        string mine = "branch " + pair.Key + Plays(c.Branch) + ", which " + c.Packs + " changed";
                        if (c.Ran)
                            Log?.LogInfo(Tag + Where + " ran its " + mine + ".");
                        else if (_tried >= 0 && _tried < pair.Key)
                            Log?.LogInfo(Tag + Where + " ran its branch " + _tried + Plays(BranchAt(_tried))
                                         + ", which comes before its " + mine + " - so that one was not reached.");
                        else if (c.Branch != null && !c.Branch.IsEnabled)
                            Log?.LogInfo(Tag + Where + " skipped its " + mine
                                         + ": the game has that branch switched off.");
                        else if (_tried == pair.Key && c.ConditionsLeft == 0)
                            // Nothing left to fail, so the branch passed; Game
                            // Creator does not start a list that is still running.
                            Log?.LogInfo(Tag + Where + " picked its " + mine
                                         + ", but its steps were still running from before, so they did not start again.");
                        else if (c.ConditionsLeft > 0)
                            Log?.LogInfo(Tag + Where + " did not run its " + mine + ": one of the "
                                         + c.ConditionsLeft + " condition(s) left in it did not pass.");
                        else
                            Log?.LogInfo(Tag + Where + " did not run its " + mine + ".");
                    }
                }
                catch (System.Exception ex)
                {
                    Log?.LogWarning(Tag + "could not say what " + Where + " ran: " + ex.Message);
                }
            }

            private Branch BranchAt(int index)
            {
                var branches = BranchesField?.GetValue(List) as IList;
                return branches != null && index >= 0 && index < branches.Count ? branches[index] as Branch : null;
            }

            /// <summary>" (plays X)" for a branch that plays a conversation.</summary>
            private static string Plays(Branch branch)
            {
                if (branch == null) return "";
                object instructions = InstructionsField?.GetValue(branch);
                string name = null;
                QuestPlaceRules.Walk(instructions, 0, new HashSet<object>(QuestPlaceRules.ByReference.Instance), o =>
                {
                    var dialogue = o as GameCreator.Runtime.Dialogue.Dialogue;
                    if (name == null && dialogue != null) name = dialogue.name;
                });
                return name == null ? "" : " (plays " + name + ")";
            }
        }

        private sealed class Planned
        {
            public JObject Gate;
            public readonly List<KeyValuePair<int, string>> Removed = new List<KeyValuePair<int, string>>();
            public readonly List<string> Packs = new List<string>();
        }

        public static void Apply(List<PackManifest> manifests, ManualLogSource log)
        {
            if (ConditionsField == null)
            {
                log?.LogError(Tag + "ConditionList.m_Conditions is not there in this game build, so no pack's "
                              + "condition removals can be applied.");
                return;
            }

            // Every pack's removals, by list.
            var plans = new Dictionary<string, Planned>(System.StringComparer.Ordinal);
            foreach (var m in manifests ?? new List<PackManifest>())
            {
                if (!(m?.Root?[GameConditionEdits.GatesKey] is JArray gates)) continue;
                foreach (var token in gates)
                {
                    if (!(token is JObject gate)) continue;
                    var removed = GameConditionEdits.RemovalsOf(gate);
                    if (removed.Count == 0) continue;
                    string key = GameConditionEdits.GateKey(gate);
                    if (!plans.TryGetValue(key, out var plan)) plans[key] = plan = new Planned { Gate = gate };
                    foreach (var r in removed)
                        if (!plan.Removed.Any(x => x.Key == r.Key)) plan.Removed.Add(r);
                    if (!plan.Packs.Contains(m.PackId)) plan.Packs.Add(m.PackId);
                }
            }

            foreach (var plan in plans.Values)
            {
                string by = (string)plan.Gate[GameConditionEdits.ByKey] ?? "";
                string script = (string)plan.Gate[GameConditionEdits.ScriptKey] ?? "";
                string where = by + " (" + script + ") at " + GameConditionEdits.PathText(plan.Gate[GameConditionEdits.AtKey] as JArray);
                string packs = string.Join(", ", plan.Packs.ToArray());

                string why;
                BranchList owner;
                int index;
                Branch branch;
                var list = Resolve(plan.Gate, out why, out owner, out index, out branch);
                if (list == null)
                {
                    log?.LogWarning(Tag + packs + " take(s) conditions out of " + where + ", which this scene does not have ("
                                    + why + "). Nothing is taken out of it.");
                    continue;
                }

                Condition[] original;
                if (!Originals.TryGetValue(list, out original))
                {
                    original = (Condition[])ConditionsField.GetValue(list) ?? new Condition[0];
                    Originals[list] = original;
                }

                var types = original.Select(c => c == null ? "" : c.GetType().Name).ToList();
                var refused = new List<KeyValuePair<int, string>>();
                var kept = GameConditionEdits.Kept(types, plan.Removed, refused);
                ConditionsField.SetValue(list, kept.Select(i => original[i]).ToArray());

                log?.LogInfo(Tag + packs + " took " + (original.Length - kept.Count) + " of " + original.Length
                             + " condition(s) out of " + where + ".");

                if (owner != null && index >= 0)
                {
                    try
                    {
                        Watch watch;
                        if (!Watches.TryGetValue(owner, out watch))
                        {
                            watch = new Watch { List = owner, Where = by + " (" + script + ")", Log = log };
                            watch.Hook();
                            Watches[owner] = watch;
                        }
                        watch.Add(index, branch, packs, kept.Count);
                    }
                    catch (System.Exception ex)
                    {
                        log?.LogWarning(Tag + "cannot follow what " + where + " runs: " + ex.Message);
                    }
                }
                foreach (var r in refused)
                    log?.LogWarning(Tag + packs + " take(s) out a " + r.Value + " at position " + r.Key + " of " + where
                                    + ", but the game has " + (r.Key >= 0 && r.Key < types.Count ? "a " + types[r.Key] : "nothing")
                                    + " there - the game was probably updated. It is left in.");
            }
        }

        /// <summary>The condition list at one path in one of the game's
        /// scripts, for anything else that has to read the same list
        /// (<see cref="QuestPlaceRules"/>).</summary>
        internal static ConditionList ListAt(string by, string script, JArray path, out string why)
        {
            BranchList owner;
            int index;
            Branch branch;
            return Resolve(by, script, path, out why, out owner, out index, out branch);
        }

        /// <summary>The condition list a gate entry names, followed down from
        /// the script field by field, or null with the reason.</summary>
        private static ConditionList Resolve(JObject gate, out string why, out BranchList owner, out int index,
                                             out Branch branch)
            => Resolve((string)gate[GameConditionEdits.ByKey], (string)gate[GameConditionEdits.ScriptKey],
                       gate[GameConditionEdits.AtKey] as JArray, out why, out owner, out index, out branch);

        private static ConditionList Resolve(string by, string script, JArray path, out string why,
                                             out BranchList owner, out int index, out Branch branch)
        {
            owner = null;
            index = -1;
            branch = null;
            var go = TransformExtensions.ResolveGameObject(by);
            if (go == null) { why = "no such object"; return null; }

            object at = go.GetComponents<Component>()
                          .FirstOrDefault(c => c != null && c.GetType().Name == script);
            if (at == null) { why = "no " + script + " on it"; return null; }

            if (path == null) { why = "no path"; return null; }
            // The last branch list the path goes through, and which of its
            // branches: the list the game picks the branch from.
            BranchList holder = null;
            foreach (var step in path)
            {
                if (at == null) break;
                if (step.Type == JTokenType.Integer)
                {
                    int position = (int)step;
                    var list = at as IList;
                    at = list != null && position >= 0 && position < list.Count ? list[position] : null;
                    var picked = at as Branch;
                    if (holder != null && picked != null)
                    {
                        owner = holder;
                        index = position;
                        branch = picked;
                    }
                    holder = null;
                }
                else
                {
                    var before = at;
                    var field = FieldOf(at.GetType(), (string)step);
                    at = field?.GetValue(at);
                    holder = before as BranchList;
                }
            }

            var found = at as ConditionList;
            why = found == null ? "the path leads nowhere in this build" : null;
            return found;
        }

        internal static FieldInfo FieldOf(System.Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                             | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }
    }
}

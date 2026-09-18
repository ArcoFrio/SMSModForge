using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
using GameCreator.Runtime.Quests;
using GameCreator.Runtime.VisualScripting;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The game's own start and reset places, once a pack has changed one.
    /// <para/>
    /// A place nobody has touched belongs to the game: its conversation or
    /// script gets there and the quest starts. As soon as a pack changes
    /// anything at it - a condition taken out, the room taken out, conditions
    /// of its own added - the place becomes that pack's RULE, and this asks it
    /// every frame: the moment everything left passes, the quest starts (or
    /// goes back to not started), whether or not the game itself ever gets
    /// there. "Take out everything and add Insert" then means what an author
    /// reads it to mean.
    /// <para/>
    /// The rule is written by the editor when the pack is saved
    /// (<c>PlaceRules</c>), and it names lists and lines rather than copying
    /// what is in them: the conditions asked here are the game's own objects,
    /// so a condition the pack took out is already gone from them, and a game
    /// update changes what is asked without changing the pack.
    /// <para/>
    /// The game's own step at such a place is kept honest too: when it runs, it
    /// only does what it would have done if the rule passes at that moment -
    /// otherwise it does nothing, and the rule starts the quest when it passes.
    /// <b>The step that runs is not the step in the scene</b>: Game Creator runs
    /// a line's lists from pooled copies, so a step is recognised by what it
    /// does and where it runs - its type and quest, and either the object
    /// running it or the line the conversation is on.
    /// </summary>
    internal static class QuestPlaceRules
    {
        private const string Tag = "[SMSModForge.PackPlugin] Quest places: ";

        /// <summary>One script that can bring a place about: the step's own, or
        /// one that plays its conversation. Any one of them is enough.</summary>
        private sealed class Scope
        {
            public string Where;
            public Args Args;
            public readonly List<ConditionList> Lists = new List<ConditionList>();
            public JArray Room;
            public JArray Fixed;
        }

        /// <summary>One line the conversation has to be able to be on.</summary>
        private sealed class Line
        {
            public Node Node;
            public Args Args;
        }

        private sealed class Rule
        {
            public PackContext Ctx;
            public string Quest;
            public string Step;
            public string Place;
            public JArray Yours;
            public readonly List<Scope> Any = new List<Scope>();
            public readonly List<Line> Lines = new List<Line>();

            /// <summary>A script's object, for the game's own step there.</summary>
            public GameObject Script;

            /// <summary>The conversation and line, for the game's own step there.</summary>
            public Dialogue Dialogue;
            public int Node;
        }

        internal sealed class ByReference : IEqualityComparer<object>
        {
            public static readonly ByReference Instance = new ByReference();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }

        private static readonly List<Rule> Rules = new List<Rule>();

        /// <summary>The line each conversation is on, while it is on one.</summary>
        private static readonly Dictionary<Dialogue, int> OnLine = new Dictionary<Dialogue, int>();

        /// <summary>Conversations already listened to - each one once, however
        /// many loads it lives through.</summary>
        private static readonly HashSet<Dialogue> Listened = new HashSet<Dialogue>();

        /// <summary>Forget the previous scene's places.</summary>
        public static void Reset()
        {
            Rules.Clear();
            OnLine.Clear();
        }

        private static void Listen(Dialogue dialogue)
        {
            if (!Listened.Add(dialogue)) return;
            dialogue.EventStartNext += id => OnLine[dialogue] = id;
            dialogue.EventFinishNext += id =>
            {
                int now;
                if (OnLine.TryGetValue(dialogue, out now) && now == id) OnLine.Remove(dialogue);
            };
        }

        // ── Reading the game ─────────────────────────────────────────────

        private static readonly FieldInfo ListConditionsField =
            typeof(ConditionList).GetField("m_Conditions", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo NodeConditionsField =
            typeof(Node).GetField("m_Conditions", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RunListField =
            typeof(RunConditionsList).GetField("m_Conditions", BindingFlags.NonPublic | BindingFlags.Instance);

        // ── Registration ─────────────────────────────────────────────────

        /// <summary>Said once if this game build keeps its conditions somewhere
        /// else: everything below reads them by name, and a silent null would
        /// read exactly like a place whose conditions all pass.</summary>
        private static bool Readable(ManualLogSource log)
        {
            var missing = new List<string>();
            if (ListConditionsField == null) missing.Add("ConditionList.m_Conditions");
            if (NodeConditionsField == null) missing.Add("Node.m_Conditions");
            if (RunListField == null) missing.Add("RunConditionsList.m_Conditions");
            if (missing.Count == 0) return true;
            log?.LogError(Tag + "this game build does not have " + string.Join(", ", missing.ToArray())
                          + ", so what the game checks at a place cannot be read. Places a pack has taken over are "
                          + "left to the game.");
            return false;
        }

        /// <summary>Find the places one pack's quest entries have changed, and
        /// take them over.</summary>
        public static void Register(PackManifest manifest, PackContext ctx, ManualLogSource log)
        {
            if (!(manifest?.Root["quests"] is JArray quests) || ctx == null) return;
            if (!Readable(log)) return;
            foreach (var token in quests)
            {
                if (!(token is JObject q)) continue;
                string quest = ((string)q["source"] ?? "").Trim();
                if (quest.Length == 0 || !(q[GameConditionEdits.SiteConditionsKey] is JArray places)) continue;

                foreach (var placeToken in places)
                {
                    if (!(placeToken is JObject place)) continue;
                    string step = GameConditionEdits.StepFor((string)place[GameConditionEdits.DoesKey]);
                    if (step == null) continue;

                    var conditions = place[GameConditionEdits.ConditionsKey] as JArray;
                    if (!(place[GameConditionEdits.RuleKey] is JObject written))
                    {
                        bool changes = (conditions != null && conditions.Count > 0)
                                       || ((place[GameConditionEdits.RoomsOutKey] as JArray)?.Count ?? 0) > 0;
                        if (changes)
                            log?.LogWarning(Tag + "'" + quest + "': " + ctx.PackId + " changes what "
                                            + (step == GameConditionEdits.ResetStep
                                                   ? "puts this quest back to not started"
                                                   : "starts this quest")
                                            + ", but its file doesn't hold that change in the form the game uses - it "
                                            + "was saved by an older ModForge. Save the pack in ModForge again and "
                                            + "re-export it. Until then, the quest "
                                            + (step == GameConditionEdits.ResetStep ? "resets" : "starts")
                                            + " the way the game has it.");
                        continue;
                    }

                    var rule = new Rule
                    {
                        Ctx = ctx, Quest = quest, Step = step,
                        Yours = conditions != null && conditions.Count > 0 ? conditions : null,
                        Place = Describe(place),
                    };
                    Build(rule, place, written, log);
                    Rules.Add(rule);
                    log?.LogInfo(Tag + ctx.PackId + ": '" + quest + "' " + (step == GameConditionEdits.ResetStep
                                     ? "goes back to not started"
                                     : "starts")
                                 + " at " + rule.Place + " when this pack's conditions for it pass"
                                 + (rule.Any.Count == 0 && rule.Lines.Count == 0 && rule.Yours == null
                                        ? " - which is always" : "") + ".");
                }
            }
        }

        private static string Describe(JObject place)
        {
            string dialogue = (string)place[GameConditionEdits.DialogueKey];
            if (!string.IsNullOrEmpty(dialogue))
                return "line " + (long?)place[GameConditionEdits.NodeKey] + " of " + dialogue;
            return (string)place[GameConditionEdits.ByKey] + " (" + (string)place[GameConditionEdits.ScriptKey] + ")";
        }

        private static void Build(Rule rule, JObject place, JObject written, ManualLogSource log)
        {
            foreach (var token in (written[GameConditionEdits.AnyKey] as JArray) ?? new JArray())
            {
                if (!(token is JObject scopeJson)) continue;
                string by = (string)scopeJson[GameConditionEdits.ByKey] ?? "";
                string script = (string)scopeJson[GameConditionEdits.ScriptKey] ?? "";
                var scope = new Scope
                {
                    Where = by + " (" + script + ")",
                    Room = scopeJson[GameConditionEdits.RoomKey] as JArray,
                    Fixed = scopeJson[GameConditionEdits.FixedKey] as JArray,
                };

                var go = TransformExtensions.ResolveGameObject(by);
                if (go == null)
                {
                    log?.LogWarning(Tag + rule.Ctx.PackId + " reads " + scope.Where + " for '" + rule.Quest
                                    + "', which this scene does not have. Nothing there is asked.");
                }
                else
                {
                    scope.Args = new Args(go, go);
                    if (string.IsNullOrEmpty((string)place[GameConditionEdits.DialogueKey])) rule.Script = go;
                }

                foreach (var path in (scopeJson[GameConditionEdits.ListsKey] as JArray) ?? new JArray())
                {
                    string why;
                    var list = GameGateRuntime.ListAt(by, script, path as JArray, out why);
                    if (list == null)
                    {
                        log?.LogWarning(Tag + rule.Ctx.PackId + ": the conditions at " + scope.Where + " for '"
                                        + rule.Quest + "' are not in this build (" + why + "), so they are not "
                                        + "asked.");
                        continue;
                    }
                    scope.Lists.Add(list);
                }
                rule.Any.Add(scope);
            }

            foreach (var token in (written[GameConditionEdits.LinesKey] as JArray) ?? new JArray())
            {
                if (!(token is JObject lineJson)) continue;
                string dialogue = (string)lineJson[GameConditionEdits.DialogueKey] ?? "";
                var nodeToken = lineJson[GameConditionEdits.NodeKey];
                long id = nodeToken != null && nodeToken.Type == JTokenType.Integer ? (long)nodeToken : 0;

                var go = TransformExtensions.ResolveGameObject(dialogue);
                var component = go != null ? go.GetComponent<Dialogue>() : null;
                var content = component != null && component.Story != null ? component.Story.Content : null;
                Node node = null;
                if (content != null)
                {
                    try { node = content.Get(unchecked((int)id)); }
                    catch (System.Exception) { node = null; }
                }
                if (node == null)
                {
                    log?.LogWarning(Tag + rule.Ctx.PackId + ": line " + id + " of " + dialogue + " is not in this "
                                    + "build, so what it checks is not asked for '" + rule.Quest + "'.");
                    continue;
                }

                rule.Lines.Add(new Line { Node = node, Args = new Args(component.gameObject) });
                if (id == (long?)place[GameConditionEdits.NodeKey])
                {
                    rule.Dialogue = component;
                    rule.Node = unchecked((int)id);
                    Listen(component);
                }
            }
        }

        // ── Asking ───────────────────────────────────────────────────────

        private static bool Passes(Rule rule, ManualLogSource log)
        {
            // The pack's own first: they are the cheap ones, and on a rule with
            // a key press in it they are false on all but one frame.
            if (rule.Yours != null && !ConditionEvaluator.All(rule.Yours, rule.Ctx.Vars, log, rule.Ctx.PackId))
                return false;

            // The script that would play the conversation, so a line's own
            // conditions are asked with the same Self the game would have given
            // them.
            Args from = null;
            if (rule.Any.Count > 0)
            {
                bool any = false;
                foreach (var scope in rule.Any)
                    if (ScopePasses(scope, rule, log)) { any = true; from = scope.Args; break; }
                if (!any) return false;
            }

            foreach (var line in rule.Lines)
                if (!LinePasses(line, from ?? line.Args, rule)) return false;
            return true;
        }

        private static bool ScopePasses(Scope scope, Rule rule, ManualLogSource log)
        {
            if (scope.Room != null && !ConditionEvaluator.All(scope.Room, rule.Ctx.Vars, log, rule.Ctx.PackId))
                return false;
            if (scope.Fixed != null && !ConditionEvaluator.All(scope.Fixed, rule.Ctx.Vars, log, rule.Ctx.PackId))
                return false;
            for (int i = 0; i < scope.Lists.Count; i++)
                if (!ListPasses(scope.Lists[i], scope.Args, rule, scope.Where + "/" + i)) return false;
            return true;
        }

        /// <summary>One of the game's condition lists, asked as the game asks
        /// it - except for a chance, which is rolled once a day rather than
        /// every frame (see <see cref="Chance"/>).</summary>
        private static bool ListPasses(ConditionList list, Args args, Rule rule, string where)
        {
            var conditions = ListConditionsField?.GetValue(list) as Condition[];
            if (conditions == null) return true;
            for (int i = 0; i < conditions.Length; i++)
            {
                var condition = conditions[i];
                if (condition == null) continue;
                if (condition.GetType().Name == "ConditionChance")
                {
                    if (!Chance(condition, args, rule, where + "/" + i)) return false;
                    continue;
                }
                bool passes;
                try { passes = condition.Check(args); }
                catch (System.Exception ex)
                {
                    rule.Ctx.Log?.LogWarning(Tag + "the game's condition at " + where + " threw: " + ex.Message
                                             + ". It is taken as not passing.");
                    return false;
                }
                if (!passes) return false;
            }
            return true;
        }

        private static readonly FieldInfo ThresholdField = AccessTools.Field(
            AccessTools.TypeByName("GameCreator.Runtime.VisualScripting.TwoCateCode.ConditionChance"), "m_Threshold");

        /// <summary>
        /// A chance of the game's, rolled once per in-game day for this pack
        /// and this place rather than on every frame. The game rolls it each
        /// time it reaches the script; a rule is asked continuously, and a 25%
        /// chance asked sixty times a second is not a chance at all.
        /// </summary>
        private static bool Chance(Condition condition, Args args, Rule rule, string where)
        {
            double threshold = 1d;
            try
            {
                var property = ThresholdField?.GetValue(condition);
                var get = property?.GetType().GetMethod("Get", new[] { typeof(Args) });
                if (get != null) threshold = System.Convert.ToDouble(get.Invoke(property, new object[] { args }));
            }
            catch (System.Exception ex)
            {
                rule.Ctx.Log?.LogWarning(Tag + "could not read the chance at " + where + " (" + ex.Message
                                         + "), so it is taken as always passing.");
                return true;
            }
            return ConditionEvaluator.DailyRollPasses(rule.Ctx.PackId, rule.Quest + "|" + where, (float)threshold,
                                                      rule.Ctx.Vars);
        }

        /// <summary>One line's conditions: the pack's version when it has one,
        /// and otherwise the game's own, read out of the list rather than run
        /// through Game Creator's pooled runner.</summary>
        private static bool LinePasses(Line line, Args args, Rule rule)
        {
            bool pack;
            if (PackNodeConditions.TryEvaluate(line.Node, out pack)) return pack;

            var run = NodeConditionsField?.GetValue(line.Node) as RunConditionsList;
            var list = run == null ? null : RunListField?.GetValue(run) as ConditionList;
            return list == null || ListPasses(list, args ?? line.Args, rule, "a line");
        }

        // ── Every frame ──────────────────────────────────────────────────

        /// <summary>
        /// The places this pack has taken over, asked in turn: the first time a
        /// rule passes, its quest starts or goes back to not started. A quest
        /// does not start while anything is putting it back.
        /// </summary>
        public static void Tick(PackContext ctx, ManualLogSource log)
        {
            if (Rules.Count == 0) return;
            var journal = QuestRuntime.Journal;
            if (journal == null) return;

            foreach (var rule in Rules)
            {
                if (!ReferenceEquals(rule.Ctx, ctx)) continue;
                var quest = QuestRegistry.FindVanilla(rule.Quest);
                if (quest == null) continue;

                bool reset = rule.Step == GameConditionEdits.ResetStep;
                bool inactive = journal.IsQuestInactive(quest);
                if (reset ? inactive : !inactive) continue;
                if (!reset && PutBackIsDue(ctx, rule.Quest, log)) continue;
                if (!Passes(rule, log)) continue;

                if (reset) journal.DeactivateQuest(quest);
                else journal.ActivateQuest(quest);
                log?.LogInfo(Tag + "'" + rule.Quest + "' " + (reset ? "put back to not started" : "started")
                             + " - everything " + ctx.PackId + " asks at " + rule.Place + " passed.");
            }
        }

        /// <summary>Whether anything of this pack's would put the quest back
        /// right now - its Resets when list, or one of its reset places. A
        /// quest that would be reset on the same frame must not start.</summary>
        private static bool PutBackIsDue(PackContext ctx, string quest, ManualLogSource log)
        {
            if (ctx.Quests != null && ctx.Quests.ResetsPass(ctx, quest, log)) return true;
            foreach (var rule in Rules)
                if (ReferenceEquals(rule.Ctx, ctx) && rule.Quest == quest
                    && rule.Step == GameConditionEdits.ResetStep && Passes(rule, log))
                    return true;
            return false;
        }

        // ── The game's own step ──────────────────────────────────────────

        public static void Install(Harmony harmony, ManualLogSource log)
        {
            foreach (var type in new[] { typeof(InstructionQuestsActivate), typeof(InstructionQuestsDeactivate) })
            {
                try
                {
                    var run = AccessTools.Method(type, "Run", new[] { typeof(Args) });
                    if (run == null)
                    {
                        log?.LogError(Tag + type.Name + ".Run(Args) is not there in this game build, so a pack "
                                      + "cannot take over that step.");
                        continue;
                    }
                    harmony.Patch(run, prefix: new HarmonyMethod(typeof(QuestPlaceRules), nameof(Prefix)));
                }
                catch (System.Exception ex)
                {
                    log?.LogError(Tag + "could not patch " + type.Name + " - packs cannot take over it. " + ex.Message);
                }
            }
        }

        private static bool Prefix(Instruction __instance, Args args, ref System.Threading.Tasks.Task __result)
        {
            if (__instance == null || Rules.Count == 0) return true;
            string step = __instance.GetType().Name;
            string quest = null;

            foreach (var rule in Rules)
            {
                if (rule.Step != step) continue;
                bool here;
                if (rule.Script != null)
                    here = args != null && (args.Target == rule.Script || args.Self == rule.Script);
                else
                {
                    int line;
                    here = rule.Dialogue != null && OnLine.TryGetValue(rule.Dialogue, out line) && line == rule.Node;
                }
                if (!here) continue;

                // Asked only once a place matches: finding the quest walks the
                // instruction's fields.
                if (quest == null) quest = QuestNamed(__instance) ?? "";
                if (quest != rule.Quest) continue;

                bool passes;
                try { passes = Passes(rule, rule.Ctx.Log); }
                catch (System.Exception ex)
                {
                    rule.Ctx.Log?.LogError(Tag + "what " + rule.Ctx.PackId + " asks at " + rule.Place + " threw: "
                                           + ex.Message + ". The step runs as the game has it.");
                    continue;
                }
                if (passes) continue;   // the game's step does what it always did

                __result = System.Threading.Tasks.Task.CompletedTask;
                return false;
            }
            return true;
        }

        /// <summary>The quest an instruction names - the first Quest asset in
        /// its own fields - or null.</summary>
        private static string QuestNamed(Instruction instruction)
        {
            string named = null;
            var seen = new HashSet<object>(ByReference.Instance);
            Walk(instruction, 0, seen, o =>
            {
                if (named == null && o is Quest asset && asset != null) named = asset.name;
            });
            return named;
        }

        /// <summary>
        /// Every object reachable from <paramref name="o"/> through fields and
        /// lists, stopping at Unity objects other than the root: a script names
        /// a scene object or an asset, and wandering into one is how a search
        /// reaches the whole scene.
        /// </summary>
        internal static void Walk(object o, int depth, HashSet<object> seen, System.Action<object> visit)
        {
            if (o == null || depth > 32) return;
            var type = o.GetType();
            if (type.IsPrimitive || type.IsEnum || o is string || o is decimal) return;
            if (o is Object unity)
            {
                if (unity == null) return;   // destroyed
                visit(o);
                if (depth > 0) return;
            }
            if (type.IsValueType) return;
            if (!seen.Add(o)) return;
            if (depth > 0 || !(o is Object)) visit(o);

            if (o is IList list)
            {
                foreach (var item in list) Walk(item, depth + 1, seen, visit);
                return;
            }
            if (o is IEnumerable) return;

            for (var t = type; t != null && t != typeof(object) && t != typeof(Object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                                  | BindingFlags.DeclaredOnly))
                {
                    var ft = field.FieldType;
                    if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string)) continue;
                    object value;
                    try { value = field.GetValue(o); }
                    catch (System.Exception) { continue; }
                    Walk(value, depth + 1, seen, visit);
                }
            }
        }
    }
}

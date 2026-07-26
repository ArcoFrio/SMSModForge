using Newtonsoft.Json.Linq;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Executes a place's authored <c>onEnter</c> / <c>onExit</c> action
    /// groups on the level's activation edges. Each group is a
    /// conditions-gated action list (the same vocabulary dialogue nodes and
    /// integration rules use): when the level flips inactive→active, every
    /// <c>onEnter</c> group whose conditions pass runs its actions exactly
    /// once; active→inactive does the same for <c>onExit</c>. Re-entering the
    /// level fires the enter groups again — one-time effects are gated by the
    /// author with a variable condition.
    /// <para/>
    /// Ticked per pack from <see cref="Plugin.Update"/>; edge state lives on
    /// the <see cref="PlaceRegistry.Entry"/> itself, which is rebuilt fresh
    /// every scene load.
    /// </summary>
    public static class LevelHooksRuntime
    {
        public static void Tick(PackContext ctx)
        {
            foreach (var place in PlaceRegistry.AllPackPlaces())
            {
                if (place.PackId != ctx.PackId) continue;
                if (place.Level == null) continue;
                if (place.OnEnterHooks == null && place.OnExitHooks == null) continue;

                bool now = place.Level.activeSelf;
                if (now == place.HooksWasActive) continue;
                place.HooksWasActive = now;

                var hooks = now ? place.OnEnterHooks : place.OnExitHooks;
                if (hooks == null) continue;

                foreach (var h in hooks)
                {
                    if (!(h is JObject hook)) continue;
                    if (!ConditionEvaluator.All(hook["conditions"] as JArray, ctx.Vars, ctx.Log, ctx.PackId))
                        continue;
                    try
                    {
                        ctx.Log?.LogInfo("[SMSModForge.PackPlugin] Level " + (now ? "enter" : "exit") +
                                         " hook fired on '" + place.Level.name + "' (" + ctx.PackId + ").");
                        ActionRuntime.ExecuteList(hook["actions"] as JArray, ctx);
                    }
                    catch (System.Exception ex)
                    {
                        ctx.Log?.LogError("[SMSModForge.PackPlugin] Level hook on '" +
                                          place.Level.name + "' threw: " + ex.Message);
                    }
                }
            }
        }
    }
}

using GameCreator.Runtime.Common;
using GameCreator.Runtime.VisualScripting;
using Newtonsoft.Json.Linq;
using System;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Custom <see cref="Condition"/> subclass that delegates to the
    /// pack's <see cref="ConditionEvaluator"/>. Used inside
    /// <see cref="GameCreator.Runtime.Dialogue.Node.m_Conditions"/> so
    /// choice-node "HideUnavailable" gating routes through pack state.
    /// <para/>
    /// The carrier instance holds the original JSON condition object so
    /// the evaluator can re-evaluate it with current variable values at
    /// every check.
    /// </summary>
    [Serializable]
    public class PackCondition : Condition
    {
        // Not serialized — we keep the JObject in memory since the
        // dialogue is rebuilt on every CoreGameScene entry anyway. GC2's
        // serializer won't roundtrip a JObject, but it doesn't need to:
        // pack dialogues never survive a scene transition in serialized
        // form, they're freshly constructed on each CoreGameScene load.
        [NonSerialized] private JObject _condition;
        [NonSerialized] private PackContext _ctx;

        public void Bind(JObject condition, PackContext ctx)
        {
            _condition = condition;
            _ctx = ctx;
        }

        protected override bool Run(Args args)
        {
            if (_condition == null || _ctx == null) return true;
            return ConditionEvaluator.Evaluate(_condition, _ctx.Vars, _ctx.Log, _ctx.PackId);
        }
    }
}

using System.Reflection;
using BepInEx.Logging;
using GameCreator.Runtime.Dialogue;
using Newtonsoft.Json.Linq;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Writes one authored field onto a Game Creator node.
    /// <para/>
    /// Shared by the two things that need it, and for the usual reason: the
    /// builder writes these onto a node it has just made, the injector writes
    /// them onto one the game already had, and a second copy of "how do you set
    /// a jump" would drift from this one the first time Game Creator renamed
    /// something.
    /// <para/>
    /// Every setter says whether it worked. A field that could not be written
    /// is a change the author made and the game will not do, which is worth
    /// reporting rather than assuming.
    /// </summary>
    internal static class DialogueNodeWriter
    {
        private static readonly FieldInfo _fldJump
            = typeof(Node).GetField("m_Jump", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldActing
            = typeof(Node).GetField("m_Acting", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldActingActor
            = typeof(GameCreator.Runtime.Dialogue.Acting).GetField(
                "m_Actor", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _fldNodeType
            = typeof(Node).GetField("m_NodeType", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Who a line is spoken by, as the pack's own actor.
        /// <para/>
        /// The bust is the pack's to route as the line plays, but the NAME
        /// beside it is Game Creator's: it draws that from the actor on the
        /// node. Set the bust and not this and a line shows the right character
        /// with nobody's name above them.
        /// </summary>
        public static bool SetActor(Node node, string actorKey, PackContext ctx)
        {
            if (node == null || string.IsNullOrEmpty(actorKey)) return false;
            if (_fldActing == null || _fldActingActor == null || ctx?.ActorFactory == null)
                return false;

            var entry = ctx.Actors == null ? null : ctx.Actors.GetOrNull(actorKey);
            string shown = entry == null ? null : entry.DisplayName;
            if (string.IsNullOrEmpty(shown)) shown = actorKey;

            var actor = ctx.ActorFactory.GetOrCreate(actorKey, shown);
            if (actor == null) return false;

            object acting = _fldActing.GetValue(node);
            if (acting == null) return false;

            _fldActingActor.SetValue(acting, actor);

            // Written back, so this holds whether Acting is a class or a
            // struct - a struct would otherwise have been changed on a copy.
            _fldActing.SetValue(node, acting);
            return true;
        }

        /// <summary>
        /// Where a line takes its presentation from — the skin, or itself.
        /// <para/>
        /// Every line in this game but one says FromSkin, which is how the
        /// typewriter speed, the fonts and the rest come from the conversation
        /// rather than from each line arguing for its own. A line built fresh
        /// does not default to that, so one added to a vanilla conversation
        /// read at a different speed to the lines around it.
        /// </summary>
        public static bool SetOptions(Node node, string options, ManualLogSource log)
        {
            if (node == null || _fldNodeType == null || string.IsNullOrEmpty(options)) return false;

            object type = _fldNodeType.GetValue(node);
            if (type == null) return false;

            var field = type.GetType().GetField(
                "m_Options", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return false;

            try
            {
                field.SetValue(type, System.Enum.Parse(field.FieldType, options));
                _fldNodeType.SetValue(node, type);
                return true;
            }
            catch (System.ArgumentException)
            {
                log?.LogWarning("[SMSModForge.PackPlugin] a line's options cannot be '"
                                + options + "' - left as they were");
                return false;
            }
        }

        /// <summary>
        /// What happens when the line finishes: carry on, end the conversation,
        /// or resume from a tagged line.
        /// <para/>
        /// Game Creator's NodeJump is a struct built by static factories, and
        /// which factories exist has moved between versions - hence the probing,
        /// and hence saying so out loud when none matches. A jump that quietly
        /// behaves like Continue is the bug this shape exists to avoid.
        /// </summary>
        public static bool SetJump(Node node, JObject jump, ManualLogSource log)
        {
            if (node == null || jump == null || _fldJump == null) return false;

            string mode = (string)jump["mode"] ?? "Continue";
            var jumpType = _fldJump.FieldType;

            MethodInfo make;
            switch (mode)
            {
                case "Exit":
                    make = jumpType.GetMethod("Exit", BindingFlags.Public | BindingFlags.Static);
                    break;
                case "Jump":
                    make = jumpType.GetMethod("To", BindingFlags.Public | BindingFlags.Static)
                        ?? jumpType.GetMethod("Jump", BindingFlags.Public | BindingFlags.Static)
                        ?? jumpType.GetMethod("JumpTo", BindingFlags.Public | BindingFlags.Static);
                    break;
                default:
                    make = jumpType.GetMethod("Continue", BindingFlags.Public | BindingFlags.Static);
                    break;
            }

            try
            {
                if (mode == "Jump" && make != null && make.GetParameters().Length == 1)
                {
                    var idStringType = make.GetParameters()[0].ParameterType;
                    var ctor = idStringType.GetConstructor(new[] { typeof(string) });
                    if (ctor == null)
                    {
                        log?.LogWarning("[SMSModForge.PackPlugin] Jump: IdString(string) ctor "
                                        + "not found - tag jump not applied.");
                        return false;
                    }

                    _fldJump.SetValue(node, make.Invoke(
                        null, new[] { ctor.Invoke(new object[] { (string)jump["targetTag"] ?? "" }) }));
                    return true;
                }

                if (make != null && make.GetParameters().Length == 0)
                {
                    _fldJump.SetValue(node, make.Invoke(null, null));
                    return true;
                }

                log?.LogWarning("[SMSModForge.PackPlugin] Jump: no matching NodeJump factory for "
                                + "mode '" + mode + "' - node keeps default Continue flow.");
                return false;
            }
            catch (System.Exception ex)
            {
                log?.LogWarning("[SMSModForge.PackPlugin] Jump: could not set mode '" + mode
                                + "': " + ex.Message);
                return false;
            }
        }
    }
}

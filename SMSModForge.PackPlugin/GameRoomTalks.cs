using GameCreator.Runtime.Dialogue;
using GameCreator.Runtime.Dialogue.UnityUI;
using GameCreator.Runtime.VisualScripting;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The game's own arrival scripts. Each room under <c>8_Room_Talk</c> has a
    /// Trigger that runs as the room is switched on - with the level at the end
    /// of a journey, or by the Talk button switching the stored one back on -
    /// and decides from its Conditions whether one of the game's conversations
    /// plays there now: deciding, leading in (a fade, a bust, a wait), the
    /// conversation, its tail. While one runs, the game is about to speak or
    /// speaking, and a pack dialogue started then lands on top of it.
    /// <para/>
    /// Read from Game Creator itself (<c>Trigger.IsRunning</c>), not from
    /// timings, because the timings were what failed: in 1.8E's gas station on
    /// a car-wash day the journey script cleared Lock-Game 0.8 s into the
    /// room's 2 s lead-in, the fixed hold after arrival ran out before the
    /// conversation came, and a pack dialogue started under it (measured with
    /// the bridge's <c>timeline</c>; the in-game suite's J5).
    /// <para/>
    /// It always ends by itself: every one of the 93 room talks in 1.8E starts
    /// on enable and waits only fixed times, its own conversations and the
    /// scripts it runs - none waits for a condition or loops
    /// (GAME_UPDATE_CHECKS.md says how to look again).
    /// </summary>
    internal static class GameRoomTalks
    {
        private static Transform _root;
        private static Trigger[] _triggers = new Trigger[0];
        private static int _childCount = -1;
        private static int _polledFrame = -1;
        private static bool _running;
        private static bool _spoke;

        /// <summary>One of the rooms' scripts is running.</summary>
        public static bool Running
        {
            get { Poll(); return _running; }
        }

        /// <summary>
        /// Running, and none of the game's conversations has been on screen yet
        /// in this run: the lead-in. A dialogue queued behind the game's would
        /// land before it there rather than after.
        /// </summary>
        public static bool BeforeItsConversation
        {
            get { Poll(); return _running && !_spoke; }
        }

        /// <summary>Once a frame, whoever asks first; frame-guarded like
        /// <see cref="DialogueDispatcher"/>'s level poll.</summary>
        private static void Poll()
        {
            if (_polledFrame == Time.frameCount) return;
            _polledFrame = Time.frameCount;

            if (_root == null)
            {
                var go = GameObject.Find("8_Room_Talk");
                _root = go != null ? go.transform : null;
                _childCount = -1;
            }
            if (_root == null) { _running = false; return; }
            // A pack's own room talk is added when its places are built.
            if (_root.childCount != _childCount) Rescan();

            bool running = false;
            foreach (var t in _triggers)
                if (t != null && t.gameObject.activeInHierarchy && t.IsRunning) { running = true; break; }
            if (running && !_running) _spoke = false;
            _running = running;
            if (running && GameConversationOnScreen()) _spoke = true;
        }

        private static void Rescan()
        {
            _childCount = _root.childCount;
            _triggers = new Trigger[_childCount];
            for (int i = 0; i < _childCount; i++)
                _triggers[i] = _root.GetChild(i).GetComponent<Trigger>();
        }

        /// <summary>A conversation is up that is not a pack's. Game Creator's
        /// Stop clears <see cref="Dialogue.Current"/> whoever it belongs to, so
        /// an open box with nobody current is counted as the game's.</summary>
        private static bool GameConversationOnScreen()
            => (Dialogue.Current != null || DialogueUI.IsOpen) && !DialogueDispatcher.IsPackDialogueOnScreen();
    }
}

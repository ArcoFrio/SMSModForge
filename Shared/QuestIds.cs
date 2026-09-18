using System;
using System.Security.Cryptography;
using System.Text;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The ids a pack quest and its tasks are known by in the game, derived from
    /// the pack's own keys.
    /// <para/>
    /// <b>Why derived, and never generated.</b> The game's journal saves a
    /// quest's state under the quest's GUID and a task's state under its
    /// numeric id. A pack quest is built again every time the game starts, so an
    /// id made up at build time would be a new id every session - and every
    /// player's progress would be quietly orphaned on the next load. Deriving
    /// both from the pack id and the keys gives the same answer on every run, on
    /// every machine, for as long as the keys stay the same.
    /// <para/>
    /// The flip side, and it is real: <b>renaming a quest's or a task's key in a
    /// pack players already have changes its id</b>, and their saved progress
    /// on it no longer matches. The editor has to say so before it lets a
    /// shipped key change.
    /// <para/>
    /// Compiled into both projects from one file, so the editor and the runtime
    /// cannot disagree about an id.
    /// </summary>
    public static class QuestIds
    {
        /// <summary>
        /// What the game's quest trees use to mean "no node" - a root task's
        /// parent. A task can never be given this id, or it would read as having
        /// no identity at all.
        /// </summary>
        public const int NoNode = -1;

        /// <summary>
        /// The GUID a pack quest is saved under.
        /// <para/>
        /// A name-based UUID (version 5, SHA-1), so it is a well-formed GUID the
        /// game's own parser accepts, and nothing about it depends on time, a
        /// random number, or the machine.
        /// </summary>
        public static string QuestGuid(string packId, string questKey)
        {
            byte[] hash = Sha1("smsmodforge/quest/" + (packId ?? "") + "/" + (questKey ?? ""));

            var bytes = new byte[16];
            Array.Copy(hash, bytes, 16);
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);   // version 5
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);   // RFC 4122 variant

            var hex = new StringBuilder(36);
            for (int i = 0; i < 16; i++)
            {
                if (i == 4 || i == 6 || i == 8 || i == 10) hex.Append('-');
                hex.Append(bytes[i].ToString("x2"));
            }
            return hex.ToString();
        }

        /// <summary>
        /// The id a pack task is saved under.
        /// <para/>
        /// Scoped by quest, so the same task key in two quests is two tasks. Not
        /// scoped by PARENT: the game looks tasks up by id across every quest it
        /// has, so a task key only has to be unique within its quest, not merely
        /// among its siblings.
        /// <para/>
        /// Never <see cref="NoNode"/>, and never 0 - zero is the default an
        /// uninitialised id reads as, and a task that looked unset would be the
        /// hardest kind of bug to see.
        /// </summary>
        public static int TaskId(string packId, string questKey, string taskKey)
        {
            return FromHash(Sha1("smsmodforge/task/" + (packId ?? "") + "/" + (questKey ?? "") + "/"
                                 + (taskKey ?? "")));
        }

        private static int FromHash(byte[] hash)
        {
            // Walk forward through the hash until a usable value turns up. Two
            // reserved values out of four billion means this almost never moves
            // past the first four bytes, and it stays deterministic when it does.
            for (int offset = 0; offset + 4 <= hash.Length; offset++)
            {
                // Assembled by hand rather than with BitConverter, whose byte
                // order is the machine's: an id has to read the same wherever
                // it is computed.
                int id = hash[offset]
                         | (hash[offset + 1] << 8)
                         | (hash[offset + 2] << 16)
                         | (hash[offset + 3] << 24);
                if (id != NoNode && id != 0) return id;
            }
            return 1;
        }

        /// <summary>
        /// The id of a task a pack adds to one of the game's own quests.
        /// <para/>
        /// Scoped by the GAME's quest name rather than by the pack entry that
        /// extends it: an entry's key is the editor's handle and can be renamed
        /// freely, and a player's progress on the task must not go with it. A
        /// different namespace from <see cref="TaskId"/>, so a pack quest that
        /// happens to share a name with one of the game's is a different set of
        /// ids.
        /// </summary>
        public static int AddedTaskId(string packId, string gameQuest, string taskKey)
            => FromHash(Sha1("smsmodforge/added-task/" + (packId ?? "") + "/" + (gameQuest ?? "") + "/"
                             + (taskKey ?? "")));

        private static byte[] Sha1(string text)
        {
            using (var sha = SHA1.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        }
    }
}

using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Repaints textures on the game's OWN busts, one slot at a time.
    /// <para/>
    /// The other half of what a pack can do to a character it did not create.
    /// <see cref="BustFactory"/> builds whole new busts; this changes the ones
    /// already standing in the scene — Anna's mouth, Kate's blink — without
    /// the pack having to supply the rest of the art.
    /// <para/>
    /// The rule the whole feature turns on: a slot the manifest does not
    /// mention is not touched. Not blanked, not reloaded, not copied — left
    /// exactly as the game drew it. That is why the manifest carries a LIST of
    /// slots rather than a field per slot: absent has to be distinguishable
    /// from empty, and a record with eleven fields in it cannot say "I have
    /// nothing to say about ten of these".
    /// <para/>
    /// Runs after every pack's own busts are built, because a pack can add an
    /// outfit to one of the game's characters and that outfit is built by
    /// <see cref="BustFactory"/> like any other.
    /// </summary>
    internal static class VanillaBustOverrides
    {
        public static void ApplyAll(PackManifest pack, Transform bustManager, ManualLogSource logger)
        {
            var characters = pack.Characters;
            if (characters == null || bustManager == null) return;

            int slots = 0, busts = 0;
            foreach (var ch in characters)
            {
                var charObj = (JObject)ch;

                // Only a character whose busts belong to the game. A pack's own
                // outfit describes every texture it has; there is nothing here
                // for it to override.
                if ((string)charObj["bustSource"] != "Vanilla") continue;

                var outfits = charObj["outfits"] as JArray;
                if (outfits == null) continue;

                foreach (var o in outfits)
                {
                    var outfit = (JObject)o;
                    var overrides = outfit["spriteOverrides"] as JArray;
                    if (overrides == null || overrides.Count == 0) continue;

                    // A bust the pack ADDED to this character is built by
                    // BustFactory from its own fields; overriding it would be
                    // overriding the pack's own art with the pack's own art.
                    if ((bool?)outfit["packArt"] ?? false) continue;

                    string goName = (string)outfit["gameObjectName"] ?? (string)outfit["key"];
                    if (string.IsNullOrEmpty(goName)) continue;

                    var bust = bustManager.FindChildIgnoreCase(goName);
                    if (bust == null)
                    {
                        logger?.LogWarning("[SMSModForge.PackPlugin] " + pack.PackId + " replaces textures on '"
                                           + goName + "', which is not under 2_Bust_Manager.");
                        continue;
                    }

                    int done = 0;
                    foreach (var entry in overrides)
                    {
                        try
                        {
                            if (ApplyOne(pack, bust, (JObject)entry, logger)) done++;
                        }
                        catch (System.Exception ex)
                        {
                            logger?.LogError("[SMSModForge.PackPlugin] Override failed on " + goName
                                             + " in " + pack.PackId + ": " + ex.Message);
                        }
                    }
                    if (done > 0) { busts++; slots += done; }
                }
            }

            if (slots > 0)
                logger?.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId + "' replaced "
                                + slots + " texture(s) across " + busts + " of the game's bust(s).");
        }

        private static bool ApplyOne(PackManifest pack, Transform bust, JObject entry, ManualLogSource logger)
        {
            string slot = (string)entry["slot"];
            string rel = (string)entry["sprite"];
            if (string.IsNullOrEmpty(slot)) return false;

            // The tick without the file. The author has said they mean to
            // replace this and has not chosen art yet; the editor complains
            // about it, and the game shows what it always showed rather than a
            // hole where a character's face was.
            if (string.IsNullOrEmpty(rel))
            {
                logger?.LogWarning("[SMSModForge.PackPlugin] " + bust.name + " asks to replace '"
                                   + slot + "' with nothing; left alone.");
                return false;
            }
            if (!pack.Has(rel))
            {
                logger?.LogWarning("[SMSModForge.PackPlugin] " + bust.name + " replaces '" + slot
                                   + "' with " + rel + ", which is not in the archive; left alone.");
                return false;
            }

            // The sprite root is MBase1 on most rigs and D1Base on others.
            // ActorRegistry.FindMBase is where that convention lives.
            var mBase = ActorRegistry.FindMBase(bust.gameObject);
            if (mBase == null)
            {
                logger?.LogWarning("[SMSModForge.PackPlugin] " + bust.name + " has no sprite root; "
                                   + "nothing to replace.");
                return false;
            }

            if (slot == SMSModForge.Shared.SpriteSlotNames.Mask)
                return ApplyMask(pack, mBase, rel);

            var target = Renderer(mBase, slot, pack, rel, logger);
            if (target == null) return false;

            BustFactory.ApplySpriteTo(target, pack, rel);
            return true;
        }

        /// <summary>The renderer one slot names, creating a face the game did
        /// not have when the pack asks for one.</summary>
        private static SpriteRenderer Renderer(Transform mBase, string slot, PackManifest pack,
                                               string rel, ManualLogSource logger)
        {
            if (slot == SMSModForge.Shared.SpriteSlotNames.Base)
                return mBase.GetComponent<SpriteRenderer>();

            if (slot == SMSModForge.Shared.SpriteSlotNames.Blink)
            {
                var blink = mBase.Find("Blink");
                return blink != null ? blink.GetComponent<SpriteRenderer>() : null;
            }

            int frame = SMSModForge.Shared.SpriteSlotNames.MouthFrame(slot);
            if (frame > 0)
            {
                var mouth = mBase.Find("Mouth");
                var child = mouth != null ? mouth.Find(frame.ToString()) : null;
                return child != null ? child.GetComponent<SpriteRenderer>() : null;
            }

            string face = SMSModForge.Shared.SpriteSlotNames.ExpressionOf(slot);
            if (face == null)
            {
                logger?.LogWarning("[SMSModForge.PackPlugin] Unknown texture slot '" + slot + "'.");
                return null;
            }

            var expressions = mBase.Find("Expressions");
            if (expressions == null)
            {
                logger?.LogWarning("[SMSModForge.PackPlugin] " + mBase.name + " has no Expressions group, "
                                   + "so '" + face + "' has nowhere to go.");
                return null;
            }

            return FindOrAddFace(expressions, face, logger);
        }

        /// <summary>
        /// A face the game's bust does not have, added to the end of its
        /// Expressions group.
        /// <para/>
        /// Cloned from a face it DOES have rather than built from nothing, so
        /// the new one inherits the renderer, material and sorting order the
        /// rest of the rig was set up with — the same reason a pack's bust is
        /// an Instantiate of a real one.
        /// <para/>
        /// The end matters. The game switches its own four faces by CHILD INDEX
        /// (a Conditions component: number 1 means child 0, and so on), so
        /// anything appended is invisible to that machinery and cannot displace
        /// it. It is invisible in the other direction too: the game's trigger
        /// switches children 0 to 3 off and knows nothing about a fifth, which
        /// is why <see cref="ActorRegistry"/> clears every child rather than
        /// the four it used to.
        /// </summary>
        internal static SpriteRenderer FindOrAddFace(Transform expressions, string face, ManualLogSource logger)
        {
            var existing = expressions.Find(face);
            if (existing != null) return existing.GetComponent<SpriteRenderer>();

            Transform model = null;
            for (int i = 0; i < expressions.childCount; i++)
                if (expressions.GetChild(i).GetComponent<SpriteRenderer>() != null)
                { model = expressions.GetChild(i); break; }

            if (model == null)
            {
                logger?.LogWarning("[SMSModForge.PackPlugin] " + expressions.name + " has no face to copy, "
                                   + "so '" + face + "' cannot be added.");
                return null;
            }

            var made = Object.Instantiate(model.gameObject, expressions);
            made.name = face;
            made.transform.SetAsLastSibling();
            made.transform.localPosition = model.localPosition;
            made.transform.localRotation = model.localRotation;
            made.transform.localScale = model.localScale;
            made.SetActive(false);

            logger?.LogInfo("[SMSModForge.PackPlugin] Added the face '" + face + "' to "
                            + expressions.parent.parent.name + ".");
            return made.GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// The jiggle mask, which is a texture on the material rather than a
        /// sprite on a renderer.
        /// <para/>
        /// Onto a CLONE of the material, always. The game's busts share
        /// materials, so writing the mask into the one this renderer happens to
        /// hold would put a pack's jiggle on every character wearing it.
        /// </summary>
        private static bool ApplyMask(PackManifest pack, Transform mBase, string rel)
        {
            var sr = mBase.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sharedMaterial == null) return false;

            var mat = new Material(sr.sharedMaterial);
            mat.SetTexture("_MaskTex", BustFactory.LoadTextureFrom(pack, rel, linear: true));
            sr.sharedMaterial = mat;
            return true;
        }
    }
}

using System;
using BepInEx.Logging;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Puts a pack scene through the game's own photo payout.
    /// <para/>
    /// <b>What a photo actually is.</b> Every one of the game's photos is a
    /// GameObject under <c>4_CG_Manager-Photos</c>, switched off until it is
    /// earned. Its own <c>Trigger</c>, on <c>EventOnEnable</c>, does three
    /// things: records the photo in the gallery, sometimes raises the
    /// <c>flash</c> signal, and switches on <c>PhotoBonus</c>.
    /// <para/>
    /// <c>PhotoBonus</c> is where everything worth having happens. It runs
    /// <c>GoodPhoto</c> and <c>BadPhoto</c>, which read Anna's mood and decide
    /// whether the shot came out - a bad one switches <c>PhotoBonus</c> off
    /// again and pays nothing - then the photography traits, the cameras and
    /// lenses the player owns, the adverts running, the skill tree, the gift
    /// buffs, the day's income and the photo counts.
    /// <para/>
    /// <b>Why none of that is reproduced here.</b> All of it is the game's, in
    /// the game's own instruction graphs, and it changes between builds. A copy
    /// would be a second opinion about the player's mood, traits and skills
    /// that drifts out of step the first time any of them is tuned. Switching
    /// <c>PhotoBonus</c> on is the whole implementation, and a pack photo then
    /// gets whatever a real photo gets, including whatever is added to it
    /// later.
    /// <para/>
    /// <b>The gallery is next door.</b> Taking the photo is remembered in the
    /// pack's own store, and <see cref="StarmakerGallery"/> puts a row in the
    /// Starmaker site's gallery for it. The two are separate on purpose: the
    /// gallery row is a record of what the player has taken, and a payout that
    /// could not run - no <c>PhotoBonus</c> in this scene - is not a reason to
    /// forget they took it.
    /// </summary>
    internal static class StarmakerPhotos
    {
        private const string Tag = "[SMSModForge.PackPlugin] Starmaker photo: ";

        /// <summary>
        /// What every one of the game's photo triggers ends by switching on,
        /// read off a dump of the running game rather than assumed.
        /// </summary>
        private const string PhotoBonusName = "PhotoBonus";

        /// <summary>Said once per pack, not once per photo: a pack whose photos
        /// pay out does it on every scene, and a log line each time would bury
        /// everything else.</summary>
        private static bool _saidItWorks;
        private static bool _saidItDoesNot;

        /// <summary>
        /// Take a photo, as far as the game is concerned.
        /// <para/>
        /// Called when a scene marked as a photo is switched ON, and never when
        /// it is switched off: a scene that is shown, hidden and shown again
        /// pays out each time it is shown, which is what taking the same shot
        /// twice does.
        /// </summary>
        public static void Taken(string sceneKey, PackContext ctx, ManualLogSource log)
        {
            string packId = ctx != null ? ctx.PackId : "";
            try
            {
                // Remembered first, so the gallery row is there even if the
                // payout below cannot run. Taking the photo and being paid for
                // it are two separate things and the player has done the first.
                ctx?.Vars?.TakePhoto(sceneKey);

                var bonus = Find(PhotoBonusName);
                if (bonus == null)
                {
                    if (!_saidItDoesNot)
                    {
                        _saidItDoesNot = true;
                        log?.LogWarning(Tag + "no '" + PhotoBonusName + "' in this scene, so '"
                                        + sceneKey + "' in " + packId + " showed without paying out. "
                                        + "Everything else about the scene is unaffected.");
                    }
                    return;
                }

                // Switched on is the whole of it. If it is already on, a photo
                // is mid-payout and switching it on again would do nothing -
                // Unity does not re-raise OnEnable for an object that is
                // already enabled - so it is turned off first and the game runs
                // its own sequence from the start, exactly as a second photo
                // taken in quick succession does.
                if (bonus.activeSelf) bonus.SetActive(false);
                bonus.SetActive(true);

                if (!_saidItWorks)
                {
                    _saidItWorks = true;
                    log?.LogInfo(Tag + "'" + sceneKey + "' in " + packId + " counts as a photo: "
                                 + "Anna's mood, the traits, the camera, the adverts and the skill "
                                 + "tree all apply to it as they do to the game's own.");
                }
            }
            catch (Exception ex)
            {
                log?.LogWarning(Tag + "'" + sceneKey + "' in " + packId + " could not be paid out ("
                                + ex.Message + "). The scene itself is unaffected.");
            }
        }

        /// <summary>
        /// The named object, switched on or not. <c>GameObject.Find</c> skips
        /// inactive ones, and <c>PhotoBonus</c> is inactive whenever a photo is
        /// not being paid out - which is every moment this is called.
        /// </summary>
        private static GameObject Find(string name)
        {
            var direct = GameObject.Find(name);
            if (direct != null) return direct;

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != name) continue;
                if (!go.scene.IsValid()) continue;      // an asset, not this scene
                return go;
            }
            return null;
        }
    }
}

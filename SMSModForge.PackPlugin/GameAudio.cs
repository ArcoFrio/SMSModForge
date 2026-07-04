using GameCreator.Runtime.Common;
using GameCreator.Runtime.Common.Audio;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Plays one-shot UI/dialogue SFX through GameCreator2's
    /// <see cref="AudioManager"/> — the exact path the host mod uses
    /// (<c>Singleton&lt;AudioManager&gt;.Instance.UserInterface.Play(clip,
    /// AudioConfigSoundUI.Create(volume, pitch), Args.EMPTY)</c>).
    /// <para/>
    /// This replaces playing through a per-pack <see cref="AudioSource"/>,
    /// which threw <c>"Can not play a disabled audio source"</c> whenever the
    /// scene deactivated the GameObject that source hung off. The managed UI
    /// audio channel is owned by the game and always available, so it can't
    /// fall into a disabled state mid-dialogue.
    /// </summary>
    public static class GameAudio
    {
        /// <summary>
        /// Fire a one-shot UI sound at <paramref name="volume"/> (0–1) and the
        /// default pitch. No-op if the clip is null or the AudioManager isn't
        /// up yet, so callers never have to guard.
        /// </summary>
        public static void PlayUi(AudioClip clip, float volume)
            => PlayUi(clip, volume, 1f, 1f);

        /// <summary>
        /// As <see cref="PlayUi(AudioClip, float)"/> but with an explicit
        /// pitch range the clip randomises between.
        /// </summary>
        public static void PlayUi(AudioClip clip, float volume, float pitchMin, float pitchMax)
        {
            if (clip == null) return;
            var am = Singleton<AudioManager>.Instance;
            if (am == null) return;

            var cfg = AudioConfigSoundUI.Create(Mathf.Clamp01(volume), new Vector2(pitchMin, pitchMax));
            // Play returns a Task we intentionally don't await — fire-and-forget,
            // matching the host mod's own SFX playback.
            _ = am.UserInterface.Play(clip, cfg, Args.EMPTY);
        }
    }
}

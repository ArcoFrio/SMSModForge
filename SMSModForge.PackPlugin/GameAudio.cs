using System.Collections.Generic;
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

            // Above 1 the channel can go no louder, so the clip is made louder
            // instead (the author, 1.7.0: a sound's volume goes up to 5). By the
            // square, because the channel squares what it is given before Unity
            // plays it (GC2's AudioBuffer.Rescale) - so 2 is to 1 what 1 is to
            // 0.5, the same curve all the way up.
            volume = Mathf.Max(0f, volume);
            if (volume > 1f)
            {
                clip = Louder(clip, volume * volume);
                volume = 1f;
            }

            var cfg = AudioConfigSoundUI.Create(volume, new Vector2(pitchMin, pitchMax));
            // Play returns a Task we intentionally don't await — fire-and-forget,
            // matching the host mod's own SFX playback.
            _ = am.UserInterface.Play(clip, cfg, Args.EMPTY);
        }

        // AudioClip's float[] GetData and SetData, by reflection: Unity 6 gives
        // both a Span overload as well, and Span is a netstandard 2.1 type this
        // .NET Framework build cannot see - so naming either call at all stops
        // the build, whichever overload it means.
        private static readonly System.Reflection.MethodInfo _getData =
            typeof(AudioClip).GetMethod("GetData", new[] { typeof(float[]), typeof(int) });
        private static readonly System.Reflection.MethodInfo _setData =
            typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });

        /// <summary>Louder copies of clips, made once each: by the clip, and by
        /// the gain to a hundredth.</summary>
        private static readonly Dictionary<long, AudioClip> _louder = new Dictionary<long, AudioClip>();

        /// <summary>
        /// <paramref name="clip"/> with every sample multiplied by
        /// <paramref name="gain"/>, or the clip itself when its samples cannot
        /// be read - a clip the game streams rather than holds - so the sound
        /// still plays, at the loudest the channel goes.
        /// </summary>
        private static AudioClip Louder(AudioClip clip, float gain)
        {
            long key = ((long)clip.GetInstanceID() << 20) ^ Mathf.RoundToInt(gain * 100f);
            AudioClip made;
            if (_louder.TryGetValue(key, out made) && made != null) return made;
            try
            {
                if (_getData == null || _setData == null) return clip;
                var samples = new float[clip.samples * clip.channels];
                if (!(bool)_getData.Invoke(clip, new object[] { samples, 0 })) return clip;
                for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
                made = AudioClip.Create(clip.name, clip.samples, clip.channels, clip.frequency, false);
                _setData.Invoke(made, new object[] { samples, 0 });
                _louder[key] = made;
                return made;
            }
            catch (System.Exception)
            {
                return clip;
            }
        }

        /// <summary>Let go of the louder copies: the packs' clips are loaded
        /// again with the packs.</summary>
        public static void Forget()
        {
            foreach (var clip in _louder.Values)
                if (clip != null) Object.Destroy(clip);
            _louder.Clear();
        }
    }
}

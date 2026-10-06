using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Makes one AudioSource louder than its volume can (the author, 1.7.0).
    /// <para/>
    /// Unity stops an AudioSource's volume at 1, and a pack's music track may
    /// now be up to five times as loud as the game's own level for a track -
    /// which, for a copy of 12_AudioPlayer/Beach at 0.5, is 2.5. The source
    /// goes to 1 and this takes the rest, on the samples as they pass: no copy
    /// of the clip, which for minutes of music would be a lot of memory.
    /// </summary>
    public sealed class SoundGain : MonoBehaviour
    {
        /// <summary>What the samples are multiplied by. Read on the audio
        /// thread; a float is written in one go.</summary>
        public float Gain = 1f;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float g = Gain;
            if (g == 1f) return;
            for (int i = 0; i < data.Length; i++) data[i] *= g;
        }
    }
}

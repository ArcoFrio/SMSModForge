using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using SMSModForge.Shared;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Plays an animation on a SpriteRenderer, from decoded frames or from a
    /// video file.
    /// <para/>
    /// The two halves exist because they fail in opposite directions. Swapping
    /// pre-decoded frames costs nothing per update — a lookup is about fifty
    /// nanoseconds against a sixteen-millisecond budget — but it costs 256 KB
    /// of memory per frame, so a thirty-second clip would want a quarter of a
    /// gigabyte. A VideoPlayer costs almost no memory and decodes as it goes.
    /// So: short things that loop are frames, and video is video.
    /// <para/>
    /// Which frame is showing comes from <see cref="AnimationClock"/>, shared
    /// with the editor, and it maps TIME to a frame rather than counting
    /// updates. Counting updates plays a 24fps animation at whatever the
    /// monitor happens to run at, and drifts further the longer a scene stays
    /// open.
    /// </summary>
    internal sealed class AnimatedSprite : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Sprite[] _frames;
        private AnimationClock _clock;
        private bool _loop = true;
        private float _startedAt;
        private int _showing = -1;

        /// <summary>
        /// Start playing decoded frames.
        /// <para/>
        /// The sprites are built by the caller so they go through whatever
        /// fitting their kind uses — which is how an animated scene ends up
        /// exactly the size a still scene of the same art would be.
        /// </summary>
        public void Play(SpriteRenderer target, Sprite[] frames, double[] delays, bool loop)
        {
            _renderer = target;
            _frames = frames;
            _loop = loop;
            _clock = new AnimationClock(delays, 0.1);

            // Restart here as well as in OnEnable: on an object that is
            // already active, OnEnable ran before this and found no clock.
            Restart();
        }

        /// <summary>
        /// Begin at the first frame, now.
        /// <para/>
        /// Called when the scene is SHOWN rather than when it is built, and
        /// that distinction is the whole of it: every scene in a pack is
        /// created inactive at load and activated later, sometimes hours
        /// later. A clock started at build time would have a looping animation
        /// opening on an arbitrary frame, and a one-shot already finished
        /// before anybody saw it.
        /// </summary>
        private void Restart()
        {
            _startedAt = Time.time;
            _showing = -1;
            Step();
        }

        private void OnEnable()
        {
            if (_clock != null) Restart();
        }

        private void Update()
        {
            Step();
        }

        private void Step()
        {
            if (_renderer == null || _frames == null || _frames.Length == 0 || _clock == null)
                return;

            int want = _clock.FrameAt(Time.time - _startedAt, _loop);
            if (want < 0 || want >= _frames.Length) return;

            // Only when it actually changed. Assigning the same sprite every
            // update is not free at the renderer even though the lookup is.
            if (want == _showing) return;

            _showing = want;
            _renderer.sprite = _frames[want];
        }

        /// <summary>
        /// Build the sprites for a decoded animation out of a pack.
        /// <para/>
        /// Null when the frames are not there — a pack whose author never saved
        /// after picking the GIF, or one shipped without the frames folder. The
        /// caller falls back to a still, which is a scene that does not move
        /// rather than a scene that is not there.
        /// </summary>
        public static Sprite[] LoadFrames(PackManifest pack, string gifRel,
                                          out double[] delays,
                                          System.Func<Texture2D, Sprite> make)
        {
            delays = null;
            if (pack == null || string.IsNullOrEmpty(gifRel)) return null;

            string folder = MediaKinds.FramesFolderFor(gifRel);
            byte[] manifestBytes = pack.ReadBytes(folder + "/" + MediaKinds.FramesManifest);
            if (manifestBytes == null) return null;

            double[] read = ReadDelays(manifestBytes);
            if (read == null || read.Length == 0) return null;

            var sprites = new List<Sprite>(read.Length);
            for (int i = 0; i < read.Length; i++)
            {
                byte[] png = pack.ReadBytes(folder + "/" + MediaKinds.FrameName(i));
                if (png == null) break;

                var tex = new Texture2D(2, 2);
                tex.filterMode = FilterMode.Point;
                ImageConversion.LoadImage(tex, png);
                sprites.Add(make(tex));
            }

            if (sprites.Count == 0) return null;

            // A short read is a truncated animation, not a missing one: play
            // what is there and say so, rather than dropping the scene.
            if (sprites.Count < read.Length)
            {
                var trimmed = new double[sprites.Count];
                System.Array.Copy(read, trimmed, sprites.Count);
                delays = trimmed;
            }
            else
            {
                delays = read;
            }
            return sprites.ToArray();
        }

        private static double[] ReadDelays(byte[] json)
        {
            try
            {
                var parsed = Newtonsoft.Json.Linq.JObject.Parse(
                    System.Text.Encoding.UTF8.GetString(json));
                var array = parsed["delays"] as Newtonsoft.Json.Linq.JArray;
                if (array == null) return null;

                var made = new double[array.Count];
                for (int i = 0; i < array.Count; i++) made[i] = (double)array[i];
                return made;
            }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        // ── Video ────────────────────────────────────────────────────

        /// <summary>
        /// Put a video on a SpriteRenderer.
        /// <para/>
        /// A Sprite must be cut from a Texture2D and a VideoPlayer produces a
        /// RenderTexture, so the two do not meet directly.
        /// <para/>
        /// The obvious bridge - leave a sprite for its geometry and point
        /// <c>material.mainTexture</c> at the video - does not work, and fails
        /// SILENTLY. A SpriteRenderer rebinds its sprite's own texture when it
        /// draws, so the assignment is simply overwritten and the scene shows
        /// the placeholder for ever. No error, no warning: the video prepares,
        /// plays, and is never seen.
        /// <para/>
        /// What does work is to give the sprite a texture of OUR own and keep
        /// copying the video into it. A Sprite holds a reference to its
        /// texture, so changing that texture's pixels changes what the sprite
        /// draws - no new sprite per frame, and the fitting rule still decides
        /// the size because it is an ordinary Texture2D like any other art.
        /// </summary>
        public static VideoSprite PlayVideo(GameObject on, SpriteRenderer target,
                                            string filePath, bool loop, float volume,
                                            int frameW, int frameH,
                                            BepInEx.Logging.ManualLogSource log)
        {
            if (on == null || target == null || string.IsNullOrEmpty(filePath)) return null;

            var driver = on.AddComponent<VideoSprite>();
            driver.Begin(target, filePath, loop, volume, frameW, log);
            return driver;
        }
    }

    /// <summary>
    /// Keeps one SpriteRenderer showing whatever a VideoPlayer is playing.
    /// <para/>
    /// Separate from <see cref="AnimatedSprite"/> because it does the opposite
    /// thing: frames swap a sprite and never touch pixels, this keeps one
    /// sprite and rewrites its pixels.
    /// </summary>
    internal sealed class VideoSprite : MonoBehaviour
    {
        private VideoPlayer _player;
        private SpriteRenderer _renderer;
        private Texture2D _surface;
        private RenderTexture _scratch;
        private BepInEx.Logging.ManualLogSource _log;
        private bool _running;

        internal void Begin(SpriteRenderer target, string filePath, bool loop, float volume,
                            int frameSize, BepInEx.Logging.ManualLogSource log)
        {
            _renderer = target;
            _log = log;

            _player = gameObject.AddComponent<VideoPlayer>();
            _player.source = VideoSource.Url;
            _player.url = filePath;
            _player.isLooping = loop;
            _player.playOnAwake = false;
            _player.waitForFirstFrame = true;

            // The player hands us its own texture rather than drawing anywhere
            // itself; where it ends up is this component's business.
            _player.renderMode = VideoRenderMode.APIOnly;

            // Audio is wired before Prepare, because that is when the player
            // reads it - but how many tracks a file HAS is only known after.
            // So this is attempted and survived: a video with no sound track
            // is the ordinary case, not a failure, and it must not be able to
            // take the picture down with it.
            if (volume > 0f)
            {
                try
                {
                    var speaker = gameObject.AddComponent<AudioSource>();
                    speaker.playOnAwake = false;
                    speaker.volume = Mathf.Clamp01(volume);
                    _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                    _player.SetTargetAudioSource(0, speaker);
                }
                catch (System.Exception ex)
                {
                    _player.audioOutputMode = VideoAudioOutputMode.None;
                    _log?.LogInfo("[SMSModForge.PackPlugin] Video '" + filePath
                                  + "' takes no audio source (" + ex.GetType().Name
                                  + ") - playing silently.");
                }
            }
            else
            {
                // Explicitly none: a video muted in the editor must be silent.
                _player.audioOutputMode = VideoAudioOutputMode.None;
            }

            _player.errorReceived += (source, message) =>
                _log?.LogWarning("[SMSModForge.PackPlugin] Video '" + filePath + "': " + message);

            _player.prepareCompleted += source =>
            {
                // Sized to the scene's own frame rather than the video's, and
                // deliberately: every frame is copied back through the CPU, so
                // a 1080p source would move eight megabytes per frame to draw
                // something the size of a postcard. The aspect is kept, so the
                // fitting rule places it exactly as it would place a still.
                int w = (int)source.width;
                int h = (int)source.height;
                if (w <= 0 || h <= 0) { w = frameSize; h = frameSize; }

                float shrink = Mathf.Max(w, h) > frameSize
                    ? frameSize / (float)Mathf.Max(w, h)
                    : 1f;
                int sw = Mathf.Max(1, Mathf.RoundToInt(w * shrink));
                int sh = Mathf.Max(1, Mathf.RoundToInt(h * shrink));

                _surface = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
                _surface.filterMode = FilterMode.Bilinear;
                _scratch = new RenderTexture(sw, sh, 0);
                _scratch.Create();

                // One sprite, made the way every other scene's art is made, so
                // an animated scene is exactly the size a still one would be.
                _renderer.sprite = FittedSprite.CreateScene(_surface);

                // Only if the scene is still on screen: preparing takes a
                // moment, and the player may have walked away inside it.
                if (isActiveAndEnabled)
                {
                    _running = true;
                    source.Play();
                }

                _log?.LogInfo("[SMSModForge.PackPlugin] Video '" + filePath + "' playing: "
                              + w + "x" + h + " at " + source.frameRate.ToString("0.##")
                              + "fps, drawn at " + sw + "x" + sh
                              + ", " + source.audioTrackCount + " audio track(s)"
                              + (loop ? ", looping" : ", once")
                              + (volume > 0f ? "" : ", muted"));
            };

            // Deliberately NOT prepared here.
            //
            // Every scene in a pack is instantiated inactive at load and
            // activated only when it is shown. A VideoPlayer on an inactive
            // GameObject does not progress: Prepare is accepted and then sits
            // there for ever, with no error, because from the engine's point of
            // view nothing went wrong. That is exactly how this failed - the
            // log said "preparing" and never said anything again.
            //
            // Waiting for OnEnable also means a pack with a hundred and
            // thirty-eight scenes is not opening a hundred and thirty-eight
            // video files at load.
            _log?.LogInfo("[SMSModForge.PackPlugin] Video '" + filePath
                          + "' ready to play when its scene is shown.");

            // Unless it is already being shown. Adding a component to an
            // ACTIVE object runs OnEnable immediately - before this method,
            // and so before there was a player to prepare - and without this
            // that video would wait for an activation that had already
            // happened.
            if (isActiveAndEnabled) OnEnable();
        }

        private void OnEnable()
        {
            if (_player == null) return;

            // Prepared already means this scene has been shown before: start it
            // again from the top, the way a scene shown twice should look.
            if (_player.isPrepared)
            {
                _running = true;
                _player.frame = 0;
                _player.Play();
                return;
            }

            _log?.LogInfo("[SMSModForge.PackPlugin] Video '" + _player.url + "' preparing.");
            _player.Prepare();
        }

        private void OnDisable()
        {
            _running = false;
            if (_player != null && _player.isPlaying) _player.Pause();
        }

        private void Update()
        {
            if (!_running || _player == null || _surface == null) return;

            var frame = _player.texture;
            if (frame == null) return;

            // Through a scratch target because the player's texture is not
            // necessarily readable or the right size; Blit does the scaling on
            // the GPU, and only the small result comes back.
            var was = RenderTexture.active;
            try
            {
                Graphics.Blit(frame, _scratch);
                RenderTexture.active = _scratch;
                _surface.ReadPixels(new Rect(0, 0, _surface.width, _surface.height), 0, 0, false);
                _surface.Apply(false);
            }
            finally { RenderTexture.active = was; }
        }

        private void OnDestroy()
        {
            _running = false;
            if (_player != null) _player.Stop();
            if (_scratch != null) { _scratch.Release(); Destroy(_scratch); }
            if (_surface != null) Destroy(_surface);
        }
    }
}

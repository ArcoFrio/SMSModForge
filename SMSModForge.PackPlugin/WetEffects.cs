using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The game's two wet effects, cloned for a pack's NPCs and busts (after 1.10.0, the author: "there should be a
    /// toggle between the two wet types for both busts and npcs"), and the settings a placement gives them.
    /// <list type="bullet">
    /// <item>The TOWEL: 2_Bust_Manager/Anna_Towel/MBase1's "Particle System" (2 thin white drops a second) and its
    /// "Particle System (1)" (6 bigger ones, 2.04 units lower) - before 1.11 only the first was cloned, a quarter of the
    /// drops Anna shows. The second goes under the first as "Wet (1)", so whatever turns "Wet" on or off turns both.</item>
    /// <item>The SHOWER: 5_Levels/4_Bath/AnnaInShower's "Particle System (1)": 200 fine blue-grey streaks a second from a
    /// 2 x 1 box, lit, on the Default layer at 15.</item>
    /// </list>
    /// Both systems scale Local: only their own object's scale is in the box and the drops (InGameSuiteTests.S17), so
    /// the second towel system copies the Wet object's scale onto itself, and its offset is a shape position.
    /// </summary>
    public static class WetEffects
    {
        /// <summary>How far below the first towel system's box the second's sits, a share of the box's height:
        /// Anna's is 0.51 under a body scaled 4, against a box 8.45 tall.</summary>
        public const float TowelSecondShift = 2.04f / 8.45f;

        public const string SecondName = "Wet (1)";

        private static Transform Find(string root) => TransformExtensions.FindGlobalIncludingInactive(root)?.transform;

        public static GameObject TowelSource() => Find("Anna_Towel")?.Find("MBase1")?.Find("Particle System")?.gameObject;
        public static GameObject TowelSecondSource() => Find("Anna_Towel")?.Find("MBase1")?.Find("Particle System (1)")?.gameObject;
        /// <summary>By its whole path: 8_Room_Talk/Bath has an "AnnaInShower" of its own (a talk's condition, no drops),
        /// and a search by name finds that one first.</summary>
        public static GameObject ShowerSource() => Find("5_Levels")?.Find("4_Bath/AnnaInShower/Particle System (1)")?.gameObject;

        /// <summary>
        /// A placed NPC's Wet object: the towel's (both systems) or the shower's, at <paramref name="place"/> (its
        /// local transform; the shower's own scale of 1.053 kept as "1"), with the placement's
        /// <paramref name="effect"/> settings. Null when the game's source is missing.
        /// </summary>
        public static GameObject BuildNpc(Transform npc, string name, JObject effect, System.Action<Transform> place)
        {
            bool shower = (string)effect?["kind"] == "shower";
            var source = shower ? ShowerSource() : TowelSource();
            if (source == null) return null;
            var p = Object.Instantiate(source, npc);
            p.name = name;
            place(p.transform);
            if (shower) p.transform.localScale = Vector3.Scale(p.transform.localScale, source.transform.localScale);
            Finish(p, shower, effect);
            return p;
        }

        /// <summary>
        /// A pack bust's wet particle under its <c>MBase1</c>: the towel's at the picture's middle (where Anna's are),
        /// or the shower's at the middle of the picture's top edge, over the bust (the Bust layer at 10, where the
        /// towel's drops are drawn).
        /// </summary>
        public static GameObject AttachBust(GameObject mBase, string name, bool active, bool shower)
        {
            var source = shower ? ShowerSource() : TowelSource();
            if (source == null) return null;
            var p = Object.Instantiate(source, mBase.transform);
            p.name = name;
            if (shower)
            {
                var sr = mBase.GetComponent<SpriteRenderer>();
                float top = sr != null && sr.sprite != null ? sr.sprite.bounds.max.y : 0f;
                p.transform.localPosition = new Vector3(0f, top, 0f);
                p.transform.localRotation = Quaternion.identity;
                var r = p.GetComponent<ParticleSystemRenderer>();
                if (r != null)
                {
                    r.sortingLayerName = "Bust";
                    r.sortingOrder = 10;
                    // Unlit, as the towel's drops on a bust: the shower's lit material takes the Bust layer's own
                    // lights there (measured 1.6 times its colour by MyRoom's window, 0.8 below), which the editor has
                    // no picture of. The towel's material with a white texture: solid quads of the shower's colours.
                    var unlit = UnlitSolid();
                    if (unlit != null) r.sharedMaterial = unlit;
                }
            }
            Finish(p, shower, null);
            p.SetActive(active);
            return p;
        }

        private static Material _unlitSolid;

        /// <summary>The towel's unlit particle material (Particles/Standard Unlit, alpha blended), white throughout.</summary>
        private static Material UnlitSolid()
        {
            if (_unlitSolid != null) return _unlitSolid;
            var towel = TowelSource()?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
            if (towel == null) return null;
            _unlitSolid = new Material(towel) { name = "SMSModForge Wet Unlit Solid", mainTexture = Texture2D.whiteTexture };
            return _unlitSolid;
        }

        /// <summary>The towel's second system under the first, and the settings on each.</summary>
        private static void Finish(GameObject p, bool shower, JObject effect)
        {
            var ps = p.GetComponent<ParticleSystem>();
            if (ps == null) return;
            float w = F(effect, "width", ps.shape.scale.x), h = F(effect, "height", ps.shape.scale.y);
            Settle(ps, effect, w, h, 0f, shower);
            if (shower)
            {
                if (B(effect, "floor"))
                {
                    var floor = p.AddComponent<WetFloor>();
                    floor.Box = new Rect(-w / 2f, -h / 2f, w, h);
                }
                return;
            }
            var second = TowelSecondSource();
            if (second == null) return;
            var q = Object.Instantiate(second, p.transform);
            q.name = SecondName;
            q.transform.localPosition = Vector3.zero;
            q.transform.localRotation = Quaternion.identity;
            // Scaling Local: the parent's scale is not in the box or the drops - the Wet object's own is copied.
            q.transform.localScale = p.transform.localScale;
            var qs = q.GetComponent<ParticleSystem>();
            if (qs != null) Settle(qs, effect, w, h, -TowelSecondShift * h, false);
        }

        /// <summary>One system's box (its middle <paramref name="down"/> below the Wet object's) and settings.</summary>
        private static void Settle(ParticleSystem ps, JObject effect, float w, float h, float down, bool shower)
        {
            var shape = ps.shape;
            bool fromTop = shower && B(effect, "fromTop");
            shape.scale = new Vector3(w, fromTop ? 0f : h, shape.scale.z);
            shape.position = new Vector3(0f, down + (fromTop ? h / 2f : 0f), 0f);
            if (effect == null) return;
            var main = ps.main;
            float amount = F(effect, "amount", 1f), size = F(effect, "size", 1f), fall = F(effect, "fall", 1f);
            if (amount != 1f)
            {
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= amount;
                var lt = main.startLifetime;
                float life = lt.mode == ParticleSystemCurveMode.TwoConstants ? lt.constantMax
                           : lt.mode == ParticleSystemCurveMode.Constant ? lt.constant : main.startLifetimeMultiplier;
                main.maxParticles = Mathf.Max(main.maxParticles, Mathf.CeilToInt(emission.rateOverTimeMultiplier * life * 1.1f) + 1);
            }
            if (size != 1f)
            {
                // startSize is startSizeX's curve when the size is per axis: each multiplied once. Whole curves: a
                // "multiplier" of two numbers scales the larger alone (S21 read 0.01..0.008 for 0.01..0.005 x 1.5).
                if (main.startSize3D)
                {
                    main.startSizeX = Times(main.startSizeX, size);
                    main.startSizeY = Times(main.startSizeY, size);
                    main.startSizeZ = Times(main.startSizeZ, size);
                }
                else main.startSize = Times(main.startSize, size);
            }
            if (fall != 1f) main.gravityModifier = Times(main.gravityModifier, fall);
            if (BustFactory.TryParseHexColor((string)effect["tint"], out var tint) && tint != Color.white)
            {
                var c = main.startColor;
                if (c.mode == ParticleSystemGradientMode.TwoColors)
                    main.startColor = new ParticleSystem.MinMaxGradient(c.colorMin * tint, c.colorMax * tint);
                else if (c.mode == ParticleSystemGradientMode.Color)
                    main.startColor = new ParticleSystem.MinMaxGradient(c.color * tint);
            }
        }

        /// <summary>A curve <paramref name="k"/> times as large: both of two numbers, the curve's multiplier otherwise.</summary>
        private static ParticleSystem.MinMaxCurve Times(ParticleSystem.MinMaxCurve c, float k)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant: return new ParticleSystem.MinMaxCurve(c.constant * k);
                case ParticleSystemCurveMode.TwoConstants: return new ParticleSystem.MinMaxCurve(c.constantMin * k, c.constantMax * k);
                default: c.curveMultiplier *= k; return c;
            }
        }

        /// <summary>
        /// A placed NPC's wet effect: its NPC's look ("wet": kind, tint, amount, size, fall) with what the placement
        /// sets over it ("wetEffect"), and the placement's box, top and floor (after 1.10.0, the author: "NPC sets
        /// them, placements can change"). The editor's WetEffectDef.Merged.
        /// </summary>
        public static JObject Merged(JObject npcWet, JObject placed)
        {
            var merged = new JObject();
            foreach (string key in new[] { "kind", "tint", "amount", "size", "fall" })
            {
                var value = Set(placed, key) ?? Set(npcWet, key);
                if (value != null) merged[key] = value.DeepClone();
            }
            foreach (string key in new[] { "width", "height", "fromTop", "floor" })
            {
                var value = Set(placed, key);
                if (value != null) merged[key] = value.DeepClone();
            }
            return merged;
        }

        /// <summary>A value the record sets: null when it has none (or says null).</summary>
        private static JToken Set(JObject o, string key)
        {
            var t = o?[key];
            return t == null || t.Type == JTokenType.Null ? null : t;
        }

        private static float F(JObject o, string key, float fallback)
        {
            var t = o?[key];
            return t == null || t.Type == JTokenType.Null ? fallback : (float)t;
        }

        private static bool B(JObject o, string key) => (bool?)o?[key] == true;
    }

    /// <summary>
    /// A shower's drops gone the moment they leave its box (the author: "I want them to disappear abruptly at the
    /// floor of the box"): every frame after the system has moved them, a drop whose lower end is out of
    /// <see cref="Box"/> (the system's own units, its scale not in them) is taken out before the frame is drawn.
    /// Drops fall straight down, so a drop leaves through the floor - or a lower side of a turned box.
    /// </summary>
    public sealed class WetFloor : MonoBehaviour
    {
        public Rect Box;
        private ParticleSystem _ps;
        private ParticleSystem.Particle[] _buf;

        private void Awake() => _ps = GetComponent<ParticleSystem>();

        private void LateUpdate()
        {
            if (_ps == null) return;
            int n = _ps.particleCount;
            if (n == 0) return;
            if (_buf == null || _buf.Length < _ps.main.maxParticles) _buf = new ParticleSystem.Particle[_ps.main.maxParticles];
            n = _ps.GetParticles(_buf);
            var tr = transform;
            Vector3 ls = tr.localScale;
            if (Mathf.Abs(ls.x) < 1e-6f || Mathf.Abs(ls.y) < 1e-6f) return;
            var back = Quaternion.Inverse(tr.rotation);
            int kept = 0;
            for (int i = 0; i < n; i++)
            {
                var p = _buf[i];
                // The drop's lower end: half its height straight down on the screen, in the system's own units.
                float halfHeight = p.GetCurrentSize3D(_ps).y * Mathf.Abs(ls.y) * 0.5f;
                Vector3 down = back * new Vector3(0f, -halfHeight, 0f);
                float x = p.position.x + down.x / ls.x, y = p.position.y + down.y / ls.y;
                if (x >= Box.xMin - 1e-4f && x <= Box.xMax + 1e-4f && y >= Box.yMin - 1e-4f && y <= Box.yMax + 1e-4f)
                    _buf[kept++] = p;
            }
            if (kept != n) _ps.SetParticles(_buf, kept);
        }
    }
}

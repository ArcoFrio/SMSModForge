using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Weather glue between the vanilla weather system and pack content.
    /// <para/>
    /// Vanilla state is two GC2 game variables — <c>rainy-day</c> and
    /// <c>snowy-day</c> — plus four particle cores under
    /// <c>Weather_System_Inside</c> / <c>Weather_System_Outside</c>
    /// (<c>Prefab_Rainy_Day/Rain_Core</c>, <c>Prefab_Snowy_Day/Snow_Core</c>).
    /// Vanilla levels activate those per level on their own; pack-built
    /// levels declare a <c>weatherType</c> (None / Inside / Outside) and this
    /// class is their activator:
    /// <list type="bullet">
    ///   <item><see cref="Tick"/> — per frame, when a pack level with a
    ///   weather type is active and today is rainy/snowy, the matching core
    ///   is switched on (mirrors the host mod's per-level loop, generalised
    ///   off <see cref="PlaceRegistry"/> data).</item>
    ///   <item><see cref="IsRaining"/> / <see cref="IsSnowing"/> — read the
    ///   vanilla variables; the <c>Weather</c> condition evaluates these.</item>
    ///   <item><see cref="Apply"/> — the <c>SetWeather</c> action: writes the
    ///   variables and refreshes the particle cores immediately so the change
    ///   is visible mid-scene, not just after the next level transition.</item>
    /// </list>
    /// </summary>
    public static class WeatherRuntime
    {
        public static bool IsRaining => GameVariableBridge.GetBool("rainy-day");
        public static bool IsSnowing => GameVariableBridge.GetBool("snowy-day");
        public static bool IsBadWeather => IsRaining || IsSnowing;

        // Cached particle cores; re-resolved per scene (Reset clears).
        private static GameObject _insideRain, _insideSnow, _outsideRain, _outsideSnow;
        private static bool _coresSearched;

        public static void Reset()
        {
            _insideRain = _insideSnow = _outsideRain = _outsideSnow = null;
            _coresSearched = false;
        }

        private static void ResolveCores()
        {
            if (_coresSearched) return;
            _coresSearched = true;
            var inside = TransformExtensions.FindGlobalIncludingInactive("Weather_System_Inside");
            var outside = TransformExtensions.FindGlobalIncludingInactive("Weather_System_Outside");
            _insideRain  = inside  != null ? inside.transform.Find("Prefab_Rainy_Day/Rain_Core")?.gameObject : null;
            _insideSnow  = inside  != null ? inside.transform.Find("Prefab_Snowy_Day/Snow_Core")?.gameObject : null;
            _outsideRain = outside != null ? outside.transform.Find("Prefab_Rainy_Day/Rain_Core")?.gameObject : null;
            _outsideSnow = outside != null ? outside.transform.Find("Prefab_Snowy_Day/Snow_Core")?.gameObject : null;
        }

        /// <summary>
        /// Per-frame activator for pack places. Only ever switches cores ON
        /// (matching the host mod's loop — vanilla machinery owns switching
        /// them off on level exits / day changes).
        /// </summary>
        public static void Tick()
        {
            bool rain = IsRaining;
            bool snow = IsSnowing;
            if (!rain && !snow) return;

            foreach (var place in PlaceRegistry.AllPackPlaces())
            {
                if (place.Level == null || !place.Level.activeSelf) continue;
                bool inside;
                if (place.WeatherType == "Inside") inside = true;
                else if (place.WeatherType == "Outside") inside = false;
                else continue;

                ResolveCores();
                if (rain) Activate(inside ? _insideRain : _outsideRain);
                if (snow) Activate(inside ? _insideSnow : _outsideSnow);
            }
        }

        /// <summary>
        /// The SetWeather action. <paramref name="weather"/> is
        /// <c>Rain</c> / <c>Snow</c> / <c>Clear</c>: writes the vanilla
        /// variables (Rain and Snow are exclusive, like the vanilla daily
        /// roll), stops the cores that no longer apply, and re-runs
        /// <see cref="Tick"/> so the new weather shows on the current pack
        /// level this frame. Vanilla levels pick the change up through their
        /// own per-level machinery.
        /// </summary>
        public static void Apply(string weather)
        {
            bool rain = weather == "Rain";
            bool snow = weather == "Snow";
            GameVariableBridge.SetBool("rainy-day", rain);
            GameVariableBridge.SetBool("snowy-day", snow);

            ResolveCores();
            if (!rain) { Deactivate(_insideRain); Deactivate(_outsideRain); }
            if (!snow) { Deactivate(_insideSnow); Deactivate(_outsideSnow); }
            Tick();
        }

        private static void Activate(GameObject go)   { if (go != null && !go.activeSelf) go.SetActive(true); }
        private static void Deactivate(GameObject go) { if (go != null && go.activeSelf) go.SetActive(false); }
    }
}

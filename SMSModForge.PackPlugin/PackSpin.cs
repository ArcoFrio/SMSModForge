using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Spins its transform around Z at a constant rate while enabled — the
    /// component behind the <c>SpinGameObject</c> dialogue action. Replaces
    /// the host mod's hardcoded per-frame <c>Rotate(0,0,1*Time.deltaTime)</c> on
    /// the a spinning prop; the action toggles <see cref="Behaviour.enabled"/>
    /// to start/stop and sets <see cref="DegreesPerSecond"/>.
    /// </summary>
    internal sealed class PackSpin : MonoBehaviour
    {
        public float DegreesPerSecond;

        private void Update()
        {
            if (DegreesPerSecond != 0f)
                transform.Rotate(0f, 0f, DegreesPerSecond * Time.deltaTime);
        }
    }
}

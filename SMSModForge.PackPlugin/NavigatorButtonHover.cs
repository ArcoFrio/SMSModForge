using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Port of the host mod's <c>ButtonHover</c> script. Smoothly fades the
    /// alpha of a child <see cref="Image"/> between 0 (idle) and 1 (hover)
    /// to give the cloned vanilla navigator buttons their original
    /// hover-glow effect — which we'd otherwise lose by stripping the
    /// vanilla <c>ButtonInstructions</c>.
    /// <para/>
    /// The cloned button structure (after <c>ButtonPressed</c> is inserted
    /// at index 0) is:
    /// <list type="number">
    ///   <item><c>ButtonPressed</c> — sentinel (index 0)</item>
    ///   <item><c>Text (TMP)</c> — label (index 1)</item>
    ///   <item><c>Image</c> — basic hover overlay (index 2) ← what we fade</item>
    ///   <item><c>Image (1)</c> — decorated overlay w/ Shadow + Outline (index 3)</item>
    ///   <item><c>keyboardnumber</c> — keyboard hint (index 4)</item>
    /// </list>
    /// </summary>
    public class NavigatorButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public float FadeSpeed = 8f;

        /// <summary>
        /// Child index to read the hover <see cref="Image"/> from.
        /// Defaults to 2 — the position of the basic <c>Image</c> child in
        /// the cloned vanilla button (after <c>ButtonPressed</c> at 0).
        /// </summary>
        public int ImageChildIndex = 2;

        private bool _mouseOver;
        private Image _image;

        public void OnPointerEnter(PointerEventData eventData) => _mouseOver = true;
        public void OnPointerExit(PointerEventData eventData) => _mouseOver = false;

        private void Start()
        {
            if (transform.childCount > ImageChildIndex)
                _image = transform.GetChild(ImageChildIndex).GetComponent<Image>();
        }

        private void OnEnable()
        {
            _mouseOver = false;
            if (_image != null)
            {
                Color c = _image.color;
                c.a = 0f;
                _image.color = c;
            }
        }

        private void Update()
        {
            if (_image == null) return;
            Color c = _image.color;
            float target = _mouseOver ? 1f : 0f;
            c.a = Mathf.MoveTowards(c.a, target, FadeSpeed * Time.deltaTime);
            _image.color = c;
        }
    }
}

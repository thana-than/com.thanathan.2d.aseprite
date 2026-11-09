using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityEngine.U2D.Aseprite
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public class ImageSpriteLayout : UIBehaviour, ILayoutSelfController
    {
        [SerializeField] private bool useSpritePivotX = true;
        [SerializeField] private bool useSpritePivotY = true;
        [SerializeField] private bool useNativeSizeX;
        [SerializeField] private bool useNativeSizeY;
        [SerializeField] private Vector2 nativeSizeMultiplier = Vector2.one;

        private DrivenRectTransformTracker _tracker;
        private Image _image;
        private Sprite _appliedSprite;

        private DrivenTransformProperties DrivenProperties =>
            (useSpritePivotX ? DrivenTransformProperties.PivotX : DrivenTransformProperties.None)
            | (useSpritePivotY ? DrivenTransformProperties.PivotY : DrivenTransformProperties.None)
            | (useNativeSizeX ? DrivenTransformProperties.SizeDeltaX : DrivenTransformProperties.None)
            | (useNativeSizeY ? DrivenTransformProperties.SizeDeltaY : DrivenTransformProperties.None);

        public void SetUseNativeSize(bool x, bool y)
        {
            useNativeSizeX = x;
            useNativeSizeY = y;
            SetDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _image = GetComponent<Image>();
            _image.RegisterDirtyVerticesCallback(OnImageVerticesDirty);
            SetDirty();
        }

        protected override void OnDisable()
        {
            _image.UnregisterDirtyVerticesCallback(OnImageVerticesDirty);
            _tracker.Clear();
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
            base.OnDisable();
        }

        public void SetLayoutHorizontal()
        {
            var rect = _image.rectTransform;
            _tracker.Clear();
            _tracker.Add(this, rect, DrivenProperties);

            var sprite = _image.overrideSprite;
            _appliedSprite = sprite;
            if (sprite == null) return;

            var spritePivot = sprite.pivot / sprite.rect.size;
            var pivot = rect.pivot;
            if (useSpritePivotX) pivot.x = spritePivot.x;
            if (useSpritePivotY) pivot.y = spritePivot.y;
            rect.pivot = pivot;

            var nativeSize = Vector2.Scale(sprite.rect.size / _image.pixelsPerUnit, nativeSizeMultiplier);
            if (useNativeSizeX) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, nativeSize.x);
            if (useNativeSizeY) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, nativeSize.y);
        }

        public void SetLayoutVertical() { }

        //? Image never dirties layout for animated sprites or same-size swaps, but always dirties vertices.
        private void OnImageVerticesDirty()
        {
            if (_image.overrideSprite != _appliedSprite) SetDirty();
        }

        private void SetDirty()
        {
            if (!IsActive()) return;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetDirty();
        }
#endif
    }
}

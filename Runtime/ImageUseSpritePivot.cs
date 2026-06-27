using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(Image))]
public class ImageUseSpritePivot : MonoBehaviour
{
    Image img;

    void Awake()
    {
        img = GetComponent<Image>();
    }

    void LateUpdate()
    {
#if UNITY_EDITOR
            if (!Application.isPlaying && img == null)
                img = GetComponent<Image>();
#endif
        if (img.sprite == null)
            return;

        Vector2 transformedPivot = img.sprite.pivot / img.sprite.textureRect.size;
        if (transformedPivot != img.rectTransform.pivot)
            img.rectTransform.pivot = transformedPivot;
    }
}
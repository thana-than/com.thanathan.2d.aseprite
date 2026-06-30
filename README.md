# 2D Aseprite Importer (Than Fork)

Fork of Unity's `com.unity.2d.aseprite` from upstream 3.0.2.

---

## Added Features

### Per-Layer Animators
Generates a separate AnimatorController per layer instead of one shared controller on the root.

---

### Animation Image Target (UI Mode)
Generates the prefab hierarchy using `Image` components and `RectTransform` instead of `SpriteRenderer`. Intended for UI canvases.

---

### UI Components
Adds `ContentSizeFitter` and `ImageUseSpritePivot` to each layer GameObject. Only available in UI mode.

---

### Sprite Atlas Generation
Packs all sprites into a `SpriteAtlas` asset on import.

---

### Layer Name Tags (Shallow Merge mode)

Tags placed in Aseprite layer names control how groups are imported. Tags are case-insensitive.

#### `#EXPAND` / `#EXP`
By default, Shallow Merge collapses every group into a single merged sprite. Tagging a group with `#EXPAND` or `#EXP` opts it out of merging. Its direct children are each processed individually using the same rules: normal layers pass through, untagged sub-groups merge, tagged sub-groups expand further.

The tag is stripped from the layer name in the output.

```
Characters #EXPAND      <- expands
  Head                  <- passes through as individual sprite
  Body                  <- no tag, merges into one sprite
    Torso
    Legs
```

**Requires:** Layer Import Mode set to Shallow Merge.

---

### Preserve Group Hierarchy
When using `#EXPAND` tags, this toggle makes expanded groups appear as empty transforms in the generated prefab. Without it, expanded children are placed flat under the root.

Animation clip paths automatically reflect the hierarchy when using a single shared animator.

**Requires:** Layer Import Mode set to Shallow Merge.

---

## Layer Tag Reference

| Tag | Shorthand | Applies To | Effect |
|-----|-----------|------------|--------|
| `#EXPAND` | `#EXP` | Groups (Shallow Merge) | Descend into group instead of merging |

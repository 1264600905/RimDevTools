# Flamethrower ImageGen Prompt

## Reference images

- **Image 1 — style reference**
  `About/Preview.png`
- **Image 2 — primary mechanical-design reference**
  `参考文档/QQ20260817-131940.png`

Only two reference images are passed to the image generation API. Image 1 controls the visual style. Image 2 controls the weapon structure and mechanical design.

## Prompt

```text
Image 1 is the visual-style reference:
D:/RimworldProject/环世界日志同步mod/About/Preview.png

Image 2 is the primary mechanical-design reference:
D:/RimworldProject/环世界日志同步mod/参考文档/QQ20260817-131940.png

Use Image 2 as the main blueprint for the weapon's structure, proportions, fuel system, stock, grip, receiver, hose, and mechanical layout. Use Image 1 only for the visual language: dark industrial RimWorld mod asset style, strong near-black outlines, compact readable silhouettes, restrained colors, and clean game-asset presentation.

Generate one new standalone industrial flamethrower weapon asset. It must clearly read as a flamethrower rather than an ordinary rifle. Include a visible fuel canister mounted below or beside the main body, a pressure hose, a valve or regulator, and a distinct burner/nozzle assembly. Preserve the recognizable long industrial structure from Image 2, but create a clean new design instead of copying either image pixel-for-pixel.

Visual style:
- clean 2D Adobe Illustrator-like vector illustration
- strong, consistent near-black contour outlines
- crisp geometric shapes
- flat controlled color fills
- hard-edged color separation
- dark charcoal-gray and muted gunmetal body
- restrained gray and brown mechanical parts
- limited muted yellow utility accents on selected fittings, bands, or warning details
- polished RimWorld mod game-asset appearance
- readable silhouette at small size
- simple but believable mechanical construction
- moderate detail, no unnecessary micro-detail

Lighting and rendering restrictions:
- no directional lighting
- no dramatic lighting
- no cast shadows
- no ambient occlusion
- no soft shadows
- no bloom
- no glow
- no glossy specular reflections
- no metallic environment reflections
- no lens effects
- no photorealistic rendering
- no gradient shading
- do not use light and shadow to create depth
- use only flat fills and a small number of neutral material tones to separate components

View and composition:
- orthographic 2D presentation
- no perspective convergence
- no vanishing point
- no forced 45-degree orientation
- orientation is free; choose the most natural and readable presentation
- show the complete weapon in one coherent view
- center the weapon on the canvas
- keep generous margins around every part
- do not crop the muzzle, stock, fuel canister, hose, or grip
- one weapon only
- no exploded view
- no multiple weapons or alternate versions in one image

Background:
- use a perfectly flat, highly saturated chroma-green background, RGB #00FF00
- no checkerboard pattern
- no texture
- no gradient
- no floor
- no ground shadow
- no environment
- the green background is only a temporary keying background and must not be part of the weapon artwork

Reference restrictions:
- do not reproduce Image 2's gray background
- do not reproduce any watermark, website mark, text, or logo from Image 2
- do not turn the result into a normal rifle
- do not add random futuristic components unrelated to the reference design
- do not add flame, smoke, muzzle flash, fire, or glowing effects

Output requirements:
- square 1024x1024 image
- one isolated flamethrower
- clean hard edges
- consistent bold outlines
- no text
- no logo
- no watermark
- no UI
- no character
- no hand
- no ammunition
- no extra objects
- raster PNG with the appearance of a polished 2D vector game asset
```

## Recommended API parameters

```json
{
  "n": 1,
  "size": "1024x1024",
  "quality": "high",
  "image": [
    "D:/RimworldProject/环世界日志同步mod/About/Preview.png",
    "D:/RimworldProject/环世界日志同步mod/参考文档/QQ20260817-131940.png"
  ]
}
```

## Included result

`relayrax-image-20260817-053736.png` is the online generation result used as a quality and prompt-analysis reference. It is stored here as a project asset; it is not passed as a third input reference when generating new images.

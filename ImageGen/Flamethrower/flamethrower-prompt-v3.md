# Flamethrower ImageGen Prompt V3

## Reference roles

Image 1 is the STYLE REFERENCE ONLY:

```text
D:/RimworldProject/环世界日志同步mod/About/Preview.png
```

Image 2 is the CONTENT AND GEOMETRY REFERENCE ONLY:

```text
D:/RimworldProject/环世界日志同步mod/参考文档/QQ20260817-131940.png
```

Use Image 1 only to determine the visual language, outline treatment, flat-color organization, and overall RimWorld game-asset presentation.

Use Image 2 only to determine the subject's silhouette, proportions, component placement, mechanical relationships, and design structure.

From both images, ignore all surface dirt, wear, damage, weathering, lighting, shading, reflections, textures, backgrounds, watermarks, text, logos, and transparency previews.

Extract clean geometry only. Do not copy either reference image pixel-for-pixel.

## Style lock

Create a clean flat 2D vector-style game asset rendered as a PNG.

The visual language should resemble a restrained RimWorld-style Adobe Illustrator vector asset:

- bold, consistent near-black contour outlines
- clean closed geometric shapes
- flat solid color fills
- hard-edged boundaries between adjacent color blocks
- simple readable silhouette
- clear mechanical construction
- polished game-asset presentation
- moderate detail
- no unnecessary micro-details
- no photorealism
- no painterly rendering
- no 3D render appearance
- no realistic material simulation
- no visual noise
- no surface texture

## Strict flat-color rules

- every panel and mechanical component must use a solid single-color fill
- a component may use one base color plus separate adjacent flat-color panels
- use outlines and neighboring color blocks to describe structure
- never use gradients
- never use smooth tonal transitions
- never use internal light-to-dark blending
- never use soft shading
- never use painted highlights
- never use painted shadows
- never use cast shadows
- never use contact shadows
- never use ambient occlusion
- never use bevel shading
- never use rim lighting
- never use gloss
- never use specular reflections
- never use metallic reflections
- never use bloom
- never use glow
- never use volumetric lighting
- never use environmental lighting
- never use light direction to create depth
- never use shadow direction to create depth
- do not simulate volume with brightness changes
- use only flat geometric shapes and solid color blocks

## Clean surface rules

The generated asset must look like a clean, newly manufactured game asset.

Do not reproduce or invent any of the following:

- dirt
- grime
- grease
- oil stains
- dust
- mud
- soot
- rust
- oxidation
- corrosion
- scratches
- dents
- cracks
- chipped paint
- peeled paint
- abrasions
- battle damage
- broken parts
- bent parts
- missing parts
- scorch marks
- burn marks
- blood
- stains
- camouflage wear
- random surface marks
- dirty decals
- damaged warning labels
- distressed textures
- procedural grunge
- rough weathering
- damaged edges

If either reference image contains dirt, damage, scratches, rust, worn paint, stains, or weathering, ignore them completely and replace them with clean flat color fills and clean geometric edges.

Do not use damage, dirt, or surface wear to create depth or visual detail.

## Color rules

- use a restrained industrial palette
- use charcoal gray, dark gunmetal, muted gray, dark brown, and near-black
- use a small amount of muted yellow or ochre only for selected fittings, bands, connectors, or accents
- do not inherit the palette from Image 2
- do not inherit dirty or damaged color variations from either image
- do not use saturated red unless explicitly requested
- do not use neon colors
- do not use rainbow colors
- do not use glossy or luminous colors
- do not introduce unnecessary color variation
- every accent must be a separate clean flat-color shape

## Content module — industrial flamethrower

Subject:
A standalone clean industrial flamethrower weapon.

Primary design:
Use Image 2 as the main geometric and mechanical reference for the long industrial body, receiver, stock, grips, fuel system, hose routing, fittings, and overall proportions.

Required flamethrower features:

- a clearly visible fuel canister mounted below or beside the main body
- a visible pressure hose connecting the fuel system to the weapon
- a clear pressure valve, regulator, or control fitting
- a distinct industrial burner nozzle at the front
- the burner nozzle must look like a flamethrower combustion head, not a rifle muzzle, suppressor, or conventional firearm barrel
- the fuel canister and pressure system must be primary structural elements
- the weapon must be recognizable as a flamethrower without flame effects
- the design must be mechanically believable
- all components must be clean, intact, and newly manufactured
- no damaged or worn parts

Avoid:

- an ordinary assault rifle with a random tank attached
- conventional ammunition magazines as the main fuel system
- ordinary rifle muzzle geometry
- random futuristic components
- unnecessary rails, scopes, optics, or rifle accessories
- fire
- flame
- smoke
- muzzle flash
- sparks
- heat haze
- glowing effects
- dirt
- rust
- scratches
- dents
- cracks
- chipped paint
- stains
- wear
- damage
- text
- logos
- symbols
- warning labels
- UI
- characters
- hands
- ammunition
- extra props

## Composition and view

- orthographic 2D presentation
- no perspective convergence
- no vanishing point
- no forced 45-degree orientation
- no required horizontal or diagonal direction
- choose the most natural and readable orientation
- show the complete object in one coherent view
- center the object on the canvas
- keep generous margins around every part
- do not crop the muzzle, stock, fuel canister, hose, grip, or nozzle
- one object only
- no exploded view
- no multiple versions
- no alternate objects
- no environmental scene

## Background and output

Use a perfectly flat chroma-green background for local background removal:

- RGB #00FF00
- no checkerboard pattern
- no gray background
- no texture
- no gradient
- no floor
- no ground shadow
- no environment
- no background objects
- the green background is only a temporary keying background
- the green color must not appear on the weapon itself

Output requirements:

- one isolated flamethrower
- square 1024x1024 canvas
- PNG output
- flat-color vector appearance
- clean solid color blocks
- clean intact surfaces
- crisp outlines
- no gradients
- no shadows
- no highlights
- no lighting effects
- no dirt
- no damage
- no weathering
- no texture
- no text
- no logo
- no watermark
- no UI
- no character
- no hand
- no ammunition
- no extra objects
- normal raster anti-aliasing at the outer contour is acceptable
- no blurred or feathered internal boundaries

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

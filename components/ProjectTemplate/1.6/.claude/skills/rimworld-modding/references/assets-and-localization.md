# Assets And Localization

## Textures

Store RimWorld textures under project content selected by `LoadFolders.xml`, normally `Textures`. Refer to them with `texPath` relative to that root and without `.png`.

Use transparent PNGs and stable power-of-two dimensions when practical. Match source art dimensions to the intended draw size rather than assuming one pixel size fits all Things. Verify rotation, atlas, apparel/body, mask, and shader requirements against a working 1.6 peer.

Do not invent `_north/_south/_east` naming or mask-channel rules for a graphic class without checking its loader/peer assets. Use DecompilerServer for custom graphic behavior and the Unity project for asset bundles.

## Sounds

Place audio under the loaded `Sounds` hierarchy and define `SoundDef` entries as needed. Prefer formats supported by the current game/Unity player and verify load errors at runtime. Balance volume and pitch ranges against comparable game sounds.

## Asset Bundles And Shaders

Use `UnityProjects/ShaderBundleBuilder` as the editable Unity source and `AssetBundles` as deployment output. Keep `mod_config.jsonc` shader settings aligned with the Unity build method and expected bundle name. When shader building is enabled, a failed/missing bundle blocks deployment.

Test the actual bundle on the target RimWorld/Unity version. A shader compiling in the editor does not prove it loads in game.

## Localization

Use `Languages/<Language>/Keyed/*.xml` for keyed UI text. Keep the same key set across English, Chinese Simplified, and Chinese Traditional where those languages are supported. Reuse established translations and avoid embedding user-facing prose in C#.

Parse language XML, check duplicate/missing keys, and attribute translation warnings before dismissing them. A warning that happens to resemble ambient game noise can still originate from this mod.

## Preview And Workshop Media

Keep `About/Preview.png` inspectable and representative of the actual mod. The packaged CLI currently has no preview renderer command; do not promise an unavailable script. Use a real prepared/generated bitmap and validate its dimensions/visibility before release.

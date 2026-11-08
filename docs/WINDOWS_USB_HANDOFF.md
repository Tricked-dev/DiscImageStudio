# DiscImageStudio — Windows handoff

## Start here

This USB contains a portable Windows x64 build of DiscImageStudio, source artwork, editable patterns, real burned-disc examples, and calibration tools. It is for an existing Windows PC; it does not install Windows. Double-click `DiscImageStudio.exe`. The .NET runtime is included. Application settings and temporary files may still be stored on the PC.

On the new PC, give Codex this folder and ask it:

> Read START-HERE.md and BUILD-INFO.md. Help me identify this PC's optical drive and the inserted media, then complete a fresh calibration. Calibration comes first. After we inspect the calibrated result, help me choose artwork from Artwork/index.html and prepare the next burn. Do not start any disc write merely because you opened this folder; confirm the selected target and media with me first. Skip read-back verification for pattern-only discs.

No SSH connection is needed. This is a fresh setup on this PC. Record its own drive, media and calibration parameters.

## What is on the USB

- `DiscImageStudio.exe`: Windows WPF interface with native Windows burning support.
- `tools/cli/disc-image-studio.exe`: portable command-line encoder for Codex and scripts.
- `Artwork/index.html`: searchable gallery of 80 designs, with original source files and editable SVGs.
- `Artwork/CATALOG.md` and `Artwork/manifest.json`: numbered choices, source URLs, attribution and checksums.
- `Artwork/community-examples/index.html`: actual burned-disc photos and available source images. Only explicitly identified source/result pairs should be treated as matched pairs.
- `Calibration/Templates/`: coded target PNG/JSON source template. No disc image is prebuilt.
- `tools/disc-calib/`: experimental photo-calibration Python source from PR #62.
- `Documentation/`: calibration instructions, worked examples and project documentation.
- `Source/DiscImageStudio-source.zip`: the source revision used for the build.
- `BUILD-INFO.md`: exact commits, included PRs, validation results and limitations.

## Plan: calibrate this setup first

1. Launch the app and select English if needed. Identify the actual optical drive/model/firmware on this PC. Inspect the inserted disc's type, capacity and available write speeds. Keep notes in `Calibration/SESSION.md`.
2. Use the preset matching the actual media as an initial estimate. The purchased discs are Verbatim 43533 AZO DVD-R, but inspect the disc actually inserted. Read capacity from the drive; do not assume a sector count from another setup. Use a fresh blank DVD-R for each new write.
3. Open **Assisted calibration**, choose **DVD**, and start with coarse calibration unless there is a reliable measured starting point. Save the generated `.calibration.json` before writing. Keep that record paired with this exact target and its photos. Use the app's native Windows calibration burn workflow.
4. Review the selected recorder, media, target, speed and capacity with the user before starting the calibration burn. A dummy test, if offered by the chosen recorder/tool, is optional and does not establish physical writing quality. Leave pattern-only read-back verification off.
5. Photograph the whole reflective side with the physical outer edge and centre hole visible. Take 2–3 sharp photos with the lamp moved between shots. Retain the original-resolution files in a dated `Calibration/Photos/` folder.
6. Load the matching calibration record and photo. Rectify using the physical disc edge and centre-hole edge, then measure the coarse target. Apply the fitted radii, save the result, and generate a new boundary target if needed. Continue to fine calibration to refine pitch when the coarse/boundary fit supports it. Use each new target's own record and photos.
7. Inspect fit quality and the corrected reference pattern. Save a named preset for this media/drive/settings combination and its measured values. Do not call calibration finished solely because a write completed or a solver returned numbers.

`Documentation/CALIBRATION.md` and `CALIBRATION_TUTORIAL.md` describe the native coarse → boundary → fine workflow and include worked examples. The examples illustrate measurement technique; their fitted values are not this PC's measured calibration. The physical radius of a standard 12 cm disc is 60 mm; this differs from the approximately 58 mm writable-area radius.

## Optional coded photo-calibration route

The Python tool is experimental and separate from the Windows manual-calibration page. The template has nominal 24–58 mm geometry. Generate an image for the measured media capacity, and retain every geometry value used for generation. A PNG alone is not a burnable disc image.

With `uv` installed, run from the USB root:

```powershell
uv run --project .\tools\disc-calib disc-calib pattern --inner-radius-mm 24 --outer-radius-mm 58 --output .\Calibration\target.png
```

Codex can use `tools/cli/disc-image-studio.exe --help` to inspect encoder commands. After writing and photographing the matching coded target, use the Python `code` command's `--help` and pass the exact generated radii, sector count and channel-bit length. Its output is `code.fit.json`. Reject poor or implausible fits; inspect residuals and coverage before applying a profile. Do not mix a coded target with a manual-calibration record.

Python and its numerical/image packages are not embedded in the EXE. `uv run` may download them on first use. The native Windows manual-calibration workflow does not require Python.

## Choose the first artwork after calibration

Browse `Artwork/index.html`, then tell Codex the design number. Suggested starters:

- **060–063:** radial stars; strong shapes and easy-to-inspect geometry.
- **064–066:** concentric rings.
- **070–072:** guilloche mandalas.
- **073:** sunflower pattern.
- **001–048:** Totoro, Spirited Away, Mononoke, Howl, Kiki and Laputa stills. Pick a recognisable silhouette with good contrast.
- **049:** Hokusai's Great Wave; **052:** Moon; **053–055:** Nix logos.

Keep originals untouched. Rasterize SVG choices to a high-resolution PNG for import, crop/position around the centre hole, inspect the tone preview, then save a `.discproj`. Use the measured preset and inspect the generated preview before asking the user to approve the next artwork burn. No artwork ISO needs to be generated until a design is chosen. Colour gratings are experimental; this Windows build does not add the Linux-only colour-grating interface.

## Save the session for continuation

Record drive/firmware, media brand/SKU/MID, capacity, selected speed/mode, build commit, generated target settings, matching records/photos, fitted preset, and chosen artwork number. Keep targets, photos and records together. Copy the USB folder to the Windows PC if preferred; eject the USB safely after copying. Source-image attribution remains in the catalogue and manifests.

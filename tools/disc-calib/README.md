# Experimental photo calibration

The `disc-calib pattern` and `disc-calib code` commands generate coded DVD targets
and attempt to fit photographs of a matching physical target. Use the exact
geometry and sector count saved when generating that target. A result with poor
quality or `actual: null` is not a usable calibration.

## Photographs

- Retain the complete physical disc rim and the physical centre-hole edge, with
  some background around the disc. A reflective hub ring is not the hole edge.
- Use a plain matte background, a steady camera nearly overhead, and sharp focus
  on the recording surface. Avoid digital zoom and clipped highlights.
- For multiple views, keep the disc and camera fixed and move the lamp to expose
  different sections of the pattern. Preserve all original files unchanged.
- Supply original-resolution files, for example in a ZIP. Chat previews can be
  substantially smaller than the originals. Sharpening or upscaling does not
  recover missing code detail.
- Additional views can reduce occlusion; neither an attractive photo nor visible
  rings prove the coded target can be decoded. No extra disc burn is needed for
  another photograph of the same target.

The Windows manual workflow and correct physical-boundary placement are explained
in [CALIBRATION.md](../../docs/CALIBRATION.md).

## Diagnostic multi-photo fusion

```sh
uv run --project tools/disc-calib python -m disc_calib.fusion \
  --photo first.jpg second.jpg third.jpg --output out/fusion
```

This separate experimental command fits the physical outer outline, makes an
**approximate affine** polar projection, removes broad illumination variation,
aligns angular detail with a correlation/uniqueness check, and combines accepted
observations while masking clipped/dark regions and rejecting isolated outliers.
Opposite contrast is allowed during registration; mirrored geometry is not.
Ambiguous alignments are rejected, and regions with no observations remain NaN
with zero support. The first usable frame is the reference; a successful alignment
does not prove the shared texture belongs to the encoded target rather than a
reflection. Physical perspective and lens distortion remain unresolved.

Outputs:

- `evidence.npz`: radius/angle coordinates, normalized contrast and support count.
- `polar-evidence.png`: a diagnostic contrast plot (rows are radii, columns angles).
- `fusion.json`: per-photo outline errors, accepted/rejected alignments and scores.

**This command does not emit a preset or calibration values.** It always reports
`calibrationValid: false` and `actual: null`. A clearer-looking composite must not
be used as a substitute for validated target decoding and a geometry fit. The
original decoder's quality thresholds are unchanged. It performs no disc writes.

## Regression checks

```sh
uv run --project tools/disc-calib python -m unittest discover \
  -s tools/disc-calib/tests -v
```

The outline regression uses known circle geometry at several resolutions and
rejects uniform images. Fusion checks cover rotation/polarity recovery,
unrelated/periodic input rejection, complementary glare masks and missing regions.

On 2026-11-08, the original outline detector failed those synthetic circles at
100, 240 and 460 px radius: it discarded the Gaussian boundary margin inside a
search window narrower than both margins. Sampling context outside the requested
interval fixes that failure. All four 959/1280 px user photos then yielded outer
outline fits with 0.44–0.53 px RMS. This establishes outline recovery only; those
photos still failed the coded target's readability checks. Private photos and
derived evidence remain in ignored local `artifacts/`, outside the source tree.

Ten original 3000×4000 Pixel photographs were also analyzed: all outer outlines
were recovered, but code matches remained 0.517–0.564 (required at least 0.75
and a 0.10 advantage over the mirrored target). Those photos did not yield a
validated calibration. A first-reference affine fusion trial rejected the nine
other original frames as ambiguous or insufficiently correlated; it did not
combine them by force. This is a limitation of this prototype, not evidence that
the media or photographs are defective.

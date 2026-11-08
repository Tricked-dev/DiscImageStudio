"""Experimental, non-generative photo fusion; outputs evidence, never a preset.

Run: python -m disc_calib.fusion --photo A.jpg B.jpg --output DIR
The outline-only affine rectification is approximate. A fused image is not proof
of calibration: target decoding and independent geometry checks are still needed.
"""
from __future__ import annotations

import argparse
import json
import warnings
from pathlib import Path

import cv2
import numpy as np
from scipy.ndimage import gaussian_filter

from .code import load_photo
from .code_real import _ellipse_matrix
from .detect import find_outer_edge
from .trace import sample_polar


def align_polar(reference, moving, valid, reference_valid=None):
    """Rigid circular registration with explicit rejection and contrast polarity."""
    reference_valid = np.isfinite(reference) if reference_valid is None else reference_valid
    valid = valid & np.isfinite(moving)
    a = np.where(reference_valid, reference, 0.0)
    b = np.where(valid, moving, 0.0)
    cross = np.fft.irfft(np.fft.rfft(a, axis=1) * np.conj(np.fft.rfft(b, axis=1)),
                         n=a.shape[1], axis=1).sum(axis=0)
    norm = np.sqrt(np.sum(a * a) * np.sum(b * b))
    score = np.abs(cross) / max(float(norm), 1e-12)
    k = int(np.argmax(score))
    shift = k if k <= a.shape[1] // 2 else k - a.shape[1]
    polarity = 1 if cross[k] >= 0 else -1
    # Broad/periodic peaks do not establish a unique angle.
    distance = np.minimum((np.arange(score.size) - k) % score.size,
                          (k - np.arange(score.size)) % score.size)
    competing = score[distance >= max(2, score.size // 72)]
    gap = float(score[k] - competing.max()) if competing.size else 0.0
    report = dict(accepted=bool(score[k] >= .20 and gap >= .025),
                  correlation=float(score[k]), peakGap=gap,
                  shiftColumns=shift, polarity=polarity)
    return np.roll(moving, shift, axis=1) * polarity, np.roll(valid, shift, axis=1), report


def combine_polar(frames, valid):
    """Robust fusion retaining missing pixels and independently observed support."""
    if not frames or len(frames) != len(valid):
        raise ValueError('Provide equally many nonempty frames and masks.')
    values = np.stack([np.where(mask & np.isfinite(frame), frame, np.nan)
                       for frame, mask in zip(frames, valid)])
    with warnings.catch_warnings():
        warnings.simplefilter('ignore', RuntimeWarning)
        middle = np.nanmedian(values, axis=0)
        spread = np.nanmedian(np.abs(values - middle), axis=0)
    # Reject isolated reflections, while allowing the complementary one-photo case.
    keep = np.isfinite(values) & (np.abs(values - middle) <= np.maximum(3 * 1.4826 * spread, .25))
    support = keep.sum(axis=0)
    fused = np.full(middle.shape, np.nan, dtype=np.float32)
    np.divide(np.where(keep, values, 0.0).sum(axis=0), support, out=fused, where=support > 0)
    return fused, support


def prepare_frame(path, radius, theta):
    bgr = load_photo(path)
    outer = find_outer_edge(bgr)
    if outer.max_gap_deg >= 60 or outer.rms >= 1:
        raise ValueError('Outer outline has insufficient coverage or precision.')
    matrix = _ellipse_matrix(outer.ellipse, (0.0, 0.0))
    gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY).astype(np.float32)
    polar = sample_polar(gray, matrix, radius, theta)
    high = sample_polar(bgr.max(axis=2).astype(np.float32), matrix, radius, theta)
    log = np.log(np.maximum(polar, 3))
    feature = log - gaussian_filter(log, (12, 20), mode=('nearest', 'wrap'))
    feature /= max(float(np.std(feature)), .01)
    valid = (high < 248) & (polar > 12)
    return feature, valid, dict(path=str(path), model='outer-only affine approximation',
                               outlineRmsPx=outer.rms, outlineGapDeg=outer.max_gap_deg,
                               matrix=matrix.tolist())


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--photo', nargs='+', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(argv)
    args.output.mkdir(parents=True, exist_ok=True)
    radius = np.arange(23.0, 58.5, .1)
    theta = np.radians(np.arange(0, 360, .2))
    prepared, reports = [], []
    for path in args.photo:
        try:
            frame, mask, report = prepare_frame(path, radius, theta)
            prepared.append((frame, mask, report))
        except (ValueError, cv2.error) as error:
            reports.append(dict(path=str(path), accepted=False, reason=str(error)))
    if not prepared:
        raise ValueError('No photos have a usable physical outline.')
    reference, reference_mask, reference_report = prepared[0]
    frames, masks = [reference], [reference_mask]
    reports.append(dict(**reference_report, accepted=True, reference=True))
    for moving, mask, report in prepared[1:]:
        aligned, aligned_mask, alignment = align_polar(reference, moving, mask, reference_mask)
        reports.append(dict(**report, **alignment))
        if alignment['accepted']:
            frames.append(aligned); masks.append(aligned_mask)
    fused, support = combine_polar(frames, masks)
    np.savez_compressed(args.output / 'evidence.npz', radiusMm=radius, thetaRad=theta,
                        contrast=fused, support=support)
    # A diagnostic contrast plot only. NaN regions stay black and have zero support.
    plot = np.where(np.isfinite(fused), np.clip(128 + 45 * fused, 1, 255), 0).astype(np.uint8)
    cv2.imwrite(str(args.output / 'polar-evidence.png'), plot)
    report = dict(calibrationValid=False, actual=None, acceptedPhotos=len(frames), photos=reports,
                  notes=['Diagnostic fusion uses observed photo data only; missing regions remain missing.',
                         'Outline-only affine registration does not establish perspective-correct calibration.',
                         'No calibration values may be inferred merely from clearer-looking rings.'])
    (args.output / 'fusion.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(f'Accepted {len(frames)}/{len(args.photo)} photos; calibration values not inferred.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

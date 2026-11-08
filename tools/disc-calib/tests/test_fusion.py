import unittest
import numpy as np
from disc_calib.fusion import align_polar, combine_polar

class FusionTests(unittest.TestCase):
    def setUp(self):
        rng = np.random.default_rng(26)
        self.pattern = rng.normal(size=(24, 256)).astype(np.float32)

    def test_rotation_and_opposite_contrast(self):
        moved = -np.roll(self.pattern, 37, axis=1)
        aligned, valid, report = align_polar(self.pattern, moved, np.ones_like(moved, bool))
        self.assertTrue(report['accepted'])
        self.assertEqual(report['shiftColumns'], -37)
        self.assertEqual(report['polarity'], -1)
        np.testing.assert_allclose(aligned, self.pattern)

    def test_unrelated_photo_is_rejected(self):
        other = np.random.default_rng(9).normal(size=self.pattern.shape)
        _, _, report = align_polar(self.pattern, other, np.ones_like(other, bool))
        self.assertFalse(report['accepted'])

    def test_repeated_stripes_do_not_establish_unique_registration(self):
        stripes = np.tile(np.sin(np.arange(256) * np.pi / 8), (24, 1))
        _, _, report = align_polar(stripes, np.roll(stripes, 5, axis=1),
                                  np.ones_like(stripes, bool))
        self.assertFalse(report['accepted'])

    def test_complementary_glare_does_not_get_averaged_into_pattern(self):
        first, second = self.pattern.copy(), self.pattern.copy()
        first[:, :90] = 500
        second[:, 100:190] = -500
        a = np.ones_like(first, bool); a[:, :90] = False
        b = np.ones_like(first, bool); b[:, 100:190] = False
        fused, support = combine_polar([first, second], [a, b])
        np.testing.assert_allclose(fused, self.pattern)
        self.assertTrue((support >= 1).all())

    def test_unobserved_region_remains_missing(self):
        mask = np.ones_like(self.pattern, bool); mask[:, 15:30] = False
        fused, support = combine_polar([self.pattern], [mask])
        self.assertTrue(np.isnan(fused[:, 15:30]).all())
        self.assertTrue((support[:, 15:30] == 0).all())

if __name__ == '__main__': unittest.main()

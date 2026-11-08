import unittest
import cv2
import numpy as np
from disc_calib.detect import Ellipse, DetectError, find_outer_edge

class OutlineTests(unittest.TestCase):
    def test_seeded_outline_at_attachment_resolutions(self):
        for radius in (100, 240, 460):
            with self.subTest(radius=radius):
                size = 2 * radius + 80
                image = np.full((size, size, 3), 55, np.uint8)
                center = size // 2
                cv2.circle(image, (center, center), radius, (175, 175, 175), -1, cv2.LINE_AA)
                result = find_outer_edge(image, seed=Ellipse(center, center, radius, radius, 0))
                self.assertLess(abs(result.ellipse.a - radius), 1.5)
                self.assertLess(abs(result.ellipse.b - radius), 1.5)
                self.assertLess(abs(result.ellipse.cx - center), 0.6)
                self.assertLess(abs(result.ellipse.cy - center), 0.6)
                self.assertGreater(result.inlier.sum(), 500)

    def test_uniform_image_does_not_invent_an_outline(self):
        with self.assertRaises(DetectError):
            find_outer_edge(np.full((500, 500, 3), 120, np.uint8),
                            seed=Ellipse(250, 250, 200, 200, 0))

if __name__ == '__main__':
    unittest.main()

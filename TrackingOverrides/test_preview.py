import socket
import unittest

try:
    import cv2
    import numpy as np
except ImportError:
    cv2 = np = None

from bridge import draw_zone_overlay, encode_preview, zone_boundaries


@unittest.skipIf(cv2 is None, "Install Tracking requirements for JPEG tests")
class PreviewTests(unittest.TestCase):
    def test_zone_overlay_draws_two_green_boundaries_and_three_labels(self):
        pixels = np.zeros((240, 320, 3), dtype=np.uint8)
        draw_zone_overlay(pixels, cv2)
        first, second = zone_boundaries(pixels.shape[1])
        for x in (first, second):
            self.assertGreater(np.count_nonzero(pixels[:, x, 1] > 200), 220)
        # Each label makes visible green pixels at the top of its own zone.
        for left, right in ((0, first), (first, second), (second, pixels.shape[1])):
            label_region = pixels[:45, left + 5:right - 5]
            self.assertGreater(np.count_nonzero(label_region[:, :, 1] > 200), 10)

    def test_encoded_preview_mirrors_source_without_moving_zone_labels(self):
        pixels = np.zeros((240, 320, 3), dtype=np.uint8)
        pixels[80:220, 15:80] = (0, 0, 255)
        original = pixels.copy()
        decoded = []
        for mirror in (False, True):
            data = encode_preview(pixels, cv2, mirror=mirror)
            decoded.append(cv2.imdecode(np.frombuffer(data, dtype=np.uint8), cv2.IMREAD_COLOR))
        np.testing.assert_array_equal(pixels, original)
        normal, mirrored = decoded
        self.assertGreater(normal[80:220, 15:80, 2].mean(), 180)
        self.assertLess(normal[80:220, 240:305, 2].mean(), 30)
        self.assertLess(mirrored[80:220, 15:80, 2].mean(), 30)
        self.assertGreater(mirrored[80:220, 240:305, 2].mean(), 180)
        # Labels are rendered after mirroring, so each stays in its own zone.
        first, second = zone_boundaries(normal.shape[1])
        for image in decoded:
            for left, right in ((0, first), (first, second), (second, image.shape[1])):
                label_region = image[:45, left + 5:right - 5]
                self.assertGreater(np.count_nonzero(label_region[:, :, 1] > 100), 4)

    def test_jpeg_fits_datagram_and_preserves_landscape_and_portrait(self):
        for height, width in ((720, 1280), (1280, 720), (240, 320)):
            pixels = np.random.default_rng(42).integers(0, 256, (height, width, 3), dtype=np.uint8)
            data = encode_preview(pixels, cv2)
            self.assertIsNotNone(data)
            self.assertLessEqual(len(data), 8000)
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as receiver, socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
                receiver.bind(('127.0.0.1', 0))
                receiver.settimeout(1)
                sender.sendto(data, receiver.getsockname())
                self.assertEqual(receiver.recvfrom(8192)[0], data)
            decoded = cv2.imdecode(np.frombuffer(data, dtype=np.uint8), cv2.IMREAD_COLOR)
            h, w = decoded.shape[:2]
            self.assertLessEqual(w, 320)
            self.assertLessEqual(h, 240)
            self.assertAlmostEqual(w / h, width / height, delta=.01)


if __name__ == '__main__':
    unittest.main()

import unittest
from pathlib import Path
from types import SimpleNamespace

try:
    import cv2
    import mediapipe as mp
    import numpy as np
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
except ImportError:
    cv2 = mp = np = python = vision = None

from bridge import (draw_gesture_overlay, encode_preview, gesture_from_result,
                    gesture_packet_fields, hand_zone_crop, zone_boundaries)


@unittest.skipIf(np is None, 'NumPy is required')
class GestureHelpersTests(unittest.TestCase):
    def test_crop_uses_displayed_left_zone(self):
        frame = np.zeros((4, 12, 3), dtype=np.uint8)
        frame[:, :4] = 10
        frame[:, 8:] = 20
        self.assertTrue(np.all(hand_zone_crop(frame, False) == 10))
        self.assertTrue(np.all(hand_zone_crop(frame, True) == 20))

    def test_top_result_is_not_replaced_with_a_lower_allowed_result(self):
        unsupported = SimpleNamespace(category_name='Pointing_Up', score=.9)
        allowed = SimpleNamespace(category_name='Thumb_Up', score=.8)
        result = SimpleNamespace(gestures=[[unsupported, allowed]])
        self.assertEqual(gesture_from_result(result), ('None', None))

    def test_no_hand_and_requested_top_gesture(self):
        self.assertEqual(gesture_from_result(SimpleNamespace(gestures=[])), ('None', None))
        top = SimpleNamespace(category_name='Victory', score=.75)
        self.assertEqual(gesture_from_result(SimpleNamespace(gestures=[[top]])), ('Victory', .75))
        none = SimpleNamespace(category_name='None', score=.8)
        self.assertEqual(gesture_from_result(SimpleNamespace(gestures=[[none]])), ('None', .8))

    def test_packet_always_neutralizes_unavailable_or_invalid_status(self):
        self.assertEqual(gesture_packet_fields(('Unavailable', None)), ('None', 0.0))
        self.assertEqual(gesture_packet_fields(('Thumb_Up', float('nan'))), ('None', 0.0))
        self.assertEqual(gesture_packet_fields(('Victory', 2)), ('Victory', 1.0))

    @unittest.skipIf(cv2 is None or np is None, 'OpenCV and NumPy are required')
    def test_overlay_stays_inside_hand_zone(self):
        frame = np.zeros((240, 320, 3), dtype=np.uint8)
        draw_gesture_overlay(frame, cv2, 'Closed_Fist', .99)
        first, _ = zone_boundaries(frame.shape[1])
        self.assertGreater(np.count_nonzero(frame[:, :first]), 20)
        self.assertEqual(np.count_nonzero(frame[:, first:]), 0)

    def test_unavailable_overlay_has_no_confidence_and_encodes(self):
        frame = np.zeros((240, 320, 3), dtype=np.uint8)
        draw_gesture_overlay(frame, cv2, 'Unavailable: model missing', None)
        first, _ = zone_boundaries(frame.shape[1])
        self.assertGreater(np.count_nonzero(frame[:, :first]), 20)
        self.assertEqual(np.count_nonzero(frame[:, first:]), 0)
        jpeg = encode_preview(frame, cv2, gesture='Unavailable: model missing')
        self.assertIsNotNone(cv2.imdecode(np.frombuffer(jpeg, dtype=np.uint8), cv2.IMREAD_COLOR))


@unittest.skipIf(any(item is None for item in (cv2, mp, np, python, vision)),
                 'MediaPipe Tasks dependencies are required')
class GestureRecognizerIntegrationTests(unittest.TestCase):
    model = Path(__file__).parent / 'gesture_recognizer.task'
    sample = Path(__file__).parent / '.test-fixtures' / 'thumbs_up.jpg'

    def test_real_model_detects_thumb_and_blank_returns_none(self):
        if not self.model.exists() or not self.sample.exists():
            self.skipTest('Run the documented fixture download before this integration test')
        image = cv2.imread(str(self.sample))
        self.assertIsNotNone(image)
        rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB).copy()
        blank = np.zeros_like(rgb)
        options = vision.GestureRecognizerOptions(
            base_options=python.BaseOptions(model_asset_path=str(self.model)),
            running_mode=vision.RunningMode.VIDEO, num_hands=1,
            min_hand_detection_confidence=.5, min_hand_presence_confidence=.5,
            min_tracking_confidence=.5)
        with vision.GestureRecognizer.create_from_options(options) as recognizer:
            thumb = recognizer.recognize_for_video(
                mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), 1)
            no_hand = recognizer.recognize_for_video(
                mp.Image(image_format=mp.ImageFormat.SRGB, data=blank), 2)
        label, score = gesture_from_result(thumb)
        self.assertEqual(label, 'Thumb_Up')
        self.assertGreater(score, .5)
        self.assertEqual(gesture_from_result(no_hand), ('None', None))


if __name__ == '__main__':
    unittest.main()

"""DigiPhant camera -> local Unity UDP bridge. Run with --help for setup options."""
import argparse
from contextlib import ExitStack
import errno
import itertools
import json
import math
from pathlib import Path
import socket
import sys
import time

FEATURES = ('LeftHandHeight', 'RightHandHeight', 'LeftFootLift', 'RightFootLift', 'Lean', 'ArmSpread')
MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task'
SUPPORTED_GESTURES = frozenset(('Closed_Fist', 'Thumb_Up', 'Thumb_Down', 'Victory', 'Open_Palm', 'None'))


def extract_features(landmarks, upper_body_only=False):
    """Body-relative image measurements; Unity subtracts the calibrated neutral pose."""
    def xy(i):
        p = landmarks[i]
        return p.x, p.y
    def quality(indices):
        return min(min(landmarks[i].visibility, landmarks[i].presence) for i in indices)
    if upper_body_only:
        ls, rs = xy(11), xy(12)
        shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
        scale = max(math.dist(ls, rs), .03)
        left = (shoulder[1] - xy(15)[1]) / scale
        right = (shoulder[1] - xy(16)[1]) / scale
        # Shoulder tilt replaces hip-relative lean; leg channels use hand heights.
        raw = [left, right, left, right, (ls[1] - rs[1]) / scale,
               abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
        groups = [[11, 12, 15], [11, 12, 16], [11, 12, 15],
                  [11, 12, 16], [11, 12], [11, 12, 15, 16]]
        return shoulder, [max(-3, min(3, v)) for v in raw], [quality(g) for g in groups]
    ls, rs, lh, rh = (xy(i) for i in (11, 12, 23, 24))
    shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
    hip = ((lh[0] + rh[0]) / 2, (lh[1] + rh[1]) / 2)
    scale = max(math.dist(shoulder, hip), .03)
    base = [11, 12, 23, 24]
    groups = [base + [15], base + [16], base + [27], base + [28], base, base + [15, 16]]
    raw = [(shoulder[1] - xy(15)[1]) / scale,
           (shoulder[1] - xy(16)[1]) / scale,
           (hip[1] - xy(27)[1]) / scale,
           (hip[1] - xy(28)[1]) / scale,
           (shoulder[0] - hip[0]) / scale,
           abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
    return hip, [max(-3, min(3, v)) for v in raw], [quality(g) for g in groups]


class PerformerTracker:
    """Small-group nearest-position assignment with ambiguity rejection.

    This is not biometric identity. Crossings/long occlusions may need manual reset.
    """
    def __init__(self, count, max_distance=.20):
        if count not in (1, 2, 3, 4):
            raise ValueError("Choose 1, 2, 3, or 4 people")
        self.count = count
        self.max_distance = max_distance
        self.positions = None

    def reset(self):
        self.positions = None

    def assign(self, centers):
        if self.positions is None:
            if len(centers) < self.count:
                return {}
            order = sorted(range(len(centers)), key=lambda i: centers[i][0])[:self.count]
            self.positions = [centers[i] for i in order]
            return {slot + 1: index for slot, index in enumerate(order)}
        candidates = []
        for assignment in itertools.product(range(-1, len(centers)), repeat=self.count):
            used = [i for i in assignment if i >= 0]
            if len(set(used)) != len(used):
                continue
            distances = [math.dist(self.positions[s], centers[i]) for s, i in enumerate(assignment) if i >= 0]
            if any(d > self.max_distance for d in distances):
                continue
            candidates.append((-len(used), sum(distances), assignment))
        candidates.sort()
        best = candidates[0]
        # Avoid arbitrary role swaps when two assignments are almost equally plausible.
        if len(candidates) > 1 and candidates[1][0] == best[0] and candidates[1][1] - best[1] < .015:
            return {}
        result = {s + 1: i for s, i in enumerate(best[2]) if i >= 0}
        for slot, index in result.items():
            self.positions[slot - 1] = centers[index]
        return result


def requested_count(data):
    """Validate Unity's local count request without changing tracking on bad input."""
    try:
        value = json.loads(data)
        if isinstance(value, dict) and value.get('version') == 1:
            count = value.get('performerCount')
            if type(count) is int and 1 <= count <= 4:
                return count
    except (ValueError, UnicodeError):
        pass
    return None


def zone_boundaries(width):
    """Return the two pixel columns that divide a frame into three equal zones."""
    return round(width / 3), round(width * 2 / 3)


def hand_zone_crop(frame, mirror_preview):
    """Return the clean camera crop that appears in the displayed HAND ZONE."""
    width = frame.shape[1]
    first, _ = zone_boundaries(width)
    # The displayed left third is the raw right third when the preview is mirrored.
    return frame[:, width - first:] if mirror_preview else frame[:, :first]


def gesture_from_result(result):
    """Return the requested top-1 label and its score, or None when no hand exists."""
    if not result.gestures or not result.gestures[0]:
        return 'None', None
    top = result.gestures[0][0]
    label = top.category_name
    score = top.score
    # Do not promote a lower ranked supported label: this is deliberately top-1.
    # An unrequested top label is displayed the same as no requested gesture.
    return (label, score) if label in SUPPORTED_GESTURES else ('None', None)


def gesture_packet_fields(status):
    """Return a finite, supported protocol value even when recognition failed."""
    gesture, score = status
    if gesture not in SUPPORTED_GESTURES or type(score) not in (int, float) or not math.isfinite(score):
        return 'None', 0.0
    return gesture, max(0.0, min(1.0, score))


def draw_zone_overlay(frame, cv2):
    """Draw the hand and pose guide zones on a display-only video frame."""
    height, width = frame.shape[:2]
    if width < 3 or height < 2:
        return frame

    first, second = zone_boundaries(width)
    green = (0, 255, 0)
    for x in (first, second):
        cv2.line(frame, (x, 0), (x, height - 1), green, 2, cv2.LINE_AA)

    zones = ((0, first, 'HAND ZONE'), (first, second, 'POSE 1'), (second, width, 'POSE 2'))
    for left, right, label in zones:
        zone_width = max(1, right - left)
        font_scale = min(.55, max(.25, width / 700))
        text_thickness = 1
        while font_scale > .15:
            (text_width, text_height), _ = cv2.getTextSize(
                label, cv2.FONT_HERSHEY_SIMPLEX, font_scale, text_thickness)
            if text_width <= zone_width - 6:
                break
            font_scale -= .05
        x = left + max(3, (zone_width - text_width) // 2)
        y = max(text_height + 4, 14)
        cv2.putText(frame, label, (x, y), cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                    (0, 0, 0), text_thickness + 2, cv2.LINE_AA)
        cv2.putText(frame, label, (x, y), cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                    green, text_thickness, cv2.LINE_AA)
    return frame


def draw_gesture_overlay(frame, cv2, gesture, score):
    """Draw the current hand-zone result after the preview is resized and labelled."""
    height, width = frame.shape[:2]
    first, _ = zone_boundaries(width)
    if first < 12 or height < 30:
        return frame
    if score is None:
        lines = ['Gesture: None'] if gesture == 'None' else ['Gesture:', gesture]
    else:
        lines = ['Gesture:', gesture, f'Confidence: {score:.0%}']
    font = cv2.FONT_HERSHEY_SIMPLEX
    scale = min(.42, max(.19, first / 245))
    thickness = 1
    line_height = max(11, round(16 * scale / .42))
    panel_top = 19
    panel_bottom = min(height - 2, panel_top + line_height * len(lines) + 7)
    cv2.rectangle(frame, (2, panel_top), (max(3, first - 3), panel_bottom), (0, 0, 0), -1)
    for index, line in enumerate(lines):
        (text_width, _), _ = cv2.getTextSize(line, font, scale, thickness)
        # Keep long built-in labels legible instead of letting them overlap POSE 1.
        if text_width > first - 8:
            scale_for_line = max(.14, scale * (first - 8) / text_width)
        else:
            scale_for_line = scale
        y = panel_top + line_height * (index + 1)
        cv2.putText(frame, line, (4, y), font, scale_for_line, (80, 255, 80), thickness, cv2.LINE_AA)
    return frame


def encode_preview(frame, cv2, mirror=False, gesture='None', gesture_score=None):
    """Fit and label one low-latency JPEG for Unity's local UDP preview."""
    height, width = frame.shape[:2]
    scale = min(320 / width, 240 / height)
    preview = cv2.resize(frame, (max(1, round(width * scale)), max(1, round(height * scale))))
    # Stay below macOS's commonly configured 9216-byte UDP send limit.
    # JPEG size depends on scene detail, so reduce quality and then dimensions.
    for _ in range(4):
        display = cv2.flip(preview, 1) if mirror else preview.copy()
        draw_zone_overlay(display, cv2)
        draw_gesture_overlay(display, cv2, gesture, gesture_score)
        for quality in (65, 45, 25):
            ok, encoded = cv2.imencode('.jpg', display, [cv2.IMWRITE_JPEG_QUALITY, quality])
            if ok and len(encoded) <= 8000:
                return encoded.tobytes()
        h, w = preview.shape[:2]
        preview = cv2.resize(preview, (max(1, w // 2), max(1, h // 2)))
    return None


def receive_datagrams(sender, limit=32):
    """Read local UDP commands without treating Windows ICMP resets as fatal."""
    for _ in range(limit):
        try:
            yield sender.recvfrom(1024)
        except BlockingIOError:
            return
        except ConnectionResetError:
            # Unity can bind its base port after the preview starts.
            continue


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--people', type=int, choices=(1, 2, 3, 4), default=3)
    parser.add_argument('--no-window', action='store_true', help='Preview is displayed inside Unity instead')
    parser.add_argument('--mirror-preview', action='store_true', help="Mirror Unity's display-only camera preview")
    parser.add_argument('--upper-body-only', action='store_true', help='Use shoulders and hands; feet and hips may be out of frame')
    parser.add_argument('--camera', type=int, default=0)
    parser.add_argument('--port', type=int, default=5055)
    parser.add_argument('--model', type=Path, default=Path(__file__).parent / 'pose_landmarker_full.task')
    parser.add_argument('--gesture-model', type=Path,
                        default=Path(__file__).parent / 'gesture_recognizer.task',
                        help='MediaPipe gesture recognizer model for the displayed HAND ZONE')
    parser.add_argument('--disable-gestures', action='store_true', help='Disable left-hand-zone gesture recognition')
    parser.add_argument('--download-model', action='store_true', help='Download the official model if missing')
    args = parser.parse_args()
    if not 1024 <= args.port <= 65533:
        parser.error('--port must be between 1024 and 65533')
    if not args.model.exists():
        if not args.download_model:
            parser.error('Model missing. Run again with --download-model (internet required).')
        import urllib.request
        args.model.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.model.with_suffix('.download')
        print('Downloading the official MediaPipe pose model...')
        urllib.request.urlretrieve(MODEL_URL, temporary)
        temporary.replace(args.model)
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision

    options = vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(args.model)),
        running_mode=vision.RunningMode.VIDEO, num_poses=4,
        min_pose_detection_confidence=.5, min_pose_presence_confidence=.5,
        min_tracking_confidence=.5)
    gesture_options = None
    gesture_status = ('Unavailable: model missing', None)
    if args.disable_gestures:
        gesture_status = ('Unavailable: disabled', None)
    elif not args.gesture_model.exists():
        print(f'Gesture recognition unavailable: model missing at {args.gesture_model}')
    else:
        try:
            gesture_options = vision.GestureRecognizerOptions(
                base_options=python.BaseOptions(model_asset_path=str(args.gesture_model)),
                running_mode=vision.RunningMode.VIDEO, num_hands=1,
                min_hand_detection_confidence=.5, min_hand_presence_confidence=.5,
                min_tracking_confidence=.5)
        except Exception as error:
            print(f'Gesture recognition unavailable: {error}')
            gesture_status = ('Unavailable', None)
    tracker = PerformerTracker(args.people)
    # DirectShow is reliable for this Windows integrated camera; CAP_ANY stays
    # the portable default on other operating systems.
    camera = cv2.VideoCapture(args.camera, cv2.CAP_DSHOW if sys.platform == 'win32' else cv2.CAP_ANY)
    if not camera.isOpened():
        raise SystemExit('Camera could not open. Check permissions or try --camera 1.')
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sender.bind(('127.0.0.1', args.port + 1))
        sender.setblocking(False)
    except OSError:
        camera.release()
        sender.close()
        raise SystemExit('Another camera bridge is already running. Close its preview with Q first.')
    edges = [(11, 12), (11, 23), (12, 24), (23, 24), (11, 13), (13, 15),
             (12, 14), (14, 16), (23, 25), (25, 27), (24, 26), (26, 28),
             (27, 29), (29, 31), (27, 31), (28, 30), (30, 32), (28, 32),
             (0, 7), (0, 8), (7, 11), (8, 12)]
    colors = [(100, 230, 100), (255, 180, 80), (100, 160, 255), (220, 100, 230)]
    previous_timestamp = -1
    last_preview = 0.0
    print('Stand side by side, all visible. IDs are assigned left-to-right in raw camera coordinates.')
    print('R: reset identities (then recalibrate in Unity). Q: quit.')
    try:
        with ExitStack() as resources:
            detector = resources.enter_context(vision.PoseLandmarker.create_from_options(options))
            recognizer = None
            if gesture_options is not None:
                try:
                    recognizer = resources.enter_context(
                        vision.GestureRecognizer.create_from_options(gesture_options))
                    gesture_status = ('None', None)
                except Exception as error:
                    print(f'Gesture recognition unavailable: {error}')
                    gesture_status = ('Unavailable: model load failed', None)
            while True:
                for data, address in receive_datagrams(sender):
                    if address == ('127.0.0.1', args.port):
                        try:
                            command = json.loads(data)
                            if isinstance(command, dict) and command.get('version') == 1:
                                upper = command.get('upperBodyOnly')
                                if type(upper) is bool and upper != args.upper_body_only:
                                    args.upper_body_only = upper
                                    tracker.reset()
                                    print('Movement mode changed. Set neutral pose in Unity.')
                                if command.get('reset') is True:
                                    tracker.reset()
                        except (ValueError, UnicodeError):
                            pass
                    count = requested_count(data) if address == ('127.0.0.1', args.port) else None
                    if count is not None and count != args.people:
                        args.people = count
                        tracker = PerformerTracker(count)
                        print(f'Unity selected {count} people. Reassigning; set neutral pose in Unity.')
                ok, frame = camera.read()
                if not ok:
                    raise RuntimeError('Camera stopped delivering frames.')
                timestamp = max(previous_timestamp + 1, time.monotonic_ns() // 1_000_000)
                previous_timestamp = timestamp
                if recognizer is not None:
                    try:
                        # Crop before pose drawing; MediaPipe requires contiguous RGB data.
                        hand_bgr = hand_zone_crop(frame, args.mirror_preview)
                        hand_rgb = cv2.cvtColor(hand_bgr, cv2.COLOR_BGR2RGB)
                        hand_rgb = hand_rgb.copy()
                        gesture_status = gesture_from_result(recognizer.recognize_for_video(
                            mp.Image(image_format=mp.ImageFormat.SRGB, data=hand_rgb), timestamp))
                    except Exception as error:
                        print(f'Gesture recognition failed; pose tracking continues: {error}')
                        recognizer = None
                        gesture_status = ('Unavailable', None)
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                result = detector.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), timestamp)
                observations = []
                for landmarks in result.pose_landmarks:
                    center, values, confidence = extract_features(landmarks, args.upper_body_only)
                    if confidence[4] >= .5:
                        observations.append((center, values, confidence, landmarks))
                assignments = tracker.assign([o[0] for o in observations])
                # Gesture fields are optional in protocol v1.  Always send a neutral
                # value when recognition is disabled or unavailable so Unity cannot
                # retain an old action after a model/camera failure.
                gesture, gesture_score = gesture_packet_fields(gesture_status)
                packet = {'version': 1, 'performerCount': args.people, 'upperBodyOnly': args.upper_body_only,
                          'gesture': gesture, 'gestureScore': gesture_score or 0.0,
                          'people': [dict(slot=slot, values=observations[i][1], confidence=observations[i][2])
                                     for slot, i in assignments.items()]}
                sender.sendto(json.dumps(packet, allow_nan=False).encode(), ('127.0.0.1', args.port))
                h, w = frame.shape[:2]
                # Mirroring is display-only. Tracking and Unity's feature packet keep
                # camera coordinates, while annotations are drawn after the flip.
                display_frame = cv2.flip(frame, 1) if args.mirror_preview else frame
                # Draw every detection, even while waiting for the full group or
                # rejecting an ambiguous identity. Only assigned bodies drive Unity.
                slots = {id(observations[i][3]): slot for slot, i in assignments.items()}
                thickness = max(2, round(max(w / 320, h / 240) * 2))
                radius = max(3, round(thickness * 1.5))
                for landmarks in result.pose_landmarks:
                    slot = slots.get(id(landmarks))
                    color = colors[slot - 1] if slot is not None else (180, 180, 180)
                    points = {}
                    for index, landmark in enumerate(landmarks):
                        if (min(landmark.visibility, landmark.presence) >= .5
                                and math.isfinite(landmark.x) and math.isfinite(landmark.y)
                                and 0 <= landmark.x <= 1 and 0 <= landmark.y <= 1):
                            x = 1 - landmark.x if args.mirror_preview else landmark.x
                            points[index] = (min(w - 1, int(x * w)),
                                             min(h - 1, int(landmark.y * h)))
                    for a, b in edges:
                        if a in points and b in points:
                            cv2.line(display_frame, points[a], points[b], (20, 20, 20), thickness + 2, cv2.LINE_AA)
                            cv2.line(display_frame, points[a], points[b], color, thickness, cv2.LINE_AA)
                    for point in points.values():
                        cv2.circle(display_frame, point, radius + 1, (20, 20, 20), -1, cv2.LINE_AA)
                        cv2.circle(display_frame, point, radius, color, -1, cv2.LINE_AA)
                    anchor = points.get(11, next(iter(points.values()), None))
                    if anchor is not None:
                        label = f'P{slot}' if slot is not None else 'Unassigned'
                        font_scale = max(.6, thickness * .3)
                        cv2.putText(display_frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    (20, 20, 20), thickness + 2, cv2.LINE_AA)
                        cv2.putText(display_frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    color, thickness, cv2.LINE_AA)
                message = f'{len(assignments)}/{args.people} assigned' if args.no_window else f'{len(assignments)}/{args.people} assigned | R: reassign | Q: quit'
                if tracker.positions is None:
                    message = f'Waiting for {args.people} visible people' if args.no_window else f'Stand side by side: waiting for {args.people} visible people | Q: quit'
                cv2.putText(display_frame, message, (12, h - 14), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)
                now = time.monotonic()
                if now - last_preview >= .1:
                    jpeg = encode_preview(display_frame, cv2, gesture=gesture_status[0], gesture_score=gesture_status[1])
                    if jpeg is not None:
                        try:
                            sender.sendto(jpeg, ('127.0.0.1', args.port + 2))
                        except OSError as error:
                            # A dropped preview must not stop performer tracking.
                            if error.errno not in (errno.EMSGSIZE, errno.EAGAIN, errno.EWOULDBLOCK, errno.ENOBUFS):
                                raise
                    last_preview = now
                if not args.no_window:
                    draw_zone_overlay(display_frame, cv2)
                    draw_gesture_overlay(display_frame, cv2, gesture_status[0], gesture_status[1])
                    title = 'DigiPhant tracking (mirrored)' if args.mirror_preview else 'DigiPhant tracking (unmirrored)'
                    cv2.imshow(title, display_frame)
                key = (cv2.waitKey(1) & 0xff) if not args.no_window else -1
                if key == ord('q'):
                    break
                if key == ord('r'):
                    tracker.reset()
    finally:
        sender.sendto(b'{"version":1,"people":[]}', ('127.0.0.1', args.port))
        sender.close()
        camera.release()
        cv2.destroyAllWindows()


if __name__ == '__main__':
    main()

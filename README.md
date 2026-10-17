# Katachi DigiPhant

#### Team: Katachi
#### Members: Ko-Qin Mei, Kai Zhang, Teresa Yang

Katachi DigiPhant is a collaborative Unity experience in which three people control one virtual elephant through body movements and hand gestures. The group works together to move through a Savannah course, steer the elephant, animate its body, and perform actions such as drinking water.

## Three-Person Controls

The current scene divides control between three performers:

| Performer | Input | Elephant action |
| --- | --- | --- |
| P1 | Raise or lower the left hand relative to the calibrated position | Move forward or backward; raising it farther increases speed |
| P1 | Raise the right hand | Raise the elephant into a standing pose and pause travel |
| P1 | Lower the right hand | Return to the normal pose and allow travel to resume |
| P1 | Lean sideways | Turn the elephant's head |
| P2 | Raise or lower the left hand | Swing the front left leg |
| P2 | Raise or lower the right hand | Swing the front right leg |
| P2 | Tilt the shoulders sideways | Steer left or right and sway the tail |
| P3 | Show a thumbs-up gesture | Flap the left ear |
| P3 | Show a thumbs-down gesture | Flap the right ear |
| P3 | Make a closed fist | Curl the trunk |
| P3 | Show a victory / peace sign | Start the drinking animation |

P3 should present gestures inside the camera preview's **HAND ZONE**. Gesture recognition reads this area rather than identifying the hand by performer ID, so other participants should keep their hands out of it.

Drinking plays once per gesture activation. Release the victory sign before triggering it again. Standing takes priority over drinking, and travel pauses during either action.

## Which Mode Should We Select?

For the intended three-person setup, select:

- **3 people**
- **Seated / upper body**
- **Camera**
- **Enable locomotion**

Upper-body mode lets participants use their hands and shoulders without requiring their feet to remain visible. **Test sliders** can be used to try the elephant's controls without live body tracking.

## Models and Assets

The project uses three main model resources:

- **MediaPipe Pose Landmarker Full** (`pose_landmarker_full.task`) tracks body landmarks and provides the movement measurements used for travel, steering, and body control.
- **MediaPipe Gesture Recognizer** (`gesture_recognizer.task`) recognizes the hand gestures used for the ears, trunk, and drinking action.
- **The supplied rigged elephant model** in `Assets/Elephant/` provides the character, skeleton, and animation clips. Using the supplied asset preserves compatibility with the existing bone mappings and animations.

The Savannah course provides the environment and finish marker. Tracking runs locally through a Python bridge and sends control data to Unity.

## Getting Started

1. Open the project in **Unity 6000.6.3f1**.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Restore the tracking dependencies and elephant assets if this is a fresh checkout. See `DIGIPHANT_SETUP.md`.
4. Press **Play** and select the settings listed above.
5. Wait for the performers to be detected.
6. Select **Set neutral pose (10 seconds)** and hold a comfortable neutral position with shoulders and hands visible.
7. Begin controlling the elephant together.

Calibrate with P1's left hand around waist height, leaving room to raise and lower it. Keep participants clearly visible and avoid overlapping bodies.

## Submission materials

- [Project documentation: Katachi DigiPhant Journal](https://jungle-turkey-eb8.notion.site/Katachi-Journal-3f642e81d14680a19b6fefdd56de5f02)
- [Setup and reproduction guide](DIGIPHANT_SETUP.md)
- [Tracking patch](TrackingPatches/bridge-windows-udp-reset.patch)
- [Tracking source snapshot and pinned dependencies](TrackingOverrides/README.md)
- [Pinned Gesture Recognizer model manifest](Tools/GestureRecognizerModel.json)

Performance video: not uploaded yet. Upload the final recording to the team Drive and replace this line with its share link before submission.

## Audio Feedback

The scene includes sounds for walking, turning, drinking, and crossing the finish line.

**Current known issue:** After the finish sound plays, the other action sounds remain disabled until Play mode is restarted.

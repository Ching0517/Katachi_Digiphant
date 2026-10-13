# DigiPhant — collective digital twin

One to four people control one elephant in Unity. Begin with the supplied
controls, then adapt who controls each part and how their movements affect it.

## Start without a camera

1. Create your URP project and copy both asset folders as instructed in
   `DigiPhantStarter/README.md`. The reference Unity version is **6000.6.0f1**.
2. In the Project panel, open **Assets → DigiPhant → Scenes → DigiPhant**.
3. Press **Play** at the top of Unity. Select the **Game** tab if needed. The
   webcam preview starts automatically above the controls. Allow camera access if
   macOS prompts; it may identify the requesting application as Unity or Python.
4. Move the on-screen **Test sliders** to move the legs, head, trunk, ears,
   and tail. **Reset body sliders** returns those gesture offsets to neutral.
5. Stop Play before saving changes. Select **DigiPhant Controls** in the
   Hierarchy and expand **Controls** in the Inspector to edit the mapping.

The original scenes and elephant asset remain available. Duplicate the DigiPhant
scene in the Project panel before experimenting so you retain a working example.

## Choose one, two, three, or four people

Use the **1 person / 2 people / 3 people / 4 people** buttons in the Game view.
Outside Play mode, select **DigiPhant Controls** and use the **Group size** buttons
in the Inspector, or use **DigiPhant → Use One/Two/Three/Four Performers**.
Save the scene to retain the choice made outside Play mode.

| Group size | Default division of control |
| --- | --- |
| 1 | P1 controls everything; foot lifts drive front and rear legs together. |
| 2 | P1 controls front legs; P2 controls rear legs, tail, head, trunk, and ears. |
| 3 | P1 controls front legs; P2 controls rear legs and tail; P3 controls head, trunk, and ears. |
| 4 | As above, with trunk control assigned to P4. |

Changing group size applies these default role assignments, resets test sliders,
and clears neutral calibration. Custom controls with other labels are kept within
the selected performer range. Adapt assignments after choosing the count.

While Camera mode is active, Unity sends the selected count to the bridge
automatically. The bridge reassigns identities; click **Set neutral pose** once
the chosen number of people are visible again. You do not need to restart the
bridge for subsequent count changes.

Roles and formation are yours to change. Begin side by side for assignment,
then try the chosen formation while keeping tracked body parts visible. If extra
people are visible during assignment, the leftmost selected number in the
unmirrored preview are used. Keep bystanders out of the camera frame when possible.

## Connect the camera

The camera bridge runs on the **same computer** as Unity. It sends movement
measurements and a small annotated preview image to Unity locally; no camera
images are sent to an online service. The first setup installs Python packages. The pose model is already included
in `DigiPhantStarter/Tracking`.

Once dependencies and the model are installed, **Play starts the bridge
automatically in the Unity Editor**. It opens the laptop webcam and shows the
preview in the Game view, including when Test sliders is selected. Select Camera
to drive the elephant and set a neutral pose. **Reassign people** resets performer
identities from inside Unity. Stop Play to close the bridge that Unity started.
An independently launched bridge is reused and remains running when Play stops.

The **Retry** button in the preview reconnects or restarts Unity's own bridge.
If a camera permission prompt was missed, enable camera access for the requesting
application in macOS System Settings → Privacy & Security → Camera, then Retry.
If the old standalone camera window is still running, quit it with Q once before
using this update. Built players use a separately launched bridge; automatic
process launch is currently Editor-only.

Follow `DigiPhantStarter/AGENT_SETUP.md` for explicit Mac and Windows
commands to create `DigiPhantStarter/Tracking/.venv` and install dependencies.
The packaged camera launcher expects that exact location beside Assets.
No separate setup launcher is included. To test the camera independently, use
the Camera test commands in that guide and quit with Q before returning
to automatic launch in Unity.

The source setup used an Apple Silicon Mac with Python 3.14. Fresh-project import,
camera operation and other classroom operating systems still need validation.

## Move through the scene while controlling body parts

**Enable locomotion** adds the elephant's supplied idle, walk, run, backward,
and turning animations. Existing gestures are applied on top, so the group can
walk while curling the trunk, moving the head, opening the ears, or adding leg
movements. Switch locomotion off to use the original stationary puppet controls.

In **Test sliders** mode, use **Travel** to move backward or forward and **Turn**
to steer. Moving the Travel slider farther to the right changes walking to
running. Turn with Travel centered to rotate in place. **Stop moving** centers
both movement sliders. The body-part sliders remain available below.

In **Camera** mode, the default movement mapping is:

| Input | Result |
| --- | --- |
| P1 raises their left hand above its calibrated height | Move forward; a larger raise changes walking to running |
| P1 lowers their left hand below its calibrated height | Move backward |
| P1 leans sideways relative to their hips | Turn; combine with forward movement to steer while walking/running |

For forward/backward control, calibrate with P1's left hand around waist height,
leaving room to raise and lower it. Other performers can use the existing body-part
mappings at the same time. The preview is unmirrored; invert the steering source's
weight if the turning direction feels opposite to the group's intention.

The camera follows the elephant's position. The stage grid provides visible
reference points for travel. **Return to starting position** restores position
and heading, stops movement, and requires a new neutral pose in Camera mode.
If any assigned movement source becomes unavailable, translation and turning
stop immediately while the animation blends back to idle.

### Configure or combine movement inputs

Stop Play and select **DigiPhant Controls**. In its **Digi Phant Locomotion**
component, expand **Forward** or **Steering**. Each mapping contains a list of
**Sources**, with a **Performer**, **Movement**, and signed **Weight**.

For example, give Forward two sources: P1 LeftHandHeight and P2 LeftHandHeight.
With **Average**, both people's signals contribute to the shared speed; with
**Sum**, their inputs add together and reach full speed sooner. Opposite signed
inputs can cancel. Negative weights invert a source. Empty mappings leave that
channel at zero, allowing a forward-only setup without a steering gesture.
All nonzero-weight sources must be tracked for locomotion to continue.

**Sensitivity** adjusts input strength and **Dead Zone** ignores small changes
around neutral. **Walk Speed**, **Run Speed**, **Backward Speed**, and **Turn
Degrees Per Second** control travel. **Run Threshold** determines when forward
input selects running. **Stage Radius** bounds travel around the initial position;
zero removes that bound. **Follow Elephant** controls camera following.

In each existing bone control, **Locomotion Weight** determines how much gesture
motion is added over the animation. It defaults to 0.35 for leg controls and 1
for head, trunk, ears, and tail. Set it to zero to leave that joint to animation,
or increase it for a stronger gestural overlay. It has no effect when locomotion
is switched off.

## Adapt a control

Stop Play, select **DigiPhant Controls**, expand **Controls**, and choose an entry:

- **Label:** the name shown with its test slider.
- **Performer:** whose movement drives it, numbered 1–4.
- **Movement:** left/right hand height, left/right foot lift, lean, or arm spread.
- **Bones:** the elephant joints affected by that control.
- **Local Axis:** the direction around which each joint rotates.
- **Degrees:** rotation per joint at full input; use a negative value to invert it.
- **Sensitivity:** how strongly a movement affects the elephant.
- **Smoothing:** how gradually the elephant responds; zero responds immediately.

For example, change **Trunk curl → Performer** to 1 and **Movement** to
**LeftHandHeight**. Save, press Play, calibrate, and raise performer 1's left hand.
Check one change at a time. Test sliders directly exercise each elephant control;
they do not simulate performer tracking or calibration.

## Troubleshooting and current limits

- **No camera:** close other camera applications, check camera permission, or run
  the bridge with `--camera 1` to try another device.
- **Waiting for performers:** initial assignment requires the full group. Keep
  hips and shoulders visible and move farther from the camera if feet are cut off.
- **Calibration refused:** every assigned movement needs visible landmarks.
  The tracking overlay helps reveal obscured hands or feet.
- **Wrong person controls a part:** reset IDs with R, then recalibrate. Assignment
  follows position, not appearance; crossings and long occlusions can confuse it.
- **No Unity movement:** select Camera, check the visible statuses, then calibrate.
  Bridge and Unity use UDP ports 5055 (tracking) and 5056 (group-size requests)
  on localhost, plus port 5057 for the camera preview. Only one receiver can use each port; stop another DigiPhant Play session if needed.
- **Tracking lost:** affected controls smoothly return to rest after missing input
  or a timeout. Their movement resumes when valid tracking returns.
- **Feet slide or float:** gait clips are combined with direct gesture offsets.
  There is no foot-planting IK, obstacle avoidance, collision-based navigation,
  or terrain following. Travel stays on a flat plane. Large leg overlays can
  exaggerate sliding; reduce their Locomotion Weight.
- **Overlapping formation:** tracking may become unreliable when people hide one
  another. Adjust spacing or camera angle; arbitrary occlusion is not supported.

The vendor keyboard controller remains disabled. While locomotion is active, a
manual animation graph owns the Animator; it evaluates the gait before gesture
offsets are applied. The supplied in-place clips drive the pose, and a parent
transform handles world travel, avoiding double root motion.

## Audio feedback

The `SampleScene` scene includes a **Katachi Audio Feedback** component on the
elephant controller. It observes the final locomotion state and plays four
assigned clips:

- **Walk:** a looping clip starts when the elephant is actually moving and
  fades out when movement stops.
- **Turn:** a one-shot plays when the action changes into a left or right turn;
  the short cooldown prevents rapid retriggering while steering.
- **Drink:** a one-shot plays when the drink gesture begins and immediately
  stops the walking loop.
- **Finish:** a one-shot plays when the elephant crosses the finish marker
  inside its configured gate. The finish event has highest priority, stops the
  walking loop, and is accepted only once until the component is reset.

Finish takes priority over drink and turning; drink takes priority over turning
and walking. Audio is presentation feedback only and does not change tracking,
locomotion, or gesture input. The component's **walkingSource** is looped and
its **eventSource** is used for the one-shot clips. Scene references, clips,
sources, finish gate width, turn cooldown, and walk fade time are configured on
the **Katachi Audio Feedback** component. The supported scene reference for the
turn sound is `Turn.wav`; `Turn.m4a` is retained as the source recording.

## Files and validation

- `Runtime/DigiPhantController.cs`: mappings, calibration, UDP receiver, smoothing,
  bone rotations, and the on-screen test panel.
- `Runtime/DigiPhantCameraPreview.cs`: live preview, local bridge startup, and
  cleanup when Play stops.
- `Runtime/DigiPhantLocomotion.cs`: compound movement inputs, gait blending, world
  travel, and camera following.
- `Editor/DigiPhantLocomotionSetup.cs`: attaches locomotion and sets up the stage.
- `Editor/DigiPhantLocomotionValidation.cs`: locomotion and gesture-composition checks.
- `Editor/DigiPhantSetup.cs`: template creation, performer presets, and validation.
- `Scenes/DigiPhant.unity`: ready-to-open template scene.
- `DigiPhantStarter/Tracking/bridge.py`: MediaPipe capture, performer assignment,
  body-relative measurements, and local UDP output.

Protocol: JSON `version: 1`, `performerCount` (1–4), with `people` entries containing `slot` (1–4),
`values` and `confidence` arrays. The six positions are LeftHandHeight,
RightHandHeight, LeftFootLift, RightFootLift, Lean, ArmSpread. An empty people
array indicates no tracked performers. Unity rejects malformed shapes and
non-finite numbers, and ignores stale input. Measurements are relative to torso
size in image coordinates; they are not shared 3D world coordinates.

`DigiPhant → Validate Saved Template` checks the default nine-control template
and reopens its saved scene afterward. Use it on the baseline, since customized
mappings may intentionally differ. Python checks run with:

```sh
python3 -m unittest discover -s Tracking -v
```

Reference: [MediaPipe Pose Landmarker](https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker/python).

After updating from the earlier three/four-person version, quit the old camera
window with Q and restart the bridge using the commands in
`DigiPhantStarter/README.md`, or press Play to use automatic launch.

Preview transport: a JPEG fitted within 320×240 pixels is sent at up to 10 Hz
to localhost on the tracking port plus 2. The preview preserves aspect ratio and
is unmirrored, matching performer IDs. Frames older than two seconds are hidden.
The bridge alone opens the camera, so Unity does not compete for the device.

## Seated / upper-body option

In Play mode, select your group size, choose **Seated / upper body**, then
**Camera**. Keep your head, shoulders and hands in view; hips and feet may be
outside the image. Wait for performer labels, hold your hands at a comfortable
mid-height and click **Set neutral pose**. Changing movement mode reassigns
people left-to-right and requires fresh calibration.

The same performer roles apply. Raising the left/right hand replaces lifting
the left/right foot for leg controls. Tilt your shoulders sideways for steering,
head turn and tail sway. Hand height still controls travel and trunk curl;
spreading your hands controls the ears. In solo mode, hand raises consequently
move legs and also affect travel/trunk together. Return to neutral to stop.

Switch back to **Full body** and recalibrate to restore foot controls. Inspector
movement names `LeftFootLift` and `RightFootLift` refer to hand-height inputs
while seated mode is active. The bridge supports `--upper-body-only` for manual
launch; Unity synchronizes its selected mode while Camera input is active.

Neutral calibration uses a **10-second countdown**: click **Set neutral pose
(10 seconds)**, get into position, and hold still until the countdown finishes.
The elephant's camera controls pause during the countdown. **Cancel countdown**
aborts it. Changing input mode, movement mode, group size or reassigning people
also cancels it. All required movements must be visible when the timer ends;
if calibration fails, correct your framing and start the countdown again.

## Record a collective performance

After selecting Camera and calibrating, click **Record performance** above the
controls. Keep the Game view visible with the camera preview and elephant in view.
Click **Stop recording and save**, then check the MP4 in the project-root
`Recordings/<session>/performance.mp4`. This captures the complete Game view at
up to 10 fps and 1280×720 without microphone audio. Tracking continues during
capture; export runs in a separate Python process afterward. Frames and timing
metadata are retained for export recovery. Verify the saved video, upload it to
your team's Drive folder and share the link. Recording does not upload to Drive.

In the student starter, read `DigiPhantStarter/RECORDING.md` for full instructions
and `STUDENT_GIT.md` for submitting a lightweight project with recordings and
heavy shared assets excluded. Live Game view recording must be tested on your
own machine; do not switch away from the Game view during a take.

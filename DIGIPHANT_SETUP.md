# DigiPhant setup

This document records the current, verified Unity setup. Source code and the supplied README files remain the detailed references.

## What the project does

`Katachi_Digiphant` is a Unity 6 URP scene containing the elephant, generated Savannah course, camera preview, and a local MediaPipe tracking bridge. The bridge reads a webcam, tracks up to three performers, recognizes a hand gesture in the preview's left third, and sends tracking data to Unity over localhost UDP. Unity assigns performers, applies calibration and smoothing, drives locomotion, and applies gesture-owned ear/trunk actions.

The current saved three-person assignment is:

| Performer | Current runtime role |
| --- | --- |
| P1 | travel, standing/head pose mapping |
| P2 | front legs, tail, and body steering |
| P3 | gesture-owned ears and trunk; `Victory` starts drink |

The saved default is three-person mode: forward/back travel comes from P1 and steering comes from P2. In upper-body mode, P2 steering is shoulder tilt; in full-body mode, the existing hip-relative lean calculation is used. Head pose remains assigned to P1. P3 continues to own the gesture ears/trunk actions.

## Project structure

- `Assets/Scenes/SampleScene.unity` is the default scene. `SampleScene_BeforeSavannah.unity` is the injection backup.
- `Assets/Elephant/` contains the supplied elephant model and animations.
- `Assets/SavannahCourse/` contains the imported course package and metadata.
- `Assets/DigiPhant/Runtime/` contains the controller, UDP receiver, preview, locomotion, and recording components.
- `Assets/DigiPhant/Editor/` contains setup and validation commands.
- `DigiPhantStarter/Tracking/bridge.py` is the Python camera and MediaPipe bridge used at runtime, kept outside `Assets`.
- `TrackingOverrides/` contains the submitted snapshot of the modified Python bridge, its tests, and pinned requirements. `TrackingPatches/` contains the reproducible Windows UDP/camera patch; `Tools/` contains guarded restore and model-install scripts.

The data path is:

```text
webcam -> bridge.py + MediaPipe -> UDP 5055 tracking / 5056 commands / 5057 JPEG preview
       -> DigiPhantController -> DigiPhantLocomotion and gesture action layer
       -> elephant and Savannah course
```

## Running the project

Use Unity `6000.6.3f1` with URP `17.6.0`, open `Assets/Scenes/SampleScene.unity`, and press Play. If the scene was already open while the saved scene changed, stop Play and reload `SampleScene.unity` from disk so the saved P2 steering assignment is in memory; preserve any unrelated unsaved work if Unity asks. Select the three-person, seated/upper-body mode, wait for performer labels, then run neutral calibration for ten seconds. Keep shoulders and hands visible. P1 controls forward/back travel and head pose; P2's shoulder tilt controls steering. Gesture recognition uses the preview's `HAND ZONE`; it does not require pose calibration.

Start the bridge from the project root:

```powershell
& .\DigiPhantStarter\Tracking\.venv\Scripts\python.exe .\DigiPhantStarter\Tracking\bridge.py --mirror-preview
```

The default camera is index `0`; use `--camera 1` for a second camera. Stop and restart Play after changing bridge code. The preview is divided at normalized X `1/3` and `2/3`, with labels `HAND ZONE`, `POSE 1`, and `POSE 2`.

For a fresh checkout, run these commands from the `Katachi_Digiphant` project root. The clone destination is inside this project, alongside `Assets`:

```powershell
git clone https://github.com/kommanderpi/studentstarter.git .\DigiPhantStarter
git -C .\DigiPhantStarter checkout --detach 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f
& .\Tools\RestoreDigiPhantStarter.ps1
py -3.12 -m venv .\DigiPhantStarter\Tracking\.venv
& .\DigiPhantStarter\Tracking\.venv\Scripts\python.exe -m pip install -r .\DigiPhantStarter\Tracking\requirements.txt
& .\Tools\InstallGestureRecognizerModel.ps1
& .\DigiPhantStarter\Tracking\.venv\Scripts\python.exe -m unittest discover -s .\DigiPhantStarter\Tracking -v
```

`RestoreDigiPhantStarter.ps1` only runs before shared assets have been restored; it refuses to overwrite an existing `Assets/Elephant` or modified starter. It verifies the fixed starter commit, copies the supplied elephant, and applies `TrackingPatches/bridge-windows-udp-reset.patch`. The fixed starter carries the pose model and its recorded `asset-checksums.json`; verify that manifest rather than using `--download-model`, which could fetch a moving `latest` asset. `InstallGestureRecognizerModel.ps1` verifies the pinned gesture model hash from `Tools/GestureRecognizerModel.json`. `TrackingOverrides/` is the reviewable submitted copy of the modified Python files; it is not the runtime location because Unity launches the bridge from `DigiPhantStarter/Tracking`.

The saved `SampleScene` mappings are `DigiPhantLocomotion.forward.sources[0]`: P1 / `LeftHandHeight`, and `steering.sources[0]`: P2 / `Lean`. Three-person and upper-body modes remain enabled. Switching to solo clamps the steering assignment from P2 to P1. After returning to three-person mode, restore Steering / Sources / Element 0 / Performer to 2 in the Inspector, or reload the saved scene. This limitation concerns the steering assignment, not the selected group count.

## Gesture and animation behavior

The bridge adds optional flat fields to the existing version-1 pose packet: `gesture` and `gestureScore`. Labels are `Thumb_Up`, `Thumb_Down`, `Closed_Fist`, `Victory`, `Open_Palm`, or `None`; unavailable/no-hand input is `None` with score `0`. Unity validates the label, finite score, range `0..1`, and minimum confidence `0.5`. A configurable `0.5` second timeout neutralizes a missing gesture while preserving pose data. Ports remain `5055`, `5056`, and `5057`.

Gesture actions are owned by the native PlayableGraph locomotion layer, so the vendor Animator trigger path is disabled to avoid two animation owners. `Thumb_Up` and `Thumb_Down` flap the left and right ears, `Closed_Fist` curls the trunk, and `Victory` plays the supplied drink clip once. `Open_Palm`, `None`, timeout, cancellation, and the feature toggle return ears/trunk to pose control or neutral. Held `Victory` does not replay until release; travel pauses during drinking or standing. The drink clip is `Assets/Elephant/Animations/elephant@drink.fbx` (GUID `90d33167f6645c64faabb84065cc32d5`, file ID `1827226128182048838`). Bone overlay weights and source-axis/trunk-wave settings remain configurable.

## Dependencies and limitations

The tested machine is Windows 11 Pro x64 with an AMD Ryzen 7 PRO 8840U. The tracking environment uses Python `3.12.10`, MediaPipe `0.10.35`, and OpenCV contrib `4.14.0.94`. The GestureRecognizer task model is stored outside `Assets` at `DigiPhantStarter/Tracking/gesture_recognizer.task` and installed by the tool script.

Preview and bridge tests are synthetic/offline checks; they do not prove a live human session. Live control still requires camera framing, performer assignment, calibration, and user testing. Pose remains full-frame even though the preview is visually partitioned. Videos belong in Drive and are excluded from Git. No Drive upload or remote Git push is configured.

## Verification record

- Packaging checks for this submission: the TrackingOverrides unittest run executed 21 tests, with 20 passing and 1 skipped because its model/fixture was absent; seven snapshot/runtime file comparisons matched byte-for-byte; and the patch reverse-check passed. Earlier forward-apply and actual-apply checks are historical validation records, not this run.
- Historical Unity validation: fresh Roslyn/runtime and Unity Editor compilation passed; only the existing Savannah warning remained.
- Historical Unity validation: Native Unity 6 batch validation in a disposable project passed with `DIGIPHANT_LOCOMOTION_VALIDATION_OK` and exit code 0. It covered gait, world turning, additive bone overlays, ear mapping, fist curl, drink completion/release/retrigger/cancel, packet validation, missing data/timeouts, course radius, and shutdown.
- Historical Unity validation: the final native steering check passed with `P1` forward/hand input and `P2` lean input: P1-only input produced travel without significant root rotation, while P2 lean produced steering and walking. The fixture explicitly restored upper-body mode after the check.
- Historical Unity validation: Native runtime and Editor compilation passed with no new errors. No Python protocol/model changes were made for this steering step; the existing Python history remains applicable.
- No claim is made that a live camera session or every Inspector hot-change has been tested.

## Recording and Git

After calibration, use `Record performance` in the controller. Output is `Recordings/<session>/performance.mp4` (Game view capture, up to 1280x720 and 10 fps, without microphone audio). Verify playback, then upload the video to the team's Drive; do not commit it. The lightweight student repository excludes Unity caches, the Python virtual environment, recordings, and heavy starter assets. It includes `TrackingOverrides/` so the modified Python source, tests, and pinned dependency requirements remain reviewable without committing the external starter clone. Keep the external `DigiPhantStarter` and Savannah source checkouts intact.

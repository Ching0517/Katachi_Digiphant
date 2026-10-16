# Submitted tracking source

This directory is the reviewable copy of the Python tracking changes used by the
project. It contains the modified UDP/camera bridge, recording helper, Python
tests, and the pinned dependency list. It contains no virtual environment,
MediaPipe model, camera recording, generated image frames, or other large
runtime data.

Unity deliberately launches the executable copy from the external
`DigiPhantStarter/Tracking` directory. For a clean checkout, follow
[`../DIGIPHANT_SETUP.md`](../DIGIPHANT_SETUP.md): restore the starter at its
recorded commit and run `Tools/RestoreDigiPhantStarter.ps1`. That script
strictly checks and applies
[`../TrackingPatches/bridge-windows-udp-reset.patch`](../TrackingPatches/bridge-windows-udp-reset.patch)
to the starter copy. The patch is the reproducible source of truth; these files
make the changed source and requirements available directly in the submitted
repository.

Create the starter environment with Python 3.12.10 and install its matching
`Tracking/requirements.txt`. The requirements snapshot here is byte-for-byte
the same pinned list. The fixed starter checkout includes the pose model and
its `asset-checksums.json`; verify that recorded asset rather than using the
bridge's `--download-model` option. Install the separately downloaded,
hash-verified Gesture Recognizer model using
`Tools/InstallGestureRecognizerModel.ps1`; do not commit the model into this
repository.

The packaging unittest run for this submission executed 21 tests: 20 passed
and 1 was skipped because its model/fixture was absent. The patch checks also
passed. These results cover the submitted source and reproducible patch, not a
live camera session or a new Unity run.

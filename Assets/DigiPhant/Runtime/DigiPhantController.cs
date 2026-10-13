using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace DigiPhant
{
    public enum Movement { LeftHandHeight, RightHandHeight, LeftFootLift, RightFootLift, Lean, ArmSpread }
    public enum InputMode { TestSliders, Camera }

    [Serializable] public class PerformerFrame
    {
        public int slot;
        public float[] values;
        public float[] confidence;
    }
    [Serializable] public class PoseFrame
    {
        public int version;
        public int performerCount;
        public bool upperBodyOnly;
        public PerformerFrame[] people;
        // Optional in protocol version 1, preserving compatibility with older bridges.
        public string gesture;
        public float gestureScore;
    }
    [Serializable] public class BoneControl
    {
        public string label;
        [Tooltip("Disable a legacy mapping while retaining its scene binding.")]
        public bool disabled;
        [Range(1, 4)] public int performer = 1;
        public Movement movement;
        public Transform[] bones;
        [Tooltip("Rotation axis in each bone's local coordinates.")]
        public Vector3 localAxis = Vector3.forward;
        [Tooltip("Maximum rotation per bone for an input of 1. Negative values invert direction.")]
        public float degrees = 20;
        [Range(0, 3)] public float sensitivity = 1;
        [Range(0, 20)] public float smoothing = 8;
        [Range(-1, 1)] public float testValue;
        [Tooltip("How much of this gesture is added over locomotion animation. Zero lets the animation own this joint.")]
        [Range(0, 1)] public float locomotionWeight = 1;
        [NonSerialized] public float current;
    }

    public class DigiPhantController : MonoBehaviour
    {
        public InputMode inputMode = InputMode.TestSliders;
        [HideInInspector] public int performerCount = 3;
        public bool upperBodyOnly;
        public BoneControl[] controls = Array.Empty<BoneControl>();
        [Tooltip("Local camera bridge port. Change outside Play mode.")]
        public int port = 5055;
        [Range(0, 1)] public float minimumConfidence = .5f;
        [Min(.1f)] public float trackingTimeout = .5f;
        [Header("Gesture actions")]
        public bool enableGestureActions = true;
        [Range(0, 1)] public float gestureMinimumConfidence = .5f;
        [Min(.1f)] public float gestureTimeout = .5f;
        public bool showControls = true;
        public bool showCameraPreview = true;
        [Range(180, 700)] [Tooltip("Width of the camera preview in the in-game controls. This is also used when the controls panel is hidden.")]
        public float cameraPreviewWidth = 330;
        public string Status { get; private set; } = "Test sliders ready";
        public int ReceivedFrames { get; private set; }
        public bool IsCalibrated { get; private set; }
        public string CurrentGesture { get; private set; } = "None";
        float calibrationDeadline = -1;
        public bool CalibrationPending => calibrationDeadline >= 0;
        readonly float[,] values = new float[4, 6];
        readonly float[,] confidence = new float[4, 6];
        readonly float[,] neutral = new float[4, 6];
        readonly float[] lastSeen = { -1000, -1000, -1000, -1000 };
        float lastGestureSeen = -1000;
        bool hasGestureSnapshot;
        readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
        UdpClient socket;
        Vector2 scroll;
        float nextCountRequest;
        float nextReceiverRetry;
        int previousCount;
        InputMode previousMode;
        bool previousUpperBodyOnly;

        float ControlsPanelWidth()
        {
            float maxPanelWidth = Mathf.Min(720, Mathf.Max(200, Screen.width * .6f));
            return Mathf.Min(cameraPreviewWidth + 20, maxPanelWidth);
        }

        void OnEnable()
        {
            if (Application.isPlaying && !Application.isBatchMode && showCameraPreview && GetComponent<DigiPhantCameraPreview>() == null)
                gameObject.AddComponent<DigiPhantCameraPreview>();
            if (Application.isPlaying && !Application.isBatchMode && GetComponent<DigiPhantRecording>() == null)
                gameObject.AddComponent<DigiPhantRecording>();
            previousCount = performerCount;
            previousUpperBodyOnly = upperBodyOnly;
            ApplyThreePerformerBodyPreset();
            ClearTracking();
            nextCountRequest = 0;
            ReceivedFrames = 0;
            Status = "Test sliders ready";
            CaptureRestPose();
            previousMode = inputMode;
            if (inputMode == InputMode.Camera) { nextReceiverRetry = 0; StartReceiver(); }
        }
        public void SetPerformerCount(int count)
        {
            performerCount = previousCount = Mathf.Clamp(count, 1, 4);
            foreach (var c in controls)
            {
                switch (c.label)
                {
                    case "Front left leg": case "Front right leg": c.performer = Mathf.Min(2, performerCount); break;
                    case "Rear left leg": case "Rear right leg":
                        c.disabled = true;
                        c.performer = Mathf.Min(2, performerCount); break;
                    case "Tail sway":
                        c.performer = Mathf.Min(2, performerCount); break;
                    case "Head turn":
                        c.performer = performerCount >= 3 ? 1 : performerCount; break;
                    case "Left ear": case "Right ear":
                        c.performer = Mathf.Min(3, performerCount); break;
                    case "Trunk curl": c.performer = performerCount; break;
                    default: c.performer = Mathf.Clamp(c.performer, 1, performerCount); break;
                }
            }
            ApplyThreePerformerBodyPreset();
            var locomotion = GetComponent<DigiPhantLocomotion>();
            locomotion?.ClampPerformers(performerCount);
            if (performerCount >= 3 && locomotion != null && locomotion.steering.sources.Length == 1 &&
                locomotion.steering.sources[0].movement == Movement.Lean)
                locomotion.steering.sources[0].performer = 2;
            ClearTracking();
            ResetControls();
            nextCountRequest = 0;
            Status = "Group size changed. Set neutral pose again in Camera mode.";
        }
        // The named bindings are part of the supplied three-performer scene.
        // Keep this narrow so custom controls retain their own assignments.
        void ApplyThreePerformerBodyPreset()
        {
            if (performerCount < 3) return;
            foreach (var c in controls)
            {
                if (c.label == "Front left leg") { c.performer = 2; c.movement = Movement.LeftHandHeight; c.disabled = false; c.locomotionWeight = 1; }
                else if (c.label == "Front right leg") { c.performer = 2; c.movement = Movement.RightHandHeight; c.disabled = false; c.locomotionWeight = 1; }
                else if (c.label == "Rear left leg" || c.label == "Rear right leg") c.disabled = true;
            }
        }
        public void SetUpperBodyOnly(bool enabled)
        {
            upperBodyOnly = previousUpperBodyOnly = enabled;
            ClearTracking();
            ResetControls();
            nextCountRequest = 0;
            Status = "Movement mode changed. Wait for camera, then set neutral pose.";
        }
        public void CaptureRestPose()
        {
            rest.Clear();
            foreach (var c in controls)
                if (c.bones != null)
                    foreach (var bone in c.bones)
                        if (bone != null && !rest.ContainsKey(bone)) rest.Add(bone, bone.localRotation);
        }
        void StartReceiver()
        {
            StopReceiver();
            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                socket.Client.Blocking = false;
                Status = "Waiting for local camera bridge";
            }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException)
            { StopReceiver(); Status = "Cannot open camera port: " + e.Message; }
        }
        void StopReceiver() { socket?.Close(); socket = null; }
        public void SetInputMode(InputMode mode)
        {
            inputMode = previousMode = mode;
            ClearTracking();
            if (mode == InputMode.Camera) { nextReceiverRetry = 0; StartReceiver(); }
            else { StopReceiver(); Status = "Test sliders ready"; }
        }
        void ClearTracking()
        {
            calibrationDeadline = -1;
            GetComponent<DigiPhantLocomotion>()?.StopMotion();
            GetComponent<DigiPhantLocomotion>()?.ForceLower();
            IsCalibrated = false;
            Array.Clear(confidence, 0, confidence.Length);
            Array.Clear(neutral, 0, neutral.Length);
            for (int i = 0; i < 4; i++) lastSeen[i] = -1000;
            GetComponent<DigiPhantLocomotion>()?.ClearGestureActions();
            ClearGesture();
        }
        void ClearGesture()
        {
            CurrentGesture = "None";
            hasGestureSnapshot = false;
            lastGestureSeen = -1000;
            GetComponent<DigiPhantLocomotion>()?.SetGesture(null, false);
        }
        public bool AcceptPacket(string json, float now)
        {
            PoseFrame frame;
            try { frame = JsonUtility.FromJson<PoseFrame>(json); }
            catch (ArgumentException) { return false; }
            if (frame == null || frame.version != 1 || frame.people == null || frame.people.Length > 4) return false;
            if (frame.performerCount != 0 && frame.performerCount != performerCount)
            {
                Array.Clear(confidence, 0, confidence.Length);
                Status = "Updating camera group size...";
                return false;
            }
            if (frame.upperBodyOnly != upperBodyOnly)
            {
                Array.Clear(confidence, 0, confidence.Length);
                Status = "Updating camera movement mode...";
                return false;
            }
            var slots = new HashSet<int>();
            foreach (var p in frame.people)
            {
                if (p == null || p.slot < 1 || p.slot > 4 || !slots.Add(p.slot) ||
                    p.values == null || p.confidence == null || p.values.Length != 6 || p.confidence.Length != 6) return false;
                for (int k = 0; k < 6; k++)
                    if (!Finite(p.values[k]) || !Finite(p.confidence[k]) || p.confidence[k] < 0 || p.confidence[k] > 1) return false;
            }
            Array.Clear(confidence, 0, confidence.Length);
            foreach (var p in frame.people)
            {
                int i = p.slot - 1;
                lastSeen[i] = now;
                for (int k = 0; k < 6; k++)
                { values[i, k] = Mathf.Clamp(p.values[k], -3, 3); confidence[i, k] = p.confidence[k]; }
            }
            ReceivedFrames++;
            AcceptGesture(frame, now);
            return true;
        }
        static bool IsSupportedGesture(string value)
        {
            return value == "None" || value == "Open_Palm" || value == "Thumb_Up" ||
                value == "Thumb_Down" || value == "Closed_Fist" || value == "Victory";
        }
        void AcceptGesture(PoseFrame frame, float now)
        {
            // Gesture data is optional. Its failure must never discard valid pose data.
            if (string.IsNullOrEmpty(frame.gesture) || !IsSupportedGesture(frame.gesture) ||
                !Finite(frame.gestureScore) || frame.gestureScore < 0 || frame.gestureScore > 1)
            { ClearGesture(); return; }
            hasGestureSnapshot = true;
            lastGestureSeen = now;
            CurrentGesture = frame.gesture == "None" || frame.gesture == "Open_Palm" ||
                frame.gestureScore < gestureMinimumConfidence ? "None" : frame.gesture;
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public void PollCamera(float now)
        {
            // Enter Play Mode Options can skip OnEnable for an already-live
            // component. Recreate the receiver on demand in Camera mode, but
            // do not repeatedly allocate sockets when the port is occupied.
            if (socket == null && now >= nextReceiverRetry)
            {
                nextReceiverRetry = now + 1;
                StartReceiver();
            }
            if (socket == null) return;
            try
            {
                if (now >= nextCountRequest)
                {
                    var request = Encoding.UTF8.GetBytes("{\"version\":1,\"performerCount\":" + performerCount + ",\"upperBodyOnly\":" + (upperBodyOnly ? "true" : "false") + "}");
                    socket.Send(request, request.Length, new IPEndPoint(IPAddress.Loopback, port + 1));
                    nextCountRequest = now + .5f;
                }
                // Bound per-frame work; use the newest datagram from this batch.
                byte[] latest = null;
                IPEndPoint sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 64 && socket.Available > 0; i++) latest = socket.Receive(ref sender);
                if (latest != null && latest.Length < 16000 && !AcceptPacket(Encoding.UTF8.GetString(latest), now))
                    Status = "Ignored invalid camera packet";
            }
            catch (SocketException e) { Status = "Camera connection: " + e.Message; }
        }
        public bool IsTracked(int slot, float now)
        {
            int i = slot - 1;
            if (i < 0 || i >= 4 || now - lastSeen[i] > trackingTimeout) return false;
            for (int k = 0; k < 6; k++) if (confidence[i, k] >= minimumConfidence) return true;
            return false;
        }
        public void ReassignPeople()
        {
            ClearTracking();
            if (socket == null) return;
            try
            {
                var request = Encoding.UTF8.GetBytes("{\"version\":1,\"performerCount\":" + performerCount + ",\"upperBodyOnly\":" + (upperBodyOnly ? "true" : "false") + ",\"reset\":true}");
                socket.Send(request, request.Length, new IPEndPoint(IPAddress.Loopback, port + 1));
                Status = "Reassigning people. Set neutral pose when visible.";
            }
            catch (SocketException e) { Status = e.Message; }
        }
        public void InvalidateCalibration()
        {
            calibrationDeadline = -1;
            IsCalibrated = false;
            GetComponent<DigiPhantLocomotion>()?.StopMotion();
            GetComponent<DigiPhantLocomotion>()?.ClearGestureActions();
        }
        public bool IsMovementVisible(int performer, Movement movement, float now)
        {
            int i = performer - 1, k = (int)movement;
            return i >= 0 && i < performerCount && k >= 0 && k < 6 &&
                IsTracked(performer, now) && confidence[i, k] >= minimumConfidence;
        }
        public bool TryGetMovementReadinessIssue(int performer, Movement movement, float now, out string issue)
        {
            int i = performer - 1, k = (int)movement;
            if (i < 0 || i >= performerCount || k < 0 || k >= 6)
            {
                issue = "Performer or movement assignment is invalid.";
                return true;
            }
            if (now - lastSeen[i] > trackingTimeout)
            {
                issue = "Performer " + performer + " is not currently detected. Make sure they are fully in camera view.";
                return true;
            }
            if (confidence[i, k] < minimumConfidence)
            {
                string movementName;
                switch (movement)
                {
                    case Movement.LeftHandHeight: movementName = "left hand height"; break;
                    case Movement.RightHandHeight: movementName = "right hand height"; break;
                    case Movement.LeftFootLift: movementName = "left foot lift"; break;
                    case Movement.RightFootLift: movementName = "right foot lift"; break;
                    case Movement.Lean: movementName = "body lean"; break;
                    case Movement.ArmSpread: movementName = "arm spread"; break;
                    default: movementName = movement.ToString(); break;
                }
                issue = "Performer " + performer + " · " + movementName +
                    " confidence is too low (" + confidence[i, k].ToString("P0") +
                    "; needs " + minimumConfidence.ToString("P0") +
                    "). Keep the relevant body parts visible and make the pose clearer.";
                return true;
            }
            issue = null;
            return false;
        }
        public bool TryReadMovement(int performer, Movement movement, float now, out float value)
        {
            value = 0;
            if (!IsCalibrated || !IsMovementVisible(performer, movement, now)) return false;
            value = values[performer - 1, (int)movement] - neutral[performer - 1, (int)movement];
            return true;
        }
        public void BeginCalibrationCountdown(float now)
        {
            if (inputMode != InputMode.Camera) return;
            InvalidateCalibration();
            ResetControls();
            calibrationDeadline = now + 10;
            Status = "Get into your neutral pose and hold still.";
        }
        public void CancelCalibrationCountdown()
        {
            calibrationDeadline = -1;
            Status = "Calibration cancelled. Set neutral pose when ready.";
        }
        public void UpdateCalibrationCountdown(float now)
        {
            if (!CalibrationPending || now < calibrationDeadline) return;
            calibrationDeadline = -1;
            CalibrateAt(now);
        }
        public void Calibrate() => CalibrateAt(Time.realtimeSinceStartup);
        public bool CalibrateAt(float now)
        {
            calibrationDeadline = -1;
            IsCalibrated = false;
            var locomotion = GetComponent<DigiPhantLocomotion>();
            foreach (var c in controls)
            {
                if (c.disabled) continue;
                if (locomotion != null && locomotion.ShouldGestureOwn(c.label)) continue;
                if (TryGetMovementReadinessIssue(c.performer, c.movement, now, out string issue))
                { Status = "Neutral pose not saved. " + c.label + ": " + issue; return false; }
            }
            if (locomotion != null && locomotion.isActiveAndEnabled &&
                !locomotion.InputsVisible(now, out string locomotionIssue))
            { Status = "Neutral pose not saved. " + locomotionIssue; return false; }
            Array.Copy(values, neutral, values.Length);
            IsCalibrated = true;
            Status = "Neutral pose saved";
            return true;
        }
        public void ResetControls()
        {
            foreach (var c in controls) { c.testValue = 0; c.current = 0; }
            foreach (var pair in rest) if (pair.Key != null) pair.Key.localRotation = pair.Value;
        }
        public void ApplyControls(float now, float dt)
        {
            foreach (var pair in rest) if (pair.Key != null) pair.Key.localRotation = pair.Value;
            var locomotion = GetComponent<DigiPhantLocomotion>();
            bool gestureFresh = enableGestureActions && hasGestureSnapshot && now - lastGestureSeen <= gestureTimeout;
            if (locomotion != null) locomotion.SetGesture(gestureFresh ? CurrentGesture : null, gestureFresh);
            bool animated = locomotion != null && locomotion.isActiveAndEnabled && locomotion.Evaluate(now, dt);
            foreach (var c in controls)
            {
                if (c.disabled) { c.current = 0; continue; }
                float target = 0;
                int i = c.performer - 1, k = (int)c.movement;
                if (i >= 0 && i < performerCount && k >= 0 && k < 6)
                {
                    if (inputMode == InputMode.TestSliders) target = c.testValue;
                    else if (IsCalibrated && IsTracked(c.performer, now) && confidence[i, k] >= minimumConfidence)
                        target = values[i, k] - neutral[i, k];
                }
                target = Mathf.Clamp(target * c.sensitivity, -1, 1);
                if (locomotion != null && locomotion.ShouldGestureOwn(c.label)) target = 0;
                c.current = c.smoothing <= 0 ? target : Mathf.Lerp(c.current, target, 1 - Mathf.Exp(-c.smoothing * dt));
                if (c.bones == null || c.localAxis.sqrMagnitude < .0001f) continue;
                float gestureValue = locomotion != null ? locomotion.GestureValueFor(c.label) : 0;
                float overlayWeight = locomotion != null ? locomotion.BoneOverlayWeight : 1;
                foreach (var bone in c.bones)
                    if (bone != null && rest.ContainsKey(bone))
                        bone.localRotation *= Quaternion.AngleAxis((c.current * (animated ? c.locomotionWeight : 1) + gestureValue) * overlayWeight * c.degrees, c.localAxis.normalized);
            }
        }
        void LateUpdate()
        {
            if (previousUpperBodyOnly != upperBodyOnly) SetUpperBodyOnly(upperBodyOnly);
            if (previousCount != performerCount) SetPerformerCount(performerCount);
            if (previousMode != inputMode) SetInputMode(inputMode);
            if (inputMode == InputMode.Camera) PollCamera(Time.realtimeSinceStartup);
            UpdateCalibrationCountdown(Time.realtimeSinceStartup);
            ApplyControls(Time.realtimeSinceStartup, Time.deltaTime);
            var stageCamera = Camera.main;
            if (stageCamera != null)
            {
                float left = showControls ? Mathf.Clamp((ControlsPanelWidth() + 20) / Mathf.Max(1, Screen.width), 0, .75f) : 0;
                stageCamera.rect = new Rect(left, 0, 1 - left, 1);
            }
        }
        void OnDisable() { calibrationDeadline = -1; StopReceiver(); GetComponent<DigiPhantLocomotion>()?.StopAnimation(); ResetControls(); }
        void OnGUI()
        {
            if (!showControls) return;
            // Leave enough room for the stage camera while allowing a useful large preview.
            float width = ControlsPanelWidth();
            GUI.DrawTexture(new Rect(0, 0, width + 20, Screen.height), Texture2D.blackTexture, ScaleMode.StretchToFill, false);
            var labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            void Label(string text) => GUILayout.Label(text, labelStyle, GUILayout.Width(width - 32));
            GUILayout.BeginArea(new Rect(10, 10, width, Screen.height - 20), GUI.skin.box);
            var preview = GetComponent<DigiPhantCameraPreview>();
            if (preview != null && preview.showPreview)
            {
                GUILayout.Label("Preview size: " + Mathf.RoundToInt(cameraPreviewWidth));
                float newPreviewWidth = GUILayout.HorizontalSlider(cameraPreviewWidth, 180, 700);
                if (!Mathf.Approximately(newPreviewWidth, cameraPreviewWidth))
                {
                    cameraPreviewWidth = newPreviewWidth;
                }
                preview.DrawInline(width - 20);
            }
            GetComponent<DigiPhantRecording>()?.DrawControls(width - 32);
            scroll = GUILayout.BeginScrollView(scroll);
            Label("DIGIPHANT | collective digital twin");
            Label("Choose your group size");
            int count = GUILayout.Toolbar(performerCount - 1, new[] { "1", "2", "3", "4" }) + 1;
            if (count != performerCount) SetPerformerCount(count);
            bool upper = GUILayout.Toolbar(upperBodyOnly ? 1 : 0, new[] { "Full body", "Seated / upper body" }) == 1;
            if (upper != upperBodyOnly) SetUpperBodyOnly(upper);
            if (upperBodyOnly && performerCount >= 3) Label("P1: left hand moves; right hand raises the elephant. P2: left/right hands swing left/right front legs and leaning turns. P3 gestures use the HAND ZONE: fist trunk, Victory drink, thumbs ears.");
            else if (upperBodyOnly) Label("Keep shoulders and hands visible. Raise hands for legs; tilt shoulders for steering, head and tail. Solo: left hand also drives travel; right hand curls trunk.");
            int mode = GUILayout.Toolbar((int)inputMode, new[] { "Test sliders", "Camera" });
            if (mode != (int)inputMode) SetInputMode((InputMode)mode);
            bool gestures = GUILayout.Toggle(enableGestureActions, "Enable hand gesture actions");
            if (gestures != enableGestureActions) { enableGestureActions = gestures; GetComponent<DigiPhantLocomotion>()?.ClearGestureActions(); ClearGesture(); }
            Label(Status);
            var locomotion = GetComponent<DigiPhantLocomotion>();
            if (locomotion != null && !string.IsNullOrEmpty(locomotion.GestureStatus)) Label(locomotion.GestureStatus);
            locomotion?.DrawControls(width - 32);
            if (inputMode == InputMode.Camera)
            {
                for (int i = 1; i <= performerCount; i++)
                    Label("Performer " + i + (IsTracked(i, Time.realtimeSinceStartup) ? " · visible" : " · waiting / lost"));
                if (!IsCalibrated) Label("Set neutral pose to begin controlling the elephant.");
                if (CalibrationPending)
                {
                    int seconds = Mathf.Max(0, Mathf.CeilToInt(calibrationDeadline - Time.realtimeSinceStartup));
                    Label("Setting neutral pose in " + seconds + " seconds — hold your resting pose.");
                    if (GUILayout.Button("Cancel countdown")) CancelCalibrationCountdown();
                }
                else if (GUILayout.Button("Set neutral pose (10 seconds)"))
                    BeginCalibrationCountdown(Time.realtimeSinceStartup);
                if (GUILayout.Button("Reassign people")) ReassignPeople();
                Label("Hold a comfortable pose, then set neutral.");
            }
            else
            {
                Label("Move a slider to test its elephant part.");
                foreach (var c in controls)
                {
                    Label(c.label + "  · P" + c.performer);
                    c.testValue = GUILayout.HorizontalSlider(c.testValue, -1, 1);
                }
                if (GUILayout.Button("Reset body sliders")) ResetControls();
            }
            GUILayout.Space(8);
            Label("To adapt: stop Play, select DigiPhant Controls,\nthen edit Controls in the Inspector.");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}

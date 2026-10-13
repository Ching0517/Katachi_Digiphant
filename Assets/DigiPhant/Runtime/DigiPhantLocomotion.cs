using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DigiPhant
{
    public enum SignalCombination { Sum, Average }
    [Serializable] public class MovementSource
    {
        [Range(1, 4)] public int performer = 1;
        public Movement movement;
        [Range(-2, 2)] public float weight = 1;
    }
    [Serializable] public class MovementDriver
    {
        public SignalCombination combination = SignalCombination.Average;
        public MovementSource[] sources = Array.Empty<MovementSource>();
        [Range(0, 3)] public float sensitivity = 1;
        [Range(0, .5f)] public float deadZone = .12f;
        public bool TryRead(DigiPhantController controller, float now, out float value)
        {
            value = 0;
            float totalWeight = 0;
            foreach (var source in sources)
            {
                if (source == null || Mathf.Abs(source.weight) < .0001f) continue;
                if (!controller.TryReadMovement(source.performer, source.movement, now, out float input)) return false;
                value += input * source.weight;
                totalWeight += Mathf.Abs(source.weight);
            }
            if (totalWeight == 0) return true; // An empty mapping leaves this channel at zero.
            if (combination == SignalCombination.Average) value /= totalWeight;
            value = Mathf.Clamp(value * sensitivity, -1, 1);
            value = Mathf.Abs(value) <= deadZone ? 0 : Mathf.Sign(value) * Mathf.InverseLerp(deadZone, 1, Mathf.Abs(value));
            return true;
        }
    }

    public class DigiPhantLocomotion : MonoBehaviour
    {
        public bool enableLocomotion = true;
        public Transform travelRoot;
        public Animator elephantAnimator;
        public Transform animationMotionRoot;
        public Camera followCamera;
        public bool followElephant = true;
        public AnimationClip idle, walk, run, backward, turnLeft, turnRight, walkLeft, walkRight, runLeft, runRight;
        [Header("Gesture actions")]
        public bool enableGestureActions = true;
        [Tooltip("The one-shot vendor clip played by a Victory gesture.")]
        public AnimationClip drink;
        [Range(.05f, 1)] public float gestureBlendSeconds = .2f;
        [Range(-1, 1)] public float thumbUpLeftEarValue = 1;
        [Range(-1, 1)] public float thumbDownRightEarValue = 1;
        [Range(.5f, 6)] public float earFlapFrequency = 2.5f;
        [Range(-1, 1)] public float closedFistTrunkValue = 1;
        [Header("P1 standing (calibrated right-hand height)")]
        [Tooltip("Enter standing when P1's right-hand-height delta rises above this normalized value. In upper-body mode this is shoulder-midpoint minus wrist height, divided by shoulder width.")]
        [Range(.1f, 2)] public float standEnterThreshold = .6f;
        [Tooltip("Leave standing only below this lower threshold. The gap prevents rapid switching at the boundary.")]
        [Range(0, 1.5f)] public float standExitThreshold = .35f;
        [Range(.05f, 1)] public float standBlendSeconds = .6f;
        [Range(.05f, 1)] public float standInputSmoothingSeconds = .15f;
        [Range(0, 70)] public float standSpineDegrees = 45;
        [Range(-1, 1)] public float testStand;
        public MovementDriver forward = new MovementDriver { sources = new[] { new MovementSource { movement = Movement.LeftHandHeight } } };
        // In the three-performer upper-body arrangement P1 drives travel speed
        // and P2's shoulder tilt steers the shared travel root.
        public MovementDriver steering = new MovementDriver { sources = new[] { new MovementSource { performer = 2, movement = Movement.Lean } } };
        [Min(.01f)] public float walkSpeed = 1.8f;
        [Min(.01f)] public float runSpeed = 4.5f;
        [Min(.01f)] public float backwardSpeed = 1.2f;
        [Min(0)] public float turnDegreesPerSecond = 60;
        [Range(.2f, .95f)] public float runThreshold = .65f;
        [Min(.01f)] public float animationBlendSeconds = .25f;
        [Range(0, 20)] public float inputSmoothing = 6;
        [Tooltip("Maximum travel distance from the starting point, in Unity units. Zero disables the limit.")]
        [Min(0)] public float stageRadius = 15;
        [Range(-1, 1)] public float testForward;
        [Range(-1, 1)] public float testSteering;
        public string CurrentAction { get; private set; } = "Idle";
        public string GestureStatus { get; private set; }
        public float CurrentSpeed { get; private set; }
        public bool AnimationActive => graph.IsValid();
        public bool IsDrinkActive => enableLocomotion && (drinkWeight > .001f || drinkRequested);
        public float DrinkWeight => Mathf.Clamp01(drinkWeight);
        public float BoneOverlayWeight => 1 - DrinkWeight;
        public float AnimationWeightSum { get; private set; }
        public bool IsStandingRequested => standRequested;
        public float StandWeight => standWeight;
        bool GestureActionsActive => enableGestureActions && controller != null &&
            controller.enableGestureActions && controller.inputMode == InputMode.Camera;
        DigiPhantController controller;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables;
        AnimationClip[] clips;
        AnimationClip graphDrink;
        float[] gaitWeights;
        readonly string[] actions = { "Idle", "Walk", "Run", "Backward", "Turn left", "Turn right", "Walk left", "Walk right", "Run left", "Run right" };
        readonly Dictionary<Transform, Pose> restPose = new Dictionary<Transform, Pose>();
        Vector3 startPosition, cameraOffset, cameraStart, motionPosition;
        Quaternion startRotation, motionRotation;
        bool initialised;
        bool previousAnimatorEnabled, previousRootMotion;
        AnimatorCullingMode previousCulling;
        RuntimeAnimatorController previousController;
        float throttle, turn, cycle;
        string activeGesture = "None";
        float leftEarGestureValue, rightEarGestureValue, trunkGestureValue, drinkWeight, drinkTime, earFlapPhase;
        bool drinkRequested, victoryLatched, victoryPending;
        bool standRequested, standInputValid = true;
        float standInput, standWeight;
        Transform[] standSpineBones;
        Vector3[] standSpineAxes;
        SkinnedMeshRenderer standingRenderer;
        Bounds originalRendererBounds;

        void OnEnable() { controller = GetComponent<DigiPhantController>(); }
        void OnValidate()
        {
            standExitThreshold = Mathf.Min(standExitThreshold, standEnterThreshold - .01f);
        }
        public void SetGesture(string gesture, bool freshSnapshot)
        {
            if (!GestureActionsActive || !freshSnapshot)
            {
                activeGesture = "None";
                drinkRequested = false;
                victoryPending = false;
                return;
            }
            activeGesture = gesture ?? "None";
            if (activeGesture != "Victory") victoryLatched = false;
            if (activeGesture == "Victory" && !victoryLatched && !victoryPending)
            {
                if (!enableLocomotion) GestureStatus = "Locomotion is disabled; drink action is unavailable.";
                else if (drink != null) { victoryPending = true; GestureStatus = null; }
                else GestureStatus = "Drink clip is not assigned; continuing locomotion.";
            }
            if (activeGesture != "Victory") { drinkRequested = false; victoryPending = false; }
        }
        public void ClearGestureActions()
        {
            activeGesture = "None";
            leftEarGestureValue = rightEarGestureValue = trunkGestureValue = 0;
            drinkWeight = drinkTime = 0;
            earFlapPhase = 0;
            drinkRequested = victoryLatched = victoryPending = false;
            GestureStatus = null;
        }
        public void ForceLower()
        {
            standRequested = false;
            standInput = 0;
        }
        public bool ShouldGestureOwn(string controlLabel)
        {
            return GestureActionsActive && (controlLabel == "Left ear" || controlLabel == "Right ear" ||
                controlLabel == "Trunk curl");
        }
        public float GestureValueFor(string controlLabel)
        {
            if (!GestureActionsActive) return 0;
            return controlLabel == "Left ear" ? leftEarGestureValue :
                controlLabel == "Right ear" ? rightEarGestureValue :
                controlLabel == "Trunk curl" ? trunkGestureValue : 0;
        }
        public bool Initialise()
        {
            if (initialised) return true;
            if (!travelRoot || !elephantAnimator || !animationMotionRoot || !idle || !walk || !run || !backward || !turnLeft || !turnRight)
            { CurrentAction = "Locomotion setup incomplete"; return false; }
            controller = GetComponent<DigiPhantController>();
            startPosition = travelRoot.position;
            startRotation = travelRoot.rotation;
            if (followCamera) { cameraStart = followCamera.transform.position; cameraOffset = cameraStart - startPosition; }
            motionPosition = animationMotionRoot.localPosition;
            motionRotation = animationMotionRoot.localRotation;
            foreach (var bone in elephantAnimator.GetComponentsInChildren<Transform>())
                restPose[bone] = new Pose(bone.localPosition, bone.localRotation);
            standSpineBones = new[] { "elephant_Spine1_bone", "elephant_Spine2_bone", "elephant_Spine3_bone" }
                .Select(name => elephantAnimator.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == name)).Where(t => t != null).ToArray();
            standSpineAxes = standSpineBones.Select(t => t.InverseTransformDirection(travelRoot.right).normalized).ToArray();
            standingRenderer = elephantAnimator.GetComponentInChildren<SkinnedMeshRenderer>();
            if (standingRenderer) originalRendererBounds = standingRenderer.localBounds;
            initialised = true;
            return true;
        }
        bool StartAnimation()
        {
            if (graph.IsValid()) return true;
            if (!Initialise()) return false;
            previousAnimatorEnabled = elephantAnimator.enabled;
            previousRootMotion = elephantAnimator.applyRootMotion;
            previousCulling = elephantAnimator.cullingMode;
            previousController = elephantAnimator.runtimeAnimatorController;
            ExpandStandingBounds();
            elephantAnimator.runtimeAnimatorController = null;
            elephantAnimator.applyRootMotion = false;
            elephantAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            elephantAnimator.enabled = true;
            graph = PlayableGraph.Create("DigiPhant locomotion");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clips = new[] { idle, walk, run, backward, turnLeft, turnRight, walkLeft ? walkLeft : walk,
                walkRight ? walkRight : walk, runLeft ? runLeft : run, runRight ? runRight : run };
            mixer = AnimationMixerPlayable.Create(graph, clips.Length + (drink ? 1 : 0));
            playables = new AnimationClipPlayable[mixer.GetInputCount()];
            gaitWeights = new float[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetApplyFootIK(false);
                graph.Connect(playables[i], 0, mixer, i);
                mixer.SetInputWeight(i, i == 0 ? 1 : 0);
                gaitWeights[i] = i == 0 ? 1 : 0;
            }
            if (drink)
            {
                int drinkIndex = clips.Length;
                playables[drinkIndex] = AnimationClipPlayable.Create(graph, drink);
                playables[drinkIndex].SetApplyFootIK(false);
                graph.Connect(playables[drinkIndex], 0, mixer, drinkIndex);
                mixer.SetInputWeight(drinkIndex, 0);
            }
            graphDrink = drink;
            var output = AnimationPlayableOutput.Create(graph, "Elephant gait", elephantAnimator);
            output.SetSourcePlayable(mixer);
            graph.Play();
            return true;
        }
        public bool InputsVisible(float now, out string issue)
        {
            issue = null;
            if (!enableLocomotion) return true;
            if (controller == null) controller = GetComponent<DigiPhantController>();
            if (!DriverInputsVisible(forward, "Forward", now, out issue)) return false;
            if (!DriverInputsVisible(steering, "Steering", now, out issue)) return false;
            if (controller.TryGetMovementReadinessIssue(1, Movement.RightHandHeight, now, out string standingIssue))
            {
                issue = "Standing input: " + standingIssue;
                return false;
            }
            return true;
        }
        bool DriverInputsVisible(MovementDriver driver, string label, float now, out string issue)
        {
            foreach (var source in driver.sources)
            {
                if (source == null || Mathf.Abs(source.weight) <= .0001f) continue;
                if (controller.TryGetMovementReadinessIssue(source.performer, source.movement, now, out string sourceIssue))
                {
                    issue = label + " input: " + sourceIssue;
                    return false;
                }
            }
            issue = null;
            return true;
        }
        public void ClampPerformers(int count)
        {
            foreach (var driver in new[] { forward, steering })
                foreach (var source in driver.sources)
                    if (source != null) source.performer = Mathf.Clamp(source.performer, 1, count);
            StopMotion();
        }
        public void StopMotion()
        {
            testForward = testSteering = throttle = turn = CurrentSpeed = 0;
            CurrentAction = "Idle";
        }
        public bool Evaluate(float now, float dt)
        {
            dt = Mathf.Clamp(dt, 0, .1f);
            float gestureBlend = 1 - Mathf.Exp(-dt / Mathf.Max(.01f, gestureBlendSeconds));
            earFlapPhase = Mathf.Repeat(earFlapPhase + dt * earFlapFrequency, 1);
            float earFlap = Mathf.Sin(earFlapPhase * Mathf.PI * 2);
            float desiredLeftEar = activeGesture == "Thumb_Up" ? thumbUpLeftEarValue * earFlap : 0;
            float desiredRightEar = activeGesture == "Thumb_Down" ? thumbDownRightEarValue * earFlap : 0;
            float desiredTrunk = activeGesture == "Closed_Fist" ? closedFistTrunkValue : 0;
            leftEarGestureValue = Mathf.Lerp(leftEarGestureValue, desiredLeftEar, gestureBlend);
            rightEarGestureValue = Mathf.Lerp(rightEarGestureValue, desiredRightEar, gestureBlend);
            trunkGestureValue = Mathf.Lerp(trunkGestureValue, desiredTrunk, gestureBlend);
            UpdateStandingInput(now, dt);
            // Standing has priority over a full-body drink clip. Fade it out as
            // soon as the raised-hand state is requested, before applying bones.
            if (standRequested && drinkRequested) { drinkRequested = false; victoryPending = false; }
            if (!enableLocomotion)
            {
                drinkRequested = false;
                drinkWeight = 0;
                StopAnimation();
                return false;
            }
            // The inspector can change this reference while the graph is live.
            // Rebuild before indexing its optional drink input.
            if (graph.IsValid() && graphDrink != drink) StopAnimation();
            if (!StartAnimation())
            {
                drinkRequested = false;
                drinkWeight = 0;
                GestureStatus = "Locomotion setup is incomplete; drink action is unavailable.";
                return false;
            }
            if (!drink && (drinkRequested || victoryPending))
            {
                drinkRequested = false;
                drinkWeight = 0;
                GestureStatus = "Drink clip is not assigned; continuing locomotion.";
            }
            else if (drinkRequested && drinkTime >= drink.length) drinkRequested = false;
            if (victoryPending && !drinkRequested && standWeight < .001f && !standRequested && activeGesture == "Victory")
            {
                drinkRequested = true;
                victoryPending = false;
                victoryLatched = true; // Consume only when the one-shot actually begins.
                drinkTime = 0;
            }
            drinkWeight = Mathf.Lerp(drinkWeight, drinkRequested ? 1 : 0, gestureBlend);
            if (drink)
            {
                int drinkIndex = clips.Length;
                playables[drinkIndex].SetTime(Mathf.Min(drinkTime, Mathf.Max(0, drink.length - .0001f)));
                mixer.SetInputWeight(drinkIndex, DrinkWeight);
                if (drinkRequested) drinkTime += dt;
            }
            float desiredForward = testForward, desiredTurn = testSteering;
            bool valid = true;
            if (controller.inputMode == InputMode.Camera)
            {
                bool moving = forward.TryRead(controller, now, out desiredForward);
                bool turning = steering.TryRead(controller, now, out desiredTurn);
                valid = moving && turning;
            }
            // A raised or lost P1 right-hand signal freezes the travel root on
            // this frame. Travel resumes only after the body has fully lowered.
            bool standLocksTravel = standRequested || standWeight > .001f || !standInputValid;
            if (!valid || IsDrinkActive || standLocksTravel) { throttle = turn = 0; }
            else
            {
                float blend = inputSmoothing <= 0 ? 1 : 1 - Mathf.Exp(-inputSmoothing * dt);
                throttle = Mathf.Lerp(throttle, Mathf.Clamp(desiredForward, -1, 1), blend);
                turn = Mathf.Lerp(turn, Mathf.Clamp(desiredTurn, -1, 1), blend);
            }
            if (Mathf.Abs(throttle) < .015f) throttle = 0;
            if (Mathf.Abs(turn) < .015f) turn = 0;
            bool running = throttle > runThreshold;
            CurrentSpeed = throttle < 0 ? throttle * backwardSpeed : throttle <= runThreshold
                ? throttle / runThreshold * walkSpeed : Mathf.Lerp(walkSpeed, runSpeed, Mathf.InverseLerp(runThreshold, 1, throttle));
            int action = throttle < 0 ? 3 : throttle > 0 ? (running ? 2 : 1) : turn < 0 ? 4 : turn > 0 ? 5 : 0;
            if (throttle > 0 && Mathf.Abs(turn) > .15f) action = running ? (turn < 0 ? 8 : 9) : (turn < 0 ? 6 : 7);
            CurrentAction = IsDrinkActive ? "Drink water" : actions[action];
            float animationRate = action == 0 ? 1 : throttle != 0
                ? Mathf.Max(.15f, Mathf.Abs(CurrentSpeed) / (throttle < 0 ? backwardSpeed : running ? runSpeed : walkSpeed))
                : Mathf.Max(.15f, Mathf.Abs(turn) * turnDegreesPerSecond / 18.75f);
            cycle += dt * animationRate / Mathf.Max(.01f, clips[action].length);
            cycle = Mathf.Repeat(cycle, 1);
            float weightBlend = 1 - Mathf.Exp(-dt / Mathf.Max(.01f, animationBlendSeconds));
            for (int i = 0; i < clips.Length; i++)
            {
                gaitWeights[i] = Mathf.Lerp(gaitWeights[i], i == action ? 1 : 0, weightBlend);
                mixer.SetInputWeight(i, gaitWeights[i] * (1 - DrinkWeight));
                playables[i].SetTime(cycle * clips[i].length);
            }
            AnimationWeightSum = 0;
            for (int i = 0; i < mixer.GetInputCount(); i++) AnimationWeightSum += mixer.GetInputWeight(i);
            graph.Evaluate(0);
            ApplyStandingPose();
            // The travel parent alone owns world movement. Remove exported motion-node drift.
            animationMotionRoot.localPosition = motionPosition;
            animationMotionRoot.localRotation = motionRotation;
            travelRoot.Rotate(Vector3.up, turn * turnDegreesPerSecond * dt, Space.World);
            Vector3 destination = travelRoot.position + travelRoot.forward * CurrentSpeed * dt;
            if (stageRadius > 0)
            {
                Vector3 offset = destination - startPosition;
                offset.y = 0;
                if (offset.magnitude > stageRadius)
                {
                    destination = startPosition + Vector3.ClampMagnitude(offset, stageRadius);
                    CurrentAction += " · stage edge";
                }
            }
            destination.y = startPosition.y;
            travelRoot.position = destination;
            if (followElephant && followCamera) followCamera.transform.position = destination + cameraOffset;
            return true;
        }
        void UpdateStandingInput(float now, float dt)
        {
            standExitThreshold = Mathf.Min(standExitThreshold, standEnterThreshold - .01f);
            float raw = testStand;
            standInputValid = true;
            if (controller != null && controller.inputMode == InputMode.Camera)
                standInputValid = controller.TryReadMovement(1, Movement.RightHandHeight, now, out raw);
            if (!standInputValid) { standRequested = false; raw = 0; }
            float inputBlend = 1 - Mathf.Exp(-dt / Mathf.Max(.01f, standInputSmoothingSeconds));
            standInput = Mathf.Lerp(standInput, raw, inputBlend);
            // Raw values use the same Schmitt trigger on both edges. Filtering
            // is only for diagnostics/visual smoothing, so a held high input
            // cannot spuriously unlatch on the next frame.
            if (!standRequested && raw >= standEnterThreshold) standRequested = true;
            else if (standRequested && raw <= standExitThreshold && standInput <= standExitThreshold) standRequested = false;
            // Travel locks immediately, but the bone layer waits for a drink
            // clip to fade completely so the raised pose never pops in.
            float target = standRequested && drinkWeight < .001f ? 1 : 0;
            // A bounded transition reaches neutral in the configured duration;
            // an exponential tail would otherwise keep travel locked for seconds.
            standWeight = Mathf.MoveTowards(standWeight, target, dt / Mathf.Max(.01f, standBlendSeconds));
        }
        void ApplyStandingPose()
        {
            if (standWeight <= 0 || drinkWeight >= .001f || standSpineBones == null || standSpineBones.Length == 0) return;
            // These are deforming spine bones. The travel root, main control and
            // pelvis stay untouched, so standing cannot move the elephant or camera.
            float[] distribution = { .65f, .25f, .1f };
            for (int i = 0; i < standSpineBones.Length; i++)
                standSpineBones[i].localRotation *= Quaternion.AngleAxis(-standSpineDegrees * Mathf.SmoothStep(0, 1, standWeight) * distribution[Mathf.Min(i, 2)], standSpineAxes[i]);
        }
        void ExpandStandingBounds()
        {
            if (!standingRenderer) return;
            // The graph can be rebuilt after inspector clip changes, so apply
            // this every graph start and restore only when the graph stops.
            // Imported rigs can use a root-bone coordinate system whose Y axis
            // is not world-up. Expand symmetrically in local bounds space.
            standingRenderer.localBounds = new Bounds(originalRendererBounds.center, originalRendererBounds.size * 3f);
        }
        public void ResetPosition()
        {
            if (!Initialise()) return;
            StopMotion();
            travelRoot.SetPositionAndRotation(startPosition, startRotation);
            if (followCamera) followCamera.transform.position = cameraStart;
            controller.InvalidateCalibration();
        }
        public void StopAnimation()
        {
            if (!graph.IsValid()) return;
            graph.Destroy();
            graphDrink = null;
            if (elephantAnimator)
            {
                elephantAnimator.runtimeAnimatorController = previousController;
                elephantAnimator.applyRootMotion = previousRootMotion;
                elephantAnimator.cullingMode = previousCulling;
                elephantAnimator.enabled = previousAnimatorEnabled;
            }
            foreach (var pair in restPose)
                if (pair.Key) { pair.Key.localPosition = pair.Value.position; pair.Key.localRotation = pair.Value.rotation; }
            if (standingRenderer) standingRenderer.localBounds = originalRendererBounds;
            StopMotion();
        }
        void OnDisable()
        {
            ClearGestureActions();
            StopAnimation();
            if (initialised)
            {
                if (travelRoot) travelRoot.SetPositionAndRotation(startPosition, startRotation);
                if (followCamera) followCamera.transform.position = cameraStart;
            }
            initialised = false;
            restPose.Clear();
        }
        public void DrawControls(float width)
        {
            var label = new GUIStyle(GUI.skin.label) { wordWrap = true };
            bool selected = GUILayout.Toggle(enableLocomotion, "Enable locomotion");
            if (selected != enableLocomotion) { enableLocomotion = selected; StopMotion(); ClearGestureActions(); }
            if (!enableLocomotion) return;
            GUILayout.Label(CurrentAction + " | " + Mathf.Abs(CurrentSpeed).ToString("0.0") + " units/s", label, GUILayout.Width(width));
            if (controller.inputMode == InputMode.TestSliders)
            {
                GUILayout.Label("Travel: backward / stop / forward", label, GUILayout.Width(width));
                testForward = GUILayout.HorizontalSlider(testForward, -1, 1);
                GUILayout.Label("Turn: left / straight / right", label, GUILayout.Width(width));
                testSteering = GUILayout.HorizontalSlider(testSteering, -1, 1);
                GUILayout.Label("P1 right hand: lower / stand", label, GUILayout.Width(width));
                testStand = GUILayout.HorizontalSlider(testStand, 0, 1);
                if (GUILayout.Button("Stop moving")) StopMotion();
            }
            else GUILayout.Label("Speed and steering use the movement mappings in the Inspector.", label, GUILayout.Width(width));
            if (GUILayout.Button("Return to starting position")) ResetPosition();
            GUILayout.Space(8);
        }
    }
}

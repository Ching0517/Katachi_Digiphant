using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiPhant.Editor
{
    public static class DigiPhantLocomotionValidation
    {
        public static void RunAll() { DigiPhantSetup.Validate(); Run(); }
        public static void Run()
        {
            EditorSceneManager.OpenScene(DigiPhantSetup.ScenePath);
            var c = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            var loco = c.GetComponent<DigiPhantLocomotion>();
            // The template may retain an older serialized steering assignment.
            // Set up the current three-person role fixture without changing any
            // compound mappings used by later validation.
            c.SetPerformerCount(3);
            if (loco.steering.sources.Length == 1 && loco.steering.sources[0].movement == Movement.Lean)
                loco.steering.sources[0].performer = 2;
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
            void Step(int count, float start = 0) { for (int i = 0; i < count; i++) c.ApplyControls(start + i / 60f, 1 / 60f); }
            Check(loco && loco.Initialise(), "Missing locomotion setup");
            c.inputMode = InputMode.TestSliders;
            c.CaptureRestPose();
            loco.inputSmoothing = 0;
            loco.animationBlendSeconds = .01f;
            Vector3 origin = loco.travelRoot.position;
            Quaternion initialRotation = loco.travelRoot.rotation;
            Vector3 cameraOrigin = loco.followCamera.transform.position;
            Vector3 modelLocalPosition = loco.elephantAnimator.transform.localPosition;
            var leg = c.controls[0].bones[0];
            loco.testForward = .4f;
            Step(15);
            Quaternion firstLeg = leg.localRotation;
            Step(30, .25f);
            Check(Quaternion.Angle(firstLeg, leg.localRotation) > 3, "Walk clip did not animate the leg");
            Step(15, .75f);
            float walkDistance = Vector3.Distance(origin, loco.travelRoot.position);
            Check(walkDistance > .9f && walkDistance < 1.3f && loco.CurrentAction == "Walk", "Walk world speed incorrect");
            Check(Vector3.Distance(loco.elephantAnimator.transform.localPosition, modelLocalPosition) < .01f, "Clip moved the model outside the travel parent");
            Check(Vector3.Distance(loco.followCamera.transform.position - cameraOrigin, loco.travelRoot.position - origin) < .001f, "Follow camera lost offset");
            loco.ResetPosition();
            loco.testForward = 1;
            Step(60);
            Check(Vector3.Distance(origin, loco.travelRoot.position) > walkDistance * 2 && loco.CurrentAction == "Run", "Run failed");
            loco.ResetPosition();
            loco.testForward = -.5f;
            Step(60);
            Check(Vector3.Dot(loco.travelRoot.position - origin, initialRotation * Vector3.forward) < -.5f && loco.CurrentAction == "Backward", "Backward failed");
            loco.ResetPosition();
            loco.testSteering = .75f;
            Step(60);
            Check(Vector3.Distance(origin, loco.travelRoot.position) < .001f, "Stationary turn translated the elephant");
            Check(Quaternion.Angle(initialRotation, loco.travelRoot.rotation) > 40 && loco.CurrentAction == "Turn right", "Turn right failed");
            loco.testSteering = -.75f;
            Step(60);
            Check(Quaternion.Angle(initialRotation, loco.travelRoot.rotation) < .1f && loco.CurrentAction == "Turn left", "Turn left failed");
            loco.ResetPosition();
            loco.testForward = .4f;
            loco.testSteering = -.5f;
            Step(60);
            Check(loco.CurrentAction == "Walk left" && Vector3.Distance(origin, loco.travelRoot.position) > .5f, "Walking and steering failed");

            // Three-person upper-body roles: P1 raises a left hand to travel,
            // P2's shoulder lean alone rotates the shared travel root, and P2's
            // hands independently own the two front legs.
            loco.ResetPosition();
            bool originalUpperBodyOnly = c.upperBodyOnly;
            c.SetUpperBodyOnly(true);
            c.inputMode = InputMode.Camera; // Deterministic packets; no receiver is needed.
            Check(loco.forward.sources.Length == 1 && loco.forward.sources[0].performer == 1 &&
                loco.forward.sources[0].movement == Movement.LeftHandHeight,
                "P1 must remain the default forward source");
            Check(loco.steering.sources.Length == 1 && loco.steering.sources[0].performer == 2 &&
                loco.steering.sources[0].movement == Movement.Lean,
                "P2 must be the default steering source");
            var p2LeftLeg = c.controls.Single(x => x.label == "Front left leg");
            var p2RightLeg = c.controls.Single(x => x.label == "Front right leg");
            Check(p2LeftLeg.performer == 2 && p2LeftLeg.movement == Movement.LeftHandHeight && !p2LeftLeg.disabled &&
                p2RightLeg.performer == 2 && p2RightLeg.movement == Movement.RightHandHeight && !p2RightLeg.disabled &&
                c.controls.Where(x => x.label.StartsWith("Rear ")).All(x => x.disabled),
                "P2 front-leg and passive rear-leg mappings are incorrect");
            PoseFrame ThreePersonFrame(float p1Hand, float p1Lean, float p2Lean) => new PoseFrame
            {
                version = 1,
                performerCount = 3,
                upperBodyOnly = true,
                people = new[]
                {
                    new PerformerFrame { slot = 1, values = new[] { p1Hand, 0f, 0f, 0f, p1Lean, 0f }, confidence = Enumerable.Repeat(1f, 6).ToArray() },
                    new PerformerFrame { slot = 2, values = new[] { 0f, 0f, 0f, 0f, p2Lean, 0f }, confidence = Enumerable.Repeat(1f, 6).ToArray() },
                    new PerformerFrame { slot = 3, values = new float[6], confidence = Enumerable.Repeat(1f, 6).ToArray() }
                }
            };
            Check(c.AcceptPacket(JsonUtility.ToJson(ThreePersonFrame(0, 0, 0)), 5), "Three-person neutral packet rejected");
            Check(c.CalibrateAt(5), "Three-person calibration failed");
            Check(c.AcceptPacket(JsonUtility.ToJson(ThreePersonFrame(.4f, .8f, 0)), 5.1f), "P1 travel packet rejected");
            c.ApplyControls(5.1f, .1f);
            Check(Quaternion.Angle(initialRotation, loco.travelRoot.rotation) < .1f &&
                Vector3.Distance(origin, loco.travelRoot.position) > .01f,
                "P1 lean changed steering or P1 hand stopped travel");
            Quaternion beforeP2Turn = loco.travelRoot.rotation;
            Check(c.AcceptPacket(JsonUtility.ToJson(ThreePersonFrame(.4f, .8f, .7f)), 5.2f), "P2 steering packet rejected");
            c.ApplyControls(5.2f, .1f);
            Check(Quaternion.Angle(beforeP2Turn, loco.travelRoot.rotation) > 1 &&
                loco.CurrentAction == "Walk right",
                "P2 lean did not steer the travelling elephant");
            c.SetUpperBodyOnly(originalUpperBodyOnly);
            c.inputMode = InputMode.TestSliders;
            loco.ResetPosition();

            var head = c.controls.Single(x => x.label == "Head turn");
            var trunk = c.controls.Single(x => x.label == "Trunk curl");
            head.smoothing = trunk.smoothing = 0;
            c.ApplyControls(10, 0);
            var baseHead = head.bones[0].localRotation;
            var baseTrunk = trunk.bones[0].localRotation;
            head.testValue = trunk.testValue = 1;
            c.ApplyControls(10, 0);
            var expectedHead = baseHead * Quaternion.AngleAxis(head.degrees, head.localAxis.normalized);
            var expectedTrunk = baseTrunk * Quaternion.AngleAxis(trunk.degrees, trunk.localAxis.normalized);
            Check(Quaternion.Angle(expectedHead, head.bones[0].localRotation) < .1f, "Head gesture overwrote or lost animation pose");
            Check(Quaternion.Angle(expectedTrunk, trunk.bones[0].localRotation) < .1f, "Trunk gesture lost during locomotion");
            for (int i = 0; i < 100; i++) c.ApplyControls(10, 0);
            Check(Quaternion.Angle(expectedHead, head.bones[0].localRotation) < .1f, "Gesture offsets accumulated");
            head.locomotionWeight = 0;
            c.ApplyControls(10, 0);
            Check(Quaternion.Angle(baseHead, head.bones[0].localRotation) < .1f, "Zero overlay weight failed");

            // Gesture fields are optional v1 packet data: failures neutralize only the
            // gesture channel and must not make an otherwise valid pose packet fail.
            loco.drink = AssetDatabase.LoadAllAssetsAtPath("Assets/Elephant/Animations/elephant@drink.fbx")
                .OfType<AnimationClip>().First(x => !x.name.StartsWith("__"));
            loco.gestureBlendSeconds = .05f;
            loco.earFlapFrequency = 2.5f;
            var leftEar = c.controls.Single(x => x.label == "Left ear");
            var rightEar = c.controls.Single(x => x.label == "Right ear");
            var headControl = c.controls.Single(x => x.label == "Head turn");
            PoseFrame GestureFrame(string gesture, float score) => new PoseFrame { version = 1,
                performerCount = c.performerCount, upperBodyOnly = c.upperBodyOnly, gesture = gesture,
                gestureScore = score, people = Array.Empty<PerformerFrame>() };
            c.inputMode = InputMode.Camera;
            Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Thumb_Up", .9f)), 20), "Thumb-up pose packet rejected");
            float minLeftEar = float.MaxValue, maxLeftEar = float.MinValue;
            for (int i = 0; i < 12; i++)
            {
                float now = 20.05f + i * .05f;
                Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Thumb_Up", .9f)), now), "Thumb-up pose packet rejected");
                c.ApplyControls(now, .05f);
                minLeftEar = Mathf.Min(minLeftEar, loco.GestureValueFor("Left ear"));
                maxLeftEar = Mathf.Max(maxLeftEar, loco.GestureValueFor("Left ear"));
            }
            Check(maxLeftEar - minLeftEar > .5f && Mathf.Abs(loco.GestureValueFor("Right ear")) < .01f,
                "Thumb-up did not flap only the left ear");
            Check(loco.GestureValueFor("Head turn") == 0 && !loco.ShouldGestureOwn("Head turn") &&
                loco.ShouldGestureOwn("Left ear") && loco.ShouldGestureOwn("Right ear"),
                "Gesture ownership should move from the head to the ears");
            Check(headControl.performer == 1 && leftEar.performer == 3 && rightEar.performer == 3,
                "Three-person head and ear assignments are incorrect");
            Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Thumb_Down", .9f)), 21), "Thumb-down pose packet rejected");
            float minRightEar = float.MaxValue, maxRightEar = float.MinValue;
            for (int i = 0; i < 12; i++)
            {
                float now = 21.05f + i * .05f;
                Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Thumb_Down", .9f)), now), "Thumb-down pose packet rejected");
                c.ApplyControls(now, .05f);
                minRightEar = Mathf.Min(minRightEar, loco.GestureValueFor("Right ear"));
                maxRightEar = Mathf.Max(maxRightEar, loco.GestureValueFor("Right ear"));
            }
            Check(maxRightEar - minRightEar > .5f && Mathf.Abs(loco.GestureValueFor("Left ear")) < .01f,
                "Thumb-down did not flap only the right ear");
            Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Closed_Fist", .9f)), 22), "Fist pose packet rejected");
            c.ApplyControls(22, .2f);
            Check(loco.GestureValueFor("Trunk curl") > .5f, "Closed fist did not select trunk curl");
            Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Victory", .9f)), 23), "Victory pose packet rejected");
            c.ApplyControls(23, .1f);
            Check(loco.IsDrinkActive && loco.CurrentAction == "Drink water", "Victory did not start the drink one-shot");
            // Holding Victory must finish once, preserve normalized mixer weights,
            // and require a real release before it can start again.
            float completeAt = 23;
            for (int i = 0; i < Mathf.CeilToInt(loco.drink.length / .1f) + 12; i++)
            {
                completeAt += .1f;
                Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Victory", .9f)), completeAt), "Held Victory packet rejected");
                c.ApplyControls(completeAt, .1f);
                Check(Mathf.Abs(loco.AnimationWeightSum - 1) < .001f, "Drink mixer weights are not normalized");
            }
            Check(!loco.IsDrinkActive, "Held Victory restarted or failed to finish");
            c.ApplyControls(completeAt + .1f, .1f);
            Check(!loco.IsDrinkActive, "Completed Victory replayed while still held");
            Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("None", 0)), completeAt + .2f), "Gesture release packet rejected");
            c.ApplyControls(completeAt + .2f, .1f);
            Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Victory", .9f)), completeAt + .3f), "Victory retrigger packet rejected");
            c.ApplyControls(completeAt + .3f, .1f);
            Check(loco.IsDrinkActive, "Victory did not retrigger after release");
            Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("None", 0)), completeAt + .4f), "Drink cancel packet rejected");
            for (int i = 0; i < 8; i++) c.ApplyControls(completeAt + .5f + i * .1f, .1f);
            Check(!loco.IsDrinkActive && Mathf.Abs(loco.DrinkWeight) < .01f, "Gesture cancellation left drink active");
            Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Unknown", .9f)), 24), "Unknown gesture rejected valid pose");
            Check(c.CurrentGesture == "None", "Unknown gesture did not neutralize");
            Check(c.AcceptPacket(JsonUtility.ToJson(GestureFrame("Thumb_Down", 1.1f)), 25), "Invalid score rejected valid pose");
            Check(c.CurrentGesture == "None", "Invalid score did not neutralize");
            Check(c.AcceptPacket(JsonUtility.ToJson(new PoseFrame { version = 1, performerCount = c.performerCount,
                upperBodyOnly = c.upperBodyOnly, people = Array.Empty<PerformerFrame>() }), 26), "Old pose-only packet rejected");
            c.ApplyControls(27, .2f);
            Check(loco.GestureValueFor("Left ear") < .1f && loco.GestureValueFor("Right ear") < .1f &&
                loco.GestureValueFor("Trunk curl") < .1f,
                "Missing or stale gesture did not return joints to neutral");
            loco.ResetPosition();
            loco.stageRadius = .2f;
            c.SetInputMode(InputMode.TestSliders);
            loco.testForward = 1;
            Step(120);
            float limitedDistance = Vector3.Distance(origin, loco.travelRoot.position);
            Check(limitedDistance >= .19f && limitedDistance <= .201f, "Stage limit failed to constrain travel at its boundary");
            loco.ResetPosition();
            loco.stageRadius = 15;

            c.SetPerformerCount(2);
            c.inputMode = InputMode.Camera; // No receiver needed for deterministic packet tests.
            loco.forward.sources = new[] { new MovementSource { performer = 1, movement = Movement.LeftHandHeight },
                new MovementSource { performer = 2, movement = Movement.LeftHandHeight } };
            loco.forward.deadZone = loco.steering.deadZone = 0;
            PoseFrame Frame(float first, float second) => new PoseFrame { version = 1, performerCount = 2,
                people = new[] { new PerformerFrame { slot = 1, values = new[] { first, 0f, 0f, 0f, 0f, 0f }, confidence = Enumerable.Repeat(1f, 6).ToArray() },
                    new PerformerFrame { slot = 2, values = new[] { second, 0f, 0f, 0f, 0f, 0f }, confidence = Enumerable.Repeat(1f, 6).ToArray() } } };
            var emptyDriver = new MovementDriver();
            Check(emptyDriver.TryRead(c, 100, out float emptyValue) && emptyValue == 0, "Empty mapping must leave a channel neutral");
            c.AcceptPacket(JsonUtility.ToJson(Frame(0, 0)), 100);
            Check(c.CalibrateAt(100), "Compound mapping calibration failed");
            c.AcceptPacket(JsonUtility.ToJson(Frame(.2f, .8f)), 100);
            c.ApplyControls(100, 1 / 60f);
            Check(Mathf.Abs(loco.CurrentSpeed - .5f / loco.runThreshold * loco.walkSpeed) < .01f, "Average of two performers is incorrect");
            loco.forward.combination = SignalCombination.Sum;
            c.ApplyControls(100, 1 / 60f);
            Check(Mathf.Abs(loco.CurrentSpeed - loco.runSpeed) < .01f, "Sum of two performers is incorrect");
            var missing = Frame(.2f, 0);
            missing.people = missing.people.Take(1).ToArray();
            c.AcceptPacket(JsonUtility.ToJson(missing), 100.1f);
            Vector3 beforeLoss = loco.travelRoot.position;
            c.ApplyControls(100.1f, 1 / 60f);
            Check(loco.CurrentSpeed == 0 && Vector3.Distance(beforeLoss, loco.travelRoot.position) < .001f, "Tracking loss did not stop travel immediately");
            c.ApplyControls(102, 1 / 60f);
            Check(loco.CurrentSpeed == 0, "Stale tracking restarted travel");
            c.AcceptPacket(JsonUtility.ToJson(Frame(0, 0)), 103);
            Check(c.CalibrateAt(103), "Recalibration setup failed");
            c.inputMode = InputMode.TestSliders;
            c.SendMessage("OnEnable");
            Check(!c.IsCalibrated && loco.CurrentSpeed == 0, "Re-entering Play retained stale movement/calibration");
            loco.ResetPosition();
            Check(Vector3.Distance(origin, loco.travelRoot.position) < .001f && !c.IsCalibrated, "Reset position/calibration failed");
            loco.enableLocomotion = false;
            c.ApplyControls(103, 1 / 60f);
            Check(!loco.AnimationActive && !loco.elephantAnimator.enabled, "Locomotion shutdown failed");
            EditorSceneManager.OpenScene(DigiPhantSetup.ScenePath);
            Debug.Log("DIGIPHANT_LOCOMOTION_VALIDATION_OK: gaits, world travel, turns, additive gestures, no accumulation, compound signals, tracking loss, limits, reset");
        }
    }
}

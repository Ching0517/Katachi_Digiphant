using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiPhant.Editor
{
    // Exercises the real packet/calibration path without Play mode or camera access.
    public static class DigiPhantStandingValidation
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        [MenuItem("DigiPhant/Validate Three Performer Body Controls")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scenes = EditorSceneManager.GetSceneManagerSetup();
            DigiPhantController c = null;
            DigiPhantLocomotion l = null;
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                c = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
                l = c ? c.GetComponent<DigiPhantLocomotion>() : null;
                Check(c && l && l.Initialise(), "Missing body-control setup");
                // Direct inputMode assignment intentionally avoids binding the live UDP port.
                c.inputMode = InputMode.Camera;
                c.SetPerformerCount(1);
                c.SetPerformerCount(3);
                Check(l.steering.sources.Length != 1 || l.steering.sources[0].movement != Movement.Lean ||
                    l.steering.sources[0].performer == 2, "Returning from solo did not restore P2 steering");
                c.SetUpperBodyOnly(true);
                c.CaptureRestPose();
                l.inputSmoothing = 0;
                float now = 100;
                PoseFrame Frame(float forward = 0, float stand = 0, float left = 0, float right = 0,
                    float steer = 0, string gesture = "None", float leftConfidence = 1, float standConfidence = 1,
                    float gestureConfidence = 1) => new PoseFrame
                {
                    version = 1, performerCount = 3, upperBodyOnly = true,
                    gesture = gesture, gestureScore = gesture == "None" ? 0 : gestureConfidence,
                    people = new[] {
                        new PerformerFrame { slot = 1, values = new[] { .2f + forward, .2f + stand, 0, 0, 0, 0 },
                            confidence = new[] { 1f, standConfidence, 1, 1, 1, 1 } },
                        new PerformerFrame { slot = 2, values = new[] { .1f + left, -.1f + right, 0, 0, steer, 0 },
                            confidence = new[] { leftConfidence, 1f, 1, 1, 1, 1 } },
                        new PerformerFrame { slot = 3, values = new float[6], confidence = Enumerable.Repeat(1f, 6).ToArray() }
                    }
                };
                void Tick(PoseFrame frame, int count = 1)
                {
                    for (int i = 0; i < count; i++)
                    {
                        now += 1f / 60;
                        if (frame != null) Check(c.AcceptPacket(JsonUtility.ToJson(frame), now), "Packet rejected");
                        c.ApplyControls(now, 1f / 60);
                    }
                }
                Tick(Frame());
                Check(c.CalibrateAt(now), "Calibration failed: " + c.Status);
                Check(c.TryReadMovement(1, Movement.RightHandHeight, now, out float delta) && Mathf.Abs(delta) < .001f,
                    "Standing input must subtract nonzero calibrated neutral");
                var leftControl = c.controls.Single(x => x.label == "Front left leg");
                var rightControl = c.controls.Single(x => x.label == "Front right leg");
                Check(leftControl.performer == 2 && leftControl.movement == Movement.LeftHandHeight && !leftControl.disabled &&
                    rightControl.performer == 2 && rightControl.movement == Movement.RightHandHeight && !rightControl.disabled,
                    "P2 front-hand mapping incorrect");
                Check(c.controls.Where(x => x.label.StartsWith("Rear ")).All(x => x.disabled), "Rear manual mappings remain active");
                var bones = l.elephantAnimator.GetComponentsInChildren<Transform>();
                var head = bones.Single(x => x.name == "elephant_Head_bone");
                var pelvis = bones.Single(x => x.name == "elephant_Pelvis_bone");
                var hind = bones.Where(x => x.name.Contains("Toe") && x.name.EndsWith("_bone")).ToArray();
                Check(hind.Length >= 2, "Hind support bones not found");
                Tick(Frame(forward: .4f), 30);
                Check(l.CurrentSpeed > 0, "P1 left hand failed to move");
                Tick(Frame(forward: -.4f), 30);
                Check(l.CurrentSpeed < 0, "P1 left hand failed to reverse");
                Tick(Frame(), 60);
                var root = l.travelRoot.position;
                var rootRotation = l.travelRoot.rotation;
                var headY = head.position.y;
                var pelvisPosition = pelvis.position;
                var hindPositions = hind.Select(x => x.position).ToArray();
                ExportSnapshot(l, "neutral");
                for (int i = 0; i < 40; i++)
                {
                    Tick(Frame(forward: .5f, stand: .8f, steer: .6f));
                    Check(l.IsStandingRequested && Mathf.Abs(l.CurrentSpeed) < .001f && Vector3.Distance(root, l.travelRoot.position) < .001f &&
                        Quaternion.Angle(rootRotation, l.travelRoot.rotation) < .01f, "Standing allowed travel/turn on frame " + (i + 1));
                }
                Check(l.StandWeight > .99f && head.position.y > headY + .7f, "Standing spine did not raise the head");
                Check(Mathf.Abs(l.travelRoot.position.y - root.y) < .001f && Vector3.Distance(pelvisPosition, pelvis.position) < .25f,
                    "Standing moved root/pelvis");
                for (int i = 0; i < hind.Length; i++) Check(Vector3.Distance(hindPositions[i], hind[i].position) < .25f,
                    "Standing displaced hind support bone");
                ExportSnapshot(l, "standing");
                var leftNeutralRotation = leftControl.bones[0].localRotation;
                var rightNeutralRotation = rightControl.bones[0].localRotation;
                var spine = bones.Single(x => x.name == "elephant_Spine1_bone");
                var spineRotation = spine.localRotation;
                Tick(Frame(stand: .8f), 120);
                Check(Quaternion.Angle(spineRotation, spine.localRotation) < 15, "Standing spine rotation accumulated over time");
                var meshCheck = new Mesh();
                try
                {
                    var renderer = l.elephantAnimator.GetComponentInChildren<SkinnedMeshRenderer>();
                    renderer.BakeMesh(meshCheck);
                    Check(meshCheck.vertices.All(v => renderer.transform.TransformPoint(v).y >= root.y - .1f), "Standing mesh penetrated the ground");
                    var meshBounds = meshCheck.bounds;
                    var boundsRoot = renderer.rootBone ? renderer.rootBone : renderer.transform;
                    Check(meshCheck.vertices.All(v => renderer.localBounds.Contains(boundsRoot.InverseTransformPoint(renderer.transform.TransformPoint(v)))),
                        "Standing mesh exceeds runtime culling bounds: renderer=" + renderer.localBounds + "; baked=" + meshBounds);
                }
                finally { UnityEngine.Object.DestroyImmediate(meshCheck); }
                Tick(Frame(stand: .8f, left: .7f), 60);
                Check(leftControl.current > .65f && Mathf.Abs(rightControl.current) < .01f, "P2 left hand affects wrong leg");
                Check(Quaternion.Angle(leftNeutralRotation, leftControl.bones[0].localRotation) > 5 &&
                    Quaternion.Angle(rightNeutralRotation, rightControl.bones[0].localRotation) < 8, "Left-hand input did not independently rotate its own front bone");
                Tick(Frame(stand: .8f, right: -.6f), 60);
                Check(Mathf.Abs(leftControl.current) < .01f && rightControl.current < -.55f, "P2 right hand affects wrong leg");
                Check(Quaternion.Angle(rightNeutralRotation, rightControl.bones[0].localRotation) > 5 &&
                    Quaternion.Angle(leftNeutralRotation, leftControl.bones[0].localRotation) < 8, "Right-hand input did not independently rotate its own front bone");
                Tick(Frame(stand: .8f, left: .7f, right: .4f, gesture: "Closed_Fist"), 60);
                Check(leftControl.current > .65f && rightControl.current > .35f && l.GestureValueFor("Trunk curl") > .9f && l.StandWeight > .99f,
                    "Standing, two front legs and Closed_Fist failed simultaneous control");
                ExportSnapshot(l, "standing-p2-legs-trunk");
                Tick(Frame(stand: .8f, left: .7f, right: .4f, gesture: "Closed_Fist", leftConfidence: .1f), 60);
                Check(Mathf.Abs(leftControl.current) < .01f && rightControl.current > .35f && l.IsStandingRequested,
                    "P2 single-hand confidence loss affected other channels");
                Tick(Frame(stand: .8f, gesture: "Closed_Fist", gestureConfidence: .1f), 120);
                Check(Mathf.Abs(l.GestureValueFor("Trunk curl")) < .01f && l.IsStandingRequested, "Low-confidence gesture did not neutralize independently");
                for (int i = 0; i < 60; i++) Tick(Frame(stand: i % 2 == 0 ? .44f : .48f));
                Check(l.IsStandingRequested, "Hysteresis toggled inside its band");
                Tick(Frame(forward: .5f, stand: 0), 120);
                Check(!l.IsStandingRequested && l.StandWeight < .001f && l.CurrentSpeed > 0, "Lowering did not restore travel");
                Tick(Frame(), 60);
                ExportSnapshot(l, "lowered");
                Tick(Frame(forward: .5f, stand: .8f, standConfidence: .1f), 60);
                Check(!l.IsStandingRequested && l.CurrentSpeed == 0, "Lost standing input allowed travel or triggered standing");
                Tick(Frame(stand: .8f, left: .5f, right: .5f, gesture: "Closed_Fist"), 60);
                Tick(null, 150);
                Check(!l.IsStandingRequested && l.StandWeight < .001f && l.CurrentSpeed == 0 &&
                    Mathf.Abs(leftControl.current) < .01f && Mathf.Abs(rightControl.current) < .01f &&
                    Mathf.Abs(l.GestureValueFor("Trunk curl")) < .01f, "Stale inputs did not safely lower/neutralize/stop");
                Tick(Frame(stand: .8f, gesture: "Victory"), 60);
                Check(!l.IsDrinkActive, "Victory interrupted standing");
                // Cancel an unplayed pending gesture by timeout, then recover while still standing.
                Tick(null, 90);
                Tick(Frame(stand: .8f, gesture: "Victory"), 60);
                Tick(Frame(gesture: "Victory"), 120);
                Check(l.IsDrinkActive, "Recovered pending Victory did not start after lowering");
                var lastSpineRotation = spine.localRotation;
                for (int i = 0; i < 150; i++)
                {
                    float priorStandWeight = l.StandWeight;
                    Tick(Frame(stand: .8f, gesture: "Victory"));
                    Check(l.CurrentSpeed == 0 && Mathf.Abs(l.StandWeight - priorStandWeight) <= 1f / (60 * l.standBlendSeconds) + .001f,
                        "Drink-to-standing transition jumped or allowed travel");
                    Check(Quaternion.Angle(lastSpineRotation, spine.localRotation) < 10,
                        "Drink-to-standing spine pose popped on frame " + i);
                    lastSpineRotation = spine.localRotation;
                }
                Check(l.StandWeight > .99f && !l.IsDrinkActive, "Standing did not cancel an active drink safely");
                Tick(Frame(gesture: "Victory"), 120);
                Check(!l.IsDrinkActive, "Consumed held Victory replayed after standing");
                Tick(Frame(), 60);
                Tick(Frame(gesture: "Victory"), 30);
                Check(l.IsDrinkActive, "Victory release/retrigger failed");
                Tick(Frame(), 150);
                Tick(Frame(gesture: "Thumb_Up"), 60);
                Check(Mathf.Abs(l.GestureValueFor("Left ear")) > .01f, "Left-ear gesture lost");
                Tick(Frame(gesture: "Thumb_Down"), 60);
                Check(Mathf.Abs(l.GestureValueFor("Right ear")) > .01f, "Right-ear gesture lost");
                Check(Mathf.Abs(l.AnimationWeightSum - 1) < .001f, "Animation weights are not normalized");
                l.StopAnimation();
                Tick(Frame(stand: .8f), 60);
                Check(l.StandWeight > .99f && head.position.y > headY + .7f, "Standing failed after graph restart");
            }
            finally
            {
                if (l) l.StopAnimation();
                if (c) c.SetInputMode(InputMode.TestSliders);
                if (scenes.Length > 0 && scenes.Any(x => x.isLoaded && x.isActive))
                    EditorSceneManager.RestoreSceneManagerSetup(scenes);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log("DIGIPHANT_STANDING_VALIDATION_OK: calibrated packets, immediate stop, hysteresis, independent legs/trunk, confidence/loss, Victory priority, geometry, graph restart");
        }
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static void ExportSnapshot(DigiPhantLocomotion l, string phase)
        {
            if (!Environment.GetCommandLineArgs().Contains("-standingSnapshots")) return;
            var renderer = l.elephantAnimator.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            try
            {
                renderer.BakeMesh(mesh);
                var data = new Snapshot {
                    vertices = mesh.vertices.Select(v => renderer.transform.TransformPoint(v)).Select(v => new V3 { x = v.x, y = v.y, z = v.z }).ToArray(),
                    triangles = mesh.triangles
                };
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/digiphant-standing-" + phase + ".json", JsonUtility.ToJson(data));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        [Serializable] class Snapshot { public V3[] vertices; public int[] triangles; }
        [Serializable] class V3 { public float x, y, z; }
    }
}

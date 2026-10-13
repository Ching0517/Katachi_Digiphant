using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DigiPhant.Editor
{
    public static class DigiPhantSetup
    {
        public const string ScenePath = "Assets/DigiPhant/Scenes/DigiPhant.unity";

        [MenuItem("DigiPhant/Create New Template Scene")]
        public static void CreateTemplate()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
                throw new InvalidOperationException("Template already exists. Open it, or duplicate it to experiment.");
            Directory.CreateDirectory("Assets/DigiPhant/Scenes");
            Directory.CreateDirectory("Assets/DigiPhant/Materials");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Elephant/Prefabs/Elephant.prefab");
            if (!prefab) throw new InvalidOperationException("Elephant prefab is missing.");
            var elephant = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            elephant.name = "Elephant — collective puppet";
            elephant.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // Use the imported rest pose. The vendor demo and Animator would overwrite our controls.
            foreach (var animator in elephant.GetComponentsInChildren<Animator>()) animator.enabled = false;
            foreach (var demo in elephant.GetComponentsInChildren<Elephant>()) demo.enabled = false;
            foreach (var collider in elephant.GetComponentsInChildren<CharacterController>()) collider.enabled = false;
            var bounds = SurfaceBounds(elephant);
            elephant.transform.position -= Vector3.up * bounds.min.y;
            bounds = SurfaceBounds(elephant);
            var controller = new GameObject("DigiPhant Controls").AddComponent<DigiPhantController>();
            var bones = elephant.GetComponentsInChildren<Transform>().ToDictionary(t => t.name, t => t);
            var controls = new List<BoneControl>();
            void Add(string label, int person, Movement input, string[] names, Vector3 axis, float degrees)
            {
                var targets = names.Select(n => bones["elephant_" + n + "_bone"]).ToArray();
                controls.Add(new BoneControl { label = label, performer = person, movement = input,
                    bones = targets, localAxis = targets[0].InverseTransformDirection(axis).normalized, degrees = degrees });
            }
            Add("Front left leg", 2, Movement.LeftHandHeight, new[] { "l_Humerus" }, Vector3.right, -25);
            Add("Front right leg", 2, Movement.RightHandHeight, new[] { "r_Humerus" }, Vector3.right, -25);
            Add("Rear left leg", 2, Movement.LeftFootLift, new[] { "l_Femur" }, Vector3.right, -25); controls[controls.Count - 1].disabled = true;
            Add("Rear right leg", 2, Movement.RightFootLift, new[] { "r_Femur" }, Vector3.right, -25); controls[controls.Count - 1].disabled = true;
            Add("Head turn", 3, Movement.Lean, new[] { "Head" }, Vector3.up, 25);
            Add("Trunk curl", 3, Movement.RightHandHeight, Enumerable.Range(1, 7).Select(i => "Trunk" + i).ToArray(), Vector3.right, 12);
            Add("Left ear", 3, Movement.ArmSpread, new[] { "l_Ear1" }, Vector3.up, 25);
            Add("Right ear", 3, Movement.ArmSpread, new[] { "r_Ear1" }, Vector3.up, -25);
            Add("Tail sway", 2, Movement.Lean, new[] { "Tail1" }, Vector3.up, 30);
            controller.controls = controls.ToArray();
            controller.SetPerformerCount(controller.performerCount);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Stage";
            floor.transform.localScale = Vector3.one * Mathf.Max(2, bounds.size.magnitude / 5);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(.14f, .21f, .24f));
            AssetDatabase.CreateAsset(material, "Assets/DigiPhant/Materials/Stage.mat");
            floor.GetComponent<Renderer>().sharedMaterial = material;
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.055f, .085f, .11f);
            camera.fieldOfView = 38;
            float size = bounds.size.magnitude;
            var aim = bounds.center + Vector3.up * bounds.size.y * .05f;
            camera.transform.position = aim + new Vector3(1.1f, .5f, 1.25f).normalized * size * 1.5f;
            camera.transform.LookAt(aim);
            // Reserve the left side for the controls panel.
            camera.rect = new Rect(.27f, 0, .73f, 1);
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 500;
            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .6f, .65f);
            DigiPhantLocomotionSetup.AddToOpenScene();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("DIGIPHANT_SETUP_OK: " + ScenePath);
        }

        static Bounds SurfaceBounds(GameObject elephant)
        {
            var renderer = elephant.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            renderer.BakeMesh(mesh);
            var vertices = mesh.vertices;
            var bounds = new Bounds(renderer.transform.TransformPoint(vertices[0]), Vector3.zero);
            foreach (var point in vertices) bounds.Encapsulate(renderer.transform.TransformPoint(point));
            UnityEngine.Object.DestroyImmediate(mesh);
            return bounds;
        }

        [MenuItem("DigiPhant/Use One Performer")]
        public static void One() => SetPeople(1);
        [MenuItem("DigiPhant/Use Two Performers")]
        public static void Two() => SetPeople(2);
        [MenuItem("DigiPhant/Use Three Performers")]
        public static void Three() => SetPeople(3);
        [MenuItem("DigiPhant/Use Four Performers")]
        public static void Four() => SetPeople(4);
        static void SetPeople(int count)
        {
            var c = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            if (!c) { Debug.LogWarning("Open the DigiPhant scene first."); return; }
            Undo.RecordObject(c, "Change performer preset");
            c.SetPerformerCount(count);
            EditorUtility.SetDirty(c);
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        // Batch validation is also available from the menu. It restores the saved scene afterwards.
        public static void BuildAndValidate()
        {
            if (!File.Exists(ScenePath)) CreateTemplate();
            Validate();
        }
        [MenuItem("DigiPhant/Validate Saved Template")]
        public static void Validate()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            var c = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
            Check(c != null, "Missing DigiPhant controller");
            // The template is intentionally serialized as a solo starter. These
            // role fixtures exercise the configured three-performer mapping.
            c.SetPerformerCount(3);
            Check(c != null && c.controls.Length == 9, "Expected nine default controls");
            Check(c.controls.All(x => x.bones.Length > 0 && x.bones.All(b => b != null)), "Missing rig reference");
            Check(UnityEngine.Object.FindObjectsByType<Animator>().All(a => !a.enabled), "Animator conflicts with puppet");
            var movement = c.GetComponent<DigiPhantLocomotion>();
            if (movement) movement.enableLocomotion = false;
            c.CaptureRestPose();
            var bone = c.controls[0].bones[0];
            Quaternion initial = bone.localRotation;
            c.controls[0].testValue = 1;
            c.ApplyControls(0, 1);
            Check(Quaternion.Angle(initial, bone.localRotation) > 10, "Slider failed to drive bone");
            c.ResetControls();
            Check(Quaternion.Angle(initial, bone.localRotation) < .01f, "Reset failed");
            c.inputMode = InputMode.Camera;
            string packet = "{\"version\":1,\"people\":[{\"slot\":1,\"values\":[0,0,0,0,0,0],\"confidence\":[1,1,1,1,1,1]},{\"slot\":2,\"values\":[1,0,0,0,0,0],\"confidence\":[1,1,1,1,1,1]}]}";
            var baseline = new PoseFrame { version = 1, people = Enumerable.Range(1, 3).Select(i => new PerformerFrame {
                slot = i, values = new float[6], confidence = Enumerable.Repeat(1f, 6).ToArray() }).ToArray() };
            Check(c.AcceptPacket(JsonUtility.ToJson(baseline), 10), "Baseline rejected");
            c.InvalidateCalibration();
            var lowConfidence = JsonUtility.FromJson<PoseFrame>(JsonUtility.ToJson(baseline));
            lowConfidence.people[1].confidence[(int)Movement.LeftHandHeight] = .2f;
            Check(c.AcceptPacket(JsonUtility.ToJson(lowConfidence), 10), "Low-confidence packet rejected");
            Check(!c.CalibrateAt(10), "Low-confidence movement was calibrated");
            Check(c.Status.Contains("Front left leg") && c.Status.Contains("Performer 2") &&
                c.Status.Contains("confidence is too low") && c.Status.Contains("20%") &&
                c.Status.Contains("50%") && !c.IsCalibrated,
                "Low-confidence calibration did not explain the failing movement and threshold");
            Check(c.AcceptPacket(JsonUtility.ToJson(baseline), 10.1f), "Restored baseline rejected");
            Check(c.CalibrateAt(10.1f), "Calibration failed");
            Check(c.AcceptPacket(packet, 10), "Valid packet rejected");
            Check(!c.AcceptPacket("{\"version\":1,\"people\":[{\"slot\":5}]}", 10), "Invalid packet accepted");
            c.ApplyControls(10, 1);
            Check(Quaternion.Angle(initial, bone.localRotation) > 10, "Camera input failed to drive bone");
            c.ApplyControls(12, 2);
            Check(Quaternion.Angle(initial, bone.localRotation) < .01f, "Stale tracking did not return to neutral");
            Check(!c.IsTracked(1, 12), "Stale performer reported visible");
            using (var bridge = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            {
                // Use a temporary port pair so validation cannot reconfigure a live classroom bridge.
                c.port = ((System.Net.IPEndPoint)bridge.Client.LocalEndPoint).Port - 1;
                bridge.Client.ReceiveTimeout = 1000;
                c.SetInputMode(InputMode.Camera);
                using (var sender = new System.Net.Sockets.UdpClient())
                {
                    byte[] data = System.Text.Encoding.UTF8.GetBytes(packet);
                    sender.Send(data, data.Length, "127.0.0.1", c.port);
                }
                c.PollCamera(20);
                Check(c.IsTracked(1, 20), "UDP receiver failed");
                var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0);
                bridge.Receive(ref endpoint); // Initial count announcement.
                foreach (int count in new[] { 1, 2, 3, 4 })
                {
                    c.SetPerformerCount(count);
                    c.PollCamera(20 + count);
                    var request = JsonUtility.FromJson<PoseFrame>(System.Text.Encoding.UTF8.GetString(bridge.Receive(ref endpoint)));
                    Check(request.version == 1 && request.performerCount == count && endpoint.Port == c.port,
                        "Camera count synchronization failed for " + count);
                    var stale = new PoseFrame { version = 1, performerCount = count == 4 ? 1 : 4, people = new PerformerFrame[0] };
                    Check(!c.AcceptPacket(JsonUtility.ToJson(stale), 30), "Wrong group-size packet accepted");
                }
                c.SetInputMode(InputMode.TestSliders);
            }
            foreach (int count in new[] { 1, 2, 3, 4, 1, 4 })
            {
                c.SetPerformerCount(count);
                Check(c.controls.All(x => x.performer >= 1 && x.performer <= count), "Unreachable control in preset " + count);
                Check(!c.IsCalibrated, "Count change retained old calibration");
            }
            One();
            Check(c.controls.All(x => x.performer == 1), "Solo preset failed");
            Two();
            Check(c.controls.Single(x => x.label == "Trunk curl").performer == 2, "Two-person trunk assignment failed");
            Four();
            Check(c.performerCount == 4 && c.controls.Single(x => x.label == "Trunk curl").performer == 4, "Four-person preset failed");
            Three();
            Check(c.controls.Single(x => x.label == "Head turn").performer == 1 &&
                c.controls.Single(x => x.label == "Front left leg").performer == 2 &&
                c.controls.Single(x => x.label == "Front left leg").movement == Movement.LeftHandHeight &&
                c.controls.Single(x => x.label == "Rear left leg").disabled &&
                c.controls.Single(x => x.label == "Tail sway").performer == 2 &&
                c.controls.Single(x => x.label == "Trunk curl").performer == 3 &&
                c.controls.Single(x => x.label == "Left ear").performer == 3 &&
                c.controls.Single(x => x.label == "Right ear").performer == 3,
                "Three-person role assignments failed");
            c.SetUpperBodyOnly(true);
            Check(!c.IsCalibrated, "Movement mode retained calibration");
            Check(!c.AcceptPacket(JsonUtility.ToJson(baseline), 40), "Full-body packet accepted in seated mode");
            baseline.upperBodyOnly = true;
            Check(c.AcceptPacket(JsonUtility.ToJson(baseline), 40), "Seated packet rejected");
            Check(c.CalibrateAt(40), "Seated calibration failed");
            baseline.people[1].values[(int)Movement.LeftHandHeight] = 1;
            Check(c.AcceptPacket(JsonUtility.ToJson(baseline), 40), "Seated hand-to-leg packet rejected");
            c.ApplyControls(40, 1);
            Check(Quaternion.Angle(initial, bone.localRotation) > 10, "Seated hand input failed to drive leg");
            c.BeginCalibrationCountdown(50);
            Check(c.CalibrationPending && !c.IsCalibrated, "Countdown did not clear calibration");
            c.UpdateCalibrationCountdown(59);
            Check(c.CalibrationPending && !c.IsCalibrated, "Countdown calibrated early");
            Check(c.AcceptPacket(JsonUtility.ToJson(baseline), 60), "Countdown baseline rejected");
            c.UpdateCalibrationCountdown(60);
            Check(!c.CalibrationPending && c.IsCalibrated, "Countdown did not calibrate at ten seconds");
            c.BeginCalibrationCountdown(70);
            c.UpdateCalibrationCountdown(80);
            Check(!c.CalibrationPending && !c.IsCalibrated, "Countdown accepted stale tracking");
            c.BeginCalibrationCountdown(90);
            c.CancelCalibrationCountdown();
            Check(!c.CalibrationPending, "Countdown cancellation failed");
            c.BeginCalibrationCountdown(100);
            c.SetUpperBodyOnly(false);
            Check(!c.CalibrationPending, "Mode change did not cancel countdown");
            Check(!c.IsCalibrated, "Returning to full body retained calibration");
            Check(!c.AcceptPacket(JsonUtility.ToJson(baseline), 41), "Seated packet accepted in full-body mode");
            c.ResetControls();
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("DIGIPHANT_VALIDATION_OK: rig, slider, reset, packets, timeout, UDP, presets");
        }
    }
    [CustomEditor(typeof(DigiPhantController))]
    public class DigiPhantControllerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var controller = (DigiPhantController)target;
            EditorGUILayout.LabelField("Group size", EditorStyles.boldLabel);
            int count = GUILayout.Toolbar(controller.performerCount - 1, new[] { "1", "2", "3", "4" }) + 1;
            if (count != controller.performerCount)
            {
                Undo.RecordObject(controller, "Change group size");
                controller.SetPerformerCount(count);
                EditorUtility.SetDirty(controller);
            }
            EditorGUILayout.HelpBox("Changing group size applies the default role assignments and clears calibration.", MessageType.Info);
            DrawDefaultInspector();
        }
    }

}

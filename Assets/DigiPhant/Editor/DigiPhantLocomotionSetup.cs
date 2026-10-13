using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiPhant.Editor
{
    public static class DigiPhantLocomotionSetup
    {
        [MenuItem("DigiPhant/Configure Three Performer Body Controls")]
        public static void ConfigureThreePerformerBodyControls()
        {
            var controls = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            if (!controls) throw new InvalidOperationException("Open a DigiPhant scene first.");
            Undo.RecordObject(controls, "Configure three performer body controls");
            var locomotion = controls.GetComponent<DigiPhantLocomotion>();
            if (locomotion != null) Undo.RecordObject(locomotion, "Configure three performer body controls");
            controls.SetPerformerCount(3);
            if (locomotion != null && locomotion.steering.sources.Length == 1 && locomotion.steering.sources[0].movement == Movement.Lean)
                locomotion.steering.sources[0].performer = 2;
            EditorUtility.SetDirty(controls);
            if (locomotion != null) EditorUtility.SetDirty(locomotion);
            EditorSceneManager.MarkSceneDirty(controls.gameObject.scene);
            Debug.Log("DIGIPHANT_THREE_PERFORMER_BODY_CONTROLS_CONFIGURED");
        }
        [MenuItem("DigiPhant/Add Locomotion To Open Scene")]
        public static void AddToOpenScene()
        {
            if (Application.isPlaying) { Debug.LogWarning("Stop Play before adding locomotion."); return; }
            var controls = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            if (!controls) throw new InvalidOperationException("Open a DigiPhant scene first.");
            if (controls.GetComponent<DigiPhantLocomotion>()) return;
            var animator = controls.controls.SelectMany(c => c.bones).Where(b => b).Select(b => b.GetComponentInParent<Animator>()).First(a => a);
            Undo.RegisterFullObjectHierarchyUndo(controls.gameObject, "Add locomotion");
            var locomotion = Undo.AddComponent<DigiPhantLocomotion>(controls.gameObject);
            var root = new GameObject("Elephant Travel");
            Undo.RegisterCreatedObjectUndo(root, "Add elephant travel root");
            root.transform.SetPositionAndRotation(animator.transform.position, animator.transform.rotation);
            Undo.SetTransformParent(animator.transform, root.transform, "Parent elephant for locomotion");
            locomotion.travelRoot = root.transform;
            locomotion.elephantAnimator = animator;
            locomotion.animationMotionRoot = animator.GetComponentsInChildren<Transform>().Single(t => t.name == "elephant_Main_ctrl");
            locomotion.followCamera = Camera.main;
            AnimationClip Clip(string name, bool inPlace = true)
            {
                string path = "Assets/Elephant/Animations/" + (inPlace ? "In-place/" : "") + "elephant@" + name + ".fbx";
                return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
            }
            locomotion.idle = Clip("idle", false);
            locomotion.walk = Clip("walk");
            locomotion.run = Clip("run");
            locomotion.backward = Clip("backward");
            locomotion.turnLeft = Clip("turnleft", false);
            locomotion.turnRight = Clip("turnright", false);
            locomotion.walkLeft = Clip("walkleft");
            locomotion.walkRight = Clip("walkright");
            locomotion.runLeft = Clip("runleft");
            locomotion.runRight = Clip("runright");
            locomotion.drink = Clip("drink", false);
            foreach (var c in controls.controls)
                c.locomotionWeight = c.label == "Front left leg" || c.label == "Front right leg" ? 1 : c.label.Contains("leg") ? .35f : 1;
            var stage = GameObject.Find("Stage");
            if (stage)
            {
                Undo.RecordObject(stage.transform, "Expand locomotion stage");
                stage.transform.localScale = new Vector3(6, 1, 6);
            }
            Directory.CreateDirectory("Assets/DigiPhant/Materials");
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/DigiPhant/Materials/Stage Lines.mat");
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", new Color(.24f, .35f, .38f));
                AssetDatabase.CreateAsset(mat, "Assets/DigiPhant/Materials/Stage Lines.mat");
            }
            var grid = new GameObject("Stage distance grid (4 units)");
            Undo.RegisterCreatedObjectUndo(grid, "Add stage grid");
            for (int i = -20; i <= 20; i += 4)
                for (int axis = 0; axis < 2; axis++)
                {
                    var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    line.name = (axis == 0 ? "X " : "Z ") + i;
                    line.transform.SetParent(grid.transform, false);
                    line.transform.position = axis == 0 ? new Vector3(i, .005f, 0) : new Vector3(0, .005f, i);
                    line.transform.localScale = axis == 0 ? new Vector3(.035f, .01f, 40) : new Vector3(40, .01f, .035f);
                    line.GetComponent<Renderer>().sharedMaterial = mat;
                    UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
                }
            EditorUtility.SetDirty(controls);
            EditorUtility.SetDirty(locomotion);
            EditorSceneManager.MarkSceneDirty(controls.gameObject.scene);
            Selection.activeGameObject = controls.gameObject;
            Debug.Log("DIGIPHANT_LOCOMOTION_ADDED");
        }
        public static void UpgradeSavedScene()
        {
            EditorSceneManager.OpenScene(DigiPhantSetup.ScenePath);
            AddToOpenScene();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
    }
}

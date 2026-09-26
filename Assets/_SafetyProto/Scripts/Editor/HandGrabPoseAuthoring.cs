using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.HandGrab.Editor;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using SafetyProto.Runtime.PPE;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SafetyProto.Editor
{
    /// <summary>
    /// Right-hand grab pose workflow: record in Play Mode with the ISDK recorder, load the
    /// recorded poses into the item's prefab, then generate left-hand mirrors by reflecting
    /// across one of the item's local symmetry planes.
    /// </summary>
    public static class HandGrabPoseAuthoring
    {
        private const string MenuRoot = "SafetyProto/Hand Grab/";
        private const string CollectionFolder = "Assets/HandGrabInteractableDataCollection";
        private const string MirrorSuffix = "_mirror";
        private static readonly Regex IndexedName = new Regex(@"^HandGrabInteractable_(\d+)$");
        private const string GhostProviderPath = "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/OpenXRGhostProvider.asset";

        [MenuItem(MenuRoot + "Start Pose Recording (Play Mode)")]
        private static void StartRecording()
        {
            Rigidbody item = SelectedItem();
            Hand hand = FindRightInteractorHand();
            if (hand == null)
            {
                Debug.LogError("[HandGrabPoseAuthoring] No right-hand HandGrabInteractor with a Hand component was found.");
                return;
            }

            // Frozen so the posing hand cannot shove it; hand physics and the surface limiter are
            // disabled so the visible hand is exactly the tracked hand the recorder samples.
            item.isKinematic = true;
            item.useGravity = false;
            foreach (HandPhysicsCapsules capsules in Object.FindObjectsByType<HandPhysicsCapsules>())
                capsules.enabled = false;
            foreach (HandSurfaceLimiter limiter in Object.FindObjectsByType<HandSurfaceLimiter>())
                limiter.enabled = false;

            // The recorder stores every active HandGrabInteractable under the item, so existing
            // ones are hidden to keep the collection limited to this session's recordings.
            foreach (HandGrabInteractable existing in item.GetComponentsInChildren<HandGrabInteractable>())
            {
                if (existing.GetComponents<Component>().Length > 2)
                    Object.Destroy(existing);
                else
                    existing.gameObject.SetActive(false);
            }

            var wizard = EditorWindow.GetWindow<HandGrabPoseWizard>();
            wizard.titleContent = new GUIContent("Hand Grab Pose Recorder");
            wizard.Hand = hand;
            var serialized = new SerializedObject(wizard);
            serialized.FindProperty("_item").objectReferenceValue = item;
            serialized.FindProperty("_posesCollection").objectReferenceValue = GetOrCreateCollection(item.name);
            SerializedProperty ghost = serialized.FindProperty("_handGhostProvider") ?? serialized.FindProperty("_ghostProvider");
            if (ghost != null && ghost.objectReferenceValue == null)
                ghost.objectReferenceValue = AssetDatabase.LoadAssetAtPath<HandGhostProvider>(GhostProviderPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            wizard.Show();
            wizard.Focus();

            Debug.Log($"[HandGrabPoseAuthoring] Recording set up for '{item.name}' with hand '{hand.name}'. " +
                      "Record with Space (recorder window focused), then Save To Collection before leaving Play Mode.");
        }

        [MenuItem(MenuRoot + "Start Pose Recording (Play Mode)", true)]
        private static bool ValidateStartRecording() => Application.isPlaying && SelectedItem() != null;

        [MenuItem(MenuRoot + "Load Recorded Poses Into Prefab")]
        private static void LoadRecordedPoses()
        {
            Rigidbody selected = SelectedItem();
            string collectionPath = CollectionPath(selected.name);
            var collection = AssetDatabase.LoadAssetAtPath<HandGrabInteractableDataCollection>(collectionPath);
            if (collection == null)
            {
                Debug.LogError($"[HandGrabPoseAuthoring] No recorded collection at {collectionPath}.");
                return;
            }

            List<HandGrabUtils.HandGrabInteractableData> recorded = collection.InteractablesData
                .Where(data => data.poses != null && data.poses.Count > 0)
                .ToList();
            if (recorded.Count == 0)
            {
                Debug.LogError($"[HandGrabPoseAuthoring] {collectionPath} holds no poses.");
                return;
            }

            EditItem(selected, item =>
            {
                int removed = RemovePoselessInteractables(item);
                int renamed = RenameDuplicateRightHandInteractables(item);
                AddInteractables(item, recorded);
                return $"loaded {recorded.Count} recorded interactable(s), removed {removed} pose-less placeholder(s), " +
                       $"renamed {renamed} duplicate(s)";
            });
        }

        [MenuItem(MenuRoot + "Load Recorded Poses Into Prefab", true)]
        private static bool ValidateLoadRecordedPoses() => !Application.isPlaying && SelectedItem() != null;

        /// <summary>
        /// Moves poses that exist only as added overrides on the selected prefab instance (for
        /// example after the ISDK recorder's own Load button) into the prefab asset, and gives
        /// same-named right-hand interactables unique names so each can be mirrored.
        /// </summary>
        [MenuItem(MenuRoot + "Apply Scene Poses To Prefab")]
        private static void ApplyScenePoses()
        {
            Rigidbody selected = SelectedItem();
            GameObject instance = selected.gameObject;
            if (!IsSceneInstanceRoot(instance))
            {
                EditItem(selected, item => $"renamed {RenameDuplicateRightHandInteractables(item)} duplicate(s)");
                return;
            }

            List<HandGrabInteractable> stranded = PrefabUtility.GetAddedGameObjects(instance)
                .SelectMany(added => added.instanceGameObject.GetComponentsInChildren<HandGrabInteractable>(true))
                .Where(interactable => interactable.HandGrabPoses.Count > 0)
                .OrderBy(interactable => interactable.transform.GetSiblingIndex())
                .ToList();
            List<HandGrabUtils.HandGrabInteractableData> moved = stranded.Select(HandGrabUtils.SaveData).ToList();

            foreach (AddedGameObject added in PrefabUtility.GetAddedGameObjects(instance))
            {
                if (added.instanceGameObject.GetComponentInChildren<HandGrabInteractable>(true) != null)
                    PrefabUtility.RevertAddedGameObject(added.instanceGameObject, InteractionMode.UserAction);
            }

            EditItem(selected, item =>
            {
                int removed = moved.Count > 0 ? RemovePoselessInteractables(item) : 0;
                int renamed = RenameDuplicateRightHandInteractables(item);
                AddInteractables(item, moved);
                return $"moved {moved.Count} scene interactable(s) into the prefab, removed {removed} pose-less placeholder(s), " +
                       $"renamed {renamed} duplicate(s)";
            });
        }

        [MenuItem(MenuRoot + "Apply Scene Poses To Prefab", true)]
        private static bool ValidateApplyScenePoses() => !Application.isPlaying && SelectedItem() != null;

        [MenuItem(MenuRoot + "Mirror Right Poses To Left/Across Local YZ (flip X)")]
        private static void MirrorAcrossYZ() => MirrorRightPoses(Vector3.right, "YZ");

        [MenuItem(MenuRoot + "Mirror Right Poses To Left/Across Local XZ (flip Y)")]
        private static void MirrorAcrossXZ() => MirrorRightPoses(Vector3.up, "XZ");

        [MenuItem(MenuRoot + "Mirror Right Poses To Left/Across Local XY (flip Z)")]
        private static void MirrorAcrossXY() => MirrorRightPoses(Vector3.forward, "XY");

        [MenuItem(MenuRoot + "Mirror Right Poses To Left/Across Local YZ (flip X)", true)]
        [MenuItem(MenuRoot + "Mirror Right Poses To Left/Across Local XZ (flip Y)", true)]
        [MenuItem(MenuRoot + "Mirror Right Poses To Left/Across Local XY (flip Z)", true)]
        private static bool ValidateMirrorRightPoses() => !Application.isPlaying && SelectedItem() != null;

        /// <summary>
        /// Rebuilds the left-hand mirror of every right-hand interactable. The plane passes through
        /// the item's root and is perpendicular to <paramref name="localNormal"/>, in root space:
        /// pick the plane the item is symmetric about.
        /// </summary>
        private static void MirrorRightPoses(Vector3 localNormal, string planeName)
        {
            EditItem(SelectedItem(), item =>
            {
                List<HandGrabInteractable> sources = item.GetComponentsInChildren<HandGrabInteractable>(true)
                    .Where(IsRightHandInteractable)
                    .ToList();
                if (sources.Count == 0)
                    return "aborted: no right-hand interactable with poses was found";
                string duplicate = sources.GroupBy(source => source.name).FirstOrDefault(group => group.Count() > 1)?.Key;
                if (duplicate != null)
                    return $"aborted: several right-hand interactables are named '{duplicate}'; run Apply Scene Poses To Prefab to give them unique names";

                int skipped = 0;
                foreach (HandGrabInteractable source in sources)
                {
                    if (source.HandGrabPoses.Any(pose => pose.SnapSurface != null))
                    {
                        Debug.LogWarning($"[HandGrabPoseAuthoring] Skipped '{source.name}': snap surfaces are not mirrored by this tool.", source);
                        skipped++;
                        continue;
                    }

                    Transform existing = source.transform.parent.Find(source.name + MirrorSuffix);
                    if (existing != null)
                        Object.DestroyImmediate(existing.gameObject);
                    CreateMirror(source, item.transform, localNormal);
                }

                var sourceNames = new HashSet<string>(sources.Select(source => source.name));
                string[] orphans = item.GetComponentsInChildren<HandGrabInteractable>(true)
                    .Where(interactable => interactable.name.EndsWith(MirrorSuffix)
                                           && !sourceNames.Contains(interactable.name.Substring(0, interactable.name.Length - MirrorSuffix.Length)))
                    .Select(interactable => interactable.name)
                    .ToArray();
                if (orphans.Length > 0)
                    Debug.LogWarning($"[HandGrabPoseAuthoring] {item.name}: {orphans.Length} mirror(s) have no matching right-hand source " +
                                     $"and were left untouched: {string.Join(", ", orphans)}. Delete them if they are stale.");
                return $"mirrored {sources.Count - skipped} right-hand interactable(s) across the local {planeName} plane";
            });
        }

        /// <summary>
        /// Reflects a right-hand grip pose across the plane through <paramref name="root"/> whose
        /// normal is <paramref name="localNormal"/> in root space, producing the matching
        /// left-hand grip pose.
        /// </summary>
        public static Pose MirrorGripPose(Pose rightGrip, Transform root, Vector3 localNormal)
        {
            Vector3 normal = root.TransformDirection(localNormal).normalized;
            Vector3 offset = rightGrip.position - root.position;
            return new Pose(
                root.position + Vector3.Reflect(offset, normal),
                HandMirroring.Reflect(rightGrip.rotation, normal));
        }

        /// <summary>Returns a left-hand copy of a right-hand finger pose.</summary>
        public static HandPose MirrorHandPose(HandPose rightHand)
        {
            var mirrored = new HandPose(rightHand) { Handedness = Handedness.Left };
            for (int i = 0; i < mirrored.JointRotations.Length; i++)
                mirrored.JointRotations[i] = HandMirroring.Mirror(mirrored.JointRotations[i]);
            return mirrored;
        }

        private static void CreateMirror(HandGrabInteractable source, Transform root, Vector3 localNormal)
        {
            HandGrabInteractable mirror = HandGrabUtils.CreateHandGrabInteractable(source.transform.parent, source.name + MirrorSuffix);
            mirror.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            HandGrabUtils.HandGrabInteractableData settings = HandGrabUtils.SaveData(source);
            settings.poses = null;
            HandGrabUtils.LoadData(mirror, settings);

            foreach (HandGrabPose rightPose in source.HandGrabPoses)
            {
                HandGrabPose leftPose = HandGrabUtils.CreateHandGrabPose(mirror.transform, mirror.RelativeTo);
                Pose grip = MirrorGripPose(rightPose.transform.GetPose(), root, localNormal);
                leftPose.transform.SetPositionAndRotation(grip.position, grip.rotation);
                leftPose.transform.localScale = rightPose.transform.localScale;
                leftPose.InjectOptionalHandPose(MirrorHandPose(rightPose.HandPose));
                mirror.HandGrabPoses.Add(leftPose);
            }
        }

        private static bool IsRightHandInteractable(HandGrabInteractable interactable) =>
            !interactable.name.EndsWith(MirrorSuffix)
            && interactable.HandGrabPoses.Count > 0
            && interactable.HandGrabPoses.All(pose => pose.HandPose != null && pose.HandPose.Handedness == Handedness.Right);

        private static void AddInteractables(Rigidbody item, IEnumerable<HandGrabUtils.HandGrabInteractableData> interactables)
        {
            int next = NextInteractableIndex(item);
            foreach (HandGrabUtils.HandGrabInteractableData data in interactables)
            {
                HandGrabInteractable interactable = HandGrabUtils.CreateHandGrabInteractable(
                    item.transform, InteractableName(next++));
                HandGrabUtils.LoadData(interactable, data);
            }
        }

        private static int RenameDuplicateRightHandInteractables(Rigidbody item)
        {
            List<HandGrabInteractable> duplicates = item.GetComponentsInChildren<HandGrabInteractable>(true)
                .Where(IsRightHandInteractable)
                .GroupBy(interactable => interactable.name)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .ToList();
            int next = NextInteractableIndex(item);
            foreach (HandGrabInteractable interactable in duplicates)
                interactable.name = InteractableName(next++);
            return duplicates.Count;
        }

        private static int NextInteractableIndex(Rigidbody item)
        {
            int highest = 0;
            foreach (HandGrabInteractable interactable in item.GetComponentsInChildren<HandGrabInteractable>(true))
            {
                Match match = IndexedName.Match(interactable.name);
                if (match.Success)
                    highest = Mathf.Max(highest, int.Parse(match.Groups[1].Value));
            }
            return highest + 1;
        }

        private static string InteractableName(int index) => $"HandGrabInteractable_{index:00}";

        private static int RemovePoselessInteractables(Rigidbody item)
        {
            int removed = 0;
            foreach (HandGrabInteractable interactable in item.GetComponentsInChildren<HandGrabInteractable>(true))
            {
                if (interactable.HandGrabPoses.Count > 0)
                    continue;
                // The placeholder shares its GameObject with the controller GrabInteractable.
                if (interactable.GetComponents<Component>().Length > 2)
                    Object.DestroyImmediate(interactable);
                else
                    Object.DestroyImmediate(interactable.gameObject);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Applies an edit to the item's prefab asset when the selection is a prefab instance root,
        /// otherwise to the object in place (Prefab Mode or a plain scene object).
        /// </summary>
        private static void EditItem(Rigidbody selected, System.Func<Rigidbody, string> edit)
        {
            GameObject selectedGo = selected.gameObject;
            if (IsSceneInstanceRoot(selectedGo))
            {
                // Poses loaded with the ISDK recorder's own Load button land on the scene instance
                // as added overrides; editing the asset would silently ignore them.
                int stranded = PrefabUtility.GetAddedGameObjects(selectedGo)
                    .Count(added => added.instanceGameObject.GetComponentInChildren<HandGrabInteractable>(true) != null);
                if (stranded > 0)
                {
                    Debug.LogError($"[HandGrabPoseAuthoring] '{selectedGo.name}' has {stranded} HandGrabInteractable(s) that exist only " +
                                   "as scene overrides. Run Apply Scene Poses To Prefab first.", selectedGo);
                    return;
                }

                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selectedGo);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string result = edit(contents.GetComponent<Rigidbody>());
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    Debug.Log($"[HandGrabPoseAuthoring] {path}: {result}.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(selectedGo, "Edit Hand Grab Poses");
            string inPlace = edit(selected);
            EditorUtility.SetDirty(selectedGo);
            EditorSceneManager.MarkSceneDirty(selectedGo.scene);
            Debug.Log($"[HandGrabPoseAuthoring] {selectedGo.name}: {inPlace}.");
        }

        // A variant's root is itself an instance of its base, so objects in Prefab Mode or in
        // loaded prefab contents must be edited in place rather than redirected to a source asset.
        private static bool IsSceneInstanceRoot(GameObject go) =>
            PrefabUtility.IsAnyPrefabInstanceRoot(go)
            && PrefabStageUtility.GetPrefabStage(go) == null
            && !EditorSceneManager.IsPreviewScene(go.scene);

        private static Rigidbody SelectedItem()
        {
            GameObject selected = Selection.activeGameObject;
            return selected != null ? selected.GetComponentInParent<Rigidbody>() : null;
        }

        private static Hand FindRightInteractorHand()
        {
            foreach (HandGrabInteractor interactor in Object.FindObjectsByType<HandGrabInteractor>())
            {
                IHand hand = interactor.Hand;
                // HandRef forwards to the real Hand component the recorder needs.
                while (hand is HandRef handRef)
                    hand = handRef.Hand;
                if (hand is Hand concrete && concrete.Handedness == Handedness.Right)
                    return concrete;
            }
            return null;
        }

        private static string CollectionPath(string itemName) =>
            $"{CollectionFolder}/{itemName}_HandGrabCollection.asset";

        private static HandGrabInteractableDataCollection GetOrCreateCollection(string itemName)
        {
            string path = CollectionPath(itemName);
            var collection = AssetDatabase.LoadAssetAtPath<HandGrabInteractableDataCollection>(path);
            if (collection != null)
                return collection;

            Directory.CreateDirectory(CollectionFolder);
            collection = ScriptableObject.CreateInstance<HandGrabInteractableDataCollection>();
            AssetDatabase.CreateAsset(collection, path);
            AssetDatabase.SaveAssets();
            return collection;
        }
    }
}

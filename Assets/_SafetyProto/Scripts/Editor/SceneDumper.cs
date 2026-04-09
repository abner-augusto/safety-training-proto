// Assets/Editor/SceneDumper.cs
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class SceneDumper : EditorWindow
{
    public enum OutputFormat { Markdown, JSON }
    private OutputFormat format = OutputFormat.Markdown;
    private bool includeInactive = true;
    private bool includeComponentFields = false;
    private bool includeReferences = true;
    private bool filterSdkInternals = true;
    private bool collapseAnchorOnly = true;
    private bool filterSelfRefs = true;
    private bool filterIgnoreSuffix = true;
    private string ignoreSuffix = "_ignore";

    private Dictionary<int, List<RefEdge>> refGraph;
    private Dictionary<int, string> idToName;

    // ── FILTROS DE HIERARQUIA ────────────────────────────────────────────────

    private static readonly string[] SkipNamePatterns = {
        "b_l_", "b_r_",
        "XRHand_",
        "_marker", "_axis_marker", "_finger_tip_marker",
        "_fingernail_marker", "_finger_pad_marker",
        "_knuckle_marker", "_palm_knuckle_marker", "_palm_collider_",
    };

    private static readonly string[] SkipNameExact = {
        "sphere", "Cube",
    };

    private static readonly string[] SkipComponentTypes = {
        "Transform", "MeshFilter",
    };

    private static readonly string[] CollapseSubtreeRoots = {
        "OVRLeftHandVisual", "OVRRightHandVisual",
        "OpenXRLeftHand",    "OpenXRRightHand",
        "OXRLeftHand",       "OXRRightHand",
        "OculusHand_R",      "OculusHand_L",
        "r_handMeshNode",    "l_handMeshNode",
        "OVRLeftHandDataSource",      "OVRRightHandDataSource",
        "OVRLeftControllerDataSource","OVRRightControllerDataSource",
        // Subárvores de interactors do OVR SDK
        "LeftInteractions",  "RightInteractions",
        "OVRControllerPrefab",
    };

    private static readonly (string left, string right)[] MirroredPairs = {
        ("LeftInteractions",  "RightInteractions"),
    };

    // ── FILTROS DE REFERÊNCIAS ───────────────────────────────────────────────

    // Componentes cujas referências são sempre wiring interno do ISDK
    private static readonly string[] SkipRefSourceComponents = {
        "HandGrabInteractable",
        "GrabInteractable",
        "HandGrabPose",
        "PointableUnityEventWrapper",
        "TouchHandGrabInteractable",
        // Componentes de locomotion/interação do OVR SDK — só referenciam
        // partes do rig, nunca objetos do projeto
        "RayInteractor",
        "PokeInteractor",
        "HandGrabInteractor",
        "DistanceHandGrabInteractor",
        "TeleportInteractor",
        "GrabInteractor",
        "DistanceGrabInteractor",
        // Visuais e helpers internos do OVR SDK
        "HandVisual",
        "HandGrabStateVisual",
        "ReticleGhostDrawer",
        "SyntheticHand",
        "SyntheticControllerInHand",
        "ControllerVisual",
        "OVRControllerHelper",
        "InteractorActiveState",
        "ActiveStateUnityEventWrapper",
        "SecondaryInteractorConnection",
        "LocomotionTunneling",
        "TunnelingEffect",
    };

    // Prefixos de nomes de TARGET que indicam player rig do SDK.
    // Qualquer aresta apontando para esses objetos é removida.
    private static readonly string[] SkipRefTargetPrefixes = {
        // Anchors do OVRCameraRig
        "CenterEyeAnchor/",
        "LeftEyeAnchor/",
        "RightEyeAnchor/",
        "LeftHandAnchor/",
        "RightHandAnchor/",
        "TrackingSpace/",
        // Interactors internos (referenciados por uns aos outros)
        "LeftInteractions/",
        "RightInteractions/",
        "OVRHands/",
        "OVRControllers/",
        "OVRHmd/",
        // Componentes genéricos do rig que nunca são do projeto
        "HandGrabAPI/",
        "HandWristPoint/",
        "GripPoint/",
        "PinchPoint/",
        "PinchPointRange/",
        "HandSphereMap/",
        // Partes do rig visual sintético / controller
        "SyntheticHandData/",
        "ControllerInHandVisibilityActiveState/",
        "OVRLeftHandVisual/",
        "OVRRightHandVisual/",
        "OVRLeftControllerVisual/",
        "OVRRightControllerVisual/",
        "OVRControllerPrefab/",
        "LeftHandSynthetic/",
        "RightHandSynthetic/",
        "OpenXRLeftHand/",
        "OpenXRRightHand/",
    };

    // Tipos de componente no TARGET que são sempre infra do SDK
    private static readonly string[] SkipRefTargetComponentSuffixes = {
        "/HandRef",
        "/HmdRef",
        "/ControllerRef",
        "/HandGrabAPI",
        "/HandRootOffset",
        "/HandPinchOffset",
        "/ControllerOffset",
        "/CenterEyeOffset",
        "/HandTransformScaler",
        "/ConicalFrustum",
        "/FilteredTransform",
        "/HandPointerPose",
        "/ControllerPointerPose",
        "/HandJoint",
    };

    // Componentes que, sozinhos, indicam que um nó é puramente visual (sem lógica)
    private static readonly HashSet<string> VisualOnlyComponents = new() {
        "MeshRenderer", "MeshCollider", "SkinnedMeshRenderer",
        "Animator", "SortingGroup", "ProBuilderMesh", "ProBuilderShape",
        "Light", "LightProbeGroup", "MaterialPropertyBlockEditor",
        "MeshFilter",
    };

    // ── STRUCT / ENUM ────────────────────────────────────────────────────────

    struct RefEdge
    {
        public string sourceComponent;
        public string fieldName;
        public int targetId;
        public string targetName;
        public RefTargetKind targetKind;
    }

    enum RefTargetKind { GameObject, Component, Asset }

    // ── WINDOW ───────────────────────────────────────────────────────────────

    [MenuItem("Tools/Scene Dumper")]
    public static void ShowWindow() => GetWindow<SceneDumper>("Scene Dumper");

    void OnGUI()
    {
        GUILayout.Label("Scene Dumper for LLM", EditorStyles.boldLabel);
        format = (OutputFormat)EditorGUILayout.EnumPopup("Format", format);

        GUILayout.Space(4);
        GUILayout.Label("Content", EditorStyles.miniBoldLabel);
        includeInactive        = EditorGUILayout.Toggle("Include Inactive Objects",  includeInactive);
        includeComponentFields = EditorGUILayout.Toggle("Include Serialized Fields", includeComponentFields);
        includeReferences      = EditorGUILayout.Toggle("Include Object References", includeReferences);

        GUILayout.Space(4);
        GUILayout.Label("Noise Reduction", EditorStyles.miniBoldLabel);
        filterSdkInternals  = EditorGUILayout.Toggle("Filter SDK Internals (OVR/XR bones)", filterSdkInternals);
        collapseAnchorOnly  = EditorGUILayout.Toggle("Collapse Anchor-Only Nodes",          collapseAnchorOnly);
        filterSelfRefs      = EditorGUILayout.Toggle("Filter Self-References",               filterSelfRefs);
        filterIgnoreSuffix  = EditorGUILayout.Toggle("Skip Objects with Ignore Suffix",      filterIgnoreSuffix);
        if (filterIgnoreSuffix)
            ignoreSuffix = EditorGUILayout.TextField("  Ignore Suffix", ignoreSuffix);

        GUILayout.Space(10);
        if (GUILayout.Button("Dump to File"))      Dump(toClipboard: false);
        if (GUILayout.Button("Copy to Clipboard")) Dump(toClipboard: true);
    }

    // ── ENTRY POINT ──────────────────────────────────────────────────────────

    void Dump(bool toClipboard)
    {
        refGraph = new Dictionary<int, List<RefEdge>>();
        idToName = new Dictionary<int, string>();

        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();

        foreach (var go in roots)
            IndexHierarchy(go);

        string output = format == OutputFormat.Markdown
            ? DumpMarkdown(scene.name, roots)
            : DumpJSON(scene.name, roots);

        if (toClipboard)
        {
            EditorGUIUtility.systemCopyBuffer = output;
            Debug.Log($"[SceneDumper] Copied! ({output.Length} chars)");
        }
        else
        {
            string ext  = format == OutputFormat.Markdown ? "md" : "json";
            string path = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                $"scene_dump_{scene.name}.{ext}");
            File.WriteAllText(path, output, Encoding.UTF8);
            Debug.Log($"[SceneDumper] Saved: {path}");
            EditorUtility.RevealInFinder(path);
        }
    }

    void IndexHierarchy(GameObject go)
    {
        idToName[go.GetInstanceID()] = go.name;
        foreach (var c in go.GetComponents<Component>())
            if (c != null) idToName[c.GetInstanceID()] = $"{go.name}/{c.GetType().Name}";
        foreach (Transform child in go.transform)
            IndexHierarchy(child.gameObject);
    }

    // ── FILTROS: HIERARQUIA ──────────────────────────────────────────────────

    bool ShouldSkipObject(GameObject go)
    {
        string name = go.name;
        if (filterIgnoreSuffix && !string.IsNullOrEmpty(ignoreSuffix) && name.EndsWith(ignoreSuffix)) return true;
        if (!filterSdkInternals) return false;
        if (SkipNameExact.Any(e => name == e)) return true;
        if (SkipNamePatterns.Any(p => p.StartsWith("_") ? name.EndsWith(p) : name.StartsWith(p))) return true;
        return false;
    }

    bool ShouldCollapseSubtree(GameObject go) =>
        filterSdkInternals && CollapseSubtreeRoots.Contains(go.name);

    bool IsMirrorOf(GameObject go, out string mirrorNote)
    {
        mirrorNote = null;
        if (!filterSdkInternals) return false;
        foreach (var (left, right) in MirroredPairs)
            if (go.name == right) { mirrorNote = left; return true; }
        return false;
    }

    bool IsAnchorOnly(GameObject go)
    {
        if (!collapseAnchorOnly) return false;
        return !HasLogicComponents(go);
    }

    bool HasLogicComponents(GameObject go)
    {
        var comps = go.GetComponents<Component>()
            .Where(c => c != null)
            .Select(c => c.GetType().Name)
            .Where(n => !SkipComponentTypes.Contains(n))
            .ToList();
        if (comps.Count > 0) return true;
        foreach (Transform child in go.transform)
            if (HasLogicComponents(child.gameObject)) return true;
        return false;
    }

    bool ShouldSkipComponent(string typeName) =>
        SkipComponentTypes.Contains(typeName);

    bool IsVisualOnly(GameObject go)
    {
        if (!filterSdkInternals) return false;
        var comps = go.GetComponents<Component>()
            .Where(c => c != null)
            .Select(c => c.GetType().Name)
            .Where(n => n != "Transform")
            .ToList();
        if (comps.Any(n => !VisualOnlyComponents.Contains(n))) return false;
        foreach (Transform child in go.transform)
            if (!IsVisualOnly(child.gameObject)) return false;
        return true;
    }

    // ── FILTROS: REFERÊNCIAS ─────────────────────────────────────────────────

    bool ShouldSkipRefSource(string componentType) =>
        filterSdkInternals && SkipRefSourceComponents.Contains(componentType);

    bool ShouldSkipRefTarget(string targetName)
    {
        if (!filterSdkInternals) return false;

        // Auto-referência (objeto apontando para si mesmo)
        // Nota: sourceName não está disponível aqui, aplicamos isso em RegisterEdges
        
        // Target é parte do player rig do SDK
        if (SkipRefTargetPrefixes.Any(p => targetName.StartsWith(p))) return true;

        // Target é um componente de infra do SDK (sufixo)
        if (SkipRefTargetComponentSuffixes.Any(s => targetName.EndsWith(s))) return true;

        return false;
    }

    bool IsSelfReference(string sourceName, string targetName) =>
        filterSelfRefs && targetName.StartsWith(sourceName + "/");

    // ── MARKDOWN ─────────────────────────────────────────────────────────────

    string DumpMarkdown(string sceneName, GameObject[] roots)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Scene: {sceneName}");
        sb.AppendLine();
        sb.AppendLine("## Hierarchy");
        sb.AppendLine();
        foreach (var go in roots)
            AppendMarkdownNode(sb, go, 0);
        if (includeReferences)
            AppendMarkdownRefGraph(sb);
        return sb.ToString();
    }

    void AppendMarkdownNode(StringBuilder sb, GameObject go, int depth)
    {
        if (!includeInactive && !go.activeInHierarchy) return;
        if (ShouldSkipObject(go)) return;

        string pad    = new string(' ', depth * 2);
        string active = go.activeSelf ? "" : " *(inactive)*";

        if (collapseAnchorOnly && IsAnchorOnly(go))
        {
            sb.AppendLine($"{pad}- **{go.name}**{active} *(anchor)* `[{go.tag}]`");
            return;
        }

        sb.AppendLine($"{pad}- **{go.name}**{active} `[{go.tag}]` layer:`{LayerMask.LayerToName(go.layer)}`");

        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) continue;
            string typeName = c.GetType().Name;
            if (ShouldSkipComponent(typeName)) continue;

            sb.Append($"{pad}  - `{typeName}`");

            var (fields, refs) = GetFieldsAndRefs(c);

            if (includeComponentFields && fields.Count > 0)
                sb.Append(" → " + string.Join(", ", fields.Select(f => $"{f.Key}: {f.Value}")));

            sb.AppendLine();

            if (includeReferences && refs.Count > 0)
                RegisterEdges(go.GetInstanceID(), go.name, refs);
        }

        // Espelho: emite nota e não expande
        if (IsMirrorOf(go, out string mirrorNote))
        {
            sb.AppendLine($"{pad}  *(mirror of {mirrorNote} — omitted)*");
            return;
        }

        // Subárvore SDK: expande o nó mas colapsa filhos
        if (ShouldCollapseSubtree(go))
        {
            if (go.transform.childCount > 0)
                sb.AppendLine($"{pad}  *(subtree collapsed — SDK internal)*");
            return;
        }

        foreach (Transform child in go.transform)
            AppendMarkdownNode(sb, child.gameObject, depth + 1);
    }

    void AppendMarkdownRefGraph(StringBuilder sb)
    {
        var filteredEdges = BuildFilteredEdges();
        if (filteredEdges.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("## Object References");
        sb.AppendLine();
        sb.AppendLine("| Source Object | Component | Field | → | Target |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (var (srcName, e) in filteredEdges)
        {
            string kindIcon = e.targetKind switch
            {
                RefTargetKind.Asset     => "📦",
                RefTargetKind.Component => "🔩",
                _                       => "🎮"
            };
            sb.AppendLine($"| `{srcName}` | `{e.sourceComponent}` | `{e.fieldName}` | → | {kindIcon} `{e.targetName}` |");
        }
    }

    // ── JSON ──────────────────────────────────────────────────────────────────

    string DumpJSON(string sceneName, GameObject[] roots)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"scene\": \"{sceneName}\",");
        sb.AppendLine("  \"gameObjects\": [");

        for (int i = 0; i < roots.Length; i++)
        {
            AppendJSONNode(sb, roots[i], 2);
            sb.AppendLine(i < roots.Length - 1 ? "," : "");
        }
        sb.AppendLine("  ]");

        if (includeReferences)
            AppendJSONRefGraph(sb);
        else
            sb.AppendLine("}");

        return sb.ToString();
    }

    void AppendJSONNode(StringBuilder sb, GameObject go, int depth)
    {
        if (!includeInactive && !go.activeInHierarchy) return;
        if (ShouldSkipObject(go)) return;

        string pad    = new string(' ', depth * 2);
        string active = go.activeSelf ? "true" : "false";

        if (collapseAnchorOnly && IsAnchorOnly(go))
        {
            sb.Append($"{pad}{{ \"name\": \"{J(go.name)}\", \"anchorOnly\": true }}");
            return;
        }

        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}  \"name\": \"{J(go.name)}\",");
        sb.AppendLine($"{pad}  \"id\": {go.GetInstanceID()},");
        sb.AppendLine($"{pad}  \"active\": {active},");
        sb.AppendLine($"{pad}  \"tag\": \"{go.tag}\",");
        sb.AppendLine($"{pad}  \"layer\": \"{LayerMask.LayerToName(go.layer)}\",");

        var components = go.GetComponents<Component>()
            .Where(c => c != null && !ShouldSkipComponent(c.GetType().Name))
            .ToArray();

        sb.AppendLine($"{pad}  \"components\": [");
        for (int i = 0; i < components.Length; i++)
        {
            var c = components[i];
            var (fields, refs) = GetFieldsAndRefs(c);
            if (includeReferences && refs.Count > 0)
                RegisterEdges(go.GetInstanceID(), go.name, refs);

            sb.Append($"{pad}    {{ \"type\": \"{c.GetType().Name}\"");
            if (includeComponentFields)
                foreach (var f in fields)
                    sb.Append($", \"{J(f.Key)}\": \"{J(f.Value)}\"");
            sb.Append(" }");
            sb.AppendLine(i < components.Length - 1 ? "," : "");
        }
        sb.AppendLine($"{pad}  ],");

        // Filhos
        sb.AppendLine($"{pad}  \"children\": [");

        if (IsMirrorOf(go, out string mirrorNote))
        {
            sb.AppendLine($"{pad}    {{ \"_note\": \"mirror of {mirrorNote} — omitted\" }}");
        }
        else if (ShouldCollapseSubtree(go))
        {
            if (go.transform.childCount > 0)
                sb.AppendLine($"{pad}    {{ \"_note\": \"subtree collapsed — SDK internal\" }}");
        }
        else
        {
            var children = go.transform.Cast<Transform>()
                .Where(t => !ShouldSkipObject(t.gameObject))
                .ToList();
            for (int i = 0; i < children.Count; i++)
            {
                AppendJSONNode(sb, children[i].gameObject, depth + 2);
                sb.AppendLine(i < children.Count - 1 ? "," : "");
            }
        }

        sb.AppendLine($"{pad}  ]");
        sb.Append($"{pad}}}");
    }

    void AppendJSONRefGraph(StringBuilder sb)
    {
        var filteredEdges = BuildFilteredEdges();

        sb.Length -= 2; // remove "}\n"
        sb.AppendLine(",");
        sb.AppendLine("  \"references\": [");

        for (int i = 0; i < filteredEdges.Count; i++)
        {
            var (srcName, e) = filteredEdges[i];
            sb.Append($"    {{ \"from\": \"{J(srcName)}\", \"component\": \"{e.sourceComponent}\", " +
                      $"\"field\": \"{J(e.fieldName)}\", \"to\": \"{J(e.targetName)}\", " +
                      $"\"kind\": \"{e.targetKind}\" }}");
            sb.AppendLine(i < filteredEdges.Count - 1 ? "," : "");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
    }

    // ── PIPELINE DE REFERÊNCIAS ──────────────────────────────────────────────

    List<(string srcName, RefEdge edge)> BuildFilteredEdges()
    {
        return refGraph
            .SelectMany(kv =>
            {
                string srcName = idToName.TryGetValue(kv.Key, out var n) ? n : kv.Key.ToString();
                return kv.Value.Select(e => (srcName, e));
            })
            .Where(x =>
                !IsSelfReference(x.srcName, x.e.targetName) &&
                !ShouldSkipRefTarget(x.e.targetName)
            )
            .ToList();
    }

    // ── FIELD / REF EXTRACTION ───────────────────────────────────────────────

    (Dictionary<string, string> fields, List<RefEdge> refs) GetFieldsAndRefs(Component c)
    {
        var fields = new Dictionary<string, string>();
        var refs   = new List<RefEdge>();

        // Componentes SDK: nunca emitem referências arquiteturalmente relevantes
        if (ShouldSkipRefSource(c.GetType().Name))
            return (fields, refs);

        try
        {
            var so   = new SerializedObject(c);
            var prop = so.GetIterator();
            prop.NextVisible(true);
            int limit = 0;

            while (prop.NextVisible(false) && limit++ < 30)
            {
                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var target = prop.objectReferenceValue;
                    if (target == null) continue;

                    var kind = target is GameObject ? RefTargetKind.GameObject
                             : target is Component  ? RefTargetKind.Component
                             : RefTargetKind.Asset;

                    if (kind == RefTargetKind.Asset)
                    {
                        fields[prop.name] = target.name;
                    }
                    else
                    {
                        refs.Add(new RefEdge
                        {
                            sourceComponent = c.GetType().Name,
                            fieldName       = prop.name,
                            targetId        = target.GetInstanceID(),
                            targetName      = idToName.TryGetValue(target.GetInstanceID(), out var tn)
                                              ? tn : target.name,
                            targetKind      = kind
                        });
                    }
                    continue;
                }

                if (!includeComponentFields) continue;

                string val = prop.propertyType switch
                {
                    SerializedPropertyType.Integer  => prop.intValue.ToString(),
                    SerializedPropertyType.Float    => prop.floatValue.ToString("F3"),
                    SerializedPropertyType.Boolean  => prop.boolValue.ToString(),
                    SerializedPropertyType.String   => prop.stringValue,
                    SerializedPropertyType.Enum     => prop.enumNames[prop.enumValueIndex],
                    SerializedPropertyType.Vector3  => prop.vector3Value.ToString(),
                    SerializedPropertyType.Vector2  => prop.vector2Value.ToString(),
                    SerializedPropertyType.Color    => prop.colorValue.ToString(),
                    _ => null
                };
                if (val != null) fields[prop.name] = val;
            }
        }
        catch { }

        return (fields, refs);
    }

    void RegisterEdges(int srcId, string srcName, List<RefEdge> edges)
    {
        var valid = edges
            .Where(e =>
                !IsSelfReference(srcName, e.targetName) &&
                !ShouldSkipRefTarget(e.targetName))
            .ToList();

        if (valid.Count == 0) return;

        if (!refGraph.ContainsKey(srcId))
            refGraph[srcId] = new List<RefEdge>();
        refGraph[srcId].AddRange(valid);
    }

    string J(string s) =>
        s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") ?? "";
}
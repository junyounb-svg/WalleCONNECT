using UnityEngine;
using System;
using System.Collections.Generic;


// =============================================================================
//  Brekel_Body_v3_DefaultMapper
//
//  Original-style mapper: maps one Brekel body to a set of individually
//  assigned Transform fields (hips, spine, hand_L, etc.).
//
//  Call AutoFindJoints() (or click the button in the Inspector via context
//  menu) to populate all fields by searching the character's hierarchy by
//  name.  Each field can still be overridden manually after auto-find.
//
//  Networking has been moved to Brekel_Body_v3_Receiver.  Assign that
//  component to the `receiver` field before entering Play mode.
// =============================================================================
public class Brekel_Body_v3_DefaultMapper : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------
    [Header("Receiver")]
    [Tooltip("Reference to the Brekel_Body_v3_Receiver component that owns the TCP connection")]
    public Brekel_Body_v3_Receiver receiver;

    [Header("Auto-Find")]
    [Tooltip("Root GameObject of the character skeleton to search when auto-finding joints")]
    public GameObject character;

    [Header("Mapping Settings")]
    [Tooltip("Which body ID (from the stream) to drive these transforms")]
    public int  body_ID            = 0;
    [Tooltip("Apply positions to all joints. When OFF only the root moves (reduces foot sliding)")]
    public bool applyPositionsToAll = true;
    [Tooltip("Seconds of no incoming data before the character is hidden")]
    public float NoDataHideDelay = 2f;
    [Tooltip("Smoothing speed for joint positions (Lerp). 0 = no smoothing, higher = snappier")]
    public float positionSmoothing = 0f;
    [Tooltip("Smoothing speed for joint rotations (Slerp). 0 = no smoothing, higher = snappier")]
    public float rotationSmoothing = 0f;

    [Header("Body Joints")]
    [Tooltip("Assign the matching bone Transforms from your character rig, or use Auto-Find")]
    public Transform hips, spine, chest, neck, head;
    public Transform upperLeg_L, lowerLeg_L, foot_L;
    public Transform upperLeg_R, lowerLeg_R, foot_R;
    public Transform collar_L, upperArm_L, foreArm_L, hand_L;
    public Transform collar_R, upperArm_R, foreArm_R, hand_R;

    [Header("Face")]
    [Tooltip("GameObject carrying the face SkinnedMeshRenderer with blendshapes")]
    public GameObject face_mesh;

    [Header("Visuals (optional)")]
    [Tooltip("If set, applies this Material to all Renderers found in the character hierarchy")]
    public Material jointMaterial;

    // -------------------------------------------------------------------------
    //  Name candidates for each joint.
    //  Searched in order — first match in the hierarchy wins.
    //  Covers Body1/Mixamo naming, generic naming, and Brekel example skeleton.
    // -------------------------------------------------------------------------
    private static readonly string[] Names_hips       = { "Body1:Hips",         "Hips",        "mixamorig:Hips"        };
    private static readonly string[] Names_spine       = { "Body1:Spine",        "Spine",       "mixamorig:Spine"       };
    private static readonly string[] Names_chest       = { "Body1:Spine1",       "Spine1",      "mixamorig:Spine1",     "Chest", "Body1:Chest" };
    private static readonly string[] Names_neck        = { "Body1:Neck",         "Neck",        "mixamorig:Neck"        };
    private static readonly string[] Names_head        = { "Body1:Head",         "Head",        "mixamorig:Head"        };
    private static readonly string[] Names_upperLeg_L  = { "Body1:LeftUpLeg",    "LeftUpLeg",   "mixamorig:LeftUpLeg",  "LeftUpperLeg"  };
    private static readonly string[] Names_lowerLeg_L  = { "Body1:LeftLeg",      "LeftLeg",     "mixamorig:LeftLeg",    "LeftLowerLeg"  };
    private static readonly string[] Names_foot_L      = { "Body1:LeftFoot",     "LeftFoot",    "mixamorig:LeftFoot"    };
    private static readonly string[] Names_upperLeg_R  = { "Body1:RightUpLeg",   "RightUpLeg",  "mixamorig:RightUpLeg", "RightUpperLeg" };
    private static readonly string[] Names_lowerLeg_R  = { "Body1:RightLeg",     "RightLeg",    "mixamorig:RightLeg",   "RightLowerLeg" };
    private static readonly string[] Names_foot_R      = { "Body1:RightFoot",    "RightFoot",   "mixamorig:RightFoot"   };
    private static readonly string[] Names_collar_L    = { "Body1:LeftShoulder", "LeftShoulder","mixamorig:LeftShoulder"};
    private static readonly string[] Names_upperArm_L  = { "Body1:LeftArm",      "LeftArm",     "mixamorig:LeftArm",    "LeftUpperArm"  };
    private static readonly string[] Names_foreArm_L   = { "Body1:LeftForeArm",  "LeftForeArm", "mixamorig:LeftForeArm","LeftLowerArm"  };
    private static readonly string[] Names_hand_L      = { "Body1:LeftHand",     "LeftHand",    "mixamorig:LeftHand"    };
    private static readonly string[] Names_collar_R    = { "Body1:RightShoulder","RightShoulder","mixamorig:RightShoulder"};
    private static readonly string[] Names_upperArm_R  = { "Body1:RightArm",     "RightArm",    "mixamorig:RightArm",   "RightUpperArm" };
    private static readonly string[] Names_foreArm_R   = { "Body1:RightForeArm", "RightForeArm","mixamorig:RightForeArm","RightLowerArm" };
    private static readonly string[] Names_hand_R      = { "Body1:RightHand",    "RightHand",   "mixamorig:RightHand"   };
    private static readonly string[] Names_faceMesh    = { "Body1:Face_Mesh",    "Face_Mesh",   "FaceMesh",  "Face"    };

    // -------------------------------------------------------------------------
    //  Internal
    // -------------------------------------------------------------------------
    private Quaternion[]        _offsets = new Quaternion[(int)Brekel_joint_name_v3.numJoints];
    private SkinnedMeshRenderer _faceSMR;
    private const int           NumBlendshapes = (int)Brekel_blendshape_name.numBlendshapes;

    private float _lastDataTimestamp  = float.MinValue;
    private float _noDataTimer        = 0f;
    private bool  _characterVisible   = true;

    /// <summary>
    /// Returns the inverse bind-pose rotation for the given joint index.
    /// Used by HumanoidMapper to extract the motion delta from incoming data.
    /// </summary>
    public Quaternion GetBindPoseInverse(int jointIndex) => _offsets[jointIndex];


    // =========================================================================
    //  Unity lifecycle
    // =========================================================================
    void Start()
    {
        if (receiver == null)
        {
            Debug.LogError("[Brekel_Body_v3_DefaultMapper] No Receiver assigned — component will be inactive.");
            enabled = false;
            return;
        }

        // Auto-find any joints not already manually assigned
        if (character != null) AutoFindJoints();

        if (face_mesh != null)
            _faceSMR = face_mesh.GetComponent<SkinnedMeshRenderer>();

        StoreOffsets();

        ApplyJointMaterial();
    }

    void Update()
    {
        if (receiver == null || !receiver.IsConnected)
            return;

        body_ID = Mathf.Clamp(body_ID, 0, Brekel_Body_v3_Receiver.MaxBodies - 1);

        BrekelBodyFrame body = receiver.GetBody(body_ID);
        if (body == null)
            return;

        // Detect whether fresh data is arriving by watching the frame timestamp.
        bool freshData = !Mathf.Approximately(body.timestamp, _lastDataTimestamp);
        if (freshData)
        {
            _lastDataTimestamp = body.timestamp;
            _noDataTimer = 0f;
            SetCharacterVisible(true);
        }
        else
        {
            _noDataTimer += Time.deltaTime;
            if (_noDataTimer >= NoDataHideDelay)
                SetCharacterVisible(false);
        }

        if (!_characterVisible) return;

        ApplyJoint(hips,       Brekel_joint_name_v3.waist,       true,                body);
        ApplyJoint(spine,      Brekel_joint_name_v3.spine,       applyPositionsToAll, body);
        ApplyJoint(chest,      Brekel_joint_name_v3.chest,       applyPositionsToAll, body);
        ApplyJoint(neck,       Brekel_joint_name_v3.neck,        applyPositionsToAll, body);
        ApplyJoint(head,       Brekel_joint_name_v3.head,        applyPositionsToAll, body);
        ApplyJoint(upperLeg_L, Brekel_joint_name_v3.upperLeg_L,  applyPositionsToAll, body);
        ApplyJoint(lowerLeg_L, Brekel_joint_name_v3.lowerLeg_L,  applyPositionsToAll, body);
        ApplyJoint(foot_L,     Brekel_joint_name_v3.foot_L,      applyPositionsToAll, body);
        ApplyJoint(upperLeg_R, Brekel_joint_name_v3.upperLeg_R,  applyPositionsToAll, body);
        ApplyJoint(lowerLeg_R, Brekel_joint_name_v3.lowerLeg_R,  applyPositionsToAll, body);
        ApplyJoint(foot_R,     Brekel_joint_name_v3.foot_R,      applyPositionsToAll, body);
        ApplyJoint(collar_L,   Brekel_joint_name_v3.collar_L,    applyPositionsToAll, body);
        ApplyJoint(upperArm_L, Brekel_joint_name_v3.upperArm_L,  applyPositionsToAll, body);
        ApplyJoint(foreArm_L,  Brekel_joint_name_v3.foreArm_L,   applyPositionsToAll, body);
        ApplyJoint(hand_L,     Brekel_joint_name_v3.hand_L,      applyPositionsToAll, body);
        ApplyJoint(collar_R,   Brekel_joint_name_v3.collar_R,    applyPositionsToAll, body);
        ApplyJoint(upperArm_R, Brekel_joint_name_v3.upperArm_R,  applyPositionsToAll, body);
        ApplyJoint(foreArm_R,  Brekel_joint_name_v3.foreArm_R,   applyPositionsToAll, body);
        ApplyJoint(hand_R,     Brekel_joint_name_v3.hand_R,      applyPositionsToAll, body);

        ApplyBlendshapes(body);
    }


    // =========================================================================
    //  Auto-find
    // =========================================================================

    /// <summary>
    /// Searches the character hierarchy for each joint by a prioritised list
    /// of common bone names.  Call this from a context-menu button in the
    /// Editor, or it runs automatically at Start() for any unassigned fields.
    /// </summary>
    /// <param name="overwriteExisting">
    /// When true, replaces all fields even if already assigned.
    /// When false (default at runtime), only fills null fields.
    /// </param>
    [ContextMenu("Auto-Find Joints")]
    public void AutoFindJoints()
    {
        if (character == null)
        {
            Debug.LogWarning("[DefaultMapper] Auto-Find: assign a Character object first.");
            return;
        }

        // Clear all refs before searching
        hips = spine = chest = neck = head = null;
        upperLeg_L = lowerLeg_L = foot_L = null;
        upperLeg_R = lowerLeg_R = foot_R = null;
        collar_L = upperArm_L = foreArm_L = hand_L = null;
        collar_R = upperArm_R = foreArm_R = hand_R = null;
        face_mesh = null;

        int found = 0, total = 19;
        Transform root = character.transform;

        hips       = Find(Names_hips,       root, ref found);
        spine      = Find(Names_spine,      root, ref found);
        chest      = Find(Names_chest,      root, ref found);
        neck       = Find(Names_neck,       root, ref found);
        head       = Find(Names_head,       root, ref found);
        upperLeg_L = Find(Names_upperLeg_L, root, ref found);
        lowerLeg_L = Find(Names_lowerLeg_L, root, ref found);
        foot_L     = Find(Names_foot_L,     root, ref found);
        upperLeg_R = Find(Names_upperLeg_R, root, ref found);
        lowerLeg_R = Find(Names_lowerLeg_R, root, ref found);
        foot_R     = Find(Names_foot_R,     root, ref found);
        collar_L   = Find(Names_collar_L,   root, ref found);
        upperArm_L = Find(Names_upperArm_L, root, ref found);
        foreArm_L  = Find(Names_foreArm_L,  root, ref found);
        hand_L     = Find(Names_hand_L,     root, ref found);
        collar_R   = Find(Names_collar_R,   root, ref found);
        upperArm_R = Find(Names_upperArm_R, root, ref found);
        foreArm_R  = Find(Names_foreArm_R,  root, ref found);
        hand_R     = Find(Names_hand_R,     root, ref found);

        Transform faceT = Find(Names_faceMesh, root, ref found);
        if (faceT != null) { face_mesh = faceT.gameObject; total++; }

        Debug.Log($"[DefaultMapper] Auto-Find: {found}/{total} joints found in '{character.name}'.");
    }

    /// <summary>
    /// Searches root itself and its entire hierarchy. Returns null if not found.
    /// root (the character object) is checked explicitly first before descending into children.
    /// </summary>
    private static Transform Find(string[] names, Transform root, ref int found)
    {
        foreach (string n in names)
        {
            if (root.name == n) { found++; return root; }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == n) { found++; return t; }
        }
        return null;
    }


    // =========================================================================
    //  Apply helpers
    // =========================================================================
    private void ApplyJoint(Transform t, Brekel_joint_name_v3 joint, bool applyPos, BrekelBodyFrame body)
    {
        if (t == null) return;
        int idx = (int)joint;
        Quaternion targetRot = _offsets[idx] * body.joints[idx].rotation;
        if (applyPos)
        {
            Vector3 targetPos = body.joints[idx].position;
            t.localPosition = positionSmoothing > 0f
                ? Vector3.Lerp(t.localPosition, targetPos, positionSmoothing * Time.deltaTime)
                : targetPos;
        }
        t.localRotation = rotationSmoothing > 0f
            ? Quaternion.Slerp(t.localRotation, targetRot, rotationSmoothing * Time.deltaTime)
            : targetRot;
    }

    private void ApplyBlendshapes(BrekelBodyFrame body)
    {
        if (_faceSMR == null) return;
        int count = Mathf.Min(_faceSMR.sharedMesh.blendShapeCount, NumBlendshapes);
        for (int i = 0; i < count; i++)
            _faceSMR.SetBlendShapeWeight(i, body.blendshapes[i] * 100f);
    }


    // =========================================================================
    //  Joint material
    // =========================================================================
    [ContextMenu("Apply Material")]
    public void ApplyJointMaterial()
    {
        if (jointMaterial == null)
        {
            Debug.LogWarning("[DefaultMapper] Apply Material: no Material assigned.");
            return;
        }

        if (character == null)
        {
            Debug.LogWarning("[DefaultMapper] Apply Material: no Character assigned.");
            return;
        }

        int count = 0;
        foreach (Renderer r in character.GetComponentsInChildren<Renderer>(true))
        {
            r.material = jointMaterial;
            count++;
        }

        Debug.Log($"[DefaultMapper] Apply Material: set '{jointMaterial.name}' on {count} Renderer(s) in '{character.name}'.");
    }


    // =========================================================================
    //  Rest-pose offset baking
    // =========================================================================
    private void StoreOffsets()
    {
        BakeOffset(hips,       Brekel_joint_name_v3.waist);
        BakeOffset(spine,      Brekel_joint_name_v3.spine);
        BakeOffset(chest,      Brekel_joint_name_v3.chest);
        BakeOffset(neck,       Brekel_joint_name_v3.neck);
        // head intentionally skipped (matches original Brekel script behaviour)
        BakeOffset(upperLeg_L, Brekel_joint_name_v3.upperLeg_L);
        BakeOffset(lowerLeg_L, Brekel_joint_name_v3.lowerLeg_L);
        BakeOffset(foot_L,     Brekel_joint_name_v3.foot_L);
        BakeOffset(upperLeg_R, Brekel_joint_name_v3.upperLeg_R);
        BakeOffset(lowerLeg_R, Brekel_joint_name_v3.lowerLeg_R);
        BakeOffset(foot_R,     Brekel_joint_name_v3.foot_R);
        BakeOffset(collar_L,   Brekel_joint_name_v3.collar_L);
        BakeOffset(upperArm_L, Brekel_joint_name_v3.upperArm_L);
        BakeOffset(foreArm_L,  Brekel_joint_name_v3.foreArm_L);
        BakeOffset(hand_L,     Brekel_joint_name_v3.hand_L);
        BakeOffset(collar_R,   Brekel_joint_name_v3.collar_R);
        BakeOffset(upperArm_R, Brekel_joint_name_v3.upperArm_R);
        BakeOffset(foreArm_R,  Brekel_joint_name_v3.foreArm_R);
        BakeOffset(hand_R,     Brekel_joint_name_v3.hand_R);

    }

    private void BakeOffset(Transform t, Brekel_joint_name_v3 joint)
    {
        if (t != null)
            _offsets[(int)joint] = Quaternion.Inverse(t.localRotation);
    }

    private void SetCharacterVisible(bool visible)
    {
        if (_characterVisible == visible) return;
        _characterVisible = visible;
        if (hips != null) hips.gameObject.SetActive(visible);
    }
}

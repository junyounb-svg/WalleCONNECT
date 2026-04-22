using UnityEngine;

/// <summary>
/// Attaches to an invisible GameObject and keeps its world position
/// locked to a specific Brekel body's waist joint each frame.
///
/// OFFLINE TESTING:
///   Tick "Manual Override" in the Inspector. IsTracked will always
///   return true and the GameObject will NOT be moved by Brekel —
///   so you can drag it around freely (or use TestPersonMover).
///   Untick when Brekel Body v3 is connected and ready to stream.
/// </summary>
public class PersonAnchor : MonoBehaviour
{
    [Header("Tracking")]
    [Tooltip("The shared Brekel_Body_v3_Receiver in the scene")]
    public Brekel_Body_v3_Receiver receiver;

    [Tooltip("0 = Person 1, 1 = Person 2 (matches Brekel stream index)")]
    public int bodyID = 0;

    [Tooltip("Minimum confidence on the waist joint to count as tracked")]
    [Range(0f, 1f)]
    public float minConfidence = 0.1f;

    [Header("Offline Testing")]
    [Tooltip("When ticked: IsTracked is always true and Brekel is ignored. " +
             "Move this GameObject manually or with TestPersonMover to simulate a person.")]
    public bool manualOverride = false;

    /// <summary>True when the body is actively being received (or manualOverride is on).</summary>
    public bool IsTracked { get; private set; }

    void Update()
    {
        if (manualOverride)
        {
            IsTracked = true;
            return;
        }

        IsTracked = false;

        if (receiver == null || !receiver.IsConnected) return;

        BrekelBodyFrame body = receiver.GetBody(bodyID);
        if (body == null) return;

        BrekelJoint waist = body.joints[(int)Brekel_joint_name_v3.waist];
        if (waist.confidence < minConfidence) return;

        transform.position = waist.position;
        IsTracked = true;
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class QuestBodyUdpSender : MonoBehaviour
{
    [Header("Networking")]
    [Tooltip("PC IP address running the Python UDP receiver (same LAN/Wi-Fi).")]
    public string remoteIp = "10.20.21.11";

    [Tooltip("UDP port on the PC to send to (python --port).")]
    public int remotePort = 9000;

    [Tooltip("Send rate in Hz (e.g., 30).")]
    public int sendHz = 30;

    [Header("Body Tracking")]
    [Tooltip("Reference to OVRBody component (on your rig).")]
    public OVRBody ovrBody;

    [Tooltip("Optional transform to report as the HMD (falls back to the main camera).")]
    public Transform hmdTransform;

    // Internal
    private UdpClient _udp;
    private IPEndPoint _remoteEndPoint;
    private float _sendInterval;
    private float _nextSendTime;

    void Awake()
    {
        if (ovrBody == null)
        {
            ovrBody = FindObjectOfType<OVRBody>();
        }

        if (hmdTransform == null && Camera.main != null)
        {
            hmdTransform = Camera.main.transform;
        }

        _sendInterval = (sendHz <= 0) ? 0.0333f : (1.0f / sendHz);
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
        _udp = new UdpClient();
        _udp.Client.SendTimeout = 5; // ms; keep small
        _nextSendTime = Time.unscaledTime;
    }

    void OnDestroy()
    {
        _udp?.Close();
        _udp = null;
    }

    void Update()
    {
        if (ovrBody == null)
            return;

        // Throttle to sendHz
        if (Time.unscaledTime < _nextSendTime)
            return;

        _nextSendTime = Time.unscaledTime + _sendInterval;

        // Ensure body tracking is active and has data
        // OVRBody provides pose in tracking space; data availability depends on device/support.
        if (!TryIsBodyTracked(ovrBody))
            return;

        // Read joints.
        // OVRBody exposes a BodyState; exact API can differ by SDK version.
        if (!TryGetBodyJoints(out List<JointSample> joints))
            return;

        PosePacket packet = BuildPacket(joints);
        byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));

        try
        {
            _udp.Send(payload, payload.Length, _remoteEndPoint);
        }
        catch (Exception)
        {
            // Network hiccups are ignored; Unity console will keep running.
        }
    }

    // Serializable containers for the JSON payload expected by the Python OSC receiver
    [Serializable]
    private struct PosePacket
    {
        public double timestamp;
        public PoseTransform hmd;
        public List<JointPayload> joints;
    }

    [Serializable]
    private struct PoseTransform
    {
        public SerializableVector3 position;
        public SerializableQuaternion rotation;
    }

    [Serializable]
    private struct JointPayload
    {
        public string name;
        public PoseTransform pose;
        public float confidence;
    }

    [Serializable]
    private struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    private struct SerializableQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    // A small container for one joint sample
    private struct JointSample
    {
        public string name;
        public Vector3 pos;
        public Quaternion rot;
    }

    private bool TryGetBodyJoints(out List<JointSample> joints)
    {
        joints = null;

        // Use TryGetComponent to avoid allocations
        if (!ovrBody.TryGetComponent<OVRSkeleton>(out OVRSkeleton skel))
            skel = ovrBody.GetComponentInChildren<OVRSkeleton>(true);

        if (skel == null)
            return false;

        // Bones list may not be ready immediately.
        var bones = skel.Bones;
        if (bones == null || bones.Count == 0)
            return false;

        joints = new List<JointSample>(bones.Count);
        for (int i = 0; i < bones.Count; i++)
        {
            Transform t = bones[i].Transform;
            joints.Add(new JointSample
            {
                name = bones[i].Id.ToString(),
                pos = t.position,
                rot = t.rotation,
            });
        }

        return true;
    }

    private PosePacket BuildPacket(List<JointSample> joints)
    {
        PosePacket packet = new PosePacket
        {
            timestamp = Time.timeAsDouble,
            hmd = BuildTransform(hmdTransform),
            joints = new List<JointPayload>(joints.Count),
        };

        float confidence = 0f;
        if (ovrBody.TryGetComponent<OVRSkeleton>(out OVRSkeleton skeleton))
        {
            confidence = (skeleton.IsDataValid && skeleton.IsDataHighConfidence) ? 1f : 0f;
        }

        for (int i = 0; i < joints.Count; i++)
        {
            var j = joints[i];
            packet.joints.Add(new JointPayload
            {
                name = j.name,
                pose = BuildTransform(j.pos, j.rot),
                confidence = confidence,
            });
        }

        return packet;
    }

    private PoseTransform BuildTransform(Transform source)
    {
        if (source == null)
        {
            return new PoseTransform
            {
                position = new SerializableVector3(),
                rotation = new SerializableQuaternion { w = 1f },
            };
        }

        return BuildTransform(source.position, source.rotation);
    }

    private PoseTransform BuildTransform(Vector3 pos, Quaternion rot)
    {
        return new PoseTransform
        {
            position = new SerializableVector3 { x = pos.x, y = pos.y, z = pos.z },
            rotation = new SerializableQuaternion { x = rot.x, y = rot.y, z = rot.z, w = rot.w },
        };
    }

    /// <summary>
    /// Checks if the OVRBody is currently tracked by verifying skeleton validity and confidence.
    /// </summary>
    private static bool TryIsBodyTracked(OVRBody body)
    {
        if (body == null)
            return false;

        OVRSkeleton skeleton;
        if (!body.TryGetComponent<OVRSkeleton>(out skeleton))
            skeleton = body.GetComponentInChildren<OVRSkeleton>(true);

        if (skeleton == null)
            return false;

        // Use IsDataValid and IsDataHighConfidence as tracking indicators
        return skeleton.IsDataValid && skeleton.IsDataHighConfidence;
    }
}

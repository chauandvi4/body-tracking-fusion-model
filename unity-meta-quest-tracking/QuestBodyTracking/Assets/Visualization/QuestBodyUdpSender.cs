using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using MessagePack;
using UnityEngine.InputSystem;

public class QuestBodyUdpSender : MonoBehaviour
{
    private const int MaxUdpPacketSize = 65507; // Max safe UDP packet size
    [Header("Networking")]
    [Tooltip("PC IP address running the Python UDP receiver (same LAN/Wi-Fi).")]
    public string remoteIp = "10.20.21.11";

    [Tooltip("UDP port on the PC to send to (python --port).")]
    public int remotePort = 9000;

    [Tooltip("Send rate in Hz (e.g., 30).")]
    public int sendHz = 30;

    [Header("Body Tracking (Visualization)")]
    [Tooltip("Reference to OVRBody component (on the rig).")]
    public OVRBody ovrBody;

    [Tooltip("Optional transform to report as the HMD (falls back to the main camera).")]
    public Transform hmdTransform;

    // Internal
    private UdpClient _udp;
    private IPEndPoint _remoteEndPoint;
    private float _sendInterval;
    private float _nextSendTime;
    private MessagePackSerializerOptions _messagePackOptions;
    private float _lastOversizeLogTime;

    void Awake()
    {
        ovrBody ??= FindObjectOfType<OVRBody>();

        if (hmdTransform == null)
        {
            var rig = FindObjectOfType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                hmdTransform = rig.centerEyeAnchor;
            }
            else if (Camera.main != null)
            {
                hmdTransform = Camera.main.transform;
            }
        }

        _sendInterval = (sendHz <= 0) ? 0.0333f : (1.0f / sendHz);
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
        _udp = new UdpClient();
        _udp.Client.SendTimeout = 5; // ms; keep small
        _nextSendTime = Time.unscaledTime;
        _messagePackOptions = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);
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

        PipelinePosePacket packet = BuildPacket(joints);
        byte[] payload = MessagePackSerializer.Serialize(packet, _messagePackOptions);

        if (payload.Length > MaxUdpPacketSize)
        {
            if (Time.unscaledTime - _lastOversizeLogTime > 1f)
            {
                _lastOversizeLogTime = Time.unscaledTime;
                Debug.LogWarning($"[QuestBodyUdpSender] Payload {payload.Length} bytes exceeds safe UDP size ({MaxUdpPacketSize} bytes); dropping to avoid fragmentation.");
            }
            return;
        }

        try
        {
            _udp.Send(payload, payload.Length, _remoteEndPoint);
        }
        catch (Exception)
        {
            // Network hiccups are ignored; Unity console will keep running.
        }
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

    private PipelinePosePacket BuildPacket(List<JointSample> joints)
    {
        string visualizationSource = PipelineSwitches.GetVisualizationSourceLabel();

       PipelinePosePacket packet = new()
       {
           pipeline = PipelineKind.Visualization,
           pipelineSource = visualizationSource,
           timestamp = Time.timeAsDouble,
           hmd = BuildTransform(hmdTransform),
           joints = new List<JointPayload>(joints.Count),
           metadata = new PacketMetadata
           {
               visualizationOnly = true,
               analysisSource = PipelineSwitches.GetAnalysisSourceLabel(),
               visualizationSource = visualizationSource,
               notes = "Movement SDK visualization stream.",
           },
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
            position = new SerializableVector3(pos),
            rotation = new SerializableQuaternion(rot),
        };
    }

    /// <summary>
    /// Checks if the OVRBody is currently tracked by verifying skeleton validity and confidence.
    /// </summary>
    private static bool TryIsBodyTracked(OVRBody body)
    {
        if (body == null)
            return false;

        if (!body.TryGetComponent<OVRSkeleton>(out OVRSkeleton skeleton))
            skeleton = body.GetComponentInChildren<OVRSkeleton>(true);

        if (skeleton == null)
            return false;

        // Use IsDataValid and IsDataHighConfidence as tracking indicators
        return skeleton.IsDataValid && skeleton.IsDataHighConfidence;
    }
}

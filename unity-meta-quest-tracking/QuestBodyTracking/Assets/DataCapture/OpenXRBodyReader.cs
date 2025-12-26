using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using MessagePack;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Collects raw OpenXR XRNode poses and streams them over UDP for the analysis pipeline.
/// </summary>
public class OpenXRBodyReader : MonoBehaviour
{
    private const int MaxUdpPacketSize = 65507;

    [Header("Networking (Analysis Path)")]
    public string remoteIp = "127.0.0.1";
    public int remotePort = 9100;
    public int sendHz = 30;

    [Header("OpenXR bindings")]
    [Tooltip("Optional override when no CenterEye node is available.")]
    public Transform fallbackHmd;

    [Tooltip("XR nodes to treat as OpenXR body joints for analysis.")]
    public List<OpenXRJointBinding> jointBindings = new()
    {
        new OpenXRJointBinding("head", XRNode.Head),
        new OpenXRJointBinding("left_hand", XRNode.LeftHand),
        new OpenXRJointBinding("right_hand", XRNode.RightHand),
        new OpenXRJointBinding("center_eye", XRNode.CenterEye),
    };

    private readonly List<XRNodeState> _nodeStates = new();
    private UdpClient _udp;
    private IPEndPoint _remoteEndPoint;
    private float _sendInterval;
    private float _nextSendTime;
    private MessagePackSerializerOptions _messagePackOptions;
    private float _lastOversizeLogTime;

    private void Awake()
    {
        _sendInterval = (sendHz <= 0) ? 0.0333f : (1.0f / sendHz);
        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
        _udp = new UdpClient();
        _udp.Client.SendTimeout = 5;
        _nextSendTime = Time.unscaledTime;
        _messagePackOptions = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);
    }

    private void OnDestroy()
    {
        _udp?.Close();
        _udp = null;
    }

    private void Update()
    {
        // Runtime switch allows disabling OpenXR ingress when MediaPipe-only analysis is desired.
        if (PipelineSwitches.AnalysisSource == AnalysisSourceOption.MediaPipeOnly)
            return;

        if (Time.unscaledTime < _nextSendTime)
            return;

        _nextSendTime = Time.unscaledTime + _sendInterval;

        PipelinePosePacket packet = BuildPacket();
        byte[] payload = MessagePackSerializer.Serialize(packet, _messagePackOptions);

        if (payload.Length > MaxUdpPacketSize)
        {
            if (Time.unscaledTime - _lastOversizeLogTime > 1f)
            {
                _lastOversizeLogTime = Time.unscaledTime;
                Debug.LogWarning($"[OpenXRBodyReader] Payload {payload.Length} bytes exceeds safe UDP size ({MaxUdpPacketSize}); dropping.");
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

    private PipelinePosePacket BuildPacket()
    {
        string analysisSource = PipelineSwitches.GetAnalysisSourceLabel();

        PipelinePosePacket packet = new()
        {
            pipeline = PipelineKind.Analysis,
            pipelineSource = analysisSource,
            timestamp = Time.timeAsDouble,
            hmd = BuildHmdPose(),
            joints = new List<JointPayload>(jointBindings.Count),
            metadata = new PacketMetadata
            {
                visualizationOnly = false,
                analysisSource = analysisSource,
                visualizationSource = PipelineSwitches.GetVisualizationSourceLabel(),
                notes = "Authoritative OpenXR joints for analysis (no IK/retargeting).",
            },
        };

        for (int i = 0; i < jointBindings.Count; i++)
        {
            OpenXRJointBinding binding = jointBindings[i];
            if (TryGetPoseForNode(binding.node, out Vector3 pos, out Quaternion rot))
            {
                packet.joints.Add(new JointPayload
                {
                    name = binding.jointName,
                    pose = BuildTransform(pos, rot),
                    confidence = 1f,
                });
            }
        }

        return packet;
    }

    private PoseTransform BuildHmdPose()
    {
        if (TryGetPoseForNode(XRNode.CenterEye, out Vector3 centerPos, out Quaternion centerRot))
        {
            return BuildTransform(centerPos, centerRot);
        }

        if (fallbackHmd != null)
        {
            return BuildTransform(fallbackHmd.position, fallbackHmd.rotation);
        }

        return new PoseTransform
        {
            position = new SerializableVector3(),
            rotation = new SerializableQuaternion { w = 1f },
        };
    }

    private bool TryGetPoseForNode(XRNode node, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = default;

        InputTracking.GetNodeStates(_nodeStates);
        for (int i = 0; i < _nodeStates.Count; i++)
        {
            XRNodeState state = _nodeStates[i];
            if (state.nodeType != node)
                continue;

            if (state.TryGetPosition(out Vector3 p) && state.TryGetRotation(out Quaternion q))
            {
                pos = p;
                rot = q;
                return true;
            }
        }

        return false;
    }

    private static PoseTransform BuildTransform(Vector3 pos, Quaternion rot)
    {
        return new PoseTransform
        {
            position = new SerializableVector3(pos),
            rotation = new SerializableQuaternion(rot),
        };
    }
}

[Serializable]
public struct OpenXRJointBinding
{
    public string jointName;
    public XRNode node;

    public OpenXRJointBinding(string name, XRNode xrNode)
    {
        jointName = name;
        node = xrNode;
    }
}
using System;
using System.Collections.Generic;
using MessagePack;
using UnityEngine;

public enum VisualizationSourceOption
{
    MovementSdkOnly,
    MovementSdkPlusMediaPipe
}

public enum AnalysisSourceOption
{
    OpenXrPlusMediaPipe,
    MediaPipeOnly
}

public enum PipelineKind
{
    Analysis,
    Visualization
}

[Serializable]
[MessagePackObject]
public class PacketMetadata
{
    [Key("visualization_only")]
    public bool visualizationOnly;

    [Key("analysis_source")]
    public string analysisSource;

    [Key("visualization_source")]
    public string visualizationSource;

    [Key("notes")]
    public string notes;
}

[Serializable]
[MessagePackObject]
public class PipelinePosePacket
{
    [Key("pipeline")]
    public PipelineKind pipeline;

    [Key("pipeline_source")]
    public string pipelineSource;

    [Key("timestamp")]
    public double timestamp;

    [Key("hmd")]
    public PoseTransform hmd;

    [Key("joints")]
    public List<JointPayload> joints;

    [Key("metadata")]
    public PacketMetadata metadata;
}

[Serializable]
[MessagePackObject]
public class PoseTransform
{
    [Key("position")]
    public SerializableVector3 position;

    [Key("rotation")]
    public SerializableQuaternion rotation;
}

[Serializable]
[MessagePackObject]
public class JointPayload
{
    [Key("name")]
    public string name;

    [Key("pose")]
    public PoseTransform pose;

    [Key("confidence")]
    public float confidence;
}

[Serializable]
[MessagePackObject]
public struct SerializableVector3
{
    [Key("x")]
    public float x;
    [Key("y")]
    public float y;
    [Key("z")]
    public float z;

    public SerializableVector3(Vector3 source)
    {
        x = source.x;
        y = source.y;
        z = source.z;
    }
}

[Serializable]
[MessagePackObject]
public struct SerializableQuaternion
{
    [Key("x")]
    public float x;
    [Key("y")]
    public float y;
    [Key("z")]
    public float z;
    [Key("w")]
    public float w;

    public SerializableQuaternion(Quaternion source)
    {
        x = source.x;
        y = source.y;
        z = source.z;
        w = source.w;
    }
}
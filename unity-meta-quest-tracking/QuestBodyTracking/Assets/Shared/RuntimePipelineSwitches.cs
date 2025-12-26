using UnityEngine;

/// <summary>
/// Central runtime switches ensuring the analysis and visualization pipelines stay isolated.
/// </summary>
public static class PipelineSwitches
{
    public static VisualizationSourceOption VisualizationSource { get; set; } = VisualizationSourceOption.MovementSdkOnly;
    public static AnalysisSourceOption AnalysisSource { get; set; } = AnalysisSourceOption.OpenXrPlusMediaPipe;

    public static string GetVisualizationSourceLabel()
    {
        return VisualizationSource == VisualizationSourceOption.MovementSdkPlusMediaPipe
            ? "movement-sdk+mediapipe"
            : "movement-sdk-only";
    }

    public static string GetAnalysisSourceLabel()
    {
        return AnalysisSource == AnalysisSourceOption.OpenXrPlusMediaPipe
            ? "openxr+mediapipe"
            : "mediapipe-only";
    }
}

/// <summary>
/// MonoBehaviour hook to expose the pipeline switches in the Unity Inspector.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class RuntimePipelineSwitches : MonoBehaviour
{
    [Header("Visualization Path")]
    public VisualizationSourceOption visualizationSource = VisualizationSourceOption.MovementSdkOnly;

    [Header("Analysis Path")]
    public AnalysisSourceOption analysisSource = AnalysisSourceOption.OpenXrPlusMediaPipe;

    private void OnEnable()
    {
        Apply();
    }

    public void Apply()
    {
        PipelineSwitches.VisualizationSource = visualizationSource;
        PipelineSwitches.AnalysisSource = analysisSource;
    }
}
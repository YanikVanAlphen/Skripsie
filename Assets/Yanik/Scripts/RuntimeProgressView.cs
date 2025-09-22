using UnityEngine;

namespace UnityVolumeRendering
{
  public class RuntimeProgressView : IProgressView
  {
    public void StartProgress(string title, string description)
    {
      Debug.Log($"Starting progress: {title} - {description}");
    }

    public void FinishProgress(ProgressStatus status)
    {
      string statusMessage = status switch
      {
        ProgressStatus.Succeeded => "Succeeded",
        ProgressStatus.Failed => "Failed",
        _ => "Unknown"
      };
      Debug.Log($"Progress finished with status: {statusMessage}");
    }

    public void UpdateProgress(float progress, float weight, string description)
    {
      Debug.Log($"Progress: {progress * 100}% (weight: {weight}) - {description}");
    }
  }
}
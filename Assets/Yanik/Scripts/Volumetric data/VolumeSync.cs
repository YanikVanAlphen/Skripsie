using FishNet.Object;
using UnityEngine;
using UnityVolumeRendering;

public class VolumeSync : NetworkBehaviour
{
  private VolumeRenderedObject volumeObject;

  private void Awake()
  {
    StartCoroutine(InitializeVolumeObject());
  }

  private System.Collections.IEnumerator InitializeVolumeObject()
  {
    int retryCount = 0;
    const int maxRetries = 40; // Wait up to 20 seconds
    while (volumeObject == null && retryCount < maxRetries)
    {
      volumeObject = FindObjectOfType<VolumeRenderedObject>();
      if (volumeObject == null)
      {
        Debug.Log("Waiting for VolumeRenderedObject component...");
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }

    if (volumeObject == null)
    {
      Debug.LogError("Failed to find VolumeRenderedObject after max retries.");
      yield break;
    }

    Debug.Log("VolumeSync initialized with VolumeRenderedObject.");
    ApplyRenderSettings();
  }

  [ServerRpc(RequireOwnership = false)]
  public void UpdateRenderMode(UnityVolumeRendering.RenderMode mode)
  {
    if (volumeObject != null)
    {
      volumeObject.SetRenderMode(mode);
      RpcUpdateRenderMode(mode);
      Debug.Log($"Render mode updated to {mode}");
    }
  }

  [ObserversRpc]
  private void RpcUpdateRenderMode(UnityVolumeRendering.RenderMode mode)
  {
    if (volumeObject != null)
    {
      volumeObject.SetRenderMode(mode);
      Debug.Log($"Client updated render mode to {mode}");
    }
  }

  [ServerRpc(RequireOwnership = false)]
  public void UpdateVisibleRange(Vector2 range)
  {
    if (volumeObject != null)
    {
      volumeObject.SetVisibilityWindow(range);
      RpcUpdateVisibleRange(range);
      Debug.Log($"Visibility window updated to {range}");
    }
  }

  [ObserversRpc]
  private void RpcUpdateVisibleRange(Vector2 range)
  {
    if (volumeObject != null)
    {
      volumeObject.SetVisibilityWindow(range);
      Debug.Log($"Client updated visibility window to {range}");
    }
  }

  private void ApplyRenderSettings()
  {
    if (volumeObject != null)
    {
      volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
      volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f));
      Debug.Log("Applied initial render settings.");
    }
  }
}
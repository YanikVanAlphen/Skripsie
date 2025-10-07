using FishNet.Object;
using UnityEngine;
using UnityVolumeRendering; // Volume rendering plugin from mlavik
using System.Collections;

/// <summary>
/// Manages any networked changes to the rendering of volumetric data
/// </summary>
public class VolumeSync : NetworkBehaviour
{
  private VolumeRenderedObject volumeObject;

  private void Awake()
  {
    StartCoroutine(InitVolumeObject());
  }

  // find volume object on startup, apply rendering settings and store to serve later RPCs
  private IEnumerator InitVolumeObject()
  {
    int retryCount = 0;
    const int maxRetries = 40; // Wait up to 20 seconds
    while (volumeObject == null && retryCount < maxRetries)
    {
      volumeObject = FindObjectOfType<VolumeRenderedObject>();
      if (volumeObject == null)
      {
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }

    if (volumeObject == null)
      yield break;
    ApplyRenderSettings();
  }

  [ServerRpc(RequireOwnership = false)]
  public void UpdateScaleServerRpc(Vector3 scale)
  {
    if (volumeObject != null)
    {
      volumeObject.transform.localScale = scale;
      RpcUpdateScale(scale);
      Debug.Log($"VolumeSync: Server updated VolumeRenderedObject scale to {scale}");
    }
  }

  [ObserversRpc]
  private void RpcUpdateScale(Vector3 scale)
  {
    if (volumeObject != null)
    {
      volumeObject.transform.localScale = scale;
      Debug.Log($"VolumeSync: Client updated VolumeRenderedObject scale to {scale}");
    }
  }

  private void ApplyRenderSettings()
  {
    if (volumeObject != null)
    {
      volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
      volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f));
      Debug.Log("VolumeSync: Applied initial render settings to volumetric dataset.");
    }
  }

  [ServerRpc(RequireOwnership = false)] // all clients allowed to call this function
  public void UpdateRenderMode(UnityVolumeRendering.RenderMode mode)
  {
    if (volumeObject != null)
    {
      volumeObject.SetRenderMode(mode);
      RpcUpdateRenderMode(mode);
      Debug.Log($"VolumeSync: Render mode updated to {mode}");
    }
  }

  [ObserversRpc] // propagate the updated change to all other clients
  private void RpcUpdateRenderMode(UnityVolumeRendering.RenderMode mode)
  {
    if (volumeObject != null)
    {
      volumeObject.SetRenderMode(mode);
      Debug.Log($"VolumeSync: Client updated render mode to {mode}");
    }
  }

  [ServerRpc(RequireOwnership = false)] // all clients allowed to call this function
  public void UpdateVisibleRange(Vector2 range)
  {
    if (volumeObject != null)
    {
      volumeObject.SetVisibilityWindow(range);
      RpcUpdateVisibleRange(range);
      Debug.Log($"VolumeSync: Visibility window updated to {range}");
    }
  }

  [ObserversRpc] // propagate the updated change to all other clients
  private void RpcUpdateVisibleRange(Vector2 range)
  {
    if (volumeObject != null)
    {
      volumeObject.SetVisibilityWindow(range);
      Debug.Log($"VolumeSync: Client updated visibility window to {range}");
    }
  }
}
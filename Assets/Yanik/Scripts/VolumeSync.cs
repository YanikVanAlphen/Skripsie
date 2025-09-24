using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityVolumeRendering;
using System.Collections;

public class VolumeSync : NetworkBehaviour
{
  [SyncVar(OnChange = nameof(OnRenderModeChanged))]
  private UnityVolumeRendering.RenderMode renderMode = UnityVolumeRendering.RenderMode.DirectVolumeRendering;

  [SyncVar(OnChange = nameof(OnVisibilityWindowChanged))]
  private Vector2 visibilityWindow = new Vector2(0.0f, 1.0f);

  private VolumeRenderedObject volumeObject;

  private void Awake()
  {
    // Defer initialization to coroutine to wait for VolumeRenderedObject
    StartCoroutine(InitializeVolumeObject());
  }

  private IEnumerator InitializeVolumeObject()
  {
    // Wait for VolumeRenderedObject to be added
    while (volumeObject == null)
    {
      volumeObject = GetComponent<VolumeRenderedObject>();
      if (volumeObject == null)
      {
        Debug.Log("Waiting for VolumeRenderedObject component...");
        yield return new WaitForSeconds(0.5f);
      }
    }

    Debug.Log("VolumeSync initialized with VolumeRenderedObject.");
    // Apply initial settings
    ApplyRenderSettings();
  }

  public void UpdateRenderMode(UnityVolumeRendering.RenderMode newMode)
  {
    if (IsServer)
    {
      renderMode = newMode;
    }
    else
    {
      CmdUpdateRenderMode(newMode);
    }
  }

  public void UpdateVisibleRange(Vector2 newRange)
  {
    if (IsServer)
    {
      visibilityWindow = newRange;
    }
    else
    {
      CmdUpdateVisibleRange(newRange);
    }
  }

  [ServerRpc(RequireOwnership = false)]
  private void CmdUpdateRenderMode(UnityVolumeRendering.RenderMode newMode)
  {
    renderMode = newMode;
  }

  [ServerRpc(RequireOwnership = false)]
  private void CmdUpdateVisibleRange(Vector2 newRange)
  {
    visibilityWindow = newRange;
  }

  private void OnRenderModeChanged(UnityVolumeRendering.RenderMode oldMode, UnityVolumeRendering.RenderMode newMode, bool asServer)
  {
    if (volumeObject != null)
    {
      volumeObject.SetRenderMode(newMode);
      Debug.Log($"Render mode updated to {newMode} on {(asServer ? "server" : "client")}.");
    }
  }

  private void OnVisibilityWindowChanged(Vector2 oldRange, Vector2 newRange, bool asServer)
  {
    if (volumeObject != null)
    {
      volumeObject.SetVisibilityWindow(newRange);
      Debug.Log($"Visibility window updated to {newRange} on {(asServer ? "server" : "client")}.");
    }
  }

  private void ApplyRenderSettings()
  {
    if (volumeObject != null)
    {
      volumeObject.SetRenderMode(renderMode);
      volumeObject.SetVisibilityWindow(visibilityWindow);
      Debug.Log("Applied initial render settings.");
    }
  }
}
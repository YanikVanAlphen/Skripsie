using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityVolumeRendering;

public class VolumeSync : NetworkBehaviour
{
  private VolumeRenderedObject volumeObject;

  [SyncVar(OnChange = nameof(OnRenderModeChanged))]
  private UnityVolumeRendering.RenderMode renderMode;
  [SyncVar(OnChange = nameof(OnVisibleRangeChanged))]
  private Vector2 visibleValueRange;
  [SyncVar(OnChange = nameof(OnLightingChanged))]
  private bool enableLighting;
  [SyncVar(OnChange = nameof(OnCubicInterpolationChanged))]
  private bool enableCubicInterpolation;

  private void Awake()
  {
    volumeObject = GetComponent<VolumeRenderedObject>();
    if (volumeObject == null)
    {
      Debug.LogError($"No VolumeRenderedObject found on {gameObject.name}");
    }
  }

  public override void OnStartClient()
  {
    base.OnStartClient();
    if (IsOwner)
    {
      UpdateRenderSettings();
    }
  }

  [ServerRpc(RequireOwnership = false)]
  public void UpdateRenderMode(UnityVolumeRendering.RenderMode mode)
  {
    renderMode = mode;
  }

  [ServerRpc(RequireOwnership = false)]
  public void UpdateVisibleRange(Vector2 range)
  {
    visibleValueRange = range;
  }

  [ServerRpc(RequireOwnership = false)]
  public void UpdateLighting(bool enabled)
  {
    enableLighting = enabled;
  }

  [ServerRpc(RequireOwnership = false)]
  public void UpdateCubicInterpolation(bool enabled)
  {
    enableCubicInterpolation = enabled;
  }

  private void OnRenderModeChanged(UnityVolumeRendering.RenderMode oldMode, UnityVolumeRendering.RenderMode newMode, bool asServer)
  {
    if (volumeObject == null)
    {
      Debug.LogError($"VolumeRenderedObject is null on {gameObject.name} during render mode change");
      return;
    }
    volumeObject.SetRenderMode(newMode);
    Debug.Log($"Updated render mode for {gameObject.name} to {newMode}");
  }

  private void OnVisibleRangeChanged(Vector2 oldRange, Vector2 newRange, bool asServer)
  {
    if (volumeObject == null)
    {
      Debug.LogError($"VolumeRenderedObject is null on {gameObject.name} during visible range change");
      return;
    }
    volumeObject.SetVisibilityWindow(newRange.x, newRange.y);
    Debug.Log($"Updated visible range for {gameObject.name} to {newRange}");
  }

  private void OnLightingChanged(bool oldValue, bool newValue, bool asServer)
  {
    if (volumeObject == null)
    {
      Debug.LogError($"VolumeRenderedObject is null on {gameObject.name} during lighting change");
      return;
    }
    volumeObject.SetLightingEnabled(newValue);
    Debug.Log($"Updated lighting for {gameObject.name} to {newValue}");
  }

  private void OnCubicInterpolationChanged(bool oldValue, bool newValue, bool asServer)
  {
    if (volumeObject == null)
    {
      Debug.LogError($"VolumeRenderedObject is null on {gameObject.name} during cubic interpolation change");
      return;
    }
    volumeObject.SetCubicInterpolationEnabled(newValue);
    Debug.Log($"Updated cubic interpolation for {gameObject.name} to {newValue}");
  }

  public void UpdateRenderSettings()
  {
    if (volumeObject != null)
    {
      UpdateRenderMode(volumeObject.GetRenderMode());
      UpdateVisibleRange(new Vector2(volumeObject.GetVisibilityWindow().x, volumeObject.GetVisibilityWindow().y));
      UpdateLighting(volumeObject.GetLightingEnabled());
      UpdateCubicInterpolation(volumeObject.GetCubicInterpolationEnabled());
    }
    else
    {
      Debug.LogWarning($"Cannot update render settings; VolumeRenderedObject is null on {gameObject.name}");
    }
  }
}
using UnityEngine;
using UnityEngine.UI;
using FishNet.Object;

[RequireComponent(typeof(Canvas))]
public class XRCanvasUpdater : NetworkBehaviour
{
  private Canvas canvas;
  private Camera xrCamera;

  public override void OnStartClient()
  {
    base.OnStartClient();
    canvas = GetComponent<Canvas>();
    UpdateCameraForClient();
  }

  [ObserversRpc(IncludeOwner = true)]
  private void UpdateCameraForClient()
  {
    // Find the local player's XR camera
    xrCamera = Camera.main;
    if (xrCamera != null && canvas != null)
    {
      canvas.worldCamera = xrCamera; // Set the event camera for this client
      GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
      if (raycaster != null)
      {
        raycaster.enabled = true; // Enable raycasting
      }
      else
      {
        Debug.LogWarning("GraphicRaycaster missing on Canvas. Adding it now.");
        canvas.gameObject.AddComponent<GraphicRaycaster>();
      }
      Debug.Log($"Canvas event camera set to {xrCamera.name} for client {base.OwnerId}");
    }
    else
    {
      Debug.LogError("XR Camera or Canvas is null. Check MainCamera tag and Canvas setup.");
    }
  }
}
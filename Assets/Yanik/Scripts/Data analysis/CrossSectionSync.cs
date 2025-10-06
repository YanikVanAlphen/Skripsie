using FishNet.Object;
using UnityEngine;
using UnityVolumeRendering;
using System.Collections;

public class CrossSectionSync : NetworkBehaviour
{
  private CrossSectionPlane crossSectionPlane;
  [SerializeField] private VolumeRenderedObject volumeObject;
  private Vector3 lastVolumeScale; // last known volumerenderobject scale

  private void Awake()
  {
    crossSectionPlane = GetComponent<CrossSectionPlane>();
  }

  private void Start()
  {
    if (volumeObject != null)
    {
      lastVolumeScale = volumeObject.transform.localScale;
      UpdatePlaneScale();
      StartCoroutine(CheckVolumeScale());
    }
  }

  public override void OnStartClient()
  {
    base.OnStartClient();
    crossSectionPlane = GetComponent<CrossSectionPlane>();
    StartCoroutine(ConfigureLocalVolume());
  }

  private IEnumerator ConfigureLocalVolume()
  {
    int retryCount = 0;
    const int maxRetries = 50;
    while (volumeObject == null || volumeObject.dataset == null)
    {
      volumeObject = FindObjectOfType<VolumeRenderedObject>();
      if (volumeObject == null)
      {
        if (retryCount >= maxRetries)
        {
          yield break;
        }
        retryCount++;
        yield return new WaitForSeconds(0.2f);
        continue;
      }

      if (volumeObject.dataset == null)
      {
        Debug.Log($"CrossSectionSync: VolumeRenderedObject found but dataset not ready, waiting...");
        yield return new WaitForSeconds(0.5f);
      }
    }

    lastVolumeScale = volumeObject.transform.localScale;
    UpdatePlaneScale();
    StartCoroutine(CheckVolumeScale());
  }

  private IEnumerator CheckVolumeScale()
  {
    while (true)
    {
      if (IsOwner && volumeObject != null && volumeObject.transform.localScale != lastVolumeScale)
      {
        lastVolumeScale = volumeObject.transform.localScale;
        UpdatePlaneScale();
        // sync to clients
        UpdatePlaneTransformServerRpc(transform.position, transform.rotation, transform.localScale);
      }
      yield return new WaitForSeconds(0.1f);
    }
  }

  // update the plane scale to match volumeObject scale
  private void UpdatePlaneScale()
  {
    if (volumeObject == null)
    {
      Debug.LogWarning("Cannot update plane scale: VolumeRenderedObject is not set.");
      return;
    }

    transform.localScale = volumeObject.transform.localScale * 1.5f;
    Debug.Log($"Updated CrossSectionPlane scale to match VolumeRenderedObject: Scale={transform.localScale}");
  }

  [ServerRpc(RequireOwnership = false)]
  public void UpdatePlaneTransformServerRpc(Vector3 position, Quaternion rotation, Vector3 scale)
  {
    if (crossSectionPlane != null)
    {
      // apply new params
      transform.position = position;
      transform.rotation = rotation;
      transform.localScale = scale;
      // let clients know to update their copies
      RpcUpdatePlaneTransform(position, rotation, scale);
      Debug.Log($"Server updated CrossSectionPlane transform: Position={position}, Rotation={rotation}, Scale={scale}");
    }
  }

  [ObserversRpc]
  private void RpcUpdatePlaneTransform(Vector3 position, Quaternion rotation, Vector3 scale)
  {
    if (crossSectionPlane != null)
    {
      transform.position = position;
      transform.rotation = rotation;
      transform.localScale = scale;
      Debug.Log($"Client updated CrossSectionPlane transform: Position={position}, Rotation={rotation}, Scale={scale}");
    }
  }

  [ServerRpc(RequireOwnership = false)]
  public void TogglePlaneEnabledServerRpc(bool enabled)
  {
    if (crossSectionPlane != null)
    {
      crossSectionPlane.enabled = enabled;
      RpcTogglePlaneEnabled(enabled);
      Debug.Log($"Server toggled CrossSectionPlane to status: {enabled}.");
    }
  }

  [ObserversRpc]
  private void RpcTogglePlaneEnabled(bool enabled)
  {
    if (crossSectionPlane != null)
    {
      crossSectionPlane.enabled = enabled;
      Debug.Log($"Client toggled CrossSectionPlane to status: {enabled}");
    }
  }

  public void SetVolumeObject(VolumeRenderedObject volume)
  {
    volumeObject = volume;
    if (volumeObject != null)
    {
      lastVolumeScale = volumeObject.transform.localScale;
      UpdatePlaneScale();
    }
  }
}

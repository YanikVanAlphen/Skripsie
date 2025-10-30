using FishNet.Object;
using UnityEngine;
using UnityVolumeRendering;
using System.Collections;

public class CrossSectionSync : NetworkBehaviour
{
  [SerializeField] private VolumeRenderedObject volumeObject;
  private CrossSectionPlane crossSectionPlane;
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

  public override void OnStartClient() // from Fish-Networking, runs when client instance starts for late joiners
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
        yield return new WaitForSeconds(0.5f);
      }
    }
    crossSectionPlane.SetTargetObject(volumeObject);
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
      return;
    // scale cross section to be 1.5 times the size of volumetric data
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
      Debug.Log($"Server updated CrossSectionPlane: Position={position}, Rotation={rotation}, Scale={scale}");
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
      Debug.Log($"Client updated CrossSectionPlane: Position={position}, Rotation={rotation}, Scale={scale}");
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
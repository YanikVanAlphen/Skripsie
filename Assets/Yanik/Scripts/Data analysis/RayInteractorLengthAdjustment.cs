using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using FishNet.Object;
using UnityVolumeRendering;
using System.Collections;

public class RayInteractorLengthAdjustment : NetworkBehaviour
{
  [SerializeField] private XRBaseInteractor rayInteractor;
  [SerializeField] private VolumeDataNetworker volumeDataNetworker;
  private VolumeRenderedObject volumeObject;
  private VolumeRaycaster raycaster;
  [SerializeField] private float defaultRayLength = 5f;
  [SerializeField] private float rayUpdateIntervalTime = 0.1f;

  private void Awake()
  {
    raycaster = new VolumeRaycaster();
    if (rayInteractor == null)
      rayInteractor = GetComponent<XRBaseInteractor>();
  }

  private void Start()
  {
    if (volumeDataNetworker == null)
      volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();

    StartCoroutine(InitVolumeObject());
    StartCoroutine(UpdateRayLength());
  }

  private IEnumerator InitVolumeObject()
  {
    // wait for VolumeDataNetworker to be assigned
    while (volumeDataNetworker == null || volumeDataNetworker.volumeRenderedObjectPrefab == null)
      yield return new WaitForSeconds(0.2f);

    NetworkObject networkObject = volumeDataNetworker.volumeRenderedObjectPrefab.GetComponent<NetworkObject>();

    var result = VolumeRenderObjectFindUtility.FindVolumeObject("RayInteractorLengthAdjustment", networkObject, 0.2f, 50);
    yield return result;
    volumeObject = result.Current as VolumeRenderedObject;
  }

  private IEnumerator UpdateRayLength()
  {
    while (true)
    {
      // check if NetworkObject has been initialised before accessing IsOwner
      if (!IsNetworkObjectInit() || volumeObject == null || rayInteractor == null)
      {
        yield return new WaitForSeconds(rayUpdateIntervalTime);
        continue;
      }

      if (!IsOwner || volumeObject == null || rayInteractor == null)
      {
        yield return new WaitForSeconds(rayUpdateIntervalTime);
        continue;
      }

      // get ray from the interactor's transform
      Vector3 rayOrigin = rayInteractor.transform.position;
      Vector3 rayDirection = rayInteractor.transform.forward;
      Ray ray = new Ray(rayOrigin, rayDirection);

      float newDistance = defaultRayLength;
      // raycast using UnityVolumeRendering's VolumeRaycaster
      if (raycaster.RaycastScene(ray, out UnityVolumeRendering.RaycastHit hit) && hit.volumeObject == volumeObject)
      {
        // set ray's length equal to hit distance
        if (rayInteractor is XRRayInteractor xrRayInteractor)
        {
          newDistance = hit.distance;
          xrRayInteractor.maxRaycastDistance = hit.distance;
        }
      }
      else
      {
        if (rayInteractor is XRRayInteractor xrRayInteractor)
        {
          // reset to default length
          xrRayInteractor.maxRaycastDistance = defaultRayLength;
        }
      }
      UpdateRayLengthRpc(newDistance);

      yield return new WaitForSeconds(rayUpdateIntervalTime);
    }
  }

  [ObserversRpc]
  private void UpdateRayLengthRpc(float distance)
  {
    if (IsOwner) return; // only do for other clients
    if (rayInteractor is XRRayInteractor xrRayInteractor)
    {
      xrRayInteractor.maxRaycastDistance = distance;
    }
  }

  private bool IsNetworkObjectInit()
  {
    var networkObject = GetComponent<NetworkObject>();
    return networkObject != null && networkObject.IsSpawned;
  }

  public void SetVolumeDataNetworker(VolumeDataNetworker networker)
  {
    volumeDataNetworker = networker;
  }

  public void SetVolumeObject(VolumeRenderedObject volume)
  {
    volumeObject = volume;
  }
}
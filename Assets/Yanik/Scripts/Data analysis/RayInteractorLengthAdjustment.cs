using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using FishNet.Object;
using UnityVolumeRendering;
using System.Collections;

public class RayInteractorLengthAdjustment : NetworkBehaviour
{
  [SerializeField] private float defaultRayLength = 2f;
  [SerializeField] private float rayUpdateIntervalTime = 0.1f;

  private VolumeRenderedObject volumeObject;
  private VolumeRaycaster raycaster = new VolumeRaycaster();
  private LineRenderer rayLineRenderer;
  private bool isRayActive = false;

  private void Awake()
  {
    rayLineRenderer = GetComponent<LineRenderer>();
    rayLineRenderer.positionCount = 2;
    gameObject.SetActive(false);
  }

  public override void OnStartClient()
  {
    base.OnStartClient();
    StartCoroutine(InitVolumeObject());
    StartCoroutine(UpdateRayLength());
  }

  private IEnumerator InitVolumeObject()
  {
    VolumeDataNetworker volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();
    // wait for VolumeDataNetworker to be assigned
    while (volumeDataNetworker == null || volumeDataNetworker.volumeRenderedObjectPrefab == null)
    {
      volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();
      yield return new WaitForSeconds(0.2f);
    }

    NetworkObject networkObject = volumeDataNetworker.volumeRenderedObjectPrefab.GetComponent<NetworkObject>();

    var result = VolumeRenderObjectFindUtility.FindVolumeObject("RayInteractorLengthAdjustment", networkObject, 0.2f, 50);
    yield return result;
    volumeObject = result.Current as VolumeRenderedObject;
  }

  private IEnumerator UpdateRayLength()
  {
    XRBaseInteractor rayInteractor = FindObjectOfType<XRBaseInteractor>(); // find right hand controller interactor
    while (true)
    {
      // check if NetworkObject has been initialised before accessing IsOwner
      if (volumeObject == null || rayInteractor == null)
      {
        yield return new WaitForSeconds(rayUpdateIntervalTime);
        continue;
      }

      // get ray from the interactor's transform
      Vector3 rayOrigin = rayInteractor.transform.position;
      Vector3 rayDirection = rayInteractor.transform.forward;
      Ray ray = new Ray(rayOrigin, rayDirection);
      float newDistance = defaultRayLength;

      if (isRayActive && raycaster.RaycastScene(ray, out UnityVolumeRendering.RaycastHit hit) && hit.volumeObject == volumeObject)
      {
        newDistance = hit.distance;
      }

      if (rayInteractor is XRRayInteractor xrRayInteractor)
      {
        xrRayInteractor.maxRaycastDistance = newDistance;
      }
      UpdateRayStateRpc(newDistance);

      yield return new WaitForSeconds(rayUpdateIntervalTime);
    }
  }

  [ObserversRpc]
  private void UpdateRayStateRpc(float distance)
  {
    if (IsOwner)
      return;

    XRBaseInteractor rayInteractor = FindObjectOfType<XRBaseInteractor>();
    if (rayInteractor is XRRayInteractor xrRayInteractor)
      xrRayInteractor.maxRaycastDistance = distance;
  }

  [ServerRpc(RequireOwnership = true)]
  public void ToggleRayActiveServerRpc(bool active)
  {
    isRayActive = active;
    gameObject.SetActive(active);
  }

  public void SetVolumeObject(VolumeRenderedObject volume)
  {
    volumeObject = volume;
  }
}
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using FishNet.Object;
using UnityVolumeRendering;
using System.Collections;

public class RayInteractorLengthAdjustment : NetworkBehaviour
{
  [SerializeField] private XRBaseInteractor rayInteractor;
  [SerializeField] private VolumeDataNetworker volumeDataNetworker;
  [SerializeField] private GameObject rayPrefab;
  [SerializeField] private float defaultRayLength = 2f;
  [SerializeField] private float rayUpdateIntervalTime = 0.1f;

  private VolumeRenderedObject volumeObject;
  private VolumeRaycaster raycaster;
  private GameObject rayInstance;
  private LineRenderer rayLineRenderer;
  private bool isRayActive = false;

  private void Awake()
  {
    raycaster = new VolumeRaycaster();
    if (rayInteractor == null)
      rayInteractor = GetComponent<XRBaseInteractor>();
  }

  public override void OnStartServer()
  {
    base.OnStartServer();
    SpawnRayPrefab();
  }

  public override void OnStartClient()
  {
    base.OnStartClient();
    StartCoroutine(InitVolumeObject());
    if (!IsServer)
    {
      StartCoroutine(FindRayInstance());
    }
  }

  private void SpawnRayPrefab()
  {
    if (!IsServer)
      return;

    rayInstance = Instantiate(rayPrefab);
    rayLineRenderer = rayInstance.GetComponent<LineRenderer>();
    // define start and end points
    rayLineRenderer.positionCount = 2;
    rayInstance.SetActive(false);
    // spawn pointer ray
    ServerManager.Spawn(rayInstance, Owner); // spawn with client ownership
  }

  private IEnumerator FindRayInstance()
  {
    int expectedPrefabId = rayPrefab.GetComponent<NetworkObject>().PrefabId;
    int retryCount = 0;
    const int maxRetries = 50;

    while (rayInstance == null && retryCount < maxRetries)
    {
      NetworkObject[] networkObjectArray = FindObjectsOfType<NetworkObject>();
      foreach (NetworkObject networkObject in networkObjectArray)
      {
        if (networkObject.PrefabId == expectedPrefabId && networkObject.Owner == NetworkManager.ClientManager.Connection)
        {
          rayInstance = networkObject.gameObject;
          rayLineRenderer = rayInstance.GetComponent<LineRenderer>();
          if (rayLineRenderer != null)
            break;
        }
      }
      if (rayInstance == null)
      {
        retryCount++;
        yield return new WaitForSeconds(0.2f);
      }
    }
    if (rayInstance == null || rayLineRenderer == null)
    {
      Debug.LogError("Client couldnt find ray instance");
      yield break;
    }
  }

  private IEnumerator InitVolumeObject()
  {
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
    while (true)
    {
      // check if NetworkObject has been initialised before accessing IsOwner
      if (!IsNetworkObjectInit() || volumeObject == null || rayInteractor == null || rayLineRenderer == null)
      {
        yield return new WaitForSeconds(rayUpdateIntervalTime);
        continue;
      }

      if (!IsOwner)
      {
        yield return new WaitForSeconds(rayUpdateIntervalTime);
        continue;
      }

      // get ray from the interactor's transform
      Vector3 rayOrigin = rayInteractor.transform.position;
      Vector3 rayDirection = rayInteractor.transform.forward;
      Ray ray = new Ray(rayOrigin, rayDirection);
      float newDistance = defaultRayLength;
      Vector3 endPoint = ray.origin + rayDirection * defaultRayLength;

      if (isRayActive && raycaster.RaycastScene(ray, out UnityVolumeRendering.RaycastHit hit) && hit.volumeObject == volumeObject)
      {
        newDistance = hit.distance;
        endPoint = rayOrigin + rayDirection * hit.distance;
      }

      if (rayInteractor is XRRayInteractor xrRayInteractor)
      {
        xrRayInteractor.maxRaycastDistance = newDistance;
      }

      if (isRayActive)
        UpdateRayStateRpc(true, rayOrigin, endPoint);
      else
        UpdateRayStateRpc(false, rayOrigin, endPoint);

      yield return new WaitForSeconds(rayUpdateIntervalTime);
    }
  }

  [ObserversRpc]
  private void UpdateRayStateRpc(bool active, Vector3 start, Vector3 end)
  {
    if (rayInstance == null || rayLineRenderer == null)
      return;

    rayInstance.SetActive(active);
    if (active)
    {
      rayLineRenderer.SetPosition(0, start);
      rayLineRenderer.SetPosition(1, end);
    }
  }

  [ServerRpc(RequireOwnership = false)]
  public void ToggleRayActiveServerRpc(bool active)
  {
    isRayActive = active;
    Vector3 start = rayInteractor.transform.position;
    Vector3 end = rayInteractor.transform.position + rayInteractor.transform.forward * defaultRayLength;

    UpdateRayStateRpc(isRayActive, start, end);
  }

  public bool IsNetworkObjectInit()
  {
    var networkObject = GetComponent<NetworkObject>();
    return networkObject != null && networkObject.IsSpawned;
  }

  private void Start()
  {
    if (volumeDataNetworker == null)
      volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();

    StartCoroutine(InitVolumeObject());
    StartCoroutine(UpdateRayLength());
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
using FishNet.Object;
using FishNet.Connection;
using FishNet.Component.Transforming;
using FishNet.Managing.Server;
using FishNet.Managing.Object;
using FishNet.Managing;
using UnityEngine;
using UnityVolumeRendering;
using System.Collections;
using System.Linq;
using FishNet.Object.Synchronizing;
using System.Collections.Generic;
using uMuVR;

public class CrossSectionManager : NetworkBehaviour
{
  [SerializeField] private GameObject crossSectionPlanePrefab;
  private VolumeDataNetworker volumeDataNetworker;
  private VolumeRenderedObject volumeObject;
  [SyncVar] private bool isCrossSectionSpawned = false; // sync the cross section spawn state in case a client joins after it has been spawned
  [SyncVar] private NetworkObject crossSectionNetObj;

  public override void OnStartClient()
  {
    base.OnStartClient();
    StartCoroutine(InitManager());
  }

  private IEnumerator InitManager()
  {
    // find VolumeDataNetworker
    while (volumeDataNetworker == null)
    {
      volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();
      yield return new WaitForSeconds(0.1f);
    }

    // find VolumeRenderedObject
    yield return StartCoroutine(FindVolumeObject());
  }

  private IEnumerator FindVolumeObject()
  {
    // wait for NetworkManager
    while (NetworkManager == null || (IsServer && NetworkManager.ServerManager == null) || (!IsServer && NetworkManager.ClientManager == null) || volumeDataNetworker == null)
    {
      yield return new WaitForSeconds(0.1f);
    }

    NetworkObject networkObject = volumeDataNetworker.volumeRenderedObjectPrefab.GetComponent<NetworkObject>();
    var result = VolumeRenderObjectFindUtility.FindVolumeObject("CrossSectionManager", networkObject);
    yield return result;
    volumeObject = result.Current as VolumeRenderedObject;
    if (volumeObject != null)
      Debug.Log($"CrossSectionManager: Found VolumeRenderedObject");
  }

  [ServerRpc(RequireOwnership = false)]
  public void SpawnCrossSectionPlaneServerRpc(Vector3 position, Quaternion rotation, NetworkConnection requester = null)
  {
    if (!IsServer || isCrossSectionSpawned || volumeObject == null || volumeObject.dataset == null)
      return;

    GameObject plane = Instantiate(crossSectionPlanePrefab, position, rotation);
    // set stored NetworkObject so we dont have to search again later
    crossSectionNetObj = plane.GetComponent<NetworkObject>();
    // Configure plane components
    var planeComponent = plane.GetComponent<CrossSectionPlane>();
    if (planeComponent != null)
      planeComponent.SetTargetObject(volumeObject);

    var syncComponent = plane.GetComponent<CrossSectionSync>();
    if (syncComponent != null)
      syncComponent.SetVolumeObject(volumeObject);

    var ownershipManager = plane.GetComponent<OwnershipManager>();

    ServerManager.Spawn(plane);

    if (requester != null)
    {
      crossSectionNetObj.GiveOwnership(requester);
      Debug.Log($"Gave initial ownership of CrossSectionPlane to Client: {requester.ClientId}");
    }

    isCrossSectionSpawned = true;
    Debug.Log($"Spawned CrossSectionPlane (ObjectId={crossSectionNetObj.ObjectId})");
  }
}
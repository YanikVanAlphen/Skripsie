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

public class CrossSectionManager : NetworkBehaviour
{
  [SerializeField] private GameObject crossSectionPlanePrefab;
  [SerializeField] private GameObject volumeRenderedObjectPrefab;
  private VolumeDataNetworker volumeDataNetworker;
  private NetworkObject crossSectionNetObj; // store cross section's NetworkObject

  private VolumeRenderedObject volumeObject;
  [SyncVar] private bool isCrossSectionSpawned = false;

  private IEnumerator WaitForNetworkReady()
  {
    int retryCount = 0;
    const int maxRetries = 40;
    while (volumeDataNetworker == null && retryCount < maxRetries)
    {
      volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();
      if (volumeDataNetworker == null)
      {
        retryCount++;
        yield return new WaitForSeconds(0.1f);
      }
    }

    if (volumeDataNetworker == null)
    {
      Debug.LogError("VolumeDataNetworker not found in scene.");
      yield break;
    }
    Debug.Log("Found VolumeDataNetworker.");
    StartCoroutine(FindVolumeObject());
  }

  private IEnumerator FindVolumeObject()
  {
    while (NetworkManager == null || NetworkManager.ServerManager == null || volumeDataNetworker == null)
    {
      // wait for NetworkManager and ServerManager init
      yield return new WaitForSeconds(0.1f);
    }

    int retryCount = 0;
    const int maxRetries = 80;
    NetworkObject volumeNetworkObject = null;

    int expectedPrefabId = -1;
    if (volumeDataNetworker != null && volumeDataNetworker.volumeRenderedObjectPrefab != null)
    {
      NetworkObject prefabNetworkObject = volumeDataNetworker.volumeRenderedObjectPrefab.GetComponent<NetworkObject>();
      if (prefabNetworkObject != null)
      {
        expectedPrefabId = prefabNetworkObject.PrefabId; // use prefab of volumeRenderedObject to evaluate against spawned networkobjects
      }
    }

    while (volumeNetworkObject == null && retryCount < maxRetries)
    {
      // search through spawned networked objects for VolumeRenderedObject
      foreach (NetworkObject obj in NetworkManager.ServerManager.Objects.Spawned.Values)
      {
        if (obj.PrefabId == expectedPrefabId && obj.GetComponent<VolumeRenderedObject>() != null)
        {
          volumeNetworkObject = obj;
          break;
        }
      }

      if (volumeNetworkObject == null)
      {
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }

    if (volumeNetworkObject == null)
    {
      Debug.LogError("Failed to find networked VolumeRenderedObject.");
      yield break;
    }

    volumeObject = volumeNetworkObject.GetComponent<VolumeRenderedObject>();
    Debug.Log($"Found VolumeRenderedObject (ObjectId={volumeNetworkObject.ObjectId}).");
  }

  public override void OnStartClient() // explicitly let clients search for cutting plane after joining
  {
    base.OnStartClient(); // ensure default FishNet inits complete
    StartCoroutine(WaitForNetworkReady());
    if (!IsServer && isCrossSectionSpawned && crossSectionNetObj != null)
    {
      StartCoroutine(FindAndConfigureCrossSectionPlane(crossSectionNetObj.ObjectId, crossSectionNetObj.transform.position, crossSectionNetObj.transform.rotation));
    }
  }

  // spawn the cross-section plane
  [ServerRpc(RequireOwnership = false)]
  public void SpawnCrossSectionPlaneServerRpc(Vector3 position, Quaternion rotation, NetworkConnection conn = null)
  {
    if (!IsServer) // only server may spawn the cross section - clients make request to spawn it
    {
      return;
    }

    if (isCrossSectionSpawned)
    {
      return; // already spawned
    }

    if (volumeObject == null)
    {
      return;
    }

    // instantiate and set up the plane
    GameObject crossSectionPlane = Instantiate(crossSectionPlanePrefab, position, rotation);
    crossSectionNetObj = crossSectionPlane.GetComponent<NetworkObject>();
    // CrossSectionPlane config
    CrossSectionPlane planeComponent = crossSectionPlane.GetComponent<CrossSectionPlane>();
    if (planeComponent != null)
    {
      planeComponent.SetTargetObject(volumeObject); // set target object to volumeobject so that plugin's cross section plane works
    }
    // set volume reference in CrossSectionSync
    CrossSectionSync syncComponent = crossSectionPlane.GetComponent<CrossSectionSync>();
    if (syncComponent != null)
    {
      syncComponent.SetVolumeObject(volumeObject);
    }

    // spawn CrossSectionPlane
    ServerManager.Spawn(crossSectionPlane);
    isCrossSectionSpawned = true;
    Debug.Log($"Server spawned CrossSectionPlane, ObjectId={crossSectionNetObj.ObjectId}, PrefabId={crossSectionNetObj.PrefabId}");

    // notify all clients
    RpcSetCrossSectionPlane(crossSectionNetObj.ObjectId, position, rotation);
  }

  // configure clients RPC
  [ObserversRpc]
  private void RpcSetCrossSectionPlane(int objectId, Vector3 position, Quaternion rotation)
  {
    if (!IsServer)
    {
      StartCoroutine(FindAndConfigureCrossSectionPlane(objectId, position, rotation));
    }
  }

  // find and configure the plane on clients
  private IEnumerator FindAndConfigureCrossSectionPlane(int objectId, Vector3 position, Quaternion rotation)
  {
    while (volumeObject == null)
    {
      // wait for VolumeRenderedObject before config of CrossSectionPlane
      yield return new WaitForSeconds(0.5f);
    }

    NetworkObject networkObject = null;
    int retryCount = 0;
    const int maxRetries = 80;

    while (networkObject == null && retryCount < maxRetries)
    {
      // use ObjectId to find cross section plane in spawned objects
      if (NetworkManager.ServerManager.Objects.Spawned.TryGetValue(objectId, out NetworkObject foundObject))
      {
        networkObject = foundObject;
      }

      if (networkObject == null)
      {
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }

    if (networkObject == null)
    {
      Debug.LogError($"Client failed to find CrossSectionPlane with ObjectId={objectId}.");
      yield break;
    }

    // plane config
    CrossSectionPlane planeComponent = networkObject.GetComponent<CrossSectionPlane>();
    if (planeComponent != null && volumeObject != null)
    {
      planeComponent.SetTargetObject(volumeObject);
    }

    networkObject.transform.position = position;
    networkObject.transform.rotation = rotation;
    Debug.Log($"Client configured CrossSectionPlane, ObjectId={objectId}");
  }
}
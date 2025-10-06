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
  private NetworkObject crossSectionNetObj;

  public override void OnStartClient()
  {
    base.OnStartClient();
    StartCoroutine(InitializeManager());
  }

  private IEnumerator InitializeManager()
  {
    // Find VolumeDataNetworker
    while (volumeDataNetworker == null)
    {
      volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();
      yield return new WaitForSeconds(0.1f);
    }

    // Find VolumeRenderedObject
    yield return StartCoroutine(FindVolumeObject());
  }

  private IEnumerator FindVolumeObject()
  {
    // Wait for NetworkManager
    while (NetworkManager == null || (IsServer && NetworkManager.ServerManager == null) || (!IsServer && NetworkManager.ClientManager == null) || volumeDataNetworker == null)
    {
      yield return new WaitForSeconds(0.1f);
    }

    int expectedPrefabId = -1;
    if (volumeDataNetworker.volumeRenderedObjectPrefab != null)
    {
      NetworkObject prefabNetObj = volumeDataNetworker.volumeRenderedObjectPrefab.GetComponent<NetworkObject>();
      if (prefabNetObj != null)
        expectedPrefabId = prefabNetObj.PrefabId;
    }

    NetworkObject volumeNetworkObject = null;
    int retryCount = 0;
    Dictionary<int, NetworkObject> spawnedObjects;
    while (volumeNetworkObject == null && retryCount < 80)
    {
      if (IsServer)
      {
        spawnedObjects = NetworkManager.ServerManager.Objects.Spawned; // spawned objects from server perspective - what has been spawned
      }
      else
      {
        spawnedObjects = NetworkManager.ClientManager.Objects.Spawned; // spawned obects from client perspective - what has been networked to me
      }
      // search through spawned networked objects for VolumeRenderedObject
      foreach (NetworkObject obj in spawnedObjects.Values)
      {
        if (obj.PrefabId == expectedPrefabId)
        {
          volumeNetworkObject = obj;
          volumeObject = obj.GetComponent<VolumeRenderedObject>();
          Debug.Log($"Found VolumeRenderedObject (ObjectId={obj.ObjectId})");
          yield break;
        }
      }

      retryCount++;
      yield return new WaitForSeconds(0.5f);
    }

    Debug.LogError("Failed to find VolumeRenderedObject.");
  }

  [ServerRpc(RequireOwnership = false)]
  public void SpawnCrossSectionPlaneServerRpc(Vector3 position, Quaternion rotation, NetworkConnection requester = null)
  {
    if (!IsServer || isCrossSectionSpawned || volumeObject == null)
    {
      return;
    }

    GameObject plane = Instantiate(crossSectionPlanePrefab, position, rotation);
    crossSectionNetObj = plane.GetComponent<NetworkObject>(); // set stored NetworkObject so we dont have to search again later
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
      Debug.Log($"Gave initial ownership of CrossSectionPlane to client {requester.ClientId}");
    }

    isCrossSectionSpawned = true;
    Debug.Log($"Server spawned CrossSectionPlane (ObjectId={crossSectionNetObj.ObjectId})");

    // Notify clients to configure
    ConfigurePlaneObserversRpc(crossSectionNetObj.ObjectId);
  }

  [ObserversRpc]
  private void ConfigurePlaneObserversRpc(int objectId)
  {
    if (!IsServer)
    {
      StartCoroutine(ConfigurePlaneOnClient(objectId));
    }
  }

  private IEnumerator ConfigurePlaneOnClient(int objectId)
  {
    // Wait for volume if not ready
    while (volumeObject == null)
    {
      yield return new WaitForSeconds(0.2f);
    }

    // Find the spawned plane
    NetworkObject planeNetObj = null;
    int retryCount = 0;
    const int maxRetries = 50;

    while (planeNetObj == null && retryCount < maxRetries)
    {
      if (NetworkManager.ClientManager.Objects.Spawned.TryGetValue(objectId, out planeNetObj))
      {
        break;
      }

      foreach (var nob in FindObjectsOfType<NetworkObject>())
      {
        if (nob.ObjectId == objectId)
        {
          planeNetObj = nob;
          break;
        }
      }
      if (planeNetObj == null)
      {
        Debug.Log($"Client waiting for CrossSectionPlane {objectId}, attempt {retryCount + 1}/{maxRetries}");
        retryCount++;
        yield return new WaitForSeconds(0.3f);
      }
    }

    if (planeNetObj != null)
    {
      var planeComponent = planeNetObj.GetComponent<CrossSectionPlane>();
      if (planeComponent != null && volumeObject != null)
      {
        planeComponent.SetTargetObject(volumeObject);
        Debug.Log($"Client configured CrossSectionPlane (ObjectId={objectId})");
      }
      else
      {
        Debug.LogError($"Missing component - Plane: {(planeComponent != null)}, Volume: {(volumeObject != null)}");
      }
    }
    else
    {
      Debug.LogError($"Client failed to find CrossSectionPlane (ObjectId={objectId}) after {maxRetries} retries");
    }
  }
}
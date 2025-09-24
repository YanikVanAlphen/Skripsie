using FishNet.Object;
using FishNet.Connection;
using FishNet.Component.Transforming; // for NetworkTransform
using UnityEngine;
using UnityVolumeRendering;
using System.Linq; // for FirstOrDefault
using FishNet.Managing.Object; // for DefaultPrefabs

public class VolumeDataNetworker : NetworkBehaviour
{
  [SerializeField] private string datasetPath = "Assets/EasyVolumeRendering/DataFiles/VisMale.raw"; // For validation
  [SerializeField] private Vector3 defaultPosition = Vector3.zero;
  [SerializeField] private Quaternion defaultRotation = Quaternion.identity;
  [SerializeField] private GameObject volumeRenderedObjectPrefab; // Assign VolumeRenderedObjectPrefab.prefab

  private VolumeRenderedObject volumeObject;

  private void Start()
  {
    if (IsServer)
    {
      // Host scans for VolumeRenderedObject and networks it
      StartCoroutine(NetworkVolumeObject());
    }
  }

  public override void OnStartClient()
  {
    base.OnStartClient();
    if (!IsServer)
    {
      // Client assigns local dataset to networked object
      StartCoroutine(AssignLocalDataset());
    }
  }

  [ServerRpc(RequireOwnership = false)]
  public void RequestLoadVolumeData(string newDatasetPath, Vector3 position, Quaternion rotation, NetworkConnection conn = null)
  {
    // Update dataset path and network object
    datasetPath = newDatasetPath;
    StartCoroutine(NetworkVolumeObject(position, rotation));
    // Send TargetRpc to each client
    foreach (NetworkConnection clientConn in NetworkManager.ClientManager.Clients.Values)
    {
      TargetAssignLocalDataset(clientConn, datasetPath, position, rotation);
    }
  }

  [TargetRpc]
  private void TargetAssignLocalDataset(NetworkConnection conn, string datasetPath, Vector3 position, Quaternion rotation)
  {
    StartCoroutine(AssignLocalDataset(datasetPath, position, rotation));
  }

  private System.Collections.IEnumerator NetworkVolumeObject(Vector3? position = null, Quaternion? rotation = null)
  {
    // Wait for VolumeRenderedObject to be created (e.g., via UI)
    while (volumeObject == null)
    {
      volumeObject = FindObjectOfType<VolumeRenderedObject>();
      if (volumeObject == null)
      {
        Debug.Log("Waiting for VolumeRenderedObject to be loaded...");
        yield return new WaitForSeconds(0.5f);
      }
    }

    // Validate dataset path
    if (!string.IsNullOrEmpty(datasetPath) && volumeObject.dataset != null)
    {
      Debug.Log($"Found VolumeRenderedObject with dataset: {volumeObject.dataset.name}");
    }

    // Instantiate the prefab on the server
    if (volumeRenderedObjectPrefab == null)
    {
      Debug.LogError("VolumeRenderedObjectPrefab is not assigned in VolumeDataNetworker!");
      yield break;
    }

    GameObject networkedObj = Instantiate(volumeRenderedObjectPrefab, position ?? defaultPosition, rotation ?? defaultRotation);
    NetworkObject networkObject = networkedObj.GetComponent<NetworkObject>();
    if (networkObject == null)
    {
      Debug.LogError("VolumeRenderedObjectPrefab missing NetworkObject component!");
      Destroy(networkedObj);
      yield break;
    }

    // Add VolumeRenderedObject component if missing
    if (networkedObj.GetComponent<VolumeRenderedObject>() == null)
    {
      networkedObj.AddComponent<VolumeRenderedObject>();
    }

    // Load dataset into the networked object
    volumeObject = networkedObj.GetComponent<VolumeRenderedObject>();
    if (volumeObject.dataset == null)
    {
      RawDatasetImporter importer = new RawDatasetImporter(datasetPath, 256, 256, 256, DataContentFormat.Uint8, Endianness.LittleEndian, 0);
      VolumeDataset dataset = importer.Import();
      if (dataset == null)
      {
        Debug.LogError($"Server failed to import dataset from {datasetPath}");
        Destroy(networkedObj);
        yield break;
      }
      volumeObject.dataset = dataset;
      volumeObject.UpdateMaterialProperties(null);
    }

    // Ensure NetworkTransform, VolumeSync, and OwnershipManager
    if (networkedObj.GetComponent<FishNet.Component.Transforming.NetworkTransform>() == null)
    {
      networkedObj.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
    }
    if (networkedObj.GetComponent<VolumeSync>() == null)
    {
      networkedObj.AddComponent<VolumeSync>();
    }
    if (networkedObj.GetComponent<uMuVR.OwnershipManager>() == null)
    {
      networkedObj.AddComponent<uMuVR.OwnershipManager>();
    }

    // Spawn on server
    ServerManager.Spawn(networkedObj);
    Debug.Log($"Server spawned VolumeRenderedObject, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Dataset={datasetPath}");
  }

  private System.Collections.IEnumerator AssignLocalDataset(string datasetPath = null, Vector3? position = null, Quaternion? rotation = null)
  {
    // Wait for server to spawn networked object
    NetworkObject networkObject = null;
    int retryCount = 0;
    const int maxRetries = 20; // Wait up to 10 seconds
    while (networkObject == null && retryCount < maxRetries)
    {
      networkObject = FindObjectsOfType<NetworkObject>().FirstOrDefault(nob => nob.GetComponent<VolumeRenderedObject>() != null);
      if (networkObject == null)
      {
        Debug.Log("Client waiting for networked VolumeRenderedObject...");
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }

    if (networkObject == null)
    {
      Debug.LogError("Client failed to find networked VolumeRenderedObject after max retries.");
      yield break;
    }

    volumeObject = networkObject.GetComponent<VolumeRenderedObject>();
    if (volumeObject == null)
    {
      Debug.LogError("NetworkObject found but missing VolumeRenderedObject component.");
      yield break;
    }

    // Load local dataset if not already loaded
    if (volumeObject.dataset == null)
    {
      RawDatasetImporter importer = new RawDatasetImporter(datasetPath ?? this.datasetPath, 256, 256, 256, DataContentFormat.Uint8, Endianness.LittleEndian, 0);
      VolumeDataset dataset = importer.Import();
      if (dataset == null)
      {
        Debug.LogError($"Client failed to import dataset from {datasetPath}");
        yield break;
      }

      volumeObject.dataset = dataset;
      volumeObject.UpdateMaterialProperties(null);
      Debug.Log($"Client assigned local dataset to networked object, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Path={datasetPath}");
    }
    else
    {
      Debug.Log($"Client used existing dataset in networked object, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}");
    }

    // Ensure position and rotation match
    volumeObject.transform.position = position ?? defaultPosition;
    volumeObject.transform.rotation = rotation ?? defaultRotation;
  }
}
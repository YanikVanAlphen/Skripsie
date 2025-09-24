using FishNet.Object;
using FishNet.Connection;
using FishNet.Component.Transforming;
using UnityEngine;
using UnityVolumeRendering;

public class VolumeDataNetworker : NetworkBehaviour
{
  [SerializeField] private string datasetPath = "Assets/DataFiles/VisMale.raw"; // For validation
  [SerializeField] private Vector3 defaultPosition = Vector3.zero;
  [SerializeField] private Quaternion defaultRotation = Quaternion.identity;

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

    // Validate dataset path (optional, for PoC)
    if (!string.IsNullOrEmpty(datasetPath) && volumeObject.dataset != null)
    {
      Debug.Log($"Found VolumeRenderedObject with dataset: {volumeObject.dataset.name}");
    }

    // Add NetworkObject and NetworkTransform if missing
    NetworkObject networkObject = volumeObject.GetComponent<NetworkObject>();
    if (networkObject == null)
    {
      networkObject = volumeObject.gameObject.AddComponent<NetworkObject>();
    }

    if (volumeObject.GetComponent<FishNet.Component.Transforming.NetworkTransform>() == null)
    {
      volumeObject.gameObject.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
    }

    // Add VolumeSync for rendering settings
    if (volumeObject.GetComponent<VolumeSync>() == null)
    {
      volumeObject.gameObject.AddComponent<VolumeSync>();
    }

    // Add OwnershipManager for uMuVR compatibility
    if (volumeObject.GetComponent<uMuVR.OwnershipManager>() == null)
    {
      volumeObject.gameObject.AddComponent<uMuVR.OwnershipManager>();
    }

    // Set position and rotation
    volumeObject.transform.position = position ?? defaultPosition;
    volumeObject.transform.rotation = rotation ?? defaultRotation;

    // Spawn on server
    ServerManager.Spawn(volumeObject.gameObject);
    Debug.Log($"Server spawned VolumeRenderedObject, ObjectId={networkObject.ObjectId}, Dataset={datasetPath}");
  }

  private System.Collections.IEnumerator AssignLocalDataset(string datasetPath = null, Vector3? position = null, Quaternion? rotation = null)
  {
    // Wait for server to spawn networked object
    NetworkObject networkObject = null;
    while (networkObject == null)
    {
      networkObject = FindObjectOfType<NetworkObject>();
      if (networkObject != null && networkObject.GetComponent<VolumeRenderedObject>() != null)
      {
        volumeObject = networkObject.GetComponent<VolumeRenderedObject>();
        break;
      }
      Debug.Log("Client waiting for networked VolumeRenderedObject...");
      yield return new WaitForSeconds(0.5f);
    }

    // Load local dataset if not already loaded
    if (volumeObject.dataset == null)
    {
      // Use RawDatasetImporter for RAW files
      RawDatasetImporter importer = new RawDatasetImporter(datasetPath ?? this.datasetPath, 256, 256, 256, DataContentFormat.Uint8, Endianness.LittleEndian, 0);
      VolumeDataset dataset = importer.Import();
      if (dataset == null)
      {
        Debug.LogError($"Client failed to import dataset from {datasetPath}");
        yield break;
      }

      volumeObject.dataset = dataset;
      volumeObject.UpdateMaterialProperties(null);
      Debug.Log($"Client assigned local dataset to networked object, ObjectId={networkObject.ObjectId}, Path={datasetPath}");
    }
    else
    {
      Debug.Log($"Client used existing dataset in networked object, ObjectId={networkObject.ObjectId}");
    }

    // Ensure position and rotation match
    volumeObject.transform.position = position ?? defaultPosition;
    volumeObject.transform.rotation = rotation ?? defaultRotation;
  }
}
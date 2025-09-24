using FishNet.Object;
using FishNet.Connection;
using FishNet.Component.Transforming; // for NetworkTransform
using UnityEngine;
using UnityVolumeRendering;
using System.Linq; // for FirstOrDefault
using FishNet.Managing.Object; // for DefaultPrefabs
using System.IO;

public class VolumeDataNetworker : NetworkBehaviour
{
  [SerializeField] private string datasetPath = "EasyVolumeRendering/DataFiles/VisMale.raw"; // Relative to Assets (Editor) or StreamingAssets (build)
  [SerializeField] private Vector3 defaultPosition = new Vector3(0f, 1f, 2f); // In front of VR camera
  [SerializeField] private Quaternion defaultRotation = Quaternion.identity;
  [SerializeField] private GameObject volumeRenderedObjectPrefab; // Assign VolumeRenderedObjectPrefab.prefab (with NetworkObject, NetworkTransform, VolumeSync, OwnershipManager)

  private VolumeRenderedObject volumeObject;

  private void Start()
  {
    if (IsServer)
    {
      // Host creates and networks VolumeRenderedObject
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
    // Validate prefab
    if (volumeRenderedObjectPrefab == null)
    {
      Debug.LogError("VolumeRenderedObjectPrefab is not assigned in VolumeDataNetworker!");
      yield break;
    }

    // Determine dataset path (Editor vs. build)
    string fullPath;
    if (Application.isEditor)
    {
      fullPath = Path.Combine(Application.dataPath, datasetPath);
    }
    else
    {
      fullPath = Path.Combine(Application.streamingAssetsPath, datasetPath);
    }

    // Validate dataset file
    if (!File.Exists(fullPath))
    {
      Debug.LogError($"Dataset file not found at {fullPath}. Ensure VisMale.raw is in Assets/StreamingAssets/EasyVolumeRendering/DataFiles/ for builds.");
      yield break;
    }

    // Check for .ini file
    string iniPath = Path.ChangeExtension(fullPath, ".ini");
    int dimX = 256, dimY = 256, dimZ = 128;
    DataContentFormat format = DataContentFormat.Uint8;
    Endianness endianness = Endianness.LittleEndian;
    int bytesToSkip = 0;

    if (File.Exists(iniPath))
    {
      try
      {
        string[] iniLines = File.ReadAllLines(iniPath);
        foreach (string line in iniLines)
        {
          if (line.StartsWith("dimX=")) dimX = int.Parse(line.Split('=')[1]);
          else if (line.StartsWith("dimY=")) dimY = int.Parse(line.Split('=')[1]);
          else if (line.StartsWith("dimZ=")) dimZ = int.Parse(line.Split('=')[1]);
          else if (line.StartsWith("format=")) format = (DataContentFormat)System.Enum.Parse(typeof(DataContentFormat), line.Split('=')[1]);
          else if (line.StartsWith("endianness=")) endianness = (Endianness)System.Enum.Parse(typeof(Endianness), line.Split('=')[1]);
          else if (line.StartsWith("skipBytes=")) bytesToSkip = int.Parse(line.Split('=')[1]);
        }
        Debug.Log($"Loaded .ini file parameters: dimX={dimX}, dimY={dimY}, dimZ={dimZ}, format={format}, endianness={endianness}, skipBytes={bytesToSkip}");
      }
      catch (System.Exception e)
      {
        Debug.LogWarning($"Failed to parse .ini file at {iniPath}: {e.Message}. Using default parameters.");
      }
    }

    // Load dataset
    RawDatasetImporter importer = new RawDatasetImporter(fullPath, dimX, dimY, dimZ, format, endianness, bytesToSkip);
    VolumeDataset dataset = importer.Import();
    if (dataset == null)
    {
      Debug.LogError($"Server failed to import dataset from {fullPath}. Check dimensions ({dimX}x{dimY}x{dimZ}), format ({format}), endianness ({endianness}), and file size (8388608 bytes).");
      yield break;
    }

    // Create VolumeRenderedObject using VolumeObjectFactory
    VolumeRenderedObject volObj = VolumeObjectFactory.CreateObject(dataset);
    GameObject volumeGameObject = volObj.gameObject;
    volumeObject = volObj;
    if (volumeObject == null)
    {
      Debug.LogError("VolumeObjectFactory failed to create VolumeRenderedObject!");
      Destroy(volumeGameObject);
      yield break;
    }

    // Set position, rotation, and scale
    volumeGameObject.transform.position = position ?? defaultPosition;
    volumeGameObject.transform.rotation = rotation ?? defaultRotation;
    volumeGameObject.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f); // Adjust scale for visibility

    // Add networking components
    NetworkObject networkObject = volumeGameObject.GetComponent<NetworkObject>();
    if (networkObject == null)
    {
      networkObject = volumeGameObject.AddComponent<NetworkObject>();
    }

    // Ensure NetworkTransform, VolumeSync, and OwnershipManager
    if (volumeGameObject.GetComponent<FishNet.Component.Transforming.NetworkTransform>() == null)
    {
      volumeGameObject.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
    }
    if (volumeGameObject.GetComponent<VolumeSync>() == null)
    {
      volumeGameObject.AddComponent<VolumeSync>();
    }
    if (volumeGameObject.GetComponent<uMuVR.OwnershipManager>() == null)
    {
      volumeGameObject.AddComponent<uMuVR.OwnershipManager>();
    }

    // Verify VolumeContainer
    Transform volumeContainer = volumeGameObject.transform.Find("VolumeContainer");
    if (volumeContainer != null)
    {
      MeshRenderer meshRenderer = volumeContainer.GetComponent<MeshRenderer>();
      if (meshRenderer != null && meshRenderer.sharedMaterial != null)
      {
        Debug.Log($"VolumeContainer found with material {meshRenderer.sharedMaterial.shader.name}");
      }
      else
      {
        Debug.LogWarning("VolumeContainer missing MeshRenderer or material!");
      }
    }
    else
    {
      Debug.LogWarning("VolumeContainer child not found in VolumeRenderedObject!");
    }

    // Set initial render settings
    /*
     * Rendering Options:
     * RenderMode.DirectVolumeRendering
     * RenderMode.MaximumIntensityProjectipon (plugin author type lol)
     * RenderMode.IsosurfaceRendering
     */
    volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
    volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f)); // Adjusted for VisMale.raw range (1-254)
    volumeObject.UpdateMaterialProperties(null);
    Debug.Log($"Server loaded dataset from {fullPath} with dimensions {dimX}x{dimY}x{dimZ}");

    // Ensure prefab is registered
    NetworkObject prefabNetworkObject = volumeRenderedObjectPrefab.GetComponent<NetworkObject>();
    if (prefabNetworkObject != null)
    {
      NetworkManager.SpawnablePrefabs.AddObject(prefabNetworkObject);
      Debug.Log("Registered VolumeRenderedObjectPrefab in SpawnablePrefabs at runtime.");
    }

    // Spawn on server
    ServerManager.Spawn(volumeGameObject);
    Debug.Log($"Server spawned VolumeRenderedObject, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Dataset={fullPath}");
  }

  private System.Collections.IEnumerator AssignLocalDataset(string datasetPath = null, Vector3? position = null, Quaternion? rotation = null)
  {
    // Wait for server to spawn networked object
    NetworkObject networkObject = null;
    int retryCount = 0;
    const int maxRetries = 60; // Wait up to 30 seconds
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
    string fullPath;
    if (Application.isEditor)
    {
      fullPath = Path.Combine(Application.dataPath, datasetPath ?? this.datasetPath);
    }
    else
    {
      fullPath = Path.Combine(Application.streamingAssetsPath, datasetPath ?? this.datasetPath);
    }

    if (!File.Exists(fullPath))
    {
      Debug.LogError($"Dataset file not found at {fullPath}. Ensure VisMale.raw is in Assets/StreamingAssets/EasyVolumeRendering/DataFiles/ for builds.");
      yield break;
    }

    // Check for .ini file
    string iniPath = Path.ChangeExtension(fullPath, ".ini");
    int dimX = 256, dimY = 256, dimZ = 128;
    DataContentFormat format = DataContentFormat.Uint8;
    Endianness endianness = Endianness.LittleEndian;
    int bytesToSkip = 0;

    if (File.Exists(iniPath))
    {
      try
      {
        string[] iniLines = File.ReadAllLines(iniPath);
        foreach (string line in iniLines)
        {
          if (line.StartsWith("dimX=")) dimX = int.Parse(line.Split('=')[1]);
          else if (line.StartsWith("dimY=")) dimY = int.Parse(line.Split('=')[1]);
          else if (line.StartsWith("dimZ=")) dimZ = int.Parse(line.Split('=')[1]);
          else if (line.StartsWith("format=")) format = (DataContentFormat)System.Enum.Parse(typeof(DataContentFormat), line.Split('=')[1]);
          else if (line.StartsWith("endianness=")) endianness = (Endianness)System.Enum.Parse(typeof(Endianness), line.Split('=')[1]);
          else if (line.StartsWith("skipBytes=")) bytesToSkip = int.Parse(line.Split('=')[1]);
        }
        Debug.Log($"Client loaded .ini file parameters: dimX={dimX}, dimY={dimY}, dimZ={dimZ}, format={format}, endianness={endianness}, skipBytes={bytesToSkip}");
      }
      catch (System.Exception e)
      {
        Debug.LogWarning($"Client failed to parse .ini file at {iniPath}: {e.Message}. Using default parameters.");
      }
    }

    if (volumeObject.dataset == null)
    {
      RawDatasetImporter importer = new RawDatasetImporter(fullPath, dimX, dimY, dimZ, format, endianness, bytesToSkip);
      VolumeDataset dataset = importer.Import();
      if (dataset == null)
      {
        Debug.LogError($"Client failed to import dataset from {fullPath}. Check dimensions ({dimX}x{dimY}x{dimZ}), format ({format}), endianness ({endianness}), and file size (8388608 bytes).");
        yield break;
      }

      volumeObject.dataset = dataset;
      volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
      volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f)); // Adjusted for VisMale.raw range (1-254)
      volumeObject.UpdateMaterialProperties(null);
      Debug.Log($"Client assigned local dataset to networked object, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Path={fullPath}");

      // Verify VolumeContainer
      Transform volumeContainer = volumeObject.transform.Find("VolumeContainer");
      if (volumeContainer != null)
      {
        MeshRenderer meshRenderer = volumeContainer.GetComponent<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.sharedMaterial != null)
        {
          Debug.Log($"Client: VolumeContainer found with material {meshRenderer.sharedMaterial.shader.name}");
        }
        else
        {
          Debug.LogWarning("Client: VolumeContainer missing MeshRenderer or material!");
        }
      }
      else
      {
        Debug.LogWarning("Client: VolumeContainer child not found in VolumeRenderedObject!");
      }
    }
    else
    {
      Debug.Log($"Client used existing dataset in networked object, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}");
    }

    // Ensure position, rotation, and scale
    volumeObject.transform.position = position ?? defaultPosition;
    volumeObject.transform.rotation = rotation ?? defaultRotation;
    volumeObject.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f); // Adjust scale for visibility
  }
}
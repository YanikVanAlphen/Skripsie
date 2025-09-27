using FishNet.Object;
using FishNet.Connection;
using FishNet.Component.Transforming;
using UnityEngine;
using UnityVolumeRendering;
using System.Linq;
using FishNet.Managing.Object;
using System.IO;
using System;

/* RENDERING OPTIONS:
     * RenderMode.DirectVolumeRendering
     * RenderMode.MaximumIntensityProjectipon (plugin author's typo)
     * RenderMode.IsosurfaceRendering
     */

/// <summary>
/// Manages the networking of a volumetric dataset by spawning a VolumeRenderedObject on the server.
/// Clients are notified to load the same dataset. Also ensures that the volume is rendered correctly with synchronised potisiton/rotation/scale.
/// Inherits from NetworkBehaviour, allowign client-server Remote Procedure Call (RPC)s and management of networked GameObjects.
/// Uses UnityVolumeRendering (https://github.com/mlavik1/UnityVolumeRendering.git) for dataset loading and rendering.
/// </summary>
/// <param</param>
/// <returns></returns>
public class VolumeDataNetworker : NetworkBehaviour
{
  [SerializeField] private string datasetPath = "EasyVolumeRendering/DataFiles/VisMale.raw"; // Relative to Assets (running from editor) or StreamingAssets folder in build version
  [SerializeField] private Vector3 defaultPosition = new Vector3(0f, 5.0f, 0f);
  [SerializeField] private Quaternion defaultRotation = Quaternion.Euler(90f, 0f, 0f);
  [SerializeField] private GameObject volumeRenderedObjectPrefab;

  private VolumeRenderedObject volumeObject;
  private bool isVolumeSpawned;
  private string DVRShaderName = "VolumeRendering/DirectVolumeRenderingShader";

  /// <summary>
  /// Called automatically by Unity on program start. 
  /// Init volume rendering process on the server when the script starts.
  /// </summary>
  /// <param</param>
  /// <returns></returns>
  private void Start()
  {
    if (IsServer)
    {
      // Host: create and network VolumeRenderedObject
      if (!isVolumeSpawned)
      {
        StartCoroutine(NetworkVolumeObject());
      }
    }
  }

  /// <summary>
  /// Handles client init, triggered when a Client joins the Host session.
  /// </summary>
  /// <param</param>
  /// <returns></returns>
  public override void OnStartClient()
  {
    base.OnStartClient();
    if (!IsServer)
    {
      // Client: assign local dataset to networked object
      RequestCurrentDatasetServerRpc(NetworkManager.ClientManager.Connection);
    }
  }

  /// <summary>
  /// Registers volumeRenderedObject prefab with the NetworkManager to allow networked spawning -- DEPRECATED
  /// </summary>
  /// <param</param>
  /// <returns></returns>
  private void RegisterPrefab()
  {
    if (volumeRenderedObjectPrefab == null)
    {
      Debug.LogError("VolumeRenderedObjectPrefab is not assigned in VolumeDataNetworker.");
      return;
    }

    NetworkObject prefabNetworkObject = volumeRenderedObjectPrefab.GetComponent<NetworkObject>();
    if (prefabNetworkObject != null)
    {
      PrefabObjects prefabObjects = NetworkManager.SpawnablePrefabs;
      bool isRegistered = false;
      for (int i = 0; i < prefabObjects.GetObjectCount(); i++)
      {
        if (prefabObjects.GetObject(true, i) == prefabNetworkObject)
        {
          isRegistered = true;
          break;
        }
      }

      if (!isRegistered)
      {
        prefabObjects.AddObject(prefabNetworkObject);
        Debug.Log($"Registered VolumeRenderedObjectPrefab with PrefabId={prefabNetworkObject.PrefabId} in NetworkManager SpawnablePrefabs.");
      }
      else
      {
        Debug.Log($"VolumeRenderedObjectPrefab with PrefabId={prefabNetworkObject.PrefabId} already registered.");
      }
    }
    else
    {
      Debug.LogError("VolumeRenderedObjectPrefab missing NetworkObject component.");
    }
  }

  /// <summary>
  /// Server RPC to request loading a new dataset + update the networked volumetric object. -- Not currently used
  /// </summary>
  /// <param name="newDatasetPath">(string): Path to the new dataset.</param>
  /// <param name="position">(Vector3): Desired position for the volume.</param>
  /// <param name="rotation">(Quaternion): Desired rotation for the volume.</param>
  /// <param name="conn">(NetworkConnection, optional): Specific client connection (null for all clients).</param>
  /// <returns></returns>
  [ServerRpc(RequireOwnership = false)]
  public void RequestLoadVolumeData(string newDatasetPath, Vector3 position, Quaternion rotation, NetworkConnection conn = null)
  {
    // Update dataset path and network object
    datasetPath = newDatasetPath;
    if (!isVolumeSpawned)
    {
      StartCoroutine(NetworkVolumeObject(position, rotation));
    }
    // Send TargetRpc to each client
    foreach (NetworkConnection clientConn in NetworkManager.ClientManager.Clients.Values)
    {
      TargetAssignLocalDataset(clientConn, datasetPath, position, rotation);
    }
  }

  /// <summary>
  /// Target RPC sent from server to a specific client to load the dataset and synchronize the volume.
  /// </summary>
  /// <param name="conn">(NetworkConnection): Target client’s connection.</param>
  /// <param name="datasetPath">(string): Path to the dataset to load.</param>
  /// <param name="position">(Vector3): Volume position.</param>
  /// <param name="rotation">(Quaternion): Volume rotation.</param>
  /// <returns></returns>
  [TargetRpc]
  private void TargetAssignLocalDataset(NetworkConnection conn, string datasetPath, Vector3 position, Quaternion rotation)
  {
    StartCoroutine(AssignLocalDataset(datasetPath, position, rotation));
  }

  /// <summary>
  /// Instantiate, configure, and spawn the VolumeRenderedObject on the server.
  /// </summary>
  /// <param name="position">(Vector3? -> nullable): Optional position. Defaults to defaultPosition.</param>
  /// <param name="rotation">(Quaternion? -> nullable): Optional rotation. Defaults to defaultRotation.</param>
  /// <returns></returns>
  private System.Collections.IEnumerator NetworkVolumeObject(Vector3? position = null, Quaternion? rotation = null)
  {
    if (isVolumeSpawned) // To prevent multiple spawns
    {
      Debug.LogWarning("Volume already spawned on server, skipping NetworkVolumeObject.");
      yield break;
    }

    // Destroy existing VolumeRenderedObjects
    foreach (var existingObj in FindObjectsOfType<VolumeRenderedObject>())
    {
      if (existingObj.gameObject != gameObject)
      {
        Debug.Log($"Destroying existing VolumeRenderedObject: {existingObj.gameObject.name}");
        ServerManager.Despawn(existingObj.gameObject);
      }
    }

    // Check prefab
    if (volumeRenderedObjectPrefab == null)
    {
      Debug.LogError("VolumeRenderedObjectPrefab is not assigned in VolumeDataNetworker!");
      yield break;
    }

    // Determine dataset path (Editor or Build version)
    string fullPath;
    if (Application.isEditor)
    {
      fullPath = Path.Combine(Application.dataPath, datasetPath);
    }
    else
    {
      fullPath = Path.Combine(Application.streamingAssetsPath, datasetPath);
    }

    // Check dataset file
    if (!File.Exists(fullPath))
    {
      Debug.LogError($"Volumetric dataset file not found at {fullPath}.");
      yield break;
    }

    // Check for .ini file (only .raw files)
    string iniPath = Path.ChangeExtension(fullPath, ".ini");
    // Default .raw params according to plugin docs
    int dimX = 128, dimY = 256, dimZ = 256;
    DataContentFormat format = DataContentFormat.Uint8;
    Endianness endianness = Endianness.LittleEndian;
    int bytesToSkip = 0;

    if (File.Exists(iniPath)) // parse contents of .ini file
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
      Debug.LogError($"Server failed to import dataset from {fullPath}. Check dimensions ({dimX}x{dimY}x{dimZ}), format ({format}) and endianness ({endianness}).");
      yield break;
    }

    // Instantiate prefab
    GameObject volumeGameObject = Instantiate(volumeRenderedObjectPrefab);
    volumeObject = volumeGameObject.GetComponent<VolumeRenderedObject>();
    if (volumeObject == null)
    {
      volumeObject = volumeGameObject.AddComponent<VolumeRenderedObject>();
    }

    // Assign dataset
    volumeObject.dataset = dataset;

    // Normalize scale (mimicking VolumeObjectFactory)
    float maxScale = Mathf.Max(dataset.scale.x, dataset.scale.y, dataset.scale.z);
    volumeGameObject.transform.localScale = Vector3.one / maxScale; // TODO: Adjust for visibility
    volumeGameObject.transform.position = position ?? defaultPosition;
    volumeGameObject.transform.rotation = rotation ?? defaultRotation;

    // Verify NetworkObject
    NetworkObject networkObject = volumeGameObject.GetComponent<NetworkObject>();
    if (networkObject == null)
    {
      Debug.LogError("Instantiated VolumeRenderedObjectPrefab missing NetworkObject.");
      Destroy(volumeGameObject);
      yield break;
    }

    // Verify VolumeContainer
    Transform volumeContainer = volumeGameObject.transform.Find("VolumeContainer");
    MeshRenderer meshRenderer = null;
    if (volumeContainer != null)
    {
      meshRenderer = volumeContainer.GetComponent<MeshRenderer>();
      if (meshRenderer != null && meshRenderer.sharedMaterial != null)
      {
        Shader volumeShader = Shader.Find(DVRShaderName);
        if (volumeShader != null && meshRenderer.sharedMaterial.shader != volumeShader)
        {
          Debug.LogWarning($"VolumeContainer material shader is {meshRenderer.sharedMaterial.shader.name}, expected {DVRShaderName}. Updating material.");
          meshRenderer.sharedMaterial = new Material(volumeShader);
        }
        Debug.Log($"VolumeContainer found with material {meshRenderer.sharedMaterial.shader.name}");
      }
      else
      {
        Debug.LogWarning("VolumeContainer missing MeshRenderer or material. Creating material.");
        meshRenderer = volumeContainer.gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
          meshRenderer = volumeContainer.gameObject.AddComponent<MeshRenderer>();
        }
        Shader volumeShader = Shader.Find(DVRShaderName);
        if (volumeShader != null)
        {
          meshRenderer.material = new Material(volumeShader);
          Debug.Log($"Applied shader {DVRShaderName}.");
        }
        else
        {
          Debug.LogWarning($"Shader {DVRShaderName} not found. Using Standard shader as fallback.");
          meshRenderer.material = new Material(Shader.Find("Standard"));
        }
      }
    }
    else
    {
      Debug.LogWarning("VolumeContainer child not found in VolumeRenderedObject. Creating manually.");
      GameObject container = new GameObject("VolumeContainer");
      container.transform.SetParent(volumeGameObject.transform, false);
      container.transform.localPosition = Vector3.zero;
      container.transform.localRotation = Quaternion.identity;
      container.transform.localScale = Vector3.one;
      MeshFilter meshFilter = container.AddComponent<MeshFilter>();
      meshFilter.mesh = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshFilter>().mesh;
      Destroy(GameObject.Find("Cube")); // Remove temporary cube
      meshRenderer = container.AddComponent<MeshRenderer>();
      Shader volumeShader = Shader.Find(DVRShaderName);
      if (volumeShader != null)
      {
        meshRenderer.material = new Material(volumeShader);
        Debug.Log($"Applied shader {DVRShaderName}.");
      }
      else
      {
        Debug.LogWarning($"Shader {DVRShaderName} not found. Using Standard shader as fallback.");
        meshRenderer.material = new Material(Shader.Find("Standard"));
      }
    }

    // Set up material properties
    if (meshRenderer != null && meshRenderer.sharedMaterial != null)
    {
      Texture3D dataTexture = dataset.GetDataTexture();
      if (dataTexture != null)
      {
        meshRenderer.sharedMaterial.SetTexture("_DataTex", dataTexture);
        Debug.Log("Assigned dataset texture to material.");
      }
      else
      {
        Debug.LogWarning("Failed to get dataset texture for VolumeContainer material.");
      }

      UnityVolumeRendering.TransferFunction tf = TransferFunctionDatabase.CreateTransferFunction();
      volumeObject.transferFunction = tf;
      Texture2D tfTexture = tf.GetTexture();
      if (tfTexture != null)
      {
        meshRenderer.sharedMaterial.SetTexture("_TFTex", tfTexture);
        Debug.Log("Assigned transfer function texture to material.");
      }
      else
      {
        Debug.LogWarning("Failed to get transfer function texture.");
      }

      // Generate noise texture
      const int noiseDimX = 512;
      const int noiseDimY = 512;
      Texture2D noiseTexture = NoiseTextureGenerator.GenerateNoiseTexture(noiseDimX, noiseDimY);
      if (noiseTexture != null)
      {
        meshRenderer.sharedMaterial.SetTexture("_NoiseTex", noiseTexture);
        Debug.Log("Assigned noise texture to material.");
      }
      else
      {
        Debug.LogWarning("Failed to generate noise texture.");
      }

      // Set shader keywords
      meshRenderer.sharedMaterial.EnableKeyword("MODE_DVR");
      meshRenderer.sharedMaterial.DisableKeyword("MODE_MIP");
      meshRenderer.sharedMaterial.DisableKeyword("MODE_SURF");

      // Assign meshRenderer to VolumeRenderedObject
      volumeObject.meshRenderer = meshRenderer;
    }

    // Set initial render settings
    volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
    volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f)); // Adjusted for VisMale.raw range (1-254)
    volumeObject.UpdateMaterialProperties();

    Debug.Log($"Server loaded dataset from {fullPath} with dimensions {dimX}x{dimY}x{dimZ}");

    // Spawn on server
    ServerManager.Spawn(volumeGameObject);
    isVolumeSpawned = true;
    Debug.Log($"Server spawned VolumeRenderedObject, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Dataset={fullPath}");
  }

  /// <summary>
  /// Load the dataset on the client (or server) and assign it to the networked VolumeRenderedObject placeholder prefab (since FishNet *needs* all clients to have the same list of SpawnablePrefabs).
  /// </summary>
  /// <param name="datasetPath">(string -> nullable): Path to the dataset Defaults to this.datasetPath if null.</param>
  /// <param name="position">(Vector3? -> nullable): Optional position. Defaults to defaultPosition if null.</param>
  /// <param name="rotation">(Quaternion? -> nullable): Optional rotation. Defaults to defaultRotation if null.</param>
  /// <returns></returns>
  private System.Collections.IEnumerator AssignLocalDataset(string datasetPath = null, Vector3? position = null, Quaternion? rotation = null)
  {
    NetworkObject networkObject = null;
    volumeObject = null;

    // Wait for server to spawn networked object
    if (!IsServer)
    {
      int retryCount = 0;
      const int maxRetries = 60; // Wait up to 60*0.5 = 30 seconds
      while (networkObject == null && retryCount < maxRetries)
      {
        NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
        int expectedPrefabId = volumeRenderedObjectPrefab.GetComponent<NetworkObject>().PrefabId;
        networkObject = networkObjects.FirstOrDefault(nob => nob.PrefabId == expectedPrefabId);
        if (networkObject == null)
        {
          Debug.Log($"Client waiting for networked VolumeRenderedObject... Attempt {retryCount + 1}/{maxRetries}. Found {networkObjects.Length} NetworkObjects. Expected PrefabId={expectedPrefabId}, datasetPath={datasetPath}");
          foreach (NetworkObject nob in networkObjects)
          {
            Debug.Log($"Client: Found NetworkObject - ObjectId={nob.ObjectId}, PrefabId={nob.PrefabId}, Name={nob.gameObject.name}, HasVolumeContainer={nob.transform.Find("VolumeContainer") != null}, HasNetworkTransform={nob.GetComponent<NetworkTransform>() != null}, HasMeshRenderer={nob.GetComponentInChildren<MeshRenderer>() != null}, HasVolumeRenderedObject={nob.GetComponent<VolumeRenderedObject>() != null}");
          }
          retryCount++;
          yield return new WaitForSeconds(0.5f);
        }
        else
        {
          volumeObject = networkObject.GetComponent<VolumeRenderedObject>();
          if (volumeObject == null)
          {
            Debug.LogWarning($"Client: VolumeRenderedObject component missing on correct NetworkObject (ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Name={networkObject.gameObject.name}). Adding component.");
            volumeObject = networkObject.gameObject.AddComponent<VolumeRenderedObject>();
          }
          Transform volumeContainer = networkObject.transform.Find("VolumeContainer");
          if (volumeContainer == null)
          {
            Debug.LogError($"Client: VolumeContainer child missing on correct NetworkObject (ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Name={networkObject.gameObject.name}). Ensure the prefab has VolumeContainer child attached on both host and client.");
            yield break;
          }
          Debug.Log($"Client found correct NetworkObject, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Name={networkObject.gameObject.name}, HasVolumeRenderedObject={volumeObject != null}, HasVolumeContainer={volumeContainer != null}, HasMeshRenderer={networkObject.GetComponentInChildren<MeshRenderer>() != null}");
        }
      }

      if (networkObject == null || volumeObject == null)
      {
        Debug.LogError($"Client failed to find or initialize networked VolumeRenderedObject after {maxRetries} retries. Expected PrefabId={volumeRenderedObjectPrefab.GetComponent<NetworkObject>().PrefabId}, datasetPath={datasetPath}. Ensure prefab is added to NetworkManager's SpawnablePrefabs in editor on both host and client with matching PrefabId (set manually to 100 in NetworkObject component if needed).");
        yield break;
      }
    }
    else
    {
      // For server, volumeObject should already be set in NetworkVolumeObject
      if (volumeObject == null)
      {
        Debug.LogError("Server: VolumeRenderedObject not set before AssignLocalDataset.");
        yield break;
      }
      networkObject = volumeObject.GetComponent<NetworkObject>();
      if (networkObject == null)
      {
        Debug.LogError("Server: VolumeRenderedObject missing NetworkObject component.");
        yield break;
      }
      Transform volumeContainer = networkObject.transform.Find("VolumeContainer");
      if (volumeContainer == null)
      {
        Debug.LogError($"Server: VolumeContainer child missing on VolumeRenderedObject (ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}). Ensure the prefab has VolumeContainer child attached.");
        yield break;
      }
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
    int dimX = 128, dimY = 256, dimZ = 256;
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
          else if (line.StartsWith("format=")) format = (DataContentFormat)Enum.Parse(typeof(DataContentFormat), line.Split('=')[1]);
          else if (line.StartsWith("endianness=")) endianness = (Endianness)Enum.Parse(typeof(Endianness), line.Split('=')[1]);
          else if (line.StartsWith("skipBytes=")) bytesToSkip = int.Parse(line.Split('=')[1]);
        }
        Debug.Log($"Client loaded .ini file parameters: dimX={dimX}, dimY={dimY}, dimZ={dimZ}, format={format}, endianness={endianness}, skipBytes={bytesToSkip}");
      }
      catch (Exception e)
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

      // Normalise scale
      float maxScale = Mathf.Max(dataset.scale.x, dataset.scale.y, dataset.scale.z);
      volumeObject.transform.localScale = Vector3.one / maxScale;

      // Verify VolumeContainer
      Transform volumeContainer = volumeObject.transform.Find("VolumeContainer");
      MeshRenderer meshRenderer = null;
      if (volumeContainer != null)
      {
        meshRenderer = volumeContainer.GetComponent<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.sharedMaterial != null)
        {
          Shader volumeShader = Shader.Find(DVRShaderName);
          if (volumeShader != null && meshRenderer.sharedMaterial.shader != volumeShader)
          {
            Debug.LogWarning($"Client: VolumeContainer material shader is {meshRenderer.sharedMaterial.shader.name}, expected {DVRShaderName}. Updating material.");
            meshRenderer.sharedMaterial = new Material(volumeShader);
          }
          Debug.Log($"Client: VolumeContainer found with material {meshRenderer.sharedMaterial.shader.name}, Material name: {meshRenderer.sharedMaterial.name}");
        }
        else
        {
          Debug.LogError($"Client: VolumeContainer missing MeshRenderer or material on NetworkObject (ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}). Ensure prefab has VolumeContainer with MeshRenderer and material set.");
          yield break;
        }
      }
      else
      {
        Debug.LogError($"Client: VolumeContainer child not found in VolumeRenderedObject (ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}). Ensure prefab has VolumeContainer child attached.");
        yield break;
      }

      // Set up material properties
      if (meshRenderer != null && meshRenderer.sharedMaterial != null)
      {
        Texture3D dataTexture = dataset.GetDataTexture();
        if (dataTexture != null)
        {
          meshRenderer.sharedMaterial.SetTexture("_DataTex", dataTexture);
          Debug.Log($"Client: Assigned dataset texture to material.");
        }
        else
        {
          Debug.LogWarning("Client: Failed to get dataset texture for VolumeContainer material.");
        }

        UnityVolumeRendering.TransferFunction tf = TransferFunctionDatabase.CreateTransferFunction();
        volumeObject.transferFunction = tf;
        Texture2D tfTexture = tf.GetTexture();
        if (tfTexture != null)
        {
          meshRenderer.sharedMaterial.SetTexture("_TFTex", tfTexture);
          Debug.Log($"Client: Assigned transfer function texture to material.");
        }
        else
        {
          Debug.LogWarning("Client: Failed to get transfer function texture.");
        }

        // Generate noise texture
        const int noiseDimX = 512;
        const int noiseDimY = 512;
        Texture2D noiseTexture = NoiseTextureGenerator.GenerateNoiseTexture(noiseDimX, noiseDimY);
        if (noiseTexture != null)
        {
          meshRenderer.sharedMaterial.SetTexture("_NoiseTex", noiseTexture);
          Debug.Log($"Client: Assigned noise texture to material.");
        }
        else
        {
          Debug.LogWarning("Client: Failed to generate noise texture.");
        }

        // Set shader keywords
        meshRenderer.sharedMaterial.EnableKeyword("MODE_DVR");
        meshRenderer.sharedMaterial.DisableKeyword("MODE_MIP");
        meshRenderer.sharedMaterial.DisableKeyword("MODE_SURF");

        // Assign meshRenderer to VolumeRenderedObject
        volumeObject.meshRenderer = meshRenderer;
      }

      volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
      volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f)); // Adjusted for VisMale.raw range (1-254)
      volumeObject.UpdateMaterialProperties();

      Debug.Log($"Client assigned local dataset to networked object, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Path={fullPath}");
    }
    else
    {
      Debug.Log($"Client used existing dataset in networked object, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}");
    }

    // Ensure position, rotation
    volumeObject.transform.position = position ?? defaultPosition;
    volumeObject.transform.rotation = rotation ?? defaultRotation;
  }

  /// <summary>
  /// Server RPC to handle a Client’s request for the current dataset and volume state.
  /// </summary>
  /// <param name="conn">(NetworkConnection): The requesting client’s connection.</param>
  /// <returns></returns>
  [ServerRpc(RequireOwnership = false)]
  private void RequestCurrentDatasetServerRpc(NetworkConnection conn)
  {
    if (isVolumeSpawned && volumeObject != null)
    {
      NetworkObject networkObject = volumeObject.GetComponent<NetworkObject>();
      if (networkObject != null)
      {
        Debug.Log($"Server: Notifying client {conn.ClientId} of current dataset: {datasetPath}, ObjectId={networkObject.ObjectId}");
        TargetAssignLocalDataset(conn, datasetPath, volumeObject.transform.position, volumeObject.transform.rotation);
      }
      else
      {
        Debug.LogWarning($"Server: VolumeRenderedObject missing NetworkObject for client {conn.ClientId}");
      }
    }
    else
    {
      Debug.Log($"Server: No volume spawned yet for client {conn.ClientId}. Spawning new volume.");
      StartCoroutine(NetworkVolumeObject());
    }
  }
}
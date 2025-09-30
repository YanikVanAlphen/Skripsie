using FishNet.Object;
using FishNet.Connection;
using FishNet.Component.Transforming;
using FishNet.Managing.Server;
using FishNet.Managing.Object;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using UnityVolumeRendering;
using System.Linq;
using System.IO;
using System;
using System.Collections.Generic;

/* RENDERING OPTIONS:
     * RenderMode.DirectVolumeRendering
     * RenderMode.MaximumIntensityProjectipon (plugin author's typo)
     * RenderMode.IsosurfaceRendering
     */

/// <summary>
/// Manages the networking of a volumetric dataset by spawning a VolumeRenderedObject on the server.
/// Clients receive the dataset from the host via serialization, without needing local files. Ensures synchronized position/rotation/scale.
/// Inherits from NetworkBehaviour, allowing client-server Remote Procedure Call (RPC)s and management of networked GameObjects.
/// Uses UnityVolumeRendering[](https://github.com/mlavik1/UnityVolumeRendering.git) for dataset loading and rendering.
/// </summary>
public class VolumeDataNetworker : NetworkBehaviour
{
  [SerializeField] private string datasetPath = "EasyVolumeRendering/DataFiles/VisMale.raw"; // Only used by host
  [SerializeField] private Vector3 defaultPosition = new Vector3(0f, 5.0f, 0f);
  [SerializeField] private Quaternion defaultRotation = Quaternion.Euler(90f, 0f, 0f);
  [SerializeField] public GameObject volumeRenderedObjectPrefab; // Public for VolumeDataControlUI
  [SerializeField] private GameObject volumeControlCanvasPrefab;

  private VolumeRenderedObject volumeObject;
  private GameObject canvasObject;
  private bool isVolumeSpawned;
  private bool isCanvasSpawned;
  private string DVRShaderName = "VolumeRendering/DirectVolumeRenderingShader";
  private static readonly Dictionary<NetworkConnection, byte[][]> chunkStorage = new Dictionary<NetworkConnection, byte[][]>();
  private byte[][] dataChunks; // Store chunks for late-joining clients

  private void Start()
  {
    if (IsServer)
    {
      ServerManager.OnServerConnectionState += OnServerConnectionState;
      Debug.Log("Registered OnServerConnectionState");
      if (!isVolumeSpawned)
      {
        StartCoroutine(NetworkVolumeObject());
      }
    }
  }

  private void OnServerConnectionState(ServerConnectionStateArgs args)
  {
    if (args.ConnectionState == LocalConnectionState.Started)
    {
      // Find most recently connected client
      NetworkConnection conn = ServerManager.Clients.Values.OrderByDescending(c => c.ClientId).FirstOrDefault();
      if (conn != null)
      {
        Debug.Log($"Client {conn.ClientId} connected. Sending dataset...");
        if (dataChunks != null && dataChunks.Length > 0)
        {
          StartCoroutine(SendDatasetToClient(conn, dataChunks));
        }
        else
        {
          Debug.LogWarning($"No dataset chunks available for client {conn.ClientId}.");
        }
      }
      else
      {
        Debug.LogWarning("No NetworkConnection found for new client.");
      }
    }
  }

  public override void OnStartClient()
  {
    base.OnStartClient();
    if (!IsServer)
    {
      RequestCurrentDatasetServerRpc(NetworkManager.ClientManager.Connection);
    }
  }

  private void RegisterPrefab()
  {
    if (volumeRenderedObjectPrefab == null)
    {
      Debug.LogError("VolumeRenderedObjectPrefab is not assigned.");
      return;
    }

    NetworkObject prefabNetworkObject = volumeRenderedObjectPrefab.GetComponent<NetworkObject>();
    if (prefabNetworkObject != null)
    {
      PrefabObjects prefabObjects = NetworkManager.SpawnablePrefabs;
      bool isRegistered = prefabObjects.GetObjectCount() > 0 && Enumerable.Range(0, prefabObjects.GetObjectCount())
          .Any(i => prefabObjects.GetObject(true, i) == prefabNetworkObject);
      if (!isRegistered)
      {
        prefabObjects.AddObject(prefabNetworkObject);
        Debug.Log($"Registered VolumeRenderedObjectPrefab with PrefabId={prefabNetworkObject.PrefabId}.");
      }
      else
      {
        Debug.Log($"VolumeRenderedObjectPrefab PrefabId={prefabNetworkObject.PrefabId} already registered.");
      }
    }
    else
    {
      Debug.LogError("VolumeRenderedObjectPrefab missing NetworkObject component.");
    }
  }

  [ServerRpc(RequireOwnership = false)]
  public void RequestLoadVolumeData(string newDatasetPath, Vector3 position, Quaternion rotation, NetworkConnection conn = null)
  {
    datasetPath = newDatasetPath;
    if (!isVolumeSpawned)
    {
      StartCoroutine(NetworkVolumeObject(position, rotation));
    }
  }

  private System.Collections.IEnumerator NetworkVolumeObject(Vector3? position = null, Quaternion? rotation = null)
  {
    if (isVolumeSpawned)
    {
      Debug.LogWarning("Volume already spawned on server.");
      yield break;
    }

    foreach (var existingObj in FindObjectsOfType<VolumeRenderedObject>())
    {
      if (existingObj.gameObject != gameObject)
      {
        Debug.Log($"Removing existing VolumeRenderedObject: {existingObj.gameObject.name}");
        ServerManager.Despawn(existingObj.gameObject);
      }
    }

    RegisterPrefab();

    if (volumeRenderedObjectPrefab == null)
    {
      Debug.LogError("VolumeRenderedObjectPrefab is not assigned in VolumeDataNetworker.");
      yield break;
    }

    string fullPath = Application.isEditor ? Path.Combine(Application.dataPath, datasetPath) : Path.Combine(Application.streamingAssetsPath, datasetPath);
    if (!File.Exists(fullPath))
    {
      Debug.LogError($"Volumetric dataset file not found: {fullPath}");
      yield break;
    }

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
        Debug.Log($"Loaded .ini file parameters: dimX={dimX}, dimY={dimY}, dimZ={dimZ}, format={format}, endianness={endianness}, skipBytes={bytesToSkip}");
      }
      catch (Exception e)
      {
        Debug.LogWarning($"Failed to parse .ini file at {iniPath}: {e.Message}. Using default parameters.");
      }
    }

    RawDatasetImporter importer = new RawDatasetImporter(fullPath, dimX, dimY, dimZ, format, endianness, bytesToSkip);
    VolumeDataset dataset = importer.Import();
    if (dataset == null)
    {
      Debug.LogError($"Server failed to import dataset from {fullPath}. Check dimensions ({dimX}x{dimY}x{dimZ}), format ({format}), endianness ({endianness}).");
      yield break;
    }

    byte[] serializedData = DatasetSerializer.Serialize(dataset);
    if (serializedData == null)
    {
      Debug.LogError("Failed to serialize dataset.");
      yield break;
    }
    dataChunks = DatasetSerializer.ChunkData(serializedData);
    if (dataChunks == null || dataChunks.Length == 0)
    {
      Debug.LogError("Failed to chunk serialized data.");
      yield break;
    }
    Debug.Log($"Server ready to send {dataChunks.Length} chunks ({serializedData.Length / 1024f / 1024f:F2} MB) to clients. Connected clients: {ServerManager.Clients.Count}");

    GameObject volumeGameObject = Instantiate(volumeRenderedObjectPrefab);
    volumeObject = volumeGameObject.GetComponent<VolumeRenderedObject>();
    if (volumeObject == null)
    {
      volumeObject = volumeGameObject.AddComponent<VolumeRenderedObject>();
      Debug.LogWarning("VolumeRenderedObject component missing on prefab. Added dynamically.");
    }

    volumeObject.dataset = dataset;
    float maxScale = Mathf.Max(dataset.scale.x, dataset.scale.y, dataset.scale.z);
    volumeGameObject.transform.localScale = Vector3.one / maxScale;
    volumeGameObject.transform.position = position ?? defaultPosition;
    volumeGameObject.transform.rotation = rotation ?? defaultRotation;

    NetworkObject networkObject = volumeGameObject.GetComponent<NetworkObject>();
    if (networkObject == null)
    {
      Debug.LogError("Instantiated VolumeRenderedObjectPrefab missing NetworkObject.");
      Destroy(volumeGameObject);
      yield break;
    }

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
      Destroy(GameObject.Find("Cube"));
      meshRenderer = container.AddComponent<MeshRenderer>();
      Shader volumeShader = Shader.Find(DVRShaderName);
      if (volumeShader != null)
      {
        meshRenderer.material = new Material(volumeShader);
        Debug.Log($"Applied {DVRShaderName} shader.");
      }
      else
      {
        Debug.LogWarning($"Shader {DVRShaderName} not found.");
        meshRenderer.material = new Material(Shader.Find("Standard"));
      }
    }

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

      meshRenderer.sharedMaterial.EnableKeyword("MODE_DVR");
      meshRenderer.sharedMaterial.DisableKeyword("MODE_MIP");
      meshRenderer.sharedMaterial.DisableKeyword("MODE_SURF");
      volumeObject.meshRenderer = meshRenderer;
    }

    volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
    volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f));
    volumeObject.UpdateMaterialProperties();

    Debug.Log($"Server loaded dataset from {fullPath} with dimensions {dimX}x{dimY}x{dimZ}");

    ServerManager.Spawn(volumeGameObject);
    isVolumeSpawned = true;
    Debug.Log($"Server spawned VolumeRenderedObject, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Dataset={fullPath}");

    if (ServerManager.Clients.Count > 0)
    {
      foreach (NetworkConnection clientConn in ServerManager.Clients.Values)
      {
        Debug.Log($"Starting transmission to client {clientConn.ClientId}");
        StartCoroutine(SendDatasetToClient(clientConn, dataChunks));
      }
    }
    else
    {
      Debug.Log("No clients connected. Dataset chunks stored for future connections.");
    }

    yield return new WaitForSeconds(0.1f);

    if (isCanvasSpawned)
    {
      Debug.LogWarning("Canvas already spawned on server.");
    }
    else
    {
      if (volumeControlCanvasPrefab == null)
      {
        Debug.LogError("VolumeControlCanvasPrefab not assigned in VolumeDataNetworker!");
        yield break;
      }

      canvasObject = Instantiate(volumeControlCanvasPrefab, new Vector3(0f, 1.5f, 3.2f), Quaternion.Euler(0f, 0f, 0f));
      NetworkObject canvasNetworkObject = canvasObject.GetComponent<NetworkObject>();
      if (canvasNetworkObject == null)
      {
        Debug.LogError("Instantiated VolumeControlCanvasPrefab missing NetworkObject.");
        Destroy(canvasObject);
        yield break;
      }

      VolumeDataControlUI controlUI = canvasObject.GetComponent<VolumeDataControlUI>();
      if (controlUI == null)
      {
        Debug.LogError("VolumeControlCanvasPrefab missing VolumeDataControlUI component.");
        Destroy(canvasObject);
        yield break;
      }

      controlUI.SetVolumeDataNetworker(this);
      ServerManager.Spawn(canvasObject);
      isCanvasSpawned = true;
      Debug.Log($"Server spawned VolumeControlCanvas, ObjectId={canvasNetworkObject.ObjectId}, PrefabId={canvasNetworkObject.PrefabId}");
    }
  }

  private System.Collections.IEnumerator SendDatasetToClient(NetworkConnection conn, byte[][] dataChunks)
  {
    if (dataChunks == null || dataChunks.Length == 0)
    {
      Debug.LogError($"No chunks to send to client {conn.ClientId}.");
      yield break;
    }

    for (int i = 0; i < dataChunks.Length; i++)
    {
      if (dataChunks[i] == null || dataChunks[i].Length == 0)
      {
        Debug.LogError($"Invalid chunk {i}/{dataChunks.Length} for client {conn.ClientId}.");
        continue;
      }
      Debug.Log($"Sending chunk {i}/{dataChunks.Length}, size={dataChunks[i].Length} bytes to client {conn.ClientId}");
      TargetSendDatasetChunk(conn, i, dataChunks.Length, dataChunks[i]);
      yield return new WaitForSeconds(0.1f);
    }
  }

  [TargetRpc]
  private void TargetSendDatasetChunk(NetworkConnection conn, int chunkIndex, int totalChunks, byte[] chunk)
  {
    if (!IsServer)
    {
      StartCoroutine(ReceiveDatasetChunk(chunkIndex, totalChunks, chunk));
    }
  }

  private System.Collections.IEnumerator ReceiveDatasetChunk(int chunkIndex, int totalChunks, byte[] chunk)
  {
    if (!chunkStorage.ContainsKey(NetworkManager.ClientManager.Connection))
    {
      chunkStorage[NetworkManager.ClientManager.Connection] = new byte[totalChunks][];
    }
    chunkStorage[NetworkManager.ClientManager.Connection][chunkIndex] = chunk;
    Debug.Log($"Client received chunk {chunkIndex}/{totalChunks}, size={chunk.Length} bytes");

    if (chunkStorage[NetworkManager.ClientManager.Connection].All(c => c != null))
    {
      byte[] serializedData = DatasetSerializer.CombineChunks(chunkStorage[NetworkManager.ClientManager.Connection]);
      VolumeDataset dataset = DatasetSerializer.Deserialize(serializedData);
      chunkStorage.Remove(NetworkManager.ClientManager.Connection);

      if (dataset == null)
      {
        Debug.LogError("Client failed to deserialize dataset.");
        yield break;
      }

      StartCoroutine(AssignLocalDataset(dataset, null, null));
    }
    yield return null;
  }

  private System.Collections.IEnumerator AssignLocalDataset(VolumeDataset dataset = null, Vector3? position = null, Quaternion? rotation = null)
  {
    NetworkObject networkObject = null;
    volumeObject = null;

    if (!IsServer)
    {
      int retryCount = 0;
      const int maxRetries = 60;
      while (networkObject == null && retryCount < maxRetries)
      {
        NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
        int expectedPrefabId = volumeRenderedObjectPrefab.GetComponent<NetworkObject>().PrefabId;
        networkObject = networkObjects.FirstOrDefault(nob => nob.PrefabId == expectedPrefabId);
        if (networkObject == null)
        {
          Debug.Log($"Client waiting for networked VolumeRenderedObject... Attempt {retryCount + 1}/{maxRetries}. Expected PrefabId={expectedPrefabId}");
          foreach (NetworkObject nob in networkObjects)
          {
            Debug.Log($"Client: Found NetworkObject: ObjectId={nob.ObjectId}, PrefabId={nob.PrefabId}, Name={nob.gameObject.name}, HasVolumeContainer={nob.transform.Find("VolumeContainer") != null}, HasNetworkTransform={nob.GetComponent<NetworkTransform>() != null}, HasMeshRenderer={nob.GetComponentInChildren<MeshRenderer>() != null}, HasVolumeRenderedObject={nob.GetComponent<VolumeRenderedObject>() != null}");
          }
          retryCount++;
          yield return new WaitForSeconds(0.5f);
        }
        else
        {
          volumeObject = networkObject.GetComponent<VolumeRenderedObject>();
          if (volumeObject == null)
          {
            Debug.LogWarning($"Client: VolumeRenderedObject component missing on NetworkObject (ObjectId={networkObject.ObjectId}). Adding dynamically.");
            volumeObject = networkObject.gameObject.AddComponent<VolumeRenderedObject>();
          }
          Transform volumeContainer = networkObject.transform.Find("VolumeContainer");
          if (volumeContainer == null)
          {
            Debug.LogError($"Client: VolumeContainer child missing on NetworkObject (ObjectId={networkObject.ObjectId}).");
            yield break;
          }
          Debug.Log($"Client found NetworkObject, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}");
        }
      }

      if (networkObject == null || volumeObject == null)
      {
        Debug.LogError($"Client failed to find VolumeRenderedObject after {maxRetries} retries. Expected PrefabId={volumeRenderedObjectPrefab.GetComponent<NetworkObject>().PrefabId}.");
        yield break;
      }
    }
    else
    {
      if (volumeObject == null)
      {
        Debug.LogError("Server: VolumeRenderedObject not set.");
        yield break;
      }
      networkObject = volumeObject.GetComponent<NetworkObject>();
      if (networkObject == null)
      {
        Debug.LogError("Server: VolumeRenderedObject missing NetworkObject.");
        yield break;
      }
      Transform volumeContainer = networkObject.transform.Find("VolumeContainer");
      if (volumeContainer == null)
      {
        Debug.LogError($"Server: VolumeContainer child missing on VolumeRenderedObject (ObjectId={networkObject.ObjectId}).");
        yield break;
      }
    }

    if (dataset != null && volumeObject.dataset == null)
    {
      volumeObject.dataset = dataset;
      float maxScale = Mathf.Max(dataset.scale.x, dataset.scale.y, dataset.scale.z);
      volumeObject.transform.localScale = Vector3.one / maxScale;

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
          Debug.Log($"Client: VolumeContainer found with material {meshRenderer.sharedMaterial.shader.name}");
        }
        else
        {
          Debug.LogError($"Client: VolumeContainer missing MeshRenderer or material.");
          yield break;
        }
      }
      else
      {
        Debug.LogError($"Client: VolumeContainer child not found.");
        yield break;
      }

      if (meshRenderer != null && meshRenderer.sharedMaterial != null)
      {
        Texture3D dataTexture = dataset.GetDataTexture();
        if (dataTexture != null)
        {
          meshRenderer.sharedMaterial.SetTexture("_DataTex", dataTexture);
          Debug.Log($"Client: Assigned dataset texture.");
        }
        else
        {
          Debug.LogWarning("Client: Failed to get dataset texture.");
        }

        UnityVolumeRendering.TransferFunction tf = TransferFunctionDatabase.CreateTransferFunction();
        volumeObject.transferFunction = tf;
        Texture2D tfTexture = tf.GetTexture();
        if (tfTexture != null)
        {
          meshRenderer.sharedMaterial.SetTexture("_TFTex", tfTexture);
          Debug.Log($"Client: Assigned transfer function texture.");
        }
        else
        {
          Debug.LogWarning("Client: Failed to get transfer function texture.");
        }

        const int noiseDimX = 512;
        const int noiseDimY = 512;
        Texture2D noiseTexture = NoiseTextureGenerator.GenerateNoiseTexture(noiseDimX, noiseDimY);
        if (noiseTexture != null)
        {
          meshRenderer.sharedMaterial.SetTexture("_NoiseTex", noiseTexture);
          Debug.Log($"Client: Assigned noise texture.");
        }
        else
        {
          Debug.LogWarning("Client: Failed to generate noise texture.");
        }

        meshRenderer.sharedMaterial.EnableKeyword("MODE_DVR");
        meshRenderer.sharedMaterial.DisableKeyword("MODE_MIP");
        meshRenderer.sharedMaterial.DisableKeyword("MODE_SURF");
        volumeObject.meshRenderer = meshRenderer;
      }

      volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
      volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f));
      volumeObject.UpdateMaterialProperties();

      Debug.Log($"Client assigned dataset to ObjectId={networkObject.ObjectId}");
    }

    volumeObject.transform.position = position ?? defaultPosition;
    volumeObject.transform.rotation = rotation ?? defaultRotation;
  }

  [ServerRpc(RequireOwnership = false)]
  private void RequestCurrentDatasetServerRpc(NetworkConnection conn)
  {
    if (isVolumeSpawned && volumeObject != null)
    {
      NetworkObject networkObject = volumeObject.GetComponent<NetworkObject>();
      if (networkObject != null)
      {
        if (dataChunks != null && dataChunks.Length > 0)
        {
          StartCoroutine(SendDatasetToClient(conn, dataChunks));
          Debug.Log($"Server: Sending dataset to client {conn.ClientId}, ObjectId={networkObject.ObjectId}, Chunks={dataChunks.Length}");
        }
        else
        {
          Debug.LogError($"Server: No dataset chunks available for client {conn.ClientId}. Re-serializing dataset.");
          byte[] serializedData = DatasetSerializer.Serialize(volumeObject.dataset);
          if (serializedData != null)
          {
            dataChunks = DatasetSerializer.ChunkData(serializedData);
            if (dataChunks != null && dataChunks.Length > 0)
            {
              StartCoroutine(SendDatasetToClient(conn, dataChunks));
              Debug.Log($"Server: Re-serialized and sending dataset to client {conn.ClientId}, ObjectId={networkObject.ObjectId}, Chunks={dataChunks.Length}");
            }
            else
            {
              Debug.LogError($"Server: Failed to chunk dataset for client {conn.ClientId}");
            }
          }
          else
          {
            Debug.LogError($"Server: Failed to serialize dataset for client {conn.ClientId}");
          }
        }
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
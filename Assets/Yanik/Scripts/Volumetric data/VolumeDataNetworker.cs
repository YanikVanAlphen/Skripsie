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
using System.Collections;
using VolumeData;

/// <summary>
/// Manages the networking of a volumetric dataset by spawning a VolumeRenderedObject on the server.
/// Clients receive the dataset from the host via serialization, without needing local files. Ensures synchronised position/rotation/scale.
/// </summary>
public class VolumeDataNetworker : NetworkBehaviour
{
  // private to other scripts but editable in inspector
  [SerializeField] private string datasetPath = "EasyVolumeRendering/DataFiles/VisMale.raw"; // Only used by host
  [SerializeField] private Vector3 defaultPosition = new Vector3(0f, 5.0f, 0f);
  [SerializeField] private Quaternion defaultRotation = Quaternion.Euler(90f, 0f, 0f);
  [SerializeField] public GameObject volumeRenderedObjectPrefab; // Public for VolumeDataControlUI
  [SerializeField] public GameObject volumeControlCanvasPrefab; // Public for OwnershipManager
  //[SerializeField] private DatasetType dataTypeToSpawn;

  private VolumeRenderedObject volumeObject;
  private GameObject canvasObject;
  private bool isVolumeSpawned;
  private bool isCanvasSpawned;
  private static readonly Dictionary<NetworkConnection, byte[][]> chunkStorage = new Dictionary<NetworkConnection, byte[][]>();
  private byte[][] dataChunks; // Stored chunks for late-joining clients
  private DataSetLoader datasetLoader;

  private void Start()
  {
    if (IsServer)
    {
      ServerManager.OnServerConnectionState += OnServerConnectionState; // subscribe OnServerConnectionState to trigger when server starts/stops
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
      NetworkConnection conn = null;
      int highestClientId = -1;
      foreach (NetworkConnection client in ServerManager.Clients.Values)
      {
        if (client.ClientId > highestClientId)
        {
          conn = client;
          highestClientId = client.ClientId;
        }
      }

      if (conn != null)
      {
        Debug.Log($"Client {conn.ClientId} connected.");
        if (dataChunks != null && dataChunks.Length > 0)
        {
          Debug.Log($"Sending dataset to Client {conn.ClientId}");
          StartCoroutine(SendDatasetToClient(conn, dataChunks));
        }
        else
        {
          Debug.LogWarning($"No data available to send to Client {conn.ClientId}.");
        }
      }
    }
  }

  public override void OnStartClient()
  {
    base.OnStartClient(); // ensure default FishNet functions run
    if (!IsServer)
    {
      RequestCurrentDatasetServerRpc(NetworkManager.ClientManager.Connection);
    }
  }

  private void RegisterPrefab()
  {
    if (volumeRenderedObjectPrefab == null)
      return;

    NetworkObject prefabNetworkObject = volumeRenderedObjectPrefab.GetComponent<NetworkObject>();
    if (prefabNetworkObject != null)
    {
      PrefabObjects prefabObjects = NetworkManager.SpawnablePrefabs;
      bool isRegistered = getRegistrationStatus(prefabObjects, prefabNetworkObject);

      if (!isRegistered)
      {
        prefabObjects.AddObject(prefabNetworkObject);
        Debug.Log($"Registered VolumeRenderedObjectPrefab with PrefabId={prefabNetworkObject.PrefabId}.");
      }
      else
      {
        Debug.Log($"VolumeRenderedObjectPrefab (PrefabId={prefabNetworkObject.PrefabId}) already registered.");
      }
    }
  }

  private bool getRegistrationStatus(PrefabObjects prefabObjects, NetworkObject prefabNetworkObject)
  {
    bool registrationStatus = false;
    if (prefabObjects.GetObjectCount() > 0)
    {
      for (int i = 0; i < prefabObjects.GetObjectCount(); i++)
      {
        if (prefabObjects.GetObject(true, i) == prefabNetworkObject)
        {
          registrationStatus = true;
          break;
        }
      }
    }
    return registrationStatus;
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

  private IEnumerator NetworkVolumeObject(Vector3? position = null, Quaternion? rotation = null)
  {
    if (isVolumeSpawned) // volume already spawned on server
    {
      yield break;
    }
    // despawn existing VolumeRenderedObjects - dont want multiple data instances in single scene
    foreach (var existingObj in FindObjectsOfType<VolumeRenderedObject>())
    {
      if (existingObj.gameObject != gameObject)
      {
        Debug.Log($"Removing existing VolumeRenderedObject: {existingObj.gameObject.name}");
        ServerManager.Despawn(existingObj.gameObject);
      }
    }

    RegisterPrefab();

    //if (volumeRenderedObjectPrefab == null)
    //{
    //  Debug.LogError("VolumeRenderedObjectPrefab is not assigned in VolumeDataNetworker.");
    //  yield break;
    //}

    datasetLoader = new DataSetLoader(datasetPath); // create new datasetloader
    VolumeDataset dataset = datasetLoader.LoadDataset();
    if (dataset == null)
    {
      yield break;
    }
    // serialize data for transmission
    byte[] serializedData = DatasetSerializer.Serialize(dataset);
    // compress serialized data for transmission
    dataChunks = DatasetSerializer.ChunkData(serializedData);
    Debug.Log($"Server ready to send data to clients.");
    // instantiate and configure VolumeRenderedObject
    GameObject volumeGameObject = Instantiate(volumeRenderedObjectPrefab);
    volumeObject = volumeGameObject.GetComponent<VolumeRenderedObject>();
    if (volumeObject == null)
    {
      volumeObject = volumeGameObject.AddComponent<VolumeRenderedObject>();
    }
    NetworkObject networkObject = volumeGameObject.GetComponent<NetworkObject>();
    yield return datasetLoader.ConfigureVolumeRenderingAsync(volumeObject, dataset);
    // spawn VolumeRenderedObject in the scene
    ServerManager.Spawn(volumeGameObject);
    isVolumeSpawned = true;
    Debug.Log($"Server spawned VolumeRenderedObject (ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Dataset={datasetPath})");
    // send dataset to connected clients
    if (ServerManager.Clients.Count > 0)
    {
      foreach (NetworkConnection clientConn in ServerManager.Clients.Values)
      {
        Debug.Log($"Starting transmission to client {clientConn.ClientId}");
        StartCoroutine(SendDatasetToClient(clientConn, dataChunks));
      }
    }

    yield return new WaitForSeconds(0.1f);
    // spawn canvas to control data params/analysis tools
    if (!isCanvasSpawned)
    {
      canvasObject = Instantiate(volumeControlCanvasPrefab, new Vector3(0f, 1.5f, 3.2f), Quaternion.Euler(0f, 0f, 0f));
      NetworkObject canvasNetworkObject = canvasObject.GetComponent<NetworkObject>();
      VolumeDataControlUI controlUI = canvasObject.GetComponent<VolumeDataControlUI>();
      controlUI.SetVolumeDataNetworker(this); // assign VolumeDataNetworker at runtime
      ServerManager.Spawn(canvasObject); // spawn canvas
      isCanvasSpawned = true;
      Debug.Log($"Server spawned VolumeControlCanvas, ObjectId={canvasNetworkObject.ObjectId}, PrefabId={canvasNetworkObject.PrefabId}");
    }
  }

  private IEnumerator SendDatasetToClient(NetworkConnection conn, byte[][] dataChunks)
  {
    if (dataChunks == null || dataChunks.Length == 0) // no chunks to send to client
    {
      yield break;
    }

    for (int i = 0; i < dataChunks.Length; i++)
    {
      if (dataChunks[i] == null || dataChunks[i].Length == 0)
      {
        Debug.LogError($"Invalid chunk {(i+1).ToString()}/{dataChunks.Length} for client {conn.ClientId}.");
        continue;
      }
      Debug.Log($"Sending chunk {(i+1).ToString()}/{dataChunks.Length}, size={dataChunks[i].Length} bytes to client {conn.ClientId}");
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

  private IEnumerator ReceiveDatasetChunk(int chunkIndex, int totalChunks, byte[] chunk)
  {
    if (!chunkStorage.ContainsKey(NetworkManager.ClientManager.Connection))
    {
      chunkStorage[NetworkManager.ClientManager.Connection] = new byte[totalChunks][];
    }
    chunkStorage[NetworkManager.ClientManager.Connection][chunkIndex] = chunk;
    Debug.Log($"Client received chunk {chunkIndex + 1}/{totalChunks}, size={chunk.Length} bytes");

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

  private IEnumerator AssignLocalDataset(VolumeDataset dataset = null, Vector3? position = null, Quaternion? rotation = null)
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
        int expectedPrefabId = volumeRenderedObjectPrefab.GetComponent<NetworkObject>().PrefabId; // get expected ID of VolumeRenderedObject in scene from prefab
        
        NetworkObject foundNetworkObject = null;
        foreach (NetworkObject nob in networkObjects) // find spawned VolumeRenderedObject in list of spawned NetworkObjects
        {
          if (nob.PrefabId == expectedPrefabId)
          {
            foundNetworkObject = nob;
            break;
          }
        }

        if (foundNetworkObject == null)
        {
          Debug.Log($"Client waiting for networked VolumeRenderedObject: Attempt {(retryCount+1).ToString()}/{maxRetries}. Expected PrefabId={expectedPrefabId}");
          retryCount++;
          yield return new WaitForSeconds(0.5f);
        }
        else
        {
          networkObject = foundNetworkObject;
          volumeObject = networkObject.GetComponent<VolumeRenderedObject>();
          if (volumeObject == null)
          {
            volumeObject = networkObject.gameObject.AddComponent<VolumeRenderedObject>();
          }
          Transform volumeContainer = networkObject.transform.Find("VolumeContainer");
          if (volumeContainer == null)
          {
            yield break;
          }
          Debug.Log($"Client found NetworkObject, ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}");
        }
      }
      if (networkObject == null || volumeObject == null)
      {
        Debug.LogError($"Client failed to find VolumeRenderedObject after {maxRetries} retries.");
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
      Transform volumeContainer = networkObject.transform.Find("VolumeContainer");
      if (volumeContainer == null)
      {
        Debug.LogError($"Server: VolumeContainer child missing on VolumeRenderedObject (ObjectId={networkObject.ObjectId}).");
        yield break;
      }
    }

    //if (dataset != null && volumeObject.dataset == null)
    //{
    //  volumeObject.dataset = dataset;
    //  float maxScale = Mathf.Max(dataset.scale.x, dataset.scale.y, dataset.scale.z);
    //  volumeObject.transform.localScale = Vector3.one / maxScale;

    //  Transform volumeContainer = volumeObject.transform.Find("VolumeContainer");
    //  MeshRenderer meshRenderer = null;
    //  if (volumeContainer != null)
    //  {
    //    meshRenderer = volumeContainer.GetComponent<MeshRenderer>();
    //    if (meshRenderer != null && meshRenderer.sharedMaterial != null)
    //    {
    //      Shader volumeShader = Shader.Find(DVRShaderName);
    //      if (volumeShader != null && meshRenderer.sharedMaterial.shader != volumeShader)
    //      {
    //        Debug.LogWarning($"Client: VolumeContainer material shader is {meshRenderer.sharedMaterial.shader.name}, expected {DVRShaderName}. Updating material.");
    //        meshRenderer.sharedMaterial = new Material(volumeShader);
    //      }
    //      Debug.Log($"Client: VolumeContainer found with material {meshRenderer.sharedMaterial.shader.name}");
    //    }
    //    else
    //    {
    //      Debug.LogError($"Client: VolumeContainer missing MeshRenderer or material.");
    //      yield break;
    //    }
    //  }
    //  else
    //  {
    //    Debug.LogError($"Client: VolumeContainer child not found.");
    //    yield break;
    //  }

    //  if (meshRenderer != null && meshRenderer.sharedMaterial != null)
    //  {
    //    Texture3D dataTexture = dataset.GetDataTexture();
    //    if (dataTexture != null)
    //    {
    //      meshRenderer.sharedMaterial.SetTexture("_DataTex", dataTexture);
    //      Debug.Log($"Client: Assigned dataset texture.");
    //    }
    //    else
    //    {
    //      Debug.LogWarning("Client: Failed to get dataset texture.");
    //    }

    //    UnityVolumeRendering.TransferFunction tf = TransferFunctionDatabase.CreateTransferFunction();
    //    volumeObject.transferFunction = tf;
    //    Texture2D tfTexture = tf.GetTexture();
    //    if (tfTexture != null)
    //    {
    //      meshRenderer.sharedMaterial.SetTexture("_TFTex", tfTexture);
    //      Debug.Log($"Client: Assigned transfer function texture.");
    //    }
    //    else
    //    {
    //      Debug.LogWarning("Client: Failed to get transfer function texture.");
    //    }

    //    const int noiseDimX = 512;
    //    const int noiseDimY = 512;
    //    Texture2D noiseTexture = NoiseTextureGenerator.GenerateNoiseTexture(noiseDimX, noiseDimY);
    //    if (noiseTexture != null)
    //    {
    //      meshRenderer.sharedMaterial.SetTexture("_NoiseTex", noiseTexture);
    //      Debug.Log($"Client: Assigned noise texture.");
    //    }
    //    else
    //    {
    //      Debug.LogWarning("Client: Failed to generate noise texture.");
    //    }

    //    meshRenderer.sharedMaterial.EnableKeyword("MODE_DVR");
    //    meshRenderer.sharedMaterial.DisableKeyword("MODE_MIP");
    //    meshRenderer.sharedMaterial.DisableKeyword("MODE_SURF");
    //    volumeObject.meshRenderer = meshRenderer;
    //  }

    //  volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
    //  volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f));
    //  volumeObject.UpdateMaterialProperties();

    //  Debug.Log($"Client assigned dataset to ObjectId={networkObject.ObjectId}");
    //}
    datasetLoader = new DataSetLoader(); // create new datasetloader
    yield return datasetLoader.ConfigureVolumeRenderingAsync(volumeObject, dataset);

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
          }
        }
      }
    }
    else
    {
      Debug.Log($"Server: No volume spawned yet for client {conn.ClientId}. Spawning new volume.");
      StartCoroutine(NetworkVolumeObject());
    }
  }
}
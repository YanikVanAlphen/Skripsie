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

public class VolumeDataNetworker : NetworkBehaviour
{
  // private to other scripts but editable in inspector
  [SerializeField] private string datasetPath = "EasyVolumeRendering/DataFiles/VisMale.raw"; // Only used by host: path to folder or file containing data in assets folder
  [SerializeField] private DatasetType dataType;

  [SerializeField] private Vector3 defaultPosition = new Vector3(0f, 5.0f, 0f);
  [SerializeField] private Quaternion defaultRotation = Quaternion.Euler(90f, 0f, 0f);
  [SerializeField] public GameObject volumeRenderedObjectPrefab; // public for VolumeDataControlUI
  [SerializeField] public GameObject volumeControlCanvasPrefab; // public for OwnershipManager

  private VolumeRenderedObject volumeObject;
  private GameObject canvasObject;
  private bool isVolumeSpawned;
  private bool isCanvasSpawned;
  // Stores dataset chunks for each clients NetworkConnection and used to transmit data to clients joining later.
  private static readonly Dictionary<NetworkConnection, byte[][]> chunkStorage = new Dictionary<NetworkConnection, byte[][]>();
  private byte[][] dataChunks; // Stored chunks for late joining clients
  private DataSetLoader datasetLoader;

  private void Start()
  {
    if (IsServer)
    {
      if (!isVolumeSpawned)
      {
        StartCoroutine(NetworkVolumeObject());
      }
    }
  }

  public override void OnStartClient()
  {
    // when I join as client, request the loaded dataset to be sent to me. Request sent to server to send
    base.OnStartClient();
    if (!IsServer)
      RequestCurrentDatasetServerRpc(NetworkManager.ClientManager.Connection);
  }

  [ServerRpc(RequireOwnership = false)]
  private void RequestCurrentDatasetServerRpc(NetworkConnection clientConnection)
  {
    if (isVolumeSpawned && volumeObject != null && dataChunks != null && dataChunks.Length > 0)
    {
      Debug.Log($"Sending dataset to client {clientConnection.ClientId}.");
      StartCoroutine(SendDatasetToClient(clientConnection, dataChunks));
    }
  }

  private IEnumerator NetworkVolumeObject()
  {
    if (isVolumeSpawned)
      yield break;

    // create new datasetLoader, load data in and prep for transmission
    datasetLoader = new DataSetLoader(datasetPath, dataType);
    VolumeDataset dataset = datasetLoader.LoadDataset();
    dataChunks = PrepareDataForTransmit(datasetPath, dataset);
    Debug.Log($"Volumetric data ready for transmission.");

    // instantiate and configure VolumeRenderedObject
    GameObject volumeGameObject = Instantiate(volumeRenderedObjectPrefab);
    volumeObject = volumeGameObject.GetComponent<VolumeRenderedObject>();
    if (volumeObject == null)
      volumeObject = volumeGameObject.AddComponent<VolumeRenderedObject>();

    NetworkObject networkObject = volumeGameObject.GetComponent<NetworkObject>();
    yield return datasetLoader.ConfigureVolumeRenderingAsync(volumeObject, dataset);

    // spawn VolumeRenderedObject in the scene
    ServerManager.Spawn(volumeGameObject);
    isVolumeSpawned = true;
    Debug.Log($"Server spawned VolumeRenderedObject (ObjectId={networkObject.ObjectId}, PrefabId={networkObject.PrefabId}, Dataset={datasetPath})");

    yield return new WaitForSeconds(0.1f);
    // spawn canvas to control data params/analysis tools
    SpawnDataMenu();
  }

  private byte[][] PrepareDataForTransmit(string datasetPath, VolumeDataset dataset)
  {
    // serialize + compress data for transmission
    byte[] serializedData = DatasetSerializer.Serialize(dataset);
    return DatasetSerializer.ChunkData(serializedData);
  }

  private void SpawnDataMenu()
  {
    if (!isCanvasSpawned) // make sure canvas is only spawned once
    {
      // instantiate new canvas instance and place at default position
      canvasObject = Instantiate(volumeControlCanvasPrefab, new Vector3(0f, 1.5f, 3.2f), Quaternion.Euler(0f, 0f, 0f));
      NetworkObject canvasNetworkObject = canvasObject.GetComponent<NetworkObject>();
      VolumeDataControlUI controlUI = canvasObject.GetComponent<VolumeDataControlUI>();
      // assign VolumeDataNetworker at runtime
      controlUI.SetVolumeDataNetworker(this);
      // spawn canvas
      ServerManager.Spawn(canvasObject);
      isCanvasSpawned = true;
      Debug.Log($"Server spawned VolumeControlCanvas, ObjectId={canvasNetworkObject.ObjectId}, PrefabId={canvasNetworkObject.PrefabId}.");
    }
  }

  private IEnumerator SendDatasetToClient(NetworkConnection clientConnection, byte[][] dataChunks)
  {
    if (dataChunks == null || dataChunks.Length == 0)
      yield break;// no data to send to client

    // loop through all chunks and send to client
    for (int i = 0; i < dataChunks.Length; i++)
    {
      if (dataChunks[i] == null || dataChunks[i].Length == 0)
      {
        Debug.LogError($"Invalid chunk {(i + 1).ToString()}/{dataChunks.Length} for client {clientConnection.ClientId}.");
        continue;
      }
      Debug.Log($"Sending chunk {(i + 1).ToString()}/{dataChunks.Length}, size={dataChunks[i].Length} bytes to client {clientConnection.ClientId}");
      TargetSendDatasetChunk(clientConnection, i, dataChunks.Length, dataChunks[i]);
      // send next chunk after small wait
      yield return new WaitForSeconds(0.1f);
    }
  }

  [TargetRpc] // RPC used to run logic on a specific target client. conn is the client to target/connection the data is going to
  private void TargetSendDatasetChunk(NetworkConnection conn, int chunkIndex, int totalChunks, byte[] chunk)
  {
    if (!IsServer)
    {
      StartCoroutine(ReceiveDatasetChunk(chunkIndex, totalChunks, chunk));
    }
  }

  private IEnumerator ReceiveDatasetChunk(int chunkIndex, int totalChunks, byte[] chunk)
  {
    NetworkConnection clientConnection = NetworkManager.ClientManager.Connection;
    // if the client networkconnection does not already exist as a key in the chunkstorage dictionary, make a new space for it.
    if (!chunkStorage.ContainsKey(clientConnection))
    {
      chunkStorage[clientConnection] = new byte[totalChunks][];
    }
    chunkStorage[clientConnection][chunkIndex] = chunk;
    Debug.Log($"Client received chunk {chunkIndex + 1}/{totalChunks}, size={chunk.Length} bytes");

    // get all chunks for the current client
    byte[][] clientChunks = chunkStorage[clientConnection];

    // check if all chunks have been received to decide whether data can be decompressed, deserialized and rendered yet
    bool allChunksReceived = true;
    foreach (var clientChunk in clientChunks)
    {
      if (clientChunk == null)
      {
        allChunksReceived = false;
        break;
      }
    }

    // only uncompress and deserialize chunks when the full dataset has been received by client
    if (allChunksReceived)
    {
      byte[] serializedData = DatasetSerializer.CombineChunks(chunkStorage[clientConnection]); // uncompress data
      VolumeDataset dataset = DatasetSerializer.Deserialize(serializedData); // deserialize back into original form
      chunkStorage.Remove(clientConnection);

      if (dataset == null)
      {
        Debug.LogError("Client failed to deserialize dataset.");
        yield break;
      }

      StartCoroutine(AssignLocalDataset(dataset));
    }
    yield return null;
  }

  private IEnumerator AssignLocalDataset(VolumeDataset dataset)
  {
    if (!IsServer)
    {
      NetworkObject networkObject = volumeRenderedObjectPrefab.GetComponent<NetworkObject>();
      // return coroutine instance to find volumeObject
      // force return of volumeObject despite the fact that it does not have an attached VolumeRenderedObject component since the client still needs to attach the component after receiving the data
      var result = VolumeRenderObjectFindUtility.FindVolumeObject(caller: "CrossSectionManager", prefab: networkObject, forceReturn: true);
      yield return result; // pause execution of this coroutine to wait for the VolumeRenderObjectFindUtility to find the volumeObject and therefore yield a result
      volumeObject = result.Current as VolumeRenderedObject; // safely cast the coroutine result to VolumeRenderedObject type
    }
    else
    {
      if (volumeObject == null)
        yield break;
    }
    Debug.Log("Configuring dataset rendering on volumeObject.");
    // create new datasetloader and configure rendering
    datasetLoader = new DataSetLoader();
    yield return datasetLoader.ConfigureVolumeRenderingAsync(volumeObject, dataset);
    // set to default positions
    volumeObject.transform.position = defaultPosition;
    volumeObject.transform.rotation = defaultRotation;
  }
}
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using FishyVoice;
using UnityEngine;
using System;
using System.Net;
using System.Linq;
using System.Collections;
using TMPro;

public class ConnectionSetup : MonoBehaviour
{
  [SerializeField] private NetworkManager networkManager;
  [SerializeField] private GameObject userAvatarPrefab; // for explicit spawning
  public Canvas mainMenuCanvas;
  [SerializeField] private GameObject ipInputFieldObject;

  private string IP = "10.255.10.54";
  private const string VOICE_ROOM_NAME = "<DEFAULT>";

  private void Start()
  {
    if (networkManager == null)
    {
      Debug.LogError("NetworkManager is not assigned in ConnectionSetup!");
      return;
    }
    // Debug connection states
    InstanceFinder.ClientManager.OnClientConnectionState += ClientConnectionState;
    InstanceFinder.ServerManager.OnServerConnectionState += ServerConnectionState;
    InstanceFinder.ServerManager.OnRemoteConnectionState += (conn, args) => RemoteConnectionState(conn, args);
  }

  private void ServerConnectionState(ServerConnectionStateArgs args)
  {
    Debug.Log($"Server state: {args.ConnectionState}");
    if (args.ConnectionState == LocalConnectionState.Started)
    {
      StartCoroutine(CreateVoiceRoomDelayed());
    }
  }

  private IEnumerator CreateVoiceRoomDelayed()
  {
    // Wait for host's client connection to be established
    while (InstanceFinder.ClientManager.Connection.ClientId == -1)
    {
      Debug.Log("Waiting for host client connection...");
      yield return new WaitForSeconds(0.1f);
    }

    VoiceNetwork voiceNetwork = VoiceNetwork.instance;
    if (voiceNetwork != null)
    {
      try
      {
        voiceNetwork.HostChatroom(VOICE_ROOM_NAME);
        Debug.Log($"Created voice chatroom: {VOICE_ROOM_NAME}");
      }
      catch (Exception e)
      {
        Debug.LogError($"Failed to create voice chatroom: {e.Message}");
      }
    }
    else
    {
      Debug.LogError("VoiceNetwork instance not found in scene!");
    }
  }

  //private void ClientConnectionState(ClientConnectionStateArgs args)
  //{
  //  Debug.Log($"Client state: {args.ConnectionState}");
  //  if (args.ConnectionState == LocalConnectionState.Started)
  //  {
  //    // Check if we're the host - already joined when creating the room
  //    bool isHost = InstanceFinder.ServerManager.Started;

  //    if (isHost)
  //    {
  //      return;
  //    }

  //    StartCoroutine(JoinVoiceRoomDelayed());
  //  }
  //}
  private void ClientConnectionState(ClientConnectionStateArgs args)
  {
    Debug.Log($"Client state: {args.ConnectionState}");
    if (args.ConnectionState == LocalConnectionState.Started)
    {
      StartCoroutine(JoinVoiceRoomDelayed());
    }
  }

  private IEnumerator JoinVoiceRoomDelayed()
  {
    yield return new WaitForSeconds(1f); // Wait for server
    VoiceNetwork voiceNetwork = VoiceNetwork.instance;
    if (voiceNetwork != null)
    {
      try
      {
        voiceNetwork.JoinChatroom(VOICE_ROOM_NAME);
        Debug.Log($"Client {InstanceFinder.ClientManager.Connection.ClientId} joined voice chatroom: {VOICE_ROOM_NAME}");
      }
      catch (Exception e)
      {
        Debug.LogError($"Client failed to join voice chatroom: {e.Message}");
      }
    }
    else
    {
      Debug.LogError("VoiceNetwork instance not found for client!");
    }
  }

  private void RemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
  {
    Debug.Log($"Client {conn.ClientId} state: {args.ConnectionState}");
  }

  private void OnDestroy()
  {
    if (InstanceFinder.ClientManager != null)
      InstanceFinder.ClientManager.OnClientConnectionState -= ClientConnectionState;
    if (InstanceFinder.ServerManager != null)
    {
      InstanceFinder.ServerManager.OnServerConnectionState -= ServerConnectionState;
      InstanceFinder.ServerManager.OnRemoteConnectionState -= (conn, args) => RemoteConnectionState(conn, args);
    }
  }

  public void StartHost()
  {
    if (networkManager == null)
    {
      Debug.LogError("NetworkManager is not assigned, cannot start host!");
      return;
    }
    var tugboat = (Tugboat)networkManager.TransportManager.Transport;
    tugboat.SetClientAddress("127.0.0.1"); // Host's client connects to itself
    tugboat.SetServerBindAddress("0.0.0.0", IPAddressType.IPv4);
    tugboat.SetPort(7770);
    Debug.Log($"Starting Host on {IP}:7770");
    StartServer();
    StartClient();
  }

  public void StartServer()
  {
    if (networkManager == null)
    {
      Debug.LogError("NetworkManager is not assigned, cannot start server!");
      return;
    }
    networkManager.ServerManager.StartConnection();
    Debug.Log("Server started");
    HideMenu();
  }

  public void StartClient()
  {
    if (networkManager == null)
    {
      Debug.LogError("NetworkManager is not assigned, cannot start client!");
      return;
    }
    networkManager.ClientManager.StartConnection();
    Debug.Log("Client connection started");
    HideMenu();
  }

  public void SetIPAddress(GameObject ipInputFieldObject)
  {
    if (networkManager == null)
    {
      Debug.LogError("NetworkManager is not assigned, cannot set IP address!");
      return;
    }
    var tugboat = (Tugboat)networkManager.TransportManager.Transport;
    if (ipInputFieldObject != null)
    {
      TMP_InputField inputField = ipInputFieldObject.GetComponent<TMP_InputField>();
      if (inputField != null && !string.IsNullOrEmpty(inputField.text))
      {
        tugboat.SetClientAddress(inputField.text);
        Debug.Log($"User Input: Changed Host IP to connect to: {inputField.text}");
      }
      else
      {
        tugboat.SetClientAddress(IP);
        Debug.LogWarning($"Input field is empty, using default: {IP}");
      }
    }
    else
    {
      tugboat.SetClientAddress(IP);
      Debug.LogWarning($"ipInputFieldObject is null, using default: {IP}");
    }
    tugboat.SetPort(7770);
  }

  private void HideMenu()
  {
    if (mainMenuCanvas != null)
    {
      //mainMenuCanvas.gameObject.SetActive(false);
      mainMenuCanvas.enabled = false; // only disable visual
    }
  }

  private string GetLocalIPv4()
  {
    try
    {
      return Dns.GetHostEntry(Dns.GetHostName()).AddressList
          .First(f => f.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
          .ToString();
    }
    catch (System.Exception ex)
    {
      Debug.LogError($"Failed to get local IPv4: {ex.Message}");
      return "127.0.0.1";
    }
  }

  /*
  private void SpawnPlayerForClient(NetworkConnection conn)
  {
      if (userAvatarPrefab == null)
      {
          Debug.LogError("UserAvatar prefab is not assigned in ConnectionSetup!");
          return;
      }
      GameObject player = Instantiate(userAvatarPrefab, Vector3.zero, Quaternion.identity);
      NetworkObject nob = player.GetComponent<NetworkObject>();
      if (nob == null)
      {
          Debug.LogError("UserAvatar prefab missing NetworkObject!");
          Destroy(player);
          return;
      }
      networkManager.ServerManager.Spawn(player, conn);
      Debug.Log($"Spawned UserAvatar for client {conn.ClientId}, ObjectId={nob.ObjectId}, Owner={nob.Owner.ClientId}");
  }
  */
}
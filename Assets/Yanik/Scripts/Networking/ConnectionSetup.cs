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
  // private to other classes while still being editable in Unity Inspector
  [SerializeField] private NetworkManager networkManager;
  [SerializeField] private GameObject ipInputFieldObject;
  [SerializeField] private string hostIP = "10.255.10.54"; // default host IP address
  [SerializeField] private ushort portNumber = 7770; // default FishNet port for comms

  public Canvas mainMenuCanvas;
  private const string VOICE_ROOM_NAME = "<DEFAULT>";
  private bool chatroomCreated = false;

  /// <summary>
  /// Executes before first frame of program to subscribe to FishNet connection states.
  /// </summary>
  private void Start()
  {
    InstanceFinder.ClientManager.OnClientConnectionState += ClientConnectionState; // handle client connecting/disconnecting
    InstanceFinder.ServerManager.OnServerConnectionState += ServerConnectionState; // handle server-side connection events
    InstanceFinder.ServerManager.OnRemoteConnectionState += (conn, args) => RemoteConnectionState(conn, args); // handle remote client connection state
  }

  private void ServerConnectionState(ServerConnectionStateArgs args)
  {
    Debug.Log($"Server state: {args.ConnectionState}.");
    if (args.ConnectionState == LocalConnectionState.Started) // successful server start
    {
      StartCoroutine(CreateVoiceRoomDelayed());
    }
  }

  /// <summary>
  /// Coroutine to create voice comms chat room.
  /// </summary>
  /// <returns></returns>
  private IEnumerator CreateVoiceRoomDelayed()
  {
    // wait until the host's client connection is established
    while (InstanceFinder.ClientManager.Connection.ClientId == -1) // [-1] is invalid ID
      yield return new WaitForSeconds(0.1f);

    VoiceNetwork voiceNetwork = VoiceNetwork.instance;
    try
    {
      voiceNetwork.HostChatroom(VOICE_ROOM_NAME);
      chatroomCreated = true;
      Debug.Log($"Created voice chatroom: {VOICE_ROOM_NAME}");
    }
    catch (Exception e)
    {
      Debug.LogError($"Failed to create voice chatroom with message: {e.Message}");
    }
  }

  private void ClientConnectionState(ClientConnectionStateArgs args)
  {
    Debug.Log($"Client state: {args.ConnectionState}");
    if (args.ConnectionState == LocalConnectionState.Started) // client connection started
    {
      StartCoroutine(JoinVoiceRoomDelayed());
    }
  }

  private IEnumerator JoinVoiceRoomDelayed()
  {
    // Wait for server to start and client to have a valid ClientId
    bool isServerStarted = InstanceFinder.ServerManager.Started;
    int clientId = InstanceFinder.ClientManager.Connection.ClientId;
    while (!isServerStarted || clientId < 0) // [-1] is invalid Id
    {
      yield return new WaitForSeconds(0.1f);
    }

    VoiceNetwork voiceNetwork = VoiceNetwork.instance;
    try
    {
      voiceNetwork.JoinChatroom(VOICE_ROOM_NAME);
      Debug.Log($"Client {InstanceFinder.ClientManager.Connection.ClientId} joined chatroom {VOICE_ROOM_NAME}");
    }
    catch (Exception e)
    {
      Debug.LogError($"Client failed to join chatroom with message: {e.Message}");
    }
  }

  private void RemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
  {
    Debug.Log($"Remote client {conn.ClientId} state: {args.ConnectionState}");
  }

  private void OnDestroy()
  {
    // unsubscribe from all events for cleanup
    if (InstanceFinder.ClientManager != null)
      InstanceFinder.ClientManager.OnClientConnectionState -= ClientConnectionState;
    if (InstanceFinder.ServerManager != null)
    {
      InstanceFinder.ServerManager.OnServerConnectionState -= ServerConnectionState;
      InstanceFinder.ServerManager.OnRemoteConnectionState -= (conn, args) => RemoteConnectionState(conn, args);
    }
  }

  public void StartHost() // Host = server + client instance
  {
    // set transport
    var tugboat = (Tugboat)networkManager.TransportManager.Transport;
    tugboat.SetClientAddress("127.0.0.1"); // Host-client connects to itself
    tugboat.SetServerBindAddress("0.0.0.0", IPAddressType.IPv4); // config server to accept connections from any IP
    tugboat.SetPort(portNumber);
    Debug.Log($"Starting Host on {hostIP}:{portNumber}");
    StartServer();
    StartClient();
  }

  public void StartServer()
  {
    networkManager.ServerManager.StartConnection();
    Debug.Log("Server started");
    HideMenu();
  }

  public void StartClient()
  {
    networkManager.ClientManager.StartConnection();
    Debug.Log("Client connection started");
    HideMenu();
  }

  public void SetIPAddress(GameObject ipInputFieldObject)
  {
    if (networkManager == null)
      return;
    // set transport
    var tugboat = (Tugboat)networkManager.TransportManager.Transport;
    if (ipInputFieldObject != null)
    {
      TMP_InputField inputField = ipInputFieldObject.GetComponent<TMP_InputField>();
      if (inputField != null && !string.IsNullOrEmpty(inputField.text))
      {
        tugboat.SetClientAddress(inputField.text);
      }
      else
      {
        tugboat.SetClientAddress(hostIP);
        Debug.LogWarning($"IP input field is empty, using default host IP: {hostIP}");
      }
    }
    else
    {
      tugboat.SetClientAddress(hostIP);
      Debug.LogWarning($"ipInputFieldObject is null, using default host IP: {hostIP}");
    }
    tugboat.SetPort(portNumber);
  }

  private void HideMenu()
  {
    if (mainMenuCanvas != null)
    {
      //mainMenuCanvas.gameObject.SetActive(false);
      mainMenuCanvas.enabled = false; // only disable visual instead of whole menu canvas
    }
  }
}
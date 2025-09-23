using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using System.Net;
using System.Linq;
using TMPro;

public class ConnectionSetup : MonoBehaviour
{
  [SerializeField] private NetworkManager networkManager;
  [SerializeField] private GameObject userAvatarPrefab; // Optional: Assign for explicit spawning
  public Canvas mainMenuCanvas;
  [SerializeField] private GameObject ipInputFieldObject;

  private string IP = "10.255.10.54";

  private void Start()
  {
    if (networkManager == null)
    {
      Debug.LogError("NetworkManager is not assigned in ConnectionSetup!");
      return;
    }
    // Debug connection states
    InstanceFinder.ClientManager.OnClientConnectionState += (state) => Debug.Log($"Client state: {state}");
    InstanceFinder.ServerManager.OnServerConnectionState += (state) => Debug.Log($"Server state: {state}");
    InstanceFinder.ServerManager.OnRemoteConnectionState += (conn, args) => Debug.Log($"Client {conn.ClientId} state: {args.ConnectionState}");
    // Optional: Uncomment for explicit spawning
    /*
    InstanceFinder.ServerManager.OnRemoteConnectionState += (conn, args) =>
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            Debug.Log($"Client {conn.ClientId} connected, spawning UserAvatar.");
            SpawnPlayerForClient(conn);
        }
    };
    */
  }

  private void OnDestroy()
  {
    if (InstanceFinder.ClientManager != null)
      InstanceFinder.ClientManager.OnClientConnectionState -= (state) => Debug.Log($"Client state: {state}");
    if (InstanceFinder.ServerManager != null)
    {
      InstanceFinder.ServerManager.OnServerConnectionState -= (state) => Debug.Log($"Server state: {state}");
      InstanceFinder.ServerManager.OnRemoteConnectionState -= (conn, args) => Debug.Log($"Client {conn.ClientId} state: {args.ConnectionState}");
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
    Debug.Log("Starting Host on 10.255.10.54:7770");
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
        Debug.LogWarning($"Input field is empty or missing TMP_InputField, using default: {IP}");
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
      mainMenuCanvas.gameObject.SetActive(false);
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
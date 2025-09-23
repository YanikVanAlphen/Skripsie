using FishNet;
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
  public Canvas mainMenuCanvas;
  [SerializeField] private GameObject ipInputFieldObject;

  private string IP = "10.255.10.54";

  private void Start()
  {
    // Debug connection states
    InstanceFinder.ClientManager.OnClientConnectionState += (state) => Debug.Log($"Client state: {state}");
    InstanceFinder.ServerManager.OnServerConnectionState += (state) => Debug.Log($"Server state: {state}");
  }

  // Host is server and client, therefore start both
  public void StartHost()
  {
    var tugboat = (Tugboat)networkManager.TransportManager.Transport;
    tugboat.SetClientAddress("127.0.0.1"); // Host's client connects to itself
    tugboat.SetServerBindAddress("0.0.0.0", IPAddressType.IPv4);
    tugboat.SetPort(7770);
    Debug.Log("Starting Host on 10.0.0.1:7770");
    StartServer();
    StartClient();
  }

  // The server can be started directly from the ServerManager or Transport
  public void StartServer()
  {
    networkManager.ServerManager.StartConnection();
    Debug.Log("Server started");
    HideMenu();
  }

  // The client can be started directly from the ClientManager or Transport
  public void StartClient()
  {
    networkManager.ClientManager.StartConnection();
    Debug.Log("Client connection started");
    HideMenu();
  }

  // Set on the Transport to indicate where the Client should connect
  public void SetIPAddress(GameObject ipInputFieldObject)
  {
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

  private string GetLocalIPv4() // Only returns first IP, maybe not best to use hardcode for safety sy
  {
    return Dns.GetHostEntry(Dns.GetHostName()).AddressList.First(f => f.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToString();
  }
}
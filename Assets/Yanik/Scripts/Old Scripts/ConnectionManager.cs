using FishNet.Managing;
using UnityEngine;
using FishNet.Transporting.Tugboat;
using FishNet.Object;

public class ConnectionManager : MonoBehaviour
{
  [SerializeField] private NetworkManager networkManager;
  public GameObject vrMenuPrefab; // VR menu prefab
  private GameObject vrMenuInstance; // Store the instantiated menu
  public GameObject xrKeyboardPrefab;

  private string ip;

  void Start() // Default to host
  {
    //StartHost();
  }

  // Host = server and client -> start both
  public void StartHost()
  {
    var tugboat = (Tugboat)networkManager.TransportManager.Transport;
    tugboat.SetClientAddress("127.0.0.1");
    tugboat.SetPort(7770);

    networkManager.ServerManager.StartConnection();
    networkManager.ClientManager.StartConnection();

    // Spawn VR menu and store instance
    vrMenuInstance = Instantiate(vrMenuPrefab);
    NetworkObject vrMenuNetworkObject = vrMenuInstance.GetComponent<NetworkObject>();
    if (vrMenuNetworkObject != null)
    {
      networkManager.ServerManager.Spawn(vrMenuNetworkObject);
    }
    else
    {
      Debug.LogError("VR menu prefab missing NetworkObject component.");
    }
    // Spawn XR keyboard
    GameObject xrKeyboardInstance = Instantiate(xrKeyboardPrefab);
    NetworkObject xrKeyboardNetworkObject = xrKeyboardInstance.GetComponent<NetworkObject>();
    if (xrKeyboardNetworkObject != null)
    {
      networkManager.ServerManager.Spawn(xrKeyboardNetworkObject);
    }
    else
    {
      Debug.LogError("XR keyboard prefab missing NetworkObject component.");
    }
  }

  public void ConnectAsClient()
  {
    if (!string.IsNullOrEmpty(ip))
    {
      Debug.Log("Connecting as Client");
      networkManager.ClientManager.StopConnection();
      var tugboat = (Tugboat)networkManager.TransportManager.Transport;
      tugboat.SetClientAddress(ip);
      tugboat.SetPort(7770);
      networkManager.ClientManager.StartConnection();
      if (vrMenuInstance != null)
      {
        HideMenu(vrMenuInstance);
      }
    }
  }

  // Restart as host (stop client and server, then restart)
  public void RestartAsHost()
  {
    networkManager.ClientManager.StopConnection();
    networkManager.ServerManager.StopConnection(true);
    StartHost();
    if (vrMenuInstance != null)
    {
      HideMenu(vrMenuInstance);
    }
  }

  private void HideMenu(GameObject menuInstance)
  {
    if (menuInstance != null)
    {
      menuInstance.SetActive(false);
    }
  }

  // Update IP string on text field input change
  public void UpdateIPOnChange(string newIP)
  {
    if (!string.IsNullOrEmpty(newIP))
    {
      var tugboat = (Tugboat)networkManager.TransportManager.Transport;
      ip = newIP;
      tugboat.SetClientAddress(ip);
      tugboat.SetPort(7770);
      Debug.Log($"IP updated to: {ip} on change");
    }
  }
}
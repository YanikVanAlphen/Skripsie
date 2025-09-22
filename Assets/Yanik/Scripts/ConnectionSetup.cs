using FishNet.Managing;
using UnityEngine;
using FishNet.Transporting.Tugboat;

public class ConnectionSetup : MonoBehaviour
{
  [SerializeField] NetworkManager networkManager;
  public Canvas mainMenuCanvas;

  // Host is server and client, therefore start both
  public void StartHost()
  {
    var tugboat = (Tugboat)networkManager.TransportManager.Transport;
    tugboat.SetClientAddress("127.0.0.1");
    tugboat.SetPort(7770);

    StartServer();
    StartClient();
  }

  // The server can be started directly from the ServerManager or Transport
  public void StartServer()
  {
    networkManager.ServerManager.StartConnection();
    HideMenu();
  }

  // The client can be started directly from the ClientManager or Transport
  public void StartClient()
  {
    networkManager.ClientManager.StartConnection();
    HideMenu();
  }

  // Set on the Transport to indicate where the Client should connect
  public void SetIPAddress(string ip)
  {
    if (!string.IsNullOrEmpty(ip))
    {
      var tugboat = (Tugboat)networkManager.TransportManager.Transport;
      tugboat.SetClientAddress(ip);
      tugboat.SetPort(7770);
      //networkManager.TransportManager.Transport.SetClientAddress(ip);
    }
  }

  private void HideMenu()
  {
    if (mainMenuCanvas != null)
    {
      mainMenuCanvas.gameObject.SetActive(false);
    }
  }
}
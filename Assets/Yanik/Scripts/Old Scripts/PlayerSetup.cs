using UnityEngine;
using FishNet.Object;
using FishNet.Connection;

public class PlayerSetup : NetworkBehaviour
{
  public GameObject vrMenuPrefab;

  public override void OnStartClient()
  {
    base.OnStartClient();
    if (base.IsOwner)
    {
      // Spawn VRMenu for this player
      GameObject menuInstance = Instantiate(vrMenuPrefab);
      menuInstance.transform.SetParent(transform); // Attach to player
      menuInstance.transform.localPosition = new Vector3(0, 0, 1); // In front of player

      // Assign Event Camera
      Canvas canvas = menuInstance.GetComponentInChildren<Canvas>();
      if (canvas != null)
      {
        canvas.worldCamera = GetComponentInChildren<Camera>(); // Assign player's camera
      }
    }
  }
}
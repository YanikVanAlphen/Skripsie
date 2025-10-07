using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine.UI;
using UnityVolumeRendering;
using System.Collections;
using TMPro;
using System.Linq;
using System;
using uMuVR;

public class VolumeDataControlUI : NetworkBehaviour
{
  // --------- UI inputs on canvas ---------
  public VolumeDataNetworker volumeDataNetworker;

  public TMP_Dropdown positionAxisDropdown;
  public Button positionIncrementButton;
  public Button positionDecrementButton;
  public TextMeshProUGUI positionXText;
  public TextMeshProUGUI positionYText;
  public TextMeshProUGUI positionZText;

  public TMP_Dropdown rotationAxisDropdown;
  public Button rotationIncrementButton;
  public Button rotationDecrementButton;
  public TextMeshProUGUI rotationXText;
  public TextMeshProUGUI rotationYText;
  public TextMeshProUGUI rotationZText;

  public Slider scaleSlider;
  public TextMeshProUGUI scaleText;

  public Button spawnCrossSectionButton;
  // ---------------------------------------

  private VolumeRenderedObject volumeObject;
  private const float positionIncrement = 0.05f; // Small increment for position
  private const float rotationIncrement = 5f;   // Small increment for rotation

  private void Start()
  {
    if (volumeDataNetworker == null)
    {
      // runtime assignment of VolumeDataNetworker component since the data menu spawns at runtime
      volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();
    }
    if (volumeDataNetworker != null)
      volumeDataNetworker.OnVolumeSpawned += OnVolumeSpawned; // subscribe to OnVolumeSpawned event to know when to start searching for the VolumeObject - make sure timing is right
    SetupUI();
    UpdateInteractableState();
    // continually update menu labels as the volume is manipulated
    StartCoroutine(UpdateLabels());
  }

  private void SetupUI()
  {
    // listener methods to activate on UI event handlers
    if (positionIncrementButton != null)
      positionIncrementButton.onClick.AddListener(OnPositionIncrementClicked);
    if (positionDecrementButton != null)
      positionDecrementButton.onClick.AddListener(OnPositionDecrementClicked);
    if (rotationIncrementButton != null)
      rotationIncrementButton.onClick.AddListener(OnRotationIncrementClicked);
    if (rotationDecrementButton != null)
      rotationDecrementButton.onClick.AddListener(OnRotationDecrementClicked);
    if (scaleSlider != null)
      scaleSlider.onValueChanged.AddListener(OnScaleSliderChanged);
    if (spawnCrossSectionButton != null)
      spawnCrossSectionButton.onClick.AddListener(OnSpawnCrossSectionButtonClicked);
  }

  private void OnDestroy()
  {
    // unsubscribe from OnVolumeSpawned event
    if (volumeDataNetworker != null)
      volumeDataNetworker.OnVolumeSpawned -= OnVolumeSpawned;
  }

  private void OnVolumeSpawned(int objectId)
  {
    // notified that VolumeObject has been spawned, can now start looking for it
    StartCoroutine(WaitForVolumeObject(objectId));
  }

  private IEnumerator WaitForVolumeObject(int objectId)
  {
    int retryCount = 0;
    const int maxRetries = 60;

    while (volumeObject == null && retryCount < maxRetries)
    {
      // NetworkManager.ClientManager.Objects.Spawned from FishNet maps ObjectIDs to NetworkObjects that have been spawned and synced to the client
      // TryGetValue tries to get the NetworkObject, if it succeeds then it assigns it to networkObject. (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.trygetvalue?view=net-9.0)
      if (NetworkManager.ClientManager.Objects.Spawned.TryGetValue(objectId, out NetworkObject networkObject))
      {
        volumeObject = networkObject.GetComponent<VolumeRenderedObject>();
        Debug.Log($"VolumeDataControlUI: Found VolumeRenderedObject (ObjectId={objectId})");
        yield break;
      }

      retryCount++;
      yield return new WaitForSeconds(0.5f);
    }
  }

  private void UpdateInteractableState()
  {
    bool isPermitted = IsOwner || IsServer; // IsOwner and IsServer provided directly by FishNet - indicates ownership
    // allow or block anything who isnt owner of canvas from editing
    if (positionAxisDropdown != null)
      positionAxisDropdown.interactable = isPermitted;
    if (positionIncrementButton != null)
      positionIncrementButton.interactable = isPermitted;
    if (positionDecrementButton != null)
      positionDecrementButton.interactable = isPermitted;
    if (rotationAxisDropdown != null)
      rotationAxisDropdown.interactable = isPermitted;
    if (rotationIncrementButton != null)
      rotationIncrementButton.interactable = isPermitted;
    if (rotationDecrementButton != null)
      rotationDecrementButton.interactable = isPermitted;
    if (scaleSlider != null)
      scaleSlider.interactable = isPermitted;
  }

  public override void OnOwnershipClient(NetworkConnection prevOwner) // callback function for when ownership of object changes
  {
    base.OnOwnershipClient(prevOwner); // ensure all default ownersip change functions are called
    UpdateInteractableState(); // only new owner can interact
    if (IsOwner && volumeObject != null) // should this not be !IsOwner??
    {
      NetworkObject volumeNetworkObject = volumeObject.GetComponent<NetworkObject>();
      if (volumeNetworkObject != null && volumeNetworkObject.Owner != Owner) // change in ownership
      {
        volumeNetworkObject.GiveOwnership(Owner);
        Debug.Log($"Transferred VolumeRenderedObject ownership to client {Owner.ClientId}, ObjectId={volumeNetworkObject.ObjectId}");
      }
    }
  }

  private IEnumerator UpdateLabels()
  {
    while (true)
    {
      // default params
      float scale = 1.0f;
      Vector3 pos = Vector3.zero;
      Vector3 rot = Vector3.zero;

      if (volumeObject != null)
      {
        pos = volumeObject.transform.position;
        rot = volumeObject.transform.rotation.eulerAngles;
        scale = volumeObject.transform.localScale.x; // uniform scaling

        // normalize angles for printing to labels
        rot.x = NormalizeAngle(rot.x);
        rot.y = NormalizeAngle(rot.y);
        rot.z = NormalizeAngle(rot.z);
      }

      // update position text
      if (positionXText != null)
        positionXText.text = $"X: {pos.x:F2}";
      if (positionYText != null)
        positionYText.text = $"Y: {pos.y:F2}";
      if (positionZText != null)
        positionZText.text = $"Z: {pos.z:F2}";

      // update rotation text
      if (rotationXText != null)
        rotationXText.text = $"X: {rot.x:F2}°";
      if (rotationYText != null)
        rotationYText.text = $"Y: {rot.y:F2}°";
      if (rotationZText != null)
        rotationZText.text = $"Z: {rot.z:F2}°";

      // update scale text
      if (scaleText != null)
        scaleText.text = $"Scale: {scale:F2}";
      if (scaleSlider != null)  
        scaleSlider.value = scale;

      // update labels every 100ms - a bit brute force
      yield return new WaitForSeconds(0.1f);
    }
  }

  private void OnPositionIncrementClicked()
  {
    // request ownership of canvas to edit
    RequestCanvasOwnership();

    if (!CanInteract() || volumeObject == null)
      return;

    int axisIndex = 0; // default value
    if (positionAxisDropdown != null)
    {
      axisIndex = positionAxisDropdown.value; // set axisIndex to current axis selected by dropdown
    }
    // get current position to modify
    Vector3 newPos = volumeObject.transform.position;

    switch (axisIndex)
    {
      case 0: // x axis
        newPos.x += positionIncrement;
        newPos.x = (float)Math.Round(newPos.x, 2);
        break;
      case 1: // y axis
        newPos.y += positionIncrement;
        newPos.y = (float)Math.Round(newPos.y, 2);
        break;
      case 2: // z axis
        newPos.z += positionIncrement;
        newPos.z = (float)Math.Round(newPos.z, 2);
        break;
    }

    //volumeObject.transform.position = newPos;
    Debug.Log($"Set volumetric data position to {newPos}.");

    SetPositionServerRpc(newPos);
  }

  private void OnPositionDecrementClicked()
  {
    // request ownership of canvas to edit
    RequestCanvasOwnership();

    if (!CanInteract() || volumeObject == null)
      return;

    int axisIndex = 0;
    if (positionAxisDropdown != null)
    {
      axisIndex = positionAxisDropdown.value; // set axisIndex to current axis selected by dropdown
    }
    // get current position to modify
    Vector3 newPos = volumeObject.transform.position;

    switch (axisIndex)
    {
      case 0: // x axis
        newPos.x -= positionIncrement;
        newPos.x = (float)Math.Round(newPos.x, 2);
        break;
      case 1: // y axis
        newPos.y -= positionIncrement;
        newPos.y = (float)Math.Round(newPos.y, 2);
        break;
      case 2: // z axis
        newPos.z -= positionIncrement;
        newPos.z = (float)Math.Round(newPos.z, 2);
        break;
    }

    //volumeObject.transform.position = newPos;
    Debug.Log($"Set volumetric data position to {newPos}.");

    SetPositionServerRpc(newPos);
  }

  private void OnRotationIncrementClicked()
  {
    // request ownership of canvas to edit
    RequestCanvasOwnership();

    if (!CanInteract() || volumeObject == null)
      return;

    int axisIndex = 0;
    if (rotationAxisDropdown != null)
    {
      axisIndex = rotationAxisDropdown.value;
    }
    Vector3 rotationAxis;

    switch (axisIndex)
    {
      case 0: // x axis
        rotationAxis = Vector3.right;
        break;
      case 1: // y axis
        rotationAxis = Vector3.up;
        break;
      case 2: // z axis
        rotationAxis = Vector3.forward;
        break;
      default:
        rotationAxis = Vector3.up; // fallback to y axis
        break;
    }

    Quaternion deltaRotation = Quaternion.AngleAxis(rotationIncrement, rotationAxis);
    Quaternion newRot = volumeObject.transform.rotation * deltaRotation; // apply change in rotation relative to current rotation
    //volumeObject.transform.rotation = newRot;

    Vector3 euler = newRot.eulerAngles;
    euler.x = NormalizeAngle(euler.x);
    euler.y = NormalizeAngle(euler.y);
    euler.z = NormalizeAngle(euler.z);
    Debug.Log($"Set volumetric data rotation to Euler={euler}.");

    SetRotationServerRpc(newRot); // sync rotation to all clients
  }

  private void OnRotationDecrementClicked()
  {
    // request ownership of canvas to edit
    RequestCanvasOwnership();

    if (!CanInteract() || volumeObject == null)
      return;

    int axisIndex = 0;
    if (rotationAxisDropdown != null)
    {
      axisIndex = rotationAxisDropdown.value;
    }
    Vector3 rotationAxis;

    switch (axisIndex)
    {
      case 0: // x axis
        rotationAxis = Vector3.right;
        break;
      case 1: // y axis
        rotationAxis = Vector3.up;
        break;
      case 2: // z axis
        rotationAxis = Vector3.forward;
        break;
      default:
        rotationAxis = Vector3.up; // fallback to y axis
        break;
    }

    Quaternion deltaRotation = Quaternion.AngleAxis(-rotationIncrement, rotationAxis);
    Quaternion newRot = volumeObject.transform.rotation * deltaRotation;
    //volumeObject.transform.rotation = newRot;

    Vector3 euler = newRot.eulerAngles;
    euler.x = NormalizeAngle(euler.x);
    euler.y = NormalizeAngle(euler.y);
    euler.z = NormalizeAngle(euler.z);
    Debug.Log($"Set volumetric data rotation to Euler={euler}.");

    SetRotationServerRpc(newRot);
  }

  private void OnScaleSliderChanged(float value)
  {
    // request ownership of canvas to edit
    RequestCanvasOwnership();

    if (!CanInteract() || volumeObject == null)
      return;

    Vector3 newScale = new Vector3(value, value, value); // uniform scaling
    //volumeObject.transform.localScale = newScale;
    Debug.Log($"Set volumetric data scale to {newScale}.");

    SetScaleServerRpc(value);
  }

  private void OnSpawnCrossSectionButtonClicked()
  {
    if (volumeObject == null)
      return;

    Debug.Log("Requested spawn of CrossSectionPlane.");
    // request ownership of canvas to edit
    RequestCanvasOwnership();

    if (spawnCrossSectionButton != null)
      spawnCrossSectionButton.interactable = false; // disable to prevent double-click spawning

    var crossSectionManager = FindObjectOfType<CrossSectionManager>();
    // default spawn for cross section
    Vector3 spawnPosition = new Vector3(0f, 1.5f, 0f);
    Quaternion spawnRotation = Quaternion.Euler(0f, 0f, 0f);

    crossSectionManager.SpawnCrossSectionPlaneServerRpc(spawnPosition, spawnRotation, NetworkManager.ClientManager.Connection);
  }

  [ServerRpc(RequireOwnership = false)]
  private void SetPositionServerRpc(Vector3 newPos) // make sure change is approved by server before clients see the update
  {
    SetPositionClientRpc(newPos);
  }

  //[Client]
  [ObserversRpc]
  private void SetPositionClientRpc(Vector3 newPos) // runs on clients to execute actual change in client's local scene -> synched with server state
  {
    if (volumeObject != null)
    {
      volumeObject.transform.position = newPos;
      Debug.Log($"Client {NetworkManager.ClientManager.Connection.ClientId} updated position to {newPos}.");
    }
  }

  [ServerRpc(RequireOwnership = false)]
  private void SetScaleServerRpc(float scale)
  {
    SetScaleClientRpc(scale);
  }

  //[Client]
  [ObserversRpc]
  private void SetScaleClientRpc(float scale)
  {
    if (volumeObject != null)
    {
      volumeObject.transform.localScale = new Vector3(scale, scale, scale);
      Debug.Log($"Client {NetworkManager.ClientManager.Connection.ClientId} updated scale to {scale}.");
    }
  }

  [ServerRpc(RequireOwnership = false)]
  private void SetRotationServerRpc(Quaternion newRot)
  {
    SetRotationClientRpc(newRot);
  }

  //[Client]
  [ObserversRpc]
  private void SetRotationClientRpc(Quaternion newRot)
  {
    if (volumeObject != null)
    {
      volumeObject.transform.rotation = newRot;
      Vector3 euler = newRot.eulerAngles;
      euler.x = NormalizeAngle(euler.x);
      euler.y = NormalizeAngle(euler.y);
      euler.z = NormalizeAngle(euler.z);
      Debug.Log($"Client {NetworkManager.ClientManager.Connection.ClientId} updated rotation to Euler={euler}, Quaternion={newRot}.");
    }
  }

  private void RequestCanvasOwnership()
  {
    OwnershipManager ownershipManager = GetComponent<OwnershipManager>();
    if (ownershipManager == null)
      return;

    if (!IsOwner)
    {
      NetworkConnection localConnection = NetworkManager.ClientManager.Connection;
      RequestCanvasOwnershipServerRpc(localConnection.ClientId);
    }
  }

  [ServerRpc(RequireOwnership = false)]
  private void RequestCanvasOwnershipServerRpc(int clientId)
  {
    NetworkConnection requester = ServerManager.Clients[clientId];
    if (requester != null && NetworkObject.Owner != requester)
    {
      NetworkObject.GiveOwnership(requester);
      Debug.Log($"Canvas ownership transferred to client {clientId}");
    }
  }

  private bool CanInteract()
  {
    return IsOwner || IsServer;
  }

  public void SetVolumeDataNetworker(VolumeDataNetworker networker)
  {
    volumeDataNetworker = networker;
  }

  // Normalise angles to [0, 360)
  private float NormalizeAngle(float angle)
  {
    angle = angle % 360f; // ensure angle is between 0 and 360
    if (angle < 0f) angle += 360f;
    return angle;
  }
}
using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine.UI;
using UnityVolumeRendering;
using System.Collections;
using TMPro;
using System.Linq;
using System;

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
  private const float positionIncrement = 0.1f; // Small increment for position
  private const float rotationIncrement = 5f;   // Small increment for rotation

  private void Start()
  {
    if (volumeDataNetworker == null)
    {
      volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();
      if (volumeDataNetworker == null)
      {
        Debug.LogError("No VolumeDataNetworker found in scene. Ensure it is assigned to VolumeDataControl canvas menu.");
        return;
      }
      Debug.LogWarning("VolumeDataNetworker was unassigned in Data Menu, found it via FindObjectOfType.");
    }

    StartCoroutine(FindVolumeObject());
    SetupUI();
    UpdateInteractableState();
    StartCoroutine(UpdateLabels());
  }

  private void SetupUI()
  {
    // wierd bug, cant seem to attach the buttons to script in UI so this is next best option
    // null-conditional operators ensures null returned without null reference exception thrown when a button with certain name isnt found
    if (positionIncrementButton == null)
      positionIncrementButton = GameObject.Find("position_pos_btn")?.GetComponent<Button>();
    if (positionDecrementButton == null)
      positionDecrementButton = GameObject.Find("position_neg_btn")?.GetComponent<Button>();
    if (rotationIncrementButton == null)
      rotationIncrementButton = GameObject.Find("rot_pos_btn")?.GetComponent<Button>();
    if (rotationDecrementButton == null)
      rotationDecrementButton = GameObject.Find("rot_neg_btn")?.GetComponent<Button>();
    if (spawnCrossSectionButton == null)
      spawnCrossSectionButton = GameObject.Find("slice_plane_btn")?.GetComponent<Button>();

    // listeners to activate function on UI event handlers
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

  private IEnumerator FindVolumeObject()
  {
    int retryCount = 0;
    const int maxRetries = 120; // searches for a full minute max

    // find networked instance of the volumeRenderedObject prefab spawned in the scene by the networkmanager by using the Prefab's NetworkObject ID
    while (volumeObject == null && retryCount < maxRetries) 
    {
      if (volumeDataNetworker != null && volumeDataNetworker.volumeRenderedObjectPrefab != null)
      {
        NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
        int expectedPrefabId = volumeDataNetworker.volumeRenderedObjectPrefab.GetComponent<NetworkObject>().PrefabId;
        
        NetworkObject targetNetObj = null;
        foreach (NetworkObject nob in networkObjects) // find first prefab with ID that matched expected prefab ID
        {
          if (nob.PrefabId == expectedPrefabId)
          {
            targetNetObj = nob;
            break;
          }
        }

        if (targetNetObj != null)
        {
          volumeObject = targetNetObj.GetComponent<VolumeRenderedObject>();
          if (volumeObject != null)
          {
            Debug.Log($"Found volumeObject (ObjectId={targetNetObj.ObjectId}, PrefabId={expectedPrefabId})");
          }
          else
          {
            Debug.LogWarning($"NetworkObject found (ObjectId={targetNetObj.ObjectId}, PrefabId={expectedPrefabId}) but missing VolumeRenderedObject component.");
          }
        }
        else
        {
          Debug.Log($"Waiting for VolumeRenderedObject: Attempt {(retryCount+1).ToString()} out of {maxRetries}.");
        }
      }
      else
      {
        Debug.LogWarning($"volumeDataNetworker or volumeRenderedObjectPrefab is null.");
      }

      if (volumeObject == null)
      {
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }

    if (volumeObject == null)
    {
      Debug.LogError($"Failed to find VolumeRenderedObject after {maxRetries} retries.");
    }
  }

  private void UpdateInteractableState()
  {
    bool isOwner = IsOwner || IsServer; // IsOwner and IsServer provided directly by FishNet - indicates ownership
    // allow or block anything who isnt owner of canvas from editing
    if (positionAxisDropdown != null)
      positionAxisDropdown.interactable = isOwner;
    if (positionIncrementButton != null)
      positionIncrementButton.interactable = isOwner;
    if (positionDecrementButton != null)
      positionDecrementButton.interactable = isOwner;
    if (rotationAxisDropdown != null)
      rotationAxisDropdown.interactable = isOwner;
    if (rotationIncrementButton != null)
      rotationIncrementButton.interactable = isOwner;
    if (rotationDecrementButton != null)
      rotationDecrementButton.interactable = isOwner;
    if (scaleSlider != null)
      scaleSlider.interactable = isOwner;

    Debug.Log($"VolumeDataControlUI: Interactable={isOwner} for client {NetworkManager.ClientManager.Connection.ClientId}, IsOwner={IsOwner}, IsServer={IsServer}");
  }

  public override void OnOwnershipClient(NetworkConnection prevOwner) // callback function for when ownership of object changes
  {
    base.OnOwnershipClient(prevOwner); // ensure all default ownersip change functions are called
    UpdateInteractableState();
    if (IsOwner && volumeObject != null)
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
        scale = volumeObject.transform.localScale.x; // Uniform scaling

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
      //if (scaleSlider != null && !scaleSlider.IsInteractable()) // might be overly restrictive TODO test
      if (scaleSlider != null)
        scaleSlider.value = scale;

      yield return new WaitForSeconds(0.1f);
    }
  }

  private void OnPositionIncrementClicked()
  {
    if (CanInteract() == false)
    {
      Debug.LogWarning("Cannot adjust position: Not the owner of the canvas.");
      return;
    }

    if (volumeObject == null)
    {
      Debug.LogError("VolumeRenderedObject not assigned in VolumeDataControlUI.");
      return;
    }

    int axisIndex = 0; // default value
    if (positionAxisDropdown != null)
    {
      axisIndex = positionAxisDropdown.value; // set axisIndex to current axis selected by dropdown
    }
    Vector3 newPos = volumeObject.transform.position;

    switch (axisIndex)
    {
      case 0: // x axis
        newPos.x += positionIncrement;
        newPos.x = (float)Math.Round(newPos.x, 1);
        break;
      case 1: // y axis
        newPos.y += positionIncrement;
        newPos.y = (float)Math.Round(newPos.y, 1);
        break;
      case 2: // z axis
        newPos.z += positionIncrement;
        newPos.z = (float)Math.Round(newPos.z, 1);
        break;
    }

    //volumeObject.transform.position = newPos;
    //Debug.Log($"Set position to {newPos} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetPositionServerRpc(newPos);
  }

  private void OnPositionDecrementClicked()
  {
    if (CanInteract() == false)
    {
      Debug.LogWarning("Cannot adjust position: Not the owner of the canvas.");
      return;
    }

    if (volumeObject == null)
    {
      Debug.LogError("VolumeRenderedObject not assigned in VolumeDataControlUI.");
      return;
    }

    int axisIndex = 0;
    if (positionAxisDropdown != null)
    {
      axisIndex = positionAxisDropdown.value; // set axisIndex to current axis selected by dropdown
    }
    Vector3 newPos = volumeObject.transform.position;

    switch (axisIndex)
    {
      case 0: // x axis
        newPos.x -= positionIncrement;
        newPos.x = (float)Math.Round(newPos.x, 1);
        break;
      case 1: // y axis
        newPos.y -= positionIncrement;
        newPos.y = (float)Math.Round(newPos.y, 1);
        break;
      case 2: // z axis
        newPos.z -= positionIncrement;
        newPos.z = (float)Math.Round(newPos.z, 1);
        break;
    }

    //volumeObject.transform.position = newPos;
    //Debug.Log($"Set position to {newPos} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetPositionServerRpc(newPos);
  }

  private void OnRotationIncrementClicked()
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust rotation: Not the owner of the canvas.");
      return;
    }

    if (volumeObject == null)
    {
      Debug.LogError("VolumeRenderedObject not assigned in VolumeDataControlUI.");
      return;
    }

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
    Debug.Log($"Set rotation to Euler={euler}, Quaternion={newRot} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetRotationServerRpc(newRot); // sync rotation to all clients
  }

  private void OnRotationDecrementClicked()
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust rotation: Not the owner of the canvas.");
      return;
    }

    if (volumeObject == null)
    {
      Debug.LogError("VolumeRenderedObject not assigned in VolumeDataControlUI.");
      return;
    }

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
    Debug.Log($"Set rotation to Euler={euler}, Quaternion={newRot} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetRotationServerRpc(newRot);
  }

  private void OnScaleSliderChanged(float value)
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust scale: Not the owner of the canvas.");
      return;
    }

    if (volumeObject == null)
    {
      Debug.LogError("VolumeRenderedObject not assigned in VolumeDataControlUI.");
      return;
    }

    Vector3 newScale = new Vector3(value, value, value); // Uniform scaling
    //volumeObject.transform.localScale = newScale;
    Debug.Log($"Set scale to {value} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetScaleServerRpc(value);
  }

  private void OnSpawnCrossSectionButtonClicked()
  {
    if (spawnCrossSectionButton != null)
    {
      spawnCrossSectionButton.interactable = false; // disable to prevent double click spawning
      Invoke(nameof(ReenableSpawnButton), 1f); // reenable after 1 second
    }
    var crossSectionManager = FindObjectOfType<CrossSectionManager>();
    if (crossSectionManager == null)
    {
      Debug.LogError("Error: Could not find Cross Section Manager Component.");
      return;
    }
    
    Vector3 spawnPosition = new Vector3(2f, 1.5f, 2f);
    Quaternion spawnRotation = Quaternion.Euler(0f, 0f, 0f);

    crossSectionManager.SpawnCrossSectionPlaneServerRpc(spawnPosition, spawnRotation, NetworkManager.ClientManager.Connection);
    Debug.Log("Requested spawn of CrossSectionPlane.");
  }

  private void ReenableSpawnButton()
  {
    spawnCrossSectionButton.interactable = true;
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
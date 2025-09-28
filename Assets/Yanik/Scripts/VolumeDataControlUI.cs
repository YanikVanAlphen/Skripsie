using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.UI;
using UnityVolumeRendering;
using System.Collections;
using TMPro;
using System.Linq;

public class VolumeDataControlUI : NetworkBehaviour
{
  public VolumeDataNetworker volumeDataNetworker;
  public TMP_Dropdown positionAxisDropdown;
  public Slider positionSlider;
  public TextMeshProUGUI positionXText;
  public TextMeshProUGUI positionYText;
  public TextMeshProUGUI positionZText;
  public TMP_Dropdown rotationAxisDropdown;
  public Slider rotationSlider;
  public TextMeshProUGUI rotationXText;
  public TextMeshProUGUI rotationYText;
  public TextMeshProUGUI rotationZText;
  public Slider scaleSlider;
  public TextMeshProUGUI scaleText;

  private VolumeRenderedObject volumeObject;
  private Vector3 lastRotationSliderValues; // Last slider value for x-, y-, z-axes

  private void Start()
  {
    if (volumeDataNetworker != null)
    {
      StartCoroutine(FindVolumeObject());
    }
    else
    {
      Debug.LogError("VolumeDataNetworker not assigned in VolumeDataControlUI.");
    }

    // UI listeners
    if (positionAxisDropdown != null)
      positionAxisDropdown.onValueChanged.AddListener(OnPositionAxisChanged);
    if (positionSlider != null)
      positionSlider.onValueChanged.AddListener(OnPositionSliderChanged);
    if (rotationAxisDropdown != null)
      rotationAxisDropdown.onValueChanged.AddListener(OnRotationAxisChanged);
    if (rotationSlider != null)
    {
      rotationSlider.onValueChanged.AddListener(OnRotationSliderChanged);
      lastRotationSliderValues = new Vector3(rotationSlider.value, rotationSlider.value, rotationSlider.value); // Init all axes
    }
    if (scaleSlider != null)
    {
      scaleSlider.onValueChanged.AddListener(OnScaleSliderChanged);
    }

    UpdateInteractableState();
    StartCoroutine(UpdateLabels());
  }

  private System.Collections.IEnumerator FindVolumeObject()
  {
    int retryCount = 0;
    const int maxRetries = 60;
    while (volumeObject == null && retryCount < maxRetries)
    {
      if (volumeDataNetworker != null && volumeDataNetworker.volumeRenderedObjectPrefab != null)
      {
        NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
        int expectedPrefabId = volumeDataNetworker.volumeRenderedObjectPrefab.GetComponent<NetworkObject>().PrefabId;
        NetworkObject targetNetObj = networkObjects.FirstOrDefault(nob => nob.PrefabId == expectedPrefabId);
        if (targetNetObj != null)
        {
          volumeObject = targetNetObj.GetComponent<VolumeRenderedObject>();
          if (volumeObject != null)
          {
            Debug.Log($"VolumeDataControlUI: Found volumeObject, ObjectId={targetNetObj.ObjectId}");
          }
        }
      }
      if (volumeObject == null)
      {
        Debug.Log($"VolumeDataControlUI: Waiting for VolumeRenderedObject... Attempt {retryCount + 1}/{maxRetries}");
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }

    if (volumeObject == null)
    {
      Debug.LogError("VolumeDataControlUI: Failed to find VolumeRenderedObject after max retries.");
    }
  }

  public override void OnOwnershipClient(NetworkConnection prevOwner)
  {
    base.OnOwnershipClient(prevOwner);
    UpdateInteractableState();
  }

  private void UpdateInteractableState()
  {
    bool isOwner = false;
    if (volumeObject != null)
    {
      NetworkObject volumeNetworkObject = volumeObject.GetComponent<NetworkObject>();
      if (volumeNetworkObject != null)
      {
        isOwner = volumeNetworkObject.IsOwner || IsServer;
      }
      else
      {
        Debug.LogWarning("VolumeDataControlUI: volumeObject missing NetworkObject component.");
      }
    }
    else
    {
      Debug.LogWarning("VolumeDataControlUI: volumeObject is null, setting UI to non-interactable.");
    }

    if (positionAxisDropdown != null) 
      positionAxisDropdown.interactable = isOwner;
    if (positionSlider != null)
      positionSlider.interactable = isOwner;
    if (rotationAxisDropdown != null) 
      rotationAxisDropdown.interactable = isOwner;
    if (rotationSlider != null) 
      rotationSlider.interactable = isOwner;
    if (scaleSlider != null)
      scaleSlider.interactable = isOwner;

    Debug.Log($"VolumeDataControlUI: Interactable={isOwner} for client {NetworkManager.ClientManager.Connection.ClientId}, IsOwner={isOwner}, IsServer={IsServer}");
  }

  private System.Collections.IEnumerator UpdateLabels()
  {
    while (true)
    {
      float scale = 1.0f; // Default scale when volumeObject is null
      Vector3 pos = Vector3.zero; // Default position
      Vector3 rot = Vector3.zero; // Default rotation

      if (volumeObject != null)
      {
        pos = volumeObject.transform.position;
        rot = volumeObject.transform.rotation.eulerAngles;
        scale = volumeObject.transform.localScale.x; // Update scale if volumeObject exists

        rot.x = NormalizeAngle(rot.x);
        rot.y = NormalizeAngle(rot.y);
        rot.z = NormalizeAngle(rot.z);
      }
      else
      {
        Debug.LogWarning("VolumeDataControlUI: volumeObject is null, skipping label update.");
      }

      // Update position text
      if (positionXText != null)
        positionXText.text = $"X: {pos.x:F2}";
      if (positionYText != null)
        positionYText.text = $"Y: {pos.y:F2}";
      if (positionZText != null)
        positionZText.text = $"Z: {pos.z:F2}";

      // Update rotation text
      if (rotationXText != null)
        rotationXText.text = $"X: {rot.x:F2}";
      if (rotationYText != null)
        rotationYText.text = $"Y: {rot.y:F2}";
      if (rotationZText != null)
        rotationZText.text = $"Z: {rot.z:F2}";

      // Update scale text and slider
      if (scaleText != null)
        scaleText.text = $"Scale: {scale:F2}";
      if (scaleSlider != null && !scaleSlider.IsInteractable())
      {
        scaleSlider.value = scale;
      }

      // Update position slider
      if (positionSlider != null && positionAxisDropdown != null && !positionSlider.IsInteractable())
      {
        switch (positionAxisDropdown.value)
        {
          case 0: // x axis
            positionSlider.value = pos.x;
            break;
          case 1: // y axis
            positionSlider.value = pos.y;
            break;
          case 2: // z axis
            positionSlider.value = pos.z;
            break;
        }
      }
      // Update rotation slider
      if (rotationSlider != null && rotationAxisDropdown != null && !rotationSlider.IsInteractable())
      {
        switch (rotationAxisDropdown.value)
        {
          case 0: // x axis
            rotationSlider.value = rot.x;
            //lastRotationSliderValues.x = rot.x;
            break;
          case 1: // y axis
            rotationSlider.value = rot.y;
            //lastRotationSliderValues.y = rot.y;
            break;
          case 2: // z axis
            rotationSlider.value = rot.z;
            //lastRotationSliderValues.z = rot.z;
            break;
        }
      }

      yield return new WaitForSeconds(0.1f);
    }
  }

  private void OnPositionAxisChanged(int index)
  {
    Debug.Log($"Position axis changed to: {positionAxisDropdown.options[index].text}");
    // Update slider value to match current position for the selected axis
    if (volumeObject != null && positionSlider != null && positionAxisDropdown != null)
    {
      Vector3 pos = volumeObject.transform.position;
      switch (index)
      {
        case 0: // x axis
          positionSlider.value = pos.x;
          break;
        case 1: // y axis
          positionSlider.value = pos.y;
          break;
        case 2: // z axis
          positionSlider.value = pos.z;
          break;
      }
    }
  }

  private void OnRotationAxisChanged(int index)
  {
    Debug.Log($"Rotation axis changed to: {rotationAxisDropdown.options[index].text}");
    // Update slider value to match current rotation for the selected axis
    if (volumeObject != null && rotationSlider != null && rotationAxisDropdown != null)
    {
      Vector3 rot = volumeObject.transform.rotation.eulerAngles;
      rot.x = NormalizeAngle(rot.x);
      rot.y = NormalizeAngle(rot.y);
      rot.z = NormalizeAngle(rot.z);
      switch (index)
      {
        case 0: // x axis
          rotationSlider.value = rot.x;
          lastRotationSliderValues.x = rot.x;
          break;
        case 1: // y axis
          rotationSlider.value = rot.y;
          lastRotationSliderValues.y = rot.y;
          break;
        case 2: // z axis
          rotationSlider.value = rot.z;
          lastRotationSliderValues.z = rot.z;
          break;
      }
    }
  }

  private void OnScaleSliderChanged(float value)
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust scale: Not the owner or server.");
      return;
    }

    // Clamp scale to a reasonable range
    // value = Mathf.Clamp(value, 0.1f, 10f);
    Vector3 newScale = new Vector3(value, value, value); // Uniform scaling

    volumeObject.transform.localScale = newScale;
    Debug.Log($"Set scale to {value} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetScaleServerRpc(value);
  }

  private void OnPositionSliderChanged(float value)
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust position: Not the owner or server.");
      return;
    }

    int axisIndex = positionAxisDropdown != null ? positionAxisDropdown.value : 0;
    Vector3 newPos = volumeObject.transform.position;
    
    // value = Mathf.Clamp(value, -10f, 10f);
    switch (axisIndex)
    {
      case 0: // x axis
        newPos.x = value; 
        break;
      case 1: // y axis
        newPos.y = value; 
        break;
      case 2: // z axis
        newPos.z = value;
        break;
    }

    volumeObject.transform.position = newPos;
    Debug.Log($"Set position to {newPos} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetPositionServerRpc(newPos);
  }

  [ServerRpc(RequireOwnership = false)]
  private void SetPositionServerRpc(Vector3 newPos)
  {
    SetPositionClientRpc(newPos);
  }

  [Client]
  private void SetPositionClientRpc(Vector3 newPos)
  {
    if (volumeObject != null && !CanInteract())
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

  [Client]
  private void SetScaleClientRpc(float scale)
  {
    if (volumeObject != null && !CanInteract())
    {
      volumeObject.transform.localScale = new Vector3(scale, scale, scale);
      Debug.Log($"Client {NetworkManager.ClientManager.Connection.ClientId} updated scale to {scale}.");
    }
  }

  private void OnRotationSliderChanged(float value)
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust rotation: Not the owner or server.");
      return;
    }

    int axisIndex = rotationAxisDropdown != null ? rotationAxisDropdown.value : 0;
    Vector3 rotationAxis;
    float lastValue;
    switch (axisIndex)
    {
      case 0: // x axis
        rotationAxis = Vector3.right;
        lastValue = lastRotationSliderValues.x;
        lastRotationSliderValues.x = value;
        break;
      case 1: // y axis
        rotationAxis = Vector3.up;
        lastValue = lastRotationSliderValues.y;
        lastRotationSliderValues.y = value;
        break;
      case 2: // z axis
        rotationAxis = Vector3.forward;
        lastValue = lastRotationSliderValues.z;
        lastRotationSliderValues.z = value;
        break;
      default:
        rotationAxis = Vector3.up; // fallback to y axis
        lastValue = lastRotationSliderValues.y;
        lastRotationSliderValues.y = value;
        break;
    }

    float deltaDegrees = value - lastValue;
    Quaternion deltaRotation = Quaternion.AngleAxis(deltaDegrees, rotationAxis);
    Quaternion newRot = volumeObject.transform.rotation * deltaRotation;

    volumeObject.transform.rotation = newRot;
    Vector3 euler = newRot.eulerAngles;
    euler.x = NormalizeAngle(euler.x);
    euler.y = NormalizeAngle(euler.y);
    euler.z = NormalizeAngle(euler.z);
    Debug.Log($"Set rotation to Euler={euler}, Quaternion={newRot} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetRotationServerRpc(newRot);
  }

  [ServerRpc(RequireOwnership = false)]
  private void SetRotationServerRpc(Quaternion newRot)
  {
    SetRotationClientRpc(newRot);
  }

  [Client]
  private void SetRotationClientRpc(Quaternion newRot)
  {
    if (volumeObject != null && !CanInteract())
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
    if (volumeObject == null)
    {
      Debug.LogError("VolumeRenderedObject not assigned in VolumeDataControlUI.");
      return false;
    }
    NetworkObject volumeNetworkObject = volumeObject.GetComponent<NetworkObject>();
    return volumeNetworkObject != null && (volumeNetworkObject.IsOwner || IsServer);
  }

  public void SetVolumeDataNetworker(VolumeDataNetworker networker)
  {
    volumeDataNetworker = networker;
  }

  // Normalise angles to [0, 360) for UI display
  private float NormalizeAngle(float angle)
  {
    angle = angle % 360f;
    if (angle < 0f) angle += 360f;
    return angle;
  }
}
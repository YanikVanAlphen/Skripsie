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
  public VolumeDataNetworker volumeDataNetworker;
  public TMP_Dropdown positionAxisDropdown;
  public UnityEngine.UI.Button positionIncrementButton;
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
        Debug.LogError("VolumeDataControlUI: No VolumeDataNetworker found in scene.");
        return;
      }
      Debug.LogWarning("VolumeDataControlUI: VolumeDataNetworker was unassigned, found via FindObjectOfType.");
    }

    StartCoroutine(FindVolumeObject());

    if (positionIncrementButton == null)
      positionIncrementButton = GameObject.Find("position_pos_btn")?.GetComponent<Button>();
    if (positionDecrementButton == null)
      positionDecrementButton = GameObject.Find("position_neg_btn")?.GetComponent<Button>();
    if (rotationIncrementButton == null)
      rotationIncrementButton = GameObject.Find("rot_pos_btn")?.GetComponent<Button>();
    if (rotationDecrementButton == null)
      rotationDecrementButton = GameObject.Find("rot_neg_btn")?.GetComponent<Button>();

    // UI listeners
    if (positionAxisDropdown != null)
      positionAxisDropdown.onValueChanged.AddListener(OnPositionAxisChanged);
    if (positionIncrementButton != null)
      positionIncrementButton.onClick.AddListener(OnPositionIncrementClicked);
    if (positionDecrementButton != null)
      positionDecrementButton.onClick.AddListener(OnPositionDecrementClicked);
    if (rotationAxisDropdown != null)
      rotationAxisDropdown.onValueChanged.AddListener(OnRotationAxisChanged);
    if (rotationIncrementButton != null)
      rotationIncrementButton.onClick.AddListener(OnRotationIncrementClicked);
    if (rotationDecrementButton != null)
      rotationDecrementButton.onClick.AddListener(OnRotationDecrementClicked);
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
    const int maxRetries = 120; // Increased to 60s
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
            Debug.Log($"VolumeDataControlUI: Found volumeObject, ObjectId={targetNetObj.ObjectId}, PrefabId={expectedPrefabId}");
          }
          else
          {
            Debug.LogWarning($"VolumeDataControlUI: NetworkObject found (ObjectId={targetNetObj.ObjectId}, PrefabId={expectedPrefabId}) but missing VolumeRenderedObject component.");
          }
        }
        else
        {
          Debug.Log($"VolumeDataControlUI: Waiting for VolumeRenderedObject... Attempt {retryCount + 1}/{maxRetries}, Expected PrefabId={expectedPrefabId}, Found NetworkObjects={networkObjects.Length}");
        }
      }
      else
      {
        Debug.LogWarning($"VolumeDataControlUI: volumeDataNetworker or volumeRenderedObjectPrefab is null. Networker={volumeDataNetworker != null}, Prefab={(volumeDataNetworker != null ? volumeDataNetworker.volumeRenderedObjectPrefab != null : false)}");
      }

      if (volumeObject == null)
      {
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }

    if (volumeObject == null)
    {
      Debug.LogError($"VolumeDataControlUI: Failed to find VolumeRenderedObject after {maxRetries} retries.");
    }
  }

  public override void OnOwnershipClient(NetworkConnection prevOwner)
  {
    base.OnOwnershipClient(prevOwner);
    UpdateInteractableState();
    if (IsOwner && volumeObject != null)
    {
      NetworkObject volumeNetworkObject = volumeObject.GetComponent<NetworkObject>();
      if (volumeNetworkObject != null && volumeNetworkObject.Owner != Owner)
      {
        volumeNetworkObject.GiveOwnership(Owner);
        Debug.Log($"VolumeDataControlUI: Transferred VolumeRenderedObject ownership to client {Owner.ClientId}, ObjectId={volumeNetworkObject.ObjectId}");
      }
    }
  }

  private void UpdateInteractableState()
  {
    bool isOwner = IsOwner || IsServer;

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

  private System.Collections.IEnumerator UpdateLabels()
  {
    while (true)
    {
      float scale = 1.0f; // Default scale
      Vector3 pos = Vector3.zero; // Default position
      Vector3 rot = Vector3.zero; // Default rotation

      if (volumeObject != null)
      {
        pos = volumeObject.transform.position;
        rot = volumeObject.transform.rotation.eulerAngles;
        scale = volumeObject.transform.localScale.x; // Uniform scaling

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

      // Update scale text
      if (scaleText != null)
        scaleText.text = $"Scale: {scale:F2}";
      if (scaleSlider != null && !scaleSlider.IsInteractable())
      {
        scaleSlider.value = scale;
      }

      yield return new WaitForSeconds(0.1f);
    }
  }

  private void OnPositionAxisChanged(int index)
  {
    Debug.Log($"Position axis changed to: {positionAxisDropdown.options[index].text}");
  }

  private void OnRotationAxisChanged(int index)
  {
    Debug.Log($"Rotation axis changed to: {rotationAxisDropdown.options[index].text}");
  }

  private void OnPositionIncrementClicked()
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust position: Not the owner of the canvas.");
      return;
    }

    if (volumeObject == null)
    {
      Debug.LogError("VolumeRenderedObject not assigned in VolumeDataControlUI.");
      return;
    }

    int axisIndex = positionAxisDropdown != null ? positionAxisDropdown.value : 0;
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

    volumeObject.transform.position = newPos;
    Debug.Log($"Set position to {newPos} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetPositionServerRpc(newPos);
  }

  private void OnPositionDecrementClicked()
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust position: Not the owner of the canvas.");
      return;
    }

    if (volumeObject == null)
    {
      Debug.LogError("VolumeRenderedObject not assigned in VolumeDataControlUI.");
      return;
    }

    int axisIndex = positionAxisDropdown != null ? positionAxisDropdown.value : 0;
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

    volumeObject.transform.position = newPos;
    Debug.Log($"Set position to {newPos} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

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

    int axisIndex = rotationAxisDropdown != null ? rotationAxisDropdown.value : 0;
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
        rotationAxis = Vector3.up; // Fallback
        break;
    }

    Quaternion deltaRotation = Quaternion.AngleAxis(rotationIncrement, rotationAxis);
    Quaternion newRot = volumeObject.transform.rotation * deltaRotation;
    volumeObject.transform.rotation = newRot;

    Vector3 euler = newRot.eulerAngles;
    euler.x = NormalizeAngle(euler.x);
    euler.y = NormalizeAngle(euler.y);
    euler.z = NormalizeAngle(euler.z);
    Debug.Log($"Set rotation to Euler={euler}, Quaternion={newRot} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetRotationServerRpc(newRot);
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

    int axisIndex = rotationAxisDropdown != null ? rotationAxisDropdown.value : 0;
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
        rotationAxis = Vector3.up; // Fallback
        break;
    }

    Quaternion deltaRotation = Quaternion.AngleAxis(-rotationIncrement, rotationAxis);
    Quaternion newRot = volumeObject.transform.rotation * deltaRotation;
    volumeObject.transform.rotation = newRot;

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
    volumeObject.transform.localScale = newScale;
    Debug.Log($"Set scale to {value} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

    SetScaleServerRpc(value);
  }

  [ServerRpc(RequireOwnership = true)]
  private void SetPositionServerRpc(Vector3 newPos)
  {
    SetPositionClientRpc(newPos);
  }

  [Client]
  private void SetPositionClientRpc(Vector3 newPos)
  {
    if (volumeObject != null)
    {
      volumeObject.transform.position = newPos;
      Debug.Log($"Client {NetworkManager.ClientManager.Connection.ClientId} updated position to {newPos}.");
    }
  }

  [ServerRpc(RequireOwnership = true)]
  private void SetScaleServerRpc(float scale)
  {
    SetScaleClientRpc(scale);
  }

  [Client]
  private void SetScaleClientRpc(float scale)
  {
    if (volumeObject != null)
    {
      volumeObject.transform.localScale = new Vector3(scale, scale, scale);
      Debug.Log($"Client {NetworkManager.ClientManager.Connection.ClientId} updated scale to {scale}.");
    }
  }

  [ServerRpc(RequireOwnership = true)]
  private void SetRotationServerRpc(Quaternion newRot)
  {
    SetRotationClientRpc(newRot);
  }

  [Client]
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

  // Normalize angles to [0, 360)
  private float NormalizeAngle(float angle)
  {
    angle = angle % 360f;
    if (angle < 0f) angle += 360f;
    return angle;
  }
}
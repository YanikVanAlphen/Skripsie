using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.UI;
using UnityVolumeRendering;
using System.Collections;
using TMPro;

public class VolumeDataControlUI : NetworkBehaviour
{
  public VolumeDataNetworker volumeDataNetworker;
  public TMP_Dropdown positionAxisDropdown;
  public Button positionPlusButton;
  public Button positionMinusButton;
  public TextMeshProUGUI positionXText;
  public TextMeshProUGUI positionYText;
  public TextMeshProUGUI positionZText;
  public TMP_Dropdown rotationAxisDropdown;
  public Button rotationPlus90Button;
  public Button rotationMinus90Button;
  public TextMeshProUGUI rotationXText;
  public TextMeshProUGUI rotationYText;
  public TextMeshProUGUI rotationZText;

  private VolumeRenderedObject volumeObject;

  private void Start()
  {
    if (volumeDataNetworker != null)
    {
      StartCoroutine(FindVolumeObject());
    }
    else
    {
      Debug.LogError("VolumeDataNetworker not assigned in VolumeControlUI.");
    }

    // Setup UI listeners
    if (positionAxisDropdown != null)
      positionAxisDropdown.onValueChanged.AddListener(OnPositionAxisChanged);
    if (positionPlusButton != null)
      positionPlusButton.onClick.AddListener(() => AdjustPosition(0.1f));
    if (positionMinusButton != null)
      positionMinusButton.onClick.AddListener(() => AdjustPosition(-0.1f));
    if (rotationAxisDropdown != null)
      rotationAxisDropdown.onValueChanged.AddListener(OnRotationAxisChanged);
    if (rotationPlus90Button != null)
      positionPlusButton.onClick.AddListener(() => AdjustRotation(90f));
    if (rotationMinus90Button != null)
      rotationMinus90Button.onClick.AddListener(() => AdjustRotation(-90f));

    UpdateInteractableState();
    StartCoroutine(UpdateLabels());
  }

  private System.Collections.IEnumerator FindVolumeObject()
  {
    int retryCount = 0;
    const int maxRetries = 60;
    while (volumeObject == null && retryCount < maxRetries)
    {
      volumeObject = FindObjectOfType<VolumeRenderedObject>();
      if (volumeObject == null)
      {
        Debug.Log($"VolumeControlUI: Waiting for VolumeRenderedObject... Attempt {retryCount + 1}/{maxRetries}");
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }

    if (volumeObject == null)
    {
      Debug.LogError("VolumeControlUI: Failed to find VolumeRenderedObject after max retries.");
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
    }

    if (positionAxisDropdown != null) positionAxisDropdown.interactable = isOwner;
    if (positionPlusButton != null) positionPlusButton.interactable = isOwner;
    if (positionMinusButton != null) positionMinusButton.interactable = isOwner;
    if (rotationAxisDropdown != null) rotationAxisDropdown.interactable = isOwner;
    if (rotationPlus90Button != null) rotationPlus90Button.interactable = isOwner;
    if (rotationMinus90Button != null) rotationMinus90Button.interactable = isOwner;

    Debug.Log($"VolumeControlUI: Interactable={isOwner} for client {NetworkManager.ClientManager.Connection.ClientId}, IsOwner={isOwner}, IsServer={IsServer}");
  }

  private System.Collections.IEnumerator UpdateLabels()
  {
    while (true)
    {
      if (volumeObject != null)
      {
        Vector3 pos = volumeObject.transform.position;
        Vector3 rot = volumeObject.transform.rotation.eulerAngles;
        if (positionXText != null) positionXText.text = $"X: {pos.x:F2}";
        if (positionYText != null) positionYText.text = $"Y: {pos.y:F2}";
        if (positionZText != null) positionZText.text = $"Z: {pos.z:F2}";
        if (rotationXText != null) rotationXText.text = $"X: {rot.x:F2}";
        if (rotationYText != null) rotationYText.text = $"Y: {rot.y:F2}";
        if (rotationZText != null) rotationZText.text = $"Z: {rot.z:F2}";
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

  private void AdjustPosition(float delta)
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust position: Not the owner or server.");
      return;
    }

    int axisIndex = positionAxisDropdown != null ? positionAxisDropdown.value : 0;
    Vector3 deltaPos = Vector3.zero;
    switch (axisIndex)
    {
      case 0: deltaPos.x = delta; break; // X
      case 1: deltaPos.y = delta; break; // Y
      case 2: deltaPos.z = delta; break; // Z
    }

    Vector3 newPos = volumeObject.transform.position + deltaPos;
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

  private void AdjustRotation(float deltaDegrees)
  {
    if (!CanInteract())
    {
      Debug.LogWarning("Cannot adjust rotation: Not the owner or server.");
      return;
    }

    int axisIndex = rotationAxisDropdown != null ? rotationAxisDropdown.value : 0;
    Vector3 euler = volumeObject.transform.rotation.eulerAngles;
    switch (axisIndex)
    {
      case 0: euler.x += deltaDegrees; break; // X
      case 1: euler.y += deltaDegrees; break; // Y
      case 2: euler.z += deltaDegrees; break; // Z
    }

    Quaternion newRot = Quaternion.Euler(euler);
    volumeObject.transform.rotation = newRot;
    Debug.Log($"Set rotation to {euler} on volume (ObjectId={volumeObject.GetComponent<NetworkObject>().ObjectId}).");

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
      Debug.Log($"Client {NetworkManager.ClientManager.Connection.ClientId} updated rotation to {newRot.eulerAngles}.");
    }
  }

  private bool CanInteract()
  {
    if (volumeObject == null)
    {
      Debug.LogError("VolumeRenderedObject not assigned in VolumeControlUI.");
      return false;
    }
    NetworkObject volumeNetworkObject = volumeObject.GetComponent<NetworkObject>();
    return volumeNetworkObject != null && (volumeNetworkObject.IsOwner || IsServer);
  }

  public void SetVolumeDataNetworker(VolumeDataNetworker networker)
  {
    volumeDataNetworker = networker;
  }
}
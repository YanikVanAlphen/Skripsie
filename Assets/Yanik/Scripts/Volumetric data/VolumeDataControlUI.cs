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

  public Slider minVisibilitySlider;
  public Slider maxVisibilitySlider;
  // ---------------------------------------

  private VolumeRenderedObject volumeObject;
  private const float positionIncrement = 0.05f; // Small increment for position
  private const float rotationIncrement = 5f;   // Small increment for rotation

  private void Start()
  {
    // set initial visibility ranges on sliders
    maxVisibilitySlider.value = 1f;
    minVisibilitySlider.value = 0f;

    if (volumeDataNetworker == null)
    {
      // runtime assignment of VolumeDataNetworker component since the data menu spawns at runtime
      volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();
    }
    StartCoroutine(FindVolumeObject());
    SetupUI();
    UpdateInteractableState();
    // continually update menu labels
    StartCoroutine(UpdateLabels());
  }

  private IEnumerator FindVolumeObject()
  {
    if (volumeDataNetworker != null && volumeDataNetworker.volumeRenderedObjectPrefab != null)
    {
      NetworkObject networkObject = volumeDataNetworker.volumeRenderedObjectPrefab.GetComponent<NetworkObject>();
      var result = VolumeRenderObjectFindUtility.FindVolumeObject("VolumeDataControlUI", networkObject);
      yield return result;
      volumeObject = result.Current as VolumeRenderedObject;
    }
  }

  private void SetupUI()
  {
    // add listener methods to activate on UI pressed or changed event handlers
    // buttons
    positionIncrementButton.onClick.AddListener(OnPositionIncrementClicked);
    positionDecrementButton.onClick.AddListener(OnPositionDecrementClicked);
    rotationIncrementButton.onClick.AddListener(OnRotationIncrementClicked);
    rotationDecrementButton.onClick.AddListener(OnRotationDecrementClicked);
    spawnCrossSectionButton.onClick.AddListener(OnSpawnCrossSectionButtonClicked);
    // sliders
    scaleSlider.onValueChanged.AddListener(OnScaleSliderChanged);
    minVisibilitySlider.onValueChanged.AddListener(OnVisibilitySlidersChanged);
    maxVisibilitySlider.onValueChanged.AddListener(OnVisibilitySlidersChanged); 
  }

  private void UpdateInteractableState()
  {
    bool isOwner = IsOwner || IsServer; // IsOwner and IsServer provided directly by FishNet - indicates ownership
    // allow or block anything who isnt owner of canvas from editing
    // buttons
    positionIncrementButton.interactable = isOwner;
    positionDecrementButton.interactable = isOwner;
    rotationIncrementButton.interactable = isOwner;
    rotationDecrementButton.interactable = isOwner;
    // dropdowns
    positionAxisDropdown.interactable = isOwner;
    rotationAxisDropdown.interactable = isOwner;
    // sliders
    scaleSlider.interactable = isOwner;   
    maxVisibilitySlider.interactable = isOwner;
    minVisibilitySlider.interactable = isOwner;
  }

  public override void OnOwnershipClient(NetworkConnection prevOwner) // callback function for when ownership of object changes
  {
    base.OnOwnershipClient(prevOwner); // ensure all default ownersip change functions are called
    UpdateInteractableState(); // update interactible state so that only new owner can interact
    if (IsOwner && volumeObject != null)
    {
      NetworkObject volumeNetworkObject = volumeObject.GetComponent<NetworkObject>();
      if (volumeNetworkObject != null && volumeNetworkObject.Owner != Owner) // change in ownership
      {
        volumeNetworkObject.GiveOwnership(Owner);
        Debug.Log($"Transferred VolumeRenderedObject ownership to client {Owner.ClientId}.");
      }
    }
  }

  private IEnumerator UpdateLabels()
  {
    // default params
    float scale = 1.0f;
    Vector3 pos = Vector3.zero;
    Vector3 rot = Vector3.zero;

    while (true)
    {
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

      yield return new WaitForSeconds(0.1f); // a bit brute force but doesnt seem to have too much effect
    }
  }

  private void OnPositionIncrementClicked()
  {
    if (!CanInteract() || volumeObject == null)
      return;

    int axisIndex = 0; // default value
    if (positionAxisDropdown != null)
      axisIndex = positionAxisDropdown.value; // set axisIndex to current axis selected by dropdown
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

    SetPositionServerRpc(newPos);
  }

  private void OnPositionDecrementClicked()
  {
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

    SetPositionServerRpc(newPos);
  }

  private void OnRotationIncrementClicked()
  {
    if (!CanInteract() || volumeObject == null)
      return;

    int axisIndex = 0;
    if (rotationAxisDropdown != null)
    {
      axisIndex = rotationAxisDropdown.value;
    }
    Vector3 rotationAxis = new Vector3(0f, 0f, 0f);

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
    }

    Quaternion deltaRotation = Quaternion.AngleAxis(rotationIncrement, rotationAxis);
    Quaternion newRot = volumeObject.transform.rotation * deltaRotation; // apply change in rotation relative to current rotation

    SetRotationServerRpc(newRot); // sync rotation to all clients
  }

  private void OnRotationDecrementClicked()
  {
    if (!CanInteract() || volumeObject == null)
      return;

    int axisIndex = 0;
    if (rotationAxisDropdown != null)
    {
      axisIndex = rotationAxisDropdown.value;
    }
    Vector3 rotationAxis = new Vector3(0f, 0f, 0f);

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
    }

    Quaternion deltaRotation = Quaternion.AngleAxis(-rotationIncrement, rotationAxis);
    Quaternion newRot = volumeObject.transform.rotation * deltaRotation;

    SetRotationServerRpc(newRot);
  }

  private void OnScaleSliderChanged(float value)
  {
    if (!CanInteract() || volumeObject == null)
      return;

    Vector3 newScale = new Vector3(value, value, value); // uniform scaling
    //volumeObject.transform.localScale = newScale;
    Debug.Log($"Set volumetric data scale to {newScale}.");

    SetScaleServerRpc(value);
  }

  private void OnVisibilitySlidersChanged(float value)
  {
    if (!CanInteract() || volumeObject == null)
      return;

    float maxValue = maxVisibilitySlider.value;
    float minValue = minVisibilitySlider.value;
    if (maxValue <= minValue)
    {
      maxValue = minValue;
      maxVisibilitySlider.value = maxValue;
      minVisibilitySlider.value = minValue;
    }
    SetVisibilityWindowServerRpc(minValue, maxValue);
  }

  private void OnSpawnCrossSectionButtonClicked()
  {
    if (volumeObject == null)
      return;

    Debug.Log("Requested spawn of CrossSectionPlane.");

    if (spawnCrossSectionButton != null)
      spawnCrossSectionButton.interactable = false; // disable to prevent double-click spawning

    var crossSectionManager = FindObjectOfType<CrossSectionManager>();
    // default spawn for cross section
    Vector3 spawnPosition = new Vector3(0f, 1.5f, 0f);
    Quaternion spawnRotation = Quaternion.Euler(0f, 0f, 0f);

    crossSectionManager.SpawnCrossSectionPlaneServerRpc(spawnPosition, spawnRotation, NetworkManager.ClientManager.Connection);
  }

  [ServerRpc(RequireOwnership = true)] // changed from 'false' since ownership IS required
  private void SetPositionServerRpc(Vector3 newPos) // make sure change is approved by server before clients see the update
  {
    SetPositionClientRpc(newPos);
  }

  [ObserversRpc]
  private void SetPositionClientRpc(Vector3 newPos) // runs on clients to execute actual change in client's local scene -> synched with server state
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

  [ObserversRpc]
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

  [ServerRpc(RequireOwnership = true)]
  private void SetVisibilityWindowServerRpc(float min, float max)
  {
    SetVisibilityWindowClientRpc(min, max);
  }

  [ObserversRpc]
  private void SetVisibilityWindowClientRpc(float min, float max)
  {
    if (volumeObject != null)
    {
      volumeObject.SetVisibilityWindow(min, max);
      if (!IsOwner)
      {
        // update slider positions for other clients
        maxVisibilitySlider.value = max;
        minVisibilitySlider.value = min;
      }

      Debug.Log($"Client {NetworkManager.ClientManager.Connection.ClientId} updated visibility window to min={min}, max={max}.");
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
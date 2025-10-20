using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Object;

public class ActivateRightHandLaserPointer : MonoBehaviour
{
  public GameObject RightPointerRay; // pointer ray object on the right controller
  public GameObject RightUIRay;
  public InputActionProperty rightAButtonAction; // InputAction property for the "A" button
  private RayInteractorLengthAdjustment rayInteractorLengthAdjustment;

  private void Awake()
  {
    rayInteractorLengthAdjustment = RightPointerRay.GetComponent<RayInteractorLengthAdjustment>();
  }

  private void OnEnable()
  {
    rightAButtonAction.action.Enable();
    rightAButtonAction.action.performed += OnAButtonPressed; // subscribe to event
  }

  private void OnDisable()
  {
    rightAButtonAction.action.performed -= OnAButtonPressed; // unsub from event
    rightAButtonAction.action.Disable();
  }

  private void OnAButtonPressed(InputAction.CallbackContext context)
  {
    Debug.Log("toggle");
    if (rayInteractorLengthAdjustment == null || !rayInteractorLengthAdjustment.IsNetworkObjectInit())
      return;

    // Toggle the pointer ray on/off each press
    bool isPointerActive = !RightPointerRay.activeSelf;
    rayInteractorLengthAdjustment.ToggleRayActiveServerRpc(isPointerActive);

    // deactivate right hand UI interaction when pointer is active
    RightUIRay.SetActive(!isPointerActive);
  }
}

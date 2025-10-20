using UnityEngine;
using UnityEngine.InputSystem;

public class ActivateRightHandLaserPointer : MonoBehaviour
{
  public GameObject RightPointerRay;            // pointer ray object on the right controller
  public GameObject RightUIRay;
  public InputActionProperty rightAButtonAction; // InputAction property for the "A" button

  private bool isPointerActive = false;

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
    // Toggle the pointer ray on/off each press
    isPointerActive = !isPointerActive;
    RightPointerRay.SetActive(isPointerActive);
    // deactivate right hand UI interaction when pointer is active
    RightUIRay.SetActive(!isPointerActive);
  }
}

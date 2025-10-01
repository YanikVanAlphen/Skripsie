using UnityEngine;
using UnityEngine.InputSystem;

public class ActivateTeleportationRay : MonoBehaviour
{
  public GameObject leftTeleportRay;            // Teleport ray object on the left controller
  public InputActionProperty leftXButtonAction; // InputAction property for the "X" button

  private bool isTeleportActive = false;

  private void OnEnable()
  {
    leftXButtonAction.action.Enable();
    leftXButtonAction.action.performed += OnXButtonPressed;
  }

  private void OnDisable()
  {
    leftXButtonAction.action.performed -= OnXButtonPressed;
    leftXButtonAction.action.Disable();
  }

  private void OnXButtonPressed(InputAction.CallbackContext context)
  {
    // Toggle the teleport ray on/off each press
    isTeleportActive = !isTeleportActive;
    leftTeleportRay.SetActive(isTeleportActive);
  }
}

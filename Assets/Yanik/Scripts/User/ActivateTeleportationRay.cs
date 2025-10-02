using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Adapted from Valem Tutorials (https://www.youtube.com/watch?v=0xt6dACM_1I&list=PLpEoiloH-4eP-OKItF8XNJ8y8e1asOJud&index=6)
/// </summary>
public class ActivateTeleportationRay : MonoBehaviour
{
  public GameObject leftTeleportRay;            // Teleport ray object on the left controller
  public InputActionProperty leftXButtonAction; // InputAction property for the "X" button

  private bool isTeleportActive = false;

  private void OnEnable()
  {
    leftXButtonAction.action.Enable();
    leftXButtonAction.action.performed += OnXButtonPressed; // subscribe to event
  }

  private void OnDisable()
  {
    leftXButtonAction.action.performed -= OnXButtonPressed; // unsub from event
    leftXButtonAction.action.Disable();
  }

  private void OnXButtonPressed(InputAction.CallbackContext context)
  {
    // Toggle the teleport ray on/off each press
    isTeleportActive = !isTeleportActive;
    leftTeleportRay.SetActive(isTeleportActive);
  }
}

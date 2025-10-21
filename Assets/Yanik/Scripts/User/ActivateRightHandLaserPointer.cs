using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Object;
using System.Collections;
using FishNet.Connection;
using FishNet.Component.Transforming;
using FishNet.Managing.Server;
using FishNet.Managing.Object;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using FishNet;

public class ActivateRightHandLaserPointer : MonoBehaviour
{
  public GameObject RightUIRay;
  public InputActionProperty rightAButtonAction;
  private bool isPointerActive = false;
  private RayInteractorLengthAdjustment rayInteractorLengthAdjustment;

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
    if (rayInteractorLengthAdjustment == null)
    {
      StartCoroutine(FindRayInteractorLengthAdjustment());
      return;
    }
    // Toggle the pointer ray on/off each press
    isPointerActive = !isPointerActive;
    rayInteractorLengthAdjustment.ToggleRayActiveServerRpc(isPointerActive);
    // deactivate right hand UI interaction when pointer is active
    RightUIRay.SetActive(!isPointerActive);
  }

  private IEnumerator FindRayInteractorLengthAdjustment()
  {
    int retryCount = 0;
    const int maxRetries = 100;
    while (rayInteractorLengthAdjustment == null && retryCount < maxRetries)
    {
      RayInteractorLengthAdjustment[] rayArray = FindObjectsOfType<RayInteractorLengthAdjustment>();
      foreach (var rayAdj in rayArray)
      {
        if (rayAdj.GetComponent<NetworkObject>().Owner == InstanceFinder.NetworkManager.ClientManager.Connection)
        {
          rayInteractorLengthAdjustment = rayAdj;
          yield break;
        }
      }
      retryCount++;
      yield return new WaitForSeconds(0.2f);
    }
    if (rayInteractorLengthAdjustment == null)
    {
      Debug.LogError("Failed to find RayInteractorLengthAdjustment");
    }
  }
}

using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;
using uMuVR;

/// <summary>
/// Adapted from tutorial on hand animations from Valem Tutorials (https://www.youtube.com/watch?v=8PCNNro7Rt0&list=PLpEoiloH-4eP-OKItF8XNJ8y8e1asOJud&index=3) with extension to networked hand animations.
/// </summary>
public class AnimateHandOnInput : NetworkBehaviour
{
  public InputActionProperty pinchAnimationAction;
  public InputActionProperty gripAnimationAction;
  public Animator handAnimator;

  [SyncVar(OnChange = nameof(OnTriggerChanged))] // register callback
  private float triggerValue;

  [SyncVar(OnChange = nameof(OnGripChanged))] // register callback
  private float gripValue;

  private void OnTriggerChanged(float oldValue, float newValue, bool asServer)
  {
    handAnimator.SetFloat("Trigger", newValue);
  }

  private void OnGripChanged(float oldValue, float newValue, bool asServer)
  {
    handAnimator.SetFloat("Grip", newValue);
  }

  private void Start()
  {
    handAnimator = GetComponent<Animator>();
  }

  private void Update()
  {
    if (IsOwner)
    {
      float newTriggerValue = pinchAnimationAction.action.ReadValue<float>();
      float newGripValue = gripAnimationAction.action.ReadValue<float>();

      if (newTriggerValue != triggerValue)
      {
        triggerValue = newTriggerValue;
        UpdateTrigger(newTriggerValue);
      }
      if (newGripValue != gripValue)
      {
        gripValue = newGripValue;
        UpdateGrip(newGripValue);
      }
    }
  }

  // update owner's trigger+grip syncvars across the server
  [ServerRpc(RequireOwnership = true)]
  private void UpdateTrigger(float value)
  {
    triggerValue = value;
  }

  [ServerRpc(RequireOwnership = true)]
  private void UpdateGrip(float value)
  {
    gripValue = value;
  }
}

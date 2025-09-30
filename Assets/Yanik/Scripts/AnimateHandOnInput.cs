using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;
using uMuVR;

public class AnimateHandOnInput : NetworkBehaviour
{
  public InputActionProperty pinchAnimationAction;
  public InputActionProperty gripAnimationAction;
  public Animator handAnimator;

  [SyncVar(OnChange = nameof(OnTriggerChanged))]
  private float triggerValue;
  [SyncVar(OnChange = nameof(OnGripChanged))]
  private float gripValue;

  private void OnTriggerChanged(float oldValue, float newValue, bool asServer)
  {
    if (handAnimator != null)
    {
      handAnimator.SetFloat("Trigger", newValue);
    }
  }

  private void OnGripChanged(float oldValue, float newValue, bool asServer)
  {
    if (handAnimator != null)
    {
      handAnimator.SetFloat("Grip", newValue);
    }
  }

  private void Start()
  {
    if (handAnimator == null)
    {
      handAnimator = GetComponent<Animator>();
      if (handAnimator == null)
      {
        Debug.LogError($"No Animator found on {gameObject.name}");
      }
    }
  }

  private void Update()
  {
    if (IsOwner && handAnimator != null)
    {
      float newTriggerValue = pinchAnimationAction.action?.ReadValue<float>() ?? 0f;
      float newGripValue = gripAnimationAction.action?.ReadValue<float>() ?? 0f;

      if (newTriggerValue != triggerValue)
      {
        triggerValue = newTriggerValue;
        CmdUpdateTrigger(newTriggerValue);
      }
      if (newGripValue != gripValue)
      {
        gripValue = newGripValue;
        CmdUpdateGrip(newGripValue);
      }
    }
  }

  [ServerRpc(RequireOwnership = true)]
  private void CmdUpdateTrigger(float value)
  {
    triggerValue = value;
  }

  [ServerRpc(RequireOwnership = true)]
  private void CmdUpdateGrip(float value)
  {
    gripValue = value;
  }
}

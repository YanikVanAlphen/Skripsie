using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using TriInspector;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
// MY EDIT
using System.Linq;
using FishNet.Managing.Timing; // Added for TimeManager.Tick
//
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace uMuVR
{

  /// <summary>
  /// Component that transfers ownership of this object to another user
  /// </summary>
  public class OwnershipManager : uMuVR.Enhanced.NetworkBehaviour
  {
    [PropertyTooltip("Enable changing ownership when a user interacts with this object.")]
    public bool enableInteractionTransfer = true;
    [PropertyTooltip("Enable changing ownership when this object enters an ownership volume that belongs to a user.")]
    public bool enableVolumeTransfer = true;
    [PropertyTooltip("Should the owner of this object return it to the scene before leaving the game?")]
    public bool releaseOwnershipOnLeave = true;

    [PropertyTooltip("XR Interactable that is interacted with to trigger interactions")]
    [ShowIf(nameof(enableInteractionTransfer)), PropertyOrder(1)]
    public XRBaseInteractable XRIinteractable = null;
    [ShowIf(nameof(enableInteractionTransfer)), PropertyOrder(2)]
    public UxrGrabbableObject UXRinteractable = null;
    [PropertyTooltip("Number of ticks to wait before an ownership transfer can occur again")]
    public uint ownershipTransferCooldown = 10;

    [PropertyTooltip("Reference to VolumeDataNetworker to find the canvas")]
    [ShowIf(nameof(enableInteractionTransfer)), PropertyOrder(3)]
    public VolumeDataNetworker volumeDataNetworker = null;
    private uint lastCanvasTransferTick = 0;
    private uint lastOwnershipRequestTick = 0;
    private const uint OWNERSHIP_RETRY_INTERVAL = 10; // Ticks
    private const uint MAX_RETRIES = 3;
    private uint ownershipRetryCount = 0;

    /// <summary>
    /// Counter tracking how many controllers are actively selecting us
    /// </summary>
    private uint selectionCount = 0;
    /// <summary>
    /// Property indicating if we are actively selected
    /// </summary>
    private bool isSelected => selectionCount > 0;

    /// <summary>
    /// When this object is spawned on the client, add it as a listener to the interaction's interactions
    /// </summary>
    public override void OnStartClient()
    {
      base.OnStartClient();

      if (enableInteractionTransfer)
      {
        if (XRIinteractable != null)
        {
          XRIinteractable.selectEntered.AddListener(OnXRIInteractableSelected);
          XRIinteractable.selectExited.AddListener(OnXRIInteractableUnselected);
        }

        if (UXRinteractable != null)
        {
          UXRinteractable.Grabbing += OnUxrInteractableSelected;
          UXRinteractable.Released += OnUxrInteractableUnselected;
          UXRinteractable.Placed += OnUxrInteractableUnselected;
        }
      }
    }

    /// <summary>
    /// When this object is destroyed on the client, remove it as an interaction listener
    /// </summary>
    public override void OnStopClient()
    {
      base.OnStopClient();

      if (XRIinteractable != null)
      {
        XRIinteractable.selectEntered.RemoveListener(OnXRIInteractableSelected);
        XRIinteractable.selectExited.RemoveListener(OnXRIInteractableUnselected);
      }

      if (UXRinteractable != null)
      {
        UXRinteractable.Grabbing -= OnUxrInteractableSelected;
        UXRinteractable.Released -= OnUxrInteractableUnselected;
        UXRinteractable.Placed -= OnUxrInteractableUnselected;
      }
    }

    /// <summary>
    /// Un/Register the listener which returns control of the object to scene when its owner leaves
    /// </summary>
    public override void OnStartServer()
    {
      base.OnStartServer();
      ServerManager.Objects.OnPreDestroyClientObjects += OnPreDestroyClientObjects;
    }
    public override void OnStopServer()
    {
      base.OnStopServer();
      ServerManager.Objects.OnPreDestroyClientObjects -= OnPreDestroyClientObjects;
    }

    /// <summary>
    /// When the owner of this object leaves, return control of it to the scene
    /// </summary>
    public void OnPreDestroyClientObjects(NetworkConnection leaving)
    {
      if (leaving != Owner) return;

      if (releaseOwnershipOnLeave)
      {
        GiveOwnership(null);
        if (volumeDataNetworker != null)
        {
          NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
          int expectedPrefabId = volumeDataNetworker.volumeControlCanvasPrefab?.GetComponent<NetworkObject>()?.PrefabId ?? -1;
          NetworkObject canvasNetworkObject = null;
          if (expectedPrefabId != -1)
          {
            canvasNetworkObject = networkObjects.FirstOrDefault(nob => nob.PrefabId == expectedPrefabId && nob.GetComponent<VolumeDataControlUI>() != null);
          }

          if (canvasNetworkObject != null)
          {
            canvasNetworkObject.GiveOwnership(null);
            Debug.Log($"OwnershipManager: Released canvas ownership to scene for client {leaving.ClientId}, ObjectId={canvasNetworkObject.ObjectId}");
          }
          else
          {
            Debug.LogWarning($"OwnershipManager: Failed to find canvas to release ownership for client {leaving.ClientId}, PrefabId={expectedPrefabId}");
          }
        }
      }
    }

    /// <summary>
    /// Automatically add the attached GrabInteractable
    /// </summary>
    protected override void OnValidate()
    {
      base.OnValidate();

      if (enableInteractionTransfer && XRIinteractable == null)
        XRIinteractable = GetComponent<XRBaseInteractable>();
      if (enableInteractionTransfer && UXRinteractable == null)
        UXRinteractable = GetComponent<UxrGrabbableObject>();
      if (enableInteractionTransfer && volumeDataNetworker == null)
        volumeDataNetworker = FindObjectOfType<VolumeDataNetworker>();
    }

    /// <summary>
    /// When this object is interacted with (only called if interaction transfers are enabled), give it to the interaction's owner
    /// </summary>
    protected void OnXRIInteractableSelected(SelectEnterEventArgs e)
    {
      var no = e.interactorObject.transform.GetComponentInParent<NetworkObject>();
      if (no == null) return;

      if (NetworkManager.TimeManager.Tick < lastOwnershipRequestTick + ownershipTransferCooldown)
      {
        Debug.Log($"OwnershipManager: Ownership request skipped due to cooldown for {gameObject.name} (ObjectId={NetworkObject.ObjectId}).");
        return;
      }

      Debug.Log($"OwnershipManager: XR interaction by client {no.Owner.ClientId}, requesting ownership of {gameObject.name} (ObjectId={NetworkObject.ObjectId}).");
      if (!NetworkObject.IsOwner)
      {
        RequestOwnershipServerRpc(no.Owner.ClientId);
        lastOwnershipRequestTick = NetworkManager.TimeManager.Tick;
        ownershipRetryCount = 0;
        InvokeRepeating(nameof(CheckOwnership), 0f, (float)(NetworkManager.TimeManager.TickDelta * OWNERSHIP_RETRY_INTERVAL));
      }
      else
      {
        Debug.Log($"OwnershipManager: Already owner (ClientId={no.Owner.ClientId}) for {gameObject.name}.");
      }
      //TransferCanvasOwnership(no.Owner);
      selectionCount++;
      LogNetworkTransformState();
    }

    protected void OnUxrInteractableSelected(object sender, UxrManipulationEventArgs args)
    {
      var no = args.Grabber.transform.GetComponentInParent<NetworkObject>();
      if (no == null) return;

      if (NetworkManager.TimeManager.Tick < lastOwnershipRequestTick + ownershipTransferCooldown)
      {
        Debug.Log($"OwnershipManager: Ownership request skipped due to cooldown for {gameObject.name} (ObjectId={NetworkObject.ObjectId}).");
        return;
      }

      Debug.Log($"OwnershipManager: UXR interaction by client {no.Owner.ClientId}, requesting ownership of {gameObject.name} (ObjectId={NetworkObject.ObjectId}).");
      if (!NetworkObject.IsOwner)
      {
        RequestOwnershipServerRpc(no.Owner.ClientId);
        lastOwnershipRequestTick = NetworkManager.TimeManager.Tick;
        ownershipRetryCount = 0;
        InvokeRepeating(nameof(CheckOwnership), 0f, (float)(NetworkManager.TimeManager.TickDelta * OWNERSHIP_RETRY_INTERVAL));
      }
      else
      {
        Debug.Log($"OwnershipManager: Already owner (ClientId={no.Owner.ClientId}) for {gameObject.name}.");
      }
      //TransferCanvasOwnership(no.Owner);
      selectionCount++;
      LogNetworkTransformState();
    }

    /// <summary>
    /// When interaction with this object ceases, decrement the number of selections
    /// </summary>
    protected void OnXRIInteractableUnselected(SelectExitEventArgs e)
    {
      selectionCount--;
    }

    protected void OnUxrInteractableUnselected(object sender, UxrManipulationEventArgs args)
    {
      selectionCount--;
    }

    /// <summary>
    /// When this object enters an Ownership Volume (only called if volume transfers are enabled), give it to the volume's owner
    /// </summary>
    protected void OnTriggerStay(Collider other)
    {
      if (!enableVolumeTransfer) return;

      var ov = other.GetComponent<OwnershipVolume>();
      if (ov == null) return;
      if (ov.volumeOwner == Owner) return;

      if (isSelected) return;

      if (ov.volumeOwner != null)
        GiveOwnershipWithCooldown(ov.volumeOwner, ownershipTransferCooldown, true);

      ov.RegisterAsListener(this);
    }

    private void TransferCanvasOwnership(NetworkConnection newOwner)
    {
      if (volumeDataNetworker == null)
      {
        Debug.LogWarning("OwnershipManager: volumeDataNetworker is null, cannot transfer canvas ownership.");
        return;
      }

      if (NetworkManager.TimeManager.Tick < lastCanvasTransferTick + ownershipTransferCooldown)
      {
        Debug.Log($"OwnershipManager: Canvas ownership transfer skipped due to cooldown for client {newOwner?.ClientId ?? -1}.");
        return;
      }

      NetworkObject canvasNetworkObject = null;
      int expectedPrefabId = volumeDataNetworker.volumeControlCanvasPrefab?.GetComponent<NetworkObject>()?.PrefabId ?? -1;
      if (expectedPrefabId != -1)
      {
        NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
        canvasNetworkObject = networkObjects.FirstOrDefault(nob => nob.PrefabId == expectedPrefabId && nob.GetComponent<VolumeDataControlUI>() != null);
      }

      if (canvasNetworkObject == null)
      {
        GameObject canvasGO = GameObject.FindWithTag("VolumeControlCanvas");
        if (canvasGO != null)
        {
          canvasNetworkObject = canvasGO.GetComponent<NetworkObject>();
        }
      }

      if (canvasNetworkObject == null)
      {
        Debug.LogWarning($"OwnershipManager: Failed to find instantiated canvas with PrefabId={expectedPrefabId} or tag 'VolumeControlCanvas'.");
        return;
      }

      if (canvasNetworkObject.Owner != newOwner)
      {
        RequestCanvasOwnershipServerRpc(canvasNetworkObject.ObjectId, newOwner);
        lastCanvasTransferTick = NetworkManager.TimeManager.Tick;
        Debug.Log($"OwnershipManager: Initiating canvas ownership transfer to client {newOwner?.ClientId ?? -1}.");
      }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCanvasOwnershipServerRpc(int canvasObjectId, NetworkConnection newOwner)
    {
      NetworkObject canvasNetworkObject = null;
      foreach (var nob in FindObjectsOfType<NetworkObject>())
      {
        if (nob.ObjectId == canvasObjectId)
        {
          canvasNetworkObject = nob;
          break;
        }
      }

      if (canvasNetworkObject != null && canvasNetworkObject.Owner != newOwner)
      {
        canvasNetworkObject.GiveOwnership(newOwner);
        Debug.Log($"OwnershipManager: Server granted canvas ownership of ObjectId={canvasObjectId} to client {newOwner?.ClientId ?? -1}.");
      }
      else
      {
        Debug.LogWarning($"OwnershipManager: Canvas ownership transfer failed for ObjectId={canvasObjectId}. Object {(canvasNetworkObject == null ? "not found" : "already owned by client " + newOwner?.ClientId)}.");
      }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOwnershipServerRpc(int clientId)
    {
      NetworkConnection requester = NetworkManager.ServerManager.Clients[clientId];
      if (requester == null)
      {
        Debug.LogWarning($"OwnershipManager: Ownership request failed for {gameObject.name} (ObjectId={NetworkObject.ObjectId}). Client {clientId} not found.");
        return;
      }

      Debug.Log($"RequestOwnershipServerRpc: Called by client {clientId} for {gameObject.name} (ObjectId={NetworkObject.ObjectId})");
      if (NetworkObject.Owner == requester)
      {
        NetworkObject.GiveOwnership(requester);
        Debug.Log($"OwnershipManager: Server granted ownership of {gameObject.name} (ObjectId={NetworkObject.ObjectId}) to client {clientId}.");
      }
      else
      {
        Debug.Log($"OwnershipManager: Ownership request ignored for {gameObject.name} (ObjectId={NetworkObject.ObjectId}). Already owned by client {NetworkObject.Owner.ClientId}.");
      }
    }

    private void CheckOwnership()
    {
      if (NetworkObject.IsOwner)
      {
        Debug.Log($"OwnershipManager: Ownership confirmed for {gameObject.name} (ObjectId={NetworkObject.ObjectId}) by client {NetworkObject.Owner.ClientId}.");
        CancelInvoke(nameof(CheckOwnership));
        
        var no = GetComponent<NetworkObject>();
        if (no != null && no.Owner != null)
        {
          TransferCanvasOwnership(no.Owner);
        }
        return;
      }
      
      if (NetworkManager.TimeManager.Tick < lastOwnershipRequestTick + OWNERSHIP_RETRY_INTERVAL)
        return;

      if (ownershipRetryCount >= MAX_RETRIES)
      {
        Debug.LogError($"OwnershipManager: Failed to gain ownership of {gameObject.name} (ObjectId={NetworkObject.ObjectId}) after {MAX_RETRIES} retries. Check network issues (e.g., VoiceNetwork packet errors).");
        CancelInvoke(nameof(CheckOwnership));
        return;
      }

      Debug.Log($"OwnershipManager: Retrying ownership request for {gameObject.name} (ObjectId={NetworkObject.ObjectId}), attempt {ownershipRetryCount + 1}.");
      RequestOwnershipServerRpc(NetworkManager.ClientManager.Connection.ClientId);
      lastOwnershipRequestTick = NetworkManager.TimeManager.Tick;
      ownershipRetryCount++;
    }

    private void LogNetworkTransformState()
    {
      NetworkTransform nt = GetComponent<NetworkTransform>();
      if (nt != null)
      {
        Debug.Log($"OwnershipManager: NetworkTransform sync state - Position: {nt.transform.position}, Rotation: {nt.transform.rotation.eulerAngles}, Scale: {nt.transform.localScale}, IsOwner: {NetworkObject.IsOwner}");
      }
      else
      {
        Debug.LogError($"OwnershipManager: NetworkTransform missing on {gameObject.name}!");
      }
    }

#if UNITY_EDITOR
        [MenuItem("CONTEXT/UxrGrabbableObject/Make Networked")]
        [MenuItem("CONTEXT/OwnershipManager/Setup Object Networking")]
        public static void SetupObjectNetworking(MenuCommand command) {
            var go = (Component)command.context;
        
            var no = go.GetComponent<NetworkObject>();
            var om = go.GetComponent<OwnershipManager>();
            var nt = go.GetComponent<NetworkTransform>();
            var rb = go.GetComponent<Rigidbody>();
        
            no ??= go.gameObject.AddComponent<NetworkObject>();
            om ??= go.gameObject.AddComponent<OwnershipManager>();
            nt ??= go.gameObject.AddComponent<NetworkTransform>();
        }
#endif
  }
}
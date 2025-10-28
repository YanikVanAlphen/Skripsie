using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using TriInspector;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

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
    // ref to VolumeDataNetworker for access to canvas prefab for ownership transfer when a client interacts with the volumedata
    public VolumeDataNetworker volumeDataNetworker = null;

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
        // find canvas networkobject and then release ownership when owner leaves similar to how MuVR implements it by assigning ownership to null
        NetworkObject canvasNetworkObject = FindCanvasNetworkObject();
        if (canvasNetworkObject != null)
          canvasNetworkObject.GiveOwnership(null);
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
      // assign VolumeDataNetworker in the scene if not already set
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

      GiveOwnershipWithCooldown(no.Owner, ownershipTransferCooldown, true);
      selectionCount++;
    }

    protected void OnUxrInteractableSelected(object sender, UxrManipulationEventArgs args)
    {
      var no = args.Grabber.transform.GetComponentInParent<NetworkObject>();
      if (no == null) return;

      GiveOwnershipWithCooldown(no.Owner, ownershipTransferCooldown, true);
      selectionCount++;
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

    public override void OnOwnershipClient(NetworkConnection prevOwner) // gets called when ownership changes
    {
      base.OnOwnershipClient(prevOwner);
      // when client becomes owner of the volumetric data object, transfer canvas ownership to match
      if (IsOwner && volumeDataNetworker != null)
      {
        TransferCanvasOwnership(Owner);
      }
    }

    private void TransferCanvasOwnership(NetworkConnection newOwner)
    {
      if (newOwner == null)
        return;

      NetworkObject canvasNetworkObject = FindCanvasNetworkObject();
      // only want to act on ownership change if the owners actually switch/ there is a new owner
      if (canvasNetworkObject == null || canvasNetworkObject.Owner == newOwner)
        return;

      RequestCanvasOwnershipServerRpc(canvasNetworkObject.ObjectId, newOwner);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCanvasOwnershipServerRpc(int canvasObjectId, NetworkConnection newOwner)
    {
      NetworkObject canvasNetworkObject = null;
      // find the canvas' networkObject in the scene by using its object ID
      NetworkObject[] networkObjectArray = FindObjectsOfType<NetworkObject>();
      foreach (var networkObject in networkObjectArray) 
      {
        if (networkObject.ObjectId == canvasObjectId)
        {
          canvasNetworkObject = networkObject;
          break;
        }
      }

      if (canvasNetworkObject != null && canvasNetworkObject.Owner != newOwner)
      {
        // canvas exists + current owner and new owner is different so transfer ownership
        canvasNetworkObject.GiveOwnership(newOwner);
      }
    }

    private NetworkObject FindCanvasNetworkObject()
    {
      if (volumeDataNetworker == null || volumeDataNetworker.volumeControlCanvasPrefab == null)
        return null;

      // get expected prefab ID from the data control menu prefab's NetworkObject to cross reference with IDs of objects in the scene
      int expectedPrefabId = volumeDataNetworker.volumeControlCanvasPrefab.GetComponent<NetworkObject>().PrefabId;
      if (expectedPrefabId == -1) // [-1] is invalid ID
        return null;

      // get list of NetworkObjects in the scene
      NetworkObject[] networkObjects = FindObjectsOfType<NetworkObject>();
      // get the first NetworkObject that matches the expected prefabID
      NetworkObject canvasNetworkObject = null;
      foreach (var networkObject in networkObjects)
      {
        if (networkObject.PrefabID = expectedPrefabId && networkObject.GetComponent<VolumeDataControlUI>() != null)
        {
          canvasNetworkObject = networkObject;
          break;
        }
      }

      return canvasNetworkObject;
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
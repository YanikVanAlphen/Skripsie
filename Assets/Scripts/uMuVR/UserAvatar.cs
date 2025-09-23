using System;
using FishNet.Connection;
using FishNet.Object;
using uMuVR.Utility;
using RotaryHeart.Lib.SerializableDictionary;
using TriInspector;
using UnityEngine;
using UnityEngine.Events;
using System.Collections;

namespace uMuVR
{
  public class UserAvatar : NetworkBehaviour
  {
    #region Pose Slots
    [Serializable]
    public class PoseRef
    {
      public Pose pose = Pose.identity;
    }

    [Serializable]
    public class StringToPoseRefDictionary : SerializableDictionaryBase<string, PoseRef> { }

    [Title("Pose Transforms")]
    public StringToPoseRefDictionary slots = new();

    public virtual PoseRef SetterPoseRef(string slot) => slots[slot];
    public virtual PoseRef GetterPoseRef(string slot) => slots[slot];

    public Transform FindOrCreatePoseProxy(string slot)
    {
      Transform proxy, cached;
      if (!slots.ContainsKey(slot)) throw new ArgumentException("The given slot " + slot + " is not stored within this avatar");
      if ((proxy = transform.Find("Proxies")) is null) proxy = new GameObject { transform = { parent = this.transform }, name = "Proxies" }.transform;
      if ((cached = proxy.Find(slot)) is not null) return cached;

      var sp = new GameObject { transform = { parent = proxy }, name = slot }.AddComponent<SyncPose>();
      sp.targetAvatar = this;
      sp.slot = slot;
      sp.mode = ISyncable.SyncMode.Load;
      return sp.transform;
    }
    #endregion

    #region Input Spawning
    [Title("Input Configuration")]
    [PropertyTooltip("List of input controls that may be spawned as appropriate")]
    [SerializeField] private GameObject[] inputPrefabs;

    [PropertyTooltip("Index indicating which of the input controls should be spawned")]
    public int spawnIndex;

    [PropertyTooltip("The input object that gets spawned")]
    [ReadOnly] public GameObject input;

    public UnityEvent<GameObject> onInputSpawned;

    protected virtual void OnInputSpawned(GameObject input)
    {
      SyncPose[] syncPoses = input.GetComponentsInChildren<SyncPose>();
      foreach (SyncPose sync in syncPoses)
      {
        if (sync.targetAvatar == null)
        {
          sync.targetAvatar = this;
          sync.slot = sync.slot ?? "Head"; // Fallback to "Head" if unset
          sync.mode = ISyncable.SyncMode.SyncTo; // XR Rig syncs to UserAvatar
          Debug.Log($"Linked SyncPose on {sync.gameObject.name} to UserAvatar {ObjectId}, slot: {sync.slot}");
        }
      }
      DisableOtherXRRigs();
    }

    private bool shouldMaintainOwnership = false;
    private Coroutine ownershipCheckCoroutine;

    public override void OnStartClient()
    {
      base.OnStartClient();
      Debug.Log($"UserAvatar OnStartClient - IsOwner: {IsOwner}, ObjectId: {ObjectId}, ClientId: {OwnerId}, LocalClientId: {LocalConnection?.ClientId}");
      if (IsOwner)
      {
        shouldMaintainOwnership = true;
        SpawnInputControls();
        if (ownershipCheckCoroutine == null)
          ownershipCheckCoroutine = StartCoroutine(MonitorOwnership());
      }
      else
      {
        DisableSyncs();
        // Check if this object is intended for this client
        if (LocalConnection != null && (OwnerId == -1 || OwnerId == LocalConnection.ClientId))
        {
          Debug.Log($"Requesting ownership for UserAvatar {ObjectId}, intended for client {LocalConnection.ClientId}");
          CmdRequestOwnership(LocalConnection);
          // Start retry coroutine to handle potential desync
          StartCoroutine(RetryOwnership());
        }
        else
        {
          Debug.Log($"Skipping ownership request for UserAvatar {ObjectId}, OwnerId: {OwnerId}, LocalClientId: {LocalConnection?.ClientId}");
        }
      }
    }

    private IEnumerator RetryOwnership()
    {
      int retries = 5;
      float delay = 0.5f;
      for (int i = 0; i < retries; i++)
      {
        yield return new WaitForSeconds(delay);
        if (IsOwner)
        {
          Debug.Log($"Ownership confirmed for UserAvatar {ObjectId} after retry {i + 1}");
          shouldMaintainOwnership = true;
          SpawnInputControls();
          if (ownershipCheckCoroutine == null)
            ownershipCheckCoroutine = StartCoroutine(MonitorOwnership());
          yield break;
        }
        if (LocalConnection != null && (OwnerId == -1 || OwnerId == LocalConnection.ClientId))
        {
          Debug.Log($"Retrying ownership request for UserAvatar {ObjectId}, attempt {i + 1}");
          CmdRequestOwnership(LocalConnection);
        }
      }
      Debug.LogWarning($"Failed to gain ownership for UserAvatar {ObjectId} after {retries} retries");
    }

    private IEnumerator MonitorOwnership()
    {
      while (shouldMaintainOwnership)
      {
        yield return new WaitForSeconds(1f);
        if (shouldMaintainOwnership && !IsOwner && LocalConnection != null && (OwnerId == -1 || OwnerId == LocalConnection.ClientId))
        {
          Debug.Log($"Attempting to reclaim ownership - ObjectId: {ObjectId}, Current owner: {OwnerId}, Local client: {LocalConnection?.ClientId}");
          CmdRequestOwnership(LocalConnection);
        }
      }
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdRequestOwnership(NetworkConnection conn)
    {
      if (conn == null)
      {
        Debug.LogWarning("Cannot change ownership: Connection is null.");
        return;
      }
      if (OwnerId != -1 && OwnerId != conn.ClientId)
      {
        Debug.LogWarning($"Ownership request from client {conn.ClientId} denied: Object {ObjectId} already owned by client {OwnerId}");
        return;
      }
      Debug.Log($"Server received ownership request from client {conn.ClientId} for ObjectId {ObjectId}");
      NetworkObject.GiveOwnership(conn);
      Debug.Log($"Ownership given to client {conn.ClientId} for ObjectId {ObjectId}");
    }

    public override void OnOwnershipClient(NetworkConnection oldOwner)
    {
      base.OnOwnershipClient(oldOwner);
      Debug.Log($"OnOwnershipClient - OldOwner: {oldOwner?.ClientId}, NewOwner: {OwnerId}, IsOwner: {IsOwner}, LocalClient: {LocalConnection?.ClientId}");
      if (IsOwner)
      {
        if (input != null)
        {
          Debug.LogWarning("Gained ownership but input controls already exist.");
        }
        else
        {
          Debug.Log("Gained ownership, spawning input controls.");
          shouldMaintainOwnership = true;
          SpawnInputControls();
          if (ownershipCheckCoroutine == null)
            ownershipCheckCoroutine = StartCoroutine(MonitorOwnership());
        }
      }
      else
      {
        if (input != null)
        {
          Debug.Log("Lost ownership, destroying XR Rig.");
          shouldMaintainOwnership = false;
          if (ownershipCheckCoroutine != null)
          {
            StopCoroutine(ownershipCheckCoroutine);
            ownershipCheckCoroutine = null;
          }
          Destroy(input);
          DisableSyncs();
          input = null;
        }
      }
    }

    [Client]
    private void SpawnInputControls()
    {
      if (spawnIndex >= inputPrefabs.Length)
        throw new IndexOutOfRangeException("Spawn Index is not associated with a valid prefab");

      if (input != null)
      {
        Debug.LogWarning("Input controls already exist, not spawning again");
        return;
      }

      Debug.Log("Spawning input controls!");
      input = Instantiate(inputPrefabs[spawnIndex], transform.position, transform.rotation, transform);
      onInputSpawned?.Invoke(input);
      OnInputSpawned(input);
    }

    [Client]
    private void DisableOtherXRRigs()
    {
      var allXROrigins = FindObjectsOfType<Unity.XR.CoreUtils.XROrigin>();
      foreach (var xrOrigin in allXROrigins)
      {
        bool belongsToUs = false;
        if (input != null)
        {
          belongsToUs = xrOrigin.transform.IsChildOf(input.transform) ||
                       xrOrigin.gameObject == input ||
                       input.transform.IsChildOf(xrOrigin.transform);
        }

        if (!belongsToUs)
        {
          var parentAvatar = xrOrigin.GetComponentInParent<UserAvatar>();
          if (parentAvatar != null && parentAvatar != this)
          {
            Debug.Log($"Disabling XR Origin from another player: {xrOrigin.gameObject.name}");
            xrOrigin.gameObject.SetActive(false);
          }
          else if (parentAvatar == null)
          {
            Debug.Log($"Disabling scene XR Origin: {xrOrigin.gameObject.name}");
            xrOrigin.gameObject.SetActive(false);
          }
        }
        else
        {
          Debug.Log($"Skipping own XR Origin: {xrOrigin.gameObject.name}");
        }
      }
    }

    [Client]
    private void DisableSyncs()
    {
      var syncs = GetComponentsInChildren<ISyncable>();
      foreach (var sync in syncs)
        sync.enabled = false;
    }

    private void OnDestroy()
    {
      shouldMaintainOwnership = false;
      if (ownershipCheckCoroutine != null)
      {
        StopCoroutine(ownershipCheckCoroutine);
        ownershipCheckCoroutine = null;
      }
    }
    #endregion
  }
}
//using System;
//using FishNet.Connection;
//using FishNet.Object;
//using uMuVR.Utility;
//using RotaryHeart.Lib.SerializableDictionary;
//using TriInspector;
//using UnityEngine;
//using UnityEngine.Events;

//namespace uMuVR
//{

//	/// <summary>
//	/// Component that holds pose data. It acts as the glue between the input layer and the networking layer.
//	/// Additionally, it provides a convenient method for spawning input controls for the object's owner
//	/// </summary>
//	public class UserAvatar : NetworkBehaviour
//	{
//		#region Pose Slots

//		/// <summary>
//		/// Class wrapper around unity's Pose struct to enable reference semantics
//		/// </summary>
//		[Serializable]
//		public class PoseRef
//		{
//			public Pose pose = Pose.identity;
//		}

//		/// <summary>
//		/// Implementation of the particular type of serialized dictionary used by this object
//		/// </summary>
//		[Serializable]
//		public class StringToPoseRefDictionary : SerializableDictionaryBase<string, PoseRef> { }

//		/// <summary>
//		/// Poses that can can be read to or from by the input and networking layers respectively
//		/// </summary>
//		[Title("Pose Transforms")]
//		public StringToPoseRefDictionary slots = new();

//		/// <summary>
//		/// Reference to a pose slot for storage purposes
//		/// </summary>
//		/// <remarks>NOTE: Provides support for the PostProcessed Avatar</remarks>
//		/// <param name="slot">Name of the slot to reference</param>
//		/// <returns>Reference to the slot where data can be stored</returns>
//		public virtual PoseRef SetterPoseRef(string slot) => slots[slot];
//		/// <summary>
//		/// Reference to a pose slot for reading purposes
//		/// </summary>
//		/// <remarks>NOTE: Provides support for the PostProcessed Avatar</remarks>
//		/// <param name="slot">Name of the slot to reference</param>
//		/// <returns>Reference to the slot where data can be read</returns>
//		public virtual PoseRef GetterPoseRef(string slot) => slots[slot];

//		/// <summary>
//		/// Creates (or finds if it already exists) a game object that synchronizes its transform with this slot, and return its transform
//		/// </summary>
//		/// <param name="slot">The name of the slot to reference</param>
//		/// <returns>Transform of the new proxy object</returns>
//		/// <exception cref="ArgumentException">Argument exception if the slot can't be found in the dictionary</exception>
//		public Transform FindOrCreatePoseProxy(string slot)
//		{
//			Transform proxy, cached;
//			// If the slot doesn't exist in the dictionary... error
//			if (!slots.ContainsKey(slot)) throw new ArgumentException("The given slot " + slot + " is not stored within this avatar");
//			// If the proxy holder object doesn't already exist... create it
//			if ((proxy = transform.Find("Proxies")) is null) proxy = new GameObject { transform = { parent = this.transform }, name = "Proxies" }.transform;
//			// If there is a proxy for this slot which already exists... return it instead
//			if ((cached = proxy.Find(slot)) is not null) return cached;

//			// Create a new proxy game object and attach a sync pose to it
//			var sp = new GameObject { transform = { parent = proxy }, name = slot }.AddComponent<SyncPose>();
//			// Configure the sync pose to reference the appropriate slot
//			sp.targetAvatar = this;
//			sp.slot = slot;
//			sp.mode = ISyncable.SyncMode.Load;

//			// Return the transform of that newly created proxy object
//			return sp.transform;
//		}

//		#endregion


//		// -- Input Spawning --


//		#region Input Spawning

//		[Title("Input Configuration")]
//		[PropertyTooltip("List of input controls that may be spawned as appropriate")]
//		[SerializeField] private GameObject[] inputPrefabs;

//		[PropertyTooltip("Index indicating which of the input controls should be spawned")]
//		public int spawnIndex;

//		[PropertyTooltip("The input object that gets spawned")]
//		[ReadOnly] public GameObject input;

//		/// <summary>
//		/// Event invoked when input controls are spawned
//		/// </summary>
//		public UnityEvent<GameObject> onInputSpawned;

//		/// <summary>
//		/// Function called when input controls are spawned, allows very easy access to the event on inherited objects
//		/// </summary>
//		/// <param name="g">Reference to the spawned controls</param>
//		protected virtual void OnInputSpawned(GameObject g) { }

//		/// <summary>
//		/// When the client starts, if we are the avatar's owner spawn us controls, otherwise disable Avatar syncs and rely on data from the network
//		/// </summary>
//		public override void OnStartClient()
//		{
//			base.OnStartClient();

//			// If we have input authority, spawn the input controls
//			if (IsOwner)
//				SpawnInputControls();
//			else
//				DisableSyncs();
//		}


//		/// <summary>
//		/// When we become the input authority spawn the input controls, when we lose input authority remove the input controls
//		/// </summary>
//		public override void OnOwnershipClient(NetworkConnection oldOwner)
//		{
//			base.OnOwnershipClient(oldOwner);

//			if (IsOwner && input is not null)
//				Debug.LogWarning("For some reason authority changed but we still have it...");
//			else if (IsOwner)
//				SpawnInputControls();
//			else if (input is not null)
//			{
//				Debug.Log("We are no longer the input authority and thus should get rid of our input controls");
//				Destroy(input);
//				DisableSyncs();
//				input = null;
//			}
//		}


//		/// <summary>
//		/// Function that spawns the input controls
//		/// </summary>
//		/// <exception cref="IndexOutOfRangeException">Throws an exception if the index of the input controls which should be spawned is out of range</exception>
//		[Client]
//		private void SpawnInputControls()
//		{
//			if (spawnIndex > inputPrefabs.Length)
//				throw new IndexOutOfRangeException("Spawn Index is not associated with a valid prefab");

//			// TODO: Add functionality to spawn VR or non VR input
//			Debug.Log("Spawning input controls!");
//			input = Instantiate(inputPrefabs[spawnIndex], transform.position, transform.rotation, transform);

//			// Notify the outside world that input controls have been spawned
//			onInputSpawned?.Invoke(input);
//			OnInputSpawned(input);
//		}

//		/// <summary>
//		/// (client only) If we aren't the owner disable all of the SyncPoses... just rely on the network transforms
//		/// </summary>
//		[Client]
//		private void DisableSyncs()
//		{
//			var syncs = GetComponentsInChildren<ISyncable>();
//			foreach (var sync in syncs)
//				sync.enabled = false;
//		}

//		#endregion
//	}
//}
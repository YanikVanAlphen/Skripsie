using UnityEngine;
using FishNet.Object;
using System.Collections;
using UnityVolumeRendering;

public static class VolumeRenderObjectFindUtility
{
  public static IEnumerator FindVolumeObject(string caller, NetworkObject prefab, float retryInterval = 0.5f, int maxRetries = 120, bool requireDataset = true, bool forceReturn = false)
  {
    int retryCount = 0;
    VolumeRenderedObject volumeObject = null;
    int expectedPrefabId = -1;

    if (prefab != null)
      expectedPrefabId = prefab.PrefabId;

    while (volumeObject == null && retryCount < maxRetries)
    {
      NetworkObject[] networkObjectArray = UnityEngine.Object.FindObjectsOfType<NetworkObject>();
      //NetworkObject[] networkObjectArray = FindObjectsOfType<NetworkObject>();

      foreach (NetworkObject currentNetworkObject in networkObjectArray)
      {
        if (currentNetworkObject.PrefabId == expectedPrefabId)
        {
          volumeObject = currentNetworkObject.GetComponent<VolumeRenderedObject>();
          if (volumeObject != null && (!requireDataset || volumeObject.dataset != null))
          {
            yield return volumeObject;
            yield break;
          }
          if (volumeObject == null && forceReturn)
          {
            volumeObject = currentNetworkObject.gameObject.AddComponent<VolumeRenderedObject>(); // client-side will not already have added the component, so add it and return anyways
            yield return volumeObject;
            yield break;
          }
        }
      }

      retryCount++;
      yield return new WaitForSeconds(retryInterval);
    }
    Debug.LogWarning("Failed to find VolumeRenderedObject, called by: " + caller);
    yield return null;
  }
}

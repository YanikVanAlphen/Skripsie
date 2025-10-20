using UnityEngine;
using FishNet.Object;
using System.Collections;
using UnityVolumeRendering;

public static class VolumeRenderObjectFindUtility
{
  public static IEnumerator FindVolumeObject(string caller, NetworkObject prefab, float retryInterval = 0.5f, int maxRetries = 120, bool requireDataset = true)
  {
    int retryCount = 0;
    VolumeRenderedObject volumeObject = null;
    int expectedPrefabId = -1;

    if (prefab != null)
      expectedPrefabId = prefab.PrefabId;

    while (volumeObject == null && retryCount < maxRetries)
    {
      NetworkObject[] networkObjectArray = UnityEngine.Object.FindObjectsOfType<NetworkObject>();

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
        }
      }

      retryCount++;
      yield return new WaitForSeconds(retryInterval);
    }
    Debug.LogError("Failed to find VolumeRenderedObject, called by: " + caller);
    yield return null;
  }
}

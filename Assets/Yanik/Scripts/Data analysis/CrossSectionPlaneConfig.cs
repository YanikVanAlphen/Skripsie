using FishNet.Object;
using UnityEngine;
using UnityVolumeRendering;
using System.Collections;

public class CrossSectionPlaneConfig : NetworkBehaviour
{
  private CrossSectionPlane planeComponent;
  private VolumeRenderedObject volumeObject;

  public override void OnStartClient()
  {
    base.OnStartClient();

    planeComponent = GetComponent<CrossSectionPlane>();
    if (planeComponent == null)
    {
      Debug.LogError($"CrossSectionPlaneConfig: Missing CrossSectionPlane on {gameObject.name}");
      return;
    }

    StartCoroutine(ConfigureLocalTarget());
  }

  private IEnumerator ConfigureLocalTarget()
  {
    // wait for the volume to be ready
    int retryCount = 0;
    const int maxRetries = 50;
    while (volumeObject == null || volumeObject.dataset == null)
    {
      volumeObject = FindObjectOfType<VolumeRenderedObject>();
      if (volumeObject == null)
      {
        if (retryCount >= maxRetries)
        {
          yield break;
        }
        retryCount++;
        yield return new WaitForSeconds(0.2f);
        continue;
      }

      if (volumeObject.dataset == null)
      {
        Debug.Log($"CrossSectionPlaneConfig: VolumeRenderedObject found but dataset not ready, waiting...");
        yield return new WaitForSeconds(0.5f);
      }
    }

    // make sure CrossSectionManager is ready
    var crossSectionManager = volumeObject.gameObject.GetComponent<UnityVolumeRendering.CrossSectionManager>();
    if (crossSectionManager == null)
    {
      crossSectionManager = volumeObject.gameObject.AddComponent<UnityVolumeRendering.CrossSectionManager>();
      Debug.Log($"CrossSectionPlaneConfig: Initialized CrossSectionManager on {volumeObject.gameObject.name}");
    }

    // set target locally
    planeComponent.SetTargetObject(volumeObject);
    Debug.Log($"CrossSectionPlaneConfig: Client configured CrossSectionPlane ({gameObject.name}) with target {volumeObject.gameObject.name}");
  }
}
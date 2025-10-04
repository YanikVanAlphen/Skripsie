using FishNet.Object;
using FishNet.Connection;
using FishNet.Component.Transforming;
using FishNet.Managing.Server;
using FishNet.Managing.Object;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using UnityVolumeRendering;
using System.Linq;
using System.IO;
using System;
using System.Collections.Generic;
using System.Collections;

public class CrossSectionManager : NetworkBehaviour
{
  [SerializeField] public GameObject crossSectionPlanePrefab;

  private VolumeRenderedObject volumeObject;

  private void Awake()
  {
    StartCoroutine(FindVolumeObject());
  }

  private IEnumerator FindVolumeObject()
  {
    int retryCount = 0;
    const int maxRetries = 40; // Wait up to 20 seconds
    while (volumeObject == null && retryCount < maxRetries)
    {
      volumeObject = FindObjectOfType<VolumeRenderedObject>();
      if (volumeObject == null)
      {
        retryCount++;
        yield return new WaitForSeconds(0.5f);
      }
    }
    if (volumeObject == null)
    {
      yield break;
    }
  }

  private void SpawnCrossSectionPlane()
  {
     GameObject crossSectionPlane = Instantiate(crossSectionPlanePrefab);

  }
}

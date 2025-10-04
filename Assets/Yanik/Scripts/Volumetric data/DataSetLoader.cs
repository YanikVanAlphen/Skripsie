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
using System.Collections;
using System.Collections.Generic;
//using System.Threading.Tasks;

namespace VolumeData
{
  /// <summary>
  /// Uses UnityVolumeRendering plugin (https://github.com/mlavik1/UnityVolumeRendering.git) for dataset loading and rendering.
  /// </summary>
  public class DataSetLoader
  {
    private readonly string datasetPath;
    private readonly string DVRShaderName = "VolumeRendering/DirectVolumeRenderingShader";

    public DataSetLoader(string dataPath)
    {
      this.datasetPath = dataPath;
      //this.dataType = dataType; // let user decide the input?
    }

    public string GetDataPath()
    {
      string fullPath;
      if (Application.isEditor) // if running in editor, look in assets path. if running in build, look in streamingassets path
      {
        fullPath = Path.Combine(Application.dataPath, datasetPath);
      }
      else
      {
        fullPath = Path.Combine(Application.streamingAssetsPath, datasetPath);
      }
      return fullPath;
    }

    public VolumeDataset LoadDataset() // load and return dataset using plugin's functions
    {
      string fullPath = GetDataPath();
      VolumeDataset dataset = null;
      DatasetType datasetType = DatasetImporterUtility.GetDatasetType(fullPath); // let plugin decide what datatype it is

      if (datasetType == DatasetType.Unknown)
      {
        Debug.LogWarning($"Unknown dataset type for file or directory: {fullPath}");
        return null;
      }

      try
      {
        if (Directory.Exists(fullPath)) // directory based datasets (DICOM / ImageSequence)
        {
          if (datasetType == DatasetType.DICOM)
          {
            IImageSequenceImporter importer = ImporterFactory.CreateImageSequenceImporter(ImageSequenceFormat.DICOM);
            if (importer != null)
            {
              string[] dicomFiles = Directory.GetFiles(fullPath, "*.dcm", SearchOption.AllDirectories); // get all .dcm files to match SimpleITKDICOMImporter expected input
              if (dicomFiles.Length > 0)
              {
                var series = importer.LoadSeries(dicomFiles); // plugin returns a grouped DICOM series
                if (series != null && series.Any()) // contain at least one IImageSequenceSeries
                {
                  dataset = importer.ImportSeries(series.First(), new ImageSequenceImportSettings()); // get first valid series
                }
              }
            }
          }
          else if (datasetType == DatasetType.ImageSequence)
          {
            IImageSequenceImporter importer = ImporterFactory.CreateImageSequenceImporter(ImageSequenceFormat.ImageSequence);
            if (importer != null)
            {
              string[] validExtensions = new[] { ".png", ".jpg", ".jpeg", ".tiff", ".tif" }; // defined valid extensions according to what image sequence importer expects
              string[] imageFiles = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories) // find all files within given dir - also searched subfolders
                .Where(file => validExtensions.Contains(Path.GetExtension(file).ToLower())) // extract file extension and compare with list of valid extensions
                .ToArray(); // convert to array for string array
              if (imageFiles.Length > 0)
              {
                var series = importer.LoadSeries(imageFiles); // plugin takes array of image file paths and returns single series of images
                if (series != null && series.Any())
                {
                  dataset = importer.ImportSeries(series.First(), new ImageSequenceImportSettings()); // get first series (usually only has one series)
                }
              }
            }
          }
          else
          {
            Debug.LogError($"Directory found but dataset type not supported: {datasetType}");
            return null;
          }
        }
        else if (File.Exists(fullPath))
        {
          if (datasetType == DatasetType.Raw)
          {
            string iniPath = Path.ChangeExtension(fullPath, ".ini");
            DatasetIniData ini = DatasetIniReader.ParseIniFile(iniPath);
            if (ini == null)
            {
              Debug.LogWarning("No .ini found for RAW dataset, using defaults.");
              ini = new DatasetIniData(); // make sure the defaults are set
              ini.dimX = 128;
              ini.dimY = 256;
              ini.dimZ = 256;
              ini.bytesToSkip = 0;
              ini.format = DataContentFormat.Uint8;
              ini.endianness = Endianness.LittleEndian;
            }
            RawDatasetImporter importer = new RawDatasetImporter(fullPath, ini.dimX, ini.dimY, ini.dimZ, ini.format, ini.endianness, ini.bytesToSkip);
            dataset = importer.Import();
          }
          else
          {
            IImageFileImporter importer = null;
            if (datasetType == DatasetType.NRRD)
              importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.NRRD);
            else if (datasetType == DatasetType.NIFTI)
              importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.NIFTI);
            else if (datasetType == DatasetType.PARCHG)
              importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.VASP);

            if (importer != null)
              dataset = importer.Import(fullPath);
          }
        }
        else
        {
          Debug.LogError($"Path is not a valid file or directory: {fullPath}");
          return null;
        }

        if (dataset == null)
        {
          Debug.LogError($"Failed to load dataset from: {fullPath}.");
        }
      }
      catch (Exception e)
      {
        Debug.LogError($"Error importing dataset: {e.Message}");
      }

      return dataset;
    }

    public IEnumerator ConfigureVolumeRenderingAsync(VolumeRenderedObject volumeObject, VolumeDataset dataset)
    {
      if (volumeObject == null || dataset == null)
      {
        Debug.LogError("VolumeRenderedObject or dataset is null.");
        yield break;
      }

      /* RENDERING OPTIONS:
      * RenderMode.DirectVolumeRendering
      * RenderMode.MaximumIntensityProjectipon (plugin author's typo)
      * RenderMode.IsosurfaceRendering
      */
      volumeObject.dataset = dataset;
      float maxScale = Mathf.Max(dataset.scale.x, dataset.scale.y, dataset.scale.z);
      volumeObject.transform.localScale = Vector3.one / maxScale;

      Transform volumeContainer = volumeObject.transform.Find("VolumeContainer"); // access VolumeContainer child of VolumeRenderedObject to explicitly set rendering params
      MeshRenderer meshRenderer = null;
      if (volumeContainer == null)
      {
        Debug.LogWarning("VolumeContainer child not found");
        GameObject container = new GameObject("VolumeContainer");
        container.transform.SetParent(volumeObject.transform, false);
        container.transform.localPosition = Vector3.zero;
        container.transform.localRotation = Quaternion.identity;
        container.transform.localScale = Vector3.one;

        MeshFilter meshFilter = container.AddComponent<MeshFilter>();
        meshFilter.mesh = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshFilter>().sharedMesh;
        GameObject.Destroy(GameObject.Find("Cube")); // Clean up temp cube

        meshRenderer = container.AddComponent<MeshRenderer>();
        volumeContainer = container.transform;
      }
      else
      {
        meshRenderer = volumeContainer.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
          meshRenderer = volumeContainer.gameObject.AddComponent<MeshRenderer>();
        }
      }

      Shader volumeShader = Shader.Find(DVRShaderName);
      if (volumeShader == null)
      {
        Debug.LogError($"Shader {DVRShaderName} not found.");
        yield break;
      }
      if (meshRenderer.sharedMaterial == null || meshRenderer.sharedMaterial.shader != volumeShader) // only create new material if the shader is wrong
      {
        meshRenderer.sharedMaterial = new Material(volumeShader);
        Debug.Log($"Applied shader {DVRShaderName} to VolumeContainer material.");
      }

      // generate texture asynchronously
      Texture3D dataTexture = null;
      var task = dataset.GetDataTextureAsync(null);
      yield return new WaitUntil(() => task.IsCompleted);
      yield return null; // wait an extra frame for Unity's texture processing

      try
      {
        dataTexture = task.Result;
        Debug.Log("Texture generation completed.");
      }
      catch (Exception e)
      {
        Debug.LogError($"Failed to generate data texture: {e.Message}");
        yield break;
      }

      if (dataTexture != null)
      {
        meshRenderer.sharedMaterial.SetTexture("_DataTex", dataTexture);
        Debug.Log("Assigned dataset texture to material.");
      }
      else
      {
        Debug.LogError("Data texture is null after generation.");
        yield break;
      }

      UnityVolumeRendering.TransferFunction tf = TransferFunctionDatabase.CreateTransferFunction();
      volumeObject.transferFunction = tf;
      Texture2D tfTexture = tf.GetTexture();
      if (tfTexture != null)
      {
        meshRenderer.sharedMaterial.SetTexture("_TFTex", tfTexture);
        Debug.Log("Assigned transfer function texture to material.");
      }
      else
      {
        Debug.LogWarning("Failed to get transfer function texture.");
      }

      Texture2D noiseTexture = GenerateNoiseTexture();
      if (noiseTexture != null)
      {
        meshRenderer.sharedMaterial.SetTexture("_NoiseTex", noiseTexture); // sharedMaterial actually modifies existing material where .material just copies it
        Debug.Log("Assigned noise texture to material.");
      }
      else
      {
        Debug.LogWarning("Failed to generate noise texture.");
      }
      meshRenderer.sharedMaterial.EnableKeyword("MODE_DVR");
      meshRenderer.sharedMaterial.DisableKeyword("MODE_MIP");
      meshRenderer.sharedMaterial.DisableKeyword("MODE_SURF");
      volumeObject.meshRenderer = meshRenderer;

      volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
      volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f));
      volumeObject.UpdateMaterialProperties();
    }

    private Texture2D GenerateNoiseTexture()
    {
      const int noiseDimX = 512;
      const int noiseDimY = 512;
      return NoiseTextureGenerator.GenerateNoiseTexture(noiseDimX, noiseDimY);
    }
  }
}
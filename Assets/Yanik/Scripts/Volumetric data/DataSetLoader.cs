using UnityEngine;
using UnityVolumeRendering;
using System.Linq;
using System.IO;
using System;
using System.Collections;

namespace VolumeData
{
  /// <summary>
  /// Uses UnityVolumeRendering plugin (https://github.com/mlavik1/UnityVolumeRendering.git) for dataset loading and rendering.
  /// </summary>
  public class DataSetLoader
  {
    private readonly string datasetPath;
    private readonly DatasetType dataType;
    private readonly string DVRShaderName = "VolumeRendering/DirectVolumeRenderingShader";

    public DataSetLoader(string dataPath, DatasetType dataType)
    {
      this.datasetPath = dataPath;
      this.dataType = dataType;
    }

    public DataSetLoader() // constructor for client-side init where dataset path not needed to set
    {
      this.datasetPath = string.Empty;
    }

    public string GetDataPath()
    {
      string fullPath;
      // if running in editor, look in assets path. if running in build, look in streamingassets path
      if (Application.isEditor)
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
      DatasetType datasetType = this.dataType;

      if (datasetType == DatasetType.Unknown)
      {
        Debug.LogWarning($"Unknown dataset type for file or directory: {fullPath}");
        return null;
      }

      try
      {
        if (Directory.Exists(fullPath)) // directory based datasets
        {
          if (datasetType == DatasetType.DICOM)
          {
            dataset = GenerateDICOM(fullPath);
          }
          else if (datasetType == DatasetType.ImageSequence)
          {
            dataset = GenerateImgSequence(fullPath);
          }
        }
        else if (File.Exists(fullPath)) // file based datasets
        {
          if (datasetType == DatasetType.Raw) // plugin's ImporterFactory does not support .raw files so create RAW importer manually
          {
            dataset = GenerateRaw(fullPath);
          }
          else
          {
            IImageFileImporter importer = null;
            switch (datasetType)
            {
              case DatasetType.NRRD:
                importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.NRRD);
                break;
              case DatasetType.NIFTI:
                importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.NIFTI);
                break;
              case DatasetType.PARCHG:
                importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.VASP);
                break;
            }

            dataset = importer.Import(fullPath); // plugin method to import data
          }
        }
        else
        {
          Debug.LogError($"Invalid provided path to data");
          return null;
        }
      }
      catch (Exception e)
      {
        Debug.LogError($"Error importing dataset: {e.Message}");
      }

      if (dataset == null)
        Debug.LogError($"Failed to load dataset from: {fullPath}.");

      return dataset;
    }

    private VolumeDataset GenerateDICOM(string fullPath)
    {
      VolumeDataset dataset = null;
      IImageSequenceImporter importer = ImporterFactory.CreateImageSequenceImporter(ImageSequenceFormat.DICOM);

      string[] dicomFiles = Directory.GetFiles(fullPath, "*.dcm", SearchOption.AllDirectories); // get all .dcm files to match SimpleITKDICOMImporter expected input
      if (dicomFiles.Length > 0)
      {
        var series = importer.LoadSeries(dicomFiles); // plugin returns a grouped DICOM series
        if (series != null && series.Any()) // contain at least one IImageSequenceSeries
        {
          dataset = importer.ImportSeries(series.First(), new ImageSequenceImportSettings()); // get first valid series
        }
      }
      return dataset;
    }

    private VolumeDataset GenerateImgSequence(string fullPath)
    {
      VolumeDataset dataset = null;
      IImageSequenceImporter importer = ImporterFactory.CreateImageSequenceImporter(ImageSequenceFormat.ImageSequence);
      string[] validExtensions = new[] { ".png", ".jpg", ".jpeg", ".tiff", ".tif" }; // defined valid extensions according to what image sequence importer expects
      string[] imageFiles = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories) // find all files within given dir - also searched subfolders
        .Where(file => validExtensions.Contains(Path.GetExtension(file).ToLower())) // extract file extension and compare with list of valid extensions
        .ToArray(); // convert to array for string array
      if (imageFiles.Length > 0)
      {
        var series = importer.LoadSeries(imageFiles); // plugin takes array of image file paths and returns single series of images
        if (series != null && series.Any())
        {
          dataset = importer.ImportSeries(series.First(), new ImageSequenceImportSettings()); // get first valid series
        }
      }
      return dataset;
    }

    private VolumeDataset GenerateRaw(string fullPath)
    {
      VolumeDataset dataset;

      string iniPath = Path.ChangeExtension(fullPath, ".ini"); // find .ini file if it exists
      DatasetIniData ini = DatasetIniReader.ParseIniFile(iniPath);
      if (ini == null)
      {
        Debug.LogWarning("No .ini found for RAW dataset, using defaults.");
        ini = new DatasetIniData(); // make sure the defaults are set per default values that plugin gives
        ini.dimX = 128;
        ini.dimY = 256;
        ini.dimZ = 256;
        ini.bytesToSkip = 0;
        ini.format = DataContentFormat.Uint8;
        ini.endianness = Endianness.LittleEndian;
      }

      RawDatasetImporter importer = new RawDatasetImporter(fullPath, ini.dimX, ini.dimY, ini.dimZ, ini.format, ini.endianness, ini.bytesToSkip);
      dataset = importer.Import();

      return dataset;
    }

    public IEnumerator ConfigureVolumeRenderingAsync(VolumeRenderedObject volumeObject, VolumeDataset dataset)
    {
      if (volumeObject == null || dataset == null)
        yield break;

      volumeObject.dataset = dataset;
      Transform volumeContainer = volumeObject.transform.Find("VolumeContainer"); // access VolumeContainer child of VolumeRenderedObject to explicitly set rendering params

      // get max scale in all axes and use that to normalize the data
      float maxScale = Mathf.Max(dataset.scale.x, dataset.scale.y, dataset.scale.z);
      // normalize scaling in all axes
      volumeObject.transform.localScale = Vector3.one / maxScale;

      MeshRenderer meshRenderer = volumeContainer.GetComponent<MeshRenderer>();
      Shader volumeShader = Shader.Find(DVRShaderName);
      if (meshRenderer.sharedMaterial == null || meshRenderer.sharedMaterial.shader != volumeShader) // only create new material if the shader is wrong
        meshRenderer.sharedMaterial = new Material(volumeShader);

      // generate texture asynchronously
      Texture3D dataTexture = null;
      // from UnityVolumeRendering plugin: "Gets the 3D data texture, containing the density values of the dataset. Will create the data texture if it does not exist, without blocking the main thread."
      // null is input since we are not accessing the optional progress handler
      var task = dataset.GetDataTextureAsync(null);
      // pause execution of this coroutine until the texture is generated using Unity WaitUntil()
      yield return new WaitUntil(() => task.IsCompleted);
      yield return null; // wait an extra frame for Unity's texture processing

      dataTexture = task.Result;
      Debug.Log("Texture generation completed.");
      meshRenderer.sharedMaterial.SetTexture("_DataTex", dataTexture);
      Debug.Log("Assigned dataset texture to material.");

      UnityVolumeRendering.TransferFunction tf = TransferFunctionDatabase.CreateTransferFunction();
      volumeObject.transferFunction = tf;
      Texture2D tfTexture = tf.GetTexture();
      meshRenderer.sharedMaterial.SetTexture("_TFTex", tfTexture);
      Debug.Log("Assigned transfer function texture to material.");

      Texture2D noiseTexture = GenerateNoiseTexture();
      meshRenderer.sharedMaterial.SetTexture("_NoiseTex", noiseTexture); // sharedMaterial actually modifies existing material where .material just copies it
      Debug.Log("Assigned noise texture to material.");

      meshRenderer.sharedMaterial.EnableKeyword("MODE_DVR");
      meshRenderer.sharedMaterial.DisableKeyword("MODE_MIP");
      meshRenderer.sharedMaterial.DisableKeyword("MODE_SURF");
      volumeObject.meshRenderer = meshRenderer;

      /* 
      * RENDERING OPTIONS (from UnityVolumeRendering plugin):
      * -----------------------------------------------------
      * RenderMode.DirectVolumeRendering
      * RenderMode.MaximumIntensityProjectipon (plugin author's typo, keep as is)
      * RenderMode.IsosurfaceRendering 
      */
      volumeObject.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
      volumeObject.SetVisibilityWindow(new Vector2(0.01f, 0.9f));
      volumeObject.UpdateMaterialProperties();
      volumeObject.SetLightingEnabled(true);
    }

    private Texture2D GenerateNoiseTexture()
    {
      const int noiseDimX = 512;
      const int noiseDimY = 512;
      return NoiseTextureGenerator.GenerateNoiseTexture(noiseDimX, noiseDimY);
    }
  }
}
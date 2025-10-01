using UnityVolumeRendering;
using System.IO;
using System.IO.Compression;
using System;
using UnityEngine;
using System.Linq;

public static class DatasetSerializer
{
  public static byte[] Serialize(VolumeDataset dataset)
  {
    if (dataset == null || dataset.data == null || dataset.data.Length == 0)
    {
      Debug.LogError("Cannot serialize dataset: dataset or data array is null or empty.");
      return null;
    }

    try
    {
      byte[] uncompressedData;
      using (MemoryStream stream = new MemoryStream())
      {
        // Manually write data to avoid BinaryWriter disposing stream
        WriteInt32(stream, dataset.dimX);
        WriteInt32(stream, dataset.dimY);
        WriteInt32(stream, dataset.dimZ);
        WriteSingle(stream, dataset.scale.x);
        WriteSingle(stream, dataset.scale.y);
        WriteSingle(stream, dataset.scale.z);
        float[] data = dataset.data;
        WriteInt32(stream, data.Length);
        Debug.Log($"Serializing dataset: dimX={dataset.dimX}, dimY={dataset.dimY}, dimZ={dataset.dimZ}, dataLength={data.Length}");
        foreach (float value in data)
          WriteSingle(stream, value);
        stream.Flush(); // Ensure all data is written
        uncompressedData = stream.ToArray();
        Debug.Log($"Uncompressed data: {uncompressedData.Length / 1024f / 1024f:F2} MB");
      }

      // Compress the uncompressed data
      using (MemoryStream compressedStream = new MemoryStream())
      {
        using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Compress, leaveOpen: true))
        {
          gzip.Write(uncompressedData, 0, uncompressedData.Length);
          gzip.Flush(); // Ensure compression is complete
        }
        byte[] result = compressedStream.ToArray();
        Debug.Log($"Serialized and compressed dataset: {result.Length / 1024f / 1024f:F2} MB");
        return result;
      }
    }
    catch (Exception e)
    {
      Debug.LogError($"Failed to serialize dataset: {e.Message}, StackTrace: {e.StackTrace}");
      return null;
    }
  }

  private static void WriteInt32(Stream stream, int value)
  {
    byte[] bytes = BitConverter.GetBytes(value);
    stream.Write(bytes, 0, bytes.Length);
  }

  private static void WriteSingle(Stream stream, float value)
  {
    byte[] bytes = BitConverter.GetBytes(value);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static VolumeDataset Deserialize(byte[] compressedData)
  {
    if (compressedData == null || compressedData.Length == 0)
    {
      Debug.LogError("Cannot deserialize dataset: compressed data is null or empty.");
      return null;
    }

    try
    {
      byte[] decompressedData;
      using (MemoryStream compressedStream = new MemoryStream(compressedData))
      using (MemoryStream decompressedStream = new MemoryStream())
      {
        using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Decompress))
        {
          gzip.CopyTo(decompressedStream);
          gzip.Flush();
        }
        decompressedData = decompressedStream.ToArray();
      }

      using (MemoryStream stream = new MemoryStream(decompressedData))
      using (BinaryReader reader = new BinaryReader(stream))
      {
        VolumeDataset dataset = new VolumeDataset
        {
          dimX = reader.ReadInt32(),
          dimY = reader.ReadInt32(),
          dimZ = reader.ReadInt32(),
          scale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
        };
        int dataLength = reader.ReadInt32();
        dataset.data = new float[dataLength];
        Debug.Log($"Deserializing dataset: dimX={dataset.dimX}, dimY={dataset.dimY}, dimZ={dataset.dimZ}, dataLength={dataLength}");
        for (int i = 0; i < dataLength; i++)
          dataset.data[i] = reader.ReadSingle();
        Debug.Log($"Deserialized dataset successfully: dimX={dataset.dimX}, dimY={dataset.dimY}, dimZ={dataset.dimZ}");
        return dataset;
      }
    }
    catch (Exception e)
    {
      Debug.LogError($"Failed to deserialize dataset: {e.Message}, StackTrace: {e.StackTrace}");
      return null;
    }
  }

  public static byte[][] ChunkData(byte[] data, int chunkSize = 1024 * 1024) // 1MB chunks
  {
    if (data == null || data.Length == 0)
    {
      Debug.LogError("Cannot chunk data: data is null or empty.");
      return new byte[0][];
    }

    int numChunks = (int)Math.Ceiling((double)data.Length / chunkSize);
    byte[][] chunks = new byte[numChunks][];
    for (int i = 0; i < numChunks; i++)
    {
      int length = Math.Min(chunkSize, data.Length - i * chunkSize);
      chunks[i] = new byte[length];
      Array.Copy(data, i * chunkSize, chunks[i], 0, length);
      Debug.Log($"Created chunk {(i+1).ToString()}/{numChunks}, size={length} bytes");
    }
    return chunks;
  }

  public static byte[] CombineChunks(byte[][] chunks)
  {
    if (chunks == null || chunks.Length == 0)
    {
      Debug.LogError("Cannot combine chunks: chunks array is null or empty.");
      return null;
    }

    int totalLength = chunks.Sum(chunk => chunk?.Length ?? 0);
    byte[] data = new byte[totalLength];
    int offset = 0;
    foreach (byte[] chunk in chunks)
    {
      if (chunk != null)
      {
        Array.Copy(chunk, 0, data, offset, chunk.Length);
        offset += chunk.Length;
      }
    }
    Debug.Log($"Combined {chunks.Length} chunks into {totalLength / 1024f / 1024f:F2} MB");
    return data;
  }
}
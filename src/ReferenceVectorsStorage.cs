using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Text.Json;

namespace FraudDetection;

public sealed class ReferenceVectorsStorage : IDisposable
{
   const int K = 5;

   private readonly IConfiguration _configuration;
   private readonly List<(float[] Vector, bool IsFraud)> _references = [];

   public ReferenceVectorsStorage(IConfiguration configuration)
   {
      _configuration = configuration;
   }

   public void Dispose()
   {
   }

   public async Task Initialize()
   {
      Console.WriteLine("Loading json");
      var references = await LoadReferences();

      _references.AddRange(references.Select(it =>
      {
         var paddedVector = new float[16];
         it.Vector.CopyTo(paddedVector, 0);
         paddedVector[14] = 0;
         paddedVector[15] = 0;

         return (
            Vector: paddedVector, 
            IsFraud: it.Label.Equals("fraud", StringComparison.OrdinalIgnoreCase)
         );
      }));

      Console.WriteLine("Done");
   }

   private async Task<List<Reference>> LoadReferences()
   {
      var path = _configuration.GetSection("ReferencesFilePath").Get<string>() ??
         throw new InvalidDataException("Could not load normalization configuration");

      using var openStream = File.OpenRead(path);

      return (await JsonSerializer.DeserializeAsync(openStream, AppJsonContext.Default.ListReference))!;
   }

   public int GetFraudCountFromKNearest(ReadOnlySpan<float> query)
   {
      Span<float> bestDist = stackalloc float[K];
      Span<bool> bestFlag = stackalloc bool[K];

      bestDist.Fill(float.PositiveInfinity);
      bestFlag.Clear();

      //if (Vector.IsHardwareAccelerated && Vector<float>.Count == 4)
      //{
         CalculateDistanceUsingSimd4(query, bestDist, bestFlag);
      //}
      //else
      //{
      //   for(int i = 0; i < _references.Count; i++)
      //   {
      //      var distance = CalculateDistance(_references[i].Vector.AsSpan(), query);
      //      //if (distance < tau)
      //      //{
      //      //   heap.Enqueue(i, distance);
      //      //   if (heap.Count > K)
      //      //   {
      //      //      heap.Dequeue(); // remove the farthest
      //      //      // peek the new worst distance
      //      //      tau = heap.UnorderedItems.Max(x => x.Priority);
      //      //   }
      //      //}
      //   }
      //}

      return bestFlag.Count(true);
   }

   private void CalculateDistanceUsingSimd4(ReadOnlySpan<float> query, Span<float> bestDist, Span<bool> bestFlag)
   {
      float worst = float.PositiveInfinity;

      // Pre vectorize the query vector
      var qv0 = new Vector<float>(query.Slice(0, 4));
      var qv1 = new Vector<float>(query.Slice(4, 4));
      var qv2 = new Vector<float>(query.Slice(8, 4));
      var qv3 = new Vector<float>(query.Slice(12, 4));

      for (int i = 0; i < _references.Count; i++)
      {
         var va0 = new Vector<float>(_references[i].Vector.AsSpan().Slice(0, 4));
         var va1 = new Vector<float>(_references[i].Vector.AsSpan().Slice(4, 4));
         var va2 = new Vector<float>(_references[i].Vector.AsSpan().Slice(8, 4));
         var va3 = new Vector<float>(_references[i].Vector.AsSpan().Slice(12, 4));

         var diff0 = va0 - qv0;
         var diff1 = va1 - qv1;
         var diff2 = va2 - qv2;
         var diff3 = va3 - qv3;

         float distance = Vector.Dot(diff0, diff0);
         distance += Vector.Dot(diff1, diff1);
         distance += Vector.Dot(diff2, diff2);
         distance += Vector.Dot(diff3, diff3);

         if (distance < worst)
         {
            InsertTopK(bestDist, bestFlag, distance, _references[i].IsFraud);
            worst = bestDist[K - 1];
         }
      }
   }

   private static float CalculateDistance(ReadOnlySpan<float> spanA, ReadOnlySpan<float> spanB)
   {
      Debug.Assert(spanA.Length == spanB.Length, $"Vectors must have the same dimension. Vector a has {spanA.Length} dimensions, vector b has {spanB.Length} dimensions.");

      float sum = 0f;
      int i = 0;

      var test = Vector128.IsHardwareAccelerated;

      if (Vector.IsHardwareAccelerated)
      {
         int width = Vector<float>.Count;
         Vector<float> acc = Vector<float>.Zero;
         int limit = spanA.Length - width;

         for (; i <= limit; i += width)
         {
               var va = new Vector<float>(spanA.Slice(i, width));
               var vb = new Vector<float>(spanB.Slice(i, width));
               var diff = va - vb;
               acc += diff * diff;
         }
         sum = Vector.Dot(acc, Vector<float>.One);
      }

      for (; i < spanA.Length; i++)
      {
         float diff = spanA[i] - spanB[i];
         sum += diff * diff;
      }

      return sum;
   }

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   private static void InsertTopK(Span<float> bestDist, Span<bool> bestFlag, float newDist, bool newFlag)
   {
      int pos = K - 1;

      while (pos > 0 && bestDist[pos - 1] > newDist)
      { 
         pos--;
      }

      for (int j = K - 1; j > pos; j--) 
      {
         bestDist[j] = bestDist[j - 1]; 
         bestFlag[j] = bestFlag[j - 1]; 
      }

      bestDist[pos] = newDist;
      bestFlag[pos] = newFlag;
   }
}
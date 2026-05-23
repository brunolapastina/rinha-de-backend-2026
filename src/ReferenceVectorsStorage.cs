using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Text.Json;

namespace FraudDetection;

public sealed class BruteForceFinder
{
   const int K = 5;

   private readonly List<(float[] Vector, bool IsFraud)> _references;

   public BruteForceFinder(IConfiguration configuration)
   {
      var references = LoadReferences(configuration);

      _references = references.Select(it =>
      {
         var paddedVector = new float[16];
         it.Vector.CopyTo(paddedVector, 0);
         paddedVector[14] = 0;
         paddedVector[15] = 0;

         return (
            Vector: paddedVector, 
            IsFraud: it.Label.Equals("fraud", StringComparison.OrdinalIgnoreCase)
         );
      }).ToList();
   }

   private static List<Reference> LoadReferences(IConfiguration configuration)
   {
      var path = configuration.GetSection("ReferencesFilePath").Get<string>() ??
         throw new InvalidDataException("Could not load normalization configuration");

      using var openStream = File.OpenRead(path);

      return JsonSerializer.Deserialize(openStream, AppJsonContext.Default.ListReference)!;
   }

   public int GetFraudCountFromKNearest(ReadOnlySpan<float> query)
   {
      Span<float> bestDist = stackalloc float[K];
      Span<bool> bestFlag = stackalloc bool[K];

      bestDist.Fill(float.PositiveInfinity);

      if (Vector.IsHardwareAccelerated)
      {
         if(Vector<float>.Count == 4)
         {
            CalculateDistanceUsingSimd4(query, bestDist, bestFlag);
         }
         else
         {
            CalculateDistanceUsingSimd8(query, bestDist, bestFlag);
         }
      }
      else
      {
         CalculateDistanceScalar(query, bestDist, bestFlag);
      }

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

   private void CalculateDistanceUsingSimd8(ReadOnlySpan<float> query, Span<float> bestDist, Span<bool> bestFlag)
   {
      float worst = float.PositiveInfinity;

      // Pre vectorize the query vector
      var qv0 = new Vector<float>(query.Slice(0, 8));
      var qv1 = new Vector<float>(query.Slice(8, 8));

      for (int i = 0; i < _references.Count; i++)
      {
         var va0 = new Vector<float>(_references[i].Vector.AsSpan().Slice(0, 8));
         var va1 = new Vector<float>(_references[i].Vector.AsSpan().Slice(8, 8));

         var diff0 = va0 - qv0;
         var diff1 = va1 - qv1;

         float distance = Vector.Dot(diff0, diff0);
         distance += Vector.Dot(diff1, diff1);

         if (distance < worst)
         {
            InsertTopK(bestDist, bestFlag, distance, _references[i].IsFraud);
            worst = bestDist[K - 1];
         }
      }
   }

   private void CalculateDistanceScalar(ReadOnlySpan<float> query, Span<float> bestDist, Span<bool> bestFlag)
   {
      float worst = float.PositiveInfinity;

      for (int i = 0; i < _references.Count; i++)
      {
         float distance = 0f;
         for (int j = 0; j < 14; j++)  // In this case I can skip the 2 padding dimensions
         {
            float diff = _references[i].Vector[j] - query[j];
            distance += diff * diff;
         }

         if (distance < worst)
         {
            InsertTopK(bestDist, bestFlag, distance, _references[i].IsFraud);
            worst = bestDist[K - 1];
         }
      }
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
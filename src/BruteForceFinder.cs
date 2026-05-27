using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace FraudDetection;

public sealed class BruteForceFinder(ReferenceStore refStore)
{
   const int K = 5;

   private readonly ReferenceStore _refStore = refStore;

   public int Search(ReadOnlySpan<float> query)
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

      for (int i = 0; i < _refStore.References.Count; i++)
      {
         var va0 = new Vector<float>(_refStore.References[i].Vector.AsSpan().Slice(0, 4));
         var va1 = new Vector<float>(_refStore.References[i].Vector.AsSpan().Slice(4, 4));
         var va2 = new Vector<float>(_refStore.References[i].Vector.AsSpan().Slice(8, 4));
         var va3 = new Vector<float>(_refStore.References[i].Vector.AsSpan().Slice(12, 4));

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
            InsertTopK(bestDist, bestFlag, distance, _refStore.References[i].IsFraud);
            worst = bestDist[K - 1];
         }
      }
   }

   private unsafe void CalculateDistanceUsingSimd8(ReadOnlySpan<float> query, Span<float> bestDist, Span<bool> bestFlag)
   {
      float worst = float.PositiveInfinity;

      fixed (float* pq = query)
      {
         // Pre vectorize the query vector
         var q0 = Vector256.Load(pq);
         var q1 = Vector256.Load(pq + 8);

         for (int i = 0; i < _refStore.References.Count; i++)
         {
            float distance;

            fixed (float* pr = _refStore.References[i].Vector)
            {
               var r0 = Vector256.Load(pr);
               var r1 = Vector256.Load(pr + 8);

               var diff0 = q0 - r0;
               var diff1 = q1 - r1;

               var sum = (diff0 * diff0) + (diff1 * diff1);
               distance = Vector256.Sum(sum);
            }

            if (distance < worst)
            {
               InsertTopK(bestDist, bestFlag, distance, _refStore.References[i].IsFraud);
               worst = bestDist[K - 1];
            }
         }
      }
   }

   private void CalculateDistanceScalar(ReadOnlySpan<float> query, Span<float> bestDist, Span<bool> bestFlag)
   {
      float worst = float.PositiveInfinity;

      for (int i = 0; i < _refStore.References.Count; i++)
      {
         float distance = 0f;
         for (int j = 0; j < 14; j++)  // In this case I can skip the 2 padding dimensions
         {
            float diff = _refStore.References[i].Vector[j] - query[j];
            distance += diff * diff;
         }

         if (distance < worst)
         {
            InsertTopK(bestDist, bestFlag, distance, _refStore.References[i].IsFraud);
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
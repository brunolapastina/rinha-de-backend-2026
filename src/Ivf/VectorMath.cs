using System.Numerics;
using System.Runtime.Intrinsics;

namespace FraudDetection.Ivf;

internal static class VectorMath
{
   /// <summary>
   /// Squared Euclidean distance. Avoids sqrt — valid for ranking
   /// purposes since sqrt is monotonic.
   /// </summary>
   public static float SquaredEuclidean(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
   {
      if (Vector.IsHardwareAccelerated)
      {
         if(Vector<float>.Count == 4)
         {
            return CalculateDistanceUsingSimd4(a, b);
         }
         else
         {
            return CalculateDistanceUsingSimd8(a, b);
         }
      }
      else
      {
         return CalculateDistanceScalar(a, b);
      }
   }

   /// <summary>
   /// Component-wise mean of a set of vectors. Returns a new float[].
   /// </summary>
   public static float[] Mean(List<float[]> vectors, int dimensions)
   {
      var result = new float[dimensions];
      
      foreach (var v in vectors)
      {
         for (int i = 0; i < dimensions; i++)
         {
            result[i] += v[i];
         }
      }

      for (int i = 0; i < dimensions; i++)
      {
         result[i] /= vectors.Count;
      }

      return result;
   }

   private static float CalculateDistanceUsingSimd4(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
   {
      var a0 = new Vector<float>(a.Slice(0, 4));
      var a1 = new Vector<float>(a.Slice(4, 4));
      var a2 = new Vector<float>(a.Slice(8, 4));
      var a3 = new Vector<float>(a.Slice(12, 4));

      var b0 = new Vector<float>(b.Slice(0, 4));
      var b1 = new Vector<float>(b.Slice(4, 4));
      var b2 = new Vector<float>(b.Slice(8, 4));
      var b3 = new Vector<float>(b.Slice(12, 4));

      var diff0 = a0 - b0;
      var diff1 = a1 - b1;
      var diff2 = a2 - b2;
      var diff3 = a3 - b3;

      float distance = Vector.Dot(diff0, diff0);
      distance += Vector.Dot(diff1, diff1);
      distance += Vector.Dot(diff2, diff2);
      distance += Vector.Dot(diff3, diff3);

      return distance;
   }

   private static unsafe float CalculateDistanceUsingSimd8(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
   {
      fixed (float* pa = a)
      fixed (float* pb = b)
      {

         var a0 = Vector256.Load(pa);
         var a1 = Vector256.Load(pa + 8);

         var b0 = Vector256.Load(pb);
         var b1 = Vector256.Load(pb + 8);

         var diff0 = a0 - b0;
         var diff1 = a1 - b1;

         //float distance = Vector.Dot(diff0, diff0);
         //distance += Vector.Dot(diff1, diff1);
         var sum = (diff0 * diff0) + (diff1 * diff1);
         var distance = Vector256.Sum(sum);

         return distance;
      }
   }

   private static float CalculateDistanceScalar(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
   {
      float distance = 0f;

      for (int i = 0; i < a.Length; i++)
      {
         float diff = a[i] - b[i];
         distance += diff * diff;
      }

      return distance;
   }








   public static void SquaredEuclidean(ReadOnlySpan<float> query, List<(float[] Vector, bool IsFraud)> references, 
                                       Span<float> bestDistances, Span<bool> bestFlags)
   {
      if (Vector.IsHardwareAccelerated)
      {
         if(Vector<float>.Count == 4)
         {
            //CalculateDistanceUsingSimd4(query, references, bestDistances, bestFlags);
         }
         else
         {
            CalculateDistanceUsingSimd8(query, references, bestDistances, bestFlags);
         }
      }
      else
      {
         //CalculateDistanceScalar(query, references, bestDistances, bestFlags);
      }
   }

   private static unsafe void CalculateDistanceUsingSimd8(ReadOnlySpan<float> query, List<(float[] Vector, bool IsFraud)> references,
                                                   Span<float> bestDist, Span<bool> bestFlag)
   {
      fixed (float* pq = query)
      {
         // Pre vectorize the query vector
         var q0 = Vector256.Load(pq);
         var q1 = Vector256.Load(pq + 8);

         for (int i = 0; i < references.Count; i++)
         {
            float distance;

            fixed (float* pr = references[i].Vector)
            {
               var r0 = Vector256.Load(pr);
               var r1 = Vector256.Load(pr + 8);

               var diff0 = q0 - r0;
               var diff1 = q1 - r1;

               var sum = (diff0 * diff0) + (diff1 * diff1);
               distance = Vector256.Sum(sum);
            }

            TopKHeap.InsertTopK(bestDist, bestFlag, distance, references[i].IsFraud);
         }
      }
   }

   /*private void CalculateDistanceUsingSimd4(ReadOnlySpan<float> query, Span<float> bestDist, Span<bool> bestFlag)
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
   }*/
}
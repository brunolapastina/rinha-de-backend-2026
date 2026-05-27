using System.Runtime.CompilerServices;

namespace FraudDetection.Ivf;

internal static class TopKHeap
{
   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   public static void InsertTopK(Span<float> distances, Span<int> idxs, float newDist, int newClusterIdx)
   {
      int pos = distances.Length - 1;
      if( newDist >= distances[pos])
      {
         return;
      }

      while (pos > 0 && distances[pos - 1] > newDist)
      {
         pos--;
      }

      for (int j = distances.Length - 1; j > pos; j--)
      {
         distances[j] = distances[j - 1];
         idxs[j] = idxs[j - 1];
      }

      distances[pos] = newDist;
      idxs[pos] = newClusterIdx;
   }

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   public static void InsertTopK(Span<float> distances, Span<bool> flags, float newDist, bool newFlag)
   {
      int pos = distances.Length - 1;
      if( newDist >= distances[pos])
      {
         return;
      }

      while (pos > 0 && distances[pos - 1] > newDist)
      {
         pos--;
      }

      for (int j = distances.Length - 1; j > pos; j--)
      {
         distances[j] = distances[j - 1];
         flags[j] = flags[j - 1];
      }

      distances[pos] = newDist;
      flags[pos] = newFlag;
   }
}
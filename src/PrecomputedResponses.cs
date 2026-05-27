using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FraudDetection;

public static class PrecomputedResponses
{
   const int K = 5;
   const float THRESHOLD = 0.6f;

   private static readonly byte[][] _possibleResponses;
   
   static PrecomputedResponses()
   {
      _possibleResponses = new byte[K+1][];
      for (int n = 0; n <= K; n++)
      {
         var score = (float)n / K;
         var resp = new FraudScoreResponse(score < THRESHOLD, score);
         _possibleResponses[n] = JsonSerializer.SerializeToUtf8Bytes(resp, AppJsonContext.Default.FraudScoreResponse);
      }
   }

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   public static byte[] GetResponse(int fraudCount)
   {
      Debug.Assert(fraudCount <= K, "Precompiled response does not exist for this count");

      return _possibleResponses[fraudCount];
   }
}
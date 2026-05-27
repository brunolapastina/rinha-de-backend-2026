// ─────────────────────────────────────────────────────────────
// KMeansTrainer.cs  —  K-Means++ init + training loop
// ─────────────────────────────────────────────────────────────

namespace FraudDetection.Ivf;

internal sealed class KMeansTrainer
{
   private const int MaxIterations = 200;
   private const float ConvergenceEpsilon = 1e-6f; // stop if centroids barely move

   private readonly int _k;
   private readonly int _dimensions;
   private readonly Random _rng;

   public KMeansTrainer(int k, int dimensions, int? seed = null)
   {
      _k = k;
      _dimensions = dimensions;
      _rng = seed.HasValue ? new Random(seed.Value) : new Random();
   }

   /// <summary>
   /// Runs K-Means++ and returns the final K centroid vectors.
   /// </summary>
   public float[][] Train(List<(float[] Vector, bool IsFraud)> vectors)
   {
      float[][] centroids = InitializePlusPlus(vectors);

      var assignments = new int[vectors.Count];  // centroid index per vector

      for (int iteration = 0; iteration < MaxIterations; iteration++)
      {
         // ── Assignment step (parallel) ──────────────────────────────
         bool anyChanged = false;

         Parallel.For(0, vectors.Count, () => false, (i, _, localChanged) =>
         {
            int best = FindNearestCentroid(vectors[i].Vector, centroids);
            if (best != assignments[i])
            {
               assignments[i] = best;
               localChanged = true;
            }
            return localChanged;
         },
         localChanged => { if (localChanged) Volatile.Write(ref anyChanged, true); });

         if (!anyChanged)
         {
            Console.WriteLine($"[KMeans] Converged early at iteration {iteration}.");
            break;
         }

         // ── Update step: recompute centroids ────────────────────────
         var buckets = new List<float[]>[_k];
         for (int i = 0; i < _k; i++)
            buckets[i] = new List<float[]>();

         for (int i = 0; i < vectors.Count; i++)
            buckets[assignments[i]].Add(vectors[i].Vector);

         float maxShift = 0f;

         for (int c = 0; c < _k; c++)
         {
            if (buckets[c].Count == 0)
            {
               // Empty cluster: reinitialize to a random vector
               // (rare but can happen with bad data distributions)
               centroids[c] = vectors[_rng.Next(vectors.Count)].Vector;
               continue;
            }

            float[] newCentroid = VectorMath.Mean(buckets[c], _dimensions);
            maxShift = Math.Max(maxShift,
                       VectorMath.SquaredEuclidean(centroids[c], newCentroid));
            centroids[c] = newCentroid;
         }

         Console.WriteLine($"[KMeans] Iteration {iteration + 1:D3} — max centroid shift²: {maxShift:F8}");

         if (maxShift < ConvergenceEpsilon)
         {
            Console.WriteLine($"[KMeans] Converged by epsilon at iteration {iteration + 1}.");
            break;
         }
      }

      return centroids;
   }

   // ── K-Means++ initialization ─────────────────────────────────────────
   // Much better than pure random: each new centroid is chosen with
   // probability proportional to its squared distance from the nearest
   // already-chosen centroid. This spreads centroids out and dramatically
   // reduces the number of iterations needed.
   private float[][] InitializePlusPlus(List<(float[] Vector, bool IsFraud)> vectors)
   {
      var centroids = new float[_k][];

      // Pick the first centroid uniformly at random
      centroids[0] = vectors[_rng.Next(vectors.Count)].Vector;

      var distances = new float[vectors.Count]; // D² from each point to nearest centroid

      for (int c = 1; c < _k; c++)
      {
         // Recompute D² for the centroid we just added
         Parallel.For(0, vectors.Count, i =>
         {
            float d = VectorMath.SquaredEuclidean(vectors[i].Vector, centroids[c - 1]);
            // Keep the minimum distance to ANY centroid chosen so far
            if (c == 1 || d < distances[i])
               distances[i] = d;
         });

         // Weighted random selection: pick next centroid with prob ∝ D²
         centroids[c] = vectors[WeightedRandomIndex(distances)].Vector;
      }

      Console.WriteLine($"[KMeans] K-Means++ init done ({_k} centroids seeded).");
      return centroids;
   }

   // Roulette-wheel selection over the D² weights
   private int WeightedRandomIndex(float[] weights)
   {
      float total = 0f;
      foreach (var w in weights) total += w;

      float threshold = (float)(_rng.NextDouble() * total);
      float cumulative = 0f;

      for (int i = 0; i < weights.Length; i++)
      {
         cumulative += weights[i];
         if (cumulative >= threshold)
            return i;
      }

      return weights.Length - 1; // fallback for floating point edge cases
   }

   // Returns the index of the centroid closest to the given vector
   private static int FindNearestCentroid(float[] vector, float[][] centroids)
   {
      int best = 0;
      float bestDist = float.MaxValue;

      for (int i = 0; i < centroids.Length; i++)
      {
         float d = VectorMath.SquaredEuclidean(vector, centroids[i]);
         if (d < bestDist)
         {
            bestDist = d;
            best = i;
         }
      }

      return best;
   }
}
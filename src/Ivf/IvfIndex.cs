// ─────────────────────────────────────────────────────────────
// IvfIndex.cs  —  the index: owns centroids + inverted lists
// ─────────────────────────────────────────────────────────────

namespace FraudDetection.Ivf;

public sealed class IvfIndex
{
   // ── Tuning knobs ────────────────────────────────────────────────────
   private const int Dimensions = 16;
   private const int K = 128;  // number of clusters
   private const int nProbe = 16;
   private const int topK = 5;

   // ── Internal state ──────────────────────────────────────────────────
   private float[][] _centroids = [];
   private List<(float[] Vector, bool IsFraud)>[] _invertedLists = [];


   /// <summary>
   /// Trains the K-Means clustering and builds the inverted lists.
   /// Call once (or re-call when the reference database changes significantly).
   /// </summary>
   public void Build(List<(float[] Vector, bool IsFraud)> referenceData, int? kmeansSeed = null)
   {
      if (referenceData.Count == 0)
      {
         throw new ArgumentException("Reference database cannot be empty.", nameof(referenceData));
      }

      Console.WriteLine($"[IvfIndex] Building index over {referenceData.Count:N0} vectors (K={K})...");

      // ── Step 1: train centroids ──────────────────────────────────────
      var trainer = new KMeansTrainer(K, Dimensions, seed: kmeansSeed);
      _centroids = trainer.Train(referenceData);

      // ── Step 2: assign every reference vector to its nearest centroid
      //           and populate the inverted lists ──────────────────────
      _invertedLists = new List<(float[], bool)>[K];
      for (int i = 0; i < K; i++)
      {
         _invertedLists[i] = new List<(float[], bool)>();
      }

      // Use a local lock-per-bucket strategy to parallelise safely
      var locks = new object[K];
      for (int i = 0; i < K; i++)
      {
         locks[i] = new object();
      }

      Parallel.For(0, referenceData.Count, i =>
      {
         int clusterIdx = FindNearestCentroid(referenceData[i].Vector);
         lock (locks[clusterIdx])
            _invertedLists[clusterIdx].Add(referenceData[i]);
      });

      // ── Diagnostics ─────────────────────────────────────────────────
      int min = _invertedLists.Min(l => l.Count);
      int max = _invertedLists.Max(l => l.Count);
      double avg = _invertedLists.Average(l => l.Count);
      Console.WriteLine($"[IvfIndex] Build complete. Cluster sizes — min: {min}, max: {max}, avg: {avg:F0}");
   }

   // Search will go here in Phase 2
   // public IReadOnlyList<(float[] Vector, bool IsFraud, float DistanceSquared)>
   //     Search(float[] query, int nProbe, int topK) { ... }

   // ── Private helpers ─────────────────────────────────────────────────
   private int FindNearestCentroid(float[] vector)
   {
      int best = 0;
      float bestDist = float.MaxValue;

      for (int i = 0; i < _centroids.Length; i++)
      {
         float d = VectorMath.SquaredEuclidean(vector, _centroids[i]);
         if (d < bestDist)
         {
            bestDist = d;
            best = i;
         }
      }

      return best;
   }

   /// <summary>
   /// Searches the index for the topK nearest neighbors of the query vector and returns the fraud count.
   /// </summary>
   /// <param name="query">The incoming transaction vector (must be 14 dimensions).</param>
   /// <param name="nProbe">How many clusters to search. Higher = better recall, slower query.
   /// Start at 5, tune upward until recall is satisfactory.</param>
   /// <param name="topK">How many nearest neighbors to return.</param>
   public int Search(ReadOnlySpan<float> query)
   {
      // ── Step 1: rank all K centroids by distance to the query ───────────
      // We always compare against all K centroids (K=100, this is negligible)
      // and then take the top nProbe. No parallelism needed here — 100 ops
      // is trivially fast.
      Span<float> centroidRankingDistance = stackalloc float[nProbe];
      Span<int> centroidRankingIdx = stackalloc int[nProbe];
      centroidRankingDistance.Fill(float.PositiveInfinity);

      for (int i = 0; i < K; i++)
      {
         var dist = VectorMath.SquaredEuclidean(query, _centroids[i]);
         TopKHeap.InsertTopK(centroidRankingDistance, centroidRankingIdx, dist, i );
      }

      // ── Step 2: collect candidates from the top nProbe clusters ─────────
      // We search each probed cluster and use a max-heap of size topK: 
      // we maintain the K best candidates seen so far, evicting the worst
      // when we find something better. This avoids sorting the full
      // candidate pool (~nProbe * 30k entries).
      
      Span<float> bestDistances = stackalloc float[topK];
      Span<bool> bestFlags = stackalloc bool[topK];
      bestDistances.Fill(float.PositiveInfinity);

      for(int i = 0; i < nProbe; i++)
      {
         int clusterIdx = centroidRankingIdx[i];
         var list = _invertedLists[clusterIdx];

         VectorMath.SquaredEuclidean(query, list, bestDistances, bestFlags);
      };

      return bestFlags.Count(true);
   }
}
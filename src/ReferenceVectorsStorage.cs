using System.Text.Json;
using Hnsw;
using Hnsw.RamStorage;

namespace FraudDetection;

public sealed class ReferenceVectorsStorage : IDisposable
{
   private readonly IConfiguration _configuration;
   private readonly RamStorageProvider _storage = new();
   private readonly HnswIndex _index;
   private readonly Dictionary<Guid, bool> _resultsDic = [];

   public ReferenceVectorsStorage(IConfiguration configuration)
   {
      _index = new HnswIndex(14, _storage)
      {
         M = 16,
         EfConstruction = 200,
         DistanceFunction = new EuclideanDistance() // built-in, SIMD-accelerated
      };
      _configuration = configuration;
   }

   public void Dispose()
   {
      _storage?.Dispose();
   }

   public async Task Initialize()
   {
      const int batchSize = 100;

      Console.WriteLine("Loading json");
      var references = await LoadReferences();
      var vectorsDic = new Dictionary<Guid, List<float>>(batchSize);
      Console.WriteLine($"Creating vectors for {references.Count} references");
      for (int i = 0; i < references.Count; i += batchSize)
      {
         vectorsDic.Clear();
         foreach(var reference in references.Skip(i).Take(batchSize))
         {
            var id = Guid.NewGuid();
            vectorsDic.Add(id, reference.Vector);
            _resultsDic.Add(id, reference.Label.Equals("fraud", StringComparison.OrdinalIgnoreCase));
         }

         Console.WriteLine($"Adding vectors with {batchSize}: {i} of {references.Count}  ({100.0 * i / references.Count:0.00}%)");
         await _index.AddNodesAsync(vectorsDic);
      }

      Console.WriteLine("Done");
   }

   private async Task<List<Reference>> LoadReferences()
   {
      var path = _configuration.GetSection("ReferencesFilePath").Get<string>() ??
         throw new InvalidDataException("Could not load normalization configuration");

      using var openStream = File.OpenRead(path);

      return (await JsonSerializer.DeserializeAsync(openStream, AppJsonContext.Default.ListReference))!;
   }

   public async Task<int> GetFraudCountFromKNearest(List<float> query)
   {
      IEnumerable<VectorResult> neighbors = await _index.GetTopKAsync(query, count: 5);
      //foreach (VectorResult r in neighbors)
      //{
      //   Console.WriteLine($"id={r.GUID} distance={r.Distance:F4} result={_resultsDic[r.GUID]}");
      //}

      return neighbors.Count(it => _resultsDic[it.GUID]);
   }
}
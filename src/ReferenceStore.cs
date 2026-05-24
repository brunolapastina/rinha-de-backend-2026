using System.Text.Json;

namespace FraudDetection;

public class ReferenceStore
{
   public List<(float[] Vector, bool IsFraud)> References { get; }

   public ReferenceStore(IConfiguration configuration)
   {
      var references = LoadReferences(configuration);

      References = references.Select(it =>
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
}
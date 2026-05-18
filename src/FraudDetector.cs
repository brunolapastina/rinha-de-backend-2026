using System.Text.Json;
using System.Text.Json.Nodes;

namespace FraudDetection;

public sealed class FraudDetector(IConfiguration configuration) : IDisposable
{
   private NormalizationConfig _normalizationConfig = null!;
   private Dictionary<string, float> _mccRisk = null!;
   private ReferenceVectorsStorage _referenceVectors = new(configuration);

   private async Task<NormalizationConfig> LoadNormalization()
   {
      var path = configuration.GetSection("NormalizationFilePath").Get<string>() ??
         throw new InvalidDataException("Could not load normalization configuration");

      using var openStream = File.OpenRead(path);

      return await JsonSerializer.DeserializeAsync(openStream, AppJsonContext.Default.NormalizationConfig) ??
         throw new InvalidDataException("Could not load normalization configuration");
   }

   public void Dispose()
   {
      _referenceVectors?.Dispose();
   }

   private async Task<Dictionary<string, float>> LoadMccRisk()
   {
      var path = configuration.GetSection("MccRiskFilePath").Get<string>() ??
         throw new InvalidDataException("Could not load normalization configuration");

      JsonNode? json;
      using (var openStream = File.OpenRead(path))
      {
         json = await JsonNode.ParseAsync(openStream);
      }

      Dictionary<string, float> mccRisk = [];
      foreach (KeyValuePair<string, JsonNode?> property in json!.AsObject())
      {
         string key = property.Key;
         JsonNode? value = property.Value;

         mccRisk.Add(property.Key, property.Value!.GetValue<float>());
      }

      return mccRisk;
   }

   private float GetMccRisk(string merchantId) =>
      _mccRisk.TryGetValue(merchantId, out float risk) ? risk : 0.5f;

   public async Task Initialize()
   {
      _normalizationConfig = await LoadNormalization();
      _mccRisk = await LoadMccRisk();
      await _referenceVectors.Initialize();
   }

   public async Task<int> GetFraudCount(FraudScoreRequest req)
   {
      var vector = VectorizeTransaction(req);
      return await _referenceVectors.GetFraudCountFromKNearest(vector);
   }

   private List<float> VectorizeTransaction(FraudScoreRequest req)
   {
      var vector = new List<float>(14)
      {
         Clamp(((float)req.Transaction.Amount) / _normalizationConfig.MaxAmount),
         Clamp(req.Transaction.Installments / _normalizationConfig.MaxInstallments),
         Clamp((float)(req.Transaction.Amount / req.Customer.AvgAmount) / _normalizationConfig.AmountVsAvgRatio),
         (float)req.Transaction.RequestedAt.Hour / 23,
         (float)req.Transaction.RequestedAt.DayOfWeek / 6,
         req.LastTransaction is null ? -1 : Clamp(((float)(req.Transaction.RequestedAt - req.LastTransaction.Timestamp).TotalMinutes) / _normalizationConfig.MaxMinutes),
         req.LastTransaction is null ? -1 : Clamp(((float)req.LastTransaction.KmFromCurrent) / _normalizationConfig.MaxKm),
         Clamp(((float)req.Terminal.KmFromHome) / _normalizationConfig.MaxKm),
         Clamp(((float)req.Customer.TxCount24h) / _normalizationConfig.MaxTxCount24h),
         req.Terminal.IsOnline ? 1 : 0,
         req.Terminal.CardPresent ? 1 : 0,
         req.Customer.KnownMerchants.Contains(req.Merchant.Id) ? 0 : 1,
         GetMccRisk(req.Merchant.Mcc),
         Clamp(((float)req.Merchant.AvgAmount) / _normalizationConfig.MaxMerchantAvgAmount)
      };
      
      return vector;
   }

   private static float Clamp(float value) =>
      Math.Clamp(value, 0, 1);
}
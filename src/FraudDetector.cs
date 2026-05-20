using System.Runtime.CompilerServices;
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

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   private float GetMccRisk(string merchantId) =>
      _mccRisk.TryGetValue(merchantId, out float risk) ? risk : 0.5f;

   public async Task Initialize()
   {
      _normalizationConfig = await LoadNormalization();
      _mccRisk = await LoadMccRisk();
      await _referenceVectors.Initialize();
   }

   public int GetFraudCount(FraudScoreRequest req)
   {
      Span<float> vector = stackalloc float[16];
      Vectorize(req, vector);
      return _referenceVectors.GetFraudCountFromKNearest(vector);
   }

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   private void Vectorize(FraudScoreRequest req, Span<float> dst)
   {
      dst[0] = Clamp(req.Transaction.Amount / _normalizationConfig.MaxAmount);
      dst[1] = Clamp(req.Transaction.Installments / _normalizationConfig.MaxInstallments);
      dst[2] = Clamp((req.Transaction.Amount / req.Customer.AvgAmount) / _normalizationConfig.AmountVsAvgRatio);
      dst[3] = req.Transaction.RequestedAt.Hour / 23f;
      var requestAtUtc = req.Transaction.RequestedAt.UtcDateTime;
      dst[4] = GetDayOfWeekStartingOnMonday(requestAtUtc.DayOfWeek) / 6f;
      dst[5] = req.LastTransaction is null ? -1 : Clamp(((float)(requestAtUtc - req.LastTransaction.Timestamp.UtcDateTime).TotalMinutes) / _normalizationConfig.MaxMinutes);
      dst[6] = req.LastTransaction is null ? -1 : Clamp(req.LastTransaction.KmFromCurrent / _normalizationConfig.MaxKm);
      dst[7] = Clamp(req.Terminal.KmFromHome / _normalizationConfig.MaxKm);
      dst[8] = Clamp(req.Customer.TxCount24h / _normalizationConfig.MaxTxCount24h);
      dst[9] = req.Terminal.IsOnline ? 1 : 0;
      dst[10] = req.Terminal.CardPresent ? 1 : 0;
      dst[11] = req.Customer.KnownMerchants.Contains(req.Merchant.Id) ? 0 : 1;
      dst[12] = GetMccRisk(req.Merchant.Mcc);
      dst[13] = Clamp(req.Merchant.AvgAmount / _normalizationConfig.MaxMerchantAvgAmount);
      dst[14] = 0;
      dst[15] = 0;
   }

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   private static float Clamp(float value) =>
      Math.Clamp(value, 0, 1);

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   private static int GetDayOfWeekStartingOnMonday(DayOfWeek d) => d == DayOfWeek.Sunday ? 6 : (int)d - 1;
}
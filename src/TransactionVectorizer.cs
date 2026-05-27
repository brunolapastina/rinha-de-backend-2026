using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FraudDetection;

public sealed class TransactionVectorizer(IConfiguration configuration)
{
   private static readonly float[] _hourMapping = [ 
      0f/23f, 1f/23f, 2f/23f, 3f/23f, 4f/23f, 5f/23f, 6f/23f, 7f/23f, 8f/23f, 9f/23f, 
      10f/23f, 11f/23f, 12f/23f, 13f/23f, 14f/23f, 15f/23f, 16f/23f, 17f/23f, 18f/23f, 19f/23f,
      20f/23f, 21f/23f, 22f/23f, 23f/23f
      ];
   private static readonly float[] _dayOfTheWeekMapping = [6f/6f, 0f/6f, 1f/6f, 2f/6f, 3f/6f, 4f/6f, 5f/6f];

   private readonly NormalizationConfig _normalizationConfig = LoadNormalization(configuration);
   private readonly Dictionary<string, float> _mccRisk = LoadMccRisk(configuration);

   private static NormalizationConfig LoadNormalization(IConfiguration configuration)
   {
      var path = configuration.GetSection("NormalizationFilePath").Get<string>() ??
         throw new InvalidDataException("Could not load normalization configuration");

      using var openStream = File.OpenRead(path);

      return JsonSerializer.Deserialize(openStream, AppJsonContext.Default.NormalizationConfig) ??
         throw new InvalidDataException("Could not load normalization configuration");
   }

   private static Dictionary<string, float> LoadMccRisk(IConfiguration configuration)
   {
      var path = configuration.GetSection("MccRiskFilePath").Get<string>() ??
         throw new InvalidDataException("Could not load normalization configuration");

      JsonNode? json;
      using (var openStream = File.OpenRead(path))
      {
         json = JsonNode.Parse(openStream);
      }

      return json!.AsObject().ToDictionary( 
         prop => prop.Key, 
         prop => prop.Value!.GetValue<float>()
      );
   }

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   public void Vectorize(FraudScoreRequest req, Span<float> dst)
   {
      dst[0] = Clamp(req.Transaction.Amount / _normalizationConfig.MaxAmount);
      dst[1] = Clamp(req.Transaction.Installments / _normalizationConfig.MaxInstallments);
      dst[2] = Clamp(req.Transaction.Amount / req.Customer.AvgAmount / _normalizationConfig.AmountVsAvgRatio);
      dst[3] = _hourMapping[req.Transaction.RequestedAt.Hour];
      dst[4] = _dayOfTheWeekMapping[(int)req.Transaction.RequestedAt.DayOfWeek];
      
      if (req.LastTransaction is null)
      {
         dst[5] = -1;
         dst[6] = -1;
      }
      else
      {
         dst[5] = Clamp(((float)(req.Transaction.RequestedAt - req.LastTransaction.Timestamp).TotalMinutes) / _normalizationConfig.MaxMinutes);
         dst[6] = Clamp(req.LastTransaction.KmFromCurrent / _normalizationConfig.MaxKm);
      }

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
   private float GetMccRisk(string merchantId) =>
      _mccRisk.TryGetValue(merchantId, out float risk) ? risk : 0.5f;

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   private static float Clamp(float value)
   {
      if (value < 0f)
      {
            return 0f;
      }
      else if (value > 1f)
      {
            return 1f;
      }

      return value;
   }

   [MethodImpl(MethodImplOptions.AggressiveInlining)]
   private static int GetDayOfWeekStartingOnMonday(DayOfWeek d) => 
      d == DayOfWeek.Sunday ? 6 : (int)d - 1;
}
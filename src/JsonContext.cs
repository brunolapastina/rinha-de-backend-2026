using System.Text.Json.Serialization;

namespace FraudDetection;

public record FraudScoreRequest(
    Transaction Transaction,
    Customer Customer,
    Merchant Merchant,
    Terminal Terminal,
    [property: JsonPropertyName("last_transaction")] LastTransaction? LastTransaction   // nullable – no prior transaction
);

public record Transaction(
    [property: JsonPropertyName("amount")] float Amount, 
    [property: JsonPropertyName("installments")] int Installments, 
    [property: JsonPropertyName("requested_at")] DateTimeOffset RequestedAt);

public record Customer(
    [property: JsonPropertyName("avg_amount")] float AvgAmount, 
    [property: JsonPropertyName("tx_count_24h")] int TxCount24h, 
    [property: JsonPropertyName("known_merchants")] List<string> KnownMerchants);

public record Merchant(
    [property: JsonPropertyName("id")] string Id, 
    [property: JsonPropertyName("mcc")] string Mcc, 
    [property: JsonPropertyName("avg_amount")] float AvgAmount);

public record Terminal(
    [property: JsonPropertyName("is_online")] bool IsOnline, 
    [property: JsonPropertyName("card_present")] bool CardPresent, 
    [property: JsonPropertyName("km_from_home")] float KmFromHome);

public record LastTransaction(
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp, 
    [property: JsonPropertyName("km_from_current")] float KmFromCurrent);

public record FraudScoreResponse(
    [property: JsonPropertyName("approved")] bool Approved, 
    [property: JsonPropertyName("fraud_score")] float FraudScore);

public record NormalizationConfig(
    float MaxAmount,
    float MaxInstallments,
    float AmountVsAvgRatio,
    float MaxMinutes,
    float MaxKm,
    [property: JsonPropertyName("max_tx_count_24h")] float MaxTxCount24h,
    float MaxMerchantAvgAmount);

public record Reference(float[] Vector, string Label);

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(FraudScoreRequest))]
[JsonSerializable(typeof(Transaction))]
[JsonSerializable(typeof(Customer))]
[JsonSerializable(typeof(Merchant))]
[JsonSerializable(typeof(Terminal))]
[JsonSerializable(typeof(LastTransaction))]
[JsonSerializable(typeof(FraudScoreResponse))]
[JsonSerializable(typeof(NormalizationConfig))]
[JsonSerializable(typeof(Reference))]
[JsonSerializable(typeof(List<Reference>))]
internal partial class AppJsonContext : JsonSerializerContext { }

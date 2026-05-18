// Adapted from the HnswLite project
// https://github.com/jchristn/HnswLite/blob/main/src/HnswIndex/IDistanceFunction.cs
namespace FraudDetection.DistanceFunctions;


public interface IDistanceFunction
{
   /// <summary>
   /// Gets the name of the distance function.
   /// </summary>
   string Name { get; }

   /// <summary>
   /// Calculates the distance between two vectors.
   /// </summary>
   /// <param name="a">First vector. Cannot be null.</param>
   /// <param name="b">Second vector. Cannot be null.</param>
   /// <returns>Distance between the vectors.</returns>
   /// <exception cref="System.ArgumentNullException">Thrown when a or b is null.</exception>
   /// <exception cref="System.ArgumentException">Thrown when vectors have different dimensions.</exception>
   float Distance(ReadOnlySpan<float> spanA, ReadOnlySpan<float> spanB);
}
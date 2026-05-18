// Adapted from the HnswLite project
// https://github.com/jchristn/HnswLite/blob/main/src/HnswIndex/EuclidianDistance.cs
using System.Numerics;
using System.Runtime.InteropServices;

namespace FraudDetection.DistanceFunctions;

/// <summary>
/// Calculates Euclidean distance between vectors.
/// Thread-safe implementation. Uses SIMD-accelerated paths when hardware support is available.
/// </summary>
public class EuclideanDistance : IDistanceFunction
{
    /// <summary>
    /// Gets the name of the distance function.
    /// </summary>
    public string Name => "Euclidean";

    /// <summary>
    /// Calculates the Euclidean distance between two vectors.
    /// </summary>
    /// <param name="spanA">First vector.</param>
    /// <param name="spanB">Second vector.</param>
    /// <returns>The square of the Euclidean distance between the vectors.</returns>
    /// <exception cref="ArgumentException">Thrown when vectors have different dimensions.</exception>
    public float Distance(ReadOnlySpan<float> spanA, ReadOnlySpan<float> spanB)
    {
        if (spanA.Length != spanB.Length)
        {
            throw new ArgumentException($"Vectors must have the same dimension. Vector a has {spanA.Length} dimensions, vector b has {spanB.Length} dimensions.", nameof(spanB));
        }

        float sum = 0f;
        int i = 0;

        if (Vector.IsHardwareAccelerated)
        {
            int width = Vector<float>.Count;
            Vector<float> acc = Vector<float>.Zero;
            int limit = spanA.Length - width;

            for (; i <= limit; i += width)
            {
                var va = new Vector<float>(spanA.Slice(i, width));
                var vb = new Vector<float>(spanB.Slice(i, width));
                var diff = va - vb;
                acc += diff * diff;
            }
            sum = Vector.Dot(acc, Vector<float>.One);
        }

        for (; i < spanA.Length; i++)
        {
            float diff = spanA[i] - spanB[i];
            sum += diff * diff;
        }

        return sum;
    }
}
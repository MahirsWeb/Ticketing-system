using System.Text.Json;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Infrastructure.Persistence;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-06: vectors were JSON text parsed number by number on every search.</summary>
public class VectorStorageTests
{
    private static float[] Sample(int length, int seed)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, length).Select(_ => (float)(rng.NextDouble() * 2 - 1)).ToArray();
    }

    [Fact]
    public void Binary_round_trip_is_exact()
    {
        var vector = Sample(3072, 1);

        var bytes = EmbeddingCodec.Encode(vector);

        Assert.Equal(3072 * 4, bytes.Length); // 12 KB, against ~78 KB of UTF-16 JSON
        Assert.Equal(vector, EmbeddingCodec.Decode(bytes));
    }

    [Fact]
    public void Converting_from_the_legacy_json_loses_nothing()
    {
        // The old column held exactly what JsonSerializer.Serialize(float[]) produced.
        var vector = Sample(3072, 2).Concat(new[] { 0f, -0f, float.Epsilon, 1e-30f, 123456.789f }).ToArray();
        var json = JsonSerializer.Serialize(vector);

        var viaJson = EmbeddingCodec.FromLegacyJson(json)!;

        Assert.Equal(vector, viaJson);
        Assert.Equal(vector, EmbeddingCodec.Decode(EmbeddingCodec.Encode(viaJson)));
    }

    [Fact]
    public void Binary_is_far_smaller_than_the_json_it_replaces()
    {
        var vector = Sample(3072, 3);

        var binaryBytes = EmbeddingCodec.Encode(vector).Length;
        var jsonBytesInNvarchar = JsonSerializer.Serialize(vector).Length * sizeof(char); // nvarchar is UTF-16

        Assert.True(binaryBytes * 3 < jsonBytesInNvarchar, $"{binaryBytes} B binary vs {jsonBytesInNvarchar} B as JSON");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_legacy_json_means_no_vector(string? json) => Assert.Null(EmbeddingCodec.FromLegacyJson(json));

    [Fact]
    public void A_truncated_blob_is_rejected_instead_of_read_as_a_shorter_vector() =>
        Assert.Throws<InvalidDataException>(() => EmbeddingCodec.Decode(new byte[10]));

    [Fact]
    public void Normalised_vectors_have_length_one_and_dot_product_is_cosine_similarity()
    {
        var a = Sample(512, 4);
        var b = Sample(512, 5);

        var ua = IVectorIndex.Normalize(a);
        var ub = IVectorIndex.Normalize(b);

        Assert.Equal(1.0, Math.Sqrt(ua.Sum(x => (double)x * x)), 4);

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        Assert.Equal(dot / (Math.Sqrt(na) * Math.Sqrt(nb)), IVectorIndex.Dot(ua, ub), 4);
    }

    [Fact]
    public void A_vector_is_perfectly_similar_to_itself_regardless_of_scale()
    {
        var a = Sample(256, 6);
        var scaled = a.Select(x => x * 7.5f).ToArray();

        Assert.Equal(1.0, IVectorIndex.Dot(IVectorIndex.Normalize(a), IVectorIndex.Normalize(scaled)), 4);
    }

    [Fact]
    public void Vectors_of_different_dimensions_never_match()
    {
        // e.g. an old 3072-number vector against a query from a model that returns 768
        Assert.Equal(0f, IVectorIndex.Dot(IVectorIndex.Normalize(Sample(3072, 7)), IVectorIndex.Normalize(Sample(768, 8))));
    }

    [Fact]
    public void A_zero_vector_is_left_alone_instead_of_producing_NaN()
    {
        var zero = new float[8];

        Assert.Equal(zero, IVectorIndex.Normalize(zero));
        Assert.Equal(0f, IVectorIndex.Dot(IVectorIndex.Normalize(zero), IVectorIndex.Normalize(Sample(8, 9))));
    }
}

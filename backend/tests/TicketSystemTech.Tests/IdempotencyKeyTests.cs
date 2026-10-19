using TicketSystemTech.Application.Common;

namespace TicketSystemTech.Tests;

public class IdempotencyKeyTests
{
    [Theory]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("a1b2c3d4e5f60718293a4b5c6d7e8f90")] // the non-UUID fallback the frontend uses over plain http
    [InlineData("order_42-retry")]
    public void Accepts_opaque_tokens(string key) => Assert.True(IdempotencyKey.IsValid(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("<script>")]
    [InlineData("unicodé")]
    public void Rejects_anything_else(string? key) => Assert.False(IdempotencyKey.IsValid(key));

    [Fact]
    public void Enforces_the_length_limit()
    {
        Assert.True(IdempotencyKey.IsValid(new string('a', IdempotencyKey.MaxLength)));
        Assert.False(IdempotencyKey.IsValid(new string('a', IdempotencyKey.MaxLength + 1)));
    }
}

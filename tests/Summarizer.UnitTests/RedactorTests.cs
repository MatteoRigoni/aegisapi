using Summarizer.Security;

namespace Summarizer.UnitTests;

public class RedactorTests
{
    [Fact]
    public void Mask_RemovesSecrets()
    {
        var input = "Bearer abc123 X-API-Key: SECRETKEY user@example.com 192.168.0.1";
        var masked = Redactor.Mask(input);
        Assert.DoesNotContain("SECRETKEY", masked);
        Assert.DoesNotContain("user@example.com", masked);
        Assert.DoesNotContain("192.168.0.1", masked);
        Assert.Contains("Bearer ***", masked);
        Assert.Contains("X-API-Key:***", masked.Replace(" ",""));
    }
}

using Listener.Api.Security;
using Microsoft.Extensions.Configuration;

namespace Listener.Tests;

public class ApiKeyValidatorTests
{
    [Fact]
    public void RequiresConfiguredLongKeyAndExactValue()
    {
        var expected = new string('a', 32) + "-collector";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ListenerSecurity:CollectorApiKey"] = expected
        }).Build();
        var validator = new ApiKeyValidator(configuration);

        Assert.True(validator.IsConfigured(ApiClient.AndroidCollector));
        Assert.True(validator.IsValid(ApiClient.AndroidCollector, expected));
        Assert.False(validator.IsValid(ApiClient.AndroidCollector, expected + "x"));
        Assert.False(validator.IsConfigured(ApiClient.TonysGo));
    }
}

using Summarizer.Model;

public static class SeedData
{
    public static IncidentBundle GetSampleLogBundle()
    {
        var now = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(0, 50).Select(i =>
            new FeatureEventLite(now.AddSeconds(-i), "client-123", "GET:/api/orders", i%5==0?500:200, false, i%13==0?1:0, 3.2)
        ).ToList();

        return new IncidentBundle(
            "dev", "Rules", "RPS spike",
            events,
            new Dictionary<string,double>{{"rps",320},{"errRate",0.08}},
            new Dictionary<string,int>{{"/api/orders",120}},
            "seed sample"
        );
    }
}

using Gateway.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gateway.IntegrationTests
{
    public class IncidentBundleFactoryTests
    {
        [Fact]
        public void Create_BuildsCountersAndTopPaths()
        {
            var factory = new IncidentBundleFactory(5);
            var now = DateTimeOffset.UtcNow;
            factory.Add(new FeatureEventLite(now, "c1", "/a", 200, false, 0, 1));
            factory.Add(new FeatureEventLite(now, "c1", "/a", 400, false, 0, 1));
            factory.Add(new FeatureEventLite(now, "c2", "/b", 500, false, 0, 1));
            var bundle = factory.Create("dev", "reason");
            Assert.Equal(3, bundle.RecentEvents.Count);
            Assert.Equal(3, bundle.Counters["rps"]);
            Assert.Equal(2.0 / 3, bundle.Counters["errRate"]);
            Assert.Equal(2, bundle.TopPaths["/a"]);
            Assert.Equal(1, bundle.TopPaths["/b"]);
        }
    }
}

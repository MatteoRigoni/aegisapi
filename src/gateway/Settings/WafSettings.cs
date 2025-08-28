namespace Gateway.Settings
{
    public class WafSettings
    {
        public bool SqlInjection { get; set; } = true;
        public bool Xss { get; set; } = true;
        public bool PathTraversal { get; set; } = true;
        public bool Ssrf { get; set; } = true;
    }
}

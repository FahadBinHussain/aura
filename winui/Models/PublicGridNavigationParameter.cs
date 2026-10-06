namespace Aura.Models
{
    public sealed class PublicGridNavigationParameter
    {
        public string PlatformName { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;

        public PublicGridNavigationParameter()
        {
        }

        public PublicGridNavigationParameter(string platformName, string mode)
        {
            PlatformName = platformName;
            Mode = mode;
        }
    }
}

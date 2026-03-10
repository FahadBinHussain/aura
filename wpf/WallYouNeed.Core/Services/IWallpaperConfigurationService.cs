using System.Threading.Tasks;
using WallYouNeed.Core.Configuration;

namespace WallYouNeed.Core.Services
{
    public interface IWallpaperConfigurationService
    {
        /// <summary>
        /// Gets the Backiee scraper configuration
        /// </summary>
        /// <returns>The current Backiee scraper configuration</returns>
        Task<BackieeScraperConfig> GetBackieeConfigAsync();

        /// <summary>
        /// Updates the Backiee scraper configuration
        /// </summary>
        /// <param name="config">The updated configuration</param>
        /// <returns>A task representing the asynchronous operation</returns>
        Task UpdateBackieeConfigAsync(BackieeScraperConfig config);
    }
} 
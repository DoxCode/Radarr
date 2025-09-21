using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Download
{
    public class ClearFailedMagnetHandler : IHandle<DownloadFailedEvent>
    {
        private readonly IMovieService _movieService;
        private readonly Logger _logger;

        public ClearFailedMagnetHandler(IMovieService movieService, Logger logger)
        {
            _movieService = movieService;
            _logger = logger;
        }

        public void Handle(DownloadFailedEvent message)
        {
            _logger.Info("ClearFailedMagnetHandler: Procesando fallo de descarga");

            var trackedDownload = message.TrackedDownload;

            if (trackedDownload?.RemoteMovie != null)
            {
                var release = trackedDownload?.RemoteMovie?.Release;
                if (release != null)
                {
                    _logger.Info("ClearFailedMagnetHandler: Release disponible, release.Guid: {0}, release.Title: {1}, release.InfoUrl: {2}, release.IndexerId: {3}", release?.Guid, release?.Title, release?.InfoUrl, release?.IndexerId);
                }
                else
                {
                    _logger.Info("ClearFailedMagnetHandler: Release no disponible");
                }
            }

            if (trackedDownload?.DownloadItem != null)
            {
                var downloadItem = trackedDownload.DownloadItem;
                if (downloadItem != null)
                {
                    _logger.Info("ClearFailedMagnetHandler: DownloadItem disponible, downloadItem.Title: {0}, downloadItem.DownloadId: {1}, downloadItem.DownloadClientInfo.Id: {2}", downloadItem?.Title, downloadItem?.DownloadId, downloadItem?.DownloadClientInfo?.Id);
                }
                else
                {
                    _logger.Info("ClearFailedMagnetHandler: DownloadItem no disponible");
                }
            }

            if (trackedDownload?.RemoteMovie != null)
            {
                var parsedMovie = trackedDownload?.RemoteMovie?.ParsedMovieInfo;
                if (parsedMovie != null)
                {
                    _logger.Info("ClearFailedMagnetHandler: ParsedMovie disponible, parsedMovie.ImdbId: {0}, parsedMovie.OriginalTitle: {1}, parsedMovie.PrimaryMovieTitle: {2}, parsedMovie.ReleaseTitle: {3}", parsedMovie?.ImdbId, parsedMovie?.OriginalTitle, parsedMovie?.PrimaryMovieTitle, parsedMovie?.ReleaseTitle);
                }
                else
                {
                    _logger.Info("ClearFailedMagnetHandler: ParsedMovie no disponible");
                }

                var movie = trackedDownload.RemoteMovie.Movie;
                _logger.Info("ClearFailedMagnetHandler: MOVIE: TrackedDownload o Movie no disponible");

                if (!string.IsNullOrWhiteSpace(movie.ExternalMagnet))
                {
                    _logger.Info("ClearFailedMagnetHandler: ExternalMagnet disponible, movie.Title: {0}, movie.ExternalMagnet: {1}", movie.Title, movie.ExternalMagnet);
                    movie.ExternalMagnet = "";
                    _logger.Info("ClearFailedMagnetHandler: No hay ExternalMagnet para película '{0}' ExternalMagnet: {1}", movie.Title, movie.ExternalMagnet);
                    return;
                }

                _logger.Info("__ENDED MOVIE__");
            }

            _logger.Info("__ENDED__");
        }
    }
}

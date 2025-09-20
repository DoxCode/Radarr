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

            var release = trackedDownload.RemoteMovie.Release;
            _logger.Debug("ClearFailedMagnetHandler: Release no disponible, release.Guid: {0}, release.Title: {1}, release.InfoUrl: {2}, release.IndexerId: {3}", release?.Guid, release?.Title, release?.InfoUrl, release?.IndexerId);

            var downloadItem = trackedDownload?.DownloadItem;
            _logger.Debug("ClearFailedMagnetHandler: DownloadItem no disponible, downloadItem.Title: {0}, downloadItem.DownloadId: {1}, downloadItem.DownloadClientInfo.Id: {2}", downloadItem?.Title, downloadItem?.DownloadId, downloadItem?.DownloadClientInfo?.Id);

            var parsedMovie = trackedDownload.RemoteMovie.ParsedMovieInfo;
            _logger.Debug("ClearFailedMagnetHandler: ParsedMovie no disponible, parsedMovie.ImdbId: {0}, parsedMovie.OriginalTitle: {1}, parsedMovie.PrimaryMovieTitle: {2}, parsedMovie.ReleaseTitle: {3}", parsedMovie.ImdbId, parsedMovie.OriginalTitle, parsedMovie.PrimaryMovieTitle, parsedMovie.ReleaseTitle);

            if (trackedDownload?.RemoteMovie?.Movie != null)
            {
                var movie = trackedDownload.RemoteMovie.Movie;
                _logger.Debug("ClearFailedMagnetHandler: MOVIE: TrackedDownload o Movie no disponible");

                if (!string.IsNullOrWhiteSpace(movie.ExternalMagnet))
                {
                    movie.ExternalMagnet = "";
                    _logger.Debug("ClearFailedMagnetHandler: No hay ExternalMagnet para película '{0}'", movie.Title);
                    return;
                }

                _logger.Debug("__ENDED__");
            }
        }
    }
}

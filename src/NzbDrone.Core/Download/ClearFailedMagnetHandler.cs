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

            if (trackedDownload?.RemoteMovie?.Movie == null)
            {
                _logger.Debug("ClearFailedMagnetHandler: TrackedDownload o Movie no disponible");
                return;
            }

            var movie = trackedDownload.RemoteMovie.Movie;

            if (string.IsNullOrWhiteSpace(movie.ExternalMagnet))
            {
                _logger.Debug("ClearFailedMagnetHandler: No hay ExternalMagnet para película '{0}'", movie.Title);
                return;
            }

            _logger.Info("ClearFailedMagnetHandler: Limpiando ExternalMagnet para película '{0}' (ID: {1}) debido a fallo de descarga", movie.Title, movie.Id);
            movie.ExternalMagnet = "";
        }
    }
}

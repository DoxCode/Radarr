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
            _logger.Info("ClearFailedMagnetHandler: Event data - Message: {0}, TrackedDownload: {1}, MovieId: {2}",
                         message != null ? "NOT NULL" : "NULL",
                         message?.TrackedDownload != null ? "NOT NULL" : "NULL",
                         message?.MovieId);

            // Intentar obtener la película directamente por MovieId del evento
            if (message?.MovieId > 0)
            {
                _logger.Info("ClearFailedMagnetHandler: Obteniendo película por MovieId: {0}", message.MovieId);

                var movie = _movieService.GetMovie(message.MovieId);

                if (movie != null && !string.IsNullOrWhiteSpace(movie.ExternalMagnet))
                {
                    _logger.Info("ClearFailedMagnetHandler: Limpiando ExternalMagnet para película '{0}' (ID: {1}). ExternalMagnet anterior: {2}",
                                 movie.Title,
                                 movie.Id,
                                 movie.ExternalMagnet);

                    // Limpiar el ExternalMagnet para evitar bucle infinito
                    movie.ExternalMagnet = null;

                    // CRÍTICO: Guardar los cambios en la base de datos
                    _movieService.UpdateMovie(movie);

                    _logger.Info("ClearFailedMagnetHandler: ExternalMagnet limpiado exitosamente para película '{0}'. Futuras búsquedas usarán indexers.", movie.Title);
                }
                else if (movie != null)
                {
                    _logger.Info("ClearFailedMagnetHandler: No hay ExternalMagnet para película '{0}' - no se requiere limpieza", movie.Title);
                }
                else
                {
                    _logger.Info("ClearFailedMagnetHandler: No se pudo obtener la película con ID: {0}", message.MovieId);
                }
            }
            else
            {
                _logger.Info("ClearFailedMagnetHandler: MovieId no válido en el evento: {0}", message?.MovieId);
            }

            _logger.Info("__ENDED__");
        }
    }
}

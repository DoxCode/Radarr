using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Download
{
    public class ClearFailedMagnetHandler : IHandle<DownloadFailedEvent>
    {
        private readonly IMovieService _movieService;
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        // Cache para evitar procesamiento repetitivo (thread-safe)
        private static readonly ConcurrentDictionary<int, DateTime> _recentlyProcessed = new ConcurrentDictionary<int, DateTime>();
        private static readonly TimeSpan _processingCooldown = TimeSpan.FromMinutes(10); // Aumentado de 5 a 10 minutos

        public ClearFailedMagnetHandler(IMovieService movieService, IHistoryService historyService, Logger logger)
        {
            _movieService = movieService;
            _historyService = historyService;
            _logger = logger;
        }

        public void Handle(DownloadFailedEvent message)
        {
            try
            {
                // Validaciones básicas tempranas
                if (message?.MovieId <= 0)
                {
                    _logger.Debug("ClearFailedMagnetHandler: MovieId no válido ({0}), omitiendo", message?.MovieId);
                    return;
                }

                // CRÍTICO: Verificar cooldown MUY TEMPRANO para evitar procesamiento duplicado
                if (IsInCooldown(message.MovieId))
                {
                    _logger.Debug("ClearFailedMagnetHandler: Película {0} en cooldown, omitiendo", message.MovieId);
                    return;
                }

                // Marcar como procesado INMEDIATAMENTE para prevenir duplicados
                MarkAsProcessed(message.MovieId);

                var movie = _movieService.GetMovie(message.MovieId);

                if (movie == null)
                {
                    _logger.Debug("ClearFailedMagnetHandler: Película no encontrada con ID: {0}", message.MovieId);
                    return;
                }

                // CRÍTICO: Solo procesar si realmente hay un ExternalMagnet
                if (string.IsNullOrWhiteSpace(movie.ExternalMagnet))
                {
                    _logger.Debug("ClearFailedMagnetHandler: No hay ExternalMagnet para película '{0}' (ID: {1}) - omitiendo",
                                  movie.Title,
                                  movie.Id);
                    return;
                }

                // Verificar si ha habido múltiples fallos recientes (EXCLUIR eventos muy recientes)
                if (HasTooManyRecentFailures(movie.Id))
                {
                    _logger.Info("ClearFailedMagnetHandler: Múltiples fallos recientes para película '{0}', limpiando ExternalMagnet",
                                 movie.Title);

                    // Limpiar el ExternalMagnet
                    movie.ExternalMagnet = null;
                    _movieService.UpdateMovie(movie);

                    _logger.Info("ClearFailedMagnetHandler: ExternalMagnet limpiado exitosamente para película '{0}' (ID: {1})",
                                 movie.Title,
                                 movie.Id);
                }
                else
                {
                    _logger.Debug("ClearFailedMagnetHandler: Fallos insuficientes para película '{0}', manteniendo ExternalMagnet",
                                  movie.Title);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "ClearFailedMagnetHandler: Error al procesar fallo de descarga para MovieId: {0}", message?.MovieId);
            }
        }

        private bool IsInCooldown(int movieId)
        {
            if (_recentlyProcessed.TryGetValue(movieId, out var lastProcessed))
            {
                return DateTime.UtcNow - lastProcessed < _processingCooldown;
            }

            return false;
        }

        private void MarkAsProcessed(int movieId)
        {
            _recentlyProcessed[movieId] = DateTime.UtcNow;

            // Limpiar entradas antiguas para evitar memory leak (thread-safe)
            var cutoff = DateTime.UtcNow.AddHours(-1);
            var keysToRemove = new List<int>();

            foreach (var kvp in _recentlyProcessed)
            {
                if (kvp.Value < cutoff)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _recentlyProcessed.TryRemove(key, out _);
            }
        }

        private bool HasTooManyRecentFailures(int movieId)
        {
            try
            {
                var cutoffTime = DateTime.UtcNow.AddMinutes(-30);

                // Obtener fallos recientes EXCLUYENDO los últimos 2 minutos para evitar falsos positivos
                var recentFailures = _historyService.GetByMovieId(movieId, MovieHistoryEventType.DownloadFailed)
                    .Where(h => h.Date > cutoffTime && h.Date < DateTime.UtcNow.AddMinutes(-2))
                    .ToList();

                _logger.Debug("ClearFailedMagnetHandler: Película {0} tiene {1} fallos en los últimos 30 minutos (excluyendo últimos 2 min)",
                             movieId,
                             recentFailures.Count);

                // Requerir al menos 5 fallos para limpiar ExternalMagnet (más conservador)
                return recentFailures.Count >= 5;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "ClearFailedMagnetHandler: Error al verificar historial de fallos para MovieId: {0}", movieId);
                return false;
            }
        }
    }
}

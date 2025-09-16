using System.Collections.Generic;
using System.Linq;
using MonoTorrent;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public class SaveSuccessfulMagnetHandler : IHandle<DownloadCompletedEvent>
    {
        private readonly IMovieService _movieService;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public SaveSuccessfulMagnetHandler(IMovieService movieService, IHttpClient httpClient, Logger logger)
        {
            _movieService = movieService;
            _httpClient = httpClient;
            _logger = logger;
        }

        public void Handle(DownloadCompletedEvent message)
        {
            var trackedDownload = message.TrackedDownload;

            // Solo procesar torrents completados exitosamente
            if (trackedDownload?.RemoteMovie?.Release == null ||
                trackedDownload.Protocol != Indexers.DownloadProtocol.Torrent)
            {
                return;
            }

            var torrentInfo = trackedDownload.RemoteMovie.Release as TorrentInfo;
            if (torrentInfo == null)
            {
                return;
            }

            var movie = trackedDownload.RemoteMovie.Movie;
            if (movie == null)
            {
                _logger.Warn("No se encontró la película para guardar el magnet exitoso");
                return;
            }

            try
            {
                var magnetUrl = GetOrGenerateMagnetUrl(torrentInfo, trackedDownload);

                if (!string.IsNullOrWhiteSpace(magnetUrl))
                {
                    // Guardar el magnet URL exitoso en el campo ExternalMagnet
                    movie.ExternalMagnet = magnetUrl;
                    _movieService.UpdateMovie(movie);

                    _logger.Debug("Magnet URL guardado exitosamente para la película '{0}': {1}",
                        movie.Title,
                        magnetUrl);
                }
                else
                {
                    _logger.Debug("No se pudo obtener o generar un magnet URL para la película '{0}'", movie.Title);
                }
            }
            catch (System.Exception ex)
            {
                _logger.Error(ex, "Error al guardar el magnet URL para la película '{0}'", movie.Title);
            }
        }

        private string GetOrGenerateMagnetUrl(TorrentInfo torrentInfo, TrackedDownload trackedDownload)
        {
            // 1. Si ya tenemos un magnet URL válido, usarlo
            if (!string.IsNullOrWhiteSpace(torrentInfo.MagnetUrl) &&
                torrentInfo.MagnetUrl.StartsWith("magnet:", System.StringComparison.OrdinalIgnoreCase))
            {
                return torrentInfo.MagnetUrl;
            }

            // 2. Si tenemos un archivo torrent (no magnet), extraer información completa incluyendo trackers
            if (!string.IsNullOrWhiteSpace(torrentInfo.DownloadUrl) &&
                !torrentInfo.DownloadUrl.StartsWith("magnet:", System.StringComparison.OrdinalIgnoreCase))
            {
                var enhancedMagnet = ExtractMagnetFromTorrentFile(torrentInfo.DownloadUrl, torrentInfo.Title);
                if (!string.IsNullOrWhiteSpace(enhancedMagnet))
                {
                    return enhancedMagnet;
                }
            }

            // 3. Si tenemos InfoHash, generar magnet URL básico
            if (!string.IsNullOrWhiteSpace(torrentInfo.InfoHash))
            {
                return GenerateMagnetFromInfoHash(torrentInfo.InfoHash, torrentInfo.Title);
            }

            // 4. Si tenemos DownloadId que parece ser un hash, intentar usarlo
            var downloadId = trackedDownload.DownloadItem?.DownloadId;
            if (!string.IsNullOrWhiteSpace(downloadId) && IsValidHash(downloadId))
            {
                return GenerateMagnetFromInfoHash(downloadId, torrentInfo.Title);
            }

            // 5. No se puede generar magnet URL
            return null;
        }

        private string GenerateMagnetFromInfoHash(string infoHash, string displayName = null, List<string> trackers = null)
        {
            // Limpiar el hash (remover espacios, convertir a lowercase)
            var cleanHash = infoHash.Replace(" ", "").ToLowerInvariant();

            // Validar que sea un hash válido (40 caracteres hexadecimales para SHA-1)
            if (!IsValidHash(cleanHash))
            {
                return null;
            }

            var magnetUrl = $"magnet:?xt=urn:btih:{cleanHash}";

            // Agregar nombre si está disponible (URL encoded)
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                var encodedName = System.Web.HttpUtility.UrlEncode(displayName);
                magnetUrl += $"&dn={encodedName}";
            }

            // Agregar trackers si están disponibles
            if (trackers != null && trackers.Any())
            {
                foreach (var tracker in trackers.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    var encodedTracker = System.Web.HttpUtility.UrlEncode(tracker);
                    magnetUrl += $"&tr={encodedTracker}";
                }
            }

            return magnetUrl;
        }

        private string ExtractMagnetFromTorrentFile(string torrentUrl, string displayName)
        {
            try
            {
                _logger.Debug("Intentando extraer magnet completo desde archivo torrent: {0}", torrentUrl);

                var request = new HttpRequest(torrentUrl);
                var response = _httpClient.Get(request);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Debug("No se pudo descargar el archivo torrent desde: {0}", torrentUrl);
                    return null;
                }

                var torrent = Torrent.Load(response.ResponseData);
                var infoHash = torrent.InfoHash.ToHex();

                // Extraer todos los trackers
                var trackers = new List<string>();
                foreach (var tier in torrent.AnnounceUrls)
                {
                    trackers.AddRange(tier);
                }

                var magnetUrl = GenerateMagnetFromInfoHash(infoHash, displayName, trackers);

                _logger.Debug("Magnet extraído exitosamente con {0} trackers: {1}",
                    trackers.Count,
                    magnetUrl);

                return magnetUrl;
            }
            catch (System.Exception ex)
            {
                _logger.Debug(ex, "Error al extraer magnet desde archivo torrent: {0}", torrentUrl);
                return null;
            }
        }

        private bool IsValidHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return false;
            }

            var cleanHash = hash.Replace(" ", "").ToLowerInvariant();

            // SHA-1 hash: 40 caracteres hexadecimales
            // SHA-256 hash: 64 caracteres hexadecimales (menos común en torrents)
            return (cleanHash.Length == 40 || cleanHash.Length == 64) &&
                   System.Text.RegularExpressions.Regex.IsMatch(cleanHash, "^[a-f0-9]+$");
        }
    }
}

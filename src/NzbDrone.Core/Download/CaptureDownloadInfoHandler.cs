using System.Collections.Generic;
using System.Linq;
using MonoTorrent;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public class CaptureDownloadInfoHandler : IHandle<DownloadStartedEvent>
    {
        private readonly IMovieService _movieService;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public CaptureDownloadInfoHandler(IMovieService movieService, IHttpClient httpClient, Logger logger)
        {
            _movieService = movieService;
            _httpClient = httpClient;
            _logger = logger;
            _logger.Info("CaptureDownloadInfoHandler: Handler registrado e inicializado correctamente");
        }

        public void Handle(DownloadStartedEvent message)
        {
            _logger.Info("CaptureDownloadInfoHandler: Capturando información de descarga iniciada");

            var remoteMovie = message.RemoteMovie;

            if (remoteMovie?.Movie == null || remoteMovie.Release == null)
            {
                _logger.Debug("CaptureDownloadInfoHandler: RemoteMovie o Release no disponible");
                return;
            }

            var movie = remoteMovie.Movie;
            var release = remoteMovie.Release;

            _logger.Info("CaptureDownloadInfoHandler: Procesando descarga para película '{0}' (ID: {1})", movie.Title, movie.Id);

            // Solo procesar torrents
            if (release.DownloadProtocol != Indexers.DownloadProtocol.Torrent)
            {
                _logger.Debug("CaptureDownloadInfoHandler: No es un torrent, omitiendo");
                return;
            }

            try
            {
                var magnetUrl = GetOrGenerateMagnetUrl(release, movie.Title);

                if (!string.IsNullOrWhiteSpace(magnetUrl))
                {
                    // Guardar el magnet URL en ExternalMagnet
                    movie.ExternalMagnet = magnetUrl;
                    _movieService.UpdateMovie(movie);

                    _logger.Info("CaptureDownloadInfoHandler: Magnet URL capturado y guardado para película '{0}'",
                        movie.Title);
                }
                else
                {
                    _logger.Info("CaptureDownloadInfoHandler: No se pudo obtener magnet URL para película '{0}'", movie.Title);
                }
            }
            catch (System.Exception ex)
            {
                _logger.Error(ex, "CaptureDownloadInfoHandler: Error al capturar información de descarga para película '{0}'", movie.Title);
            }
        }

        private string GetOrGenerateMagnetUrl(ReleaseInfo release, string movieTitle)
        {
            _logger.Info("CaptureDownloadInfoHandler: Intentando obtener/generar magnet URL");

            var torrentInfo = release as TorrentInfo;

            // 1. Si ya tenemos un magnet URL válido, usarlo
            if (!string.IsNullOrWhiteSpace(torrentInfo?.MagnetUrl) &&
                torrentInfo.MagnetUrl.StartsWith("magnet:", System.StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("CaptureDownloadInfoHandler: Usando magnet URL existente");
                return torrentInfo.MagnetUrl;
            }

            // 2. Si tenemos un archivo torrent (no magnet), extraer información completa incluyendo trackers
            if (!string.IsNullOrWhiteSpace(release.DownloadUrl) &&
                !release.DownloadUrl.StartsWith("magnet:", System.StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("CaptureDownloadInfoHandler: Intentando extraer magnet desde archivo torrent");
                var enhancedMagnet = ExtractMagnetFromTorrentFile(release.DownloadUrl, movieTitle);
                if (!string.IsNullOrWhiteSpace(enhancedMagnet))
                {
                    _logger.Info("CaptureDownloadInfoHandler: Magnet extraído exitosamente desde archivo torrent");
                    return enhancedMagnet;
                }
            }

            // 3. Si tenemos InfoHash, generar magnet URL básico
            if (!string.IsNullOrWhiteSpace(torrentInfo?.InfoHash))
            {
                _logger.Info("CaptureDownloadInfoHandler: Generando magnet básico desde InfoHash");
                return GenerateMagnetFromInfoHash(torrentInfo.InfoHash, movieTitle);
            }

            // 4. Si tenemos DownloadUrl como magnet, usarlo
            if (!string.IsNullOrWhiteSpace(release.DownloadUrl) &&
                release.DownloadUrl.StartsWith("magnet:", System.StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("CaptureDownloadInfoHandler: Usando DownloadUrl como magnet");
                return release.DownloadUrl;
            }

            // 5. No se puede generar magnet URL
            _logger.Info("CaptureDownloadInfoHandler: No fue posible generar magnet URL - información insuficiente");
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
                _logger.Debug("CaptureDownloadInfoHandler: Intentando extraer magnet completo desde archivo torrent: {0}", torrentUrl);

                var request = new HttpRequest(torrentUrl);
                var response = _httpClient.Get(request);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Debug("CaptureDownloadInfoHandler: No se pudo descargar el archivo torrent desde: {0}", torrentUrl);
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

                _logger.Debug("CaptureDownloadInfoHandler: Magnet extraído exitosamente con {0} trackers: {1}",
                    trackers.Count,
                    magnetUrl);

                return magnetUrl;
            }
            catch (System.Exception ex)
            {
                _logger.Debug(ex, "CaptureDownloadInfoHandler: Error al extraer magnet desde archivo torrent: {0}", torrentUrl);
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

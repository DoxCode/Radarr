using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.IndexerSearch
{
    public interface ISearchForReleases
    {
        Task<List<DownloadDecision>> MovieSearch(int movieId, bool userInvokedSearch, bool interactiveSearch);
        Task<List<DownloadDecision>> MovieSearch(Movie movie, bool userInvokedSearch, bool interactiveSearch);
    }

    public class ReleaseSearchService : ISearchForReleases
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly IMakeDownloadDecision _makeDownloadDecision;
        private readonly IMovieService _movieService;
        private readonly IMovieTranslationService _movieTranslationService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        public ReleaseSearchService(IIndexerFactory indexerFactory,
                                IMakeDownloadDecision makeDownloadDecision,
                                IMovieService movieService,
                                IMovieTranslationService movieTranslationService,
                                IQualityProfileService qualityProfileService,
                                IHistoryService historyService,
                                Logger logger)
        {
            _indexerFactory = indexerFactory;
            _makeDownloadDecision = makeDownloadDecision;
            _movieService = movieService;
            _movieTranslationService = movieTranslationService;
            _qualityProfileService = qualityProfileService;
            _historyService = historyService;
            _logger = logger;
        }

        public async Task<List<DownloadDecision>> MovieSearch(int movieId, bool userInvokedSearch, bool interactiveSearch)
        {
            var movie = _movieService.GetMovie(movieId);
            movie.MovieMetadata.Value.Translations = _movieTranslationService.GetAllTranslationsForMovieMetadata(movie.MovieMetadataId);

            return await MovieSearch(movie, userInvokedSearch, interactiveSearch);
        }

        public async Task<List<DownloadDecision>> MovieSearch(Movie movie, bool userInvokedSearch, bool interactiveSearch)
        {
            var downloadDecisions = new List<DownloadDecision>();
            var searchSpec = Get<MovieSearchCriteria>(movie, userInvokedSearch, interactiveSearch);

            // CRÍTICO: Circuit breaker para ExternalMagnet
            if (!string.IsNullOrWhiteSpace(movie.ExternalMagnet) &&
                !interactiveSearch &&
                !HasRecentFailures(movie.Id))
            {
                _logger.Info("ReleaseSearchService: ExternalMagnet present for movie {0}, creating direct download decision", movie.Id);

                try
                {
                    var torrentInfo = new TorrentInfo
                    {
                        // Magnet must be provided to DownloadService clients
                        DownloadUrl = movie.ExternalMagnet,
                        MagnetUrl  = movie.ExternalMagnet,
                        Title      = movie.Title ?? movie.MovieMetadata.Value.Title,
                        DownloadProtocol = DownloadProtocol.Torrent
                    };

                    var remoteMovie = new RemoteMovie
                    {
                        Release = torrentInfo,
                        Movie   = movie,
                        ParsedMovieInfo = new ParsedMovieInfo
                       {
                           // Minimal defaults so downstream consumers don't NRE / violate DB constraints
                           ReleaseTitle = torrentInfo.Title,
                           Quality = new QualityModel(Quality.Remux1080p),
                           Languages = new List<Language>()
                       },
                        MovieMatchType = MovieMatchType.Title
                    };

                    // Ensure the release is allowed / visible to downstream processors
                    remoteMovie.DownloadAllowed = true;

                    // Ensure Release.Title exists and source is marked
                    remoteMovie.Release.Title = torrentInfo.Title;
                    remoteMovie.Release.Indexer = string.Empty;
                    remoteMovie.Release.DownloadProtocol = DownloadProtocol.Torrent;

                    // Mark source (so history / pending know where it came from)
                    remoteMovie.ReleaseSource = searchSpec.InteractiveSearch ? ReleaseSourceType.InteractiveSearch
                                                : searchSpec.UserInvokedSearch ? ReleaseSourceType.UserInvokedSearch
                                                : ReleaseSourceType.Search;

                    // Return a DownloadDecision approved (no rejections) so ProcessDownloadDecisions will attempt grab
                    var decision = new DownloadDecision(remoteMovie);

                    _logger.Debug("ReleaseSearchService: Created download decision for ExternalMagnet: {0}", movie.ExternalMagnet);

                    return new List<DownloadDecision> { decision };
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "ReleaseSearchService: Error al procesar ExternalMagnet para película {0}", movie.Id);
                }
            }

            // Log cuando se omite ExternalMagnet para búsquedas interactivas
            if (!string.IsNullOrWhiteSpace(movie.ExternalMagnet) && interactiveSearch)
            {
                _logger.Debug("ExternalMagnet present for movie {0} but skipping for interactive search to allow manual selection.", movie.Id);
            }

            var decisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);
            downloadDecisions.AddRange(decisions);

            return DeDupeDecisions(downloadDecisions);
        }

        private TSpec Get<TSpec>(Movie movie, bool userInvokedSearch, bool interactiveSearch)
            where TSpec : SearchCriteriaBase, new()
        {
            var spec = new TSpec
            {
                Movie = movie,
                UserInvokedSearch = userInvokedSearch,
                InteractiveSearch = interactiveSearch
            };

            if (!string.IsNullOrWhiteSpace(movie.CustomName))
            {
                spec.SceneTitles = new List<string> { movie.CustomName };
                spec.ForceExactTitle = true;

                // Asegurar que los generadores que leen desde MovieMetadata usen el CustomName.
                if (spec.Movie?.MovieMetadata?.Value != null)
                {
                    spec.Movie.MovieMetadata.Value.Title = movie.CustomName;
                    spec.Movie.MovieMetadata.Value.OriginalTitle = movie.CustomName;

                    spec.Movie.MovieMetadata.Value.TmdbId = 0;
                    spec.Movie.MovieMetadata.Value.ImdbId = null;
                }

                return spec;
            }
            else
            {
                spec.ForceExactTitle = false;
            }

            _logger.Info("Get generate");

            // Solo forzar ExternalMagnet para búsquedas automáticas, no para búsquedas interactivas
            if (!string.IsNullOrWhiteSpace(movie.ExternalMagnet) && !interactiveSearch)
            {
                _logger.Info("IS EXTERNAL MAGNET");
                spec.ExternalMagnet = movie.ExternalMagnet;
                spec.ForceExternalMagnet = true;
            }

            var wantedLanguages = _qualityProfileService.GetAcceptableLanguages(movie.QualityProfileId);
            var translations = _movieTranslationService.GetAllTranslationsForMovieMetadata(movie.MovieMetadataId);

            var queryTranslations = new List<string>
            {
                movie.MovieMetadata.Value.Title,
                movie.MovieMetadata.Value.OriginalTitle
            };

            // Add Translation of wanted languages to search query
            foreach (var translation in translations.Where(a => wantedLanguages.Contains(a.Language)))
            {
                queryTranslations.Add(translation.Title);
            }

            spec.SceneTitles = queryTranslations.Where(t => t.IsNotNullOrWhiteSpace()).Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();

            return spec;
        }

        private async Task<List<DownloadDecision>> Dispatch(Func<IIndexer, Task<IList<ReleaseInfo>>> searchAction, SearchCriteriaBase criteriaBase)
        {
            var indexers = criteriaBase.InteractiveSearch ?
                _indexerFactory.InteractiveSearchEnabled() :
                _indexerFactory.AutomaticSearchEnabled();

            // Filter indexers to untagged indexers and indexers with intersecting tags
            indexers = indexers.Where(i => i.Definition.Tags.Empty() || i.Definition.Tags.Intersect(criteriaBase.Movie.Tags).Any()).ToList();

            _logger.ProgressInfo("Searching indexers for {0}. {1} active indexers", criteriaBase, indexers.Count);

            var tasks = indexers.Select(indexer => DispatchIndexer(searchAction, indexer, criteriaBase));

            var batch = await Task.WhenAll(tasks);

            var reports = batch.SelectMany(x => x).ToList();

            _logger.ProgressDebug("Total of {0} reports were found for {1} from {2} indexers", reports.Count, criteriaBase, indexers.Count);

            // Update the last search time for movie if at least 1 indexer was searched.
            if (indexers.Any())
            {
                var lastSearchTime = DateTime.UtcNow;
                _logger.Debug("Setting last search time to: {0}", lastSearchTime);

                criteriaBase.Movie.LastSearchTime = lastSearchTime;
                _movieService.UpdateLastSearchTime(criteriaBase.Movie);
            }

            return _makeDownloadDecision.GetSearchDecision(reports, criteriaBase).ToList();
        }

        private async Task<IList<ReleaseInfo>> DispatchIndexer(Func<IIndexer, Task<IList<ReleaseInfo>>> searchAction, IIndexer indexer, SearchCriteriaBase criteriaBase)
        {
            try
            {
                return await searchAction(indexer);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while searching for {0}", criteriaBase);
            }

            return Array.Empty<ReleaseInfo>();
        }

        private List<DownloadDecision> DeDupeDecisions(List<DownloadDecision> decisions)
        {
            // De-dupe reports by guid so duplicate results aren't returned. Pick the one with the least rejections and higher indexer priority.
            return decisions.GroupBy(d => d.RemoteMovie.Release.Guid)
                .Select(d => d.OrderBy(v => v.Rejections.Count()).ThenBy(v => v.RemoteMovie?.Release?.IndexerPriority ?? IndexerDefinition.DefaultPriority).First())
                .ToList();
        }

        private bool HasRecentFailures(int movieId)
        {
            try
            {
                var recentFailures = _historyService.GetByMovieId(movieId, MovieHistoryEventType.DownloadFailed)
                    .Where(h => h.Date > DateTime.UtcNow.AddMinutes(-30))
                    .ToList();

                var hasFailures = recentFailures.Any();

                if (hasFailures)
                {
                    _logger.Debug("ReleaseSearchService: Película {0} tiene fallos recientes, omitiendo ExternalMagnet", movieId);
                }

                return hasFailures;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "ReleaseSearchService: Error al verificar fallos recientes para MovieId: {0}", movieId);
                return false; // En caso de error, permitir el uso de ExternalMagnet
            }
        }
    }
}

using NzbDrone.Common.Messaging;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public class DownloadStartedEvent : IEvent
    {
        public RemoteMovie RemoteMovie { get; private set; }
        public string DownloadClientId { get; private set; }

        public DownloadStartedEvent(RemoteMovie remoteMovie, string downloadClientId)
        {
            RemoteMovie = remoteMovie;
            DownloadClientId = downloadClientId;
        }
    }
}

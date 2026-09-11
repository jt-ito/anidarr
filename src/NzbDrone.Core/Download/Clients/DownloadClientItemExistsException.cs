using System;

namespace NzbDrone.Core.Download.Clients
{
    public class DownloadClientItemExistsException : DownloadClientException
    {
        public DownloadClientItemExistsException(string message, params object[] args)
            : base(string.Format(message, args))
        {
        }

        public DownloadClientItemExistsException(string message)
            : base(message)
        {
        }

        public DownloadClientItemExistsException(string message, Exception innerException, params object[] args)
            : base(string.Format(message, args), innerException)
        {
        }

        public DownloadClientItemExistsException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

using System;

namespace SftpTransferAgent.Common.Logging
{
    public interface ILogger
    {
        void Write(LogLevel level, string message, Exception ex = null);
    }
}
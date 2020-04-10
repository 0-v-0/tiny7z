using Tiny7z.Common;
using System;

namespace Tiny7z.Archive
{
    public class SevenZipArchiveFile : ArchiveFile
    {
        public ulong? UnPackIndex;
        public MultiFileStream.Source Source;
        public SevenZipArchiveFile()
            : base()
        {
            UnPackIndex = null;
            Source = null;
        }
    }
}

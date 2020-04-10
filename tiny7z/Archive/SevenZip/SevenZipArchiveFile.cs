using Tiny7z.Common;

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

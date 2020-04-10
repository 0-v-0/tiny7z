using System;

namespace Tiny7z.Archive
{
    /// <summary>
    /// Represents one file in an archive
    /// </summary>
    public class ArchiveFile
    {
        public string Name;
        public ulong? Size;
        public uint? CRC;
        public DateTime? Time;
        public uint? Attributes;
        public bool IsEmpty;
        public bool IsDirectory;
        public bool IsDeleted;
        public ArchiveFile()
        {
            Name = null;
            Size = null;
            CRC = null;
            Time = null;
            Attributes = null;
            IsEmpty = false;
            IsDirectory = false;
            IsDeleted = false;
        }
    }
}

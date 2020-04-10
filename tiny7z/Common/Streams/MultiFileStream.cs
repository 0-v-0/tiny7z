using System;
using System.IO;

namespace Tiny7z.Common
{
    /// <summary>
    /// MultiFileStream - Allows treating a bunch of files sequentially to behave as if they're one stream.
    /// </summary>
    public class MultiFileStream : AbstractMultiStream
    {
        /// <summary>
        /// Multi-purpose container class. Holds either a file path or an already opened stream.
        /// </summary>
        public class Source
        {
            public Stream Get(FileAccess fileAccess)
            {
                Stream s = null;
                if (stream != null)
                {
                    s = stream;
                    if ((fileAccess == FileAccess.Read && !s.CanRead) || (fileAccess == FileAccess.Write && !s.CanWrite))
                        throw new IOException();
                }
                else if (filePath != null)
                {
                    if (fileAccess == FileAccess.Read)
                        s = File.Open(filePath, FileMode.Open, FileAccess.Read);
                    else
                        s = File.Open(filePath, FileMode.Create, FileAccess.Write);
                }
                Clear();
                return s;
            }

            public long Size()
            {
                if (stream != null)
                    return stream.Length;
                else if (filePath != null)
                    return new FileInfo(filePath).Length;
                return -1;
            }

            public Source Set(Stream stream)
            {
                this.stream = stream;
                filePath = null;
                return this;
            }

            public Source Set(string filePath)
            {
                stream = null;
                this.filePath = filePath;
                return this;
            }

            public Source Clear()
            {
                stream = null;
                filePath = null;
                return this;
            }

            public Source()
            {
                stream = null;
                filePath = null;
            }

            public Source(string FilePath)
            {
                stream = null;
                filePath = FilePath;
            }

            public Source(Stream Stream)
            {
                stream = Stream;
                filePath = null;
            }

            Stream stream;
            string filePath;
        }

        /// <summary>
        /// Overridden method returns source from list as next stream!
        /// </summary>
        protected override Stream NextStream()
        {
            return Sources[currentIndex].Get(fileAccess);
        }

        /// <summary>
        /// List of sources
        /// </summary>
        public Source[] Sources
        {
            get; private set;
        }

        /// <summary>
        /// Straightforward stream initialization.
        /// </summary>
        public MultiFileStream(FileAccess fileAccess, params Source[] sources)
            : base((ulong)sources.LongLength)
        {
            if (fileAccess == FileAccess.ReadWrite)
                throw new ArgumentException();
            if (sources == null || sources.Length == 0)
                throw new ArgumentOutOfRangeException();

            this.fileAccess = fileAccess;
            Sources = sources;
            for (long i = 0; i < sources.LongLength; ++i)
            {
                Sizes[i] = Sources[i].Size();
            }
        }

        /// <summary>
        /// Remember either read or write access.
        /// </summary>
        private FileAccess fileAccess;
    }
}

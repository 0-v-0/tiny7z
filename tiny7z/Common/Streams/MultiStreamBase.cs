﻿using System;
using System.IO;
using System.Linq;

namespace pdj.tiny7z.Common
{
    public abstract class MultiStreamBase : Stream
    {
        /// <summary>
        /// This is called when current stream is exhausted, to prepare next stream.
        /// </summary>
        protected abstract Stream NextStream();

        /// <summary>
        /// This is called when the current stream is exhausted, to properly close current stream.
        /// </summary>
        protected virtual void CloseStream()
        {
            internalStream.Close();
        }

        /// <summary>
        /// For reading streams, this will hold stream sizes once they have been exhausted. For writing streams, these have to be filled in advance.
        /// </summary>
        public long?[] Sizes
        {
            get; protected set;
        }

        /// <summary>
        /// Those are updated as reading/writing goes and are correct once the whole current stream has been exhausted.
        /// </summary>
        public uint?[] CRCs
        {
            get; protected set;
        }

        /// <summary>
        /// Private members.
        /// </summary>
        protected CRCStream internalStream;
        protected long numStreams;
        protected long currentIndex;
        private long currentOffset;
        private long currentPos;
        private long currentSize;

        /// <summary>
        /// Read capabilities (also gets stream ready).
        /// </summary>
        public override bool CanRead
        {
            get
            {
                getReady();
                return currentIndex < numStreams && internalStream is Stream && internalStream.CanRead;
            }
        }


        /// <summary>
        /// Write capabilities (also gets stream ready).
        /// </summary>
        public override bool CanWrite
        {
            get
            {
                getReady();
                return currentIndex < numStreams && Sizes != null && Sizes[currentIndex] != null && internalStream is Stream && internalStream.CanWrite;
            }
        }

        /// <summary>
        /// Can't seek on this stream.
        /// </summary>
        public override bool CanSeek => false;

        public override long Length
        {
            get
            {
                if (Sizes != null && Sizes.LongLength >= numStreams)
                {
                    return Sizes.Sum(size => (long)size);
                }
                return currentPos + currentSize;
            }
        }

        /// <summary>
        /// Getting position is fine. Setting, not!
        /// </summary>
        public override long Position
        {
            get => currentPos;
            set => throw new NotImplementedException();
        }

        /// <summary>
        /// The only required parameter is (max) number of streams.
        /// </summary>
        public MultiStreamBase(UInt64 numStreams)
            : base()
        {
            if (numStreams == 0)
                throw new ArgumentNullException();

            Sizes = new long?[numStreams];
            CRCs = new uint?[numStreams];

            this.internalStream = null;
            this.numStreams = (long)numStreams;
            currentIndex = -1;
            currentOffset = 0;
            currentPos = 0;
            currentSize = 0;
        }

        /// <summary>
        /// Properly close currently opened stream.
        /// </summary>
        public override void Close()
        {
            if (internalStream is Stream)
            {
                CloseStream();

                internalStream.Dispose();
                internalStream = null;
            }
        }

        public override void Write(byte[] array, int offset, int count)
        {
            if (CanWrite)
            {
                while (count > 0)
                {
                    // write data if some space remains in current stream
                    long internalRemain = (long)Sizes[currentIndex] - currentSize;
                    int w = count > internalRemain ? (int)internalRemain : count;
                    if (w > 0)
                    {
                        internalStream.Write(array, offset, w);

                        offset += w;
                        count -= w;
                        currentPos += w;
                        currentSize += w;
                        internalRemain -= w;
                    }
                    // if no more data remains in current stream, goes to next
                    if (internalRemain == 0)
                        if (!iterateStream())
                            break;
                }

                if (count > 0)
                    throw new EndOfStreamException();
            }
        }

        public override void WriteByte(byte value)
        {
            if (CanWrite)
            {
                // loop until we've written that one byte *sic*
                int count = 1;
                while (count > 0)
                {
                    // write byte if there's at least room for one byte
                    if (Sizes[currentIndex] > currentSize)
                    {
                        internalStream.WriteByte(value);
                        ++currentPos;
                        ++currentSize;
                        break;
                    }
                    else
                    {
                        if (!iterateStream())
                            break;
                    }
                }

                if (count > 0)
                    throw new EndOfStreamException();
            }
        }

        public override int Read(byte[] array, int offset, int count)
        {
            int read = 0;
            if (CanRead)
            {
                while (count > 0)
                {
                    int r = internalStream.Read(array, offset, count);
                    if (r == 0)
                    {
                        if (!iterateStream())
                            break;
                    }
                    else
                    {
                        offset += r;
                        read += r;
                        count -= r;
                        currentPos += r;
                        currentSize += r;
                    }
                }
            }
            return read;
        }

        public override int ReadByte()
        {
            int r = -1;
            if (CanRead)
            {
                r = internalStream.ReadByte();
                if (r == -1)
                {
                    if (iterateStream())
                        return ReadByte();
                }
                else
                {
                    ++currentPos;
                    ++currentSize;
                }
            }
            return r;
        }

        public override void Flush()
        {
            if (CanWrite)
            {
                internalStream.Flush();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Internal method to make sure stream is ready after first being initialized.
        /// </summary>
        private void getReady()
        {
            if (currentIndex == -1)
            {
                currentIndex = 0;
                this.internalStream = new CRCStream(NextStream(), false);
            }
        }

        /// <summary>
        /// Internal method to finish current stream and prepare next one.
        /// </summary>
        private bool iterateStream()
        {
            // calc crc
            CRCs[currentIndex] = internalStream.CRC;
            if (Sizes[currentIndex] == null)
                Sizes[currentIndex] = currentSize;

            // close stream properly
            CloseStream();
            internalStream.Dispose();
            internalStream = null;

            // reset per-stream counters
            currentOffset = currentPos;
            currentSize = 0;

            // get next stream if possible, break out of loop if done
            Stream nextStream = null;
            if (++currentIndex < numStreams)
                nextStream = NextStream();

            // set internal stream and wrap it with crc, if any
            if (nextStream != null)
                internalStream = new CRCStream(nextStream, false);

            return internalStream != null;
        }
    }
}
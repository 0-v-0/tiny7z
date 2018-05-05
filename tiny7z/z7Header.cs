﻿using pdj.tiny7z.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;

namespace pdj.tiny7z
{
    public partial class z7Header : IHeaderParser, IHeaderWriter
    {
        /// <summary>
        /// All valid property IDs
        /// </summary>
        public enum PropertyID
        {
            kEnd = 0x00,

            kHeader = 0x01,

            kArchiveProperties = 0x02,

            kAdditionalStreamsInfo = 0x03,
            kMainStreamsInfo = 0x04,
            kFilesInfo = 0x05,

            kPackInfo = 0x06,
            kUnPackInfo = 0x07,
            kSubStreamsInfo = 0x08,

            kSize = 0x09,
            kCRC = 0x0A,

            kFolder = 0x0B,

            kCodersUnPackSize = 0x0C,
            kNumUnPackStream = 0x0D,

            kEmptyStream = 0x0E,
            kEmptyFile = 0x0F,
            kAnti = 0x10,

            kName = 0x11,
            kCTime = 0x12,
            kATime = 0x13,
            kMTime = 0x14,
            kWinAttributes = 0x15,
            kComment = 0x16,

            kEncodedHeader = 0x17,

            kStartPos = 0x18,
            kDummy = 0x19,
        };

        public class Digests : IHeaderParser, IHeaderWriter
        {
            public UInt64 NumStreams;
            public bool[] Defined; // [NumStreams]
            public UInt64 NumDefined;
            public UInt32[] CRCs; // [NumDefined]
            public Digests(UInt64 NumStreams)
            {
                this.NumStreams = NumStreams;
                this.Defined = new bool[NumStreams];
                this.NumDefined = 0;
                this.CRCs = new UInt32[0];
            }

            public void Parse(Stream hs)
            {
                NumDefined = hs.ReadOptionalBoolVector(NumStreams, out Defined);

                CRCs = new UInt32[NumDefined];
                using (BinaryReader reader = new BinaryReader(hs, Encoding.Default, true))
                    for (ulong i = 0; i < NumDefined; ++i)
                        CRCs[i] = reader.ReadUInt32();
            }

            public void Write(Stream hs)
            {
                hs.WriteOptionalBoolVector(Defined);

                using (BinaryWriter writer = new BinaryWriter(hs, Encoding.Default, true))
                    for (ulong i = 0; i < NumDefined; ++i)
                        writer.Write((UInt32)CRCs[i]);
            }
        }

        public class ArchiveProperty : IHeaderParser, IHeaderWriter
        {
            public PropertyID Type;
            public UInt64 Size;
            public Byte[] Data;
            public ArchiveProperty(PropertyID type)
            {
                this.Type = type;
                this.Size = 0;
                this.Data = new Byte[0];
            }

            public void Parse(Stream hs)
            {
                Size = hs.ReadDecodedUInt64();
                if (Size > 0)
                    Data = hs.ReadThrow(Size);
            }

            public void Write(Stream hs)
            {
                hs.WriteByte((Byte)Type);
                hs.WriteEncodedUInt64(Size);
                if (Size > 0)
                    hs.Write(Data, 0, (int)Size);
            }
        }

        public class ArchiveProperties : IHeaderParser, IHeaderWriter
        {
            public List<ArchiveProperty> Properties; // [Arbitrary number]
            public ArchiveProperties()
            {
                this.Properties = new List<ArchiveProperty>();
            }

            public void Parse(Stream hs)
            {
                while (true)
                {
                    PropertyID propertyID = GetPropertyID(this, hs);
                    if (propertyID == PropertyID.kEnd)
                        return;

                    ArchiveProperty property = new ArchiveProperty(propertyID);
                    property.Parse(hs);
                    Properties.Add(property);
                }
            }

            public void Write(Stream hs)
            {
                foreach (var property in Properties)
                    property.Write(hs);
                hs.WriteByte((Byte)PropertyID.kEnd);
            }
        }

        public class PackInfo : IHeaderParser, IHeaderWriter
        {
            public UInt64 PackPos;
            public UInt64 NumPackStreams;
            public UInt64[] Sizes; // [NumPackStreams]
            public UInt32?[] CRCs; // [NumPackStreams]
            public PackInfo()
            {
                this.PackPos = 0;
                this.NumPackStreams = 0;
                this.Sizes = new UInt64[0];
                this.CRCs = new UInt32?[0];
            }

            public void Parse(Stream hs)
            {
                PackPos = hs.ReadDecodedUInt64();
                NumPackStreams = hs.ReadDecodedUInt64();
                Sizes = new UInt64[NumPackStreams­];
                CRCs = new UInt32?[NumPackStreams];
                while (true)
                {
                    PropertyID propertyID = GetPropertyID(this, hs);
                    switch (propertyID)
                    {
                        case PropertyID.kSize:
                            for (ulong i = 0; i < NumPackStreams; ++i)
                                Sizes[i] = hs.ReadDecodedUInt64();
                            break;
                        case PropertyID.kCRC:
                            {
                                Digests packStreamDigests = new Digests(NumPackStreams);
                                packStreamDigests.Parse(hs);
                                for (ulong i = 0, j = 0; i < packStreamDigests.NumStreams; ++i)
                                    if (packStreamDigests.Defined[i])
                                        CRCs[i] = packStreamDigests.CRCs[j++];
                            }
                            break;
                        case PropertyID.kEnd:
                            return;
                        default:
                            throw new NotImplementedException(propertyID.ToString());
                    }
                }
            }

            public void Write(Stream hs)
            {
                hs.WriteEncodedUInt64(PackPos);
                hs.WriteEncodedUInt64(NumPackStreams);

                hs.WriteByte((Byte)PropertyID.kSize);
                for (ulong i = 0; i < NumPackStreams; ++i)
                    hs.WriteEncodedUInt64(Sizes[i]);

                for (ulong i = 0; i < NumPackStreams; ++i)
                    if (CRCs[i] != null)
                    {
                        hs.WriteByte((Byte)PropertyID.kCRC);
                        Digests packStreamDigests = new Digests(NumPackStreams);
                        packStreamDigests.CRCs = new UInt32[NumPackStreams];
                        for (ulong j = 0, k = 0; j < NumPackStreams; ++j)
                            if (CRCs[j] != null)
                            {
                                packStreamDigests.Defined[j] = true;
                                packStreamDigests.CRCs[k++] = (UInt32)CRCs[j];
                                packStreamDigests.NumDefined++;
                            }
                        packStreamDigests.Write(hs);
                        break;
                    }

                hs.WriteByte((Byte)PropertyID.kEnd);
            }
        }

        public class CoderInfo : IHeaderParser, IHeaderWriter
        {
            public Byte Attributes;
            public Byte[] CodecId; // [CodecIdSize]
            public UInt64 NumInStreams;
            public UInt64 NumOutStreams;
            public UInt64 PropertiesSize;
            public Byte[] Properties; // [PropertiesSize]
            public CoderInfo()
            {
                this.Attributes = 0;
                this.CodecId = new Byte[0];
                this.NumInStreams = 0;
                this.NumOutStreams = 0;
                this.PropertiesSize = 0;
                this.Properties = new Byte[0];
            }

            public void Parse(Stream hs)
            {
                Attributes = hs.ReadByteThrow();
                int codecIdSize = (Attributes & 0b00001111);
                bool isComplexCoder = (Attributes & 0b00010000) > 0;
                bool hasAttributes = (Attributes & 0b00100000) > 0;

                CodecId = hs.ReadThrow((uint)codecIdSize);

                NumInStreams = NumOutStreams = 1;
                if (isComplexCoder)
                {
                    NumInStreams = hs.ReadDecodedUInt64();
                    NumOutStreams = hs.ReadDecodedUInt64();
                }

                PropertiesSize = 0;
                if (hasAttributes)
                {
                    PropertiesSize = hs.ReadDecodedUInt64();
                    Properties = hs.ReadThrow(PropertiesSize);
                }
            }

            public void Write(Stream hs)
            {
                hs.WriteByte(Attributes);
                int codecIdSize = (Attributes & 0b00001111);
                bool isComplexCoder = (Attributes & 0b00010000) > 0;
                bool hasAttributes = (Attributes & 0b00100000) > 0;

                hs.Write(CodecId, 0, codecIdSize);

                if (isComplexCoder)
                {
                    hs.WriteEncodedUInt64(NumInStreams);
                    hs.WriteEncodedUInt64(NumOutStreams);
                }

                if (hasAttributes)
                {
                    hs.WriteEncodedUInt64(PropertiesSize);
                    hs.Write(Properties, 0, (int)PropertiesSize);
                }
            }
        }

        public class BindPairsInfo : IHeaderParser, IHeaderWriter
        {
            public UInt64 InIndex;
            public UInt64 OutIndex;
            public BindPairsInfo()
            {
                this.InIndex = 0;
                this.OutIndex = 0;
            }

            public void Parse(Stream hs)
            {
                InIndex = hs.ReadDecodedUInt64();
                OutIndex = hs.ReadDecodedUInt64();
            }

            public void Write(Stream hs)
            {
                hs.WriteEncodedUInt64(InIndex);
                hs.WriteEncodedUInt64(OutIndex);
            }
        }

        public class Folder : IHeaderParser, IHeaderWriter
        {
            public UInt64 NumCoders;
            public CoderInfo[] CodersInfo;
            public UInt64 NumInStreamsTotal;
            public UInt64 NumOutStreamsTotal;
            public UInt64 NumBindPairs; // NumOutStreamsTotal - 1
            public BindPairsInfo[] BindPairsInfo; // [NumBindPairs]
            public UInt64 NumPackedStreams; // NumInStreamsTotal - NumBindPairs
            public UInt64[] PackedIndices; // [NumPackedStreams]
            
            // start --- added from UnPackInfo
            public UInt64[] UnPackSizes; // [NumOutStreamsTotal]
            public UInt32? UnPackCRC;
            // end ----- added from UnPackInfo

            public Folder()
            {
                this.NumCoders = 0;
                this.CodersInfo = new CoderInfo[0];
                this.NumInStreamsTotal = 0;
                this.NumOutStreamsTotal = 0;
                this.NumBindPairs = 0;
                this.BindPairsInfo = new BindPairsInfo[0];
                this.NumPackedStreams = 0;
                this.PackedIndices = new UInt64[0];
                this.UnPackSizes = new UInt64[0];
                this.UnPackCRC = null;
            }

            /// <summary>
            /// Helper to get final unpacked size of folder
            /// </summary>
            /// <returns></returns>
            public UInt64 GetUnPackSize()
            {
                if (UnPackSizes.Length == 0)
                    return 0;

                for (long i = 0; i < UnPackSizes.LongLength; ++i)
                {
                    bool foundBindPair = false;
                    for (ulong j = 0; j < NumBindPairs; ++j)
                    {
                        if (BindPairsInfo[j].OutIndex == (UInt64)i)
                        {
                            foundBindPair = true;
                            break;
                        }
                    }
                    if (!foundBindPair)
                    {
                        return UnPackSizes[i];
                    }
                }

                throw new z7Exception("Could not find final unpack size.");
            }

            public void Parse(Stream hs)
            {
                NumCoders = hs.ReadDecodedUInt64();
                CodersInfo = new CoderInfo[NumCoders];
                for (ulong i = 0; i < NumCoders; ++i)
                {
                    CodersInfo[i] = new CoderInfo();
                    CodersInfo[i].Parse(hs);
                    NumInStreamsTotal += CodersInfo[i].NumInStreams;
                    NumOutStreamsTotal += CodersInfo[i].NumOutStreams;
                }

                NumBindPairs = NumOutStreamsTotal - 1;
                BindPairsInfo = new BindPairsInfo[NumBindPairs];
                for (ulong i = 0; i < NumBindPairs; ++i)
                {
                    BindPairsInfo[i] = new BindPairsInfo();
                    BindPairsInfo[i].Parse(hs);
                }

                NumPackedStreams = NumInStreamsTotal - NumBindPairs;
                if (NumPackedStreams > 1)
                {
                    PackedIndices = new UInt64[NumPackedStreams];
                    for (ulong i = 0; i < NumPackedStreams; ++i)
                        PackedIndices[i] = hs.ReadDecodedUInt64();
                }
                else
                    PackedIndices = new UInt64[] { 0 };
            }

            public void Write(Stream hs)
            {
                hs.WriteEncodedUInt64(NumCoders);
                for (ulong i = 0; i < NumCoders; ++i)
                    CodersInfo[i].Write(hs);

                for (ulong i = 0; i < NumBindPairs; ++i)
                    BindPairsInfo[i].Write(hs);

                if (NumPackedStreams > 1)
                    for (ulong i = 0; i < NumPackedStreams; ++i)
                        hs.WriteEncodedUInt64(PackedIndices[i]);
            }
        }

        public class UnPackInfo : IHeaderParser, IHeaderWriter
        {
            public UInt64 NumFolders;
            public Byte External;
            public Folder[] Folders; // [NumFolders]
            public UInt64 DataStreamsIndex;
            public UnPackInfo()
            {
                this.NumFolders = 0;
                this.External = 0;
                this.Folders = new Folder[0];
                this.DataStreamsIndex = 0;
            }

            public void Parse(Stream hs)
            {
                ExpectPropertyID(this, hs, PropertyID.kFolder);

                // Folders

                NumFolders = hs.ReadDecodedUInt64();
                External = hs.ReadByteThrow();
                switch (External)
                {
                    case 0:
                        Folders = new Folder[NumFolders];
                        for (ulong i = 0; i < NumFolders; ++i)
                        {
                            Folders[i] = new Folder();
                            Folders[i].Parse(hs);
                        }
                        break;
                    case 1:
                        DataStreamsIndex = hs.ReadDecodedUInt64();
                        break;
                    default:
                        throw new z7Exception("External value must be `0` or `1`.");
                }

                ExpectPropertyID(this, hs, PropertyID.kCodersUnPackSize);

                // CodersUnPackSize (data stored in `Folder.UnPackSizes`)

                for (ulong i = 0; i < NumFolders; ++i)
                {
                    Folders[i].UnPackSizes = new UInt64[Folders[i].NumOutStreamsTotal];
                    for (ulong j = 0; j < Folders[i].NumOutStreamsTotal; ++j)
                        Folders[i].UnPackSizes[j] = hs.ReadDecodedUInt64();
                }

                // Optional: UnPackDigests (data stored in `FolderInfo.UnPackCRC`)

                PropertyID propertyID = GetPropertyID(this, hs);

                var UnPackDigests = new Digests(NumFolders);
                if (propertyID == PropertyID.kCRC)
                {
                    UnPackDigests.Parse(hs);
                    propertyID = GetPropertyID(this, hs);
                }
                for (ulong i = 0, j = 0; i < NumFolders; ++i)
                    if (UnPackDigests.Defined[i])
                        Folders[i].UnPackCRC = UnPackDigests.CRCs[j++];

                // end of UnPackInfo

                if (propertyID != PropertyID.kEnd)
                    throw new z7Exception("Expected kEnd property.");
            }

            public void Write(Stream hs)
            {
                hs.WriteByte((Byte)PropertyID.kFolder);

                // Folders

                hs.WriteEncodedUInt64(NumFolders);
                hs.WriteByte(0);
                for (ulong i = 0; i < NumFolders; ++i)
                    Folders[i].Write(hs);

                // CodersUnPackSize in `Folder.UnPackSizes`

                hs.WriteByte((Byte)PropertyID.kCodersUnPackSize);
                for (ulong i = 0; i < NumFolders; ++i)
                    for (ulong j = 0; j < (ulong)Folders[i].UnPackSizes.LongLength; ++j)
                        hs.WriteEncodedUInt64(Folders[i].UnPackSizes[j]);
                
                // UnPackDigests in `Folder.UnPackCRC`

                if (Folders.Any(folder => folder.UnPackCRC != null))
                {
                    hs.WriteByte((Byte)PropertyID.kCRC);

                    var UnPackDigests = new Digests(NumFolders);
                    UnPackDigests.CRCs = new UInt32[NumFolders];
                    ulong j = 0;
                    for (ulong i = 0; i < NumFolders; ++i)
                    {
                        if (Folders[i].UnPackCRC != null)
                        {
                            UnPackDigests.Defined[i] = true;
                            UnPackDigests.CRCs[j++] = (UInt32)Folders[i].UnPackCRC;
                        }
                        else
                            UnPackDigests.Defined[i] = false;
                    }
                    UnPackDigests.NumDefined = j;
                    UnPackDigests.Write(hs);
                }

                hs.WriteByte((Byte)PropertyID.kEnd);
            }
        }

        public class SubStreamsInfo : IHeaderParser, IHeaderWriter
        {
            UnPackInfo unPackInfo; // dependency

            public UInt64[] NumUnPackStreamsInFolders; // [NumFolders]
            public UInt64 NumUnPackStreamsTotal;
            public List<UInt64> UnPackSizes;
            public Digests Digests; // [Number of streams with unknown CRCs]
            public SubStreamsInfo(UnPackInfo unPackInfo)
            {
                this.unPackInfo = unPackInfo;
                this.NumUnPackStreamsInFolders = new UInt64[0];
                this.NumUnPackStreamsTotal = 0;
                this.UnPackSizes = new List<UInt64>();
                this.Digests = new Digests(0);
            }

            public void Parse(Stream hs)
            {
                PropertyID propertyID = GetPropertyID(this, hs);

                // Number of UnPack Streams per Folder

                if (propertyID == PropertyID.kNumUnPackStream)
                {
                    NumUnPackStreamsInFolders = new UInt64[unPackInfo.NumFolders];
                    NumUnPackStreamsTotal = 0;
                    for (ulong i = 0; i < unPackInfo.NumFolders; ++i)
                        NumUnPackStreamsTotal += NumUnPackStreamsInFolders[i] = hs.ReadDecodedUInt64();

                    propertyID = GetPropertyID(this, hs);
                }
                else
                {
                    NumUnPackStreamsInFolders = Enumerable.Repeat((UInt64)1, (int)unPackInfo.NumFolders).ToArray();
                    NumUnPackStreamsTotal = unPackInfo.NumFolders;
                }

                // UnPackSizes

                UnPackSizes = new List<UInt64>();
                if (propertyID == PropertyID.kSize)
                {
                    for (ulong i = 0; i < unPackInfo.NumFolders; ++i)
                    {
                        ulong num = NumUnPackStreamsInFolders[i];
                        if (num == 0)
                            continue;

                        ulong sum = 0;
                        for (ulong j = 1; j < num; ++j)
                        {
                            ulong size = hs.ReadDecodedUInt64();
                            sum += size;
                            UnPackSizes.Add(size);
                        }
                        UnPackSizes.Add(unPackInfo.Folders[i].GetUnPackSize() - sum);
                    }

                    propertyID = GetPropertyID(this, hs);
                }
                else
                {
                    for (ulong i = 0; i < unPackInfo.NumFolders; ++i)
                    {
                        ulong num = NumUnPackStreamsInFolders[i];
                        if (num == 1)
                            UnPackSizes.Add(unPackInfo.Folders[i].GetUnPackSize());
                    }
                }

                // Digests [Number of Unknown CRCs]

                UInt64 numDigests = 0;
                UInt64 numDigestsTotal = 0;
                for (UInt64 i = 0; i < this.unPackInfo.NumFolders; ++i)
                {
                    UInt64 numSubStreams = NumUnPackStreamsInFolders[i];
                    if (numSubStreams > 1 || this.unPackInfo.Folders[i].UnPackCRC == null)
                        numDigests += numSubStreams;
                    numDigestsTotal += numSubStreams;
                }

                if (propertyID == PropertyID.kCRC)
                {
                    Digests = new Digests(numDigests);
                    Digests.Parse(hs);

                    propertyID = GetPropertyID(this, hs);
                }

                if (propertyID != PropertyID.kEnd)
                    throw new z7Exception("Expected `kEnd` property ID.");
            }

            public void Write(Stream hs)
            {
                // Number of UnPacked Streams in Folders

                if (NumUnPackStreamsTotal != unPackInfo.NumFolders && NumUnPackStreamsInFolders.Any())
                {
                    hs.WriteByte((Byte)PropertyID.kNumUnPackStream);
                    for (long i = 0; i < NumUnPackStreamsInFolders.LongLength; ++i)
                        hs.WriteEncodedUInt64(NumUnPackStreamsInFolders[i]);
                }

                // Substreams UnPackSizes
                if (UnPackSizes.Any())
                {
                    hs.WriteByte((Byte)PropertyID.kSize);

                    List<UInt64>.Enumerator u = UnPackSizes.GetEnumerator();
                    for (long i = 0; i < NumUnPackStreamsInFolders.LongLength; ++i)
                    {
                        for (long j = 1; j < (long)NumUnPackStreamsInFolders[i]; ++j)
                        {
                            if (!u.MoveNext())
                                throw new z7Exception("Missing `SubStreamInfo.UnPackSize` entry.");
                            hs.WriteEncodedUInt64(u.Current);
                        }
                        u.MoveNext(); // skip the `useless` one
                    }
                }

                if (Digests.NumStreams > 0)
                {
                    hs.WriteByte((Byte)PropertyID.kCRC);
                    Digests.Write(hs);
                }

                hs.WriteByte((Byte)PropertyID.kEnd);
            }
        }

        public class StreamsInfo : IHeaderParser, IHeaderWriter
        {
            public PackInfo PackInfo;
            public UnPackInfo UnPackInfo;
            public SubStreamsInfo SubStreamsInfo;
            public StreamsInfo()
            {
                PackInfo = null;
                UnPackInfo = null;
                SubStreamsInfo = null;
            }

            public void Parse(Stream hs)
            {
                while (true)
                {
                    PropertyID propertyID = GetPropertyID(this, hs);
                    switch (propertyID)
                    {
                        case PropertyID.kPackInfo:
                            PackInfo = new PackInfo();
                            PackInfo.Parse(hs);
                            break;
                        case PropertyID.kUnPackInfo:
                            UnPackInfo = new UnPackInfo();
                            UnPackInfo.Parse(hs);
                            break;
                        case PropertyID.kSubStreamsInfo:
                            if (UnPackInfo == null)
                                UnPackInfo = new UnPackInfo();
                            SubStreamsInfo = new SubStreamsInfo(UnPackInfo);
                            SubStreamsInfo.Parse(hs);
                            break;
                        case PropertyID.kEnd:
                            return;
                        default:
                            throw new NotImplementedException(propertyID.ToString());
                    }
                }
            }

            public void Write(Stream hs)
            {
                if (PackInfo != null)
                {
                    hs.WriteByte((Byte)PropertyID.kPackInfo);
                    PackInfo.Write(hs);
                }
                if (UnPackInfo != null)
                {
                    hs.WriteByte((Byte)PropertyID.kUnPackInfo);
                    UnPackInfo.Write(hs);
                }
                if (SubStreamsInfo != null)
                {
                    hs.WriteByte((Byte)PropertyID.kSubStreamsInfo);
                    SubStreamsInfo.Write(hs);
                }
                hs.WriteByte((Byte)PropertyID.kEnd);
            }
        }

        abstract public class FileProperty : IHeaderParser, IHeaderWriter
        {
            public PropertyID PropertyID;
            public UInt64 NumFiles;
            public UInt64 Size;
            long positionMarker;
            public FileProperty(PropertyID PropertyID, UInt64 NumFiles)
            {
                this.PropertyID = PropertyID;
                this.NumFiles = NumFiles;
                Size = 0;
                positionMarker = -1;
            }

            public virtual void Parse(Stream hs)
            {
                Size = hs.ReadDecodedUInt64();
                positionMarker = hs.Position;
            }
            public void PostParse(Stream hs)
            {
                if (positionMarker != -1)
                {
                    UInt64 expectedPosition = (UInt64)positionMarker + Size;
                    if ((UInt64)hs.Position < expectedPosition)
                    {
                        if (hs.CanSeek)
                            hs.Seek((long)expectedPosition, SeekOrigin.Begin);
                        else
                            hs.ReadThrow(expectedPosition - (UInt64)hs.Position);
                    }
                }
            }

            public virtual void Write(Stream hs)
            {
                // no-op
            }
            public void PostWrite(Stream headerStream, Stream dataStream)
            {
                headerStream.WriteByte((Byte)PropertyID);
                Size = (UInt64)dataStream.Length;
                headerStream.WriteEncodedUInt64(Size);

                positionMarker = headerStream.Position;
                long expectedPosition = positionMarker + (long)Size;

                dataStream.Position = 0;
                dataStream.CopyTo(headerStream);
                if (headerStream.Position < expectedPosition)
                {
                    byte[] buffer = Enumerable.Repeat((Byte)0, (int)(expectedPosition - headerStream.Position)).ToArray();
                    headerStream.Write(buffer, 0, buffer.Length);
                }
            }
        }

        public class PropertyEmptyStream : FileProperty
        {
            public bool[] IsEmptyStream;
            public UInt64 NumEmptyStreams;
            public PropertyEmptyStream(UInt64 NumFiles) : base(PropertyID.kEmptyStream, NumFiles) { }

            public override void Parse(Stream hs)
            {
                base.Parse(hs);
                NumEmptyStreams = hs.ReadBoolVector(NumFiles, out IsEmptyStream);
            }

            public override void Write(Stream hs)
            {
                base.Write(hs);
                hs.WriteBoolVector(IsEmptyStream);
            }
        }

        public class PropertyEmptyFile : FileProperty
        {
            public UInt64 NumEmptyStreams;
            public bool[] IsEmptyFile;
            public PropertyEmptyFile(UInt64 NumFiles, UInt64 NumEmptyStreams)
                : base(PropertyID.kEmptyFile, NumFiles)
            {
                this.NumEmptyStreams = NumEmptyStreams;
            }

            public override void Parse(Stream hs)
            {
                base.Parse(hs);
                hs.ReadBoolVector(NumEmptyStreams, out IsEmptyFile);
            }

            public override void Write(Stream hs)
            {
                base.Write(hs);
                hs.WriteBoolVector(IsEmptyFile);
            }
        }

        public class PropertyAnti : FileProperty
        {
            public UInt64 NumEmptyStreams;
            public bool[] IsAnti;
            public PropertyAnti(UInt64 NumFiles, UInt64 NumEmptyStreams)
                : base(PropertyID.kAnti, NumFiles)
            {
                this.NumEmptyStreams = NumEmptyStreams;
            }

            public override void Parse(Stream hs)
            {
                base.Parse(hs);
                hs.ReadBoolVector(NumEmptyStreams, out IsAnti);
            }

            public override void Write(Stream hs)
            {
                base.Write(hs);
                hs.WriteBoolVector(IsAnti);
            }
        }

        public class PropertyTime : FileProperty
        {
            public bool[] Defined; // [NumFiles]
            public UInt64 NumDefined;
            public Byte External;
            public UInt64 DataIndex;
            public DateTime[] Times; // [NumDefined]
            public PropertyTime(PropertyID propertyID, UInt64 NumFiles)
                : base(propertyID, NumFiles)
            {
            }

            public override void Parse(Stream hs)
            {
                base.Parse(hs);
                NumDefined = hs.ReadOptionalBoolVector(NumFiles, out Defined);

                External = hs.ReadByteThrow();
                switch (External)
                {
                    case 0:
                        Times = new DateTime[NumDefined];
                        using (BinaryReader reader = new BinaryReader(hs, Encoding.Default, true))
                            for (ulong i = 0; i < NumDefined; ++i)
                            {
                                UInt64 encodedTime = reader.ReadUInt64();
                                if (encodedTime >= 0 && encodedTime <= 2650467743999999999)
                                {
                                    Times[i] = DateTime.FromFileTimeUtc((long)encodedTime).ToLocalTime();
                                }
                            }
                        break;
                    case 1:
                        DataIndex = hs.ReadDecodedUInt64();
                        break;
                    default:
                        throw new z7Exception("External value must be 0 or 1.");
                }
            }

            public override void Write(Stream hs)
            {
                base.Write(hs);
                hs.WriteOptionalBoolVector(Defined);
                hs.WriteByte(0);
                using (BinaryWriter writer = new BinaryWriter(hs, Encoding.Default, true))
                    for (ulong i = 0; i < NumDefined; ++i)
                    {
                        UInt64 encodedTime = (UInt64)Times[i].ToUniversalTime().ToFileTimeUtc();
                        writer.Write((UInt64)encodedTime);
                    }
            }
        }

        public class PropertyName : FileProperty
        {
            public Byte External;
            public UInt64 DataIndex;
            public string[] Names;
            public PropertyName(UInt64 NumFiles) : base(PropertyID.kName, NumFiles) { }

            public override void Parse(Stream hs)
            {
                base.Parse(hs);
                External = hs.ReadByteThrow();
                if (External != 0)
                {
                    DataIndex = hs.ReadDecodedUInt64();
                }
                else
                {
                    Names = new string[NumFiles];
                    using (BinaryReader reader = new BinaryReader(hs, Encoding.Default, true))
                    {
                        List<Byte> nameData = new List<byte>(1024);
                        for (ulong i = 0; i < NumFiles; ++i)
                        {
                            nameData.Clear();
                            UInt16 ch;
                            while (true)
                            {
                                ch = reader.ReadUInt16();
                                if (ch == 0x0000)
                                    break;
                                nameData.Add((Byte)(ch >> 8));
                                nameData.Add((Byte)(ch & 0xFF));
                            }
                            Names[i] = Encoding.BigEndianUnicode.GetString(nameData.ToArray());
                        }
                    }
                }
            }

            public override void Write(Stream hs)
            {
                base.Write(hs);
                hs.WriteByte(0);
                using (BinaryWriter writer = new BinaryWriter(hs, Encoding.Default, true))
                {
                    for (ulong i = 0; i < NumFiles; ++i)
                    {
                        Byte[] nameData = Encoding.Unicode.GetBytes(Names[i]);
                        writer.Write(nameData);
                        writer.Write((UInt16)0x0000);
                    }
                }
            }
        }

        public class PropertyAttributes : FileProperty
        {
            public bool[] Defined; // [NumFiles]
            public UInt64 NumDefined;
            public Byte External;
            public UInt64 DataIndex;
            public UInt32[] Attributes; // [NumDefined]
            public PropertyAttributes(UInt64 NumFiles) : base(PropertyID.kWinAttributes, NumFiles) { }

            public override void Parse(Stream hs)
            {
                base.Parse(hs);
                NumDefined = hs.ReadOptionalBoolVector(NumFiles, out Defined);

                External = hs.ReadByteThrow();
                switch (External)
                {
                    case 0:
                        Attributes = new UInt32[NumDefined];
                        using (BinaryReader reader = new BinaryReader(hs, Encoding.Default, true))
                            for (ulong i = 0; i < NumDefined; ++i)
                                Attributes[i] = reader.ReadUInt32();
                        break;
                    case 1:
                        DataIndex = hs.ReadDecodedUInt64();
                        break;
                    default:
                        throw new z7Exception("External value must be 0 or 1.");
                }
            }

            public override void Write(Stream hs)
            {
                base.Write(hs);
                hs.WriteOptionalBoolVector(Defined);
                hs.WriteByte(0);
                using (BinaryWriter writer = new BinaryWriter(hs, Encoding.Default, true))
                    for (ulong i = 0; i < NumDefined; ++i)
                        writer.Write((UInt32)Attributes[i]);
            }
        }

        public class PropertyDummy : FileProperty
        {
            public PropertyDummy()
                : base(PropertyID.kDummy, 0) { }
            public override void Parse(Stream hs)
            {
                base.Parse(hs);
                Byte[] dummy = hs.ReadThrow(Size);
            }
            public override void Write(Stream hs)
            {
                base.Write(hs);
                hs.WriteByte(0);
                hs.WriteByte(0);
            }
        }

        public class FilesInfo : IHeaderParser, IHeaderWriter
        {
            public UInt64 NumFiles;
            public UInt64 NumEmptyStreams;
            public List<FileProperty> Properties; // [Arbitrary number]
            public FilesInfo()
            {
                this.NumFiles = 0;
                this.NumEmptyStreams = 0;
                this.Properties = new List<FileProperty>();
            }

            public void Parse(Stream hs)
            {
                NumFiles = hs.ReadDecodedUInt64();
                while (true)
                {
                    PropertyID propertyID = GetPropertyID(this, hs);
                    if (propertyID == PropertyID.kEnd)
                        break;

                    FileProperty property = null;
                    switch (propertyID)
                    {
                        case PropertyID.kEmptyStream:
                            {
                                PropertyEmptyStream p = new PropertyEmptyStream(NumFiles);
                                p.Parse(hs);
                                p.PostParse(hs);
                                Properties.Add(p);
                                NumEmptyStreams = p.NumEmptyStreams;
                                break;
                            }
                        case PropertyID.kEmptyFile:
                            property = new PropertyEmptyFile(NumFiles, NumEmptyStreams);
                            break;
                        case PropertyID.kAnti:
                            property = new PropertyAnti(NumFiles, NumEmptyStreams);
                            break;
                        case PropertyID.kCTime:
                        case PropertyID.kATime:
                        case PropertyID.kMTime:
                            property = new PropertyTime(propertyID, NumFiles);
                            break;
                        case PropertyID.kName:
                            property = new PropertyName(NumFiles);
                            break;
                        case PropertyID.kWinAttributes:
                            property = new PropertyAttributes(NumFiles);
                            break;
                        case PropertyID.kDummy:
                            property = new PropertyDummy();
                            break;
                        default:
                            throw new NotImplementedException(propertyID.ToString());
                    }

                    if (property != null)
                    {
                        property.Parse(hs);
                        property.PostParse(hs);
                        Properties.Add(property);
                    }
                }
            }

            public void Write(Stream hs)
            {
                hs.WriteEncodedUInt64(NumFiles);
                foreach (var property in Properties)
                {
                    // TODO: align with dummy
                    using (MemoryStream dataStream = new MemoryStream())
                    {
                        property.Write(dataStream);
                        property.PostWrite(hs, dataStream);
                    }
                }
                hs.WriteByte((Byte)PropertyID.kEnd);
            }
        }

        public class Header : IHeaderParser, IHeaderWriter
        {
            public ArchiveProperties ArchiveProperties;
            public StreamsInfo AdditionalStreamsInfo;
            public StreamsInfo MainStreamsInfo;
            public FilesInfo FilesInfo;
            public Header()
            {
                ArchiveProperties = null;
                AdditionalStreamsInfo = null;
                MainStreamsInfo = null;
                FilesInfo = null;
            }

            public void Parse(Stream hs)
            {
                while (true)
                {
                    PropertyID propertyID = GetPropertyID(this, hs);
                    switch (propertyID)
                    {
                        case PropertyID.kArchiveProperties:
                            ArchiveProperties = new ArchiveProperties();
                            ArchiveProperties.Parse(hs);
                            break;
                        case PropertyID.kAdditionalStreamsInfo:
                            AdditionalStreamsInfo = new StreamsInfo();
                            AdditionalStreamsInfo.Parse(hs);
                            break;
                        case PropertyID.kMainStreamsInfo:
                            MainStreamsInfo = new StreamsInfo();
                            MainStreamsInfo.Parse(hs);
                            break;
                        case PropertyID.kFilesInfo:
                            FilesInfo = new FilesInfo();
                            FilesInfo.Parse(hs);
                            break;
                        case PropertyID.kEnd:
                            return;
                        default:
                            throw new NotImplementedException(propertyID.ToString());
                    }
                }
            }

            public void Write(Stream hs)
            {
                if (ArchiveProperties != null)
                {
                    hs.WriteByte((Byte)PropertyID.kArchiveProperties);
                    ArchiveProperties.Write(hs);
                }
                if (AdditionalStreamsInfo != null)
                {
                    hs.WriteByte((Byte)PropertyID.kAdditionalStreamsInfo);
                    AdditionalStreamsInfo.Write(hs);
                }
                if (MainStreamsInfo != null)
                {
                    hs.WriteByte((Byte)PropertyID.kMainStreamsInfo);
                    MainStreamsInfo.Write(hs);
                }
                if (FilesInfo != null)
                {
                    hs.WriteByte((Byte)PropertyID.kFilesInfo);
                    FilesInfo.Write(hs);
                }
                hs.WriteByte((Byte)PropertyID.kEnd);
            }
        }

        /// <summary>
        /// Class properties
        /// </summary>
        public Header RawHeader
        {
            get; set;
        }
        public StreamsInfo EncodedHeader
        {
            get; set;
        }

        /// <summary>
        /// Private variables
        /// </summary>
        private Stream headerStream;

        /// <summary>
        /// 7zip file header constructor
        /// </summary>
        public z7Header(Stream headerStream, bool createNew = false)
        {
            this.headerStream = headerStream;
            RawHeader = createNew ? new Header() : null;
            EncodedHeader = null;
        }

        /// <summary>
        /// Main parser that initiates cascaded parsing
        /// </summary>
        public void Parse()
        {
            Parse(headerStream);
        }
        public void Parse(Stream headerStream)
        {
            try
            {
                var propertyID = GetPropertyID(this, headerStream);
                switch (propertyID)
                {
                    case PropertyID.kHeader:
                        RawHeader = new Header();
                        RawHeader.Parse(headerStream);
                        break;

                    case PropertyID.kEncodedHeader:
                        EncodedHeader = new StreamsInfo();
                        EncodedHeader.Parse(headerStream);
                        break;

                    case PropertyID.kEnd:
                        return;

                    default:
                        throw new NotImplementedException(propertyID.ToString());
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        /// <summary>
        /// Main writer that initiates header writing
        /// </summary>
        public void Write(Stream headerStream)
        {
            try
            {
                if (RawHeader == null)
                    throw new z7Exception("No header to write.");

                headerStream.WriteByte((Byte)PropertyID.kHeader);
                RawHeader.Write(headerStream);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        /// <summary>
        /// Helper function to return a property id while making sure it's valid (+ trace)
        /// </summary>
        public static PropertyID GetPropertyID(IHeaderParser parser, Stream headerStream)
        {
            Byte propertyID = headerStream.ReadByteThrow();
            if (propertyID > (Byte)PropertyID.kDummy)
                throw new z7Exception(parser.GetType().Name + $": Unknown property ID = {propertyID}.");

            Trace.TraceInformation(parser.GetType().Name + $": Property ID = {(PropertyID)propertyID}");
            return (PropertyID)propertyID;
        }

        /// <summary>
        /// Helper function to read and ensure a specific PropertyID is next in header stream
        /// </summary>
        public static void ExpectPropertyID(IHeaderParser parser, Stream headerStream, PropertyID propertyID)
        {
            if (GetPropertyID(parser, headerStream) != propertyID)
                throw new z7Exception(parser.GetType().Name + $": Expected property ID = {propertyID}.");
        }
    }
}
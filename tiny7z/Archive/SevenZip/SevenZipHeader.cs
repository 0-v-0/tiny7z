using Tiny7z.Common;
using System;
using System.Collections.Generic;

using System.Linq;
using System.IO;
using System.Text;

namespace Tiny7z.Archive
{
    internal partial class SevenZipHeader : IHeaderParser, IHeaderWriter
    {
        #region Internal Enums
        /// <summary>
        /// All valid property IDs
        /// </summary>
        internal enum PropertyID
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
        #endregion Internal Enums

        #region Internal Classes
        internal class Digests : IHeaderParser, IHeaderWriter
        {
            public ulong NumStreams()
            {
                return (ulong)CRCs.LongLength;
            }
            public ulong NumDefined()
            {
                return (ulong)CRCs.Count(crc => crc != null);
            }
            public bool Defined(ulong index)
            {
                return CRCs[index] != null;
            }
            public uint?[] CRCs;
            public Digests(ulong NumStreams)
            {
                CRCs = new uint?[NumStreams];
            }

            public void Parse(Stream hs)
            {
                bool[] defined;
                var numDefined = hs.ReadOptionalBoolVector(NumStreams(), out defined);

                using (var reader = new BinaryReader(hs, Encoding.Default, true))
                    for (long i = 0; i < defined.LongLength; ++i)
                        if (defined[i])
                            CRCs[i] = reader.ReadUInt32();
            }

            public void Write(Stream hs)
            {
                bool[] defined = CRCs.Select(crc => (bool)(crc != null)).ToArray();
                hs.WriteOptionalBoolVector(defined);

                using (var writer = new BinaryWriter(hs, Encoding.Default, true))
                    for (ulong i = 0; i < NumStreams(); ++i)
                        if (CRCs[i] != null)
                            writer.Write((uint)CRCs[i]);
            }
        }

        internal class ArchiveProperty : IHeaderParser, IHeaderWriter
        {
            public PropertyID Type;
            public ulong Size;
            public byte[] Data;
            public ArchiveProperty(PropertyID type)
            {
                Type = type;
                Size = 0;
                Data = new byte[0];
            }

            public void Parse(Stream hs)
            {
                Size = hs.ReadDecodedUInt64();
                if (Size > 0)
                    Data = hs.ReadThrow(Size);
            }

            public void Write(Stream hs)
            {
                hs.WriteByte((byte)Type);
                hs.WriteEncodedUInt64(Size);
                if (Size > 0)
                    hs.Write(Data, 0, (int)Size);
            }
        }

        internal class ArchiveProperties : IHeaderParser, IHeaderWriter
        {
            public List<ArchiveProperty> Properties; // [Arbitrary number]
            public ArchiveProperties()
            {
                Properties = new List<ArchiveProperty>();
            }

            public void Parse(Stream hs)
            {
                while (true)
                {
                    PropertyID propertyID = GetPropertyID(this, hs);
                    if (propertyID == PropertyID.kEnd)
                        return;

                    var property = new ArchiveProperty(propertyID);
                    property.Parse(hs);
                    Properties.Add(property);
                }
            }

            public void Write(Stream hs)
            {
                foreach (var property in Properties)
                    property.Write(hs);
                hs.WriteByte((byte)PropertyID.kEnd);
            }
        }

        internal class PackInfo : IHeaderParser, IHeaderWriter
        {
            public ulong PackPos;
            public ulong NumPackStreams;
            public ulong[] Sizes; // [NumPackStreams]
            public Digests Digests; // [NumPackStreams]
            public PackInfo()
            {
                PackPos = 0;
                NumPackStreams = 0;
                Sizes = new ulong[0];
                Digests = new Digests(0);
            }

            public void Parse(Stream hs)
            {
                PackPos = hs.ReadDecodedUInt64();
                NumPackStreams = hs.ReadDecodedUInt64();
                Sizes = new ulong[NumPackStreams­];
                Digests = new Digests(NumPackStreams);
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
                            Digests.Parse(hs);
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

                hs.WriteByte((byte)PropertyID.kSize);
                for (ulong i = 0; i < NumPackStreams; ++i)
                    hs.WriteEncodedUInt64(Sizes[i]);

                if (Digests.NumDefined() > 0)
                {
                    hs.WriteByte((byte)PropertyID.kCRC);
                    Digests.Write(hs);
                }

                hs.WriteByte((byte)PropertyID.kEnd);
            }
        }

        internal class CoderInfo : IHeaderParser, IHeaderWriter
        {
            public const byte AttrSizeMask      = 0b00001111;
            public const byte AttrComplexCoder  = 0b00010000;
            public const byte AttrHasAttributes = 0b00100000;
            public byte Attributes;
            public byte[] CodecId; // [CodecIdSize]
            public ulong NumInStreams;
            public ulong NumOutStreams;
            public ulong PropertiesSize;
            public byte[] Properties; // [PropertiesSize]
            public CoderInfo()
            {
                Attributes = 0;
                CodecId = new byte[0];
                NumInStreams = 0;
                NumOutStreams = 0;
                PropertiesSize = 0;
                Properties = new byte[0];
            }

            public void Parse(Stream hs)
            {
                Attributes = hs.ReadByteThrow();
                int codecIdSize = (Attributes & AttrSizeMask);
                bool isComplexCoder = (Attributes & AttrComplexCoder) > 0;
                bool hasAttributes = (Attributes & AttrHasAttributes) > 0;

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
                int codecIdSize = (Attributes & AttrSizeMask);
                bool isComplexCoder = (Attributes & AttrComplexCoder) > 0;
                bool hasAttributes = (Attributes & AttrHasAttributes) > 0;

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

        internal class BindPairsInfo : IHeaderParser, IHeaderWriter
        {
            public ulong InIndex;
            public ulong OutIndex;
            public BindPairsInfo()
            {
                InIndex = 0;
                OutIndex = 0;
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

        internal class Folder : IHeaderParser, IHeaderWriter
        {
            public ulong NumCoders;
            public CoderInfo[] CodersInfo;
            public ulong NumInStreamsTotal;
            public ulong NumOutStreamsTotal;
            public ulong NumBindPairs; // NumOutStreamsTotal - 1
            public BindPairsInfo[] BindPairsInfo; // [NumBindPairs]
            public ulong NumPackedStreams; // NumInStreamsTotal - NumBindPairs
            public ulong[] PackedIndices; // [NumPackedStreams]

            #region Added From UnPackInfo (for convenience)
            public ulong[] UnPackSizes; // [NumOutStreamsTotal]
            public uint? UnPackCRC; // NULL is undefined
            #endregion Added From UnPackInfo

            public Folder()
            {
                NumCoders = 0;
                CodersInfo = new CoderInfo[0];
                NumInStreamsTotal = 0;
                NumOutStreamsTotal = 0;
                NumBindPairs = 0;
                BindPairsInfo = new BindPairsInfo[0];
                NumPackedStreams = 0;
                PackedIndices = new ulong[0];
                UnPackSizes = new ulong[0];
                UnPackCRC = null;
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
                    PackedIndices = new ulong[NumPackedStreams];
                    for (ulong i = 0; i < NumPackedStreams; ++i)
                        PackedIndices[i] = hs.ReadDecodedUInt64();
                }
                else
                    PackedIndices = new ulong[] { 0 };
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

            public ulong GetUnPackSize()
            {
                if (UnPackSizes.Length == 0)
                    return 0;

                for (long i = 0; i < UnPackSizes.LongLength; ++i)
                {
                    bool foundBindPair = false;
                    for (ulong j = 0; j < NumBindPairs; ++j)
                    {
                        if (BindPairsInfo[j].OutIndex == (ulong)i)
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

                throw new SevenZipException("Could not find final unpack size.");
            }

            public long FindBindPairForInStream(ulong inStreamIndex)
            {
                for (ulong i = 0; i < NumBindPairs; ++i)
                    if (BindPairsInfo[i].InIndex == inStreamIndex)
                        return (long)i;
                return -1;
            }

            public long FindBindPairForOutStream(ulong outStreamIndex)
            {
                for (ulong i = 0; i < NumBindPairs; ++i)
                    if (BindPairsInfo[i].OutIndex == outStreamIndex)
                        return (long)i;
                return -1;
            }

            public long FindPackedIndexForInStream(ulong inStreamIndex)
            {
                for (ulong i = 0; i < NumPackedStreams; ++i)
                    if (PackedIndices[i] == inStreamIndex)
                        return (long)i;
                return -1;
            }
        }

        internal class UnPackInfo : IHeaderParser, IHeaderWriter
        {
            public ulong NumFolders;
            public byte External;
            public Folder[] Folders; // [NumFolders]
            public ulong DataStreamsIndex;
            public UnPackInfo()
            {
                NumFolders = 0;
                External = 0;
                Folders = new Folder[0];
                DataStreamsIndex = 0;
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
                        throw new SevenZipException("External value must be `0` or `1`.");
                }

                ExpectPropertyID(this, hs, PropertyID.kCodersUnPackSize);

                // CodersUnPackSize (data stored in `Folder.UnPackSizes`)

                for (ulong i = 0; i < NumFolders; ++i)
                {
                    Folders[i].UnPackSizes = new ulong[Folders[i].NumOutStreamsTotal];
                    for (ulong j = 0; j < Folders[i].NumOutStreamsTotal; ++j)
                        Folders[i].UnPackSizes[j] = hs.ReadDecodedUInt64();
                }

                // Optional: UnPackDigests (data stored in `Folder.UnPackCRC`)

                PropertyID propertyID = GetPropertyID(this, hs);

                var UnPackDigests = new Digests(NumFolders);
                if (propertyID == PropertyID.kCRC)
                {
                    UnPackDigests.Parse(hs);
                    propertyID = GetPropertyID(this, hs);
                }
                for (ulong i = 0; i < NumFolders; ++i)
                    if (UnPackDigests.Defined(i))
                        Folders[i].UnPackCRC = UnPackDigests.CRCs[i];

                // end of UnPackInfo

                if (propertyID != PropertyID.kEnd)
                    throw new SevenZipException("Expected kEnd property.");
            }

            public void Write(Stream hs)
            {
                hs.WriteByte((byte)PropertyID.kFolder);

                // Folders

                hs.WriteEncodedUInt64(NumFolders);
                hs.WriteByte(0);
                for (ulong i = 0; i < NumFolders; ++i)
                    Folders[i].Write(hs);

                // CodersUnPackSize in `Folder.UnPackSizes`

                hs.WriteByte((byte)PropertyID.kCodersUnPackSize);
                for (ulong i = 0; i < NumFolders; ++i)
                    for (ulong j = 0; j < (ulong)Folders[i].UnPackSizes.LongLength; ++j)
                        hs.WriteEncodedUInt64(Folders[i].UnPackSizes[j]);
                
                // UnPackDigests in `Folder.UnPackCRC`

                if (Folders.Any(folder => folder.UnPackCRC != null))
                {
                    hs.WriteByte((byte)PropertyID.kCRC);

                    var UnPackDigests = new Digests(NumFolders);
                    for (ulong i = 0; i < NumFolders; ++i)
                        UnPackDigests.CRCs[i] = Folders[i].UnPackCRC;
                    UnPackDigests.Write(hs);
                }

                hs.WriteByte((byte)PropertyID.kEnd);
            }
        }

        internal class SubStreamsInfo : IHeaderParser, IHeaderWriter
        {
            UnPackInfo unPackInfo; // dependency

            public ulong[] NumUnPackStreamsInFolders; // [NumFolders]
            public ulong NumUnPackStreamsTotal;
            public List<ulong> UnPackSizes;
            public Digests Digests; // [Number of streams with unknown CRCs]
            public SubStreamsInfo(UnPackInfo unPackInfo)
            {
                this.unPackInfo = unPackInfo;
                NumUnPackStreamsInFolders = new ulong[0];
                NumUnPackStreamsTotal = 0;
                UnPackSizes = new List<ulong>();
                Digests = new Digests(0);
            }

            public void Parse(Stream hs)
            {
                PropertyID propertyID = GetPropertyID(this, hs);

                // Number of UnPack Streams per Folder

                if (propertyID == PropertyID.kNumUnPackStream)
                {
                    NumUnPackStreamsInFolders = new ulong[unPackInfo.NumFolders];
                    NumUnPackStreamsTotal = 0;
                    for (ulong i = 0; i < unPackInfo.NumFolders; ++i)
                        NumUnPackStreamsTotal += NumUnPackStreamsInFolders[i] = hs.ReadDecodedUInt64();

                    propertyID = GetPropertyID(this, hs);
                }
                else // If no records, assume `1` output stream per folder
                {
                    NumUnPackStreamsInFolders = Enumerable.Repeat((ulong)1, (int)unPackInfo.NumFolders).ToArray();
                    NumUnPackStreamsTotal = unPackInfo.NumFolders;
                }

                // UnPackSizes

                UnPackSizes = new List<ulong>();
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
                else // If no records, assume one unpack size per folder
                {
                    for (ulong i = 0; i < unPackInfo.NumFolders; ++i)
                    {
                        ulong num = NumUnPackStreamsInFolders[i];
                        if (num > 1)
                            throw new SevenZipException($"Invalid number of UnPackStreams `{num}` in Folder # `{i}`.");
                        if (num == 1)
                            UnPackSizes.Add(unPackInfo.Folders[i].GetUnPackSize());
                    }
                }

				// Digests [Number of Unknown CRCs]

				ulong numDigests = 0;
                for (ulong i = 0; i < unPackInfo.NumFolders; ++i)
                {
					ulong numSubStreams = NumUnPackStreamsInFolders[i];
                    if (numSubStreams > 1 || unPackInfo.Folders[i].UnPackCRC == null)
                        numDigests += numSubStreams;
                }

                if (propertyID == PropertyID.kCRC)
                {
                    Digests = new Digests(numDigests);
                    Digests.Parse(hs);

                    propertyID = GetPropertyID(this, hs);
                }

                if (propertyID != PropertyID.kEnd)
                    throw new SevenZipException("Expected `kEnd` property ID.");
            }

            public void Write(Stream hs)
            {
                // Number of UnPacked Streams in Folders

                if (NumUnPackStreamsTotal != unPackInfo.NumFolders && NumUnPackStreamsInFolders.Any())
                {
                    hs.WriteByte((byte)PropertyID.kNumUnPackStream);

                    for (long i = 0; i < NumUnPackStreamsInFolders.LongLength; ++i)
                        hs.WriteEncodedUInt64(NumUnPackStreamsInFolders[i]);
                }

                // UnPackSizes

                if (UnPackSizes.Any())
                {
                    hs.WriteByte((byte)PropertyID.kSize);

                    List<ulong>.Enumerator u = UnPackSizes.GetEnumerator();
                    for (long i = 0; i < NumUnPackStreamsInFolders.LongLength; ++i)
                    {
                        for (ulong j = 1; j < NumUnPackStreamsInFolders[i]; ++j)
                        {
                            if (!u.MoveNext())
                                throw new SevenZipException("Missing `SubStreamInfo.UnPackSize` entry.");
                            hs.WriteEncodedUInt64(u.Current);
                        }
                        u.MoveNext(); // skip the `unneeded` one
                    }
                }

                // Digests [Number of unknown CRCs]

                if (Digests.NumDefined() > 0)
                {
                    hs.WriteByte((byte)PropertyID.kCRC);
                    Digests.Write(hs);
                }

                hs.WriteByte((byte)PropertyID.kEnd);
            }
        }

        internal class StreamsInfo : IHeaderParser, IHeaderWriter
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
                            {
                                //Trace.TraceWarning("SubStreamsInfo block found, yet no UnPackInfo block has been parsed so far.");
                                UnPackInfo = new UnPackInfo();
                            }
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
                    hs.WriteByte((byte)PropertyID.kPackInfo);
                    PackInfo.Write(hs);
                }
                if (UnPackInfo != null)
                {
                    hs.WriteByte((byte)PropertyID.kUnPackInfo);
                    UnPackInfo.Write(hs);
                }
                if (SubStreamsInfo != null)
                {
                    hs.WriteByte((byte)PropertyID.kSubStreamsInfo);
                    SubStreamsInfo.Write(hs);
                }
                hs.WriteByte((byte)PropertyID.kEnd);
            }
        }

        internal abstract class FileProperty : IHeaderParser, IHeaderWriter
        {
            public PropertyID PropertyID;
            public ulong NumFiles;
            public ulong Size;
            public FileProperty(PropertyID PropertyID, ulong NumFiles)
            {
                this.PropertyID = PropertyID;
                this.NumFiles = NumFiles;
                Size = 0;
            }

            public virtual void Parse(Stream headerStream)
            {
                Size = headerStream.ReadDecodedUInt64();
                ParseProperty(headerStream);
            }
            public abstract void ParseProperty(Stream hs);

            public virtual void Write(Stream headerStream)
            {
                using (var dataStream = new MemoryStream())
                {
                    WriteProperty(dataStream);
                    Size = (ulong)dataStream.Length;

                    headerStream.WriteByte((byte)PropertyID);
                    headerStream.WriteEncodedUInt64(Size);
                    dataStream.Position = 0;
                    dataStream.CopyTo(headerStream);
                }
            }
            public abstract void WriteProperty(Stream hs);
        }

        internal class PropertyEmptyStream : FileProperty
        {
            public bool[] IsEmptyStream;
            public ulong NumEmptyStreams;
            public PropertyEmptyStream(ulong NumFiles) : base(PropertyID.kEmptyStream, NumFiles) { }

            public override void ParseProperty(Stream hs)
            {
                NumEmptyStreams = hs.ReadBoolVector(NumFiles, out IsEmptyStream);
            }

            public override void WriteProperty(Stream hs)
            {
                hs.WriteBoolVector(IsEmptyStream);
            }
        }

        internal class PropertyEmptyFile : FileProperty
        {
            public ulong NumEmptyStreams;
            public bool[] IsEmptyFile;
            public PropertyEmptyFile(ulong NumFiles, ulong NumEmptyStreams)
                : base(PropertyID.kEmptyFile, NumFiles)
            {
                this.NumEmptyStreams = NumEmptyStreams;
            }

            public override void ParseProperty(Stream hs)
            {
                hs.ReadBoolVector(NumEmptyStreams, out IsEmptyFile);
            }

            public override void WriteProperty(Stream hs)
            {
                hs.WriteBoolVector(IsEmptyFile);
            }
        }

        internal class PropertyAnti : FileProperty
        {
            public ulong NumEmptyStreams;
            public bool[] IsAnti;
            public PropertyAnti(ulong NumFiles, ulong NumEmptyStreams)
                : base(PropertyID.kAnti, NumFiles)
            {
                this.NumEmptyStreams = NumEmptyStreams;
            }

            public override void ParseProperty(Stream hs)
            {
                hs.ReadBoolVector(NumEmptyStreams, out IsAnti);
            }

            public override void WriteProperty(Stream hs)
            {
                hs.WriteBoolVector(IsAnti);
            }
        }

        internal class PropertyTime : FileProperty
        {
            public byte External;
            public ulong DataIndex;
            public DateTime?[] Times; // [NumFiles]
            public PropertyTime(PropertyID propertyID, ulong NumFiles)
                : base(propertyID, NumFiles)
            {
            }

            public override void ParseProperty(Stream hs)
            {
                bool[] defined;
                var numDefined = hs.ReadOptionalBoolVector(NumFiles, out defined);

                External = hs.ReadByteThrow();
                switch (External)
                {
                    case 0:
                        Times = new DateTime?[NumFiles];
                        using (var reader = new BinaryReader(hs, Encoding.Default, true))
                            for (ulong i = 0; i < NumFiles; ++i)
                            {
                                if (defined[i])
                                {
									ulong encodedTime = reader.ReadUInt64();
                                    if (encodedTime >= 0 && encodedTime <= 2650467743999999999)
                                        Times[i] = DateTime.FromFileTimeUtc((long)encodedTime).ToLocalTime();
                                    //else
                                        //Trace.TraceWarning($"Defined date # `{i}` is invalid.");
                                }
                                else
                                    Times[i] = null;
                            }
                        break;
                    case 1:
                        DataIndex = hs.ReadDecodedUInt64();
                        break;
                    default:
                        throw new SevenZipException("External value must be 0 or 1.");
                }
            }

            public override void WriteProperty(Stream hs)
            {
                bool[] defined = Times.Select(time => time != null).ToArray();
                hs.WriteOptionalBoolVector(defined);
                hs.WriteByte(0);
                using (var writer = new BinaryWriter(hs, Encoding.Default, true))
                    for (ulong i = 0; i < NumFiles; ++i)
                        if(Times[i] != null)
                        {
							ulong encodedTime = (ulong)(((DateTime)Times[i]).ToUniversalTime().ToFileTimeUtc());
                            writer.Write((ulong)encodedTime);
                        }
            }
        }

        public class PropertyName : FileProperty
        {
            public byte External;
            public ulong DataIndex;
            public string[] Names;
            public PropertyName(ulong NumFiles) : base(PropertyID.kName, NumFiles) { }

            public override void ParseProperty(Stream hs)
            {
                External = hs.ReadByteThrow();
                if (External != 0)
                {
                    DataIndex = hs.ReadDecodedUInt64();
                }
                else
                {
                    Names = new string[NumFiles];
                    using (var reader = new BinaryReader(hs, Encoding.Default, true))
                    {
                        var nameData = new List<byte>(1024);
                        for (ulong i = 0; i < NumFiles; ++i)
                        {
                            nameData.Clear();
							ushort ch;
                            while (true)
                            {
                                ch = reader.ReadUInt16();
                                if (ch == 0x0000)
                                    break;
                                nameData.Add((byte)(ch >> 8));
                                nameData.Add((byte)(ch & 0xFF));
                            }
                            Names[i] = Encoding.BigEndianUnicode.GetString(nameData.ToArray());
                        }
                    }
                }
            }

            public override void WriteProperty(Stream hs)
            {
                hs.WriteByte(0);
                using (var writer = new BinaryWriter(hs, Encoding.Default, true))
                {
                    for (ulong i = 0; i < NumFiles; ++i)
                    {
						byte[] nameData = Encoding.Unicode.GetBytes(Names[i]);
                        writer.Write(nameData);
                        writer.Write((ushort)0x0000);
                    }
                }
            }
        }

        public class PropertyAttributes : FileProperty
        {
            public byte External;
            public ulong DataIndex;
            public uint?[] Attributes; // [NumFiles]
            public PropertyAttributes(ulong NumFiles) : base(PropertyID.kWinAttributes, NumFiles) { }

            public override void ParseProperty(Stream hs)
            {
                bool[] defined;
                var numDefined = hs.ReadOptionalBoolVector(NumFiles, out defined);

                External = hs.ReadByteThrow();
                switch (External)
                {
                    case 0:
                        Attributes = new uint?[NumFiles];
                        using (var reader = new BinaryReader(hs, Encoding.Default, true))
                            for (ulong i = 0; i < NumFiles; ++i)
                                Attributes[i] = defined[i] ? (uint?)reader.ReadUInt32() : null;
                        break;
                    case 1:
                        DataIndex = hs.ReadDecodedUInt64();
                        break;
                    default:
                        throw new SevenZipException("External value must be 0 or 1.");
                }
            }

            public override void WriteProperty(Stream hs)
            {
                bool[] defined = Attributes.Select(attr => attr != null).ToArray();
                hs.WriteOptionalBoolVector(defined);
                hs.WriteByte(0);
                using (var writer = new BinaryWriter(hs, Encoding.Default, true))
                    for (ulong i = 0; i < NumFiles; ++i)
                        if (defined[i])
                            writer.Write((uint)Attributes[i]);
            }
        }

        public class PropertyDummy : FileProperty
        {
            public PropertyDummy()
                : base(PropertyID.kDummy, 0) { }
            public override void ParseProperty(Stream hs)
            {
				byte[] dummy = hs.ReadThrow(Size);
            }
            public override void WriteProperty(Stream hs)
            {
                hs.Write(Enumerable.Repeat((byte)0, (int)Size).ToArray(), 0, (int)Size);
            }
        }

        public class FilesInfo : IHeaderParser, IHeaderWriter
        {
            public ulong NumFiles;
            public ulong NumEmptyStreams;
            public List<FileProperty> Properties; // [Arbitrary number]
            public FilesInfo()
            {
                NumFiles = 0;
                NumEmptyStreams = 0;
                Properties = new List<FileProperty>();
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
                            property = new PropertyEmptyStream(NumFiles);
                            property.Parse(hs);
                            NumEmptyStreams = (property as PropertyEmptyStream).NumEmptyStreams;
                            break;
                        case PropertyID.kEmptyFile:
                            property = new PropertyEmptyFile(NumFiles, NumEmptyStreams);
                            property.Parse(hs);
                            break;
                        case PropertyID.kAnti:
                            property = new PropertyAnti(NumFiles, NumEmptyStreams);
                            property.Parse(hs);
                            break;
                        case PropertyID.kCTime:
                        case PropertyID.kATime:
                        case PropertyID.kMTime:
                            property = new PropertyTime(propertyID, NumFiles);
                            property.Parse(hs);
                            break;
                        case PropertyID.kName:
                            property = new PropertyName(NumFiles);
                            property.Parse(hs);
                            break;
                        case PropertyID.kWinAttributes:
                            property = new PropertyAttributes(NumFiles);
                            property.Parse(hs);
                            break;
                        case PropertyID.kDummy:
                            property = new PropertyDummy();
                            property.Parse(hs);
                            break;
                        default:
                            throw new NotImplementedException(propertyID.ToString());
                    }

                    if (property != null)
                        Properties.Add(property);
                }
            }

            public void Write(Stream hs)
            {
                hs.WriteEncodedUInt64(NumFiles);
                foreach (var property in Properties)
                    property.Write(hs);
                hs.WriteByte((byte)PropertyID.kEnd);
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
                    hs.WriteByte((byte)PropertyID.kArchiveProperties);
                    ArchiveProperties.Write(hs);
                }
                if (AdditionalStreamsInfo != null)
                {
                    hs.WriteByte((byte)PropertyID.kAdditionalStreamsInfo);
                    AdditionalStreamsInfo.Write(hs);
                }
                if (MainStreamsInfo != null)
                {
                    hs.WriteByte((byte)PropertyID.kMainStreamsInfo);
                    MainStreamsInfo.Write(hs);
                }
                if (FilesInfo != null)
                {
                    hs.WriteByte((byte)PropertyID.kFilesInfo);
                    FilesInfo.Write(hs);
                }
                hs.WriteByte((byte)PropertyID.kEnd);
            }
        }
        #endregion Internal Classes

        #region Internal Properties
        internal Header RawHeader
        {
            get; set;
        }
        internal StreamsInfo EncodedHeader
        {
            get; set;
        }
        #endregion Internal Properties

        #region Private Fields
        Stream headerStream;
        #endregion Private Fields

        #region Internal Constructors
        /// <summary>
        /// 7zip file header constructor
        /// </summary>
        internal SevenZipHeader(Stream headerStream, bool createNew = false)
        {
            this.headerStream = headerStream;
            RawHeader = createNew ? new Header() : null;
            EncodedHeader = null;
        }
        #endregion Internal Constructors

        #region Public Methods (Interfaces)
        /// <summary>
        /// Main parser entry point.
        /// </summary>
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
                //Trace.TraceWarning(ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }

        /// <summary>
        /// Main writer that initiates header writing
        /// </summary>
        public void Write(Stream headerStream)
        {
            try
            {
                if (RawHeader != null)
                {
                    headerStream.WriteByte((byte)PropertyID.kHeader);
                    RawHeader.Write(headerStream);
                }
                else if (EncodedHeader != null)
                {
                    headerStream.WriteByte((byte)PropertyID.kEncodedHeader);
                    EncodedHeader.Write(headerStream);
                }
                else
                    throw new SevenZipException("No header to write.");
            }
            catch (Exception ex)
            {
                //Trace.TraceWarning(ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
        #endregion Public Methods (Interfaces)

        #region Internal Methods
        /// <summary>
        /// Main parser that initiates cascaded parsing
        /// </summary>
        internal void Parse()
        {
            Parse(headerStream);
        }

        /// <summary>
        /// Helper function to return a property id while making sure it's valid (+ trace)
        /// </summary>
        internal static PropertyID GetPropertyID(IHeaderParser parser, Stream headerStream)
        {
			byte propertyID = headerStream.ReadByteThrow();
            if (propertyID > (byte)PropertyID.kDummy)
                throw new SevenZipException(parser.GetType().Name + $": Unknown property ID = {propertyID}.");

            //Trace.TraceInformation(parser.GetType().Name + $": Property ID = {(PropertyID)propertyID}");
            return (PropertyID)propertyID;
        }

        /// <summary>
        /// Helper function to read and ensure a specific PropertyID is next in header stream
        /// </summary>
        internal static void ExpectPropertyID(IHeaderParser parser, Stream headerStream, PropertyID propertyID)
        {
            if (GetPropertyID(parser, headerStream) != propertyID)
                throw new SevenZipException(parser.GetType().Name + $": Expected property ID = {propertyID}.");
        }
        #endregion Internal Methods
    }
}

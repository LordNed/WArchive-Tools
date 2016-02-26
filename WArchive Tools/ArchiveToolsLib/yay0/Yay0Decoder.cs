using GameFormatReader.Common;
using System.IO;

namespace WArchiveTools.yay0
{
    public partial class Yay0
    {
        public MemoryStream Decode(EndianBinaryReader reader)
        {
            if (reader.ReadUInt32() != 0x59617930) // "Yay0" Magic
            {
                throw new InvalidDataException("Invalid Magic, not a Yay0 File");
            }

            int uncompressedSize = reader.ReadInt32();

            int linkTableOffset = reader.ReadInt32();

            int nonLinkedTableOffset = reader.ReadInt32();

            int maskBitCounter = 0;

            int currentOffsetInDestBuffer = 0;

            int currentMask = 0;

            byte[] uncompData = new byte[uncompressedSize];

            do
            {
                if (maskBitCounter == 0)
                {
                    currentMask = reader.ReadInt32();

                    maskBitCounter = 32;
                }

                if (((uint)currentMask & (uint)0x80000000) == 0x80000000)
                {
                    uncompData[currentOffsetInDestBuffer] = reader.ReadByteAt(nonLinkedTableOffset);

                    currentOffsetInDestBuffer++;

                    nonLinkedTableOffset++;
                }

                else
                {
                    ushort link = reader.ReadUInt16At(linkTableOffset);

                    linkTableOffset += 2;

                    int offset = currentOffsetInDestBuffer - (link & 0xfff);

                    int count = link >> 12;

                    if (count == 0)
                    {
                        byte countModifier;

                        countModifier = reader.ReadByteAt(nonLinkedTableOffset);

                        nonLinkedTableOffset++;

                        count = countModifier + 18;
                    }

                    else
                        count += 2;

                    int blockCopy = offset;

                    for (int i = 0; i < count; i++)
                    {
                        uncompData[currentOffsetInDestBuffer] = uncompData[blockCopy - 1];

                        currentOffsetInDestBuffer++;

                        blockCopy++;
                    }
                }

                currentMask <<= 1;

                maskBitCounter--;

            } while (currentOffsetInDestBuffer < uncompressedSize);

            return new MemoryStream(uncompData);
        }
    }
}

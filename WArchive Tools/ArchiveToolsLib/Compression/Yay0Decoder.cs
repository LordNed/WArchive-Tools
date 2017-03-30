using GameFormatReader.Common;
using System.IO;

namespace WArchiveTools.Compression
{
    public static partial class Yay0
    {
        public static MemoryStream Decode(EndianBinaryReader reader)
        {
            if (reader.ReadUInt32() != 0x59617930) // "Yay0" Magic
            {
                throw new InvalidDataException("Invalid Magic, not a Yay0 File");
            }

            int uncompressedSize = reader.ReadInt32();
            int linkTableOffset = reader.ReadInt32();
            int byteChunkAndCountModifiersOffset = reader.ReadInt32();

            int maskBitCounter = 0;
            int currentOffsetInDestBuffer = 0;
            int currentMask = 0;

            byte[] uncompressedData = new byte[uncompressedSize];

            do
            {
                // If we're out of bits, get the next mask.
                if (maskBitCounter == 0)
                {
                    currentMask = reader.ReadInt32();
                    maskBitCounter = 32;
                }

                // If the next bit is set, the chunk is non-linked and just copy it from the non-link table.
                if (((uint)currentMask & (uint)0x80000000) == 0x80000000)
                {
                    uncompressedData[currentOffsetInDestBuffer] = reader.ReadByteAt(byteChunkAndCountModifiersOffset);
                    currentOffsetInDestBuffer++;
                    byteChunkAndCountModifiersOffset++;
                }
                // Do a copy otherwise.
                else
                {
                    // Read 16-bit from the link table
                    ushort link = reader.ReadUInt16At(linkTableOffset);
                    linkTableOffset += 2;

                    // Calculate the offset
                    int offset = currentOffsetInDestBuffer - (link & 0xfff);

                    // Calculate the count
                    int count = link >> 12;

                    if (count == 0)
                    {
                        byte countModifier;
                        countModifier = reader.ReadByteAt(byteChunkAndCountModifiersOffset);
                        byteChunkAndCountModifiersOffset++;
                        count = countModifier + 18;
                    }
                    else
                    {
                        count += 2;
                    }

                    // Copy the block
                    int blockCopy = offset;

                    for (int i = 0; i < count; i++)
                    {
                        uncompressedData[currentOffsetInDestBuffer] = uncompressedData[blockCopy - 1];
                        currentOffsetInDestBuffer++;
                        blockCopy++;
                    }
                }

                // Get the next bit in the mask.
                currentMask <<= 1;
                maskBitCounter--;

            } while (currentOffsetInDestBuffer < uncompressedSize);

            return new MemoryStream(uncompressedData);
        }
    }
}

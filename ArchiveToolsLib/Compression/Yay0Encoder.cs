using GameFormatReader.Common;
using System;
using System.IO;

namespace WArchiveTools.Compression
{
    public static partial class Yay0
    {
        static int sNumBytes1, sMatchPos;
        static bool sPrevFlag = false;

        public static EndianBinaryWriter Encode(MemoryStream input)
        {
            if (input == null)
                throw new ArgumentNullException("input", "input MemoryStream should not be null!");
            if (input.Length == 0)
                throw new ArgumentNullException("input", "Cannot incode empty MemoryStream !");

            EndianBinaryWriter output = new EndianBinaryWriter(new MemoryStream((int)input.Length), Endian.Big);
            output.Write((int)0x59617930); // "Yay0" Magic
            output.Write((int)input.Length); // Write uncompressed data size.
            output.Write((int)0); // Placeholder for Link Table
            output.Write((int)0); // Placeholder for Chunk/Count Table

            EncodeYay0(input, output);
            return output;
        }

        private static void EncodeYay0(MemoryStream input, EndianBinaryWriter output)
        {
            int srcPos = 0;
            int progressPercent = -1;

            // Represents the current code byte which doesn't get writen until it's filled.
            int validBitCount = 0;
            int curCodeByte = 0;

            EndianBinaryWriter packedData = new EndianBinaryWriter(new MemoryStream(), Endian.Big); // The packed data, created out of code bytes
            EndianBinaryWriter linkTable = new EndianBinaryWriter(new MemoryStream(), Endian.Big); // Raw data bytes which can be copied from
            EndianBinaryWriter chunkTable = new EndianBinaryWriter(new MemoryStream(), Endian.Big); // Chunk & Modifiers table.

            // Create a byte array out of it because all existing code works with indexes already.
            byte[] src = new byte[input.Length];
            input.Read(src, 0, (int)input.Length);

            while(srcPos < input.Length)
            {
                // TODO: Encode somehow.
                int numBytes, matchPos;
                NintendoYay0Encode(src, srcPos, out numBytes, out matchPos);
                Console.WriteLine("RLE Encode Result: srcPos: {0} numBytes: {1} matchPos: {2}", srcPos, numBytes, matchPos);

                if(numBytes < 3)
                {
                    // If there's less than 3 bytes in the run, we just have to straight copy it over to the chunk & modifiers table, as
                    // the link can't refer to anything less than 3 bytes.
                    //ToDo: This might be wrong if numBytes == 2, but this is how Yaz0 encoding works and it doesn't break anything??
                    chunkTable.Write(src[srcPos]);
                    srcPos++;

                    // Set the flag for straight copy
                    curCodeByte |= 0x8000 >> validBitCount;
                }
                else
                {
                    // RLE part. Yay0 stores a 16-bit value in the link table. The first 12 bits of this
                    // store the offset into the source data to pull from, while the last 4 bits store a 
                    // count modifier of how many bytes.

                    short linkVal = (short)matchPos;

                    // If the count modifier is set to zero, then read the next byte from the chunk table
                    // which stores the count, plus 18. Otherwise, the count is -= 2, as decoding += 2 to
                    // the count. (???)

                    // Finally, increment our srcPos by the number of bytes we encoded.
                    srcPos += numBytes;

                    if (numBytes > 17)
                    {
                        numBytes -= 18;
                        chunkTable.Write((byte)numBytes);
                    }
                    else
                    {
                        // If there's less than 15, it looks like we can just write the number in directly.
                        linkVal |= (short)(((numBytes-2) << 12));
                    }

                    linkTable.Write(linkVal);

    
                }

                validBitCount++;
                
                // Write the current code byte if we've filled it up.
                if(validBitCount == 32)
                {
                    packedData.Write(curCodeByte);

                    curCodeByte = 0;
                    validBitCount = 0;
                }

                if((srcPos + 1) * 100/input.Length != progressPercent)
                {
                    progressPercent = (int)((srcPos + 1) * 100 / input.Length);
                    Console.WriteLine("{0}%", progressPercent);
                }
            }

            // If we didn't finish off a whole code byte, add the last code byte.
            if(validBitCount > 0)
            {
                packedData.Write(curCodeByte);
            }

            // Now, copy our tables to the output file.
            packedData.Seek(0, SeekOrigin.Begin);
            linkTable.Seek(0, SeekOrigin.Begin);
            chunkTable.Seek(0, SeekOrigin.Begin);

            // Packed Data
            packedData.BaseStream.CopyTo(output.BaseStream);
            int linkTableOffset = (int)output.BaseStream.Position;

            // Link Table
            linkTable.BaseStream.CopyTo(output.BaseStream);
            int chunkTableOffset = (int)output.BaseStream.Position;

            // Chunk Table.
            chunkTable.BaseStream.CopyTo(output.BaseStream);


            // Finally jump back and write the offsets in our header.
            output.Seek(0x8, SeekOrigin.Begin);
            output.Write(linkTableOffset);
            output.Write(chunkTableOffset);
        }


        private static void NintendoYay0Encode(byte[] src, int srcPos, out int outNumBytes, out int outMatchPos)
        {
            int startPos = srcPos - 0x1000;
            int numBytes = 1;

            // If prevFlag is set, it means that the previous position was determined by the look-ahead try so use
            // that. This is not the best optimization, but apparently Nintendo's choice for speed.
            if (sPrevFlag)
            {
                outMatchPos = sMatchPos;
                sPrevFlag = false;
                outNumBytes = sNumBytes1;
                return;
            }

            sPrevFlag = false;
            SimpleRLEEncode(src, srcPos, out numBytes, out sMatchPos);
            outMatchPos = sMatchPos;

            // If this position is RLE encoded, then compare to copying 1 byte and next pos (srcPos + 1) encoding.
            if (numBytes >= 3)
            {
                SimpleRLEEncode(src, srcPos + 1, out sNumBytes1, out sMatchPos);

                // If the next position encoding is +2 longer than current position, choose it.
                // This does not gurantee the best optimization, but fairly good optimization with speed.
                if (sNumBytes1 >= numBytes + 2)
                {
                    numBytes = 1;
                    sPrevFlag = true;
                }
            }

            outNumBytes = numBytes;
        }

        private static void SimpleRLEEncode(byte[] src, int srcPos, out int outNumBytes, out int outMatchPos)
        {
            int startPos = srcPos - 0x400;
            int numBytes = 1;
            int matchPos = 0;

            if (startPos < 0)
                startPos = 0;

            // Search backwards through the stream for an already encoded bit.
            for (int i = startPos; i < srcPos; i++)
            {
                int j;
                for (j = 0; j < src.Length - srcPos; j++)
                {
                    if (src[i + j] != src[j + srcPos])
                        break;
                }

                if (j > numBytes)
                {
                    numBytes = j;
                    matchPos = i;
                }
            }

            outMatchPos = matchPos;
            if (numBytes == 2)
                numBytes = 1;

            outNumBytes = numBytes;
        }
    }
}

using GameFormatReader.Common;
using System;
using System.IO;

namespace WArchiveTools.Compression
{
    public static partial class Yay0
    {
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
            byte curCodeByte = 0;

            EndianBinaryWriter packedData = new EndianBinaryWriter(new MemoryStream(), Endian.Big); // The packed data, created out of code bytes
            EndianBinaryWriter linkTable = new EndianBinaryWriter(new MemoryStream(), Endian.Big); // Raw data bytes which can be copied from
            EndianBinaryWriter chunkTable = new EndianBinaryWriter(new MemoryStream(), Endian.Big); // Chunk & Modifiers table.

            while(srcPos < input.Length)
            {
                // TODO: Encode somehow.


                validBitCount++;
                
                // Write the current code byte if we've filled it up.
                if(validBitCount == 8)
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
                srcPos++;
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
    }
}

using GameFormatReader.Common;
using System;
using System.IO;
using WArchiveTools.Compression;

namespace WArchiveTools
{
    public static class FileUtilities
    {
        /// <summary>
        /// Loads a file into a <see cref="EndianBinaryReader"/>, automatically de-compressing the file if required.
        /// Throws an exception if the filepath to the file is not valid.
        /// </summary>
        /// <param name="filePath">Filepath of file to (optionally) decompress and load.</param>
        /// <returns><see cref="EndianBinaryReader"/> containing the contents.</returns>
        public static EndianBinaryReader LoadFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException("filePath", "Cannot load archive from empty file path!");

            if (!File.Exists(filePath))
                throw new ArgumentException("Cannot load archive from non-existant file!", "filePath");

            MemoryStream decompressedFile = null;
            using (EndianBinaryReader fileReader = new EndianBinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Endian.Big))
            {
                // Read the first 4 bytes to see if it's a compressed file (Yaz0, Yay0, etc.)
                uint fileMagic = fileReader.ReadUInt32();
                fileReader.BaseStream.Position = 0L; // Reset to the start so that the next thing to read it is at the start like it expects.

                switch (fileMagic)
                {
                    case 0x59617A30: // Yaz0 Compression
                        decompressedFile = Yaz0.Decode(fileReader);
                        break;

                    case 0x59617930: // Yay0 Compression
                        decompressedFile = Yay0.Decode(fileReader);
                        break;

                    default: // Uncompressed
                        decompressedFile = new MemoryStream((int)fileReader.BaseStream.Length);
                        fileReader.BaseStream.CopyTo(decompressedFile);

                        // CopyTo modifies the decompressedFile's read head (places it at new location) so we rewind.
                        decompressedFile.Position = 0L;
                        break;
                }
            }

            // Return the decompressed file
            return new EndianBinaryReader(decompressedFile, Endian.Big);
        }
    }
}

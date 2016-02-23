using GameFormatReader.Common;
using System;
using System.IO;
using WArchiveTools.rarc;
using WArchiveTools.yaz0;
using WEditor.FileSystem;
namespace WArchiveTools
{
    public static class ArcUtilities
    {
        /// <summary>
        /// Loads an archive into a <see cref="VirtualFilesystemDirectory"/>, automatically de-compressing the archive if required.
        /// </summary>
        /// <param name="filePath">Filepath of file to decompress and load.</param>
        /// <returns><see cref="VirtualFilesystemDirectory"/> containing the contents, or null if not an archive.</returns>
        public static VirtualFilesystemDirectory LoadArchive(string filePath)
        {
            if(string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException("filePath", "Cannot load archive from empty file path!");

            if (!File.Exists(filePath))
                throw new ArgumentException("Cannot load archive from non-existant file!", "filePath");

            MemoryStream decompressedFile = null;
            using (EndianBinaryReader fileReader = new EndianBinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Endian.Big))
            {
                // Read the first 4 bytes to see if it's a compressed file (Yaz0) or a plain RARC file.
                uint fileMagic = fileReader.ReadUInt32();
                fileReader.BaseStream.Position = 0L; // Reset to the start so that the next thing to read it is at the start like it expects.

                if (fileMagic == 0x59617A30) // Yaz0
                {
                    Yaz0 yaz0 = new Yaz0();
                    decompressedFile = yaz0.Decode(fileReader);
                }
                else if (fileMagic == 0x52415243) // RARC
                {
                    // Copy the fileReader stream to a new memorystream.
                    decompressedFile = new MemoryStream((int)fileReader.BaseStream.Length);
                    fileReader.BaseStream.CopyTo(decompressedFile);
                    decompressedFile.Position = 0L;
                }
            }

            // Not an archive we know how to handle.
            if (decompressedFile == null)
                return null;

            // Decompress the archive into the folder. It'll generate a sub-folder with the Archive's ROOT name.
            RARC rarc = new RARC();
            using (EndianBinaryReader reader = new EndianBinaryReader(decompressedFile, Endian.Big))
            {
                return rarc.ReadFile(reader);
            }
        }
    }
}

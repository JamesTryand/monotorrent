using System;
using System.Collections.Generic;
using System.Text;
using MonoTorrent.Client;
using MonoTorrent.Common;
using System.Threading;

namespace MonoTorrent.Client.PieceWriters
{
    public class MemoryWriter : PieceWriter
    {
        private int capacity;
        private List<BufferedIO> memoryBuffer;
        private PieceWriter writer;


        public int Capacity
        {
            get { return capacity; }
        }

        public int Used
        {
            get { return this.memoryBuffer.Count * Piece.BlockSize; }
        }

        public MemoryWriter(PieceWriter writer)
            : this(writer, 2 * 1024 * 1024)
        {

        }

        public MemoryWriter(PieceWriter writer, int capacity)
        {
            if (writer == null)
                throw new ArgumentNullException("writer");

            if (capacity < 0)
                throw new ArgumentOutOfRangeException("capacity");

            memoryBuffer = new List<BufferedIO>();
            this.capacity = capacity;
            this.writer = writer;
        }

        public override int Read(BufferedIO data)
        {
            if(data == null)
                throw new ArgumentNullException("data");

            memoryBuffer.Sort(delegate(BufferedIO left, BufferedIO right) { return left.Offset.CompareTo(right.Offset); });
            BufferedIO io = memoryBuffer.Find(delegate(BufferedIO m) {
                return (data.PieceIndex == m.PieceIndex && data.BlockIndex == m.BlockIndex);
            });

            if (io == null)
                return writer.Read(data);

            int toCopy = Math.Min(data.Count, io.Count + (int)(io.Offset - data.Offset));
            Buffer.BlockCopy(io.buffer.Array, io.buffer.Offset + (int)(io.Offset - data.Offset), data.buffer.Array, data.buffer.Offset, toCopy);
            data.ActualCount += toCopy;
            return toCopy;
        }

        public override void Write(BufferedIO data)
        {
            Write(data, false);
        }

        public void Write(BufferedIO data, bool forceWrite)
        {
            if (forceWrite)
            {
                writer.Write(data);
                return;
            }

            if (Used > (Capacity - data.Count))
                Flush(delegate(BufferedIO io) { return memoryBuffer[0] == io; });

            memoryBuffer.Add(data);
        }
        
        public override void Close(TorrentFile file)
        {
            Flush(file);
            writer.Close(file);
        }

        public override bool Exists(TorrentFile file)
        {
            return this.writer.Exists(file);
        }

        public override void Flush(TorrentFile file)
        {
            Flush(delegate(BufferedIO io) {
                return io.Files.IndexOf (file) != -1 &&
                    io.PieceIndex >= file.StartPieceIndex &&
                    io.PieceIndex <= file.EndPieceIndex;
            });
        }

        public void Flush(Predicate<BufferedIO> flush)
        {
            memoryBuffer.ForEach(delegate(BufferedIO io)
            {
                if (!flush(io))
                    return;

                Write(io, true);
            });

            memoryBuffer.RemoveAll(flush);
        }

        public override void Move(string oldPath, string newPath, bool ignoreExisting)
        {
            writer.Move(oldPath, newPath, ignoreExisting);
        }

        public override void Dispose()
        {
            // Flush everything in memory to disk
            Flush(delegate { return true; });

            // Dispose the held writer
            writer.Dispose();

            base.Dispose();
        }
    }
}

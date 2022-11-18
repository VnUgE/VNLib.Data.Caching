using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils;
using VNLib.Utils.IO;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;

#nullable enable

namespace VNLib.Data.Caching
{
    /// <summary>
    /// A general purpose binary storage item 
    /// </summary>
    public class BlobItem //: VnDisposeable
    {
        /*
        private static readonly JoinableTaskContext JTX = new();
        private static readonly Semaphore CentralSwapLock = new(Environment.ProcessorCount, Environment.ProcessorCount);

        private readonly VnMemoryStream _loadedData;
        private bool _loaded;

        /// <summary>
        /// The time the blob was last modified
        /// </summary>
        public DateTimeOffset LastAccessed { get; private set; }
     

        /// <summary>
        /// Gets the current size of the file (in bytes) as an atomic operation
        /// </summary>
        public int FileSize => (int)_loadedData.Length;
        /// <summary>
        /// The operation synchronization lock
        /// </summary>
        public AsyncReaderWriterLock OpLock { get; }
        /// <summary>
        /// Initializes a new <see cref="BlobItem"/>
        /// </summary>
        /// <param name="heap">The heap to allocate buffers from</param>
        internal BlobItem(IUnmangedHeap heap)
        {
            _loadedData = new(heap);
            OpLock = new AsyncReaderWriterLock(JTX);
            _loaded = true;
            LastAccessed = DateTimeOffset.UtcNow;
        }
        ///<inheritdoc/>
        protected override void Free()
        {
            _loadedData.Dispose();
            OpLock.Dispose();
        }

        /// <summary>
        /// Reads data from the internal buffer and copies it to the specified buffer.
        /// Use the <see cref="FileSize"/> property to obtain the size of the internal buffer
        /// </summary>
        /// <param name="buffer">The buffer to copy data to</param>
        /// <returns>When completed, the number of bytes copied to the buffer</returns>
        public int Read(Span<byte> buffer)
        {
            //Make sure the blob has been swapped back into memory
            if (!_loaded)
            {
                throw new InvalidOperationException("The blob was not loaded from the disk");
            }
            //Read all data from the buffer and write it to the output buffer
            _loadedData.AsSpan().CopyTo(buffer);
            //Update last-accessed
            LastAccessed = DateTimeOffset.UtcNow;
            return (int)_loadedData.Length;
        }
        /// <summary>
        /// Overwrites the internal buffer with the contents of the supplied buffer
        /// </summary>
        /// <param name="buffer">The buffer containing data to store within the blob</param>
        /// <returns>A <see cref="ValueTask"/> that completes when write access has been granted and copied</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public void Write(ReadOnlySpan<byte> buffer)
        {
            //Make sure the blob has been swapped back into memory
            if (!_loaded)
            {
                throw new InvalidOperationException("The blob was not loaded from the disk");
            }
            //Reset the buffer
            _loadedData.SetLength(buffer.Length);
            _loadedData.Seek(0, SeekOrigin.Begin);
            _loadedData.Write(buffer);
            LastAccessed = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Writes the contents of the memory buffer to its designated file on the disk
        /// </summary>
        /// <param name="heap">The heap to allocate buffers from</param>
        /// <param name="swapDir">The <see cref="IsolatedStorageDirectory"/> that stores the file</param>
        /// <param name="filename">The name of the file to write data do</param>
        /// <param name="log">A log to write errors to</param>
        /// <returns>A task that completes when the swap to disk is complete</returns>
        internal async Task SwapToDiskAsync(IUnmangedHeap heap, DirectoryInfo swapDir, string filename, ILogProvider log)
        {
            try
            {
                //Wait for write lock
                await using (AsyncReaderWriterLock.Releaser releaser = await OpLock.WriteLockAsync())
                {
                    //Enter swap lock
                    await CentralSwapLock;
                    try
                    {
                        //Open swap file data stream
                        await using FileStream swapFile = swapDir.OpenFile(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite, bufferSize: 8128);
                        //reset swap file
                        swapFile.SetLength(0);
                        //Seek loaded-data back to 0 before writing
                        _loadedData.Seek(0, SeekOrigin.Begin);
                        //Write loaded data to disk
                        await _loadedData.CopyToAsync(swapFile, 8128, heap);
                    }
                    finally
                    {
                        CentralSwapLock.Release();
                    }
                    //Release memory held by stream
                    _loadedData.SetLength(0);
                    //Clear loaded flag
                    _loaded = false;
                    LastAccessed = DateTimeOffset.UtcNow;
                }
                log.Debug("Blob {name} swapped to disk", filename);
            }
            catch(Exception ex)
            {
                log.Error(ex, "Blob swap to disk error");
            }
        }
        /// <summary>
        /// Reads the contents of the blob into a memory buffer from its designated file on disk
        /// </summary>
        /// <param name="heap">The heap to allocate buffers from</param>
        /// <param name="swapDir">The <see cref="IsolatedStorageDirectory"/> that stores the file</param>
        /// <param name="filename">The name of the file to write the blob data to</param>
        /// <param name="log">A log to write errors to</param>
        /// <returns>A task that completes when the swap from disk is complete</returns>
        internal async Task SwapFromDiskAsync(IUnmangedHeap heap, DirectoryInfo swapDir, string filename, ILogProvider log)
        {
            try
            {
                //Wait for write lock
                await using (AsyncReaderWriterLock.Releaser releaser = await OpLock.WriteLockAsync())
                {
                    //Enter swap lock
                    await CentralSwapLock;
                    try
                    {
                        //Open swap file data stream
                        await using FileStream swapFile = swapDir.OpenFile(filename, FileMode.OpenOrCreate, FileAccess.Read, bufferSize:8128);
                        //Copy from disk to memory
                        await swapFile.CopyToAsync(_loadedData, 8128, heap);
                    }
                    finally
                    {
                        CentralSwapLock.Release();
                    }
                    //Set loaded flag
                    _loaded = true;
                    LastAccessed = DateTimeOffset.UtcNow;
                }
                log.Debug("Blob {name} swapped from disk", filename);
            }
            catch(Exception ex)
            {
                log.Error(ex, "Blob swap from disk error");
            }
        }
        */
    }
}

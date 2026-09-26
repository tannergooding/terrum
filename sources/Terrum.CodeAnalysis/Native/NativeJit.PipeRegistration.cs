// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Terrum.CodeAnalysis.Native;

internal sealed class NativeJitPipeRegistration : INativeJitContinuationRegistration
{
    private readonly BinaryReader _reader;
    private readonly BinaryWriter _writer;
    private readonly TimeSpan? _responseTimeout;
    private bool _pending;

    internal NativeJitPipeRegistration(string inputHandle, string outputHandle)
        : this(new AnonymousPipeClientStream(PipeDirection.In, inputHandle),
            new AnonymousPipeClientStream(PipeDirection.Out, outputHandle))
    {
    }

    internal NativeJitPipeRegistration(Stream input, Stream output, TimeSpan? responseTimeout = null,
        bool leaveOpen = false)
    {
        _reader = new BinaryReader(input, Encoding.UTF8, leaveOpen);
        _writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen);
        _responseTimeout = responseTimeout;
    }

    public void Register(nint memory, int textLength, int[] offsets)
    {
        if (offsets.Length is < 1 or > ushort.MaxValue || textLength is < 1 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(offsets));
        }
        _pending = true;
        _writer.Write((byte)1);
        _writer.Write(checked((ushort)offsets.Length));
        _writer.Write(memory.ToInt64());
        _writer.Write(textLength);
        foreach (var offset in offsets)
        {
            _writer.Write(offset);
        }
        _writer.Flush();
        var response = Response();
        if (response == 1)
        {
            return;
        }
        if (response == 0)
        {
            _pending = false;
        }
        throw new InvalidOperationException("Native JIT supervisor could not register checked continuations.");
    }

    public void Dispose()
    {
        if (_pending)
        {
            _writer.Write((byte)2);
            _writer.Flush();
            if (Response() != 1)
            {
                throw new InvalidOperationException("Native JIT supervisor could not remove checked continuations; code remains allocated.");
            }
            _pending = false;
        }
        _writer.Dispose();
        _reader.Dispose();
    }

    private byte Response()
    {
        if (_responseTimeout is null)
        {
            return _reader.ReadByte();
        }
        var buffer = new byte[1];
        var read = _reader.BaseStream.ReadAsync(buffer).AsTask()
            .WaitAsync(_responseTimeout.Value).GetAwaiter().GetResult();
        if (read != 1)
        {
            throw new EndOfStreamException("Native JIT continuation registrar exited before acknowledging the target set.");
        }
        return buffer[0];
    }
}

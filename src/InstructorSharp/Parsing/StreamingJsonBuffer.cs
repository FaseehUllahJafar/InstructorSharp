using System.Text;

namespace InstructorSharp.Parsing;

/// <summary>
/// Accumulates streamed text as UTF-8 bytes so each chunk is encoded exactly once.
/// </summary>
/// <remarks>
/// <para>
/// The naive version of this keeps a <see cref="StringBuilder"/> and calls
/// <c>Encoding.UTF8.GetBytes(builder.ToString())</c> on every chunk, which re-encodes the entire
/// response for each arriving fragment. A long answer delivered in many small chunks then costs
/// quadratic allocation, and the garbage that produces is the sort of thing that only shows up
/// once a feature meets production traffic.
/// </para>
/// <para>
/// Encoding incrementally also fixes a correctness bug that the naive version hides. An emoji or
/// other astral character is a surrogate pair in UTF-16, and a provider is free to split that
/// pair across two chunks. Encoding each chunk independently turns the orphaned half into a
/// replacement character and corrupts the text permanently. The stateful <see cref="Encoder"/>
/// used here holds the high surrogate back until its partner arrives.
/// </para>
/// </remarks>
internal sealed class StreamingJsonBuffer
{
    private readonly Encoder _encoder = Encoding.UTF8.GetEncoder();
    private readonly int _maxBytes;
    private byte[] _bytes = new byte[1024];
    private int _length;
    private bool _flushed;

    internal StreamingJsonBuffer(int maxBytes) => _maxBytes = maxBytes;

    /// <summary>Total bytes accumulated so far.</summary>
    internal int Length => _length;

    /// <summary>Appends a chunk of streamed text.</summary>
    /// <param name="chunk">The text fragment.</param>
    internal void Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        // GetMaxByteCount rather than GetByteCount: it is a pure calculation on the encoding,
        // whereas GetByteCount runs through the stateful encoder. The worst case it reserves is
        // (n+1)*3 bytes, which also covers a surrogate held over from the previous chunk.
        int required = Encoding.UTF8.GetMaxByteCount(chunk.Length);

        // A model that loops can stream without end. Without a ceiling the buffer grows until the
        // process dies, and the caller's cancellation token cannot help because the growth happens
        // between yields.
        if ((long)_length + required > _maxBytes)
        {
            throw new InstructorException(
                $"The streamed response exceeded {_maxBytes} bytes without completing. " +
                "Raise InstructorOptions.MaxStreamBytes if this limit is too low for your payloads.");
        }

        EnsureCapacity(_length + required);

        int written = _encoder.GetBytes(
            chunk.ToCharArray(), 0, chunk.Length,
            _bytes, _length,
            flush: false);

        _length += written;
    }

    /// <summary>
    /// Attempts to read what has arrived so far as a complete JSON document.
    /// </summary>
    /// <param name="json">The repaired document.</param>
    /// <returns>True when something parseable could be produced.</returns>
    internal bool TryComplete(out string json) =>
        PartialJsonCompleter.TryComplete(new ReadOnlySpan<byte>(_bytes, 0, _length), out json);

    /// <summary>Decodes everything accumulated. Call once, at the end of the stream.</summary>
    /// <returns>The full streamed text.</returns>
    internal string GetText()
    {
        Flush();
        return Encoding.UTF8.GetString(_bytes, 0, _length);
    }

    /// <summary>
    /// Releases anything the encoder is still holding. Without this a response whose final chunk
    /// ends on an unpaired high surrogate loses that character silently, which is the opposite of
    /// what the stateful encoder is here to achieve.
    /// </summary>
    private void Flush()
    {
        if (_flushed)
        {
            return;
        }

        _flushed = true;

        int required = _encoder.GetByteCount([], 0, 0, flush: true);
        if (required == 0)
        {
            return;
        }

        EnsureCapacity(_length + required);
        _length += _encoder.GetBytes([], 0, 0, _bytes, _length, flush: true);
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _bytes.Length)
        {
            return;
        }

        // Doubling a signed int overflows to a negative once past 2^30, after which the loop
        // below would never terminate and would spin a core indefinitely.
        long capacity = _bytes.Length;
        while (capacity < required)
        {
            capacity *= 2;
        }

        Array.Resize(ref _bytes, (int)Math.Min(capacity, MaxArrayLength));
    }

    /// <summary>Largest array the runtime will allocate, conservatively stated.</summary>
    private const int MaxArrayLength = 0x7FFFFFC7;
}

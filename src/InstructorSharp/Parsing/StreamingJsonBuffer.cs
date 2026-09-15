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
    private byte[] _bytes = new byte[1024];
    private int _length;

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

        // GetByteCount with flush:false accounts for any surrogate still held from last time.
        int required = _encoder.GetByteCount(chunk.ToCharArray(), 0, chunk.Length, flush: false);
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
    internal string GetText() => Encoding.UTF8.GetString(_bytes, 0, _length);

    private void EnsureCapacity(int required)
    {
        if (required <= _bytes.Length)
        {
            return;
        }

        int capacity = _bytes.Length;
        while (capacity < required)
        {
            capacity *= 2;
        }

        Array.Resize(ref _bytes, capacity);
    }
}

namespace Kun.Storage.Abstractions;

/// <summary>
/// Content stream plus metadata returned from
/// <see cref="IFileStorage.GetObjectAsync(string, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// Dispose this instance to release the underlying <see cref="Stream"/>.
/// </remarks>
public sealed class ObjectDownload : IAsyncDisposable, IDisposable
{
    /// <summary>Initializes a new download result.</summary>
    public ObjectDownload(Stream content, ObjectMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(metadata);
        Content = content;
        Metadata = metadata;
    }

    /// <summary>The object's payload. Ownership is transferred to the caller.</summary>
    public Stream Content { get; }

    /// <summary>Metadata for the downloaded object.</summary>
    public ObjectMetadata Metadata { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Content.DisposeAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public void Dispose() => Content.Dispose();
}

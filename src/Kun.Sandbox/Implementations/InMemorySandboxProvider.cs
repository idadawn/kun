using System.Collections.Concurrent;
using System.Diagnostics;
using Kun.Sandbox.Abstractions;

namespace Kun.Sandbox.Implementations;

/// <summary>
/// In-process fake <see cref="ISandboxProvider"/> for unit tests and
/// local dev loops. Does not run anything: <c>processes.run</c> returns
/// a scripted <see cref="ProcessResult"/> and file APIs operate on a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. Clearly labelled
/// "test only" so no-one accidentally wires it into production.
/// </summary>
public sealed class InMemorySandboxProvider : ISandboxProvider
{
    private readonly Func<IReadOnlyList<string>, ProcessRunOptions?, ProcessResult>? _runScript;
    private long _counter;

    /// <summary>Creates a provider that returns an empty <c>ProcessResult</c> for every <c>run</c> call (exit 0, empty stdout/stderr).</summary>
    public InMemorySandboxProvider() : this(runScript: null)
    {
    }

    /// <summary>
    /// Creates a provider with a caller-supplied scripted response for
    /// <see cref="IProcesses.RunAsync(string[], ProcessRunOptions?, CancellationToken)"/>.
    /// </summary>
    /// <param name="runScript">
    /// Invoked on every <c>run</c> call; receives the argv and options
    /// and must return the <see cref="ProcessResult"/> the test expects.
    /// </param>
    public InMemorySandboxProvider(Func<IReadOnlyList<string>, ProcessRunOptions?, ProcessResult>? runScript)
    {
        _runScript = runScript;
    }

    /// <inheritdoc />
    public Task<ISandboxHandle> CreateAsync(SandboxCreateOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var id = $"sb_mem_{Interlocked.Increment(ref _counter)}";
        var now = DateTimeOffset.UtcNow;
        var handle = new InMemorySandboxHandle(
            sandboxId: id,
            templateId: options.TemplateId,
            startedAt: now,
            endAt: now.AddMilliseconds(options.TimeoutMs),
            runScript: _runScript);

        return Task.FromResult<ISandboxHandle>(handle);
    }

    private sealed class InMemorySandboxHandle : ISandboxHandle
    {
        private readonly InMemoryProcesses _processes;
        private readonly InMemoryFiles _files;
        private int _killed;

        public InMemorySandboxHandle(
            string sandboxId,
            string templateId,
            DateTimeOffset startedAt,
            DateTimeOffset endAt,
            Func<IReadOnlyList<string>, ProcessRunOptions?, ProcessResult>? runScript)
        {
            SandboxId = sandboxId;
            TemplateId = templateId;
            StartedAt = startedAt;
            EndAt = endAt;
            _processes = new InMemoryProcesses(this, runScript);
            _files = new InMemoryFiles(this);
        }

        public string SandboxId { get; }

        public string TemplateId { get; }

        public SandboxState State => _killed == 0 ? SandboxState.Running : SandboxState.Killed;

        public DateTimeOffset StartedAt { get; }

        public DateTimeOffset EndAt { get; }

        public IProcesses Processes => _processes;

        public IFiles Files => _files;

        internal bool IsKilled => _killed != 0;

        public Task KillAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _killed, 1);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryProcesses : IProcesses
    {
        private readonly InMemorySandboxHandle _handle;
        private readonly Func<IReadOnlyList<string>, ProcessRunOptions?, ProcessResult>? _runScript;
        private long _processCounter;

        public InMemoryProcesses(
            InMemorySandboxHandle handle,
            Func<IReadOnlyList<string>, ProcessRunOptions?, ProcessResult>? runScript)
        {
            _handle = handle;
            _runScript = runScript;
        }

        public Task<ProcessResult> RunAsync(
            string[] command,
            ProcessRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAlive();

            var sw = Stopwatch.StartNew();
            var scripted = _runScript?.Invoke(command, options);
            sw.Stop();

            var result = scripted ?? new ProcessResult
            {
                ProcessId = $"proc_mem_{Interlocked.Increment(ref _processCounter)}",
                ExitCode = 0,
                Stdout = string.Empty,
                Stderr = string.Empty,
                DurationMs = sw.ElapsedMilliseconds,
                Truncated = false,
            };

            return Task.FromResult(result);
        }

        public Task<ProcessResult> RunAsync(
            string command,
            ProcessRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(command);
            return RunAsync(["/bin/sh", "-c", command], options, cancellationToken);
        }

        private void EnsureAlive()
        {
            if (_handle.IsKilled)
            {
                throw new InvalidOperationException(
                    $"Sandbox '{_handle.SandboxId}' has been killed; no further commands may be executed.");
            }
        }
    }

    private sealed class InMemoryFiles : IFiles
    {
        private readonly InMemorySandboxHandle _handle;
        private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public InMemoryFiles(InMemorySandboxHandle handle)
        {
            _handle = handle;
        }

        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAlive();

            if (!_files.TryGetValue(path, out var bytes))
            {
                throw new FileNotFoundException($"Sandbox file not found: {path}", path);
            }

            return Task.FromResult((byte[])bytes.Clone());
        }

        public Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(content);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAlive();

            _files[path] = (byte[])content.Clone();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FileEntry>> ListAsync(
            string path,
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAlive();

            var prefix = path.EndsWith('/') ? path : path + "/";
            var matches = _files
                .Where(kv =>
                {
                    if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (recursive)
                    {
                        return true;
                    }

                    var tail = kv.Key.AsSpan(prefix.Length);
                    return tail.IndexOf('/') < 0;
                })
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new FileEntry
                {
                    Path = kv.Key,
                    Type = "file",
                    SizeBytes = kv.Value.LongLength,
                    ModifiedAt = DateTimeOffset.UtcNow,
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<FileEntry>>(matches);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAlive();

            _files.TryRemove(path, out _);
            return Task.CompletedTask;
        }

        private void EnsureAlive()
        {
            if (_handle.IsKilled)
            {
                throw new InvalidOperationException(
                    $"Sandbox '{_handle.SandboxId}' has been killed; file operations are no longer valid.");
            }
        }
    }
}

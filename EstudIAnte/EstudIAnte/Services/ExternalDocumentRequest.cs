namespace EstudIAnte.Services;

public sealed record ExternalDocumentFile(string Path, string FileName);

public static class ExternalDocumentRequest
{
    static readonly object Sync = new();
    static TaskCompletionSource<(ExternalDocumentFile? File, string? Error)>? _completion;

    public static void Begin()
    {
        lock (Sync)
            _completion = new TaskCompletionSource<(ExternalDocumentFile?, string?)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static void Set(ExternalDocumentFile? file, string? error = null)
    {
        lock (Sync)
            _completion?.TrySetResult((file, error));
    }

    public static Task<(ExternalDocumentFile? File, string? Error)>? TakeAsync()
    {
        lock (Sync)
        {
            return _completion is null ? null : TakeAndClearAsync(_completion);
        }
    }

    static async Task<(ExternalDocumentFile? File, string? Error)> TakeAndClearAsync(
        TaskCompletionSource<(ExternalDocumentFile? File, string? Error)> completion)
    {
        var result = await completion.Task;
        lock (Sync)
        {
            if (ReferenceEquals(_completion, completion))
                _completion = null;
        }
        return result;
    }
}

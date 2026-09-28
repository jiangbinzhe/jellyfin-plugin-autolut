using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AutoLut;

public sealed class LutTranscodeManager(ITranscodeManager inner, LutService luts, ILogger<LutTranscodeManager> logger) : ITranscodeManager, IDisposable
{
    public Task<TranscodingJob> StartFfMpeg(StreamState state, string outputPath, string commandLineArguments, Guid userId, TranscodingJobType transcodingJobType, CancellationTokenSource cancellationTokenSource, string? workingDirectory = null)
    {
        var plan = luts.Get(state.Request.PlaySessionId);
        if (plan != null && plan.UserId == userId && plan.DeviceId == state.Request.DeviceId && plan.ItemId == state.Request.Id
            && plan.MediaPath == state.MediaPath && File.Exists(plan.CubePath))
        {
            if (CommandPatch.TryApply(commandLineArguments, plan.CubePath, out var changed, out var reason))
            {
                commandLineArguments = changed;
                logger.LogInformation("AutoLut applied {Mode} to session {Session}", reason, state.Request.PlaySessionId);
            }
            else logger.LogWarning("AutoLut skipped unsupported transcode graph for session {Session}; video will play without LUT", state.Request.PlaySessionId);
        }
        // Do not alter outputPath: Jellyfin hashes the playback session into its cache key.
        return inner.StartFfMpeg(state, outputPath, commandLineArguments, userId, transcodingJobType, cancellationTokenSource, workingDirectory);
    }
    public TranscodingJob? GetTranscodingJob(string playSessionId) => inner.GetTranscodingJob(playSessionId);
    public TranscodingJob? GetTranscodingJob(string path, TranscodingJobType type) => inner.GetTranscodingJob(path, type);
    public void PingTranscodingJob(string playSessionId, bool? isUserPaused) => inner.PingTranscodingJob(playSessionId, isUserPaused);
    public Task KillTranscodingJobs(string deviceId, string? playSessionId, Func<string, bool> deleteFiles) => inner.KillTranscodingJobs(deviceId, playSessionId, deleteFiles);
    public void ReportTranscodingProgress(TranscodingJob job, StreamState state, TimeSpan? transcodingPosition, float? framerate, double? percentComplete, long? bytesTranscoded, int? bitRate) => inner.ReportTranscodingProgress(job, state, transcodingPosition, framerate, percentComplete, bytesTranscoded, bitRate);
    public TranscodingJob? OnTranscodeBeginRequest(string path, TranscodingJobType type) => inner.OnTranscodeBeginRequest(path, type);
    public void OnTranscodeEndRequest(TranscodingJob job) => inner.OnTranscodeEndRequest(job);
    public ValueTask<IDisposable> LockAsync(string outputPath, CancellationToken cancellationToken) => inner.LockAsync(outputPath, cancellationToken);
    // This decorator exclusively owns the one original manager instance.
    public void Dispose() { if (inner is IDisposable disposable) disposable.Dispose(); }
}

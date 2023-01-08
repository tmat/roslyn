// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.Internal.VisualStudio.Shell.Embeddable.Feedback;
using Microsoft.VisualStudio.TextManager.Interop;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis.EditAndContinue;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.VisualStudio.LanguageServices.EditAndContinue;

[Export(typeof(IFeedbackDiagnosticFileProvider))]
internal sealed class EditAndContinueFeedbackDiagnosticFileProvider : IFeedbackDiagnosticFileProvider
{
    /// <summary>
    /// Name of the file displayed in VS Feedback UI.
    /// See https://dev.azure.com/devdiv/DevDiv/_workitems/edit/1714452.
    /// </summary>
    private const string ZipFileName = "source_files_and_binaries_updated_during_hot_reload.zip";

    private const string VSFeedbackSemaphoreDir = @"Microsoft\VSFeedbackCollector";
    private const string VSFeedbackSemaphoreFileName = "feedback.recording.json";

    /// <summary>
    /// VS Feedback creates a JSON file at the start of feedback session and deletes it when the session is over.
    /// Watching the file is currently the only way to detect the feedback session.
    /// </summary>
    private readonly string _vsFeedbackSemaphoreFullPath;
    private readonly FileSystemWatcher _vsFeedbackSemaphoreFileWatcher;

    private readonly int _vsProcessId;
    private readonly DateTime _vsProcessStartTime;
    private readonly string _tempDir;

    /// <summary>
    /// Initialized to a unique temp directory we use to collect logs for this session when VS Feedback collection starts.
    /// </summary>
    private string? _feedbackDirectory;

    /// <summary>
    /// Set to <see cref="_feedbackDirectory"/> when recording starts, cleared when it finishes.
    /// </summary>
    private string? _activeFeedbackDirectory;

    private readonly EditAndContinueLanguageService _encService;

    [ImportingConstructor]
    [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
    public EditAndContinueFeedbackDiagnosticFileProvider(EditAndContinueLanguageService encService)
    {
        _encService = encService;

        var vsProcess = Process.GetCurrentProcess();

        _vsProcessId = vsProcess.Id;
        _vsProcessStartTime = vsProcess.StartTime;

        _tempDir = Path.GetTempPath();
        var vsFeedbackTempDir = Path.Combine(_tempDir, VSFeedbackSemaphoreDir);
        _vsFeedbackSemaphoreFullPath = Path.Combine(vsFeedbackTempDir, VSFeedbackSemaphoreFileName);

        _vsFeedbackSemaphoreFileWatcher = new FileSystemWatcher(vsFeedbackTempDir, VSFeedbackSemaphoreFileName);
        _vsFeedbackSemaphoreFileWatcher.Created += (_, _) => OnFeedbackSemaphoreCreatedOrChanged();
        _vsFeedbackSemaphoreFileWatcher.Changed += (_, _) => OnFeedbackSemaphoreCreatedOrChanged();
        _vsFeedbackSemaphoreFileWatcher.Deleted += (_, _) => OnFeedbackSemaphoreDeleted();

        if (File.Exists(_vsFeedbackSemaphoreFullPath))
        {
            OnFeedbackSemaphoreCreatedOrChanged();
        }

        _vsFeedbackSemaphoreFileWatcher.EnableRaisingEvents = true;
    }

    private static string GetLogDirectory(string feedbackDirectory)
        => Path.Combine(Path.Combine(feedbackDirectory, "Log"));

    private static string GetZipFilePath(string feedbackDirectory)
        => Path.Combine(Path.Combine(feedbackDirectory, ZipFileName));

    public IReadOnlyCollection<string> GetFiles()
    {
        // Create a directory unique for this session. 
        // Returns the same value if called multiple times before the recording starts.

        var dir = Path.Combine(_tempDir, $"EnC_{Guid.NewGuid()}");
        return new[] { GetZipFilePath(Interlocked.CompareExchange(ref _feedbackDirectory, dir, null) ?? dir) };
    }

    private void OnFeedbackSemaphoreCreatedOrChanged()
    {
        var feedbackDirectory = _feedbackDirectory;
        if (feedbackDirectory is null || !IsLoggingEnabledForCurrentVisualStudioInstance(_vsFeedbackSemaphoreFullPath))
        {
            // The semaphore file was created for another VS instance.
            return;
        }

        if (Interlocked.CompareExchange(ref _activeFeedbackDirectory, feedbackDirectory, null) == null)
        {
            _encService.SetFileLoggingDirectory(GetLogDirectory(feedbackDirectory));
        }
    }

    private void OnFeedbackSemaphoreDeleted()
    {
        var activeFeedbackDirectory = Interlocked.Exchange(ref _activeFeedbackDirectory, null);
        if (activeFeedbackDirectory != null)
        {
            _encService.SetFileLoggingDirectory(logDirectory: null);

            // Including the zip files in VS Feedback is currently on best effort basis.
            // See https://dev.azure.com/devdiv/DevDiv/_workitems/edit/1714439
            Task.Run(() =>
            {
                try
                {
                    ZipFile.CreateFromDirectory(GetLogDirectory(activeFeedbackDirectory), GetZipFilePath(activeFeedbackDirectory));
                }
                catch
                {
                }
            });
        }

        Interlocked.Exchange(ref _feedbackDirectory, null);
    }

    private bool IsLoggingEnabledForCurrentVisualStudioInstance(string semaphoreFilePath)
    {
        try
        {
            if (_vsProcessStartTime > File.GetCreationTime(semaphoreFilePath))
            {
                // Semaphore file is older than the running instance of VS
                return false;
            }

            // Check the contents of the semaphore file to see if it's for this instance of VS
            var content = File.ReadAllText(semaphoreFilePath);
            return JObject.Parse(content)["processIds"] is JContainer pidCollection && pidCollection.Values<int>().Contains(_vsProcessId);
        }
        catch
        {
            // Something went wrong opening or parsing the semaphore file - ignore it
            return false;
        }
    }
}

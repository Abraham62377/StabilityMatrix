﻿using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StabilityMatrix.Core.Database;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Models.FileInterfaces;
using StabilityMatrix.Core.Models.Progress;

namespace StabilityMatrix.Core.Services;

public class TrackedDownloadService : ITrackedDownloadService, IDisposable
{
    private readonly ILogger<TrackedDownloadService> logger;
    private readonly IDownloadService downloadService;
    private readonly ISettingsManager settingsManager;
    
    private readonly ConcurrentDictionary<Guid, (TrackedDownload, FileStream)> downloads = new();

    public IEnumerable<TrackedDownload> Downloads => downloads.Values.Select(x => x.Item1);
    
    /// <inheritdoc />
    public event EventHandler<TrackedDownload>? DownloadAdded;
    
    public TrackedDownloadService(
        ILogger<TrackedDownloadService> logger,
        IDownloadService downloadService,
        ISettingsManager settingsManager)
    {
        this.logger = logger;
        this.downloadService = downloadService;
        this.settingsManager = settingsManager;

        // Index for in-progress downloads when library dir loaded
        settingsManager.RegisterOnLibraryDirSet(path =>
        {
            var downloadsDir = new DirectoryPath(settingsManager.DownloadsDirectory);
            // Ignore if not exist
            if (!downloadsDir.Exists) return;
            
            LoadInProgressDownloads(downloadsDir);
        });
    }
    
    private void OnDownloadAdded(TrackedDownload download)
    {
        DownloadAdded?.Invoke(this, download);
    }

    /// <summary>
    /// Creates a new tracked download with backed json file and adds it to the dictionary.
    /// </summary>
    /// <param name="download"></param>
    private void AddDownload(TrackedDownload download)
    {
        // Set download service
        download.SetDownloadService(downloadService);
        
        // Create json file
        var downloadsDir = new DirectoryPath(settingsManager.DownloadsDirectory);
        downloadsDir.Create();
        var jsonFile = downloadsDir.JoinFile($"{download.Id}.json");
        var jsonFileStream = jsonFile.Info.Open(FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        
        // Serialize to json
        var json = JsonSerializer.Serialize(download);
        jsonFileStream.Write(Encoding.UTF8.GetBytes(json));
        jsonFileStream.Flush();
        
        // Add to dictionary
        downloads.TryAdd(download.Id, (download, jsonFileStream));
        
        // Connect to state changed event to update json file
        AttachHandlers(download);
        
        logger.LogDebug("Added download {Download}", download.FileName);
        OnDownloadAdded(download);
    }
    
    /// <summary>
    /// Update the json file for the download.
    /// </summary>
    private void UpdateJsonForDownload(TrackedDownload download)
    {
        // Serialize to json
        var json = JsonSerializer.Serialize(download);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        
        // Write to file
        var (_, fs) = downloads[download.Id];
        fs.Seek(0, SeekOrigin.Begin);
        fs.Write(jsonBytes);
        fs.Flush();
    }
    
    private void AttachHandlers(TrackedDownload download)
    {
        download.ProgressStateChanged += TrackedDownload_OnProgressStateChanged;
    }
    
    /// <summary>
    /// Handler when the download's state changes
    /// </summary>
    private void TrackedDownload_OnProgressStateChanged(object? sender, ProgressState e)
    {
        if (sender is not TrackedDownload download)
        {
            return;
        }
        
        // Update json file
        UpdateJsonForDownload(download);
        
        // If the download is completed, remove it from the dictionary and delete the json file
        if (e is ProgressState.Success or ProgressState.Failed or ProgressState.Cancelled)
        {
            if (downloads.TryRemove(download.Id, out var downloadInfo))
            {
                downloadInfo.Item2.Dispose();
                // Delete json file
                new DirectoryPath(settingsManager.DownloadsDirectory).JoinFile($"{download.Id}.json").Delete();
                logger.LogDebug("Removed download {Download}", download.FileName);
            }
        }
        
        // On successes, run the continuation action
        if (e == ProgressState.Success)
        {
            if (download.ContextAction is CivitPostDownloadContextAction action)
            {
                logger.LogDebug("Running context action for {Download}", download.FileName);
                action.Invoke(settingsManager);
            }
        }
    }
    
    private void LoadInProgressDownloads(DirectoryPath downloadsDir)
    {
        logger.LogDebug("Indexing in-progress downloads at {DownloadsDir}...", downloadsDir);
        
        var jsonFiles = downloadsDir.Info.EnumerateFiles("*.json", SearchOption.TopDirectoryOnly);
            
        // Add to dictionary, the file name is the guid
        foreach (var file in jsonFiles)
        {
            // Try to get a shared write handle
            try
            {
                var fileStream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                
                // Deserialize json and add to dictionary
                var download = JsonSerializer.Deserialize<TrackedDownload>(fileStream)!;
                download.SetDownloadService(downloadService);
                
                downloads.TryAdd(download.Id, (download, fileStream));
                
                AttachHandlers(download);
                
                OnDownloadAdded(download);
                
                logger.LogDebug("Loaded in-progress download {Download}", download.FileName);
            }
            catch (Exception e)
            {
                logger.LogInformation(e, "Could not open file {File} for reading", file.Name);
            }
        }
    }

    public TrackedDownload NewDownload(Uri downloadUrl, FilePath downloadPath)
    {
        var download = new TrackedDownload
        {
            Id = Guid.NewGuid(),
            SourceUrl = downloadUrl,
            DownloadDirectory = downloadPath.Directory!,
            FileName = downloadPath.Name,
            TempFileName = NewTempFileName(downloadPath.Directory!),
        };

        AddDownload(download);
        
        return download;
    }
    
    /// <summary>
    /// Generate a new temp file name that is unique in the given directory.
    /// In format of "Unconfirmed {id}.smdownload"
    /// </summary>
    /// <param name="parentDir"></param>
    /// <returns></returns>
    private static string NewTempFileName(DirectoryPath parentDir)
    {
        FilePath? tempFile = null;

        for (var i = 0; i < 10; i++)
        {
            if (tempFile is {Exists: false})
            {
                return tempFile.Name;
            }
            var id = Random.Shared.Next(1000000, 9999999);
            tempFile = parentDir.JoinFile($"Unconfirmed {id}.smdownload");
        }
        
        throw new Exception("Failed to generate a unique temp file name.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var (_, fs) in downloads.Values)
        {
            fs.Dispose();
        }
        
        GC.SuppressFinalize(this);
    }
}

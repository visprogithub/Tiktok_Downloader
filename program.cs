using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

class Program
{
    static async Task Main()
    {
        using var client = new HttpClient()
        {
            Timeout = TimeSpan.FromMinutes(10) // Increased timeout
        };
        
        Console.WriteLine("Reading file...");
        string[] lines = File.ReadAllLines("Post.txt");
        
        var videoPattern = new Regex(@"https://video-useast5-download\.tiktokv\.us[^?\s]*\?[^\s]+");
        var videoUrls = new List<string>();
        
        Console.WriteLine("Finding video URLs...");
        foreach (string line in lines)
        {
            if (line.StartsWith("Link:") && line.Contains("video-useast5-download"))
            {
                var match = videoPattern.Match(line);
                if (match.Success)
                {
                    videoUrls.Add(match.Value);
                }
            }
        }
        
        Console.WriteLine($"Found {videoUrls.Count} videos to download");
        int completedDownloads = 0;
        int failedDownloads = 0;
        var downloadTasks = new List<Task>();
        var semaphore = new SemaphoreSlim(3); // Limit concurrent downloads
        
        for (int i = 0; i < videoUrls.Count; i++)
        {
            int index = i; // Capture the index for async lambda
            var task = Task.Run(async () =>
            {
                await semaphore.WaitAsync(); // Wait for a slot to be available
                try
                {
                    string filename = $"video_{index}.mp4";
                    Console.WriteLine($"Starting download of {filename}");
                    
                    using var response = await client.GetAsync(videoUrls[index], HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    
                    var contentLength = response.Content.Headers.ContentLength;
                    using var sourceStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None);
                    
                    var buffer = new byte[8192];
                    var totalBytesRead = 0L;
                    int bytesRead;
                    
                    while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;
                        
                        // if (contentLength.HasValue)
                        // {
                        //     var progress = (double)totalBytesRead / contentLength.Value * 100;
                        //     Console.WriteLine($"{filename}: {progress:F1}% complete");
                        // }
                    }
                    
                    await fileStream.FlushAsync();
                    Interlocked.Increment(ref completedDownloads);
                    Console.WriteLine($"Completed download of {filename}");
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failedDownloads);
                    Console.WriteLine($"Error downloading video_{index}: {ex.Message}");
                    // Clean up partial file
                    string failedFile = $"video_{index}.mp4";
                    if (File.Exists(failedFile))
                    {
                        try
                        {
                            File.Delete(failedFile);
                            Console.WriteLine($"Deleted incomplete file: {failedFile}");
                        }
                        catch (Exception deleteEx)
                        {
                            Console.WriteLine($"Failed to delete incomplete file: {deleteEx.Message}");
                        }
                    }
                }
                finally
                {
                    semaphore.Release(); // Release the slot
                }
            });
            
            downloadTasks.Add(task);
        }
        
        Console.WriteLine("Waiting for all downloads to complete...");
        await Task.WhenAll(downloadTasks);
        
        Console.WriteLine($"\nAll tasks completed!");
        Console.WriteLine($"Successfully downloaded: {completedDownloads}/{videoUrls.Count} videos");
        Console.WriteLine($"Failed downloads: {failedDownloads}");
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SpecFlowTestGenerator.Core;
using SpecFlowTestGenerator.CodeGeneration;
using SpecFlowTestGenerator.Utils;
using SpecFlowTestGenerator.Browser;

namespace SpecFlowTestGenerator
{
    /// <summary>
    /// Main entry point for the SpecFlowTestGenerator application
    /// </summary>
    class Program
    {
        private static RecorderEngine? _recorder;
        private static readonly CancellationTokenSource _cts = new();
        private static IServiceProvider? _serviceProvider;

        static async Task<int> Main(string[] args)
        {
            // Set up graceful shutdown
            Console.CancelKeyPress += OnCancelKeyPress;

            try
            {
                PrintHeader();
                
                // Set up dependency injection
                _serviceProvider = ConfigureServices();
                
                string featureName = GetFeatureNameFromArgs(args);
                
                // Get recorder from DI container
                _recorder = _serviceProvider.GetRequiredService<RecorderEngine>();
                _recorder.SetInitialFeatureName(featureName);
                
                if (!await InitializeRecorderAsync())
                {
                    ConsoleHelper.WriteError("Failed to initialize recorder. Exiting...");
                    return 1;
                }

                // Start recording session
                await RunRecordingSessionAsync(_cts.Token);
                
                // Generate final output
                GenerateFinalOutput();
                
                ConsoleHelper.WriteSuccess("Recording session completed successfully!");
                return 0;
            }
            catch (OperationCanceledException)
            {
                ConsoleHelper.WriteInfo("\nRecording cancelled by user.");
                return 130; // Standard exit code for SIGINT
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Fatal error: {ex.Message}");
                Logger.Log($"Stack trace: {ex}");
                return 1;
            }
            finally
            {
                await CleanupAsync();
                PrintFooter();
                
                // Dispose service provider
                if (_serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        /// <summary>
        /// Configures dependency injection services
        /// </summary>
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Register core services
            services.AddSingleton<RecorderState>(sp => new RecorderState("DefaultFeature"));
            services.AddSingleton<EventHandlers>(sp => 
                new EventHandlers(sp.GetRequiredService<RecorderState>()));
            
            // Register components with circular dependencies
            services.AddSingleton<JavaScriptInjector>();
            services.AddSingleton<DevToolsSessionManager>();
            
            // Register generators
            services.AddSingleton<FeatureFileBuilder>();
            services.AddSingleton<StepsFileBuilder>();
            services.AddSingleton<SpecFlowGenerator>();
            services.AddSingleton<ActionDeduplicator>();
            
            // Register recorder engine
            services.AddSingleton<RecorderEngine>(sp =>
            {
                var state = sp.GetRequiredService<RecorderState>();
                var eventHandlers = sp.GetRequiredService<EventHandlers>();
                var jsInjector = sp.GetRequiredService<JavaScriptInjector>();
                var sessionManager = sp.GetRequiredService<DevToolsSessionManager>();
                var generator = sp.GetRequiredService<SpecFlowGenerator>();
                var deduplicator = sp.GetRequiredService<ActionDeduplicator>();
                
                return new RecorderEngine(
                    state,
                    eventHandlers,
                    jsInjector,
                    sessionManager,
                    generator,
                    deduplicator);
            });

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Prints application header
        /// </summary>
        private static void PrintHeader()
        {
            Console.Clear();
            ConsoleHelper.WriteHeader("SpecFlowTestGenerator");
            Console.WriteLine("Automated SpecFlow test generation from browser interactions");
            Console.WriteLine("Version 1.0.0");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine();
        }

        /// <summary>
        /// Gets feature name from command line arguments
        /// </summary>
        private static string GetFeatureNameFromArgs(string[] args)
        {
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                string featureName = FileHelper.SanitizeForFileName(args[0]);
                ConsoleHelper.WriteInfo($"Feature name: {featureName}");
                return featureName;
            }

            string defaultName = "MyFeature";
            ConsoleHelper.WriteInfo($"No feature name provided. Using default: {defaultName}");
            return defaultName;
        }

        /// <summary>
        /// Initializes the recorder with progress indication
        /// </summary>
        private static async Task<bool> InitializeRecorderAsync()
        {
            if (_recorder == null)
                return false;

            Console.WriteLine();
            ConsoleHelper.WriteInfo("Initializing recorder...");
            
            using var progress = new ProgressIndicator("Starting Chrome browser");
            
            try
            {
                progress.UpdateStatus("Launching Chrome...");
                
                bool initialized = await _recorder.Initialize();
                
                if (initialized)
                {
                    progress.Complete("Recorder initialized successfully");
                    await Task.Delay(500); // Let user see the success message
                    return true;
                }
                else
                {
                    progress.Fail("Initialization failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                progress.Fail($"Initialization error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Runs the main recording session
        /// </summary>
        private static async Task RunRecordingSessionAsync(CancellationToken cancellationToken)
        {
            if (_recorder == null)
                throw new InvalidOperationException("Recorder not initialized");

            _recorder.StartRecording();
            
            Console.WriteLine();
            PrintRecordingInstructions();
            Console.WriteLine();

            // Create a channel for command processing
            var commandChannel = System.Threading.Channels.Channel.CreateUnbounded<string>();
            
            // Start command reading task
            var readTask = Task.Run(async () => 
            {
                await ReadCommandsAsync(commandChannel.Writer, cancellationToken);
            }, cancellationToken);
            
            // Start command processing task
            var processTask = Task.Run(async () => 
            {
                await ProcessCommandsAsync(commandChannel.Reader, cancellationToken);
            }, cancellationToken);
            
            // Start status update task
            var statusTask = Task.Run(async () => 
            {
                await UpdateStatusAsync(cancellationToken);
            }, cancellationToken);

            try
            {
                // Wait for command processing to complete (when user types 'stop')
                await processTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during cancellation
            }
            finally
            {
                // Ensure recording is stopped
                if (_recorder.IsRecording)
                {
                    _recorder.StopRecording();
                }
                
                // Close the channel
                commandChannel.Writer.Complete();
            }
        }

        /// <summary>
        /// Reads commands from console input asynchronously
        /// </summary>
        private static async Task ReadCommandsAsync(
            System.Threading.Channels.ChannelWriter<string> writer, 
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Use Task.Run to avoid blocking
                    var input = await Task.Run(() =>
                    {
                        // Poll for input without blocking
                        while (!cancellationToken.IsCancellationRequested && !Console.KeyAvailable)
                        {
                            Thread.Sleep(50);
                        }
                        
                        if (cancellationToken.IsCancellationRequested)
                            return null;
                            
                        return Console.ReadLine();
                    }, cancellationToken);
                    
                    if (input != null)
                    {
                        await writer.WriteAsync(input, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            finally
            {
                writer.Complete();
            }
        }

        /// <summary>
        /// Processes commands from the channel
        /// </summary>
        private static async Task ProcessCommandsAsync(
            System.Threading.Channels.ChannelReader<string> reader, 
            CancellationToken cancellationToken)
        {
            if (_recorder == null)
                return;

            try
            {
                await foreach (var input in reader.ReadAllAsync(cancellationToken))
                {
                    if (string.IsNullOrWhiteSpace(input))
                        continue;

                    string command = input.Trim().ToLowerInvariant();
                    
                    // Handle commands
                    if (command == "stop")
                    {
                        ConsoleHelper.WriteInfo("Stopping recording...");
                        _recorder.StopRecording();
                        break; // Exit the loop
                    }
                    else if (command.StartsWith("new feature "))
                    {
                        HandleNewFeatureCommand(input);
                    }
                    else if (command == "help" || command == "?")
                    {
                        PrintRecordingInstructions();
                    }
                    else if (command == "status")
                    {
                        PrintCurrentStatus();
                    }
                    else if (command == "clear")
                    {
                        Console.Clear();
                        PrintRecordingInstructions();
                    }
                    else if (command == "pause")
                    {
                        _recorder.PauseRecording();
                        ConsoleHelper.WriteInfo("Recording paused. Type 'resume' to continue.");
                    }
                    else if (command == "resume")
                    {
                        _recorder.ResumeRecording();
                        ConsoleHelper.WriteSuccess("Recording resumed.");
                    }
                    else if (command == "undo")
                    {
                        _recorder.ProcessCommand("undo");
                    }
                    else if (command.StartsWith("navigate "))
                    {
                        _recorder.ProcessCommand(input);
                    }
                    else
                    {
                        // Pass through to recorder for any custom commands
                        _recorder.ProcessCommand(input);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        /// <summary>
        /// Handles the 'new feature' command
        /// </summary>
        private static void HandleNewFeatureCommand(string input)
        {
            if (_recorder == null)
                return;

            string newFeatureName = input.Substring("new feature ".Length).Trim();
            
            if (string.IsNullOrWhiteSpace(newFeatureName))
            {
                ConsoleHelper.WriteError("Feature name cannot be empty. Usage: new feature <FeatureName>");
                return;
            }

            string sanitizedName = FileHelper.SanitizeForFileName(newFeatureName);
            ConsoleHelper.WriteInfo($"Starting new feature: {sanitizedName}");
            
            _recorder.ProcessCommand(input);
            
            Console.WriteLine();
            ConsoleHelper.WriteSuccess($"Now recording: {sanitizedName}");
            ConsoleHelper.WriteInfo("Navigate to the starting page for this feature.");
        }

        /// <summary>
        /// Periodically updates status information
        /// </summary>
        private static async Task UpdateStatusAsync(CancellationToken cancellationToken)
        {
            if (_recorder == null)
                return;

            int lastActionCount = 0;
            
            while (_recorder.IsRecording && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, cancellationToken); // Update every 5 seconds
                    
                    // Only show status if actions have changed
                    int currentActionCount = _recorder.GetActionCount();
                    if (currentActionCount != lastActionCount)
                    {
                        lastActionCount = currentActionCount;
                        // Subtle status update (optional, can be commented out if too noisy)
                        // ConsoleHelper.WriteInfo($"[{DateTime.Now:HH:mm:ss}] Actions recorded: {currentActionCount}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Prints current recording status
        /// </summary>
        private static void PrintCurrentStatus()
        {
            if (_recorder == null)
                return;

            var summary = _recorder.GetSessionSummary();
            
            Console.WriteLine();
            Console.WriteLine(new string('-', 50));
            ConsoleHelper.WriteInfo("Current Recording Status:");
            Console.WriteLine($"  Feature: {summary.FeatureName}");
            Console.WriteLine($"  Recording: {(summary.IsRecording ? "Active" : "Stopped")}");
            Console.WriteLine($"  Total Actions: {summary.TotalActions}");
            Console.WriteLine($"    - Navigate: {summary.NavigateActions}");
            Console.WriteLine($"    - Click: {summary.ClickActions}");
            Console.WriteLine($"    - Input: {summary.InputActions}");
            Console.WriteLine($"    - Select: {summary.SelectActions}");
            
            if (summary.Duration.HasValue)
            {
                Console.WriteLine($"  Duration: {summary.Duration.Value:mm\\:ss}");
            }
            
            Console.WriteLine(new string('-', 50));
            Console.WriteLine();
        }

        /// <summary>
        /// Prints recording instructions
        /// </summary>
        private static void PrintRecordingInstructions()
        {
            Console.WriteLine(new string('-', 70));
            ConsoleHelper.WriteHeader("Recording Active - Available Commands:");
            Console.WriteLine("  stop              - Stop recording and generate files");
            Console.WriteLine("  new feature <n>   - Start recording a new feature");
            Console.WriteLine("  pause             - Pause recording temporarily");
            Console.WriteLine("  resume            - Resume recording after pause");
            Console.WriteLine("  undo              - Remove last recorded action");
            Console.WriteLine("  navigate <url>    - Manually navigate to URL");
            Console.WriteLine("  status            - Show current recording status");
            Console.WriteLine("  help or ?         - Show this help message");
            Console.WriteLine("  clear             - Clear console and show instructions");
            Console.WriteLine();
            Console.WriteLine("Interact with the browser to record actions.");
            Console.WriteLine("Actions are automatically captured as you click, type, and navigate.");
            Console.WriteLine(new string('-', 70));
        }

        /// <summary>
        /// Generates final output files
        /// </summary>
        private static void GenerateFinalOutput()
        {
            if (_recorder == null)
                return;

            Console.WriteLine();
            ConsoleHelper.WriteInfo("Generating SpecFlow files...");
            
            try
            {
                _recorder.GenerateCurrentFeatureFiles();
                ConsoleHelper.WriteSuccess("Files generated successfully!");
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Error generating files: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up resources
        /// </summary>
        private static async Task CleanupAsync()
        {
            if (_recorder == null)
                return;

            Console.WriteLine();
            ConsoleHelper.WriteInfo("Cleaning up resources...");

            try
            {
                await _recorder.CleanUp();
                
                // Print event handler logs
                var logs = Logger.GetLogBuffer();
                if (logs.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine(new string('=', 70));
                    ConsoleHelper.WriteHeader("Event Handler Logs:");
                    Console.WriteLine(new string('=', 70));
                    foreach (var log in logs)
                    {
                        Console.WriteLine(log);
                    }
                    Console.WriteLine(new string('=', 70));
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Cleanup error: {ex.Message}");
            }
        }

        /// <summary>
        /// Prints application footer
        /// </summary>
        private static void PrintFooter()
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 70));
            ConsoleHelper.WriteInfo("Thank you for using SpecFlowTestGenerator!");
            Console.WriteLine("Press Enter to exit...");
            
            try
            {
                Console.ReadLine();
            }
            catch
            {
                // Ignore if console is unavailable
            }
        }

        /// <summary>
        /// Handles Ctrl+C gracefully
        /// </summary>
        private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true; // Prevent immediate termination
            ConsoleHelper.WriteInfo("\nReceived cancellation signal. Shutting down gracefully...");
            _cts.Cancel();
        }
    }

    /// <summary>
    /// Helper class for console output formatting
    /// </summary>
    public static class ConsoleHelper
    {
        public static void WriteSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ {message}");
            Console.ResetColor();
        }

        public static void WriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ {message}");
            Console.ResetColor();
        }

        public static void WriteInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"ℹ {message}");
            Console.ResetColor();
        }

        public static void WriteHeader(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Simple progress indicator for long-running operations
    /// </summary>
    public class ProgressIndicator : IDisposable
    {
        private readonly string _operation;
        private readonly int _startLeft;
        private readonly int _startTop;
        private bool _disposed;

        public ProgressIndicator(string operation)
        {
            _operation = operation;
            _startLeft = Console.CursorLeft;
            _startTop = Console.CursorTop;
            
            Console.Write($"{_operation}...");
        }

        public void UpdateStatus(string status)
        {
            if (_disposed) return;
            
            try
            {
                Console.SetCursorPosition(_startLeft, _startTop);
                Console.Write(new string(' ', Console.WindowWidth - _startLeft - 1));
                Console.SetCursorPosition(_startLeft, _startTop);
                Console.Write($"{_operation}: {status}...");
            }
            catch
            {
                // Ignore cursor positioning errors
            }
        }

        public void Complete(string? message = null)
        {
            if (_disposed) return;
            
            try
            {
                Console.SetCursorPosition(_startLeft, _startTop);
                Console.Write(new string(' ', Console.WindowWidth - _startLeft - 1));
                Console.SetCursorPosition(_startLeft, _startTop);
                ConsoleHelper.WriteSuccess(message ?? $"{_operation} complete");
            }
            catch
            {
                ConsoleHelper.WriteSuccess(message ?? $"{_operation} complete");
            }
            
            _disposed = true;
        }

        public void Fail(string? message = null)
        {
            if (_disposed) return;
            
            try
            {
                Console.SetCursorPosition(_startLeft, _startTop);
                Console.Write(new string(' ', Console.WindowWidth - _startLeft - 1));
                Console.SetCursorPosition(_startLeft, _startTop);
                ConsoleHelper.WriteError(message ?? $"{_operation} failed");
            }
            catch
            {
                ConsoleHelper.WriteError(message ?? $"{_operation} failed");
            }
            
            _disposed = true;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Console.WriteLine();
                _disposed = true;
            }
        }
    }
}
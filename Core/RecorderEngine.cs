using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using SpecFlowTestGenerator.Browser;
using SpecFlowTestGenerator.CodeGeneration;
using SpecFlowTestGenerator.Models;
using SpecFlowTestGenerator.Utils;

namespace SpecFlowTestGenerator.Core
{
    /// <summary>
    /// Main engine for the recorder application - orchestrates browser control,
    /// action recording, and SpecFlow file generation
    /// </summary>
    public class RecorderEngine : IDisposable
    {
        #region Fields

        private readonly RecorderState _state;
        private readonly EventHandlers _eventHandlers;
        private readonly JavaScriptInjector _jsInjector;
        private readonly DevToolsSessionManager _sessionManager;
        private readonly SpecFlowGenerator _specFlowGenerator;
        private readonly ActionDeduplicator _deduplicator;

        private IWebDriver? _driver;
        private ChromeDriverService? _chromeService;
        private bool _disposed;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets whether recording is currently active
        /// </summary>
        public bool IsRecording
        {
            get => _state.IsRecording;
            set => _state.IsRecording = value;
        }

        /// <summary>
        /// Gets the current feature name being recorded
        /// </summary>
        public string GetCurrentFeatureName() => _state.CurrentFeatureName;

        /// <summary>
        /// Gets the count of recorded actions for the current feature
        /// </summary>
        public int GetActionCount() => _state.GetActions().Count;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new instance of the RecorderEngine with dependency injection
        /// </summary>
        public RecorderEngine(
            RecorderState state,
            EventHandlers eventHandlers,
            JavaScriptInjector jsInjector,
            DevToolsSessionManager sessionManager,
            SpecFlowGenerator specFlowGenerator,
            ActionDeduplicator deduplicator)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _eventHandlers = eventHandlers ?? throw new ArgumentNullException(nameof(eventHandlers));
            _jsInjector = jsInjector ?? throw new ArgumentNullException(nameof(jsInjector));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _specFlowGenerator = specFlowGenerator ?? throw new ArgumentNullException(nameof(specFlowGenerator));
            _deduplicator = deduplicator ?? throw new ArgumentNullException(nameof(deduplicator));

            // Wire up circular dependencies
            _jsInjector.SetSessionManager(_sessionManager);
            _sessionManager.SetDependencies(_eventHandlers, _jsInjector);

            Logger.Log("RecorderEngine initialized with dependency injection");
        }

        /// <summary>
        /// Sets the initial feature name after construction
        /// </summary>
        public void SetInitialFeatureName(string featureName)
        {
            if (string.IsNullOrWhiteSpace(featureName))
                throw new ArgumentException("Feature name cannot be null or empty", nameof(featureName));

            _state.CurrentFeatureName = featureName;
            Logger.Log($"Initial feature name set to: {featureName}");
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the recorder engine - launches browser and sets up DevTools
        /// </summary>
        /// <returns>True if initialization succeeded, false otherwise</returns>
        public async Task<bool> Initialize()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecorderEngine));

            try
            {
                Logger.Log("Starting RecorderEngine initialization...");

                // Step 1: Launch Chrome browser
                if (!await InitializeBrowser())
                {
                    Logger.Log("Failed to initialize browser");
                    return false;
                }

                // Step 2: Set up DevTools session
                if (!await InitializeDevTools())
                {
                    Logger.Log("Failed to initialize DevTools");
                    await CleanUp();
                    return false;
                }

                // Step 3: Inject JavaScript listeners
                if (!await InitializeJavaScriptListeners())
                {
                    Logger.Log("Failed to initialize JavaScript listeners");
                    await CleanUp();
                    return false;
                }

                Logger.Log("SUCCESS: RecorderEngine fully initialized and ready");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"FAIL: Initialization error: {ex.Message}");
                Logger.Log($"Stack trace: {ex}");
                await CleanUp();
                return false;
            }
        }

        /// <summary>
        /// Initializes the Chrome browser
        /// </summary>
        private async Task<bool> InitializeBrowser()
        {
            try
            {
                var (driver, service) = BrowserFactory.CreateChromeDriver();
                
                if (driver == null)
                {
                    Logger.Log("BrowserFactory returned null driver");
                    return false;
                }

                _driver = driver;
                _chromeService = service;

                // Give browser a moment to stabilize
                await Task.Delay(500);

                Logger.Log("Browser initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Browser initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Initializes the DevTools session for CDP communication
        /// </summary>
        private async Task<bool> InitializeDevTools()
        {
            if (_driver == null)
            {
                Logger.Log("Cannot initialize DevTools: driver is null");
                return false;
            }

            try
            {
                bool success = await _sessionManager.InitializeSession(_driver);
                
                if (success)
                {
                    Logger.Log("DevTools session initialized successfully");
                }
                else
                {
                    Logger.Log("DevTools session initialization returned false");
                }

                return success;
            }
            catch (Exception ex)
            {
                Logger.Log($"DevTools initialization failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Injects JavaScript listeners into the browser for action capture
        /// </summary>
        private async Task<bool> InitializeJavaScriptListeners()
        {
            try
            {
                await _jsInjector.InjectListeners();
                Logger.Log("JavaScript listeners injected successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"JavaScript injection failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Recording Control

        /// <summary>
        /// Starts recording user actions
        /// </summary>
        public void StartRecording()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecorderEngine));

            _state.StartRecording();
            
            Logger.Log($"=== Recording Started ===");
            Logger.Log($"Feature: {_state.CurrentFeatureName}");
            Logger.Log($"Started at: {_state.StartTime:yyyy-MM-dd HH:mm:ss}");
            Logger.Log("Interact with the browser - all actions will be recorded");
        }

        /// <summary>
        /// Stops recording user actions
        /// </summary>
        public void StopRecording()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecorderEngine));

            _state.StopRecording();
            
            Logger.Log($"=== Recording Stopped ===");
            Logger.Log($"Feature: {_state.CurrentFeatureName}");
            Logger.Log($"Duration: {_state.EndTime - _state.StartTime}");
            Logger.Log($"Actions recorded: {_state.GetActions().Count}");
        }

        /// <summary>
        /// Pauses recording temporarily without stopping
        /// </summary>
        public void PauseRecording()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecorderEngine));

            _state.IsRecording = false;
            Logger.Log("Recording paused");
        }

        /// <summary>
        /// Resumes recording after a pause
        /// </summary>
        public void ResumeRecording()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecorderEngine));

            _state.IsRecording = true;
            Logger.Log("Recording resumed");
        }

        #endregion

        #region Command Processing

        /// <summary>
        /// Processes a command from the user interface
        /// </summary>
        /// <param name="command">The command string to process</param>
        public void ProcessCommand(string? command)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecorderEngine));

            if (string.IsNullOrWhiteSpace(command))
                return;

            string cmd = command.Trim();
            string cmdLower = cmd.ToLowerInvariant();

            try
            {
                if (cmdLower == "stop")
                {
                    HandleStopCommand();
                }
                else if (cmdLower.StartsWith("new feature "))
                {
                    HandleNewFeatureCommand(cmd);
                }
                else if (cmdLower == "pause")
                {
                    PauseRecording();
                }
                else if (cmdLower == "resume")
                {
                    ResumeRecording();
                }
                else if (cmdLower == "clear")
                {
                    HandleClearCommand();
                }
                else if (cmdLower == "undo")
                {
                    HandleUndoCommand();
                }
                else if (cmdLower.StartsWith("navigate "))
                {
                    HandleNavigateCommand(cmd);
                }
                else
                {
                    Logger.Log($"Unknown command: {cmd}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error processing command '{cmd}': {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the 'stop' command
        /// </summary>
        private void HandleStopCommand()
        {
            StopRecording();
            GenerateCurrentFeatureFiles();
        }

        /// <summary>
        /// Handles the 'new feature' command
        /// </summary>
        private void HandleNewFeatureCommand(string command)
        {
            string featureNameInput = command.Substring("new feature ".Length).Trim();
            
            if (string.IsNullOrWhiteSpace(featureNameInput))
            {
                Logger.Log("ERROR: Feature name cannot be empty");
                Logger.Log("Usage: new feature <FeatureName>");
                return;
            }

            string newFeatureName = FileHelper.SanitizeForFileName(featureNameInput);
            
            Logger.Log($"--- Switching to new feature: {newFeatureName} ---");
            
            // Generate files for current feature before switching
            if (_state.HasActions())
            {
                GenerateCurrentFeatureFiles();
            }
            
            // Switch to new feature
            SwitchFeature(newFeatureName);
            
            Logger.Log($"--- Now recording: {_state.CurrentFeatureName} ---");
        }

        /// <summary>
        /// Handles the 'clear' command - clears current feature actions
        /// </summary>
        private void HandleClearCommand()
        {
            int actionCount = _state.GetActions().Count;
            _state.Clear();
            Logger.Log($"Cleared {actionCount} actions from current feature");
        }

        /// <summary>
        /// Handles the 'undo' command - removes last recorded action
        /// </summary>
        private void HandleUndoCommand()
        {
            var actions = _state.GetActions();
            if (actions.Count == 0)
            {
                Logger.Log("No actions to undo");
                return;
            }

            var lastAction = actions[actions.Count - 1];
            actions.RemoveAt(actions.Count - 1);
            
            Logger.Log($"Undid action: {lastAction.ActionType} on {lastAction.SelectorType}='{lastAction.SelectorValue}'");
        }

        /// <summary>
        /// Handles the 'navigate' command - manually navigate to URL
        /// </summary>
        private void HandleNavigateCommand(string command)
        {
            if (_driver == null)
            {
                Logger.Log("Cannot navigate: browser not initialized");
                return;
            }

            string url = command.Substring("navigate ".Length).Trim();
            
            if (string.IsNullOrWhiteSpace(url))
            {
                Logger.Log("ERROR: URL cannot be empty");
                Logger.Log("Usage: navigate <URL>");
                return;
            }

            // Add protocol if missing
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            try
            {
                Logger.Log($"Manually navigating to: {url}");
                _driver.Navigate().GoToUrl(url);
            }
            catch (Exception ex)
            {
                Logger.Log($"Navigation failed: {ex.Message}");
            }
        }

        #endregion

        #region Feature Management

        /// <summary>
        /// Switches to a new feature, resetting the action list
        /// </summary>
        /// <param name="featureName">Name of the new feature</param>
        private void SwitchFeature(string featureName)
        {
            _state.CurrentFeatureName = featureName;
            _state.Reset();
            
            Logger.Log($"Switched to feature: {featureName}");
        }

        /// <summary>
        /// Generates SpecFlow files for the current feature
        /// </summary>
        public void GenerateCurrentFeatureFiles()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RecorderEngine));

            if (!_state.HasActions())
            {
                Logger.Log($"INFO: No actions recorded for '{_state.CurrentFeatureName}' - skipping file generation");
                return;
            }

            try
            {
                List<RecordedAction> actions = _state.GetActions();
                
                Logger.Log($"--- Generating SpecFlow files ---");
                Logger.Log($"Feature: {_state.CurrentFeatureName}");
                Logger.Log($"Actions: {actions.Count}");

                // Apply deduplication before generating
                var deduplicated = _deduplicator.DeduplicateActions(actions);
                int removed = actions.Count - deduplicated.Count;
                
                if (removed > 0)
                {
                    Logger.Log($"Removed {removed} duplicate/redundant actions");
                }

                // Generate files
                string outputDir = Environment.CurrentDirectory;
                _specFlowGenerator.GenerateFiles(deduplicated, _state.CurrentFeatureName, outputDir);
                
                Logger.Log($"SUCCESS: Files generated in {outputDir}");
            }
            catch (Exception ex)
            {
                Logger.Log($"ERROR: Failed to generate files: {ex.Message}");
                Logger.Log($"Stack trace: {ex}");
            }
        }

        /// <summary>
        /// Gets a summary of the current recording session
        /// </summary>
        public RecordingSessionSummary GetSessionSummary()
        {
            var actions = _state.GetActions();
            
            return new RecordingSessionSummary
            {
                FeatureName = _state.CurrentFeatureName,
                IsRecording = _state.IsRecording,
                StartTime = _state.StartTime,
                EndTime = _state.EndTime,
                TotalActions = actions.Count,
                NavigateActions = actions.Count(a => a.ActionType == "Navigate"),
                ClickActions = actions.Count(a => a.ActionType == "Click"),
                InputActions = actions.Count(a => a.ActionType == "SendKeys" || a.ActionType == "SendKeysEnter"),
                SelectActions = actions.Count(a => a.ActionType == "SelectOption")
            };
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Cleans up all resources (browser, DevTools, etc.)
        /// </summary>
        public async Task CleanUp()
        {
            if (_disposed)
                return;

            Logger.Log("Starting cleanup...");

            try
            {
                // Clean up DevTools session
                await _sessionManager.CleanUpSession();

                // Quit and dispose driver
                if (_driver != null)
                {
                    try
                    {
                        _driver.Quit();
                        _driver.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error during driver cleanup: {ex.Message}");
                    }
                    finally
                    {
                        _driver = null;
                    }
                }

                // Dispose Chrome service
                if (_chromeService != null)
                {
                    try
                    {
                        _chromeService.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error during service cleanup: {ex.Message}");
                    }
                    finally
                    {
                        _chromeService = null;
                    }
                }

                Logger.Log("Cleanup completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Disposes of the RecorderEngine and all its resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes resources
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Cleanup is async, but Dispose must be sync
                // Best effort cleanup
                CleanUp().GetAwaiter().GetResult();
            }

            _disposed = true;
        }

        #endregion
    }

    /// <summary>
    /// Represents a summary of the current recording session
    /// </summary>
    public class RecordingSessionSummary
    {
        public string FeatureName { get; init; } = string.Empty;
        public bool IsRecording { get; init; }
        public DateTime? StartTime { get; init; }
        public DateTime? EndTime { get; init; }
        public int TotalActions { get; init; }
        public int NavigateActions { get; init; }
        public int ClickActions { get; init; }
        public int InputActions { get; init; }
        public int SelectActions { get; init; }

        public TimeSpan? Duration => EndTime.HasValue && StartTime.HasValue 
            ? EndTime.Value - StartTime.Value 
            : null;
    }

    /// <summary>
    /// Deduplicates and cleans up recorded actions
    /// </summary>
    public class ActionDeduplicator
    {
        /// <summary>
        /// Removes duplicate and redundant actions
        /// </summary>
        public List<RecordedAction> DeduplicateActions(List<RecordedAction> actions)
        {
            if (actions == null || actions.Count == 0)
                return new List<RecordedAction>();

            var result = new List<RecordedAction>();
            RecordedAction? lastAction = null;

            foreach (var action in actions)
            {
                if (ShouldIncludeAction(action, lastAction))
                {
                    result.Add(action);
                    lastAction = action;
                }
                else
                {
                    Logger.LogEventHandler($"   -> Deduplicated: {action.ActionType} on {action.SelectorType}='{action.SelectorValue}'");
                }
            }

            return result;
        }

        /// <summary>
        /// Determines if an action should be included in the final output
        /// </summary>
        private bool ShouldIncludeAction(RecordedAction action, RecordedAction? lastAction)
        {
            if (lastAction == null)
                return true;

            // Remove duplicate consecutive navigations to the same URL
            if (action.ActionType == "Navigate" && 
                lastAction.ActionType == "Navigate" &&
                action.Value == lastAction.Value)
            {
                return false;
            }

            // Remove rapid consecutive clicks on the same element (within 500ms)
            if (action.ActionType == "Click" &&
                lastAction.ActionType == "Click" &&
                action.SelectorType == lastAction.SelectorType &&
                action.SelectorValue == lastAction.SelectorValue &&
                (action.Timestamp - lastAction.Timestamp).TotalMilliseconds < 500)
            {
                return false;
            }

            // Remove redundant SendKeys events followed by SendKeysEnter on same element
            // (The feature builder handles this, but we can clean it up here too)
            if (action.ActionType == "SendKeysEnter" &&
                lastAction.ActionType == "SendKeys" &&
                action.SelectorType == lastAction.SelectorType &&
                action.SelectorValue == lastAction.SelectorValue &&
                (action.Timestamp - lastAction.Timestamp).TotalMilliseconds < 1000)
            {
                // The SendKeysEnter will capture the value, so we can skip the previous SendKeys
                // But we need to preserve the SendKeysEnter, so just mark this as includable
                return true;
            }

            return true;
        }
    }
}
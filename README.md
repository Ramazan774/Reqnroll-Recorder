# SpecFlow Recorder Chrome Extension

A Chrome Extension that records user interactions on web pages and generates SpecFlow feature files and C# step definitions for automated testing.

## Project Structure

```
SpecFlowRecorderExtension/
├── manifest.json       # Extension configuration
├── popup.html         # Extension UI
├── popup.js           # UI logic
├── background.js      # File generation & state management
└── content.js         # Event capture & selector generation
```

## Installation

1. Open Chrome and navigate to `chrome://extensions/`
2. Enable **Developer mode** (toggle in top-right)
3. Click **Load unpacked**
4. Select the `SpecFlowRecorderExtension` folder

## Usage

1. Click the extension icon in your browser toolbar
2. Enter a **Feature Name** (e.g., "LoginTest")
3. Click **Start Recording**
4. Interact with your web application (click, type, navigate)
5. Click the extension icon again and click **Stop & Generate**
6. Two files will download:
   - `YourFeatureName.feature` - Gherkin feature file
   - `YourFeatureNameSteps.cs` - C# step definitions

## Generated Files

The extension generates production-ready SpecFlow test files:

- **Feature File**: Contains Gherkin scenarios with recorded actions
- **Steps File**: Contains C# step definitions with Selenium WebDriver code

## Features

- ✅ Smart selector generation (data attributes, IDs, XPath, CSS)
- ✅ Automatic step deduplication
- ✅ Support for clicks, typing, and navigation
- ✅ Handles hidden checkboxes and radio buttons
- ✅ Hover-aware element interaction
- ✅ Contextual text-based selectors for list items

## Test Project

A separate SpecFlow test project is available at:
`/Users/ram/Downloads/SpecFlowTestsProject/`

This project can run the generated feature files using Selenium WebDriver and NUnit.

## Development

To update the extension after making changes:
1. Go to `chrome://extensions/`
2. Click the **Refresh** icon on the extension card
3. Reload any web pages you want to record

## Notes

- The extension works on most websites except Chrome internal pages (chrome://, chrome-extension://)
- Delete buttons and hover-only elements may require manual adjustment
- Generated files use SpecFlow 3.9+ and Selenium WebDriver 4.x

# Reqnroll Recorder

A powerful Chrome Extension that records your browser interactions and automatically generates production-ready **Reqnroll (Gherkin)** feature files and **Selenium C#** step definitions.

Features

- **Record & Playback**: Captures clicks, typing, navigation, and special keys (Enter).
- **Smart Selectors**: Automatically finds the best, most robust selector.
- **Auto-Generation**: Instantly creates `.feature` and `.cs` files ready for your test project.
- **Robust Code**: Generates C# code with built-in `WebDriverWait` and hover handling for stability.
- **Session Management**: Persists recording state even if you close the popup or reload the page.

## Installation

1.  Clone this repository.
2.  Open Chrome and navigate to `chrome://extensions/`.
3.  Enable Developer mode (top right).
4.  Click Load unpacked.
5.  Select the `ReqnrollRecorderExtension` folder from this project.

## Usage Guide

1.  Click the extension icon, enter a Feature Name (e.g., `Login`), and hit 'Start Recording'.
2.  Browse your website as a user would. The extension captures your actions in the background.
3.  Click the extension icon again and hit 'Stop & Generate'.
4.  Two files will automatically download:
       `Login.feature`: The Gherkin scenarios.
       `LoginSteps.cs`: The C# automation code.
5.  Drop these files into your Reqnroll project and run your tests!

## Running the Generated Tests

A compatible Reqnroll test project template is available (if you moved it) or can be created with:

```bash
dotnet new reqnroll -n ReqnrollRecorderTests
dotnet add package Selenium.WebDriver
```

## Tech Stack

- Frontend: HTML5, CSS3, Vanilla JavaScript
- Extension API: Chrome Extensions Manifest V3
- Automation: Selenium WebDriver, Reqnroll (Gherkin)


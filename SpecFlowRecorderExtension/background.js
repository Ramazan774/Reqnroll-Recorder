let isRecording = false;
let currentFeatureName = 'MyFeature';
let recordedActions = [];

// Initialize storage
chrome.storage.local.set({ isRecording: false, actionCount: 0 });

console.log('SpecFlow Recorder Background Service v1.1 Loaded');

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.command === 'startRecording') {
        isRecording = true;
        currentFeatureName = request.featureName;
        recordedActions = [];

        chrome.storage.local.set({
            isRecording: true,
            featureName: currentFeatureName,
            actionCount: 0
        });

        // Inject content script into active tab
        chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
            if (tabs[0]) {
                chrome.tabs.sendMessage(tabs[0].id, { command: 'start' });
            }
        });

        sendResponse({ status: 'started' });
    }
    else if (request.command === 'stopRecording') {
        isRecording = false;
        chrome.storage.local.set({ isRecording: false });

        // Tell content script to stop
        chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
            if (tabs[0]) {
                try {
                    chrome.tabs.sendMessage(tabs[0].id, { command: 'stop' }).catch(err => {
                        console.log('Could not send stop message to tab (likely restricted or closed):', err);
                    });
                } catch (e) {
                    console.log('Error sending stop message:', e);
                }
            }
        });

        // Retrieve actions from storage before generating
        chrome.storage.local.get(['recordedActions', 'featureName'], (result) => {
            const actions = result.recordedActions || [];
            const featureName = result.featureName || 'MyFeature';
            generateFiles(actions, featureName);
        });

        sendResponse({ status: 'stopped' });
    }
    else if (request.command === 'recordAction') {
        // We need to get the current list first to append
        chrome.storage.local.get(['recordedActions', 'isRecording'], (result) => {
            if (result.isRecording) {
                const actions = result.recordedActions || [];
                actions.push(request.action);

                chrome.storage.local.set({
                    recordedActions: actions,
                    actionCount: actions.length
                });

                // Notify popup if open
                chrome.runtime.sendMessage({
                    type: 'actionRecorded',
                    count: actions.length
                }).catch(() => { });
            }
        });
    }

    return true;
});

function generateFiles(actions, featureName) {
    console.log('Generating files for', featureName, 'with', actions.length, 'actions');

    const featureContent = generateFeatureFile(actions, featureName);
    const stepsContent = generateStepsFile(actions, featureName);

    // Use Data URLs (Base64) which are more reliable in Service Workers than Blob URLs
    const featureDataUrl = 'data:text/plain;base64,' + btoa(unescape(encodeURIComponent(featureContent)));
    const stepsDataUrl = 'data:text/plain;base64,' + btoa(unescape(encodeURIComponent(stepsContent)));

    chrome.downloads.download({
        url: featureDataUrl,
        filename: `${featureName}.feature`,
        saveAs: true
    }, (downloadId) => {
        if (chrome.runtime.lastError) {
            console.error('Feature download failed:', chrome.runtime.lastError);
        } else {
            console.log('Feature download started, ID:', downloadId);

            // Download Steps File
            chrome.downloads.download({
                url: stepsDataUrl,
                filename: `${featureName}Steps.cs`,
                saveAs: true
            }, (stepDownloadId) => {
                if (chrome.runtime.lastError) {
                    console.error('Steps download failed:', chrome.runtime.lastError);
                } else {
                    console.log('Steps download started, ID:', stepDownloadId);
                }
            });
        }
    });
}

function generateFeatureFile(actions, featureName) {
    let content = `Feature: ${featureName}\n\n`;
    content += `  Scenario: Recorded Scenario\n`;

    // Simple deduplication logic could go here

    actions.forEach(action => {
        switch (action.type) {
            case 'navigate':
                content += `    Given I navigate to "${action.value}"\n`;
                break;
            case 'click':
                content += `    When I click the element with ${action.selector} "${action.selectorValue}"\n`;
                break;
            case 'type':
                content += `    When I type "${action.value}" into element with ${action.selector} "${action.selectorValue}"\n`;
                break;
            case 'enterkey':
                content += `    When I type "${action.value}" and press Enter in element with ${action.selector} "${action.selectorValue}"\n`;
                break;
        }
    });

    content += `    Then the page should be in the expected state\n`;
    return content;
}

function generateStepsFile(actions, featureName) {
    const className = `${featureName}Steps`;
    let content = `using System;
using TechTalk.SpecFlow;
using OpenQA.Selenium;
using System.Threading;

namespace SpecFlowTests.Steps
{
    [Binding]
    public class ${className}
    {
        private readonly IWebDriver _driver;

        public ${className}(IWebDriver driver)
        {
            _driver = driver;
        }

        [Then(@"the page should be in the expected state")]
        public void ThenThePageShouldBeInTheExpectedState()
        {
            Thread.Sleep(2000);
        }
`;

    // Generate step definitions
    const signatures = new Set();

    actions.forEach(action => {
        if (action.type === 'navigate') {
            if (!signatures.has('NavigateToUrl')) {
                content += `
        [Given(@"I navigate to ""(.*)""")]
        [When(@"I navigate to ""(.*)""")]
        public void NavigateToUrl(string url)
        {
            _driver.Navigate().GoToUrl(url);
            Thread.Sleep(1000);
        }
`;
                signatures.add('NavigateToUrl');
            }
        }
        else if (action.type === 'click') {
            if (!signatures.has('ClickElementWith')) {
                content += `
        [When(@"I click the element with (.*?) ""(.*?)""")]
        public void ClickElementWith(string selectorType, string selectorValue)
        {
            var wait = new OpenQA.Selenium.Support.UI.WebDriverWait(_driver, TimeSpan.FromSeconds(10));
            var element = wait.Until(d => {
                var el = GetElement(selectorType, selectorValue);
                // Special handling for checkboxes/radios that might be hidden by custom UI
                if (el != null && !el.Displayed && el.TagName.ToLower() == "input" && 
                   (el.GetAttribute("type") == "checkbox" || el.GetAttribute("type") == "radio"))
                {
                    return el;
                }
                return (el != null && el.Displayed && el.Enabled) ? el : null;
            });

            // Move to element to ensure it's interactable (handles hover-only buttons)
            var actions = new OpenQA.Selenium.Interactions.Actions(_driver);
            actions.MoveToElement(element).Perform();
            Thread.Sleep(500);

            element.Click();
            Thread.Sleep(500);
        }
`;
                signatures.add('ClickElementWith');
            }
        }
        else if (action.type === 'type') {
            if (!signatures.has('TypeIntoElement')) {
                content += `
        [When(@"I type ""(.*)"" into element with (.*?) ""(.*?)""")]
        public void TypeIntoElement(string text, string selectorType, string selectorValue)
        {
            var element = GetElement(selectorType, selectorValue);
            element.Clear();
            element.SendKeys(text);
            Thread.Sleep(300);
        }
`;
                signatures.add('TypeIntoElement');
            }
        }
        else if (action.type === 'enterkey') {
            if (!signatures.has('TypeAndEnter')) {
                content += `
        [When(@"I type ""(.*)"" and press Enter in element with (.*?) ""(.*?)""")]
        public void TypeAndEnter(string text, string selectorType, string selectorValue)
        {
            var element = GetElement(selectorType, selectorValue);
            element.Clear();
            element.SendKeys(text);
            element.SendKeys(Keys.Enter);
            Thread.Sleep(1000);
        }
`;
                signatures.add('TypeAndEnter');
            }
        }
    });

    // Add Helper Method
    content += `
        private IWebElement GetElement(string selectorType, string selectorValue)
        {
            By by;
            switch (selectorType.ToLower())
            {
                case "id": by = By.Id(selectorValue); break;
                case "cssselector": by = By.CssSelector(selectorValue); break;
                case "xpath": by = By.XPath(selectorValue); break;
                case "name": by = By.Name(selectorValue); break;
                default: by = By.CssSelector(selectorValue); break;
            }
            return _driver.FindElement(by);
        }
    }
}`;

    return content;
}

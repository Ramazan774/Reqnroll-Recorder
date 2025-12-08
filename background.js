if (typeof browser !== 'undefined') globalThis.chrome = browser;

let isRecording = false;
let currentFeatureName = 'MyFeature';
let recordedActions = [];

chrome.storage.local.set({ isRecording: false, actionCount: 0 });

console.log('Reqnroll Recorder Background Service Loaded');

chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.command === 'startRecording') {
        isRecording = true;
        currentFeatureName = request.featureName;
        recordedActions = [];

        chrome.storage.local.set({
            isRecording: true,
            featureName: currentFeatureName,
            recordedActions: [],
            actionCount: 0
        });

        chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
            if (tabs[0]) {
                chrome.tabs.sendMessage(tabs[0].id, { command: 'start' }).catch(err => {
                    console.log('Could not send start message to tab:', err);
                });
            }
        });

        sendResponse({ status: 'started' });
    }
    else if (request.command === 'stopRecording') {
        isRecording = false;
        chrome.storage.local.set({ isRecording: false });

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

        chrome.storage.local.get(['recordedActions', 'featureName'], (result) => {
            const actions = result.recordedActions || [];
            const featureName = result.featureName || 'MyFeature';
            generateFiles(actions, featureName);
        });

        sendResponse({ status: 'stopped' });
    }
    else if (request.command === 'recordAction') {
        chrome.storage.local.get(['recordedActions', 'isRecording'], (result) => {
            if (result.isRecording) {
                const actions = result.recordedActions || [];
                actions.push(request.action);

                chrome.storage.local.set({
                    recordedActions: actions,
                    actionCount: actions.length
                });

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

    const isFirefox = typeof browser !== 'undefined';

    let featureUrl, stepsUrl;

    if (isFirefox) {
        console.log('Using blob URLs for Firefox');
        const featureBlob = new Blob([featureContent], { type: 'text/plain' });
        const stepsBlob = new Blob([stepsContent], { type: 'text/plain' });
        featureUrl = URL.createObjectURL(featureBlob);
        stepsUrl = URL.createObjectURL(stepsBlob);
    } else {
        console.log('Using data URLs for Chrome');
        featureUrl = 'data:application/octet-stream;base64,' + utf8_to_b64(featureContent);
        stepsUrl = 'data:application/octet-stream;base64,' + utf8_to_b64(stepsContent);
    }

    console.log('Download URLs created');

    chrome.downloads.download({
        url: featureUrl,
        filename: `${featureName}.feature`,
        saveAs: true
    }, (downloadId) => {
        if (chrome.runtime.lastError) {
            console.error('Feature download failed:', chrome.runtime.lastError);
            alert('Feature file download failed: ' + chrome.runtime.lastError.message);
        } else {
            console.log('Feature download started, ID:', downloadId);
            if (isFirefox) {
                setTimeout(() => URL.revokeObjectURL(featureUrl), 10000);
            }

            chrome.downloads.download({
                url: stepsUrl,
                filename: `${featureName}Steps.cs`,
                saveAs: true
            }, (stepDownloadId) => {
                if (chrome.runtime.lastError) {
                    console.error('Steps download failed:', chrome.runtime.lastError);
                    alert('Steps file download failed: ' + chrome.runtime.lastError.message);
                } else {
                    console.log('Steps download started, ID:', stepDownloadId);

                    if (isFirefox) {
                        setTimeout(() => URL.revokeObjectURL(stepsUrl), 10000);
                    }
                }
            });
        }
    });
}

function generateFeatureFile(actions, featureName) {
    let content = `Feature: ${featureName}\n\n`;
    content += `  Scenario: Recorded Scenario\n`;

    let previousKeyword = null;

    actions.forEach(action => {
        let currentKeyword = null;
        let stepText = '';

        switch (action.type) {
            case 'navigate':
                currentKeyword = 'Given';
                stepText = `I navigate to "${action.value}"`;
                break;
            case 'hover':
                currentKeyword = 'When';
                stepText = `I hover over the element with ${action.selector} "${action.selectorValue}"`;
                break;
            case 'click':
                currentKeyword = 'When';
                stepText = `I click the element with ${action.selector} "${action.selectorValue}"`;
                break;
            case 'type':
                currentKeyword = 'Then';
                stepText = `I type "${action.value}" into element with ${action.selector} "${action.selectorValue}"`;
                break;
            case 'enterkey':
                currentKeyword = 'Then';
                stepText = `I type "${action.value}" and press Enter in element with ${action.selector} "${action.selectorValue}"`;
                break;
        }

        const keyword = (currentKeyword === previousKeyword) ? 'And' : currentKeyword;
        content += `    ${keyword} ${stepText}\n`;

        previousKeyword = currentKeyword;
    });

    content += `    Then the page should be in the expected state\n`;
    return content;
}

function generateStepsFile(actions, featureName) {
    const className = `${featureName}Steps`;
    let content = `using System;
using Reqnroll;
using OpenQA.Selenium;
using System.Threading;

namespace ReqnrollTests.Steps
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
        else if (action.type === 'hover') {
            if (!signatures.has('HoverOverElement')) {
                content += `
        [When(@"I hover over the element with (.*?) ""(.*?)""")]
        public void HoverOverElement(string selectorType, string selectorValue)
        {
            var wait = new OpenQA.Selenium.Support.UI.WebDriverWait(_driver, TimeSpan.FromSeconds(10));
            var element = wait.Until(d => {
                var el = GetElement(selectorType, selectorValue);
                return (el != null && el.Displayed) ? el : null;
            });

            var actions = new OpenQA.Selenium.Interactions.Actions(_driver);
            actions.MoveToElement(element).Perform();
            Thread.Sleep(500); 
        }
`;
                signatures.add('HoverOverElement');
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
        [Then(@"I type ""(.*)"" into element with (.*?) ""(.*?)""")]
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
        [Then(@"I type ""(.*)"" and press Enter in element with (.*?) ""(.*?)""")]
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

function utf8_to_b64(str) {
    return btoa(encodeURIComponent(str).replace(/%([0-9A-F]{2})/g,
        function toSolidBytes(match, p1) {
            return String.fromCharCode('0x' + p1);
        }));
}

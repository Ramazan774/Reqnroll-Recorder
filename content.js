if (typeof browser !== 'undefined') globalThis.chrome = browser;
(function () {
    let isRecording = false;

    chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
        if (request.command === 'start') {
            isRecording = true;
            attachListeners();
        } else if (request.command === 'stop') {
            isRecording = false;
            removeListeners();
        }
    });

    chrome.storage.local.get(['isRecording'], (result) => {
        if (result.isRecording) {
            isRecording = true;
            attachListeners();
        }
    });

    const inputValues = new Map();
    const lastRecordedValues = new Map();

    function getCssPath(el) {
        if (!(el instanceof Element)) return;
        const path = [];
        let depth = 0;
        const maxDepth = 4;

        while (el.nodeType === Node.ELEMENT_NODE && depth < maxDepth) {
            let selector = el.nodeName.toLowerCase();

            if (el.id) {
                selector += '#' + CSS.escape(el.id);
                path.unshift(selector);
                break;
            }

            if (el.className && typeof el.className === 'string') {
                const classes = el.className.split(/\s+/).filter(c => c);
                const meaningfulClass = classes.find(c =>
                    !c.startsWith('atm_') &&
                    !c.startsWith('css-') &&
                    !c.startsWith('style-') &&
                    !c.match(/^[a-z]\d+$/) &&
                    c.length > 2 &&
                    c.length < 30
                );

                if (meaningfulClass) {
                    selector += '.' + meaningfulClass;
                    path.unshift(selector);
                    depth++;
                    el = el.parentNode;
                    continue;
                }
            }

            const dataAttrs = ['data-testid', 'data-test-id', 'data-test', 'data-qa', 'role', 'aria-label'];
            let foundAttr = false;
            for (const attr of dataAttrs) {
                if (el.hasAttribute(attr)) {
                    const val = el.getAttribute(attr);
                    selector += `[${attr}="${val}"]`;
                    path.unshift(selector);
                    foundAttr = true;
                    break;
                }
            }

            if (foundAttr) {
                depth++;
                el = el.parentNode;
                continue;
            }

            let sib = el, nth = 1;
            while (sib = sib.previousElementSibling) {
                if (sib.nodeName.toLowerCase() == el.nodeName.toLowerCase())
                    nth++;
            }
            if (nth != 1)
                selector += ':nth-of-type(' + nth + ')';

            path.unshift(selector);
            depth++;
            el = el.parentNode;
        }
        return path.join(' > ');
    }

    function getXPath(element) {
        if (element.id && element.id.trim() !== '') {
            return `//*[@id="${element.id}"]`;
        }

        const tagName = element.tagName.toLowerCase();

        if (['a', 'button', 'span', 'div', 'li', 'td', 'th', 'label', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6'].includes(tagName)) {
            const text = element.textContent.trim();
            if (text && text.length > 0 && text.length < 50 && !text.includes('"') && !text.includes("'")) {
                const textXPath = `//${tagName}[normalize-space()="${text}"]`;
                try {
                    const count = document.evaluate(`count(${textXPath})`, document, null, XPathResult.NUMBER_TYPE, null).numberValue;
                    if (count === 1) {
                        return textXPath;
                    }
                } catch (e) { }
            }
        }

        const meaningfulAttrs = ['name', 'type', 'value', 'title', 'alt', 'href', 'src'];
        for (const attr of meaningfulAttrs) {
            if (element.hasAttribute(attr)) {
                const value = element.getAttribute(attr);
                if (value && value.length > 0 && value.length < 100 && !value.includes('"')) {
                    const attrXPath = `//${tagName}[@${attr}="${value}"]`;
                    try {
                        const count = document.evaluate(`count(${attrXPath})`, document, null, XPathResult.NUMBER_TYPE, null).numberValue;
                        if (count === 1) {
                            return attrXPath;
                        }
                    } catch (e) { }
                }
            }
        }

        if (element.className && typeof element.className === 'string') {
            const classes = element.className.split(/\s+/).filter(c => c);
            const meaningfulClass = classes.find(c =>
                !c.startsWith('atm_') &&
                !c.startsWith('css-') &&
                !c.startsWith('style-') &&
                !c.match(/^[a-z]\d+$/) &&
                c.length > 2 &&
                c.length < 30
            );

            if (meaningfulClass) {
                const classXPath = `//${tagName}[contains(@class, "${meaningfulClass}")]`;
                try {
                    const count = document.evaluate(`count(${classXPath})`, document, null, XPathResult.NUMBER_TYPE, null).numberValue;
                    if (count === 1) {
                        return classXPath;
                    }
                } catch (e) { }
            }
        }

        const parent = element.parentElement;
        if (parent) {
            const parentText = parent.textContent.trim();
            if (parentText && parentText.length > 0 && parentText.length < 100 && !parentText.includes('"')) {
                const siblings = Array.from(parent.children).filter(el => el.tagName === element.tagName);
                if (siblings.length > 1) {
                    const index = siblings.indexOf(element);
                    if (index >= 0) {
                        const xpath = `//*[contains(text(), "${parentText.substring(0, 30)}")]//${tagName}[${index + 1}]`;
                        try {
                            const count = document.evaluate(`count(${xpath})`, document, null, XPathResult.NUMBER_TYPE, null).numberValue;
                            if (count === 1) {
                                return xpath;
                            }
                        } catch (e) { }
                    }
                }
            }
        }

        if (element.parentElement) {
            const parentTag = element.parentElement.tagName.toLowerCase();
            const sameTagSiblings = Array.from(element.parentElement.children).filter(
                el => el.tagName === element.tagName
            );
            const position = sameTagSiblings.indexOf(element) + 1;

            if (element.parentElement.id) {
                return `//*[@id="${element.parentElement.id}"]/${tagName}[${position}]`;
            }

            return `//${parentTag}/${tagName}[${position}]`;
        }

        return `//${tagName}`;
    }

    function isUnique(selector) {
        try {
            return document.querySelectorAll(selector).length === 1;
        } catch (e) { return false; }
    }

    function getBestSelector(el) {
        if (!el || !el.tagName) return null;

        try {
            const dataAttrs = ['data-testid', 'data-test-id', 'data-test', 'data-qa'];
            for (const attr of dataAttrs) {
                if (el.hasAttribute(attr)) {
                    const val = el.getAttribute(attr);
                    const sel = `[${attr}="${val}"]`;
                    if (isUnique(sel)) return { type: 'CssSelector', value: sel };
                }
            }

            if (el.id && isUnique('#' + CSS.escape(el.id))) {
                return { type: 'Id', value: el.id };
            }

            if (el.name && isUnique(`[name="${el.name}"]`)) {
                return { type: 'Name', value: el.name };
            }

            if (['BUTTON', 'A', 'LABEL', 'SPAN', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6'].includes(el.tagName)) {
                const text = el.innerText.trim();
                if (text && text.length < 50 && !text.includes('"') && !text.includes("'")) {
                    const xpath = `//${el.tagName.toLowerCase()}[normalize-space()="${text}"]`;
                    if (document.evaluate(`count(${xpath})`, document, null, XPathResult.NUMBER_TYPE, null).numberValue === 1) {
                        return { type: 'XPath', value: xpath };
                    }
                }
            }

            const ariaLabel = el.getAttribute('aria-label');
            if (ariaLabel && isUnique(`[aria-label="${ariaLabel}"]`)) {
                return { type: 'CssSelector', value: `[aria-label="${ariaLabel}"]` };
            }

            const role = el.getAttribute('role');
            if (role && ariaLabel) {
                const roleAriaSel = `[role="${role}"][aria-label="${ariaLabel}"]`;
                if (isUnique(roleAriaSel)) {
                    return { type: 'CssSelector', value: roleAriaSel };
                }
            }

            const placeholder = el.getAttribute('placeholder');
            if (placeholder && isUnique(`[placeholder="${placeholder}"]`)) {
                return { type: 'CssSelector', value: `[placeholder="${placeholder}"]` };
            }

            if (el.className && typeof el.className === 'string' && el.className.trim().length > 0) {
                const classes = el.className.split(/\s+/).filter(c => c);
                const meaningfulClass = classes.find(c =>
                    !c.startsWith('atm_') &&
                    !c.startsWith('css-') &&
                    !c.startsWith('style-') &&
                    !c.match(/^[a-z]\d+$/) &&
                    c.length > 2 &&
                    c.length < 30
                );

                if (meaningfulClass) {
                    const classSel = '.' + meaningfulClass;
                    if (isUnique(classSel)) return { type: 'CssSelector', value: classSel };
                }

                if (meaningfulClass) {
                    const tagClassSel = el.tagName.toLowerCase() + '.' + meaningfulClass;
                    if (isUnique(tagClassSel)) return { type: 'CssSelector', value: tagClassSel };
                }
            }

            const cssPath = getCssPath(el);
            if (cssPath && isUnique(cssPath)) {
                return { type: 'CssSelector', value: cssPath };
            }

            const absXPath = getXPath(el);
            if (absXPath) {
                return { type: 'XPath', value: absXPath };
            }

            return { type: 'TagName', value: el.tagName.toLowerCase() };
        }
        catch (e) {
            console.error('Error getting selector:', e);
            return { type: 'TagName', value: el.tagName ? el.tagName.toLowerCase() : 'unknown' };
        }
    }

    function findInputElement(el) {
        if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT') {
            return el;
        }
        const inputs = el.querySelectorAll('input, textarea, select');
        if (inputs && inputs.length > 0) return inputs[0];
        if (el.shadowRoot) {
            const shadowInputs = el.shadowRoot.querySelectorAll('input, textarea, select');
            if (shadowInputs && shadowInputs.length > 0) return shadowInputs[0];
        }
        return null;
    }

    document.addEventListener('input', function (e) {
        const target = e.target;
        if (!target || !target.tagName) return;
        inputValues.set(target, target.value);
        let parent = target.parentElement;
        while (parent && parent !== document.body) {
            inputValues.set(parent, target.value);
            parent = parent.parentElement;
        }
    }, true);

    function sendMessageSafe(message) {
        if (chrome.runtime?.id) {
            try {
                chrome.runtime.sendMessage(message).catch(err => {
                    console.log('Reqnroll Recorder: Could not send message (extension might be reloaded)', err);
                });
            } catch (e) {
                console.log('Reqnroll Recorder: Extension context invalidated');
            }
        }
    }

    function handleEvent(e) {
        if (!isRecording) return;

        const target = e.target;
        if (!target || !target.tagName || target.tagName === 'HTML' || target.tagName === 'BODY') {
            return;
        }

        try {
            const inputEl = findInputElement(target);
            const value = inputEl ? inputEl.value : (inputValues.get(target) || target.value);
            const selector = getBestSelector(target);

            if (!selector) return;

            let action = {
                type: e.type,
                selector: selector.type,
                selectorValue: selector.value,
                value: value,
                key: e.key,
                tagName: target.tagName,
                elementType: target.type,
                url: window.location.href
            };

            if (e.type === 'blur') {
                if (['INPUT', 'TEXTAREA'].includes(target.tagName)) {
                    const currentValue = target.value;
                    const lastValue = lastRecordedValues.get(target);

                    if (currentValue && currentValue !== lastValue) {
                        action.type = 'type';
                        action.value = currentValue;
                        lastRecordedValues.set(target, currentValue);
                        sendMessageSafe({ command: 'recordAction', action: action });
                    }
                }
            }
            else if (e.type === 'change') {
                if (target.tagName === 'SELECT') {
                    action.type = 'type';
                    action.value = target.value;
                    lastRecordedValues.set(target, target.value);
                    sendMessageSafe({ command: 'recordAction', action: action });
                }
            }
            else if (e.type === 'click') {
                const isSubmitClick = ['BUTTON', 'A', 'INPUT'].includes(target.tagName) &&
                    target.type !== 'text' &&
                    target.type !== 'search';

                if (isSubmitClick) {
                    console.log('Reqnroll Recorder: Checking for uncommitted input before click on', target.tagName);

                    // Check the currently focused element
                    const activeElement = document.activeElement;
                    if (activeElement &&
                        (activeElement.tagName === 'INPUT' || activeElement.tagName === 'TEXTAREA')) {

                        const currentValue = activeElement.value;
                        const lastValue = lastRecordedValues.get(activeElement);

                        console.log('Reqnroll Recorder: Active input found:', {
                            value: currentValue,
                            lastRecorded: lastValue,
                            willRecord: currentValue && currentValue !== lastValue
                        });

                        if (currentValue && currentValue !== lastValue) {
                            const inputSelector = getBestSelector(activeElement);
                            if (inputSelector) {
                                console.log('Reqnroll Recorder: Recording input value before click:', currentValue);
                                sendMessageSafe({
                                    command: 'recordAction',
                                    action: {
                                        type: 'type',
                                        selector: inputSelector.type,
                                        selectorValue: inputSelector.value,
                                        value: currentValue,
                                        tagName: activeElement.tagName,
                                        elementType: activeElement.type,
                                        url: window.location.href
                                    }
                                });
                                lastRecordedValues.set(activeElement, currentValue);
                            }
                        }
                    }

                    const allInputs = document.querySelectorAll('input[type="text"], input[type="search"], input[type="email"], input[type="password"], textarea');
                    allInputs.forEach(input => {
                        const currentValue = input.value;
                        const lastValue = lastRecordedValues.get(input);

                        if (currentValue && currentValue !== lastValue && input !== activeElement) {
                            const inputSelector = getBestSelector(input);
                            if (inputSelector) {
                                console.log('Reqnroll Recorder: Found uncommitted input field:', currentValue);
                                sendMessageSafe({
                                    command: 'recordAction',
                                    action: {
                                        type: 'type',
                                        selector: inputSelector.type,
                                        selectorValue: inputSelector.value,
                                        value: currentValue,
                                        tagName: input.tagName,
                                        elementType: input.type,
                                        url: window.location.href
                                    }
                                });
                                lastRecordedValues.set(input, currentValue);
                            }
                        }
                    });
                }

                action.value = inputValues.get(target) || null;
                sendMessageSafe({ command: 'recordAction', action: action });
            }
            else if (e.type === 'keydown' && e.key === 'Enter') {
                action.type = 'enterkey';
                action.value = inputValues.get(target) || (inputEl ? inputEl.value : target.value);
                lastRecordedValues.set(target, action.value);
                sendMessageSafe({ command: 'recordAction', action: action });
            }
        }
        catch (error) {
            console.error('Event handling error:', error);
        }
    }

    function attachListeners() {
        document.addEventListener('click', handleEvent, { capture: true, passive: true });
        document.addEventListener('keydown', handleEvent, { capture: true, passive: true });
        document.addEventListener('blur', handleEvent, { capture: true, passive: true });
        document.addEventListener('change', handleEvent, { capture: true, passive: true });
        console.log('Reqnroll Recorder: Listeners attached');

        sendMessageSafe({
            command: 'recordAction',
            action: { type: 'navigate', value: window.location.href }
        });
    }

    function removeListeners() {
        document.removeEventListener('click', handleEvent, { capture: true });
        document.removeEventListener('keydown', handleEvent, { capture: true });
        document.removeEventListener('blur', handleEvent, { capture: true });
        document.removeEventListener('change', handleEvent, { capture: true });
        console.log('Reqnroll Recorder: Listeners removed');
    }

})();

document.addEventListener('DOMContentLoaded', () => {
    const startBtn = document.getElementById('startBtn');
    const stopBtn = document.getElementById('stopBtn');
    const featureNameInput = document.getElementById('featureName');
    const statusDiv = document.getElementById('status');
    const actionCountSpan = document.getElementById('actionCount');
    const actionCountContainer = document.getElementById('actionCountContainer');

    chrome.storage.local.get(['isRecording', 'featureName', 'actionCount'], (result) => {
        if (result.isRecording) {
            setRecordingState(true);
            if (result.featureName) featureNameInput.value = result.featureName;
            if (result.actionCount) actionCountSpan.textContent = result.actionCount;
        } else {
            setRecordingState(false);
        }
    });

    startBtn.addEventListener('click', () => {
        const featureName = featureNameInput.value.trim() || 'MyFeature';

        chrome.runtime.sendMessage({ command: 'startRecording', featureName: featureName }, (response) => {
            if (response && response.status === 'started') {
                setRecordingState(true);
                window.close();
            }
        });
    });

    stopBtn.addEventListener('click', () => {
        chrome.runtime.sendMessage({ command: 'stopRecording' }, (response) => {
            if (response && response.status === 'stopped') {
                setRecordingState(false);
                statusDiv.textContent = 'Files generated!';
            }
        });
    });

    function setRecordingState(isRecording) {
        if (isRecording) {
            startBtn.classList.add('hidden');
            stopBtn.classList.remove('hidden');
            featureNameInput.disabled = true;
            actionCountContainer.classList.remove('hidden');
            statusDiv.textContent = 'Recording in progress...';
        } else {
            startBtn.classList.remove('hidden');
            stopBtn.classList.add('hidden');
            featureNameInput.disabled = false;
            actionCountContainer.classList.add('hidden');
            statusDiv.textContent = 'Ready to record';
        }
    }

    chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
        if (request.type === 'actionRecorded') {
            actionCountSpan.textContent = request.count;
        }
    });
});

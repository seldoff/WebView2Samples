console.log('Loaded');

function postNativeMessage(message) {
    console.log(message);
    postNativeMessageBinding(JSON.stringify(message));
}

function error(e) {
    try {
        postNativeMessage({state: 'error', error: JSON.stringify(e)});
    } catch {
        postNativeMessage({state: 'error'});
    }
}

// Check if the last extension API call was successful
function checkSuccess() {
    if (chrome.runtime.lastError) {
        error('Extension API error: ' + chrome.runtime.lastError.message);
        return false;
    }
    return true;
}

burn = function(sinceStr, excludeOrigins) {
    try {
        postNativeMessage({state: 'burning'});

        let since = undefined;
        if (sinceStr !== null) {
            since = new Date(sinceStr).getTime();
        }

        // These data types support excludeOrigins argument
        chrome.browsingData.remove(
            { since, excludeOrigins },
            {
                "cacheStorage": true,
                "cookies": true,
                "fileSystems": true,
                "indexedDB": true,
                "localStorage": true,
                "serviceWorkers": true,
                "webSQL": true
            },
            () => {
                try {
                    if (!checkSuccess()) {
                        return;
                    }
                    postNativeMessage({state: 'burned_excludeOrigins'});

                    // These data types do not support excludeOrigins argument
                    chrome.browsingData.remove(
                        { since },
                        {
                            "appcache": true,
                            "cache": true,
                            "downloads": true,
                            "formData": true,
                            "history": true,
                            "passwords": true
                        },
                        () => {
                            if (checkSuccess()) {
                                postNativeMessage({state: 'burned'});
                            }
                        }
                    );
                } catch (e) {
                    error(e);
                }
            }
        );
    } catch (e) {
        error(e);
    }
}
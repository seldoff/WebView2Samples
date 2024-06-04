### Conditions for crash
* An item with icon is added to the context menu when the context menu is triggered for the first time.
* A different item with icon is added to the context menu when the context menu is triggered for the second time.
* Context menu is triggered for a misspelled word.

See `MainWindow.WebView_ContextMenuRequested` method for the code that reproduces the crash.

Crash does not occur if no icons are used. See `Consts.UseIcon` constant.

### Steps to reproduce the crash
1. Start the app, it navigates to https://www.w3schools.com/tags/tryit.asp?filename=tryhtml_textarea.
2. Replace text in text box in the right panel with a misspelled word: "tesst".
   <img src="https://github.com/seldoff/WebView2Samples/blob/menu-crash/SampleApps/WebView2WpfBrowser/crash-screenshots/Screenshot%202024-06-04%20at%2018.18.42.png?raw=true">
3. Right-click on the misspelled word. The context menu with a spelling suggestion is displayed.
   <img src="https://github.com/seldoff/WebView2Samples/blob/menu-crash/SampleApps/WebView2WpfBrowser/crash-screenshots/Screenshot%202024-06-04%20at%2019.02.10.png?raw=true">
4. Right-click on the misspelled word again. The context menu with three empty suggestions is displayed.
<img src="https://github.com/seldoff/WebView2Samples/blob/menu-crash/SampleApps/WebView2WpfBrowser/crash-screenshots/Screenshot%202024-06-04%20at%2019.02.21.png?raw=true">
5. Click on the third empty suggestion. The browser process crashes.
<img src="https://github.com/seldoff/WebView2Samples/blob/menu-crash/SampleApps/WebView2WpfBrowser/crash-screenshots/Screenshot%202024-06-04%20at%2018.19.13.png?raw=true">

### Failed workarounds
* Crash occurs with icons loaded from assembly resources or from files. See `Consts.UseIconFromResources` constant.
* Crash occurs with icons copied to a dedicated MemoryStream. See `Consts.CopyIcon` constant.
// The only bridge between the page and the main process. contextIsolation is
// on, nodeIntegration off, and the renderer is sandboxed, so the page sees
// exactly this object and nothing else of Node or Electron.
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('spike', {
  // page -> main: {cmd, value|on}, the same shape spike.html sent to WinForms
  postMessage: (msg) => ipcRenderer.send('spike:cmd', msg),
  // main -> page: {type:'state', ...}
  onMessage: (cb) => ipcRenderer.on('spike:state', (_e, data) => cb(data)),
  // right-click anywhere on the page -> the native menu
  contextMenu: () => ipcRenderer.send('spike:menu'),
});

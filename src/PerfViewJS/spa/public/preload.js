const { contextBridge, ipcRenderer } = require("electron");

const validChannels = ["toMain", "fromMain"];
//https://github.com/electron/electron/issues/21437#issuecomment-802288574
contextBridge.exposeInMainWorld("electronBridge", {
  send: (channel, filePath) => {
    if (validChannels.includes(channel)) {
      ipcRenderer.send(channel, filePath);
    }
  },
  receive: (channel, func) => {
    if (validChannels.includes(channel)) {
      const subscription = (event, action) => func(action);
      ipcRenderer.on(channel, subscription);
      return () => {
        ipcRenderer.removeListener(channel, subscription);
      };
    }
  },
});
